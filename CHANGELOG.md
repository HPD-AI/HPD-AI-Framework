# Changelog

All notable changes to the HPD AI Framework are documented in this file.

## HPD-Agent 1.0.0 - 2026-11-08

### Stable

- HPD-Agent and its release-train packages now start at `1.0.0` and follow
  semantic versioning for public APIs, serialized events, and supported
  persistence contracts.
- Hosted DTOs, route names, service contracts, persistence projections, and TUI
  runtime interfaces documented for 1.0 are compatibility surfaces.
- Error formatting continues to include detailed exception messages by default.
  Applications that expose model-facing errors should set
  `IncludeDetailedErrorsInChat` to `false` when tool exception text is sensitive.

### Changed

- Agent applications construct runtimes through `AgentBuilder`; the prepared
  `Agent` constructor is internal so ownership and provider lifecycle remain
  builder-managed.
- Runtime-defined durable extension events are explicitly recorded in the
  first-party durable-event compatibility inventory.
- Event durability now distinguishes canonical recovery and replay facts from
  live diagnostics; execution snapshots, retries, content-storage mechanics,
  and skill/read/script activity remain observable without entering thread
  journals.
- `AgentBuilder` can override overridable event durability before the session
  store is created. Recovery-critical events marked required-durable reject
  live-only overrides.
- Event contracts carry generated JSON-shape digests. Public subagent
  discriminators use the canonical `SUBAGENT_*` prefix.
- Streaming text and reasoning deltas are coalesced above the session-store
  abstraction, including for custom stores that do not implement
  `IThreadDeltaStore`.
- Event and input envelopes require wire version `1.0`; missing, null, or
  unsupported versions fail closed.
- Application capability composition is source-generated and shared across
  ASP.NET and Native AOT hosts.
- ToolHarness construction, retry lifecycle, permission authorization, sandbox
  escalation, and native tool-output retrieval use the finalized 1.0 contracts.
- Middleware `OnErrorAsync` now observes terminal model, function, iteration,
  message-turn, input, runtime, and thread-fork failures exactly once.
- Configured middleware is required: missing registry entries, unresolved
  dependencies, and constructor failures now stop agent construction.
- Function middleware scopes apply consistently to `BeforeFunctionAsync`,
  `WrapFunctionCallAsync`, and `AfterFunctionAsync`; skill scopes use typed
  capability relationships.
- Provider composition now merges compatible backend contributions from
  packages that add different client families to the same provider.
- Realtime user-transcript failures expose structured provider code, category,
  retryability, status, request ID, and retry-delay details.
- `HPD-Agent.Audio.LiveKit` is packaged with its managed transport and verified
  native assets.
- Stable 1.0 packages retain the required prerelease Azure OpenAI SDK and NLP
  evaluation package; the prerelease scope is limited to those upstream
  dependencies and is documented in the release notes.
- The `HPD-Agent.Audio` public surface is now explicitly baselined. Runtime
  clocks, ID factories, deterministic runners, fake providers, in-memory
  projection helpers, and low-level output implementation services are internal.
- Version suffixes were removed from audio and audio-provider type names and
  implementation file names. Protocol-version information remains in schema
  identifiers, payload tags, and genuine legacy compatibility contracts.

### Breaking

- Removed the durable `ITERATION_CONTEXT_SNAPSHOT` event and its snapshot
  models. It duplicated model-bound context and schemas without participating
  in execution recovery or thread projection. Journals containing this
  discriminator are intentionally not replay-compatible with 1.0.

### Fixed

- Generated ASP.NET bot webhook routes now execute adapter responses, preserving
  status codes, headers, and bodies for platform verification and error signaling.
- `MapHPDBots()` now lets each adapter apply its own default mapping, including
  the Microsoft 365 Agents SDK endpoint used by Teams.
- Bot streaming failures are reported through `ILogger` without logging user
  message content to the console.
- Bot NuGet packages now include package-specific setup documentation.

### Deprecated

- `HPD-Agent.ToolHarness.FileSystem` is deprecated. It remains available for
  migration but is not a supported workspace-isolation or command-execution
  security boundary. New applications should use
  `HPD-Agent.ToolHarness.Coding` for trusted coding workspaces or an HPD sandbox
  runtime for untrusted workloads.

### Release and validation

- NuGet publication now requires the reusable cross-platform build-and-test gate.
- The Agent package train includes the client, provider-neutral MultiAgent
  ASP.NET Core routes, and ASP.NET Core OpenAPI and evaluation integrations.
- `HPD-Agent.MultiAgent.AspNetCore` is provider-neutral and does not depend on
  HPD Base. The optional `HPD-Agent.MultiAgent.HpdBase` adapter remains gated
  on published `HPD-Base.AspNetCore` and `HPD-Graph.Base` dependencies.
  `HPD-Agent.Studio` retains its separate release path.

## 0.9.2 - 2026-11-02

### Added

- Declarative MCP server and OAuth configuration through `AgentConfig`.
- Source-generated permission descriptors and MCP capability-to-permission resolution.
- Inheritable tool-harness capabilities with centralized activation and sub-agent configuration overrides.
- Function-level capability collapsing through generated metadata and selectors.
- Unified declarative workflow runtime with typed output contracts, graph-host bindings, recovery validation, and node result metadata.
- Structured-output resolution and canonical tool-argument handling across nested agent projections.
- Scoped command, debugging, exploration, and tool-call activity state, including turn-scoped sandbox capability approval.
- A single-input `Release HPD Agent and Dependencies` workflow that publishes the complete Agent dependency train in dependency order.

### Changed

- HPD-Environment now owns process-isolation planning contracts, sandbox plans, domain-pattern parsing, and the sandbox isolation compiler/planner implementation.
- HPD-Agent now consumes those contracts from HPD-Environment; Environment no longer depends on Agent packages or Agent build targets.
- Environment Apple Virtualization packages now have a clean dependency direction: Environment must be published before Agent.
- Agent configuration serialization now shares the framework YAML/JSON configuration bridge.

### Fixed

- Workflow execution now drains pending event inboxes before reporting completion.
- Execute-command completion and output handling now preserve terminal results during streaming and completion transitions.
- YAML and JSON configuration serialization now use a consistent document and bridge representation.

### Breaking

- Sandbox planning contracts including `SandboxExecutionContext`, `SandboxEnforcementLocation`, `SandboxPlanEnvelope`, `ISandboxPlanner`, and `SandboxIsolationPlan` moved from `HPD.Agent.Sandbox.ProcessIsolation` to `HPD.Environment.Contracts`.
- `DomainPattern` and `DomainPatternKind` moved from `HPD.Agent.Sandbox.Policy` to `HPD.Environment.Contracts`.
- `SandboxIsolationPlanner` and `SandboxIsolationCompiler` moved from `HPD.Agent.Sandbox.ProcessIsolation` to `HPD.Environment.Runtime`.
- Consumers of those types must update namespace imports and publish HPD-Environment before HPD-Agent.
- The serialized sandbox-plan schema identifier remains `hpd.agent.sandbox.plan` for wire compatibility.
