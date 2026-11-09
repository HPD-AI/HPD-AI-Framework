# HPD-Agent 0.9.5

## Release status

HPD-Agent 0.9.5 is a pre-1.0 stabilization release for additional compatibility
testing. The documented public API, serialized event contracts, persistence
formats, hosted DTOs and route names, and TUI integration contracts are candidates
for 1.0, but breaking changes remain possible before 1.0.0 is released.

## Highlights

- Builder-owned agent and provider lifecycles.
- Source-generated application composition shared by ASP.NET and Native AOT hosts.
- Immutable event catalogs with explicit durable replay facts and live-only
  diagnostics.
- Builder-selected event durability overrides with required-durable protection
  for recovery and authorization facts.
- JSON-shape contract digests, canonical `SUBAGENT_*` discriminators, and
  store-independent delta coalescing.
- Durable runtime extension events.
- Unified permission authorization and sandbox capability escalation.
- Bounded native tool-output projection and retrieval.
- Completed ToolHarness construction, middleware lifecycle, and retry contracts.
- `OnErrorAsync` observes terminal model, function, iteration, turn, input,
  runtime, and thread-fork failures exactly once.
- Skill instances and script capabilities use validated factories, while
  projection, generated-reference, and content-source plumbing stays internal.
- Skill activation lasts for the current message turn; unsupported lifetime
  modes and configuration were removed before the planned 1.0 freeze.
- Configured middleware fails agent construction when a registry entry,
  dependency, or constructor cannot be resolved.
- Function, ToolHarness, and skill middleware scopes apply consistently to
  function wrappers as well as before/after function hooks.
- Provider-composition merging for packages that contribute different client
  families to the same provider backend.
- Managed `HPD-Agent.Audio.LiveKit` packaging with verified `osx-arm64` native
  assets.
- Structured `ProviderErrorInfo` diagnostics for realtime user-transcript
  failures.
- A closed, checked-in audio public API inventory with no exported `V1`/`V2`
  type names.
- Runtime/test plumbing such as fake providers, clocks, deterministic runners,
  and in-memory sinks is internal to `HPD-Agent.Audio`.
- A test-gated NuGet release path covering the client, provider-neutral MultiAgent
  ASP.NET Core routes, and ASP.NET Core OpenAPI and evaluation integrations.
- The coordinated package train releases HPD-Auth Core and ControlPlane before
  HPD Base, then completes the remaining Auth, Base, Graph, and Agent packages
  in dependency order.
- Evaluation consumers get a reduced 1.0-candidate API surface, explicit structured-input
  formatting, interpretation-aware pass rates, interpretation-aware CI policy
  gates, factory-backed deterministic and red-team implementations, consistent
  live evaluator policy defaults, persistence-failure diagnostics, stable
  generated event-module names, and package-level quickstarts for batch, live,
  storage, and hosted evaluation workflows.
- MCP and OpenAPI packages expose only their documented consumer surfaces.
  Generated-code bridges remain public for source generation but are hidden from
  consumer IntelliSense; raw MCP runtimes and projection serializers are internal.
- MCP and OpenAPI packages ship runnable setup, permission, authentication, and
  response-shaping guidance instead of placeholder package documentation, plus a
  central integration and public-API-boundary guide.
- OpenAPI loading emits structured skipped-operation warnings and fails fast when
  a source produces no operations, with explicit `AllowEmptyCatalog` opt-in.
- Retry middleware now uses the selected provider's error handler for model
  failures and package-owned handlers for function sources; OpenAPI responses use
  their status and `Retry-After` classification automatically.
- Native AOT MCP publish-and-run validation is a dedicated CI gate.
- Bot adapters now preserve complete webhook responses, support mixed-host
  registry mapping, expose consistent logging, and ship package-level setup guides.
- Skill names are validated against cross-provider function-name constraints,
  and malformed directory metadata reports the exact script and field.
- Optional `net10.0` HPD Base adapters provide restart-durable agent
  definitions, session/thread journals, streaming-delta recovery, evaluation
  scores and runs, immutable dataset versions, SCD-2 case history, and diffs.
  Dataset persistence uses explicit AOT-safe codecs rather than reflection.

## Error detail visibility

`ErrorHandlingConfig.IncludeDetailedErrorsInChat` defaults to `true`. Function and
model errors therefore include exception detail intended to help the model recover.
Production-facing agents whose tools can produce secrets, filesystem paths,
connection strings, or sensitive implementation details should explicitly set the
option to `false`.

## Deliberate package exclusions

- `HPD-Agent.Studio` follows the separate Studio release process.
- `HPD-Agent.Audio.LiveKit` currently supports only the qualified `osx-arm64`
  runtime. Unsupported runtime identifiers are packaged but fail closed.

## Prerelease upstream dependencies

`HPD-Agent.Providers.OpenAI` and `HPD-Agent.Providers.AzureAI` retain
`Azure.AI.OpenAI` `2.9.0-beta.1`. The latest stable Azure SDK, `2.1.0`, does not
provide the service-version and pipeline options exposed by these providers.
`HPD-Agent.Evaluations` retains
`Microsoft.Extensions.AI.Evaluation.NLP` `10.11.0-preview.1.26513.7` because no
stable release of that package exists. These upstream prerelease dependencies
are intentional for 0.9.5 and the planned 1.0.0 release. HPD-owned public
contracts remain documented but pre-1.0.

## Deprecated packages

- `HPD-Agent.ToolHarness.FileSystem` is deprecated. It remains available for a
  migration window but is not a supported workspace-isolation or
  command-execution security boundary. Public harness types emit compiler
  warning `CS0618`. Use `HPD-Agent.ToolHarness.Coding` for trusted coding
  workspaces or an HPD sandbox runtime for untrusted workloads.

## Upgrade

Applications should create agents through `AgentBuilder`. Consumers using the
former public direct constructor must migrate provider, middleware, content-store,
and resource ownership to builder-owned construction.

The NuGet package identifiers for HPD Base use the reserved-safe `HPD-Base.*`
family, for example `HPD-Base`, `HPD-Base.AspNetCore`, and `HPD-Base.Auth`.
Assembly names and `HPD.Base` namespaces are unchanged.

The durable `ITERATION_CONTEXT_SNAPSHOT` event was removed before the 1.0
freeze. It was diagnostic-only and did not participate in compaction recovery
or thread projection. Existing journals that contain this discriminator
intentionally fail closed rather than carrying a compatibility decoder into
1.0. Use model/provider logs or application-defined live diagnostics when the
provider-bound context must be observed.

Audio consumers should remove `V1` and `V2` suffixes from HPD audio type names.
For example, use `LiveAudioSessionStartRequest`,
`EndpointDecisionProjection`, and `VoiceActivityRequest` instead of their former
version-suffixed spellings. These are source-level renames: serialized event,
payload, and schema version identifiers are unchanged.

Consumers of runtime extension durable events should include
`HPD.Agent.RuntimeAgentDurableEvent` in their application event composition and
persistence compatibility decisions.

Applications that previously relied on every core diagnostic entering the
thread journal must subscribe to those live events or define their own durable
audit events. Retry, snapshot, skill progress, content-storage mechanics, and
other re-derivable diagnostics no longer inflate canonical thread history.
