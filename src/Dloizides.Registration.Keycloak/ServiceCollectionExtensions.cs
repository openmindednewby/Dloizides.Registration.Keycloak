using Dloizides.Registration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dloizides.Registration.Keycloak;

public static class ServiceCollectionExtensions
{
    /// <summary>Uses Keycloak for logins. The host registers IKeycloakAdminClient and sets RolesForRealm.</summary>
    public static IServiceCollection AddRegistrationKeycloak(this IServiceCollection services, Action<KeycloakRegistrationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.TryAddScoped<IIdentityProvisioner, KeycloakIdentityProvisioner>();
        return services;
    }
}
