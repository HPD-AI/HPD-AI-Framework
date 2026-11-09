# HPD-Agent.MultiAgent.HpdBase

Adapter that maps the provider-neutral `HPD-Agent.MultiAgent.AspNetCore`
workflow backend contract to HPD Base durable activations.

This package owns the HPD Base-specific behavior:

- installed graph activation definitions;
- canonical graph input;
- principal- and session-scoped access;
- idempotency fingerprints;
- durable activation enqueue;
- Base activation state and disposition projections.

## Install

```bash
dotnet add package HPD-Agent.MultiAgent.HpdBase
```

## Registration

```csharp
builder.Services.AddHPDMultiAgentHpdBase(installedGraphs);
app.MapHPDMultiAgentApi();
```

This package depends on compatible `HPD-Base.AspNetCore` and
`HPD-Graph.Base` releases. Use `HPD-Agent.MultiAgent.AspNetCore` without this
adapter when supplying another durable backend.
