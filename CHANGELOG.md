# Changelog

All notable changes to the HPD AI Framework are documented in this file.

## HPD-Agent 0.9.5 - 2026-11-09

### Pre-1.0 release

- HPD-Agent and its coordinated release-train packages now target `0.9.5`.
  APIs, serialized events, persistence contracts, and hosted integration
  surfaces remain pre-1.0 while compatibility testing continues.
- Hosted DTOs, route names, service contracts, persistence projections, and TUI
  runtime interfaces are tracked 1.0 compatibility candidates.
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
  escalation, and native tool-output retrieval use the planned 1.0 contracts.
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
- The 0.9.5 packages retain the required prerelease Azure OpenAI SDK and NLP
  evaluation package; the prerelease scope is limited to those upstream
  dependencies and is documented in the release notes.
- The `HPD-Agent.Audio` public surface is now explicitly baselined. Runtime
  clocks, ID factories, deterministic runners, fake providers, in-memory
  projection helpers, and low-level output implementation services are internal.
- Version suffixes were removed from audio and audio-provider type names and
  implementation file names. Protocol-version information remains in schema
  identifiers, payload tags, and genuine legacy compatibility contracts.
- The evaluation consumer surface is limited to builder extensions, evaluators,
  datasets, reports, persistence contracts, events, and documented value types.
  Live evaluation middleware, judge-trace middleware, generated state
  registries, and source-generator diagnostics are internal.
- Deterministic and NLP evaluator implementations are factory-backed behind
  `Eval`, while built-in red-team plugins and strategies are created through
  stable factories. Their concrete implementations remain internal.
- A core evaluator registry now supplies stable IDs, default policies, judge
  requirements, metric names, and required contexts to the hosted catalog.
- Evaluation metrics now honor explicit `EvaluationMetricInterpretation.Failed`
  values before applying HPD's numeric pass-rate fallback.
- Structured batch inputs require an explicit `RunEvalsOptions<T>.InputFormatter`;
  HPD no longer silently sends `ToString()` output to the agent.
- Live evaluation defaults deterministic evaluators to `MustAlwaysPass` and
  model-judged or custom evaluators to `TrackTrend`. Global judge fallback is
  configured through `UseEvaluationJudgeConfig`.
- Evaluation packages now have a dedicated public API baseline and runnable
  package documentation.
- Optional HPD Base adapters now provide restart-durable agent definitions,
  session metadata, atomic thread journals, streaming-delta recovery,
  evaluation scores/runs, immutable dataset versions, case history, and diffs.
  The adapters target `net10.0` to match HPD Base.
- Durable dataset storage requires source-generated input metadata through
  `DatasetCodec<TInput>`, avoiding reflection-based generic persistence.
- Live evaluation publishes `EVAL_PERSISTENCE_FAILED` when score persistence
  fails instead of silently losing the result.
- Evaluation HTTP endpoints distinguish validation errors (`400`), missing
  stores (`503`), cancellation, and unexpected storage failures (`500`).
- Generated event-module providers now use stable module-derived names rather
  than hash-suffixed public type names.
- The skills consumer surface is limited to authoring and extension contracts.
  Capability projection, generated function references, content-store sources,
  and script argument construction are internal. Validated skill and script
  instances can only be produced through their factories.

### Breaking

- Removed the durable `ITERATION_CONTEXT_SNAPSHOT` event and its snapshot
  models. It duplicated model-bound context and schemas without participating
  in execution recovery or thread projection. Journals containing this
  discriminator are intentionally not replay-compatible with the planned 1.0
  contract.
- Removed unsupported model-iteration and session skill activation lifetimes,
  along with the redundant `SkillRuntimeOptions.ActivationLifetime` setting.
  Skills activate for the remainder of the current message turn.

### Fixed

- Generated ASP.NET bot webhook routes now execute adapter responses, preserving
  status codes, headers, and bodies for platform verification and error signaling.
- `MapHPDBots()` now lets each adapter apply its own default mapping, including
  the Microsoft 365 Agents SDK endpoint used by Teams.
- MCP and OpenAPI implementation types no longer leak into the 1.0-targeted consumer
  surface. Generated-code bridges are hidden from IntelliSense, and package docs
  now provide complete setup, permission, authentication, and response-shaping
  examples. A central MCP/OpenAPI guide documents package selection, public API
  boundaries, Native AOT, and troubleshooting.
- OpenAPI source loading now emits structured warnings for skipped operations and
  fails with `OpenApiSourceProducedNoOperations` instead of silently producing an
  empty tool catalog. `AllowEmptyCatalog` provides an explicit opt-in.
- Retry middleware now resolves the selected provider error handler at build time
  and routes OpenAPI functions through `OpenApiErrorHandler` for status and
  `Retry-After` classification.
- Bot streaming failures are reported through `ILogger` without logging user
  message content to the console.
- Bot NuGet packages now include package-specific setup documentation.
- Skill declarations reject provider-unsafe model names at runtime and with
  `HPDSKILL019` during source generation.
- Malformed directory-skill metadata now identifies the exact skill file,
  script path, and missing or invalid field.

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
- The release train now publishes HPD Base and `HPD-Graph.Base` between the
  shared foundations and Agent packages, waits for each version to become
  available on NuGet, and then publishes all three `net10.0` HPD Base Agent
  adapters against those exact dependency versions.
- The composed release publishes Auth Core and ControlPlane before Base, then
  publishes the remaining Auth packages after Base. This preserves the real
  dependency direction without requiring an externally supplied Auth version.
- `HPD-Agent.MultiAgent.AspNetCore` remains provider-neutral. Its optional
  `HPD-Agent.MultiAgent.HpdBase` adapter is included after the coordinated
  HPD Base and HPD Graph releases. `HPD-Agent.Studio` retains its separate
  release path.

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
