using System.Globalization;
using Dloizides.Registration.Model;
using Identity.Keycloak.Admin;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Dloizides.Registration.Keycloak.Tests;

public sealed class KeycloakIdentityProvisionerTests
{
    private const string Phrase = "Blue7HorseTrail";

    private readonly FakeKeycloakAdmin _admin = new();

    [Fact]
    public async Task CreateAsync_WithNewUser_SetsTenantRolesAndRealm()
    {
        var provisioner = Provisioner(["tenant-owner", "user"]);

        var result = await provisioner.CreateAsync(Login(realm: "forms", tenantId: "t-1"), CancellationToken.None);

        result.Outcome.ShouldBe(LoginCreateOutcome.Created);
        result.UserId.ShouldBe("new-user");
        _admin.Realms.ShouldBe(["forms", "forms"]);
        _admin.Users.Single().FirstAttribute("tenantId").ShouldBe("t-1");
        _admin.Users.Single().EmailVerified.ShouldBe(expected: false);
        _admin.Roles.ShouldBe(["tenant-owner", "user"]);
    }

    [Fact]
    public async Task CreateAsync_WhenConflict_ReturnsDuplicate()
    {
        _admin.CreateOutcome = KeycloakAdminOutcome.Conflict;

        var result = await Provisioner().CreateAsync(Login(), CancellationToken.None);

        result.Outcome.ShouldBe(LoginCreateOutcome.Duplicate);
    }

    [Fact]
    public async Task CreateAsync_WhenBadRequest_ReturnsInvalid()
    {
        _admin.CreateOutcome = KeycloakAdminOutcome.BadRequest;

        var result = await Provisioner().CreateAsync(Login(), CancellationToken.None);

        result.Outcome.ShouldBe(LoginCreateOutcome.Invalid);
    }

    [Fact]
    public async Task CreateAsync_WhenUpstreamFails_Throws()
    {
        _admin.CreateOutcome = KeycloakAdminOutcome.UpstreamError;

        await Should.ThrowAsync<IdentityProvisioningException>(() => Provisioner().CreateAsync(Login(), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_WhenRoleAssignmentFails_DeletesUserAndThrows()
    {
        _admin.RoleOutcome = KeycloakAdminOutcome.UpstreamError;

        await Should.ThrowAsync<IdentityProvisioningException>(() => Provisioner(["user"]).CreateAsync(Login(), CancellationToken.None));

        _admin.Users.ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateAsync_WhenCallerCancelsAfterUserCreated_StillAssignsRoles()
    {
        using var caller = new CancellationTokenSource();
        _admin.OnCreated = caller.Cancel;

        var result = await Provisioner(["user"]).CreateAsync(Login(), caller.Token);

        result.Outcome.ShouldBe(LoginCreateOutcome.Created);
        _admin.Roles.ShouldBe(["user"]);
    }

    [Fact]
    public async Task CreateAsync_WithoutRolesConfigured_Throws()
    {
        var provisioner = new KeycloakIdentityProvisioner(_admin, Options.Create(new KeycloakRegistrationOptions()));

        await Should.ThrowAsync<InvalidOperationException>(() => provisioner.CreateAsync(Login(), CancellationToken.None));

        _admin.Users.ShouldBeEmpty();
    }

    [Fact]
    public async Task FindByIdAsync_WithRealm_QueriesThatRealmOnly()
    {
        _admin.Add(User("u-1", "t-1", email: "ana@example.test"), realm: "forms");

        var absent = await Provisioner().FindByIdAsync("shop", "u-1", CancellationToken.None);
        var found = await Provisioner().FindByIdAsync("forms", "u-1", CancellationToken.None);

        absent.ShouldBeNull();
        found.ShouldNotBeNull();
        found.TenantId.ShouldBe("t-1");
        found.Email.ShouldBe("ana@example.test");
        _admin.Realms.ShouldBe(["shop", "forms"]);
    }

    [Fact]
    public async Task FindByIdAsync_WhenNotFound_ReturnsNull()
    {
        var found = await Provisioner().FindByIdAsync("shop", "missing", CancellationToken.None);

        found.ShouldBeNull();
    }

    [Theory]
    [InlineData(KeycloakAdminOutcome.UpstreamError)]
    [InlineData(KeycloakAdminOutcome.AccessDenied)]
    public async Task FindByIdAsync_WhenLookupFails_ThrowsInsteadOfAbsent(KeycloakAdminOutcome outcome)
    {
        _admin.LookupOutcome = outcome;

        await Should.ThrowAsync<IdentityProvisioningException>(() => Provisioner().FindByIdAsync("shop", "u-1", CancellationToken.None));
    }

    [Fact]
    public async Task FindByUsernameAsync_WithUser_SearchesExactMatch()
    {
        _admin.Add(User("u-1", "t-1"));

        var found = await Provisioner().FindByUsernameAsync("shop", "ana", CancellationToken.None);

        found.ShouldNotBeNull();
        found.UserId.ShouldBe("u-1");
        _admin.Queries.Single().Username.ShouldBe("ana");
        _admin.Queries.Single().Exact.ShouldBe(expected: true);
    }

    [Fact]
    public async Task FindByUsernameAsync_WhenListFails_Throws()
    {
        _admin.LookupOutcome = KeycloakAdminOutcome.UpstreamError;

        await Should.ThrowAsync<IdentityProvisioningException>(() => Provisioner().FindByUsernameAsync("shop", "ana", CancellationToken.None));
    }

    [Fact]
    public async Task AnyUsersAsync_WithUserOfTenant_ReturnsTrue()
    {
        _admin.Add(User("u-1", "t-1"));

        var any = await Provisioner().AnyUsersAsync("shop", "t-1", enabledOnly: false, CancellationToken.None);

        any.ShouldBeTrue();
        _admin.Queries.Single().AttributeQuery.ShouldBe("tenantId:t-1");
    }

    [Fact]
    public async Task AnyUsersAsync_WhenTenantHasUsersOnlyInAnotherRealm_ReturnsFalse()
    {
        _admin.Add(User("u-1", "t-1"), realm: "forms");

        var any = await Provisioner().AnyUsersAsync("shop", "t-1", enabledOnly: false, CancellationToken.None);

        any.ShouldBeFalse();
    }

    [Fact]
    public async Task AnyUsersAsync_WhenServerIgnoresQuery_StillFiltersByTenant()
    {
        _admin.ServerIgnoresAttributeQuery = true;
        _admin.Add(User("u-1", "t-2"));

        var any = await Provisioner().AnyUsersAsync("shop", "t-1", enabledOnly: false, CancellationToken.None);

        any.ShouldBeFalse();
    }

    [Fact]
    public async Task AnyUsersAsync_WithEnabledOnlyAndOnlyDisabled_ReturnsFalse()
    {
        var disabled = User("u-1", "t-1");
        disabled.Enabled = false;
        _admin.Add(disabled);

        var any = await Provisioner().AnyUsersAsync("shop", "t-1", enabledOnly: true, CancellationToken.None);

        any.ShouldBeFalse();
    }

    [Fact]
    public async Task AnyUsersAsync_WithEnabledUserOnSecondPage_ReturnsTrue()
    {
        for (var index = 0; index < 100; index++)
        {
            var disabled = User(string.Create(CultureInfo.InvariantCulture, $"d-{index}"), "t-1");
            disabled.Enabled = false;
            _admin.Add(disabled);
        }

        _admin.Add(User("enabled", "t-1"));

        var any = await Provisioner().AnyUsersAsync("shop", "t-1", enabledOnly: true, CancellationToken.None);

        any.ShouldBeTrue();
        _admin.Queries.Select(query => query.First).ShouldBe([0, 100]);
    }

    [Fact]
    public async Task AnyUsersAsync_WhenListFails_ThrowsSoTenantStays()
    {
        _admin.LookupOutcome = KeycloakAdminOutcome.UpstreamError;

        await Should.ThrowAsync<IdentityProvisioningException>(() => Provisioner().AnyUsersAsync("shop", "t-1", enabledOnly: false, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("t-1 other:x")]
    public async Task AnyUsersAsync_WithUnsafeTenantId_ThrowsWithoutQuerying(string tenantId)
    {
        await Should.ThrowAsync<ArgumentException>(() => Provisioner().AnyUsersAsync("shop", tenantId, enabledOnly: false, CancellationToken.None));

        _admin.Queries.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetEnabledAsync_WhenDisabling_DisablesAndLogsOutInThatRealm()
    {
        _admin.Add(User("u-1", "t-1"), realm: "forms");

        await Provisioner().SetEnabledAsync("forms", "u-1", enabled: false, CancellationToken.None);

        _admin.Users.Single().Enabled.ShouldBe(expected: false);
        _admin.LoggedOut.ShouldBe(["u-1"]);
        _admin.Realms.ShouldAllBe(realm => realm == "forms");
    }

    [Fact]
    public async Task DeleteAsync_WhenAlreadyGone_Succeeds()
    {
        _admin.DeleteOutcome = KeycloakAdminOutcome.NotFound;

        await Provisioner().DeleteAsync("shop", "u-1", CancellationToken.None);

        _admin.Realms.ShouldBe(["shop"]);
    }

    [Fact]
    public async Task DeleteAsync_WhenUpstreamFails_Throws()
    {
        _admin.DeleteOutcome = KeycloakAdminOutcome.UpstreamError;

        await Should.ThrowAsync<IdentityProvisioningException>(() => Provisioner().DeleteAsync("shop", "u-1", CancellationToken.None));
    }

    [Fact]
    public async Task SetPasswordAsync_WithUser_ReplacesPermanentlyAndLogsOutInThatRealm()
    {
        _admin.Add(User("u-1", "t-1"), realm: "forms");

        await Provisioner().SetPasswordAsync("forms", "u-1", Phrase, CancellationToken.None);

        var reset = _admin.PasswordResets.ShouldHaveSingleItem();
        reset.Realm.ShouldBe("forms");
        reset.Credential.Value.ShouldBe(Phrase);
        reset.Credential.Temporary.ShouldBeFalse();
        _admin.LoggedOut.ShouldBe(["u-1"]);
    }

    [Fact]
    public async Task SetPasswordAsync_WhenResetFails_ThrowsWithoutLoggingOut()
    {
        _admin.ResetOutcome = KeycloakAdminOutcome.UpstreamError;

        await Should.ThrowAsync<IdentityProvisioningException>(() => Provisioner().SetPasswordAsync("shop", "u-1", Phrase, CancellationToken.None));

        _admin.LoggedOut.ShouldBeEmpty();
    }

    private static KeycloakUserRepresentation User(string id, string tenantId, string? email = null) => new()
    {
        Id = id,
        Username = "ana",
        Email = email,
        Enabled = true,
        EmailVerified = true,
        Attributes = new Dictionary<string, List<string>>(StringComparer.Ordinal) { ["tenantId"] = [tenantId] },
    };

    private static LoginRequest Login(string realm = "shop", string tenantId = "t-1") => new()
    {
        Realm = realm,
        Username = "ana",
        Email = "ana@example.test",
        Password = Phrase,
        TenantId = tenantId,
    };

    private KeycloakIdentityProvisioner Provisioner(string[]? roles = null) =>
        new(_admin, Options.Create(new KeycloakRegistrationOptions { RolesForRealm = _ => roles ?? [] }));
}
