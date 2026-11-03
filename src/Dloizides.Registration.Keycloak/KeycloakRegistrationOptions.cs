namespace Dloizides.Registration.Keycloak;

public sealed class KeycloakRegistrationOptions
{
    public Func<string, IReadOnlyList<string>>? RolesForRealm { get; set; }

    public string TenantAttribute { get; set; } = "tenantId";
}
