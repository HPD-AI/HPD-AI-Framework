# HPD-AI Framework 0.9.0

Release candidate: `50e47820a1676457f8f332a991dfb870012cc831`

Comparison base: [`v0.8.1`](https://github.com/HPD-AI/HPD-AI-Framework/releases/tag/v0.8.1)

This release contains 897 commits across 4,165 files compared with `v0.8.1`
(562,759 insertions and 109,069 deletions). It is a coordinated framework
milestone rather than a small patch release.

## Release Scope

- The primary release is the HPD-Agent package train and the foundation
  packages required by its tested dependency graph.
- HPD-Agent.Studio has a separate Studio release path.
- HPD.Payments remains a separate `0.1.0` product track and is not published by
  the Agent 0.9.0 workflow.
- HPD-Agent.MultiAgent.AspNetCore remains blocked until HPD-Base.AspNetCore and
  HPD-Graph.Base are published.
- The package version metadata is now `0.9.0`. Both `v0.8.0` and `v0.8.1`
  contained `VersionPrefix` 0.6.0, so this change also corrects the stale
  package-version baseline.

## Highlights

### HPD-Agent

- Replaced mutable event registration with source-generated, authoritative
  event composition. Library event modules now compose automatically when an
  application does not provide a composition.
- Added durable goals, planning, questions, explicit subagent results, and
  context handoffs.
- Added hierarchical subagent policy and event delivery, durable fork and
  continuation receipts, authority attenuation, retry recovery, and
  crash-safe restart behavior.
- Unified capability and operation lifecycles, action admission, operation
  ownership, action-scoped authority, canonical schemas, and audit records.
- Ported execution-owned ToolHarness v9, including generated registries,
  cross-assembly activation, local action references, and Native AOT coverage.
- Unified provider authentication, Codex OAuth, provider usage accounting,
  per-model-call usage, and model valuation.
- Added managed speech and audio sessions over LiveKit, including streaming
  speech input, committed turn delivery, interruption flow, Silero VAD, and
  ElevenLabs streaming transcription.
- Added bot integrations for Discord, Slack, Teams, Telegram, and WhatsApp.
- Added the FileSystem ToolHarness and expanded coding, language-server,
  debugging, MCP 2, OpenAPI, evaluation, sandbox, and provider coverage.

### HPD-TUI

- Introduced a retained display-command compositor with revisioned component
  trees, bounded damage tracking, incremental rasterization, frame pacing, and
  structured performance diagnostics.
- Replaced the streaming Markdown path with a source-backed projection that
  preserves source spans, layout identity, and preview metadata during
  incremental updates.
- Added a managed terminal publication state machine with backpressure,
  transactional component lifecycle, scrollback separation, resize recovery,
  and resize-safe shutdown.
- Added conformance, differential ANSI, randomized raster, scaling-budget, and
  interactive subagent-action coverage.

### HPD-Base, Auth, and Graph

- Hardened HPD-Base's graph-owned mutation and transaction authority, durable
  activations, generated indexes, vector execution, SQLite persistence,
  serializer identities, schema ledgers, and Native AOT boundaries.
- Migrated HPD-Auth persistence and activation toward HPD-Base graph contracts;
  expanded authentication, authorization, 2FA, OAuth, admin, audit, Studio,
  and Native AOT coverage.
- Replaced process-local graph workflow and checkpoint authority, added graph
  runtime admission and replacement replay, and consolidated connectors,
  scheduling, Studio adapters, and generated activation contracts.

### HPD-Gateway and Studio

- Added a product-neutral HPD Studio core and unified Studio host/adapters
  across Base, Auth, Graph, Gateway, and Agent.
- Expanded HPD-Gateway with standalone and managed hosting surfaces, secured
  admin/auth integration, YARP forwarding, Microsoft service discovery,
  SQLite control-plane persistence, and Redis-backed distributed admission.
- Added TypeScript Gateway client generation, generated Studio contracts, and
  React Base client foundations.

### HPD-Environment, Media, and Payments

- Added local environment provider host authority, allowlisted executable
  roots, rootless/rootful Podman socket classification, container-host
  readiness, participant lifecycle coordination, and recovery coverage.
- Consolidated realtime media and audio projects, repaired cross-project and
  AOT references, and qualified LiveKit native assets for all six RIDs.
- Landed the separate HPD-Payments product track with formal ledger authority,
  embedded and distributed profiles, SQLite/Postgres/InMemory adapters,
  Stripe and simulator connectors, out-of-process extensions, generators,
  workers, conformance tests, and Native AOT checks.

### Other Package Families

- Updated HPD-RAG ingestion, retrieval, reranking, evaluation, and provider
  integration around the new shared foundations.
- Updated HPD-Extract and HPD-ML dependency and project graphs. HPD-ML removed
  its PJRT and XLA integration.
- Expanded HPD-Events, HPD-OpenApi.Core, HPD-VCS, HPD-RealtimeMedia, and
  serialization compatibility coverage.

## Migration and Compatibility

- Supported modern targets remain `net8.0`, `net9.0`, and `net10.0`. Legacy
  `netstandard2.0` and `net472` support remains disabled.
- HPD.Payments currently requires `net10.0` and C# 14.0.
- Event consumers and custom event modules must use the generated project-level
  event composition. The mutable global event registry is no longer the
  source of truth.
- Tool action schemas are closed by default. Review generated action unions,
  permission-to-authority migrations, invocation modes, and ToolHarness v9
  registration.
- Input interruption has been replaced by explicit execution control. Queued
  input and steering now share the user-input delivery lifecycle.
- Global provider discovery has been removed. Use explicit provider
  registration, the immutable provider catalog, and supported environment or
  dependency-injection authentication.
- OpenAPI consumers must use the OpenAPI.NET v3 model namespace and
  `IOpenApiMediaType` content contracts.
- Markdown consumers must use the source-backed projection contracts rather
  than the former streaming/reparse path.
- Graph, Auth, and Base persistence/activation contracts contain hard
  pre-1.0 cutovers. Review the subsystem migration tests and generated schema
  ledgers when upgrading persisted applications.

## Dependency Changes

- Migrated OpenAPI support to `Microsoft.OpenApi` and
  `Microsoft.OpenApi.YamlReader` 3.11.0.
- Updated the `Microsoft.Extensions.*` platform family to 10.0.13.
- Updated the `Microsoft.Extensions.AI` family to 10.11.0.
- Updated ModelContextProtocol to 2.2.0 and OpenAI to 2.14.0.
- Updated Microsoft.Agents, CodeAnalysis, Graph, Anthropic, DashScope, AWS
  runtime, and other provider/tooling package families.
- Updated ONNX Runtime to 1.31.0, ONNX Runtime GenAI to 0.18.0, PDFiumCore to
  157.0.8100, NLayer to 3.0.0, Markdig to 1.4.2, and LiveKit to 0.1.4.
- Replaced deprecated `AWSSDK.Extensions.Bedrock.MEAI` with
  `AWS.Bedrock.MEAI` 1.1.1.
- `Microsoft.Extensions.AI.Evaluation.NLP` remains on preview package
  `10.11.0-preview.1.26513.7` because no stable release exists.
- `Azure.AI.OpenAI` remains on explicit prerelease exception `2.9.0-beta.1`.

## Package Coverage

- The Agent release workflow now includes the bot packages, FileSystem
  ToolHarness, and LiveKit native assets.
- LiveKit retains the `livekit-ffi/v0.12.60` archives. All six RID library
  hashes were re-qualified; `osx-arm64` is the execution-qualified platform.
- Package identity validation and package-content checks remain release gates.
- Publishing is manually dispatched through `publish-agent-nuget.yml`.
  Creating the Git tag alone does not publish the NuGet packages.

## Validation

The 0.9.0 milestone recorded green net10, net8, and net9 Agent sets; green
HPD-RAG, HPD-TUI, and PDF extraction suites; a zero-known-vulnerability
package audit; and successfully produced Agent and foundation release
packages.

Before publishing, rerun the release gate at the exact tag commit:

- GitHub Actions "Build and Test" for net10, net9, net8, Windows, and macOS.
- `dotnet/eng/nuget/validate-package-identities.sh` for the release project set.
- Release package build and content validation.
- A final NuGet audit against the exact release dependency graph.
