using Dloizides.Registration.Abstractions;
using Dloizides.Registration.Model;
using Identity.Keycloak.Admin;
using Microsoft.Extensions.Options;

namespace Dloizides.Registration.Keycloak;

/// <summary>Every call uses the realm it is given. Only a 404 reads as absent; any other failure throws.</summary>
public sealed class KeycloakIdentityProvisioner(IKeycloakAdminClient admin, IOptions<KeycloakRegistrationOptions> options) : IIdentityProvisioner
{
    private const int PageSize = 100;
    private const int UsernameLookupLimit = 2;
    private const string InvalidUserCode = "INVALID_USER";

    public async Task<LoginCreateResult> CreateAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rolesForRealm = options.Value.RolesForRealm
            ?? throw new InvalidOperationException("KeycloakRegistrationOptions.RolesForRealm is not set.");
        var roles = rolesForRealm(request.Realm);

        var created = await admin.CreateUserAsync(request.Realm, ToRepresentation(request), cancellationToken);
        if (created.Outcome == KeycloakAdminOutcome.Conflict)
            return LoginCreateResult.Duplicate;
        if (created.Outcome == KeycloakAdminOutcome.BadRequest)
            return LoginCreateResult.Invalid(InvalidUserCode);
        EnsureSuccess(created, "create user", request.Realm);

        if (roles.Count > 0)
            await AssignRolesOrRemoveUserAsync(request.Realm, created.Value!, roles);
        return LoginCreateResult.Created(created.Value!);
    }

    public async Task<LoginInfo?> FindByUsernameAsync(string realm, string username, CancellationToken cancellationToken)
    {
        var query = new KeycloakUserQuery { Username = username, Exact = true, Max = UsernameLookupLimit, BriefRepresentation = false };
        var found = await admin.ListUsersAsync(realm, query, cancellationToken);
        EnsureSuccess(found, "find user", realm);

        var user = found.Value!.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        return user is null ? null : ToInfo(user);
    }

    public async Task<LoginInfo?> FindByEmailAsync(string realm, string email, CancellationToken cancellationToken)
    {
        var query = new KeycloakUserQuery { Email = email, Exact = true, Max = UsernameLookupLimit, BriefRepresentation = false };
        var found = await admin.ListUsersAsync(realm, query, cancellationToken);
        EnsureSuccess(found, "find user by email", realm);

        var user = found.Value!.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
        return user is null ? null : ToInfo(user);
    }

    public async Task<LoginInfo?> FindByIdAsync(string realm, string userId, CancellationToken cancellationToken)
    {
        var found = await admin.GetUserAsync(realm, userId, cancellationToken);
        if (found.Outcome == KeycloakAdminOutcome.NotFound)
            return null;
        EnsureSuccess(found, "get user", realm);
        return ToInfo(found.Value!);
    }

    public async Task<bool> AnyUsersAsync(string realm, string tenantId, bool enabledOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        if (tenantId.Length == 0 || tenantId.Any(char.IsWhiteSpace))
            throw new ArgumentException("A tenant id must be non-empty with no whitespace.", nameof(tenantId));

        var attribute = options.Value.TenantAttribute;
        var first = 0;
        while (true)
        {
            var query = new KeycloakUserQuery { AttributeQuery = $"{attribute}:{tenantId}", First = first, Max = PageSize, BriefRepresentation = false };
            var page = await admin.ListUsersAsync(realm, query, cancellationToken);
            EnsureSuccess(page, "list tenant users", realm);

            if (page.Value!.Any(user => IsCountedUser(user, attribute, tenantId, enabledOnly)))
                return true;
            if (page.Value!.Count < PageSize)
                return false;
            first += PageSize;
        }
    }

    public async Task SetEnabledAsync(string realm, string userId, bool enabled, CancellationToken cancellationToken)
    {
        var found = await admin.GetUserAsync(realm, userId, cancellationToken);
        if (found.Outcome == KeycloakAdminOutcome.NotFound)
            return;
        EnsureSuccess(found, "get user", realm);

        var user = found.Value!;
        user.Enabled = enabled;
        EnsureSuccess(await admin.UpdateUserAsync(realm, userId, user, cancellationToken), "update user", realm);
        if (!enabled)
            EnsureSuccess(await admin.LogoutUserAsync(realm, userId, cancellationToken), "log out user", realm);
    }

    public async Task DeleteAsync(string realm, string userId, CancellationToken cancellationToken)
    {
        var deleted = await admin.DeleteUserAsync(realm, userId, cancellationToken);
        if (deleted.Outcome != KeycloakAdminOutcome.NotFound)
            EnsureSuccess(deleted, "delete user", realm);
    }

    public async Task SetPasswordAsync(string realm, string userId, string newPassword, CancellationToken cancellationToken)
    {
        var credential = KeycloakCredentialRepresentation.Password(newPassword, temporary: false);
        EnsureSuccess(await admin.ResetPasswordAsync(realm, userId, credential, cancellationToken), "set password", realm);
        EnsureSuccess(await admin.LogoutUserAsync(realm, userId, CancellationToken.None), "log out user", realm);
    }

    private static bool IsCountedUser(KeycloakUserRepresentation user, string attribute, string tenantId, bool enabledOnly) =>
        string.Equals(user.FirstAttribute(attribute), tenantId, StringComparison.Ordinal) && (!enabledOnly || user.Enabled == true);

    private static void EnsureSuccess(KeycloakAdminResult result, string operation, string realm)
    {
        if (!result.IsSuccess)
            throw new IdentityProvisioningException($"Keycloak {operation} failed in realm {realm}: {result.Outcome} {result.StatusCode}");
    }

    private async Task AssignRolesOrRemoveUserAsync(string realm, string userId, IReadOnlyList<string> roles)
    {
        var assigned = await admin.AddUserRealmRolesAsync(realm, userId, roles, CancellationToken.None);
        if (assigned.IsSuccess)
            return;

        await admin.DeleteUserAsync(realm, userId, CancellationToken.None);
        EnsureSuccess(assigned, "assign roles", realm);
    }

    private KeycloakUserRepresentation ToRepresentation(LoginRequest request) => new()
    {
        Username = request.Username,
        Email = request.Email,
        FirstName = request.FirstName,
        LastName = request.LastName,
        Enabled = true,
        EmailVerified = false,
        Attributes = new Dictionary<string, List<string>>(StringComparer.Ordinal) { [options.Value.TenantAttribute] = [request.TenantId] },
        Credentials = [KeycloakCredentialRepresentation.Password(request.Password, temporary: false)],
    };

    private LoginInfo ToInfo(KeycloakUserRepresentation user) =>
        new(user.Id ?? string.Empty, user.FirstAttribute(options.Value.TenantAttribute), user.Email, user.EmailVerified == true, user.Enabled == true);
}
