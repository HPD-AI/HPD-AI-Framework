# HPD-Agent.OpenApi

Use an OpenAPI-described REST API as HPD-Agent tools. Each imported operation becomes an HPD-aware `AIFunction` that participates in tool visibility, permissions, retries, and model-facing response shaping.

This package consumes external OpenAPI specifications. To publish HPD-Agent's own HTTP API as an OpenAPI document, use `HPD-Agent.AspNetCore.OpenApi`.

## Install

```bash
dotnet add package HPD-Agent.OpenApi
dotnet add package HPD-Agent.Providers.OpenAI
```

`HPD-Agent.OpenApi` includes `HPD-Agent.Framework`. Install a provider package or supply an `IChatClient` through `AgentBuilder.WithChatClient`.

## Builder registration

```csharp
using HPD.Agent;
using HPD.Agent.OpenApi;

var config = new AgentConfig { Name = "assistant" };

await using var agent = await new AgentBuilder(config)
    .WithChatClient(chatClient)
    .WithOpenApi("petstore", "petstore.json", options =>
    {
        options.ServerUrlOverride = new Uri("https://petstore.example.com/");
        options.OperationsToExclude = ["deletePet"];
        options.ResponseOptimization = new ResponseOptimizationConfig
        {
            FieldsToExclude = ["internalId"]
        };
    })
    .BuildAsync();

var sessionId = await agent.CreateSessionAsync();
var result = await agent.RunAsync("List the available pets.", sessionId);
```

Operations are named `{source}_{operationId}`. A source named `petstore` and an operation named `listPets` produce `petstore_listPets`.

The source name must be unique within the agent. Operations from one source can be filtered with either `OperationsToExclude` or `OperationSelectionPredicate`, but not both.

## Authentication

Use `AuthCallback` for application-owned authentication. The callback runs immediately before each API request, so secret rotation and token refresh do not require rebuilding the agent:

```csharp
var config = new OpenApiConfig
{
    SpecUri = new Uri("https://api.example.com/openapi.json"),
    AuthCallback = async (request, cancellationToken) =>
    {
        var token = await secrets.RequireAsync(
            "example:ApiToken",
            "Example API",
            cancellationToken: cancellationToken);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }
};

await using var agent = await new AgentBuilder(agentConfig)
    .WithChatClient(chatClient)
    .WithOpenApi("example", config)
    .BuildAsync();
```

Provide your own `HttpClient` when the API requires a custom handler, proxy, certificate policy, or lifetime:

```csharp
var apiConfig = new OpenApiConfig
{
    SpecPath = "openapi.json",
    HttpClient = applicationOwnedHttpClient
};
```

An application-provided `HttpClient` is not disposed by HPD. Otherwise, HPD owns the client for the lifetime of the agent capability revision.

## ToolHarness registration

Use `[OpenApi]` when API tools belong to a generated ToolHarness:

```csharp
using HPD.Agent;
using HPD.Agent.OpenApi;
using HPD.Agent.Secrets;

public sealed class ApiTools(ISecretResolver secrets)
{
    [OpenApi(Prefix = "petstore")]
    [RequiresPermission]
    public OpenApiConfig PetStore() => new()
    {
        SpecPath = "petstore.json",
        AuthCallback = async (request, cancellationToken) =>
        {
            var token = await secrets.RequireAsync(
                "petstore:ApiToken",
                "Petstore",
                cancellationToken: cancellationToken);
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
    };
}
```

`[OpenApi]` methods must be parameterless and return `OpenApiConfig`. The ToolHarness instance itself may receive dependencies through constructor injection.

## Permissions and request behavior

OpenAPI tools do not require permission by default. Set `RequiresPermission = true` on `OpenApiConfig`, or apply `[RequiresPermission]` to an `[OpenApi]` method, to require permission for every operation generated from that source.

Retryable HTTP failures (`401`, `408`, `429`, and `5xx`) are surfaced through the agent's function retry path. OpenAPI functions automatically use HPD's OpenAPI error handler, including status categorization and `Retry-After` delays. Client errors such as `400`, `404`, and `422` are returned as structured model-facing errors so the model can correct its arguments.

Skipped operations emit a structured warning containing the source, operation ID,
method, path, and reason. Full exception detail is available through the configured
logger.

An OpenAPI source that produces zero operations fails agent construction with
`OpenApiSourceProducedNoOperations`. Inspect `OperationSelectionPredicate` and
`OperationsToExclude`, or set `AllowEmptyCatalog = true` when an empty source is
intentional.

## Response optimization

`ResponseOptimizationConfig` reduces the content returned to the model:

```csharp
options.ResponseOptimization = new ResponseOptimizationConfig
{
    DataField = "result.items",
    FieldsToInclude = ["id", "name", "status"],
    MaxLength = 8_000
};
```

`DataField` is applied first, followed by `FieldsToInclude` or `FieldsToExclude`, and finally `MaxLength`. The default response limit is 4,000 characters.

## Public API boundary

Application code composes OpenAPI through `WithOpenApi`, `OpenApiConfig`,
`ResponseOptimizationConfig`, and the generated `[OpenApi]` ToolHarness path.

Specification loading, operation parsing, function construction, response
formatting, error classification, and automatic package discovery are internal.
Do not depend on implementation types from examples that inspect package source.

The package is Native AOT-compatible. Use generated composition and do not add
runtime reflection-based operation discovery.

## Troubleshooting

- If no tools appear, inspect `OperationsToExclude`,
  `OperationSelectionPredicate`, the `OpenApiSourceProducedNoOperations`
  diagnostic, and the generated operation IDs.
- If requests fail before reaching the API, verify `ServerUrlOverride` and the
  relative-server resolution in the source document.
- If secret rotation must not rebuild the agent, resolve the token inside
  `AuthCallback`.
- If an API uses HTTP 200 for application-level failures, configure
  `ErrorDetector`.
- If only some tools are missing, search application logs for skipped OpenAPI
  operation warnings and compare the operation IDs with the source document.
