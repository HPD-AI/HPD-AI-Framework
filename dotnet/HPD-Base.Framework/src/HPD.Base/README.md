# HPD.Base

HPD.Base is a typed, policy-aware application-data runtime for .NET 10. It
combines generated schemas and queries with records, atomic mutations, files,
realtime delivery, durable replay, live queries, relational execution, mutation
receipts, history, backup, restore, and provider-owned administration.

```csharp
builder.Services.AddHPDBase(hpd => hpd
    .AddCollection(Project.Collection)
    .AddAspNetCore());

app.MapHPDBasePublicApi();
app.MapHPDBaseApplicationApi(new HPDBaseApplicationEndpointOptions
{
    AuthorizationPolicy = "App.User"
});
```

Install `HPD.Base.Auth` and call `AddHPDAuth()` plus
`MapHPDBaseControlPlane(...)` when an HPD.Auth L1-secured management surface is
required. ASP.NET capability authorization and BASE Runtime resource policy are
independent gates.

Documentation and cookbooks: <https://github.com/HPD-AI/HPD-Base>

Durable background work and scheduling: [Durable activations](DurableActivations.md)
