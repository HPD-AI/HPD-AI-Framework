# HPD-Agent.MultiAgent.AspNetCore

Provider-neutral ASP.NET Core routes for listing installed multi-agent
workflows and submitting runs to a durable backend.

## Install

```bash
dotnet add package HPD-Agent.MultiAgent.AspNetCore
```

## Backend contract

Register one or more `IHPDMultiAgentWorkflowBackend` implementations, then map
the routes:

```csharp
builder.Services.AddHPDMultiAgentAspNetCore();
builder.Services.AddSingleton<IHPDMultiAgentWorkflowBackend, MyWorkflowBackend>();

var app = builder.Build();
app.MapHPDMultiAgentApi();
```

The backend owns durable persistence and idempotency. This package owns request
validation, JSON contracts, route mapping, and HTTP error projection.

For HPD Base-backed durable activations, use
`HPD-Agent.MultiAgent.HpdBase` as the backend adapter.
