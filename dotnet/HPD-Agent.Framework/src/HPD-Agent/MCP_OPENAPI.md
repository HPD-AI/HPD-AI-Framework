# MCP and OpenAPI integrations

HPD-Agent has three related but distinct packages:

| Package | Purpose | Target frameworks |
|---|---|---|
| `HPD-Agent.MCP` | Consume Model Context Protocol servers as agent capabilities | .NET 8, 9, 10 |
| `HPD-Agent.OpenApi` | Consume OpenAPI-described REST APIs as agent tools | .NET 8, 9, 10 |
| `HPD-Agent.AspNetCore.OpenApi` | Publish HPD-Agent's own ASP.NET Core API contract as OpenAPI 3.1 | .NET 10 |

Use `HPD-Agent.MCP` for MCP tools, resources, and prompts. Use
`HPD-Agent.OpenApi` to import operations from an external REST API. Use
`HPD-Agent.AspNetCore.OpenApi` when the agent application itself is the API
provider and needs a machine-readable contract.

## Composition

Both consumer integrations participate in the same immutable, leased capability
catalog as native functions and ToolHarness capabilities:

```csharp
using HPD.Agent;
using HPD.Agent.MCP;
using HPD.Agent.OpenApi;

await using var agent = await new AgentBuilder(new AgentConfig
{
    Name = "integration-assistant"
})
    .WithChatClient(chatClient)
    .WithMcp("mcp.json")
    .WithOpenApi("billing", "billing-openapi.json")
    .BuildAsync();
```

MCP and OpenAPI sources load during agent construction by default. A required
source that cannot load fails construction rather than silently starting with a
partial tool catalog. Use each package's deferred or retain-last-known-good
options only when that behavior is intentional.

Agent turns pin one source revision. Catalog invalidation or refresh changes
later turns without mutating a turn already in progress.

## Permissions

The packages intentionally have different defaults:

- MCP tools require permission by default.
- OpenAPI operations do not require permission by default.

Set the policy explicitly in the MCP manifest or through
`[RequiresPermission]` on an `[McpServer]` method. For OpenAPI, set
`OpenApiConfig.RequiresPermission = true` or apply `[RequiresPermission]` to the
`[OpenApi]` method.

Use MCP `allowedTools` to constrain admission to exact original server tool
names. Operation filtering for OpenAPI uses `OperationsToExclude` or
`OperationSelectionPredicate`.

## Authentication and secrets

OpenAPI authentication is application-owned through `AuthCallback`. The callback
runs before each request and is the correct place to refresh a token from an
`ISecretResolver`.

MCP HTTP authorization is configured through the server manifest's `oauth`
section and an application-owned `IMcpAuthorizationStore`. Secret values are
resolved from secret keys and never belong in committed manifests.

Neither package serializes bearer tokens, OAuth codes, or SDK transport sessions
into events or manifests.

## Generated code and public surface

The consumer surface consists of builder extensions, configuration objects,
result/input contracts, and application extension interfaces.

The following are implementation details:

- raw MCP runtime and client ownership;
- MCP resource and prompt projection serializers;
- OpenAPI parsing, function construction, and response formatting;
- internal MCP activation and capability-resolution plumbing.

`McpCapabilitySourceFactory`, `McpConfigurationPackage`, and
`McpServerSource` remain public because generated application code references
them. They are hidden from consumer IntelliSense and must not be called directly.
Use `WithMcp`, `WithMcpContent`, or `[McpServer]` instead.

Package API-surface tests guard the exported type sets so implementation types
cannot accidentally become part of the 1.0 compatibility contract.

## Native AOT

Both consumer packages are AOT-compatible. Applications must use generated
capability composition rather than runtime reflection.

The MCP Native AOT gate publishes and executes an actual native binary, covering
declarative AgentConfig composition, HTTP MCP discovery, tool invocation,
permission handling, and serialization.

## Troubleshooting

- A protected tool that fails publication usually needs
  `AgentBuilder.WithPermissions()` and a `PermissionRequestEvent` handler.
- An MCP source that fails during construction should be checked with
  `FailOnServerError = false` only to isolate the failing server; fix the
  source before production.
- An OpenAPI source with no operations fails with
  `OpenApiSourceProducedNoOperations`. Inspect the logged exclusions and predicate
  state, or opt into an empty catalog explicitly.
- An MCP HTTP server that cannot negotiate should use automatic protocol
  fallback. Set `Protocol.ExactVersion` only when the server contract requires
  one revision.

## Package guides

- [MCP package guide](../HPD-Agent.MCP/README.md)
- [OpenAPI consumer guide](../HPD-Agent.OpenApi/README.md)
- [ASP.NET Core OpenAPI publisher guide](../HPD-Agent.AspNetCore.OpenApi/README.md)
- [OpenAPI client generator](../HPD-Agent.ClientGenerator/README.md)
