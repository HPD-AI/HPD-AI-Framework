# HPD-Agent.AspNetCore.OpenApi

Optional `.NET 10` OpenAPI 3.1 document generation for the HPD-Agent HTTP API.

## Registration

```csharp
using HPD.Agent;
using HPD.Agent.AspNetCore;
using HPD.Agent.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddHPDAgentHost("assistant");
builder.Services.AddHPDAgentOpenApi();

var app = builder.Build();
app.MapHPDAgentOpenApiDocuments();
app.MapHPDAgentHostApi("assistant");
```

Documents are available at:

```text
/openapi/hpd-agent-v1.json
/openapi/hpd-agent-v1.yaml
```

The document adds HPD-specific descriptions for:

- typed public agent inputs;
- permission, clarification, continuation, and client-tool responses;
- accepted thread execution submissions;
- runtime state and context-use responses;
- execution cancellation and queue promotion;
- multipart content upload;
- binary content download;
- resumable committed SSE with cursor replay;
- the application event union, including custom output events, with durability
  and module metadata per event schema;
- client-tool-provider WebSocket metadata;
- the immutable agent event catalog.

## Authentication and clients

Authentication remains owned by the consuming ASP.NET Core application. The generated document does not invent a security scheme or attach one to every operation. Add the document transformer, security scheme, and operation requirements that match your host's middleware before using the contract for protected endpoints:

```csharp
using Microsoft.OpenApi;

builder.Services.AddHPDAgentOpenApi(configure: options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] =
            new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            };
        return Task.CompletedTask;
    });
});
```

Place the transformer after `AddHPDAgentOpenApi` and add operation-level security requirements with your normal ASP.NET Core OpenAPI conventions. The generated contract is OpenAPI 3.1 and can be consumed by Kiota, OpenAPI Generator, or another 3.1-capable client generator.

HPD also provides a deterministic C# client generator for its canonical hosted
contract. See the
[client generator guide](../HPD-Agent.ClientGenerator/README.md) for runtime
event unions, resumable SSE, client-tool WebSockets, authentication, and generated
API-change checks.

Customize the standard `OpenApiOptions` through the optional callback:

```csharp
builder.Services.AddHPDAgentOpenApi(configure: options =>
{
    options.ShouldInclude = description =>
        description.RelativePath?.StartsWith("api/hpd-agent") == true;
});
```

This package does not include Swagger UI, Scalar, ReDoc, or client generation.
