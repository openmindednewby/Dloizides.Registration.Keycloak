# Build

```bash
dotnet test Dloizides.Registration.Keycloak.slnx
pwsh ./publish.ps1 -NoBump
```

Publish `Dloizides.Registration` first: the project reference becomes a package dependency of the same version.
