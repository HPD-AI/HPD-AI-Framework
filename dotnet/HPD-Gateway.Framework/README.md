# HPD Gateway

HPD Gateway is an embeddable, ASP.NET Core-native gateway product. Applications
author one typed `GatewayConfiguration`; HPD validates and governs candidate
lifecycles while YARP and ASP.NET Core retain HTTP execution.

## Products

The release contains five library packages and one executable distribution:

| Product | Purpose |
|---|---|
| `HPD.Gateway` | Embedded runtime, declarations, validation, publication, readiness, inspection, resilience, caching, hosting, and applied truth |
| `HPD.Gateway.ControlPlane` | Optional management authority, Admin API, OpenAPI contracts, and Gateway Studio hosting |
| `HPD.Gateway.ControlPlane.Sqlite` | Optional restart-durable SQLite authority |
| `HPD.Gateway.ControlPlane.HPDAuth` | Optional HPD.Auth translation for the Admin API |
| `HPD.Gateway.Discovery.Microsoft` | Optional governed Microsoft Service Discovery adapter |
| `HPD.Gateway.Standalone` | Native AOT-compatible executable distribution |

The packages are intentionally one-directional. Installing `HPD.Gateway` does
not install a control plane, database provider, authentication system, Studio,
or service-discovery provider.

## Embedded runtime

```csharp
builder.Services.AddHpdGateway(gateway =>
{
    gateway.EnableCoreDeclarations();
});

var app = builder.Build();
app.MapHpdGateway();
```

`EnableCoreDeclarations()` enables the four built-in declaration families for
request timeouts, request transforms, response transforms, and protected
credential disposition. Authorization, CORS, admission, inspection,
resilience, caching, discovery, and control-plane products remain explicit. A code-authored
configuration can cross the source-generated canonical wire boundary with
`configuration.ToCanonicalDocument()`; host-aware acceptance still occurs
when the candidate is read against the installed capability snapshot.

## Optional control plane

```csharp
builder.Services.AddHpdGatewayControlPlane(controlPlane =>
{
    controlPlane.UseProcessLocalAuthority();
    controlPlane.AddAdminApi();
    controlPlane.AddStudio();
});

app.MapHpdGatewayControlPlane();
```

Process-local authority is deliberately ephemeral. For restart-durable state,
install `HPD.Gateway.ControlPlane.Sqlite` and select `UseSqlite(...)` with the
required stable authority and protection keys.

HPD.Auth is composed only through the selected Admin product:

```csharp
builder.Services.AddHpdGatewayControlPlane(controlPlane =>
{
    controlPlane.UseSqlite(sqlite => { /* file-backed provider and keys */ });
    controlPlane.AddAdminApi().AddHpdAuth("gateway-admin");
    controlPlane.AddStudio();
});
```

## Optional Microsoft discovery

```csharp
builder.Services.AddHpdGateway(gateway =>
{
    gateway.EnableCoreDeclarations();
    gateway.AddMicrosoftDiscovery("aspire", profile =>
        profile.AddConfiguration());
});
```

The adapter preserves Microsoft provider composition and watching while HPD
owns immutable profile identity, bounds, stale policy, TLS admissibility,
readiness correlation, and redacted applied truth.

## Contract boundary

There are no compatibility packages, namespace aliases, type forwarders, or
legacy extension methods. Public Gateway library namespaces are exactly:

- `HPD.Gateway`
- `HPD.Gateway.ControlPlane`
- `HPD.Gateway.ControlPlane.Sqlite`
- `HPD.Gateway.ControlPlane.HPDAuth`
- `HPD.Gateway.Discovery.Microsoft`

See the official HPD Gateway documentation for runnable tutorials, complete
declaration reference, Admin operations, Studio workflows, deployment, and
troubleshooting guidance.
