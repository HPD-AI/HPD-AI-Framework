# HPD-Agent.MCP

MCP client integration for HPD agents. It connects HPD agents to MCP servers and exposes MCP tools plus optional resource and prompt access as HPD `AIFunction`s.

## Install

```bash
dotnet add package HPD-Agent.MCP
```

## Use When

Use this package when an HPD agent needs tools, readable resources, or reusable prompt templates from local stdio MCP servers or remote HTTP MCP servers.

## Manifest

MCP server entries must declare an explicit `transport`.

### Local stdio server

```json
{
  "servers": [
    {
      "name": "filesystem",
      "transport": "stdio",
      "command": "npx",
      "arguments": ["-y", "@modelcontextprotocol/server-filesystem", "."],
      "workingDirectory": "/workspace",
      "inheritEnvironmentVariables": false,
      "useDefaultEnvironmentVariables": true,
      "environment": {
        "FILESYSTEM_ROOT": "/workspace"
      },
      "environmentSecretKeys": {
        "GITHUB_TOKEN": "mcp:filesystem:GitHubToken"
      },
      "enableResources": true,
      "maxResourceListResults": 100,
      "maxResourceContentLength": 200000,
      "enablePrompts": true,
      "maxPromptListResults": 100,
      "maxPromptContentLength": 200000,
      "shutdownTimeoutMs": 5000
    }
  ]
}
```

### Remote HTTP server

```json
{
  "servers": [
    {
      "name": "search",
      "transport": "http",
      "endpoint": "https://mcp.example.com/mcp",
      "httpTransportMode": "auto",
      "headers": {
        "X-Workspace": "example"
      },
      "headerSecretKeys": {
        "Authorization": "mcp:search:Authorization"
      }
    }
  ]
}
```

### Stdio process isolation

Local stdio MCP servers can request HPD process isolation through `processIsolation`. This is only valid for `transport: "stdio"` because HTTP MCP servers are reached over the network rather than launched as child processes.

```json
{
  "servers": [
    {
      "name": "filesystem",
      "transport": "stdio",
      "command": "npx",
      "arguments": ["-y", "@modelcontextprotocol/server-filesystem", "."],
      "processIsolation": {
        "mode": "Isolated",
        "profile": "filesystem-only",
        "allowWrite": ["."],
        "denyRead": ["~/.ssh", "~/.aws", "~/.gnupg"],
        "networkMode": "Blocked"
      }
    }
  ]
}
```

The manifest declares the requested policy. The host application must provide an `IProcessProvider` through `MCPOptions.ProcessProvider` to enforce it. If a server requests isolated mode and no process provider is configured, HPD fails closed instead of launching the server unsandboxed.

```csharp
var agent = new AgentBuilder()
    .WithMCP("mcp.json", options =>
    {
        options.ProcessProvider = myProcessProvider;
    })
    .Build();
```

`processIsolation` currently has 20 manifest fields, grouped into seven policy areas:

| Area | Fields |
| --- | --- |
| Mode/profile | `mode`, `profile` |
| Filesystem | `allowRead`, `denyRead`, `allowWrite`, `denyWrite` |
| Network | `networkMode`, `allowedDomains`, `deniedDomains` |
| Unix sockets | `allowUnixSockets`, `allowAllUnixSockets` |
| Environment | `allowedEnvironmentVariables`, `stripUnlistedEnvironmentVariables` |
| TLS trust | `tlsTrustMode`, `injectTlsTrustEnvironmentVariables` |
| Process behavior | `allowPty`, `allowLocalBinding`, `allowedMachLookups`, `ignoreViolations`, `violationAction` |

Named profiles are `filesystem-only`, `network-only`, `permissive`, and `disabled`. Omitting `profile` uses HPD's restrictive default profile.

### OAuth-protected HTTP server

```json
{
  "servers": [
    {
      "name": "enterprise",
      "transport": "http",
      "endpoint": "https://mcp.example.com/mcp",
      "oauth": {
        "redirectUri": "http://localhost:8787/callback",
        "clientId": "client-id",
        "clientSecretKey": "mcp:enterprise:ClientSecret",
        "scopes": ["read", "write"],
        "additionalAuthorizationParameters": {
          "audience": "mcp"
        }
      }
    }
  ]
}
```

OAuth runtime behavior is code-injected through `MCPOptions.OAuthRuntime`, not serialized into the manifest. The manifest carries server data such as redirect URI, client ID, scopes, and secret keys; the host application owns browser/login UX and token persistence.

```csharp
var agent = new AgentBuilder()
    .WithMCP("mcp.json", options =>
    {
        options.OAuthRuntime = new MyMcpOAuthRuntime();
    })
    .Build();
```

For toolharness-owned `[MCPServer]` declarations without a standalone manifest, configure options separately:

```csharp
var agent = new AgentBuilder()
    .WithMCPOptions(options =>
    {
        options.OAuthRuntime = new MyMcpOAuthRuntime();
    })
    .WithToolHarness<MyHarness>()
    .Build();
```

`IMcpOAuthRuntime` can provide SDK hooks for authorization redirects, token caching, authorization-server selection, scope selection, and dynamic client registration persistence.

Built-in runtimes:

- `InMemoryMcpOAuthRuntime` stores tokens and dynamic client registrations for the lifetime of the runtime instance.
- `JsonMcpOAuthRuntime` stores tokens and dynamic client registrations as JSON files under a host-provided cache directory.

```csharp
var agent = new AgentBuilder()
    .WithMCP("mcp.json", options =>
    {
        options.OAuthRuntime = new JsonMcpOAuthRuntime(
            "~/.hpd/mcp/oauth",
            authorizationRedirectDelegate: McpOAuthRedirectHandlers.LocalBrowser());
    })
    .Build();
```

Secret-backed fields are resolved through HPD's `ISecretResolver`. Literal values win when both a literal value and a `*SecretKey` are provided. Configuration and custom/vault resolvers can resolve arbitrary keys such as `mcp:enterprise:ClientSecret`; environment resolution requires the key to have registered aliases or to be exposed through configuration.

## Resources

Set `enableResources` to `true` to expose a small generic resource toolset for servers that advertise the MCP resources capability:

- `mcp_{server}_list_resources`
- `mcp_{server}_list_resource_templates`
- `mcp_{server}_read_resource`

These functions obey the same collapse/no-collapse behavior as MCP tools. When server collapsing is enabled, they sit behind the same `MCP_{server}` container as the server's tools. Text resource reads are capped by `maxResourceContentLength`; binary resources return metadata instead of blob data.

## Prompts

Set `enablePrompts` to `true` to expose a small generic prompt toolset for servers that advertise the MCP prompts capability:

- `mcp_{server}_list_prompts`
- `mcp_{server}_get_prompt`

These functions obey the same collapse/no-collapse behavior as MCP tools and resources. Prompt results are returned as structured tool output, not automatically injected into the agent prompt. Text prompt content is capped by `maxPromptContentLength`; image, audio, and binary embedded-resource content returns metadata instead of base64 payloads.

## Live Updates

Set `enableLiveUpdates` to `true` to emit HPD agent events when an MCP server reports that tools, prompts, or resources changed. Add `resourceSubscriptions` when the agent should subscribe to updates for specific MCP resource URIs.

```json
{
  "name": "workspace",
  "transport": "stdio",
  "command": "mcp-server",
  "enableLiveUpdates": true,
  "resourceSubscriptions": [
    "file:///workspace/README.md"
  ]
}
```

Live updates are invalidation signals. HPD does not mutate an active tool list mid-turn; applications can subscribe and decide whether to reload or rebuild:

```csharp
using var subscription = agent.Subscribe<McpServerChangedEvent>(evt =>
{
    Console.WriteLine($"{evt.ServerName}: {evt.ChangeKind} changed");
});
```

MCP live update events live in `HPD.Agent.MCP` and are registered for agent-event serialization by the MCP package. Events are emitted into the owning agent's event coordinator, so existing parent/sub-agent/workflow bubbling applies automatically.

## Builder

```csharp
var agent = new AgentBuilder()
    .WithMCP("mcp.json")
    .Build();
```
