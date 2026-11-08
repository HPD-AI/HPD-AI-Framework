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

Customize the standard `OpenApiOptions` through the optional callback:

```csharp
builder.Services.AddHPDAgentOpenApi(configure: options =>
{
    options.ShouldInclude = description =>
        description.RelativePath?.StartsWith("api/hpd-agent") == true;
});
```

This package does not include Swagger UI, Scalar, ReDoc, or client generation.
