# Building Web Apps

> Stream agent events to web, mobile, and desktop apps in real time

HPD-Agent works in web applications through real-time event streaming. The framework provides serialization tools for C# backends and two TypeScript packages for frontends:

- **`@hpd/hpd-agent-client`** — framework-agnostic TypeScript SDK for event streaming (SSE/WebSocket). Works in any frontend: Svelte, Vue, vanilla JS, or Node.js.
- **`@hpd/hpd-agent-headless-ui`** — headless Svelte component library built on top of the client SDK. Zero CSS — you bring the styles.

## Architecture Overview

```
┌─────────────────┐    SSE or WebSocket    ┌─────────────────┐
│   Frontend      │ ──────────────────── → │   ASP.NET API   │
│   (TypeScript)  │ ← ─ ─ ─ ─ ─ ─ ─ ─ ─  │   (C# Backend)  │
└─────────────────┘       events           └─────────────────┘
                                                    ↓
                                           Agent.RunAsync()
```

**How it works:**
1. Frontend sends user messages to the backend
2. Backend streams agent events via SSE or WebSocket
3. Frontend receives events in real-time using `@hpd/hpd-agent-client`
4. Bidirectional events (permissions, clarifications) are sent back to the backend

## Quick Start

### 1. Backend: ASP.NET Core

Install the ASP.NET Core integration package:
```bash
dotnet add package HPD-Agent.AspNetCore
```

Register and map the agent:

```csharp
using HPD.Agent.AspNetCore;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddHPDAgent(options =>
{
    options.PersistAfterTurn = true;
    options.ConfigureAgent = agent => agent
        .WithProvider("anthropic", "claude-sonnet-4-5")
        .WithInstructions("You are a helpful assistant.");
});

var app = builder.Build();

app.MapHPDAgentApi();  // Mounts the full REST + streaming API

app.Run();
```

That's it. `MapHPDAgentApi()` registers the complete API automatically — sessions, branches, SSE streaming, WebSocket streaming, asset upload, permission responses, and client tool responses.

### 2. Frontend: TypeScript Client SDK

Install the client:
```bash
npm install @hpd/hpd-agent-client
```

Connect and stream:

```typescript
import { HpdAgentClient } from '@hpd/hpd-agent-client';

const client = new HpdAgentClient({ baseUrl: 'http://localhost:5000' });

for await (const event of client.streamMessage({ content: 'Hello!' })) {
    if (event.type === 'TextDeltaEvent') {
        process.stdout.write(event.text);
    }
}
```

The client handles event parsing, bidirectional communication, and reconnection automatically.

## What You Get

`MapHPDAgentApi()` mounts the full REST + streaming API:

**Sessions**
- `POST /sessions` — create session
- `GET /sessions` — list sessions
- `POST /sessions/search` — filter sessions
- `GET /sessions/{sid}` — get session metadata
- `PATCH /sessions/{sid}` — update session metadata
- `DELETE /sessions/{sid}` — delete session + all branches

**Branches**
- `GET /sessions/{sid}/branches` — list branches
- `POST /sessions/{sid}/branches` — create branch
- `POST /sessions/{sid}/branches/{bid}/fork` — fork at message index
- `DELETE /sessions/{sid}/branches/{bid}` — delete branch
- `GET /sessions/{sid}/branches/{bid}/messages` — get messages
- `GET /sessions/{sid}/branches/{bid}/siblings` — get sibling branches

**Streaming**
- `POST /sessions/{sid}/branches/{bid}/stream` — SSE streaming
- `GET /sessions/{sid}/branches/{bid}/ws` — WebSocket streaming

**Assets**
- `POST /sessions/{sid}/assets` — upload file (multipart)
- `GET /sessions/{sid}/assets` — list assets
- `GET /sessions/{sid}/assets/{assetId}` — download asset
- `DELETE /sessions/{sid}/assets/{assetId}` — delete asset

**Bidirectional**
- `POST /sessions/{sid}/branches/{bid}/permissions/respond` — approve/deny permission
- `POST /sessions/{sid}/branches/{bid}/client-tools/respond` — client tool result

## Configuration

```csharp
builder.Services.AddHPDAgent(options =>
{
    // Session persistence
    options.SessionStore = new JsonSessionStore("./sessions");
    options.PersistAfterTurn = true;

    // Agent setup
    options.ConfigureAgent = agent => agent
        .WithProvider("openai", "gpt-4o")
        .WithToolkit<MyTools>();

    // Session lifecycle
    options.AgentIdleTimeout = TimeSpan.FromMinutes(30);  // default
    options.AllowRecursiveBranchDelete = false;            // default
});
```

### HPDAgentConfig Reference

`HPDAgentConfig` is the configuration class passed to `AddHPDAgent(options => ...)`. All properties are optional.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SessionStore` | `ISessionStore?` | `InMemorySessionStore` | Store for session/branch state. The hosting layer owns this (not `AgentBuilder`) so session endpoints work before an agent is created. |
| `PersistAfterTurn` | `bool` | `false` | Auto-save conversation history after each completed turn. Only meaningful with a durable `SessionStore`. |
| `AgentConfig` | `AgentConfig?` | `null` | Serializable agent configuration applied to every new agent. Takes priority over `AgentConfigPath`. |
| `AgentConfigPath` | `string?` | `null` | Path to a JSON file containing an `AgentConfig`. Loaded once at startup. Ignored if `AgentConfig` is set. |
| `ConfigureAgent` | `Action<AgentBuilder>?` | `null` | Callback to configure the `AgentBuilder` for each new session. Called after `AgentConfig`/`AgentConfigPath` are applied. Use for runtime-only concerns: compiled type references, DI services, native toolkits. |
| `AgentIdleTimeout` | `TimeSpan` | 30 minutes | How long an agent can sit idle before being evicted from the in-process cache. |
| `AllowRecursiveBranchDelete` | `bool` | `false` | Whether `DELETE /branches/{id}?recursive=true` is allowed. When `false`, you must delete leaf branches manually before deleting their parents. |

**Separation of concerns:** `AgentConfig` / `AgentConfigPath` define serializable agent settings (provider, system instructions, toolkits by name). `ConfigureAgent` handles what cannot be serialized: compiled `Type` references, dependency-injected services, and runtime state.

```csharp
builder.Services.AddHPDAgent(options =>
{
    // Serializable config from file (provider, instructions, named toolkits)
    options.AgentConfigPath = "./agent-config.json";

    // Runtime additions (compiled types, DI services) — not in JSON
    options.ConfigureAgent = agent => agent
        .WithServiceProvider(serviceProvider)
        .WithToolkit<MyNativeToolkit>();
});
```

### Multiple Agents

Host multiple agents at different route prefixes:

```csharp
builder.Services.AddHPDAgent("support", options => {
    options.ConfigureAgent = agent => agent.WithProvider("openai", "gpt-4o-mini");
});
builder.Services.AddHPDAgent("research", options => {
    options.ConfigureAgent = agent => agent.WithProvider("anthropic", "claude-opus-4-6");
});

app.MapHPDAgentApi("support").WithPrefix("/support");
app.MapHPDAgentApi("research").WithPrefix("/research");
```

### Custom Agent Factory

For full control over agent creation per session:

```csharp
public class MyAgentFactory : IAgentFactory
{
    public Task<Agent> CreateAgentAsync(string sessionId, ISessionStore store, CancellationToken ct)
    {
        var agent = await new AgentBuilder()
            .WithProvider("openai", "gpt-4o")
            .WithSessionStore(store)
            .BuildAsync();
        return Task.FromResult(agent);
    }
}

builder.Services.AddSingleton<IAgentFactory, MyAgentFactory>();
builder.Services.AddHPDAgent();
```

## Production Setup

For complete production patterns, see:

- [**Event Handling**](05%20Event%20Handling.md) - Understanding the event stream
- [**Bidirectional Events**](../Events/05.6%20Bidirectional%20Events.md) - User prompts and permissions
- [**Streaming & Cancellation**](../Events/05.5%20Streaming%20%26%20Cancellation.md) - Stop button implementation
- [**Client Tools**](../Tools/02.3%20Client%20Tools.md) - Browser-side tool execution

## TypeScript Packages

### `@hpd/hpd-agent-client` — Event Streaming SDK

```bash
npm install @hpd/hpd-agent-client
```

Framework-agnostic. Works in Svelte, Vue, vanilla JS, Node.js, or any environment that can make HTTP requests. Choose the transport that fits your backend:

- **SSE** (`SseTransport`) — unidirectional streaming over HTTP; simplest to set up
- **WebSocket** (`WebSocketTransport`) — full-duplex; better for high-frequency bidirectional events

### `@hpd/hpd-agent-headless-ui` — Svelte Component Library

```bash
npm install @hpd/hpd-agent-headless-ui
```

A Svelte 5 component library built on top of `@hpd/hpd-agent-client`. Ships zero CSS — components expose `data-*` attributes for styling. Targets < 20 KB bundle size.

**Available components:**

| Component | Purpose |
|-----------|---------|
| `<Message>` | Single message display |
| `<MessageList>` | Scrollable message history |
| `<ChatInput>` | Text input with send controls |
| `<ToolExecution>` | Tool call progress display |
| `<PermissionDialog>` | Permission approval prompt |
| `<Artifact>` | Code/file artifact display |
| `<BranchSwitcher>` | List and switch conversation branches |
| `<SessionList>` | List and select saved sessions |
| `<SplitPanel>` | Resizable split-panel layout |
| `<Workspace>` | Full workspace layout (session list + chat) |
| `<AudioPlayer>` | Audio playback for voice agents |
| `<Transcription>` | Voice transcription display |
| `<TurnIndicator>` | Agent thinking/turn status |

## See Also

- [**Event Handling**](05%20Event%20Handling.md) - Understanding the event stream
- [**Building Console Apps**](07%20Building%20Console%20Apps.md) - Native .NET console patterns
