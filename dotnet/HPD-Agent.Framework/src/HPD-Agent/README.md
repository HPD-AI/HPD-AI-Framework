# HPD-Agent.Framework

A middleware-driven agentic AI framework built on Microsoft.Extensions.AI for building intelligent, tool-using agents.

## Thread-scoped event subscriptions

Subscriptions without a thread key observe only events owned by that `Agent` instance. They include
the agent's per-run coordinators, but exclude events originating in independently owned subagents:

```csharp
using var local = agent.Subscribe<TextDeltaEvent>(evt => Console.Write(evt.Text));
```

Use the complete `(SessionId, ThreadId)` key to observe one concrete child invocation:

```csharp
var child = new ThreadKey(childSessionId, childThreadId);
using var exact = agent.Subscribe<TextDeltaEvent>(child, evt => Console.Write(evt.Text));
```

Request hierarchy explicitly to supervise an entire branch at every depth. Sibling branches are
excluded, and each delivery retains its root-to-origin route:

```csharp
using var subtree = agent.SubscribeAny(
    child,
    AgentEventHierarchy.ThreadAndDescendants,
    evt => Observe(evt));
```

Thread-keyed subscriptions exclude threadless events. Live descendant delivery multiplexes events
from independent thread journals; it does not merge their journal cursors. Callbacks run on the
subscription's mailbox pump, publication does not wait for callback completion, and disposal stops
observation without stopping execution or event bubbling. Framework infrastructure that truly needs
every owner must opt into global observation explicitly.

## Install

```bash
dotnet add package HPD-Agent.Framework
```

## Use When

Use `HPD-Agent.Framework` when building an agent with a model client,
source-generated function and ToolHarness capabilities, sessions and threads,
durable events, permissions, middleware, and provider-neutral hosting
composition.

Provider and integration packages stay optional:

- use `HPD-Agent.Providers.*` for model clients;
- use `HPD-Agent.MCP` for Model Context Protocol servers;
- use `HPD-Agent.OpenApi` for external REST APIs described by OpenAPI;
- use `HPD-Agent.AspNetCore` for hosted sessions, committed SSE, and uploads;
- use `HPD-Agent.AspNetCore.OpenApi` to publish the hosted API contract.

## Middleware

`IAgentMiddleware` adds cross-cutting behavior around agent turns, iterations, model
calls, functions, runtime lifecycle, and error handling. Override only the hooks your
middleware needs:

```csharp
using HPD.Agent;
using HPD.Agent.Middleware;

public sealed class RequestAuditMiddleware : IAgentMiddleware
{
    public Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        Console.WriteLine(
            $"{context.AgentName}: {context.UserInputMessages.Count} input message(s)");
        return Task.CompletedTask;
    }
}

await using var agent = await new AgentBuilder(new AgentConfig())
    .WithChatClient(chatClient)
    .WithMiddleware(new RequestAuditMiddleware())
    .BuildAsync();
```

Middleware runs in registration order for `Before*` hooks; `After*` and `OnErrorAsync`
run in reverse order. For wrapper hooks such as `WrapFunctionCallAsync`, the first
registered middleware is outermost. Function middleware scopes also apply to function
wrappers, not only the before/after function hooks.

Middleware referenced from `AgentConfig.Middlewares` is required. A missing registry
entry, unresolved dependency, or constructor failure stops the build with a structured
`AgentRunConfigurationException`.

See the [middleware overview](https://hpd-ai.github.io/HPD-Agent/guides/middleware/overview),
[custom middleware guide](https://hpd-ai.github.io/HPD-Agent/guides/middleware/custom-middleware),
and [lifecycle reference](https://hpd-ai.github.io/HPD-Agent/concepts/middleware-lifecycle).

## Supported targets

The core framework, general hosting, ASP.NET Core host, bots, audio, sandbox,
TUI, MCP, evaluation, multi-agent orchestration, and tool-harness packages
target `net8.0`, `net9.0`, and `net10.0`.

The following packages target `net10.0` only:

- `HPD-Agent.Client`
- `HPD-Agent.AspNetCore.OpenApi`
- `HPD-Agent.MultiAgent.AspNetCore`
- `HPD-Agent.MultiAgent.HpdBase`
- `HPD-Agent.Stores.HpdBase` (`net10.0`)
- `HPD-Agent.Evaluations.HpdBase` (`net10.0`)
- `HPD-Agent.Providers.Cohere`
- `HPD-Agent.Providers.DashScope`
- `HPD-Agent.Providers.Fireworks`
- `HPD-Agent.Providers.Groq`
- `HPD-Agent.Providers.HuggingFace`
- `HPD-Agent.Providers.Mistral`
- `HPD-Agent.Providers.Replicate`
- `HPD-Agent.Providers.Together`

Analyzer packages target `netstandard2.0`. `HPD-Agent.Audio.LiveKit` targets
`net8.0`, `net9.0`, and `net10.0`. Its companion native-assets package targets
`net8.0` and currently supplies the execution-qualified `osx-arm64` LiveKit FFI
artifact. Unsupported runtime identifiers fail closed.

## Audio

Install `HPD-Agent.Audio` plus one or more audio provider packages when the
agent accepts speech, returns synthesized speech, or uses a retained realtime
transport:

```bash
dotnet add package HPD-Agent.Audio
dotnet add package HPD-Agent.Providers.Audio.OpenAI
dotnet add package HPD-Agent.Providers.Audio.ElevenLabs
```

Audio input and output are opt-in through `WithAudio(...)` after the relevant
STT, TTS, or realtime provider has been configured. Assistant audio emits
artifact, stream, playback, interruption, and failure events. Realtime user
transcription emits delta, completion, and structured failure events.

See the [audio integration guide](../HPD-Agent.Audio/HPD-Agent.Audio/README.md)
for batch STT, TTS, realtime, playback, artifact, and diagnostic examples.

## Event composition

Agent events need one immutable application schema so live events, durable thread
journals, replay, and source-generated Native AOT metadata agree. Applications
usually do not configure this manually:

- An executable or a project with `HpdAgentApplication=true` generates and
  auto-registers one complete application composition.
- A library generates and auto-registers its own event module. When no full
  application composition is present, `AgentBuilder` composes the loaded
  generated modules automatically.
- Select with `WithEventApplicationIdentity(...)` only when multiple generated
  applications are loaded into one process.
- Supply `WithEventComposition(...)` for a custom or intentionally core-only
  event surface.

Use the builder's session-store helpers, such as
`WithInMemorySessionStore()` or `WithFileSessionStore(path)`, so the store is
created with the selected composition's codec. A manually created store must use
that same codec.

See [Custom agent events](CUSTOM_EVENTS.md) for defining event contracts,
choosing `[DurableEvent]`, publishing live versus journaled events, and handling
unknown or stale event contracts.

See [Runtime agent extensions](RUNTIME_EXTENSIONS.md) for tenant-owned JSON
schemas, validated live/durable output events, and trusted runtime input
handlers that do not require runtime C# compilation.

## ToolHarness construction

ToolHarnesses have exactly one public constructor and one generated activation
path. Harness settings use `IHarnessConfig<T>`; constructor dependencies are
Agent-scoped services. Invalid constructor shapes and dependency defaults fail
compilation with `HPDHIN009` through `HPDHIN013`.

```csharp
var agent = await new AgentBuilder(agentConfig)
    .WithToolHarness<SearchHarness>(options =>
        options.Configure(new SearchSettings { MaxResults = 20 })
            .UseDependency(searchClient))
    .BuildAsync();
```

See [ToolHarness construction](Tools/TOOL_HARNESS_CONSTRUCTION.md) for settings,
run overrides, dependency ownership, supplied instances, and migration.

## Skills

Use a skill when the model needs focused workflow instructions plus a bounded
set of functions, resources, or scripts that should remain hidden until the
workflow is relevant.

```csharp
public sealed partial class DataTools
{
    [Skill]
    public static Skill Analyze() => Skill.Create(
        "data_analysis",
        "Validates and analyzes datasets.",
        SkillInstructions.FromText("Validate the dataset before analysis."));
}

await using var agent = await new AgentBuilder(new AgentConfig { Name = "data-agent" })
    .WithToolHarness<DataTools>()
    .BuildAsync();
```

Skills can be declared in C#, imported from `SKILL.md` directories, or installed
through a versioned managed store. Package versions are immutable, invalid
reloads retain the last valid capability epoch, and external scripts require a
host-provided `ISkillScriptRunner`.

See the [complete Skills guide](Skills/README.md) for directory metadata,
resources, script contracts, store selection, and
`RefreshCapabilitiesAsync()` reload instructions.

## MCP and OpenAPI integrations

HPD-Agent imports external capabilities through optional packages:

- `HPD-Agent.MCP` consumes MCP servers using Streamable HTTP or stdio.
- `HPD-Agent.OpenApi` turns OpenAPI-described REST operations into agent tools.
- `HPD-Agent.AspNetCore.OpenApi` publishes the application's own ASP.NET Core API
  as an OpenAPI 3.1 document.

All three participate in the same capability catalog, permissions, tool
visibility, and revision lifecycle. MCP tools require permission by default;
OpenAPI tools do not.

See [MCP and OpenAPI integrations](MCP_OPENAPI.md) for package selection,
authentication boundaries, generated-code contracts, Native AOT behavior, and
troubleshooting.

For explicit native result offloading and the bounded `Content` retrieval tool,
see [Native tool outputs and retrieval](Tools/TOOL_OUTPUTS.md).

## Compatibility

HPD-Agent `0.9.5` is pre-1.0 stabilization. Public APIs, serialized events, and
persistence formats remain subject to breaking changes while further compatibility
testing is completed. The 1.0 compatibility commitment begins with `1.0.0`. The
current conversation model is session-owned threads with event-sourced thread history.

## Session Fork Graph

Use `ThreadForkGraph.BuildVisibleForkGroups(...)` when rendering thread/fork
navigation. `Thread.ForkedFrom` is direct lineage; fork groups are semantic
choice points derived from the shared conversation boundary.

See [Session/THREAD_FORK_GRAPH.md](Session/THREAD_FORK_GRAPH.md).
