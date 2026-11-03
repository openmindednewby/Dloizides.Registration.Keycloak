# Dloizides.Registration.Keycloak

`IIdentityProvisioner` on `Identity.Keycloak`'s `IKeycloakAdminClient`. Every call uses the realm it is given. A 404 reads as absent; any other failure throws, so a Keycloak outage can never close a deletion request.

```csharp
services.AddRegistrationKeycloak(options => options.RolesForRealm = realm => rolesByRealm[realm]);
```

The host registers `IKeycloakAdminClient` through `Identity.Keycloak`.
