# HPD Coding Harness Debugging Subsystem: Detailed Implementation Proposal

Status: Canonical implementation architecture  
Audience: HPD Agent framework and harness maintainers  
Owning projects: `HPD-Agent.Harness.Coding` and `HPD-Agent.Harness.Coding.SourceGenerator`  
Engineering tool: `eng/HPD-Agent.DebugProtocol.CodeGen`

## 1. Executive summary

This proposal defines the debugging subsystem of the HPD Coding tool harness. It will let an agent
launch or attach to programs, configure breakpoints, control execution, inspect stopped state, work
with advanced/native debugging facilities, and receive asynchronous debugger state changes through
HPD agent events. Debugging has its own folders and namespaces, but it is compiled, dependency-
registered, versioned, and shipped through the existing Coding harness rather than as a separate
product assembly or default model-facing harness.

The implementation will use the Microsoft Debug Adapter Protocol (DAP) as its canonical protocol contract. It will use the `oh-my-pi` coding-agent debugger as a reference for real adapter behavior, transport irregularities, session races, and semantic debugging ergonomics. It will not copy `oh-my-pi`'s process-global session manager or restrict HPD to the subset of DAP implemented there. HPD already provides runtime scoping, invocation context, background resources, event publication, content storage, permissions, dependency injection, and source generation; the design will reuse those facilities.

The architecture has nine explicit boundaries:

1. Adapter attributes declare facts known at compile time.
2. The adapter catalog generator validates those declarations and emits immutable metadata plus direct DI factory resolvers.
3. An explicit engineering code-generation tool consumes the pinned official DAP schema and updates the checked-in complete AOT-safe wire model and typed request descriptors.
4. DI-backed `IDebugAdapterFactory` services perform environment-, endpoint-, and policy-bound runtime resolution.
5. `DebugProtocolClient` implements framing, typed correlation, events, reverse requests, cancellation, and transport failure handling over a transport-neutral contract.
6. `IDebugSessionManager` owns authorized debug-session trees within one HPD agent runtime.
7. `DebugSessionHandle` represents each long-lived root debugging operation in HPD's background-handle system.
8. A presentation-neutral semantic service exposes typed debugger operations over the session manager.
9. A separate generated `DebugToolHarness` class exposes those operations; skills owned by the
   already-collapsed `CodingToolHarness` reference and selectively reveal its functions.

The central design rule is:

> The official DAP schema owns protocol truth; generated declarations own stable catalog metadata; DI factories own adapter-specific runtime resolution; HPD Environment owns process execution and isolation; HPD Agent owns authorization, lifecycle, persistence, and agent integration.

## 2. Goals

The implementation must:

- Support a broad, capability-correct DAP client rather than a debugger-specific API.
- Make common adapters cheap to add and difficult to misconfigure.
- Avoid reflection in normal registration and invocation paths.
- Remain compatible with Native AOT and trimming.
- Isolate debug state per agent runtime rather than per process.
- Represent a root session and adapter-created child sessions as one debug tree.
- Preserve adapter output categories and meaningful DAP state changes.
- Publish typed HPD agent events for asynchronous debugger activity.
- Use `FunctionExecutionContext` as the boundary between tool calls and runtime services.
- Use HPD background handles and tasks for long-lived session ownership and observation.
- Expose common and advanced debugging behavior through typed semantic operations rather than raw
  protocol dispatch.
- Support host overrides and custom adapters without editing the built-in registry.
- Use HPD Environment capabilities for every adapter process rather than launching processes directly.
- Bind remote transports to host-authorized registered endpoints rather than accepting raw model-supplied addresses.
- Generate command/argument/response-safe protocol descriptors instead of serializing arbitrary request objects.
- Provide deterministic shutdown, bounded buffers, timeouts, and cancellation.
- Keep adapter-specific conditionals out of the shared protocol and session layers.

## 3. Non-goals

The implementation will not:

- Implement a graphical debugger UI inside the core Coding assembly.
- Implement a debugger or runtime itself.
- Replace adapter-specific launch configuration with one universal strongly typed schema.
- Guarantee that every adapter supports every DAP request.
- Treat arbitrary adapter output or telemetry as trusted agent instructions.
- Persist live protocol connections across process restarts.
- Use middleware to infer debugging activity from unrelated coding tools.
- Generate the DAP state machine or semantic operations from adapter declarations.
- Use reflection-based assembly scanning to discover adapter factories or event types.
- Accept arbitrary model-provided adapter commands, environment variables, endpoints, or custom DAP requests.

## 4. Sources of truth

### 4.1 Official DAP repository

The canonical JSON schema and generated specification define:

- Message framing and base protocol rules.
- Requests, responses, events, and reverse requests.
- Initialization sequencing.
- Client and adapter capability negotiation.
- Request-specific data contracts.
- Compatibility rules for optional features.

Protocol behavior must be validated against this source. If a reference implementation disagrees with the specification, the specification wins unless a documented compatibility workaround is required for deployed adapters.

### 4.2 `oh-my-pi`

The reference implementation provides empirical guidance for:

- Adapter commands, arguments, file types, root markers, and launch defaults.
- Stdio, TCP server, Unix socket, and callback transport behavior.
- JavaScript debug-server discovery.
- Delve platform differences.
- Launch/configuration handshake races.
- Event subscription ordering.
- Reverse `runInTerminal` and `startDebugging` behavior.
- Child-session trees and breakpoint propagation.
- Request, write, connection, and stop-capture timeouts.
- Bounded output retention and lifecycle cleanup.
- A practical semantic debugging vocabulary.

It is not the complete protocol contract. Missing requests and ignored events must not be mistaken for intentionally unsupported DAP features.

### 4.3 HPD Agent runtime

The implementation must reuse:

- `FunctionExecutionContext`
- `IRuntimeCapabilityRegistry`
- `IAgentBackgroundTaskRegistry`
- `IAgentBackgroundHandleRegistry`
- `IBackgroundHandle` and its readable/stoppable/artifact variants
- `AgentEvent` publication and request/response event flows
- `ToolResultMetadata`
- `IContentStore`
- Harness source generation and capability metadata
- Permission middleware and `[RequiresPermission]`
- Dependency injection and secret/configuration resolution
- HPD Environment `IProcessProvider`, `ProcessInvocationSpec`, and
  `IProcessInvocationHandle`
- Environment filesystem, network, identity, resource-limit, and process-tree policy
- Host-owned endpoint and credential/authority resolution

## 5. High-level architecture

```text
Adapter declaration classes
        |
        v
DebugAdapterSourceGenerator               Official DAP schema
        |                                          |
        v                                          v
Generated catalog metadata + DI resolver   DebugProtocolModelGenerator
        |                                          |
        v                                          v
DI-backed IDebugAdapterFactory             typed wire model/descriptors
        |                                          |
        +----------------+-------------------------+
                         v
              authorized launch plan
                         |
                         v
              IDebugProtocolTransport
                 +-------+--------+
                 |                |
                 v                v
       HPD Environment stdio   approved socket
                 |                |
                 +-------+--------+
                         v
                DebugProtocolClient <---- DAP ---- Debug adapter
                        |
                        v
               IDebugSessionManager
                        |
            +-----------+------------+
            |                        |
            v                        v
     DebugSessionTree         DebugSessionHandle
            |                        |
            v                        v
     typed AgentEvents       HPD background registry
            ^                        ^
            |                        |
            +-------- DebugToolHarness --------+
                     receives
              FunctionExecutionContext
                         ^
                         |
          CodingToolHarness skills reference
             selected generated functions
```

## 6. Existing-project integration and file layout

```text
src/HPD-Agent.Harness/HPD-Agent.Harness.Coding/
  HPD-Agent.Harness.Coding.csproj
  CodingHarness.cs
  CodingHarness.Prompt.cs

  Debugging/
    README.md
    DebugToolHarness.cs
    DebugToolHarness.Lifecycle.cs
    DebugToolHarness.Breakpoints.cs
    DebugToolHarness.Execution.cs
    DebugToolHarness.Inspection.cs
    DebugToolHarness.StateMutation.cs
    DebugToolHarness.Native.cs
    DebugToolHarness.Advanced.cs
    Skills/
      CodingHarness.DebuggingSkills.cs

  Debugging/Adapters/
    DebugPyAdapter.cs
    GdbAdapter.cs
    LldbDapAdapter.cs
    CodeLldbAdapter.cs
    NetCoreDbgAdapter.cs
    DelveAdapter.cs
    JavaScriptDebugAdapter.cs
    RubyDebugAdapter.cs

  Debugging/Attributes/
    DebugAdapterAttributes.cs

  Debugging/Definitions/
    DebugAdapterDescriptor.cs
    DebugAdapterCatalogEntry.cs
    DebugAdapterCatalog.cs
    IDebugAdapterCatalogProvider.cs
    DebugAdapterOptions.cs
    DebugAdapterEnums.cs
    DebugAdapterProvenance.cs
    DebugAdapterTrustDecision.cs

  Debugging/Discovery/
    IDebugAdapterFactory.cs
    StandardDebugAdapterFactory.cs
    DebugAdapterFactoryResolver.cs
    IDebugAdapterToolResolver.cs
    DebugAdapterToolResolver.cs
    DebugAdapterSelector.cs
    DebugAdapterAvailabilityCache.cs

  Debugging/Configuration/
    DebugLaunchPolicy.cs
    DebugConfigurationComposer.cs
    DebugEnvironmentOverridePolicy.cs
    IDebugEndpointResolver.cs
    DebugEndpointDescriptor.cs

  Debugging/Runtime/
    DebugRuntimeBinding.cs
    DebugSessionStarter.cs
    DebugProcessInvocationFactory.cs
    DebugRuntimeBindingState.cs

  Debugging/Protocol/
    DebugProtocolClient.cs
    IDebugProtocolTransport.cs
    DebugEnvironmentProcessTransport.cs
    DebugTcpTransport.cs
    DebugUnixSocketTransport.cs
    InMemoryDebugTransport.cs
    DebugEnvironmentOutputPump.cs
    DebugProtocolFramer.cs
    DebugProtocolPendingRequest.cs
    Generated/DebugProtocolModels.g.cs
    Generated/DebugProtocolDescriptors.g.cs
    Generated/DapJsonContext.g.cs

  Debugging/Sessions/
    IDebugSessionManager.cs
    DebugSessionManager.cs
    DebugSession.cs
    DebugSessionTree.cs
    DebugSessionState.cs
    DebugBreakpointStore.cs
    DebugOutputBuffer.cs
    DebugProgressRegistry.cs

  Debugging/Handles/
    DebugSessionHandle.cs
    DebugSessionObserver.cs

  Debugging/Events/
    DebugSessionEvents.cs
    DebugStateEvents.cs
    DebugProgressEvents.cs
    CodingDebugEventSerialization.cs
    CodingDebugJsonContext.cs
    DebugEventPublisher.cs
    DebugHostRequestBroker.cs

  Debugging/Models/
    Requests/
      DebugInitialConfiguration.cs
    Results/
    Snapshots/
    DebugMetadataKeys.cs

src/HPD-Agent.Harness/HPD-Agent.Harness.Coding.SourceGenerator/
  HPD-Agent.Harness.Coding.SourceGenerator.csproj
  Debugging/DebugAdapterSourceGenerator.cs
  Debugging/Analysis/DebugAdapterAnalyzer.cs
  Debugging/Analysis/DebugAdapterInfo.cs
  Debugging/Diagnostics/DebugAdapterDiagnostics.cs
  Debugging/Generation/DebugAdapterRegistryGenerator.cs

eng/HPD-Agent.DebugProtocol.CodeGen/
  HPD-Agent.DebugProtocol.CodeGen.csproj
  Program.cs
  DebugProtocolModelGenerator.cs
  Schema/DapSchemaReader.cs
  Generation/DapWireModelGenerator.cs
  Generation/DapDescriptorGenerator.cs
  Generation/DapJsonContextGenerator.cs
  Generation/DapFeatureInventoryGenerator.cs
```

`DebugToolHarness` is a separate generated tool-harness class, not a partial extension of
`CodingToolHarness`. It lives in the same runtime assembly and uses
`HPD.Agent.ToolHarness.Coding.Debugging`; implementation types use narrower child namespaces such as
`.Protocol`, `.Sessions`, and `.Adapters`. Generator implementation types use
`HPD.Agent.ToolHarness.Coding.SourceGenerator.Debugging`. Debugger internals are `internal` unless a
host extension contract or generated harness reference requires a public surface. The engineering
code-generation tool remains separate because it is build/repository tooling, not a shipped harness
assembly.

### 6.1 Skill-owned registration and nested disclosure

`CodingToolHarness` remains the explicitly registered, outer `[Collapse]` harness. Its debugging
skills reference model-callable members declared by `DebugToolHarness` with symbol-analyzable
capabilities:

```csharp
[Skill]
public Skill DebugExecution() => Skill.Create(
    name: "debug_execution",
    description: "Launch, attach, and control a debug session.",
    instructions: DebugSkillInstructions.Execution,
    capabilities:
    [
        SkillCapabilities.Function<DebugToolHarness>(nameof(DebugToolHarness.Launch)),
        SkillCapabilities.Function<DebugToolHarness>(nameof(DebugToolHarness.Attach)),
        SkillCapabilities.Function<DebugToolHarness>(nameof(DebugToolHarness.Continue)),
        SkillCapabilities.Function<DebugToolHarness>(nameof(DebugToolHarness.Step))
    ]);
```

The existing HPD source generator emits `CodingToolHarness.GetReferencedToolHarnesses()` and
`GetReferencedFunctions()`. Registering Coding normally through
`builder.WithToolHarness<CodingToolHarness>()` causes `AutoRegisterDependenciesFromFactory` to select
the generated `DebugToolHarness` factory and apply the referenced-function filter. The dependency is
intentionally not added to `_explicitlyRegisteredToolHarnesses`; therefore it does not become an
independent top-level visible harness and requires no separate
`builder.WithToolHarness<DebugToolHarness>()` call.

`DebugToolHarness` is not marked `[Collapse]` on the default path. The skill activation is the inner
container. Giving the dependency harness its own collapse container would add an unnecessary
top-level activation node and would misrepresent the intended `Coding -> skill -> selected function`
graph. If a different product explicitly exposes the debugger directly, it must define that
presentation policy deliberately rather than changing the shared Coding default.

The generated capability graph supplies the nesting. Because Coding is collapsed, each Coding-owned
skill activation has the Coding harness container as its parent. Each referenced debug function keeps
its existing parents and gains the skill ID as an alternative parent; the skill's `Reveals` list
contains those function IDs. Visibility is therefore:

```text
initially                         Coding
after Coding expansion           debugging skill activations
after one skill activation       only that skill's referenced DebugToolHarness functions
```

There is no intermediate Debug harness container in this route. The effective graph is
`Coding container -> skill activation -> selected DebugToolHarness functions`, not
`Coding container -> Debug container -> functions`. Multiple skills may reference the same function;
the function receives multiple alternative skill parents and becomes visible when any authorized
referencing skill is active. The capability graph must deduplicate identical parent IDs and reject
missing references, duplicate model-facing names, or cycles deterministically.

A host may still explicitly register `DebugToolHarness` as a deliberate alternative product surface,
but that is not the Coding-agent default and is outside the automatic skill-dependency path. The
default architecture obtains modular class ownership without a second package or a second explicit
builder registration.

Skill grouping is presentation policy, not a protocol or service boundary. A product may reference
the complete semantic function set from one comprehensive debugging skill, partition it among several
task-oriented skills, reference common functions from multiple skills, or explicitly register
`DebugToolHarness` to expose its complete permitted surface directly. These choices alter discovery,
instructions, and token/tool visibility only; they do not change function identities, authorization,
session ownership, DAP behavior, or the underlying semantic service. The checked-in feature matrix
maps every semantic function to all exposing skills so omissions and accidental overexposure are
reviewable. The initial grouping is selected from schema/tool-count measurements and agent
evaluations, and may evolve without restructuring the debugger core.

## 7. Cold path: adapter declarations and generated catalog

### 7.1 Boundary

Adapter declarations contain stable classification and package-default metadata. They never claim
that an executable exists, that a version is supported, that a remote endpoint is authorized, or
that a launch is permitted in the selected environment. Those are runtime factory decisions.

The declaration vocabulary is intentionally restricted to:

```csharp
[HpdDebugAdapter("netcoredbg")]
[DebugAdapterLanguages("csharp", "fsharp")]
[DebugAdapterFileExtensions(".cs", ".csx", ".fs", ".fsx")]
[DebugAdapterRootMarkers("*.sln", "*.csproj", "*.fsproj", "global.json")]
[DebugAdapterTargetKinds(DebugTargetKind.Executable | DebugTargetKind.Process)]
[DebugAdapterFactory(typeof(NetCoreDbgAdapterFactory))]
[DebugAdapterCommandHint("netcoredbg")]
[DebugAdapterArgumentHints("--interpreter=vscode")]
[DebugAdapterInstallGuidance("debug.netcoredbg.install")]
public sealed class NetCoreDbgAdapterDeclaration;
```

`DebugTargetKind` is a flags enum covering executable, source file, project directory, module,
process, and registered remote endpoint targets. Command, argument, and transport hints are trusted
package defaults, not resolved facts. Attributes never contain paths, credentials, raw endpoints,
environment availability, final transport parameters, policy decisions, or final launch payloads.

### 7.2 Static and behavioral adapters

A static declaration without `DebugAdapterFactoryAttribute` binds to the shared
`StandardDebugAdapterFactory`. The generated descriptor supplies its trusted command and argument
hints. A behavioral declaration names a concrete `IDebugAdapterFactory` service. Neither form is
constructed with `new` by generated code, and no parameterless constructor is required.

Factories are registered with DI and may use constructor-injected logging, options, Environment
capabilities, endpoint resolution, policy, and package services. The catalog generator emits a direct,
reflection-free resolver:

```csharp
internal static IDebugAdapterFactory ResolveNetCoreDbg(IServiceProvider services) =>
    services.GetRequiredService<NetCoreDbgAdapterFactory>();
```

### 7.3 Catalog contracts

```csharp
public sealed record DebugAdapterDescriptor
{
    public required string Id { get; init; }
    public required IReadOnlyList<string> Languages { get; init; }
    public required IReadOnlyList<string> FileExtensions { get; init; }
    public required IReadOnlyList<string> RootMarkers { get; init; }
    public required DebugTargetKind TargetKinds { get; init; }
    public IReadOnlyList<string> CommandHints { get; init; } = [];
    public IReadOnlyList<string> ArgumentHints { get; init; } = [];
    public string? InstallGuidanceId { get; init; }
    public int Priority { get; init; }
    public bool EnabledByDefault { get; init; } = true;
    public bool Experimental { get; init; }
    public required DebugAdapterProvenance Provenance { get; init; }
}

public delegate IDebugAdapterFactory DebugAdapterFactoryResolver(IServiceProvider services);

public sealed record DebugAdapterCatalogEntry
{
    public required DebugAdapterDescriptor Descriptor { get; init; }
    public required DebugAdapterFactoryResolver FactoryResolver { get; init; }
}

public interface IDebugAdapterCatalogProvider
{
    IEnumerable<DebugAdapterCatalogEntry> GetEntries();
}
```

Metadata and factory resolution remain separate so catalog inspection and diagnostics do not treat a
service delegate as serializable domain data. `DebugAdapterCatalogEntry` and its resolver delegate are
infrastructure and are excluded from JSON serializer contexts.

### 7.4 Provenance and host-derived trust

Catalog packages provide provenance claims, not authoritative trust:

```csharp
public sealed record DebugAdapterProvenance
{
    public required string PackageId { get; init; }
    public required string PackageVersion { get; init; }
    public required string AssemblyName { get; init; }
    public string? ClaimedSignatureIdentity { get; init; }
}

public sealed record DebugAdapterTrustDecision
{
    public required DebugAdapterTrustLevel TrustLevel { get; init; }
    public string? VerifiedSignatureIdentity { get; init; }
    public required string PolicyRevision { get; init; }
    public required string ReasonCode { get; init; }
}
```

The host verifies package registration, signature identity, and policy and produces the trust
decision. An adapter cannot grant itself trust through an attribute or generated descriptor. Selection
and launch authorization use the host decision to determine whether an entry may execute command
hints, search workspace/global tools, request network/socket access, resolve credentials or
authorities, or create terminal and child processes.

### 7.5 Cross-assembly composition

Every adapter package generates one assembly-local catalog provider. Packages expose explicit
registration extensions; the runtime never scans assemblies:

```csharp
services.AddHPDCodingDebugging();
services.AddHPDBuiltInDebugAdapters();
services.AddHPDPythonDebugging();
services.AddHPDJavaScriptDebugging();
```

These are DI/catalog composition extensions, not `AgentBuilder` harness registrations.
`AddHPDCodingDebugging()` installs the shared debugger services used by the generated
`DebugToolHarness`; model-visible registration follows the skill dependency path in section 6.1.

At service-provider construction, `DebugAdapterCatalog` materializes all registered providers into
one immutable catalog. Duplicate IDs, invalid resolvers, conflicting metadata, and missing required
factory registrations fail deterministically with provider/package provenance.

Every generated resolver is validated during catalog materialization. A built-in resolver failure
aborts startup. An invalid optional external package may be disabled only by explicit host policy.
Diagnostics report adapter ID, package provenance, and factory type without leaking container
internals.

## 8. Adapter catalog generator

Use `ForAttributeWithMetadataName` and semantic symbols. The incremental pipeline discovers
declarations, validates metadata, validates the exact factory interface, checks local duplicates, and
emits immutable descriptors plus direct DI resolver methods.

Required diagnostics include blank/duplicate IDs; inaccessible declaration or factory types; invalid
extensions and markers; empty target kinds; duplicate language/extension/marker values; incompatible
factory types; static declarations missing command hints; unsupported attribute values; malformed
trusted default fragments; and conflicts between static metadata and behavioral factory selection.
Diagnostics point to the exact attribute argument when possible.

The adapter catalog generator does not generate DAP wire types. Catalog generation is registration
infrastructure; protocol generation is correctness infrastructure with a different authoritative
input and project.

The debug adapter incremental generator is added to the existing
`HPD-Agent.Harness.Coding.SourceGenerator` assembly beside the language-server generator. Each has
its own syntax provider, semantic model, diagnostics, and generated hint-name prefix; neither shares
mutable pipeline state or emits the other's registry. The Coding project references one analyzer
assembly, so adding debugging creates no analyzer project reference, packaging unit, or versioning
boundary. Generator tests remain grouped by subsystem and include a combined compilation proving
that language-server and debug outputs coexist without duplicate hint names, symbols, or diagnostics.

## 9. Runtime adapter resolution

### 9.1 Factory contract

```csharp
public interface IDebugAdapterFactory
{
    ValueTask<DebugAdapterAvailability> ProbeAsync(
        DebugAdapterDescriptor descriptor,
        DebugAdapterResolutionContext context,
        CancellationToken cancellationToken = default);

    ValueTask<DebugAdapterLaunchPlan> CreateLaunchPlanAsync(
        DebugAdapterDescriptor descriptor,
        DebugLaunchContext context,
        CancellationToken cancellationToken = default);

    ValueTask<DebugAdapterLaunchPlan> CreateAttachPlanAsync(
        DebugAdapterDescriptor descriptor,
        DebugAttachContext context,
        CancellationToken cancellationToken = default);
}
```

Normal absence is a typed availability result, not an exception. Exceptions are reserved for broken
factory invariants or unexpected infrastructure failures.

Availability probes are bounded and observational. They may inspect files, metadata, and versions or
run a bounded transient version check, but they cannot leave persistent processes, allocate remote
endpoints, resolve secret material, request credentials, or mutate project state. Availability does
not authorize launch; plan creation occurs only after selection and authorization.

`DebugAdapterResolutionContext`, `DebugLaunchContext`, and `DebugAttachContext` carry the immutable
`DebugRuntimeBinding` captured from the initiating `FunctionExecutionContext`. Factories and tool
resolvers use that binding to inspect the selected Environment; they never resolve or construct an
independent `IProcessProvider`. A launch plan describes what should be started, but it does not start
the process or own the Environment handle.

### 9.2 Authorized launch plan

`DebugAdapterLaunchPlan` contains only resolved and authorized data:

- Adapter and package identity.
- Bound Environment identity/revision and execution-unit target.
- A closed transport plan.
- Filtered working directory and environment.
- Owned launch/attach `JsonElement` arguments.
- Policy revision and authorization scope.
- Bounded safe remediation, with sensitive probe diagnostics retained separately.

The plan is immutable and records catalog provenance and host trust decision, environment/policy/
endpoint revisions, authorization identity, transport plan, filtered environment, canonical working
directory, timeout bounds, reverse-request policy, path mapper, and owned extension arguments. The
orchestrator revalidates these revisions and invariants immediately before process or connection
creation.

Transport plans distinguish Environment stdio, Environment-started TCP server, approved TCP connect,
approved Unix socket, and host callback. Raw model host/port/URI/socket values never enter these plans.

### 9.3 Tool and endpoint resolution

`IDebugAdapterToolResolver` searches only host-approved workspace-local, package, managed-assembly,
and global locations inside the selected Environment. It returns provenance and version information,
not merely a path.

Ordinary semantic input supplies `DebugEndpointId`. `IDebugEndpointResolver` maps it to an authorized
descriptor containing addresses, credentials/authority references, environment binding, rotation,
revocation, and policy revision. Trusted host APIs may provide validated direct descriptors. Adapter
factories receive authorized descriptors; transport factories receive only final parameters.

### 9.4 Availability cache

The availability cache key includes adapter/package ID, environment identity and revision, platform,
canonical workspace root, project-marker fingerprint, launch-policy revision, and endpoint-catalog
revision. Positive entries default to 30 seconds and negative entries to five seconds. Identical
concurrent probes are coalesced. Entries never retain secrets, raw environment values, endpoint
addresses, or unbounded diagnostics.

### 9.5 Selection

Explicit adapter ID limits resolution to that entry. Automatic launch selection filters by enablement,
experimental policy, target kind, extension/language, root markers, and runtime availability, then
uses deterministic priority. Attach additionally considers process/runtime hints and endpoint kind.
Results are `Available`, `Unavailable`, `NoMatch`, or `Ambiguous`; material ambiguity returns bounded
candidates instead of silently choosing.

## 10. Configuration, environment, and endpoint policy

### 10.1 Layering

Configuration is composed in this order:

```text
adapter-package defaults
< trusted host adapter configuration
< validated untrusted project configuration
< per-agent-runtime configuration
< typed semantic operation fields
< HPD-controlled invariants
```

The model-facing service never accepts arbitrary launch JSON or an unrestricted adapter-options
dictionary. Project configuration is untrusted and must pass the selected factory's schema and host
policy. Trusted host configuration may contain adapter extension fields. The factory produces one
owned `JsonElement`; the protocol client never serializes an arbitrary object.

HPD-controlled fields such as `request`, target/program identity, canonical `cwd`, endpoint identity,
and transport values either win by documented policy or cause a typed conflict. They are never
silently overwritten.

### 10.2 Environment overrides

Environment overrides are deny-by-default and host-allowlisted. Defaults limit entries to 32, keys to
128 UTF-8 bytes, and values to 4 KiB. Null/delete semantics are not exposed to model input. Key
comparison follows the target platform. Adapter, loader, credential, proxy, tracing, and protocol
variables are reserved unless host policy explicitly grants them. Rejected values are never echoed in
errors or events. Only the filtered result reaches `ProcessCommandSpec.Environment`.

### 10.3 Endpoint surfaces

| Caller | Accepted endpoint input |
|---|---|
| Model semantic service | Opaque `DebugEndpointId` |
| Trusted host API | Validated direct descriptor or endpoint ID |
| Adapter factory | Authorized resolved descriptor |
| Transport factory | Final transport parameters |

Revocation or policy revision invalidates cached availability and prevents new connections. Existing
connections follow the host's revocation policy: terminate immediately or enter a bounded drain.

## 11. Mandatory canonical DAP generation

The pinned official `debugAdapterProtocol.json` is the authoritative input to an explicit,
deterministic engineering tool. The tool updates the complete checked-in wire contract, not only the
subset exposed through semantic operations:

- Base protocol messages.
- Every request argument and response body.
- Every event and reverse-request body.
- Client and adapter capabilities.
- Supporting types, extension fields, and open-string enum wrappers.
- `DapJsonContext` with metadata for every generated type.
- Typed request, event, and reverse-request descriptors.
- XML documentation copied within upstream licensing limits.
- Schema revision and upstream commit metadata.
- A baseline canonical feature inventory.

Normal builds compile the checked-in `.g.cs` files and do not run a Roslyn generator for these same
types. Regeneration is an explicit engineering command. CI runs it from the pinned schema and fails on
a dirty diff, preventing duplicate compiled types and hidden build-time drift. Updating the schema
requires reviewing generated source, feature-inventory, documentation, and licensing changes.

DAP open enums preserve unknown values. Schema extension points use `JsonExtensionData` or owned
`JsonElement` values as appropriate. No runtime schema interpretation or reflection metadata creation
is allowed.

The generated inventory classifies every definition as client-to-adapter request,
adapter-to-client reverse request, adapter-to-client event, base/supporting type, or extension seam.
Classification uses schema structure plus a small reviewed override table for ambiguous cases; it does
not infer direction solely from a type-name suffix. Completeness means every feature is generated and
classified and every advertised client capability has implemented tests—not that every request is
sent indiscriminately.

Before generated files are committed, pin the upstream repository, version, and commit; distinguish
the upstream code license (MIT) from the documentation/specification license (Creative Commons
Attribution); preserve the attribution required for any derived descriptions; add the repository
third-party notices; and emit the applicable license/source pointer in every generated header.
Licensing policy is reviewed centrally on schema upgrade rather than improvised per generated file.

## 12. Protocol client and transports

### 12.1 Typed descriptors

Generated descriptors bind command, arguments, response, and source-generated JSON metadata:

```csharp
public sealed record DapRequestDescriptor<TArguments, TResponse>(
    string Command,
    JsonTypeInfo<TArguments> ArgumentsTypeInfo,
    JsonTypeInfo<TResponse> ResponseTypeInfo);

ValueTask<TResponse> SendAsync<TArguments, TResponse>(
    DapRequestDescriptor<TArguments, TResponse> descriptor,
    TArguments arguments,
    CancellationToken cancellationToken,
    TimeSpan? timeout = null);
```

This prevents command/argument/response mismatches and arbitrary-object serialization. Launch and
attach descriptors accept the generated standard envelope plus the factory-owned extension object.

### 12.2 Transport contract

```csharp
public interface IDebugProtocolTransport : IAsyncDisposable
{
    bool IsAlive { get; }
    ValueTask<int> ReadProtocolAsync(Memory<byte> buffer, CancellationToken cancellationToken);
    ValueTask WriteProtocolAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);
    IAsyncEnumerable<DebugTransportDiagnosticChunk> ReadDiagnosticsAsync(
        CancellationToken cancellationToken = default);
    ValueTask<DebugTransportExit> WaitForExitAsync(CancellationToken cancellationToken = default);
    ValueTask StopAsync(DebugTransportStopRequest request, CancellationToken cancellationToken);
}
```

The start orchestrator—not the transport—uses the `IProcessProvider` captured in
`DebugRuntimeBinding`. It builds a `ProcessInvocationSpec` with a direct adapter executable and
argument array, streaming stdin/stdout/stderr, explicit isolation, network, environment, limits, and
process-tree stop policy, then calls `StartAsync`. Adapter processes are never launched through a
shell command string.

The debugger does not also attach an `IProcessOutputSink` to the same invocation. The returned handle
and the transport's single pump are the sole debugger output-consumption path, avoiding duplicate or
competing delivery semantics.

`DebugEnvironmentProcessTransport` receives and owns the resulting `IProcessInvocationHandle`:

```csharp
internal sealed class DebugEnvironmentProcessTransport : IDebugProtocolTransport
{
    private readonly IProcessInvocationHandle _process;

    public DebugEnvironmentProcessTransport(IProcessInvocationHandle process)
        => _process = process;
}
```

It adapts the existing Environment handle rather than reimplementing process execution. Only tagged
stdout feeds the DAP parser; tagged stderr feeds the bounded diagnostic channel. Protocol writes call
`WriteStdinAsync`, liveness observes `WaitAsync`, stop calls `StopAsync`, and disposal releases the
same handle.

The transport owns exactly one enumeration of `IProcessInvocationHandle.ReadOutputAsync`. A single
output pump copies chunks marked `ProcessOutputChunkFlags.BorrowedBuffer` before their valid lifetime
ends and demultiplexes the tagged sequence:

```text
IProcessInvocationHandle.ReadOutputAsync
        |
        v
copy borrowed bytes when required
        |
        +---- stdout --> ordered lossless bounded/backpressured protocol channel
        |
        +---- stderr --> bounded non-blocking diagnostic channel + drop counters
```

`ReadProtocolAsync` reads only the stdout channel; `ReadDiagnosticsAsync` reads only the diagnostic
channel. No second consumer enumerates the handle output. Protocol stdout is never silently dropped:
exceeding a hard resource limit faults the transport because losing one byte can corrupt framing.
Stderr may truncate only under configured policy and records dropped chunks/bytes. Per-stream order is
preserved. An unread diagnostic consumer cannot stall stdout.

The underlying `IProcessProvider` remains responsible for safely draining OS pipes into its tagged
output mechanism. Final markers and process exit complete both transport channels exactly once. Pump
failure faults the transport and settles pending requests. Disposal stops the handle, terminates the
pump, completes both consumers, and disposes the handle exactly once.

Approved TCP, Unix-socket, callback, and in-memory transports implement the same contract. Socket
transports are created only from authorized endpoint or factory plans and cannot bypass HPD policy.

### 12.3 Protocol responsibilities

`DebugProtocolClient` owns sequence allocation, UTF-8 `Content-Length` framing, partial/combined
reads, typed serialization, correlation, events, reverse requests, bounded writes, cancellation,
DAP `cancel`, late responses, disconnect settlement, and optional bounded host-only raw tracing. It does not
own breakpoints, selected frames, tree ownership, permissions, or agent events.

Sequence numbers are positive and monotonically allocated within one connection. A response is
correlated only when both `request_seq` and the expected command agree; mismatches, impossible
duplicates, and responses for an unknown live sequence are bounded protocol diagnostics or faults
according to whether safe settlement remains possible. A well-formed response with `success: false`
is an ordinary structured adapter request failure, not a `ProtocolViolation`. The client preserves
the command, adapter error code, and safe DAP `Message` metadata (`id`, `format`, sanitized
`variables`, `showUser`, and approved `url`/`urlLabel`) while redacting telemetry, PII, secrets, and
unsafe links.

The framer accepts the DAP ASCII header grammar terminated by `\r\n\r\n`, requires exactly one valid
supported `Content-Length`, measures the following UTF-8 payload in bytes, and rejects duplicate,
negative, overflowing, missing, or over-limit lengths. It handles split headers/bodies and coalesced
messages without treating adapter stdout contamination as a recoverable event by default.

There is exactly one reader. Handler failures are isolated. A reader callback may reconcile bounded
in-memory state but may not synchronously await a request whose response requires the same reader.

### 12.4 Cancellation and settlement

Every pending request settles exactly once. Caller cancellation stops that wait and sends DAP
`cancel` only when advertised. The client retains sufficient tombstone state to ignore or safely
reconcile late responses. Request cancellation never terminates the transport or debug tree. Adapter
exit, transport failure, and disposal settle all pending requests with the appropriate typed failure.

DAP progress cancellation is tracked separately from caller request-wait cancellation. A
`progressStart` entry retains its opaque `progressId`, optional related `requestId`, and `cancellable`
flag. An authorized cancellation sends `cancel` with `progressId` only when the operation is declared
cancellable. Cancellation is a best-effort hint: the progress entry remains live until `progressEnd`,
transport/session termination, or a bounded orphan-cleanup policy settles it.

### 12.5 Malformed input

A correctly framed message with malformed JSON faults the protocol session by default because
skipping it can strand correlation state. Invalid/missing framing, oversized headers/bodies, invalid
UTF-8, and protocol stdout contamination also fault after bounded safe diagnostics. Unknown
well-formed events are ignored with bounded telemetry. Unknown reverse requests receive a DAP
not-supported response. Optional bounded resynchronization is an adapter-scoped compatibility mode,
never the production default.

## 13. Session model

### 13.1 Session tree

```csharp
internal sealed class DebugSessionTree
{
    public required DebugTreeOwnership Ownership { get; init; }
    public required string RootSessionId { get; init; }
    public required DebugBreakpointStore Breakpoints { get; init; }
    public ConcurrentDictionary<string, DebugSession> Sessions { get; } = new();
    public string? ActiveSessionId { get; set; }
}

public sealed record DebugTreeOwnership(
    string AgentRuntimeRegistrationId,
    string SessionId,
    string ThreadId,
    string DebugTreeId,
    string EnvironmentId,
    long EnvironmentRevision);
```

A tree owns desired breakpoint/exception configuration, authorization, environment binding, output
and artifact policy, lifetime token, root handle, and all root/child protocol sessions. Individual
sessions own adapter-confirmed state. `IDebugSessionManager` is registered in the runtime capability
registry and disposed with that agent runtime. There is no process-global active session.

The manager creates one stable opaque runtime identity when constructed:

```csharp
public interface IDebugSessionManager : IAsyncDisposable
{
    string RuntimeId { get; }
}
```

`DebugRuntimeBinding.Capture` obtains `AgentRuntimeRegistrationId` from `SessionManager.RuntimeId`.
It never derives runtime identity from agent name, agent ID, conversation ID, session ID, or thread ID.

Every operation must resolve a tree through its complete ownership scope. A matching tree ID owned by
another runtime/session/thread is rejected as `SessionOwnershipMismatch`, not reported as missing.
Protocol-session IDs identify members only inside their owning tree.

### 13.2 Session state

```csharp
public enum DebugSessionStatus
{
    Created,
    Initializing,
    Configuring,
    Running,
    Stopped,
    Terminating,
    Terminated,
    Faulted
}
```

`DebugSessionStatus` is a lifecycle/summary state, not a replacement for DAP's per-thread execution
state. Each session owns a `DebugThreadState` projection keyed by adapter thread ID and records, for
each live thread, running/stopped state, stop reason, stop details, and a monotonically increasing
suspension epoch. `stopped` and `continued` honor `allThreadsStopped` and `allThreadsContinued`; when
those flags are absent or false, only the identified thread changes state. The session derives
`Running`, `Stopped`, or a presentation-level partially-stopped summary from those thread states.

Each session tracks:

- Session, root, and parent IDs.
- Adapter definition and resolution.
- Protocol client.
- Status and timestamps.
- Negotiated capabilities.
- Initialize/configuration state.
- Current process and thread projections.
- Per-thread stop reasons, locations, and suspension epochs.
- Cached stack frames and their validity within the owning thread's current suspension epoch.
- Adapter-confirmed breakpoints.
- Output buffer.
- Progress operations.
- Child session IDs.
- Exit code and failure details.
- The authorized launch plan and environment revision.
- Its protocol reader/observer lifetime and exactly-once completion gates.
- Bounded pending-request, reverse-request, event, projection, and continuation-token state.

### 13.3 Active session

The active session is the tree member targeted by operations that omit an explicit session ID. Selection rules:

1. A newly stopped child becomes active.
2. A newly stopped root becomes active if no child is stopped later.
3. When active session terminates, prefer another stopped live child, then a live child, then the root.
4. Explicit `debug_session_id` overrides active selection.

Tool requests should accept an optional debug session ID even if the first UI generally uses the active one.

Active selection is deterministic convenience inside an explicitly owned tree. No tree-less or
process-global “current debugger” exists.

### 13.4 State invalidation

On `stopped`, update either the identified thread or every thread according to `allThreadsStopped`,
advance each affected thread's suspension epoch, and invalidate its prior stack/scope/variable
projections before fetching a new top frame.

On `continued`, update either the identified thread or every thread according to
`allThreadsContinued` and clear only the affected stop-dependent data. A partial continue does not
make unrelated stopped threads current or running.

On DAP `invalidated`, mark the specified areas invalid. Do not return invalid cached data as current.

On `thread`, `module`, `loadedSource`, `breakpoint`, `process`, `memory`, and `capabilities`, reconcile the corresponding projection. A `memory` event invalidates overlapping cached regions only when
they use the same opaque `memoryReference`; different references are never assumed comparable.

Adapter-issued `frameId`, `variablesReference`, `sourceReference`, `memoryReference`,
`locationReference`, and adapter-data values remain opaque. They are bound to their protocol session
and, where the protocol defines a suspended-state lifetime, to the current thread suspension epoch.
Semantic operations reject stale references locally rather than sending them to the adapter.

## 14. Launch and attach lifecycle

Launch and attach requests may carry typed initial desired state:

```csharp
public sealed record DebugInitialConfiguration
{
    public IReadOnlyList<DebugSourceBreakpointSet> SourceBreakpoints { get; init; } = [];
    public IReadOnlyList<DebugFunctionBreakpoint> FunctionBreakpoints { get; init; } = [];
    public IReadOnlyList<DebugExceptionFilter> ExceptionFilters { get; init; } = [];
    public IReadOnlyList<DebugDataBreakpoint> DataBreakpoints { get; init; } = [];
    public IReadOnlyList<DebugInstructionBreakpoint> InstructionBreakpoints { get; init; } = [];
    public bool StopOnEntry { get; init; }
}
```

The unpublished tree records this object in the same desired-state store used by later mutations
before sending any breakpoint replacement or `configurationDone`.

### 14.1 Pre-publication start

1. Resolve and authorize the HPD ownership scope, workspace, target, environment, and optional
   endpoint ID.
2. Capture `DebugRuntimeBinding` once from `FunctionExecutionContext.RuntimeCapabilities`, including
   `SessionManager.RuntimeId`, `IProcessProvider`, optional `IEnvironmentRuntime`, manager, and
   authorized host services required by this start.
3. Classify target kind and select a catalog entry.
4. Resolve its DI factory and probe with the full environment/policy cache key and captured binding.
5. Compose typed semantic, validated project, trusted host, and runtime configuration.
6. Request the factory's authorized launch/attach plan.
7. For an adapter process, build `ProcessInvocationSpec`, call the captured
   `IProcessProvider.StartAsync`, and wrap the returned `IProcessInvocationHandle` in
   `DebugEnvironmentProcessTransport`. For a connect-only plan, create an approved endpoint transport.
8. Create the protocol client, root session, tree-lifetime token, and exactly-once configuration gate.
9. Register event and reverse-request handlers before sending `initialize`.
10. Build client capabilities through `DebugInitializePolicy` from implemented handlers, enabled host
    policy, transport behavior, and renderer support.
11. Send `initialize` exactly once as the first protocol message, await its response before allowing
    any other request or response to cross the connection, and retain negotiated adapter capabilities.

`DebugInitializePolicy` explicitly selects line/column origin, path format, locale, client identity,
and every advertised client capability. It advertises `supportsRunInTerminalRequest`, progress,
invalidated, memory, `startDebugging`, ANSI styling, and related features only when the complete
handler, authorization, resource-limit, and presentation paths are active. Schema generation alone
never causes a capability to be advertised. Dynamic adapter capability events apply patches with
correct absence-versus-false semantics and can remove previously available operations.

Until the tree and background handle are published successfully, caller cancellation aborts start and
cleans up every created transport/session resource.

### 14.2 Race-safe configuration

The configuration state machine must accept all valid adapter orderings:

- `initialized` before the launch/attach response.
- `initialized` after that response.
- Launch/attach response withheld until `configurationDone`.
- Immediate stopped/exited/terminated events.
- An adapter that does not support `configurationDone`.

Launch/attach request, `initialized` observation, and configuration are coordinated concurrently;
code must not await a withheld launch response while preventing configuration. When `initialized`
arrives, one exactly-once owner applies all eligible initial source/function/exception breakpoint
intentions plus only data/instruction breakpoints whose identifiers are valid and portable for that
new protocol session, then sends `configurationDone` when advertised. Breakpoints that require a
stopped frame or session-local discovery remain pending semantic intentions and are discovered at the
first eligible suspension. Duplicate events or callbacks observe the same completion task and cannot
configure twice.

Start completes only after the required launch/attach response and configuration boundary have both
settled, or after a terminal event makes success impossible. Initial stopped state is captured through
a waiter installed before any request capable of producing it. Follow-up stack requests run outside
the reader loop.

### 14.3 Tree publication

After valid configuration, publish through a failure-atomic protocol with gated intermediate
visibility. The current manager and background registries do not provide one cross-registry
transaction, so the design does not claim impossible strict atomicity:

1. Create and configure the unpublished tree.
2. Reserve a non-addressable manager entry.
3. Create a `DebugSessionHandle` in `Starting` state.
4. Register the root background handle.
5. Attach its registration to the reserved tree.
6. Commit the manager entry as live.
7. Transition the handle to its live running/stopped state.
8. Activate observers under the tree-lifetime token.
9. Commit and publish the durable started event.
10. Return a bounded typed snapshot.

While `Starting`, handle reads return bounded starting status, control operations reject or await
publication, and the manager cannot select the tree. Any failure rolls back the reservation, settles
pending work, stops/disposes the transport and process handles, and stops or completes a registered
background handle as necessary. A failed publication emits no started event.

Cancellation after publication cancels only the initiating operation; it never implicitly terminates
the live tree.

### 14.4 Attach semantics

Attach follows the same handshake, but termination semantics differ. Unless explicitly requested and supported, disconnecting an attached session must not terminate the debuggee.

`disconnect` construction makes `terminateDebuggee` and `suspendDebuggee` explicit whenever the
negotiated adapter supports those arguments. Defaults derive from launch-versus-attach ownership and
host policy, never from omission accidentally inheriting an adapter-specific default. `exited`
records debuggee exit; `terminated` records protocol debugging termination. Either may arrive without
the other and neither is synthesized merely to simplify the state machine.

The semantic service distinguishes terminating the complete root tree, disconnecting one protocol
session, terminating one debuggee where supported, detaching without terminating an attached
debuggee, and forced transport disposal after a graceful deadline. Child termination does not
silently terminate the root tree unless the adapter's documented semantics or explicit policy require
it.

### 14.5 Failure cleanup

Any failure after creating the connection must:

- Remove the partially registered session.
- Cancel pending requests.
- Stop the adapter process/connection.
- Dispose owned resources.
- Publish a bounded failure event if runtime publication is available.
- Return an adapter-specific availability hint when applicable.

Cleanup uses a fresh bounded internal token rather than an already-cancelled caller token. A cleanup
failure is recorded but cannot prevent the remaining owned resources from being disposed.

### 14.6 Owner lifecycle matrix

| Owner event | Required debugger result |
|---|---|
| Agent turn finishes | Published trees remain live |
| Presentation-layer visibility changes | Trees remain live |
| Root debug handle stops | Terminate and dispose the entire tree |
| HPD thread is deleted | Terminate every tree owned by that thread |
| Agent runtime stops/disposes | Terminate every tree owned by that runtime |
| Bound environment exits/disposes | Fault its trees and release transports |
| Debugging service provider disposes | Terminate all remaining owned resources |
| Process restarts and journal reloads | Historical IDs remain non-live and are never relaunched |

Disposal enumerates trees independently; failure in one tree cannot prevent cleanup of another. Tree
shutdown uses bounded per-tree and aggregate deadlines.

### 14.7 Restart lifecycle

Restart is a lifecycle operation, not merely another execution-control request. When the negotiated
adapter advertises `supportsRestartRequest`, HPD sends the typed `restart` request and reconciles the
result under the existing session ownership and authorization. Otherwise, semantic restart performs
a bounded teardown and creates a fresh adapter process/connection/session through the normal
authorized launch or attach path.

A `terminated` event can contain an opaque `restart` value. HPD preserves that value as an owned
`JsonElement` without interpreting, merging, logging, or model-exposing it and supplies it unmodified
as the adapter-reserved `__restart` property on the replacement launch/attach request. `terminated`
with restart data is therefore a restart transition rather than immediate irreversible tree
finalization. The old protocol session still settles all requests and resources exactly once; the
tree or replacement tree becomes live only through the normal failure-atomic publication rules.
Launch and attach restart retain their distinct disconnect/debuggee-termination policies.

## 15. Background handle integration

### 15.1 New handle kind

Add `DebugSession` to `BackgroundHandleKind` and update all serialization contexts and projections.

### 15.2 Handle implementation

`DebugSessionHandle` implements:

```csharp
IReadableBackgroundHandle
IStoppableBackgroundHandle
IArtifactBackgroundHandle
IAsyncDisposable
```

Status returns:

- Root tree ID.
- Active session ID.
- Adapter ID.
- Running/stopped/terminated state.
- Current stop summary.
- Child count.
- Output statistics.
- Failure summary.

Read returns:

- Bounded recent output.
- Current stopped location.
- Optionally a concise stack summary.
- Content-store references for full artifacts.

Stop performs best-effort tree termination followed by unconditional disposal.

Ordinary semantic/background-handle artifacts may include:

- Categorized debuggee output.
- Adapter stderr.
- Crash dumps or adapter-produced diagnostics.
- A final session summary.

Raw protocol traces, when separately enabled by trusted host policy, use a distinct host-diagnostic
artifact classification. They are not returned by the semantic service, ordinary handle reads, or the
ordinary handle artifact list.

### 15.3 Observer task

The observer waits until the root tree reaches a final state. It:

- Publishes asynchronous state events.
- Responds to runtime shutdown by terminating the tree.
- Updates handle status.
- Stores final artifacts.
- Completes with an appropriate notification summary.

The observer owns a debug-specific event publisher backed by `IThreadEventPublisher` and
`IEventCoordinator`. It must not retain the originating `FunctionExecutionContext`. The publisher
exposes separate durable and live-only operations so background code cannot accidentally journal a
high-frequency event merely because session and thread scope are available.

## 16. FunctionExecutionContext usage

Every harness function that starts or interacts with a live session accepts
`FunctionExecutionContext` and `CancellationToken` as injected parameters. This follows the existing
Coding `ExecuteCommand` pattern: runtime infrastructure is obtained through the invocation context,
while long-lived ownership is transferred to registered background resources.

It uses the context to:

- Resolve `IDebugSessionManager` from `RuntimeCapabilities`.
- Resolve the current `IProcessProvider` and optional `IEnvironmentRuntime` from
  `RuntimeCapabilities` for adapter-process starts.
- Scope operations to the current HPD session/thread.
- Publish synchronous operation events.
- Register the debug background handle and observer.
- Store structured result metadata.
- Store large output or source in `IContentStore`.
- Initiate host request/response flows when needed.

At start, `DebugRuntimeBinding.Capture` resolves the specific runtime-owned capabilities required by
the tree and records immutable ownership/scope data:

```csharp
internal sealed record DebugRuntimeBinding
{
    public required string AgentRuntimeRegistrationId { get; init; }
    public required string SessionId { get; init; }
    public required string ThreadId { get; init; }
    public IProcessProvider? ProcessProvider { get; init; }
    public IEnvironmentRuntime? EnvironmentRuntime { get; init; }
    public required IDebugSessionManager SessionManager { get; init; }
    public required DebugEventScope EventScope { get; init; }
    public required DebugRuntimeBindingState State { get; init; }
}
```

The binding captures capability references selected by the runtime; it does not clone providers,
retain the mutable capability registry, or create a second Environment abstraction. The runtime owns
those capabilities and its disposal remains authoritative. A connect-only remote plan may omit
`ProcessProvider` if it starts no local adapter process; any plan that launches an adapter requires it
and fails with a typed availability error when absent.

The tree retains this narrow binding so a later DAP `startDebugging` request can create a child adapter
through the same runtime-selected provider after the originating tool call has returned. It may not
re-query a different capability registry or silently fall back to a host-local process launcher.

Selected shared service references are valid only until agent-runtime disposal. The tree never
disposes `IProcessProvider`, `IEnvironmentRuntime`, `IDebugSessionManager`, or other shared runtime
services. It owns and disposes only process invocation, transport, session, pump, waiter, and artifact
handles created for that tree. Environment loss or runtime teardown atomically invalidates
`DebugRuntimeBindingState`, faults affected trees, and rejects later child starts with a typed
`SessionUnavailable`/environment-loss result. Service-provider teardown terminates trees before the
shared services disappear.

Once `IProcessProvider.StartAsync` returns, the tree owns the resulting
`IProcessInvocationHandle` through `DebugEnvironmentProcessTransport`. After the tree and
`DebugSessionHandle` are published, protocol readers, reverse requests, events, and observation use
tree-owned dependencies and lifetime tokens rather than the invocation context.

It must not:

- Mutate raw agent/session/thread state.
- Be stored in `DebugSession`.
- Be captured by an indefinite protocol event handler.
- Be used as a replacement for `IDebugSessionManager`.
- Be retained merely to access `RuntimeCapabilities`, background registries, events, or content after
  the initiating function returns.

## 17. Agent event model

### 17.1 HPD event boundaries

HPD has several related event facilities, but they do not have interchangeable semantics:

| Facility | Guarantee | Debugger use |
|---|---|---|
| `IEventCoordinator.EmitAsync` | Accepted by matching live subscriber mailboxes, subject to their backpressure policy; handlers may still be running | Live semantic notifications |
| `IThreadEventPublisher.CommitAndPublishAsync` | Canonically serialized and appended first, then the exact committed event is emitted live | Durable lifecycle and decision-relevant state |
| `IEventCoordinator.RegisterRequest` / `RespondAsync` | Tracks an answerable request before publication and releases its waiter only after an optional completion boundary | Host-mediated reverse requests |
| `IStructEventHub` | Typed, process-local, bounded high-volume lanes with explicit overflow behavior | Protocol timing/counters and dropped-trace telemetry without raw payloads |
| `EventLoopMailbox<T>` / `EventSignal` | Local scheduler wake-up and bounded work queue | Internal session pump coordination, not public agent events |

The debugger must not equate `EmitAsync` with persistence or subscriber completion. It must also not
route every DAP message through the durable thread journal.

Introduce `IDebugEventPublisher` with two explicit methods:

```csharp
ValueTask<TEvent> PublishDurableAsync<TEvent>(
    DebugEventScope scope,
    TEvent evt,
    CancellationToken cancellationToken = default)
    where TEvent : AgentEvent;

ValueTask<TEvent> PublishLiveAsync<TEvent>(
    DebugEventScope scope,
    TEvent evt,
    CancellationToken cancellationToken = default)
    where TEvent : AgentEvent;
```

`PublishDurableAsync` scopes the event, calls `CommitAndPublishAsync` when a thread journal is
available, and otherwise emits live. `PublishLiveAsync` scopes and emits directly through the
coordinator. This service is created from immutable runtime dependencies and can safely be owned by
the background debug tree; `FunctionExecutionContext` remains invocation-only.

`DebugEventScope` carries `TraceId`, `SessionId`, `ThreadId`, root debug-tree ID, debug-session ID,
adapter ID, and optional process/thread identity. The root debug-tree ID is a normal debugger field,
not automatically an HPD `EventFlowId`.

HPD event flows are cancellation groups, not general correlation IDs. A debug tree commonly outlives
the agent turn that launched it. Reusing the tree ID as `EventFlowId` would either require keeping an
unbounded flow registered or risk dropping valid later events after interruption. Use `EventFlowId`
only when an event truly belongs to an active HPD interruptible flow. Terminal debugger events set
`CanInterrupt = false` so termination/failure outcomes are not discarded.

### 17.2 Publication policy

Durably journal only events required to reconstruct important debugger history or reconcile an
interactive host decision:

- Root/child session started, terminated, and failed.
- Stopped and continued transitions.
- Debuggee exit.
- Material breakpoint verification changes.
- Reverse requests and accepted responses when they cross the host boundary.
- Bounded artifact references and the final session summary.

Publish these live-only by default:

- Output chunks and output-available nudges.
- Progress updates.
- Thread/module/loaded-source churn.
- Cache invalidation and memory-change notices.
- Repeated capability/status snapshots that do not change durable behavior.

The durable terminal/session events can later feed a dedicated debugger projection. Unknown debug
events must remain harmless to the existing `ThreadProjector`; debugger state reconstruction belongs
in `DebugSessionProjector`, not in transcript projection.

Use `AgentStructEvent` only for optional process-local hot-path measurements such as request latency,
wire bytes, queue depth, parse failures, and dropped trace records. Struct events are not hosted
`AgentEvent` values, do not bubble through coordinator hierarchies, and are not journaled. Semantic
stops, exits, breakpoint changes, output references, and host requests remain class events.

### 17.3 General event principles

- Emit semantic state changes, not every wire message.
- Preserve HPD trace/session/thread scope without retaining the invocation context.
- Include root-tree, adapter, debug-session, process, and debugger-thread identifiers explicitly.
- Keep payloads bounded and store large data externally.
- Set `Channel` deliberately: lifecycle/control for state transitions, streaming for bounded output
  notifications, and diagnostic for observations.
- Do not use `EventDirection` as a substitute for session routing; coordinator hierarchy bubbling is
  configured by `SetParent`.
- Make every class event round-trip through the canonical `AgentEventSerializer` before it can enter
  the thread journal.
- Treat mailbox overflow and drops as observable health data; `EmitAsync` only waits for subscribers
  configured for backpressure.

### 17.4 Lifecycle events

```csharp
DebugAdapterResolvingEvent
DebugAdapterUnavailableEvent
DebugAdapterInitializedEvent
DebugSessionStartingEvent
DebugSessionStartedEvent
DebugChildSessionStartedEvent
DebugSessionContinuedEvent
DebugSessionStoppedEvent
DebuggeeExitedEvent
DebugSessionTerminatingEvent
DebugSessionTerminatedEvent
DebugSessionFailedEvent
```

### 17.5 State events

```csharp
DebugProcessChangedEvent
DebugThreadChangedEvent
DebugBreakpointChangedEvent
DebugModuleChangedEvent
DebugLoadedSourceChangedEvent
DebugCapabilitiesChangedEvent
DebugStateInvalidatedEvent
DebugMemoryChangedEvent
DebugOutputAvailableEvent
```

### 17.6 Progress events

```csharp
DebugProgressStartedEvent
DebugProgressUpdatedEvent
DebugProgressCompletedEvent
```

### 17.7 Event coalescing

Output and progress updates can be frequent. Coalesce output over a short interval or byte threshold.
Progress updates retain the latest state and are live-only unless a terminal progress outcome is part
of the final summary. A bounded output event points to content storage once its inline allowance is
exceeded. Struct telemetry records dropped/coalesced counts without turning raw traffic into agent
history.

### 17.8 Serialization and source generation

Before debugger events ship, harden the shared `AgentEventSerializer` registration registry. One
cold-path lock protects atomic forward/reverse registration records. Required semantics are:

- Same CLR type and discriminator is idempotent.
- Same CLR type with another discriminator fails deterministically.
- Same discriminator with another CLR type fails deterministically.
- Harness/external events require source-generated `JsonTypeInfo` for the registered CLR type.
- A registration lacking metadata may be atomically enriched, but no partial forward/reverse mapping
  is ever externally visible.
- Registry inspection is available to tests without exposing mutable dictionaries.

After that prerequisite, follow the Coding harness's AOT-safe event pattern:

1. Declare debugger events as ordinary `AgentEvent` records.
2. Add every concrete event and nested payload type to `CodingDebugJsonContext` using
   `JsonSerializable`.
3. Register each discriminator and its generated `JsonTypeInfo` from a module initializer in
   `CodingDebugEventSerialization`.
4. Make the Coding subsystem's `AddHPDCodingDebugging()` composition path call the same idempotent
   registration method defensively.
5. Add a round-trip test through `AgentEventSerializer`, not merely direct `JsonSerializer` tests.

The existing custom-event generator discovers concrete `AgentEvent` and `AgentStructEvent` records
and generates discriminator registration, but generated registration does not itself manufacture STJ
metadata for arbitrary consumer types. Until that generator is extended to emit or integrate a
consumer JSON context reliably under Native AOT, the explicit Coding-harness pattern is the safer
contract. Do not maintain a second hand-written polymorphic converter.

## 18. DAP event handling

The runtime must handle all canonical events, even if some only update internal state initially:

| DAP event | Required behavior |
|---|---|
| `initialized` | Enable configuration sequence |
| `stopped` | Stop the identified thread or all threads per `allThreadsStopped`, advance affected suspension epochs, invalidate affected caches, select the session, and fetch a top frame asynchronously |
| `continued` | Resume the identified thread or all threads per `allThreadsContinued` and clear only affected stop-dependent data |
| `exited` | Store debuggee exit code |
| `terminated` | Resolve waiters and either finalize the protocol session or enter the opaque restart lifecycle |
| `thread` | Reconcile thread projection |
| `output` | Append categorized bounded output and optionally notify |
| `breakpoint` | Reconcile adapter-confirmed breakpoint |
| `module` | Reconcile module projection |
| `loadedSource` | Reconcile source projection |
| `process` | Reconcile process projection |
| `capabilities` | Merge changed capabilities |
| `progressStart` | Register progress ID, optional request ID, cancellability, and operation state |
| `progressUpdate` | Update progress operation |
| `progressEnd` | Complete progress operation |
| `invalidated` | Invalidate indicated cached areas |
| `memory` | Mark overlapping projections stale only within the same opaque memory reference and publish notification |

No protocol request should be awaited directly inside the single message-reader dispatch path if doing so can block response processing. Follow-up requests such as fetching the top stack frame must be scheduled outside the reader loop.

## 19. Breakpoint architecture

### 19.1 Desired versus confirmed state

The root tree stores desired breakpoint configuration. Each session stores adapter-confirmed breakpoint results.

Supported kinds:

- Source breakpoints.
- Function breakpoints.
- Exception breakpoints.
- Instruction breakpoints.
- Data breakpoints.

### 19.2 Mutation serialization

DAP breakpoint setters replace complete collections. Every read-modify-write mutation for a breakpoint kind must be serialized per root tree. Concurrent mutations must not overwrite one another.

### 19.3 Child propagation

New child sessions receive portable root desired breakpoint state after `initialized` and before
`configurationDone`. Source, function, and exception breakpoint intentions are recomposed for the
child. Instruction breakpoints and memory/instruction references are treated as protocol-session
scoped unless the adapter factory supplies an explicit portability contract.

Data-breakpoint discovery results are not blindly copied. A `dataId` derived from a frame or
`variablesReference` is bound to that session and suspension; HPD honors `canPersist` and propagates
only a persistent semantic recipe. Non-persistent data breakpoints must be rediscovered independently
in the child or remain unavailable there. Failures in one child are reported without corrupting root
desired state or another session's confirmed state.

### 19.4 Breakpoint events

Later `breakpoint` events can verify, move, change, or remove breakpoints. Reconcile by adapter ID when available, with source/location fallback when necessary.

## 20. Execution control

Execution-changing operations include:

- `continue`
- `next` (step over)
- `stepIn`
- `stepOut`
- `pause`
- `stepBack`
- `reverseContinue`
- `restartFrame`
- `goto`
- `restart`
- `terminate`
- `terminateThreads`

For operations expected to produce a later state event:

1. Select the target session/thread.
2. Clear or invalidate prior stop state where appropriate.
3. Allocate an operation/resumption generation and register a session/thread-correlated outcome
   waiter before sending the request.
4. Send the request.
5. Wait for the targeted session/thread and generation to stop, for that session/tree to terminate,
   or for another operation-specific expected outcome, bounded by the tool timeout.
6. If stopped, fetch the top frame outside the event reader.
7. If the timeout expires while running, return a successful `running` result with `TimedOutWaitingForStop = true`.

A stop in an unrelated thread, session, or child tree never completes the operation accidentally.
Simultaneous stops are reconciled independently. A separate explicitly requested tree-wide wait may
complete when any owned member stops. A running target after a wait timeout is not a protocol failure.

## 21. Inspection and mutation operations

The session manager should implement the complete canonical request surface in semantic groups.

### 21.1 Inspection

- Threads.
- Stack trace with paging.
- Scopes.
- Variables with paging/filtering.
- Evaluate.
- Source retrieval.
- Modules with paging.
- Loaded sources.
- Exception information.
- Breakpoint locations.
- Step-in targets.
- Goto targets.
- Completions.
- Location-reference resolution.

### 21.2 State mutation

- Set variable.
- Set expression.
- Write memory.
- Breakpoint mutations.

Adapter-defined mutations are exposed only as registered typed host extensions, never as an ordinary
raw semantic request.

### 21.3 Native inspection

- Disassemble.
- Read memory.
- Instruction breakpoints.
- Data breakpoint discovery and configuration.

Every optional request must be guarded by the negotiated capability where the specification defines one.

Paging and continuation tokens are opaque, query-bound, tree-bound, protocol-session-bound, expiring,
and bounded in number. Relevant state invalidation revokes tokens whose underlying projection is no
longer current. A token from another owner, query, tree, or protocol session is rejected.

Inspection results retain protocol scoping without interpreting adapter references. `sourceReference`
takes precedence according to the DAP source contract and is resolved only through its owning
session. `adapterData` is an owned opaque value round-tripped only to the adapter that produced it.
Frame, scope, variable, data-breakpoint, and location references derived from a suspension are tagged
with the owning thread's suspension epoch. Memory reads/writes validate base64, requested bounds,
partial-result counts, and unreadable-byte metadata before creating semantic results.

## 22. Output and artifacts

### 22.1 Categorized buffer

Each session maintains a bounded buffer of records rather than one merged string:

```csharp
public sealed record DebugOutputRecord
{
    public required long Sequence { get; init; }
    public required string DebugTreeId { get; init; }
    public required string DebugSessionId { get; init; }
    public required DateTimeOffset ObservedAt { get; init; }
    public required string OriginalCategory { get; init; }
    public required DebugOutputCategory Category { get; init; }
    public string? GroupId { get; init; }
    public required string Text { get; init; }
    public required int Utf8ByteLength { get; init; }
    public required long DroppedBeforeSequenceCount { get; init; }
    public bool Truncated { get; init; }
    public DebugSourceLocation? Source { get; init; }
    public int? VariablesReference { get; init; }
}
```

Default limits should cover:

- Maximum records.
- Maximum total UTF-8 bytes.
- Maximum single record bytes.
- Oldest and newest retained sequence.
- Dropped record/byte counters.

### 22.2 Security

- Treat output as untrusted text.
- Sanitize control sequences for model/TUI rendering unless ANSI output was explicitly negotiated and the renderer is safe.
- Do not expose telemetry-category output to the model by default.
- Avoid interpreting adapter output as instructions.

### 22.3 Content storage

Live output events, retained session buffers, and stored artifacts have independent limits. Live
notifications default to 16 KiB after coalescing; retaining 256 KiB does not authorize a 256 KiB event.

When output exceeds inline limits, attempt to persist a snapshot in `IContentStore` and return a
content reference with a bounded tail. The store may be absent or fail. In that case:

- Keep the live tree running.
- Return only the bounded preview/tail.
- Return `ContentStoreUnavailable` or `OutputTooLarge` where the operation needs a typed status.
- Record a bounded diagnostic without increasing the inline ceiling.

Artifacts carry runtime/session/thread/tree/protocol-session scope and inherit the configured store's
retention policy. Raw protocol traces are disabled by default, separately bounded, redacted where
possible, and stored only as host-diagnostic artifacts. They are never placed in ordinary logs, the
durable thread journal, semantic results, or model-readable artifact collections. The semantic service
may expose only bounded capability and trace-health summaries.

## 23. Reverse request integration

### 23.1 `runInTerminal`

Introduce a host-facing abstraction implemented with agent request/response events:

```csharp
DebugRunInTerminalRequestEvent : AgentEvent, IRequestEvent
DebugRunInTerminalResponseEvent : AgentEvent, IResponseEvent
```

The request includes:

- Integrated/external preference.
- Title.
- Working directory interpreted using the `pathFormat` negotiated at initialization.
- The verbatim argument array, where `args[0]` is the executable/command.
- The exact DAP environment delta whose values are strings or null, where null deletes an inherited
  variable after policy validation.
- `argsCanBeInterpretedByShell`, retained separately from the argument data.
- Debug session identity.

The host response includes process and optional shell process IDs.

By default HPD passes the argument vector verbatim to the selected Environment process provider. It
must not join, quote, normalize, expand, or reinterpret arguments. Shell interpretation is available
only if HPD advertised support during `initialize`, the reverse request sets
`argsCanBeInterpretedByShell`, and the tree authorization and host policy approve the shell boundary.
The filtered string/null environment delta is applied using target-platform key semantics; reserved
or unauthorized deletions fail explicitly. The host response preserves the process ID and optional
shell process ID defined by DAP.

The request lifecycle must use HPD's existing race-safe ordering:

1. Create and scope the request event.
2. Call `RegisterRequest` before exposing the request to any observer.
3. Commit and publish the request when thread scope is available; otherwise emit it live.
4. Await the returned handle with a bounded timeout.
5. On response ingress, call `RespondAsync` and use its `beforeCompletion` callback to commit the
   accepted response before the waiting adapter continuation is released.
6. Cancel the handle if publication or waiting fails.

This logic belongs in a reusable background-safe `IDebugHostRequestBroker`; it cannot depend on the
originating `FunctionExecutionContext.RequestAsync` because reverse requests may arrive long after the
launch tool call returns. `RequestStartedEvent`, `RequestResolvedEvent`, expiration, cancellation, and
rejection diagnostics are supplied by the coordinator and should be observed rather than duplicated
as debugger-specific lifecycle plumbing.

### 23.2 `startDebugging`

The session manager handles this internally:

1. Validate the reverse request.
2. Preserve and validate `outputPresentation` and the adapter-originated launch/attach configuration
   as untrusted adapter input under the existing factory and host policy.
3. Resolve the same adapter type through the retained runtime binding and start the new debug session
   through its normal authorized path. A fresh adapter process and connection is valid and is the
   default unless the factory explicitly supports a safe shared-server topology.
4. Create an HPD child session regardless of whether the protocol transport is fresh or shared;
   ownership hierarchy never implies connection reuse.
5. Register all standard handlers recursively.
6. Recompose portable root breakpoint intentions and rediscover session-bound breakpoints before
   `configurationDone`.
7. Publish `DebugChildSessionStartedEvent`.
8. Return success only after the child start request reaches a valid configured state.

## 24. Result metadata

Define stable keys:

```csharp
public static class DebugToolMetadataKeys
{
    public const string SessionSnapshot = "HPD.Debug.SessionSnapshot";
    public const string StopLocation = "HPD.Debug.StopLocation";
    public const string Breakpoints = "HPD.Debug.Breakpoints";
    public const string StackFrames = "HPD.Debug.StackFrames";
    public const string Capabilities = "HPD.Debug.Capabilities";
    public const string OutputReference = "HPD.Debug.OutputReference";
    public const string Operation = "HPD.Debug.Operation";
}
```

The textual result should optimize for the model's next decision. Full structured data belongs in the result object and metadata.

## 25. Permissions and safety

### 25.1 Session authorization

Launch and attach require approval that identifies the adapter, target, environment, process or
registered endpoint, working directory, isolation posture, and whether the operation may create child
or terminal processes. Successful approval creates a bounded authorization attached to the owned
debug tree.

That authorization normally covers routine operations on the same tree:

- Status, threads, stack, scopes, variables, sources, modules, and output.
- Continue, pause, ordinary stepping, and termination.
- Standard source/function/exception/instruction breakpoint configuration.
- Child sessions and terminal launches already described by the approved launch plan and policy.

It does not automatically authorize:

- Evaluation when host policy treats expressions as executable code.
- Variable or expression mutation.
- Memory writes.
- A new process, environment, endpoint, credential, or network trust boundary.
- `runInTerminal` outside the approved launch scope.
- Adapter-specific privileged host operations.

Authorization is bound to runtime/session/thread/tree ownership, policy revision, environment revision,
and optional endpoint revision. It cannot be transferred to another tree. Repeated continue/step and
breakpoint operations do not prompt again unless they cross the approved scope.

### 25.2 Workspace and path safety

- Normalize paths before matching or sending them.
- Enforce configured workspace boundaries for automatic root discovery.
- Permit remote/virtual sources only through explicit DAP source references.
- Do not follow unapproved executable paths discovered from untrusted project configuration.
- Treat project-local adapter configuration as untrusted input.

### 25.3 Attach safety

Attaching to arbitrary processes is materially different from launching a workspace target. It
requires explicit permission and exposes the target process identity or opaque endpoint identity in
the approval prompt. Raw credentials and resolved endpoint addresses are not exposed to the model.

### 25.4 Custom requests

The ordinary semantic service does not expose raw custom DAP requests. Permission cannot make unknown
effects understandable. The complete protocol engine retains custom dispatch only for registered
trusted host extensions, adapter packages, compatibility tests, and explicitly authorized host
diagnostics. Such extensions define their own typed contracts and policy.

## 26. Serialization and AOT

Use two generated contexts:

- `DapJsonContext` for every canonical generated wire type.
- `CodingDebugJsonContext` for semantic requests/results/snapshots/artifacts and debugger agent events.

Native AOT acceptance requires no adapter reflection scan, no runtime schema generation, no
arbitrary-object protocol serialization, direct generated DI resolvers, preserved adapter factories
under trimming, and debugger events registered with generated `JsonTypeInfo`. A published Native AOT
smoke application must complete a simulated root/child lifecycle, reverse request, event round-trip,
and artifact fallback.

## 27. Observability

Metrics should include:

- Adapter resolution duration and outcome.
- Connection/initialize/launch duration.
- Request count, latency, timeout, and cancellation by command.
- Session count and lifetime.
- Stop reasons.
- Output bytes retained/dropped.
- Child-session count.
- Protocol parse/resynchronization failures.
- Adapter exits and failure categories.

Logs must avoid leaking evaluated values, environment secrets, raw memory, or full output by default.

Optional raw protocol tracing is a trusted host-only facility: bounded, disabled by default, redacted
where possible, and stored under a host-diagnostic artifact classification rather than emitted into
normal logs or semantic/model-facing surfaces.

## 28. Error model

Define normalized categories:

```csharp
public enum DebugErrorCategory
{
    InvalidRequest,
    AdapterNotFound,
    AdapterUnavailable,
    AdapterAmbiguous,
    InvalidConfiguration,
    PermissionDenied,
    RemoteEndpointDenied,
    SessionNotFound,
    SessionUnavailable,
    SessionOwnershipMismatch,
    InvalidSessionState,
    CapabilityUnavailable,
    AdapterRequestFailed,
    TransportFailure,
    ProtocolViolation,
    RequestTimedOut,
    RequestCancelled,
    DebuggeeExited,
    AdapterExited,
    OutputTooLarge,
    ContentStoreUnavailable,
    InternalFailure
}
```

Errors returned to the model should include an actionable summary and safe details. Internal exceptions and adapter stderr remain in logs/artifacts unless safe and useful.

`AdapterRequestFailed` represents a well-formed DAP response with `success: false`. It retains the
safe structured adapter failure described by the protocol-client boundary and is never normalized to
`ProtocolViolation`. `ProtocolViolation` is reserved for invalid framing, malformed messages,
impossible correlation, and other failures of the wire contract.

Adapter declarations/providers may supply availability remediation without shared runtime branches on adapter ID.

## 29. Timeouts and limits

All defaults are host-overridable downward and validated against hard ceilings:

| Limit | Default |
|---|---:|
| Initialize/start request | 30 seconds |
| Ordinary DAP request | 10 seconds |
| Adapter connection readiness | 10 seconds |
| Protocol write | 30 seconds |
| Initial stop capture | 5 seconds |
| Continue/step observation | 30 seconds |
| Disconnect cleanup | 5 seconds per tree; 15 seconds aggregate |
| Protocol header/body | 16 KiB / 4 MiB |
| Pending requests | 128 per protocol session |
| Recent semantic events | 256 per protocol session |
| Threads/frames/scopes page | 100 / 100 / 64 |
| Variables/modules/instructions page | 200 / 200 / 256 |
| Name/type/value text | 1 KiB / 1 KiB / 16 KiB |
| Evaluate inline output | 64 KiB |
| Memory read/write | 64 KiB / 4 KiB per operation |
| Adapter diagnostics | 64 KiB per protocol session |
| Retained debug output | 256 KiB per protocol session |
| Live output event | 16 KiB |
| Continuation tokens | 128 per tree; five-minute expiry |
| Concurrent/rate reverse requests | 16 / 60 per minute |
| Environment overrides | 32 entries; 128-byte keys; 4-KiB values |

Idle cleanup is disabled for actively running owned roots unless host policy says otherwise. Raw
protocol tracing has an independent host-controlled hard limit. Limits count UTF-8 bytes wherever
wire, memory, or storage size matters. Raising a ceiling requires trusted host policy and cannot come
from model or project input.

## 30. Testing strategy

### 30.1 Canonical protocol generation

- Every official schema definition is generated or explicitly classified.
- Required, optional, and explicitly nullable properties remain distinct; `allOf` inheritance maps
  correctly.
- Exact wire names are generated and tested, including easily confused singular capability names.
- Open enums preserve unknown strings.
- Extension data round-trips.
- Every generated type is covered by `DapJsonContext`.
- Request descriptors bind the correct command, arguments, response, and metadata.
- Generated XML documentation and schema revision metadata are present.
- Regeneration from the pinned schema produces no diff.
- Normal compilation and regeneration cannot emit duplicate compiled protocol types.
- Direction classification uses schema structure and reviewed overrides for ambiguous reverse requests.
- Generated headers and repository notices contain the approved upstream licensing attribution.
- Generated notices distinguish MIT-licensed code from Creative-Commons-Attribution specification
  text and derived documentation.
- No semantic operation is generated from the wire schema.

### 30.2 Adapter catalog generation and policy

- Static declarations bind to the shared DI factory without constructing it.
- Behavioral declarations generate direct DI resolvers and support constructor injection.
- Every diagnostic has positive and negative cases.
- Generated code compiles on supported targets without reflection.
- Multiple registered catalog providers compose explicitly.
- Duplicate IDs fail with provider provenance.
- Package provenance claims cannot grant trust; host verification produces the effective trust decision.
- Resolver failures follow built-in-fatal/optional-host-policy behavior with bounded diagnostics.
- Explicit, automatic, unavailable, no-match, and ambiguous selection work.
- Environment/workspace/policy/endpoint revisions isolate availability caches.
- Concurrent probes coalesce and endpoint revocation invalidates entries.
- Environment allowlists and reserved-key rules fail closed without leaking values.
- Missing tools return bounded guidance without raw probe output.

### 30.3 Protocol client and transport tests

- Arbitrary header and body chunking.
- Multiple messages in one read.
- UTF-8 content length.
- ASCII/CRLF header grammar; exactly one supported `Content-Length`; zero, duplicate, negative,
  overflowing, missing, and over-limit lengths.
- Out-of-order and duplicate responses; correlation validates both `request_seq` and command.
- `success: false` becomes a redacted structured adapter failure rather than a protocol violation.
- Cancellation/response/disconnect races and late responses.
- DAP cancel supported and unsupported behavior.
- Progress cancellation by `progressId`, request-linked progress, non-cancellable progress, late
  request responses, and progress remaining live until `progressEnd`.
- Pending-request and sequence limits.
- Write timeout.
- Reverse request success, rejection, timeout, and publication failure.
- Event-handler isolation and reader-loop reentrancy prohibition.
- Malformed frame/JSON default failure and compatibility resynchronization.
- Oversized headers/bodies and invalid UTF-8.
- Protocol stdout contamination.
- HPD Environment stdout/stderr separation, process stop, and environment loss.
- Exactly one handle-output enumeration demultiplexes tagged stdout/stderr.
- Borrowed output chunks are copied before asynchronous retention.
- Protocol stdout is ordered/lossless or faults at a hard limit; stderr drops are counted and cannot
  stall stdout.
- Pump failure/exit/disposal complete both consumers and settle requests exactly once.
- Start orchestration resolves `IProcessProvider` once from `FunctionExecutionContext` and passes the
  returned handle—not the context or capability registry—to the transport.
- Adapter executable and arguments remain direct `ProcessCommandSpec` fields and never become a shell
  command string.
- Approved TCP/Unix-socket and deterministic in-memory transports.

### 30.4 Session trees and lifecycle

- Root and multiple child sessions with deterministic active-member selection.
- Partial thread stop/continue, all-thread flags, derived partially-stopped session state, per-thread
  suspension epochs, and simultaneous child/thread stops.
- Cross-runtime/session/thread ownership rejection.
- Manager-owned opaque runtime identity is stable for one runtime and is not derived from agent or
  conversation identity.
- `initialized` before and after launch/attach response.
- Launch response withheld until `configurationDone`.
- Configuration completion exactly once.
- Typed initial configuration populates desired state before breakpoint replacement and
  `configurationDone`.
- Stop event in same packet as response.
- Continue timeout while target remains running.
- Operation-correlated waiters reject unrelated thread/session/child stops; an explicit tree-wide
  waiter retains first-owned-stop semantics.
- Restart request support, fresh-session fallback, and opaque `terminated.restart` to `__restart`
  relay for both launch and attach.
- Operation cancellation before and after tree publication.
- Failure-atomic gated tree/handle publication never exposes a selectable handle-less tree or emits a
  started event after rollback.
- Adapter crash and environment-loss isolation.
- Captured runtime-binding invalidation rejects later child starts without host-local fallback.
- Whole-tree and single-child termination policy.
- Thread, runtime, environment, handle, and service teardown.
- Historical journal IDs remain unavailable after reconstruction.

### 30.5 Breakpoints and state

- Concurrent mutations never lose updates.
- Every breakpoint family uses full replacement semantics.
- Desired state survives child creation and confirmed state differs per child.
- Session/suspension-bound data IDs are never copied; `canPersist` and per-child rediscovery are
  honored; instruction/memory references require explicit portability.
- Breakpoint events verify, move, change, and remove.
- Breakpoint-event reconciliation preserves the original desired request properties while updating
  adapter-confirmed identity, verification, and resolved locations.
- Continue/stopped/invalidated events invalidate the correct projections.
- Memory invalidation respects overlap only for the same opaque `memoryReference`.
- Dynamic capabilities alter operation availability in both directions, including explicit false.
- Frame/variable/source/location/adapter-data references remain opaque, session-bound, and
  suspension-epoch-bound where required.
- Base64 memory payloads, partial reads/writes, and unreadable-byte counts are validated.

### 30.6 Agent integration, events, and artifacts

- `FunctionExecutionContext` is injected and not model-visible.
- Manager resolution and disposal are runtime-scoped.
- Handle registration uses correct HPD session/thread identity.
- Durable events commit before live publication and live-only events never enter the journal.
- Terminal events are not lost to expired event flows.
- Reverse-request responses commit before adapter continuation release.
- `runInTerminal` preserves verbatim argv, null environment deletions, negotiated path format,
  shell-interpretation gating, and process/shell process IDs.
- `startDebugging` supports fresh adapter connections, preserves `outputPresentation`, validates
  adapter configuration, and keeps HPD ownership separate from transport topology.
- Event registration is atomic, idempotent, collision-safe, and round-trips through `AgentEventSerializer`.
- Output/progress coalesce and respect backpressure and byte limits.
- Missing/failing content stores produce bounded fallback without faulting the tree.
- Secrets, expressions, values, memory, endpoint details, and raw payloads stay out of default logs/events.
- Raw protocol traces are inaccessible from semantic results and ordinary handle artifacts.
- Result metadata is populated.
- Semantic operations expose the expected typed contracts.
- Session authorization covers routine control while privileged mutations reauthorize.
- Registering only `CodingToolHarness` auto-selects the referenced `DebugToolHarness` factory without
  adding it to `_explicitlyRegisteredToolHarnesses`.
- Generated referenced-function filters materialize every and only the debug functions named by
  Coding-owned skills.
- Before Coding expansion, debugging skills and debug functions are hidden; after Coding expansion,
  skill activations are visible; after skill activation, only that skill's referenced functions are
  visible.
- A debug function referenced by multiple skills receives deduplicated alternative skill parents and
  is visible when any one of them is active.
- One comprehensive skill may reveal the complete permitted debugger surface, multiple focused skills
  may partition or overlap it, and deliberate direct Debug harness registration may expose it without
  skills; every configured mode produces the same function identities and permission behavior.
- Missing harness/member references, duplicate model-facing names, and capability-graph cycles fail
  deterministically during generation, materialization, or graph validation as appropriate.

### 30.7 Adapter integration tests

Use optional environment-gated suites for installed adapters:

- debugpy.
- netcoredbg.
- gdb.
- lldb-dap.
- Delve.
- JavaScript debug adapter.

Test launch, breakpoint, stop, inspect, continue, and terminate for each. Do not make local adapter installation mandatory for ordinary unit-test runs.

### 30.8 AOT and trimming tests

- Publish and execute a Native AOT sample.
- Compose built-in and external generated catalogs through DI.
- Resolve static and behavioral factories with trimming enabled.
- Serialize every descriptor family and representative events with generated contexts.
- Complete root/child, reverse-request, artifact, and bounded-fallback lifecycles.
- Fail on debugger-owned reflection/trimming warnings.

## 31. Delivery plan

### Phase 0: Protocol and design baseline

Deliverables:

- Pin the official schema version and upstream commit.
- Approve upstream licensing/attribution and implement the explicit engineering code-generation tool.
- Generate and check in deterministic complete wire/context/descriptor output.
- Generate the baseline canonical feature inventory.
- Add a feature matrix covering every request, event, reverse request, and capability.
- Finalize public naming and namespaces.
- Reserve `Debugging` folders/namespaces inside the existing Coding runtime and source-generator
  projects; add no product project or package boundary.
- Define `DebugToolHarness` as a separate generated harness class and declare Coding-owned skills
  using typed `SkillCapabilities.Function<DebugToolHarness>` references.
- Implement collision-safe shared event registration and approve background-handle changes.

Acceptance criteria:

- No canonical DAP message is unclassified.
- Regeneration is deterministic and produces no diff.
- Normal builds compile only checked-in protocol output and cannot duplicate generated types.
- Every advertised client capability has an implementation owner.
- No adapter-name special case is planned in shared session code.
- Registering the collapsed Coding harness automatically selects the filtered Debug harness
  dependency; no separate Debug builder registration is required and no Debug container becomes
  top-level visible by default.
- The combined analyzer build emits language-server, debug-adapter, harness, and skill-reference
  metadata without collisions.

### Phase 1: Generated adapter catalog

Deliverables:

- Attributes.
- Descriptor, catalog-provider, factory, and generated-resolver contracts.
- Standard DI factory.
- Generator and diagnostics.
- Explicit cross-package registration and immutable catalog composition.
- Initial adapter declarations.
- Package provenance and host-derived trust decisions.
- Environment/policy/endpoint-aware probing and cache isolation.

Acceptance criteria:

- Registry works without runtime reflection.
- Invalid declarations fail at compile time.
- Static and behavioral factories resolve through the same DI contract.
- Duplicate IDs and invalid factories fail deterministically.
- Catalog trust and resolver failure policy are enforced before selection.
- No reflection scan or parameterless-provider requirement exists.

### Phase 2: Protocol and transport foundation

Deliverables:

- Typed descriptor dispatch and framer.
- Explicit initialize/client-capability policy and structured adapter-error mapping.
- HPD Environment single-output-pump stdio transport and approved socket seams.
- Correlation, events, reverse requests, cancellation, DAP cancel, and late responses.
- Strict response command correlation and progress-ID cancellation tracking.
- Strict malformed-input, bounded diagnostics, limits, and disposal.

Acceptance criteria:

- Protocol conformance tests pass.
- Pending requests always settle on timeout, cancellation, or exit.
- No reader deadlocks when event handlers schedule follow-up requests.
- Adapter processes launch only through `IProcessProvider`.
- Protocol stdout cannot be dropped and diagnostics cannot block the output pump.

### Phase 3: Core sessions and semantic service

Deliverables:

- Per-runtime session manager.
- Manager-owned opaque runtime identity and retained-binding lifetime.
- Owned root/child state machines.
- Per-thread execution state, suspension epochs, and operation-correlated outcome waiters.
- Typed initial configuration and failure-atomic gated tree/handle publication.
- Endpoint/configuration/environment policy.
- Race-safe launch/attach and exactly-once configuration.
- Restart request, fresh-session fallback, and opaque adapter restart-data relay.
- Debug background handle.
- Lifecycle observer.
- Core lifecycle functions.
- Typed execution and inspection operations.
- Typed lifecycle events.

Acceptance criteria:

- One root session can launch, stop on entry, inspect state, continue, and terminate.
- No global mutable session singleton exists.
- Runtime shutdown disposes the adapter and debuggee according to policy.
- Cancellation after publication does not terminate the tree.
- Environment/runtime invalidation prevents later child starts through stale capabilities.
- Historical IDs cannot become live after replay.

### Phase 4: Breakpoints and child sessions

Deliverables:

- All breakpoint kinds.
- Serialized mutations.
- `runInTerminal`.
- `startDebugging` child trees.
- Exact reverse-request wire semantics, fresh child transports, operation-scoped and explicit
  tree-wide stop waiting, portable breakpoint propagation, and per-child rediscovery.
- Session-scoped authorization.

Acceptance criteria:

- Child sessions inherit only portable desired breakpoint intentions and rediscover session-bound
  breakpoint identities before configuration completion.
- A stopped child becomes the active inspection target.
- Tree termination is deterministic.

### Phase 5: Complete state/event integration

Deliverables:

- Dynamic capability changes.
- Invalidation.
- Thread/process/module/source/breakpoint/memory events.
- Progress and DAP cancellation.
- Categorized output artifacts.
- Durable/live publication policy and debugger projection.
- Missing-content-store fallback.
- Host-only raw protocol diagnostic artifacts and safe semantic health summaries.

Acceptance criteria:

- Cached state is never presented as current after relevant invalidation.
- Every advertised client capability has corresponding behavior.
- High-volume output/progress cannot flood memory or the thread journal.

### Phase 6: Advanced protocol surface

Deliverables:

- Exception information/configuration.
- Source retrieval.
- Set variable/expression.
- Reverse execution.
- Goto/restart-frame.
- Native memory/disassembly.
- Completions and location references.
- Typed trusted-host adapter extensions.

Acceptance criteria:

- Every canonical request is supported, explicitly deferred, or rejected with a typed reason.
- Optional tools are capability-gated.
- Ordinary semantic APIs expose no raw custom request.

### Phase 7: Product integration

Deliverables:

- TUI debug status and session views.
- Documentation and adapter install guidance.
- Evaluation scenarios.
- Compatibility matrix.
- Native AOT verification.

Product integration is a separate gate over the reusable core. TUI completion does not block core
debug-harness completion; it consumes the same semantic service, projections, permissions, and
artifacts as other hosts.

### Phase 8: Production qualification

Deliverables:

- Required Native AOT smoke applications.
- At least two materially different adapters in required CI.
- Platform/environment-specific suites.
- Malformed-protocol, crash, cancellation, disposal, soak, and resource-limit suites.
- Security review of endpoints, environment values, logs, events, artifacts, and authorization.

Acceptance criteria:

- The complete feature matrix, AOT, isolation, ownership, security, and resource gates pass.
- No advertised capability lacks tested behavior.
- Every teardown path releases its transport, process, tasks, waiters, and handles.

## 32. Feature matrix requirement

Before implementation, add a checked-in matrix with one row for every canonical feature and these columns:

```text
Feature
Protocol kind and direction
Schema type and generated descriptor
Required or related capability
Protocol/session/host owner
Semantic-service exposure
Exposing skills or deliberate direct-harness surface
Adapter provenance/trust requirement
Authorization/permission class
State precondition and mutation
Reference/session/suspension lifetime
Agent-event and durability effect
Delivery dependency
Implementation and test status
```

This prevents silent protocol omissions and prevents the client from advertising capabilities it does not actually implement.

## 33. Key architectural invariants

The implementation is acceptable only if all of these remain true:

1. Debugging ships inside the existing Coding runtime and source-generator projects as a separate
   generated `DebugToolHarness` class, without a second assembly or package. The default Coding-agent
   path registers it only as a filtered skill dependency, not as an explicitly registered top-level
   harness.
2. `DebugToolHarness` has no default `[Collapse]` container. Coding-owned skill activations are the
   inner containers and reveal selected debug functions by typed reference.
3. Skill grouping is replaceable presentation policy: one skill, multiple overlapping skills, or
   deliberate direct harness exposure reuses the same semantic functions and cannot change protocol,
   ownership, or authorization behavior.
4. The official DAP schema, not a reference client's subset, defines completeness.
5. Canonical wire types, JSON metadata, descriptors, directions, and feature inventory are produced
   deterministically by an explicit licensed engineering tool and checked into source exactly once.
6. No process-global mutable debug-session manager or tree-less active session exists; manager-owned
   opaque runtime identity is not derived from agent or conversation identity.
7. Every live root tree has complete runtime/session/thread/environment ownership and a required
   background handle established through failure-atomic gated publication.
8. `FunctionExecutionContext` is invocation-only and is never retained by a tree, observer, handler,
   or reverse-request callback.
9. Stable adapter metadata and provenance are generated at compile time; trust is a host-derived
   decision, and availability/policy/endpoints/transport/launch plans are runtime factory decisions.
10. Behavioral factories support constructor injection and are never reflection-scanned or required to
   have parameterless constructors.
11. Adapter processes execute through the `IProcessProvider` selected from the initiating invocation;
   remote connections use only authorized transport plans.
12. Runtime capability resolution occurs once during start orchestration. The transport owns an
   `IProcessInvocationHandle` and never independently chooses a provider.
13. Retained runtime bindings are invalidated on Environment/runtime loss; child starts never fall back
    to host-local execution, and trees never dispose shared runtime services.
14. Exactly one output pump enumerates each Environment handle; borrowed chunks are copied, protocol
    stdout is never dropped, and diagnostic drops are bounded and counted.
15. The protocol client accepts typed descriptors, never serializes arbitrary request objects, and does
    not own semantic session state; the session manager does not parse wire framing.
16. Every pending request and reverse request settles exactly once under response, cancellation,
    timeout, transport exit, pump failure, and disposal races; well-formed unsuccessful responses
    remain structured adapter failures rather than protocol violations.
17. Typed initial desired configuration is recorded before configuration, which completes exactly once
    without assuming launch-response/`initialized` ordering.
18. Meaningful asynchronous changes become bounded typed events with explicit durable/live policy.
19. Event registration is atomic, collision-safe, idempotent, and AOT metadata-backed.
20. Large/high-frequency output is bounded and referenced rather than blindly journaled; content-store
    failure never increases inline limits.
21. Raw protocol traces remain host-only diagnostic artifacts and never enter semantic results,
    ordinary handle artifacts, logs, or the durable journal.
22. `initialize` is sent first and exactly once; advertised client capabilities correspond to active
    end-to-end handlers and policy, while dynamic adapter capability removal takes effect.
23. Session summaries derive from per-thread state; partial stop/continue events affect only their
    identified threads and advance the correct suspension epochs.
24. Breakpoint mutations are serialized; only portable intentions propagate across the tree, while
    confirmed and suspension-bound data/instruction identities remain per protocol session.
25. Outcome waiters are installed before triggering requests and correlate session, thread, and
    resumption generation; unrelated stops never satisfy them.
26. Cancellation after tree publication never implicitly terminates the tree.
27. Ordinary semantic endpoints are opaque IDs, environment overrides are filtered, and raw custom
    requests are host-only typed extensions.
28. Continuation tokens and adapter references are owner/session/query/state- or suspension-bound,
    opaque, expiring where HPD-created, bounded, and revoked by invalidation.
29. Tree termination, child disconnect, debuggee termination, detach, restart, and forced transport
    disposal remain distinct semantic operations.
30. Adapter restart data is relayed opaquely as `__restart`; lack of adapter restart support uses a
    freshly authorized adapter/session rather than inventing in-place semantics.
31. Reverse requests preserve DAP argv/environment/path/output-presentation semantics, and HPD child
    ownership never requires protocol connection reuse.
32. Progress cancellation is a best-effort hint keyed by `progressId`; progress remains live until
    `progressEnd` or session cleanup.
33. Memory overlap is computed only within the same opaque `memoryReference`.
34. Runtime/thread/environment/handle/service teardown attempts every owned cleanup using bounded
    internal tokens.
35. Historical journal identities never reactivate protocol connections after restart.

## 34. Final recommendation

Proceed inside the two existing projects, `HPD-Agent.Harness.Coding` and
`HPD-Agent.Harness.Coding.SourceGenerator`, plus the explicit engineering tool
`eng/HPD-Agent.DebugProtocol.CodeGen`. Do not create a debug product assembly, debug analyzer
assembly or separate package. Use a distinct `DebugToolHarness` class plus `Debugging` folders and
namespaces as the maintenance boundary. Keep deployment and versioning with the Coding assembly;
keep default model-facing ownership under Coding-owned skills.

Register only `CodingToolHarness` in the normal Coding agent. Its generated skill references must
auto-select the filtered `DebugToolHarness` dependency through the existing factory catalog. Do not
add a routine `WithToolHarness<DebugToolHarness>()` call: that would mark the debugger explicitly
registered and change its top-level visibility semantics. The default disclosure graph is the outer
Coding collapse, then a task-oriented debugging skill, then only the debug functions referenced by
that skill.

Build the adapter catalog using the established language-server declaration pattern, improved so
generated entries resolve constructor-injected factories through direct DI delegates. Keep stable
classification in generated descriptors and runtime truth in environment-, endpoint-, and
policy-aware factories. Generate and check in the complete typed protocol contract and descriptors
from the pinned, attributed official DAP schema. Run adapter processes through HPD Environment and remote transports through the
authorized endpoint boundary. Use the practical reference implementation only for compatibility and
concurrency evidence. Reuse HPD's invocation context, runtime capabilities, background resources,
events, request broker, permissions, and content store rather than recreating a private host runtime.

The resulting implementation is comprehensive rather than a permanently reduced first version:
smaller in adapter-specific registration code than the reference, complete against canonical protocol
truth, capability-correct, Native AOT-compatible, safe across concurrent agent runtimes and
environments, bounded under hostile or malformed adapters, and naturally integrated with HPD's
event-driven execution model.
