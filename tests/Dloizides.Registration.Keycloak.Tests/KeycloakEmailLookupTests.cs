using Identity.Keycloak.Admin;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Dloizides.Registration.Keycloak.Tests;

public sealed class KeycloakEmailLookupTests
{
    private readonly FakeKeycloakAdmin _admin = new();

    [Fact]
    public async Task FindByEmailAsync_WithUserUnderAnotherUsername_ReturnsIt()
    {
        _admin.Add(new KeycloakUserRepresentation { Id = "u-1", Username = "other-name", Email = "ana@example.test", EmailVerified = false }, realm: "forms");

        var found = await Provisioner().FindByEmailAsync("forms", "ANA@example.test", CancellationToken.None);

        found.ShouldNotBeNull();
        found.UserId.ShouldBe("u-1");
        _admin.Queries.Single().Email.ShouldBe("ANA@example.test");
        _admin.Queries.Single().Exact.ShouldBe(expected: true);
        _admin.Realms.ShouldBe(["forms"]);
    }

    [Fact]
    public async Task FindByEmailAsync_WhenAbsent_ReturnsNull()
    {
        var found = await Provisioner().FindByEmailAsync("shop", "ana@example.test", CancellationToken.None);

        found.ShouldBeNull();
    }

    [Fact]
    public async Task FindByEmailAsync_WhenListFails_ThrowsInsteadOfAbsent()
    {
        _admin.LookupOutcome = KeycloakAdminOutcome.UpstreamError;

        await Should.ThrowAsync<IdentityProvisioningException>(() => Provisioner().FindByEmailAsync("shop", "ana@example.test", CancellationToken.None));
    }

    private KeycloakIdentityProvisioner Provisioner() =>
        new(_admin, Options.Create(new KeycloakRegistrationOptions { RolesForRealm = _ => [] }));
}
