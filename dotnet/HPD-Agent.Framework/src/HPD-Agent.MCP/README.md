# HPD Agent MCP

`HPD-Agent.MCP` integrates HPD Agent with stable Model Context Protocol C# SDK 2.x. MCP remains outside agent core and participates through the same immutable, leased capability catalog as native and optional-package capabilities.

## Install

```bash
dotnet add package HPD-Agent.MCP
dotnet add package HPD-Agent.Providers.OpenAI
```

`HPD-Agent.MCP` brings in `HPD-Agent.Framework`; install a provider package or supply an `IChatClient` through `AgentBuilder.WithChatClient`.

## Quick start

Create `mcp.json`:

```json
{
  "servers": [
    {
      "name": "filesystem",
      "transport": "stdio",
      "command": "npx",
      "arguments": ["-y", "@modelcontextprotocol/server-filesystem", "/workspace"],
      "permission": {
        "requiresPermission": false
      }
    }
  ]
}
```

Register it while building the agent:

```csharp
using HPD.Agent;
using HPD.Agent.MCP;

var config = new AgentConfig { Name = "assistant" };

await using var agent = await new AgentBuilder(config)
    .WithChatClient(chatClient)
    .WithMcp("mcp.json")
    .BuildAsync();

var sessionId = await agent.CreateSessionAsync();
var result = await agent.RunAsync(
    "List the files available through the MCP tools.",
    sessionId);

Console.WriteLine(result.Text);
```

The first example explicitly disables permission so it can run without an approval UI. Leave permission enabled for real use and register approval handling with `AgentBuilder.WithPermissions()` plus a `PermissionRequestEvent` subscriber.

Required MCP sources fail agent construction when they cannot load. Set `options.Catalog.LoadMode = McpCatalogLoadMode.Deferred` to load at the first capability boundary, or `options.FailOnServerError = false` to retain successful servers when one server fails.

## Permissions

MCP tools require permission by default. Configure the server policy in the manifest or through `WithMcpOptions`:

```json
{
  "servers": [
    {
      "name": "filesystem",
      "transport": "stdio",
      "command": "npx",
      "arguments": ["-y", "@modelcontextprotocol/server-filesystem", "/workspace"],
      "allowedTools": ["read_file"],
      "permission": {
        "requiresPermission": true,
        "policy": "hpd.filesystem.v1",
        "tools": {
          "read_file": {
            "requiresPermission": false
          }
        }
      }
    }
  ]
}
```

Use `allowedTools` to admit an exact allowlist of original MCP tool names. Permission denial fails closed before the tool executes.

## Registration

```csharp
await using var agent = await new AgentBuilder(config)
    .WithChatClient(chatClient)
    .WithMcp("mcp.json", options =>
    {
        // Null uses SDK discovery-first negotiation and SDK-managed fallback.
        options.Protocol.ExactVersion = null;
        options.Protocol.DiscoveryTimeout = TimeSpan.FromSeconds(5);
        options.Catalog.LoadMode = McpCatalogLoadMode.Eager;
        options.Subscriptions.EnableCatalogInvalidation = true;
    })
    .BuildAsync();
```

Manifest JSON can instead be registered with `WithMcpContent(json, sourceName, configure)`. The source name is a stable identity, not a server display name.

## Manifest

There is one final manifest schema. A server uses `stdio` or Streamable `http`. The manifest may request an exact protocol version, but it never claims negotiated facts. Session IDs, SSE selection, client-owned session flags, bearer tokens, OAuth codes, and runtime callbacks are not manifest fields.

```json
{
  "servers": [
    {
      "name": "filesystem",
      "transport": "stdio",
      "command": "mcp-filesystem",
      "arguments": ["/workspace"],
      "enableResources": true
    },
    {
      "name": "search",
      "transport": "http",
      "endpoint": "https://example.test/mcp",
      "oauth": {
        "registrationMode": "ClientIdMetadataDocument",
        "redirectUri": "http://127.0.0.1:43110/callback",
        "clientIdMetadataDocument": "https://client.example.test/oauth/metadata.json"
      }
    }
  ]
}
```

Reserved MCP headers cannot be configured. HTTP operation is stateless from HPD's perspective; the SDK owns protocol headers, discovery, fallback, and any legacy transport details.

## MRTR and remote Tasks

Ordinary tools invoke the SDK-provided `McpClientTool`. HPD registers bounded client handlers through `IMcpInputResolver`; SDK 2.x owns `input_required` reconstruction, retries, cancellation, and its round limit. Resolver values are JSON and never become successful output while unresolved.

Remote MCP Tasks support ships in this package and is inert until a source opts in explicitly with `options.AddTasksExtension()`.

Remote MCP Tasks are provider-owned durable work. They are distinct from HPD sessions and from local background execution. HPD assigns its own `OperationId`; a remote task ID is retained separately as `ProviderOperationId`. Detaching local observation does not imply remote cancellation.

The four similarly named lifetime concepts are deliberately independent:

- An **HPD session** is application conversation state and can span many turns and connections.
- A **legacy MCP transport session** is an SDK-owned protocol connection detail, such as `Mcp-Session-Id`; application code must not persist or manufacture it.
- **HPD background work** is locally controlled work represented by the unified operation registry.
- A **remote MCP Task** is server-controlled work observed through that same registry while preserving its separate provider task ID.

## Lifetime and refresh

Each MCP capability revision owns its clients, subscriptions, and authorization adapters. Agent turns pin one immutable catalog epoch. Notifications make a source eligible for transactional refresh; they never mutate a running turn. Retired connections close only after the final snapshot or turn lease releases the revision. `Agent` and MCP runtime ownership are asynchronous (`await using`).

## Public API boundary

Application code composes MCP through:

- `WithMcp`, `WithMcpContent`, and `WithMcpOptions`;
- `McpOptions` and its policy/configuration types;
- `IMcpInputResolver`, `IMcpInputAuthorizer`, and
  `IMcpRecoveryReferenceProtector`;
- `IMcpAuthorizationStore`, `McpAuthorizationRecord`, and
  `McpResourceRegistrationId.Create`;
- `AddTasksExtension`.

The raw MCP runtime, JSON serializer context, resource/prompt projection models,
catalog cache, transport framing, and client-handler implementation are internal.

`McpCapabilitySourceFactory`, `McpConfigurationPackage`, and the generated
`McpServerSource` descriptor remain public only because generated application code
references them. They are hidden from IntelliSense. Application code must not call
them directly.

## Authorization

Client ID Metadata Documents and pre-registration are the normal modes. Dynamic Client Registration requires both `DynamicRegistration` mode and `AllowDynamicRegistration = true`. Durable authorization records are application-owned through `IMcpAuthorizationStore` and are bound to resource identity, authorization-server issuer, client identity, and normalized scopes. Secrets are resolved from secret keys and are never serialized into manifests or projected into events.

## Isolated stdio

When `processIsolation.enabled` is true, an application-provided `IProcessProvider` is required and HPD fails closed when it is absent. The custom transport changes process launch and byte ownership only; JSON-RPC framing and MCP semantics remain SDK-owned.

## Native AOT

Declarative `AgentConfig.mcp` and source-generated `[McpServer]` are the Native AOT
paths. The package has a publish-and-run Native AOT gate covering discovery, tool
listing, invocation, permissions, serialization, and shutdown.
