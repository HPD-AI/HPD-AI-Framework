# Changelog

All notable changes to the HPD-Agent Framework are documented here.

---

## [v0.3.0] — Unreleased

### Breaking Changes

- **`AgentSession` → `Session` + `Branch`** — Split into `Session` (metadata) and `Branch` (message history). All `AgentSession` references must be updated.
- **`AgentRunOptions` → `AgentRunConfig`** — Renamed across 100+ call sites.
- **`ISessionStore` slimmed** — ~25 members reduced to ~10. Custom implementations must be updated.
- **Durable execution removed** — `DurableExecutionService`, `ExecutionCheckpoint`, `PendingWrite`, and related types deleted. Crash recovery now uses `UncommittedTurn`.
- **`HPD-Agent.Memory` removed** — `DynamicMemory` / `StaticMemory` replaced by the unified `IContentStore` interface.

### New Features

- **`UncommittedTurn` crash recovery** — Lightweight delta-based buffer (~10–20 KB vs ~100 KB) replacing the entire checkpoint subsystem. Three new `ISessionStore` methods: `Load/Save/DeleteUncommittedTurnAsync`.
- **`IContentStore`** — Unified session-scoped asset storage abstraction.
- **V3 Branch Tree** — Atomic branch operations, referential integrity, recursive deletion (`AllowRecursiveBranchDelete`).
- **Secrets caching** — TTL-based caching with `ExpiresAt` resolution.
- **Scoped middleware state** — `StateScope`, `MiddlewareStateFactory`, `MiddlewareStateAttribute` for per-scope isolation with automated save/restore.
- **Image content support** — Multimodal agent turns via new image content middleware.
- **OTel tracing** — `TracingObserver` maps agent events to spans (`agent.turn → agent.iteration → agent.tool_call`). `AgentBuilder.WithTracing()` convenience method.
- **`ObserverDispatcher`** — Per-observer FIFO channel for ordered, race-free event delivery.
- **Evaluation framework** — New `HPD-Agent.Evaluations` project with judge-agent integration and loop-prevention flags.
- **`HPD.VCS`** — New versioning library, now properly tracked (was a broken submodule).
- **OpenAPI support** — New `HPD-Agent.OpenApi` and `HPD.OpenApi.Core` packages.
- **Toolkit MCP exposure** — Toolkits can be served directly as MCP servers.
- **Plan mode refactor** — `AgentPlan`/`Manager`/`Store` replaced by `PlanModePersistentState`. `CollapseAttribute` added for toolkit organization.
- **HPD.Graph** — Port-based routing, lazy cloning (`CloningPolicy`), artifact registry (`Materialize/Backfill`), partition-aware execution, temporal operators (`Delay`, `Schedule`, `RetryPolicy`), polling/sensor pattern, map node partitioning, input validation schema builders.
- **New headless UI components** — `branch-switcher`, `message-actions`, `message-edit`, `session-list`, `workspace`, split panel.
- **`hpd-agent-client` refactor** — Improved error and session type exports.

### Bug Fixes

- Fixed shallow copy bug in `HPD.Graph` context propagation.
- Fixed `Context.Tags` isolation in port-based routing.
- Fixed reasoning content coalescing during streaming.
- Fixed provider assembly resolution issues.
- Fixed intermittent CI suspension test timing failures.

### Infrastructure

- CI workflows updated: `.sln` → `.slnx`, VitePress docs deploy fix, updated NuGet publish project list.
- Middleware message flow: eliminated defensive copying, middleware now operates on shared mutable state with scoped ownership.
- Source generator: now supports both instance and static skill/toolkit methods.
- Repo restructured to `dotnet/src/` + `dotnet/test/` layout with centralized `Directory.Build.props`.
- ~5,800 lines of dead code removed.

---

## [v0.2.0] — January 2026

### Breaking Changes

- **Package renames** — `HPD-Agent.Plugins.*` → `HPD-Agent.Toolkit.*`, `FileSystemPlugin` → `FileSystemToolkit`, all `Plugins` namespaces → `Toolkit`.
- **Audio overhaul** — `AudioPipelineConfig` deleted, replaced with modular `AudioConfig`. Audio split into STT / TTS / VAD modules with new per-capability provider factories.
- **Provider module pattern** — All providers now require explicit `*ProviderConfig`, JSON serialization contexts, and builder extensions. Auto-discovery replaced with explicit module registration. Affects: Anthropic, OpenAI, AzureAIInference, Bedrock, GoogleAI, HuggingFace, Mistral, Ollama, OnnxRuntime, OpenRouter, AudioProviders.OpenAI, AudioProviders.ElevenLabs.
- **Middleware** — Removed `ContainerErrorRecoveryMiddleware`, refactored `ContainerMiddleware`.

### New Features

- **New packages** — `HPD.Events`, `HPD.Graph.Abstractions`, `HPD.Graph.Core`, `HPD.MultiAgent`, `HPD-Agent.Providers.AzureAI`.
- **Asset management** — `IAssetStore`, `InMemoryAssetStore`, `LocalFileAssetStore`, `AssetUploadMiddleware`.
- **Multi-target framework** — Now targets .NET 8.0, 9.0, and 10.0 with CI matrix builds across Linux / Windows / macOS.
- **Multi-agent workflows** — Subagent/multiagent chat client inheritance, event bubbling, `AgentId`/`ParentAgentId` hierarchy, deferred agent building, workflow events.
- **Provider enhancements** — `AnthropicSchemaFixingChatClient`, detailed error handlers across all providers.
- **Middleware enhancements** — Automated state saving, improved scoping, tool visibility management, circuit breaker improvements.

### Bug Fixes

- Fixed container middleware scoping issues.
- Fixed audio provider registration.
- Fixed session serialization edge cases.

### Deprecated

- **`HPD-Agent.Providers.AzureAIInference`** — Use `HPD-Agent.Providers.AzureAI` instead.

---

## [v0.1.1] — December 2025

> See git tag `v0.1.1` for full history.
