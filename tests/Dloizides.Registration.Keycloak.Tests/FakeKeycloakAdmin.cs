using System.Net;
using Identity.Keycloak.Admin;

namespace Dloizides.Registration.Keycloak.Tests;

internal sealed class FakeKeycloakAdmin : IKeycloakAdminClient
{
    public const string DefaultRealm = "shop";

    private readonly List<(string Realm, KeycloakUserRepresentation User)> _users = [];

    public List<string> Realms { get; } = [];

    public IEnumerable<KeycloakUserRepresentation> Users => _users.Select(entry => entry.User);

    public List<KeycloakUserQuery> Queries { get; } = [];

    public List<string> Deleted { get; } = [];

    public List<string> LoggedOut { get; } = [];

    public List<string> Roles { get; } = [];

    public List<(string Realm, string UserId, KeycloakCredentialRepresentation Credential)> PasswordResets { get; } = [];

    public KeycloakAdminOutcome ResetOutcome { get; set; } = KeycloakAdminOutcome.Success;

    public bool ServerIgnoresAttributeQuery { get; set; }

    public Action? OnCreated { get; set; }

    public KeycloakAdminOutcome CreateOutcome { get; set; } = KeycloakAdminOutcome.Success;

    public KeycloakAdminOutcome LookupOutcome { get; set; } = KeycloakAdminOutcome.Success;

    public KeycloakAdminOutcome RoleOutcome { get; set; } = KeycloakAdminOutcome.Success;

    public KeycloakAdminOutcome DeleteOutcome { get; set; } = KeycloakAdminOutcome.Success;

    public void Add(KeycloakUserRepresentation user, string realm = DefaultRealm) => _users.Add((realm, user));

    public Task<KeycloakAdminResult<IReadOnlyList<KeycloakUserRepresentation>>> ListUsersAsync(string realm, KeycloakUserQuery query, CancellationToken cancellationToken = default)
    {
        Realms.Add(realm);
        Queries.Add(query);
        if (LookupOutcome != KeycloakAdminOutcome.Success)
            return Task.FromResult(KeycloakAdminResult<IReadOnlyList<KeycloakUserRepresentation>>.Failure(LookupOutcome, HttpStatusCode.InternalServerError, "boom"));

        var matching = InRealm(realm).Where(user => MatchesAttribute(user, query.AttributeQuery) && MatchesUsername(user, query.Username) && MatchesEmail(user, query.Email));
        var page = matching.Skip(query.First ?? 0).Take(query.Max ?? int.MaxValue).ToList();
        return Task.FromResult(KeycloakAdminResult<IReadOnlyList<KeycloakUserRepresentation>>.Success(page));
    }

    public Task<KeycloakAdminResult<KeycloakUserRepresentation>> GetUserAsync(string realm, string userId, CancellationToken cancellationToken = default)
    {
        Realms.Add(realm);
        if (LookupOutcome != KeycloakAdminOutcome.Success)
            return Task.FromResult(KeycloakAdminResult<KeycloakUserRepresentation>.Failure(LookupOutcome, HttpStatusCode.InternalServerError, "boom"));
        var user = InRealm(realm).FirstOrDefault(u => string.Equals(u.Id, userId, StringComparison.Ordinal));
        return Task.FromResult(user is null
            ? KeycloakAdminResult<KeycloakUserRepresentation>.Failure(KeycloakAdminOutcome.NotFound, HttpStatusCode.NotFound, "absent")
            : KeycloakAdminResult<KeycloakUserRepresentation>.Success(user));
    }

    public Task<KeycloakAdminResult<string>> CreateUserAsync(string realm, KeycloakUserRepresentation user, CancellationToken cancellationToken = default)
    {
        Realms.Add(realm);
        if (CreateOutcome != KeycloakAdminOutcome.Success)
            return Task.FromResult(KeycloakAdminResult<string>.Failure(CreateOutcome, HttpStatusCode.BadRequest, "boom"));
        user.Id = "new-user";
        _users.Add((realm, user));
        OnCreated?.Invoke();
        return Task.FromResult(KeycloakAdminResult<string>.Success("new-user"));
    }

    public Task<KeycloakAdminResult> UpdateUserAsync(string realm, string userId, KeycloakUserRepresentation user, CancellationToken cancellationToken = default)
    {
        Realms.Add(realm);
        var existing = InRealm(realm).FirstOrDefault(u => string.Equals(u.Id, userId, StringComparison.Ordinal));
        if (existing is null)
            return Task.FromResult(KeycloakAdminResult.Failure(KeycloakAdminOutcome.NotFound, HttpStatusCode.NotFound, "absent"));
        existing.Enabled = user.Enabled;
        return Task.FromResult(KeycloakAdminResult.Success());
    }

    public Task<KeycloakAdminResult> DeleteUserAsync(string realm, string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Realms.Add(realm);
        if (DeleteOutcome != KeycloakAdminOutcome.Success)
            return Task.FromResult(KeycloakAdminResult.Failure(DeleteOutcome, HttpStatusCode.InternalServerError, "boom"));
        Deleted.Add(userId);
        _users.RemoveAll(entry => string.Equals(entry.Realm, realm, StringComparison.Ordinal) && string.Equals(entry.User.Id, userId, StringComparison.Ordinal));
        return Task.FromResult(KeycloakAdminResult.Success());
    }

    public Task<KeycloakAdminResult> LogoutUserAsync(string realm, string userId, CancellationToken cancellationToken = default)
    {
        Realms.Add(realm);
        LoggedOut.Add(userId);
        return Task.FromResult(KeycloakAdminResult.Success());
    }

    public Task<KeycloakAdminResult> AddUserRealmRolesAsync(string realm, string userId, IEnumerable<string> roleNames, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Realms.Add(realm);
        if (RoleOutcome != KeycloakAdminOutcome.Success)
            return Task.FromResult(KeycloakAdminResult.Failure(RoleOutcome, HttpStatusCode.InternalServerError, "boom"));
        Roles.AddRange(roleNames);
        return Task.FromResult(KeycloakAdminResult.Success());
    }

    public Task<string> GetAdminTokenAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string realm, string relativePath, HttpContent? content = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<KeycloakAdminResult> ResetPasswordAsync(string realm, string userId, KeycloakCredentialRepresentation credential, CancellationToken cancellationToken = default)
    {
        Realms.Add(realm);
        if (ResetOutcome != KeycloakAdminOutcome.Success)
            return Task.FromResult(KeycloakAdminResult.Failure(ResetOutcome, HttpStatusCode.InternalServerError, "boom"));
        PasswordResets.Add((realm, userId, credential));
        return Task.FromResult(KeycloakAdminResult.Success());
    }

    public Task<KeycloakAdminResult<IReadOnlyList<KeycloakRoleRepresentation>>> ListRealmRolesAsync(string realm, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<KeycloakAdminResult<IReadOnlyList<KeycloakRoleRepresentation>>> GetUserRealmRolesAsync(string realm, string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<KeycloakAdminResult> RemoveUserRealmRolesAsync(string realm, string userId, IEnumerable<string> roleNames, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    private IEnumerable<KeycloakUserRepresentation> InRealm(string realm) =>
        _users.Where(entry => string.Equals(entry.Realm, realm, StringComparison.Ordinal)).Select(entry => entry.User);

    private bool MatchesAttribute(KeycloakUserRepresentation user, string? attributeQuery)
    {
        if (attributeQuery is null || ServerIgnoresAttributeQuery)
            return true;
        var parts = attributeQuery.Split(':', 2);
        return string.Equals(user.FirstAttribute(parts[0]), parts[1], StringComparison.Ordinal);
    }

    private static bool MatchesEmail(KeycloakUserRepresentation user, string? email) =>
        email is null || string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesUsername(KeycloakUserRepresentation user, string? username) =>
        username is null || string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase);
}
