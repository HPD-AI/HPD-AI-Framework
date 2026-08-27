# HPD Agent MCP

`HPD-Agent.MCP` integrates HPD Agent with stable Model Context Protocol C# SDK 2.x. MCP remains outside agent core and participates through the same immutable, leased capability catalog as native and optional-package capabilities.

## Registration

```csharp
await using var agent = await new AgentBuilder(config)
    .WithMcp("mcp.json", options =>
    {
        // Null uses SDK discovery-first negotiation and SDK-managed fallback.
        options.Protocol.ExactVersion = null;
        options.Protocol.DiscoveryTimeout = TimeSpan.FromSeconds(5);
        options.Invocation.InputResolver = inputResolver;
        options.ProcessProvider = processProvider;
        options.AuthorizationStore = authorizationStore;
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

Remote MCP Tasks are optional and live in the separate `HPD-Agent.MCP.Tasks` package. Installing that package alone changes nothing; opt a source in explicitly with `options.AddTasksExtension()`. The base package therefore has no Tasks-extension dependency or runtime activation.

Remote MCP Tasks are provider-owned durable work. They are distinct from HPD sessions and from local background execution. HPD assigns its own `OperationId`; a remote task ID is retained separately as `ProviderOperationId`. Detaching local observation does not imply remote cancellation.

The four similarly named lifetime concepts are deliberately independent:

- An **HPD session** is application conversation state and can span many turns and connections.
- A **legacy MCP transport session** is an SDK-owned protocol connection detail, such as `Mcp-Session-Id`; application code must not persist or manufacture it.
- **HPD background work** is locally controlled work represented by the unified operation registry.
- A **remote MCP Task** is server-controlled work observed through that same registry while preserving its separate provider task ID.

## Lifetime and refresh

Each MCP capability revision owns its clients, subscriptions, and authorization adapters. Agent turns pin one immutable catalog epoch. Notifications make a source eligible for transactional refresh; they never mutate a running turn. Retired connections close only after the final snapshot or turn lease releases the revision. `Agent` and MCP runtime ownership are asynchronous (`await using`).

## Authorization

Client ID Metadata Documents and pre-registration are the normal modes. Dynamic Client Registration requires both `DynamicRegistration` mode and `AllowDynamicRegistration = true`. Durable authorization records are application-owned through `IMcpAuthorizationStore` and are bound to resource identity, authorization-server issuer, client identity, and normalized scopes. Secrets are resolved from secret keys and are never serialized into manifests or projected into events.

## Isolated stdio

When `processIsolation.enabled` is true, an application-provided `IProcessProvider` is required and HPD fails closed when it is absent. The custom transport changes process launch and byte ownership only; JSON-RPC framing and MCP semantics remain SDK-owned.
