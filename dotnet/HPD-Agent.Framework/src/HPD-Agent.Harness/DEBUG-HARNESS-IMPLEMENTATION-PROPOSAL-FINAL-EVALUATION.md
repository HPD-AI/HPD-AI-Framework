# Final Evaluation of the HPD Debug Harness Architecture

Status: Final architectural evaluation  
Date: July 22, 2026  
Evaluates: `DEBUG-HARNESS-IMPLEMENTATION-PROPOSAL.md`  
Supersedes: `DEBUG-HARNESS-IMPLEMENTATION-PROPOSAL-EVALUATION.md`

## 1. Verdict

The updated implementation proposal is the strongest available foundation for HPD
debugging. It should become the single canonical implementation architecture after
the corrections identified in this evaluation.

The proposal now combines:

- complete protocol truth generated from the official DAP schema;
- compile-time validation of stable adapter catalog metadata;
- direct DI resolution of runtime adapter factories;
- HPD Environment process execution and isolation;
- transport-neutral protocol operation;
- per-agent-runtime root and child session trees;
- race-safe initialization, configuration, execution, and teardown;
- desired versus adapter-confirmed breakpoint state;
- background handles and observers;
- durable and live-only HPD event publication;
- background-safe reverse requests;
- bounded output, artifacts, protocol traffic, and content-store fallback;
- complete advanced, native, child-session, and product scope;
- Native AOT and trimming as acceptance requirements.

The remaining issues are concrete contract and implementation details rather than
fundamental architectural disagreement. None requires abandoning the combined
design.

This evaluation intentionally addresses the debugger core, semantic service, and
host integration only. Presentation-layer organization is outside its scope.

## 2. Relationship to earlier documents

### 2.1 Updated implementation proposal

`DEBUG-HARNESS-IMPLEMENTATION-PROPOSAL.md` becomes canonical after revision. It is
the only document that should direct implementation.

### 2.2 Earlier evaluation

`DEBUG-HARNESS-IMPLEMENTATION-PROPOSAL-EVALUATION.md` remains useful decision
history. Its runtime safety, protocol generation, Environment integration,
ownership, cancellation, endpoint, storage, and Native AOT findings were largely
adopted.

Two of its recommendations are superseded:

- The adapter catalog generator is retained in a hybrid form rather than removed.
- Event registration follows a hardened shared registry and the existing Coding
  harness initialization pattern rather than debugger-local explicit-only setup.

### 2.3 Native DAP proposal

The separate Native DAP proposal is now an earlier design. Its strongest details—
endpoint IDs, limits, content-store failure, cancellation ownership, teardown,
errors, transport diagnostics, and AOT boundaries—have been incorporated into the
updated implementation proposal.

It should not remain a competing active specification.

## 3. Architecture accepted without further redesign

### 3.1 Canonical protocol generation

The official `debugAdapterProtocol.json` is the sole wire-contract authority. A
deterministic generator produces:

- every canonical message and supporting type;
- typed request, event, and reverse-request descriptors;
- command/arguments/response bindings;
- source-generated JSON metadata;
- open-string enum handling;
- extension-data seams;
- XML documentation;
- upstream version and commit metadata;
- a baseline feature inventory.

The runtime never interprets the schema dynamically and never creates reflection
metadata for protocol types.

### 3.2 Hybrid adapter catalog

The hybrid cold/runtime split is accepted:

```text
Adapter declarations
    -> generated immutable descriptors and direct DI delegates
    -> DI-backed runtime factories
    -> environment-, endpoint-, and policy-authorized launch plans
```

Compile-time declarations own stable facts:

- ID;
- languages and extensions;
- root markers;
- target kinds;
- package command and argument hints;
- installation-guidance identity;
- priority;
- enabled and experimental state;
- package provenance.

Runtime factories own runtime truth:

- installation and version;
- selected Environment;
- executable resolution;
- host policy;
- endpoint and authority resolution;
- transport selection;
- filtered environment;
- final launch or attach payload.

Generated code resolves factories through direct DI delegates. It never constructs
behavioral providers with parameterless constructors and never scans assemblies.

### 3.3 Protocol and transport separation

`DebugProtocolClient` is transport-neutral. The default stdio implementation uses:

```text
IProcessProvider.StartAsync(ProcessInvocationSpec)
    -> IProcessInvocationHandle
    -> DebugEnvironmentProcessTransport
```

Adapter processes are direct executable-plus-argument invocations, never shell
strings. Approved TCP, Unix-socket, callback, and deterministic in-memory transports
implement the same protocol transport contract.

### 3.4 Typed protocol dispatch

Generated request descriptors bind command, arguments, response, and both JSON
metadata objects. Ordinary protocol dispatch accepts no arbitrary object.

Adapter-specific launch and attach extensions are factory-owned `JsonElement`
values created with factory-owned generated metadata and validated policy.

### 3.5 Per-runtime session trees

One manager belongs to one agent runtime. A root debugging operation owns a tree
containing its root and adapter-created child protocol sessions.

The tree owns:

- complete ownership scope;
- environment binding;
- authorization;
- desired breakpoint and exception configuration;
- lifetime token;
- background handle;
- output and artifact policy;
- root and child membership;
- deterministic active-member selection.

Individual protocol sessions own negotiated capabilities, protocol state,
adapter-confirmed breakpoints, projections, output, progress, exit, and failure.

### 3.6 Race-safe lifecycle

Initialization and configuration do not assume ordering among:

- launch or attach response;
- `initialized` event;
- `configurationDone` dependency;
- immediate stopped, exited, or terminated events.

Configuration is exactly once. Outcome waiters are installed before requests that
may immediately trigger events. Follow-up requests never block the sole protocol
reader.

### 3.7 Complete breakpoint architecture

The root tree owns desired source, function, exception, data, and instruction
breakpoints. Each protocol session owns confirmed adapter results.

Mutations are serialized by tree and breakpoint family. Child sessions inherit
desired configuration before configuration completion. Later breakpoint events
reconcile verification, movement, changes, and removal.

### 3.8 State reconciliation

The client handles every canonical event. Stopped-state projections are invalidated
on continue and replaced on stop. `invalidated`, `memory`, `thread`, `module`,
`loadedSource`, `process`, `breakpoint`, and `capabilities` events reconcile the
corresponding state.

Stale state is never returned as current.

### 3.9 Event architecture

Debugger events distinguish durable, live-only, and process-local telemetry.
`EventFlowId` remains an interruptible-flow mechanism, not the root-tree correlation
identifier.

The background-owned event publisher never retains invocation context. Durable
events commit to the thread journal before live publication. High-frequency output,
progress, and reconstructible churn remain live-only or struct telemetry.

### 3.10 Reverse requests

Late reverse requests use a background-safe broker. Requests are registered before
publication. Accepted responses commit before the waiting adapter continuation is
released. Publication, response, cancellation, rejection, expiration, and teardown
all settle the request exactly once.

### 3.11 Authorization and security

Launch or attach approval creates a bounded tree authorization. Routine inspection,
execution control, termination, and standard breakpoint management remain within
that authorization.

Evaluation, mutation, memory writes, new processes, new endpoints, credentials,
network boundaries, and out-of-scope terminal launches require separate policy or
approval.

Ordinary endpoint input is an opaque ID. Environment overrides are deny-by-default
and bounded. Raw custom protocol requests remain trusted host extensions rather than
ordinary semantic operations.

## 4. Required corrections before implementation

### 4.1 Fix catalog-entry accessibility

The proposal currently defines a public record with an internal required member:

```csharp
public sealed record DebugAdapterCatalogEntry
{
    public required DebugAdapterDescriptor Descriptor { get; init; }
    internal required DebugAdapterFactoryResolver FactoryResolver { get; init; }
}
```

A required member cannot be less visible than its public containing type, and
external catalog providers must be able to create complete entries.

Use:

```csharp
public sealed record DebugAdapterCatalogEntry
{
    public required DebugAdapterDescriptor Descriptor { get; init; }
    public required DebugAdapterFactoryResolver FactoryResolver { get; init; }
}
```

The resolver is trusted executable catalog infrastructure, not serializable domain
data. JSON contexts must not register catalog delegates.

### 4.2 Define one Environment output pump

`IProcessInvocationHandle.ReadOutputAsync` produces one tagged sequence containing
stdout and stderr. Protocol and diagnostic consumers must not enumerate it
independently.

`DebugEnvironmentProcessTransport` owns one pump:

```text
ReadOutputAsync
    -> copy borrowed bytes when necessary
    -> tagged demultiplexer
       -> lossless bounded/backpressured stdout channel
       -> bounded stderr diagnostic channel with drop counters
```

Required behavior:

- only one handle-output enumeration;
- stdout is never silently dropped;
- stderr may truncate only under configured policy and reports dropped bytes;
- borrowed buffers are copied before their valid lifetime ends;
- per-stream ordering is retained;
- final markers and process exit complete both channels;
- pump failure faults the transport and settles pending requests;
- disposal stops the handle, terminates the pump, and completes consumers exactly
  once.

`ReadProtocolAsync` reads from the stdout channel. `ReadDiagnosticsAsync` reads from
the diagnostic channel.

### 4.3 Define runtime identity

The current runtime does not expose the proposed `AgentRuntimeRegistrationId` as an
existing invocation-context property.

The per-runtime manager should own a generated stable opaque identity:

```csharp
public interface IDebugSessionManager
{
    string RuntimeId { get; }
}
```

`DebugRuntimeBinding.Capture` uses `SessionManager.RuntimeId` with the invocation's
session and thread IDs. If a general `IAgentRuntimeIdentity` capability is later
introduced for multiple subsystems, the manager may use that instead.

The debugger must not derive runtime identity from agent name or conversation ID.

### 4.4 Make tree publication atomic with handle ownership

A tree must not become externally addressable before its required root background
handle exists.

Use a publication transaction:

1. create and configure unpublished tree;
2. reserve a non-addressable manager entry;
3. register the root background handle;
4. attach handle registration to the tree;
5. commit the manager entry as live;
6. activate observers under the tree token;
7. publish the durable started event.

Any failure rolls back the reservation, settles pending work, stops the transport,
disposes resources, and completes or stops the registered handle where necessary.
No started event is emitted for a failed publication.

### 4.5 Clarify protocol code-generation mechanics

The protocol output is checked in and CI verifies regeneration. Therefore the
protocol generator should be an engineering/build tool, not a Roslyn analyzer that
also emits the same compiled types.

Recommended project:

```text
eng/HPD-Agent.DebugProtocol.CodeGen
```

Normal builds compile checked-in `.g.cs` files. Regeneration runs explicitly and CI
fails on a dirty diff. Schema upgrades produce reviewable source, feature-inventory,
and licensing changes.

The adapter catalog generator remains a Roslyn incremental generator because its
input is consumer compilation symbols.

### 4.6 Keep raw protocol traces host-only

Raw traces may contain expressions, values, memory, source, environment-derived
data, endpoint details, and adapter extensions. They remain disabled by default,
separately bounded, redacted where possible, and stored as host diagnostic
artifacts.

The semantic service may expose safe capability and health summaries. It must not
return raw protocol traces.

### 4.7 Define typed initial configuration

The unpublished tree must obtain initial desired configuration before the exactly-
once configuration boundary.

Define:

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

Launch and attach semantic requests optionally carry this typed configuration. The
tree records it as desired state before sending any breakpoint replacement or
`configurationDone`.

Later breakpoint mutations use the same desired-state store.

### 4.8 Specify retained runtime-capability lifetime

The tree may retain selected runtime service references because child sessions can
start after the initiating invocation ends.

The contract must state:

- selected services are valid until agent-runtime disposal;
- the tree never disposes shared provider or runtime services;
- invocation-created process and transport handles are tree-owned;
- environment loss invalidates the binding and faults affected trees;
- child starts use the captured provider and never fall back to host-local process
  execution;
- a child start after binding invalidation fails with a typed result;
- service-provider teardown terminates trees before shared services disappear.

### 4.9 Add adapter provenance and trust

Compiled catalog metadata is not automatically executable policy merely because an
assembly is loaded.

Add:

```csharp
public sealed record DebugAdapterProvenance
{
    public required string PackageId { get; init; }
    public required string PackageVersion { get; init; }
    public required string AssemblyName { get; init; }
    public required DebugAdapterTrustLevel TrustLevel { get; init; }
    public string? SignatureIdentity { get; init; }
}
```

Host policy determines whether a catalog entry may:

- execute package command hints;
- search global tools;
- use workspace-local tools;
- request network or socket access;
- request credentials or authorities;
- create terminal or child processes.

Generated provenance is part of selection diagnostics and launch authorization.

### 4.10 Make protocol direction explicit

The generated inventory classifies each schema feature as:

- client-to-adapter request;
- adapter-to-client reverse request;
- adapter-to-client event;
- base/supporting type;
- extension seam.

Completeness means every canonical feature is generated and classified and every
advertised client capability has implemented tested behavior. It does not mean the
client sends every request indiscriminately.

Direction classification must not rely only on type-name suffixes. The generator
uses schema structure plus an explicit reviewed override table for ambiguous cases.

### 4.11 Establish generated-code licensing

Before generated protocol files are committed:

- record the pinned upstream repository, version, and commit;
- include required Microsoft/MIT attribution for code-derived output;
- preserve the applicable documentation attribution for derived descriptions;
- add repository-level third-party notices;
- emit a license/source pointer in generated file headers;
- review the policy once centrally rather than leaving it implicit per file.

### 4.12 Keep product integration separate from core completion

The reusable debug package is complete when its protocol, catalog, factories,
transports, trees, semantic operations, events, background resources, policy, AOT,
and tests pass.

TUI and other product consumers remain in the comprehensive program but have a
separate integration gate. They consume the same semantic service, projections,
permissions, and artifacts without becoming dependencies of the core package.

## 5. Additional contract clarifications

### 5.1 Adapter factory resolver failures

Catalog materialization validates every generated DI resolver at startup. A missing
registration reports adapter ID, package provenance, and factory type without
leaking container internals. One invalid optional external package may be disabled
by explicit host policy; built-in catalog failures abort startup.

### 5.2 Availability and launch separation

Availability probes are observational and bounded. They cannot create persistent
processes, allocate remote endpoints, request credentials, or mutate project state.

Launch-plan creation occurs only after selection and authorization. A probe result
does not authorize its later launch.

### 5.3 Launch-plan immutability

The final launch plan is immutable and records adapter/catalog provenance,
environment and policy revisions, authorization identity, transport plan, filtered
environment, working directory, timeout bounds, reverse-request policy, path mapper,
and owned extension arguments.

The orchestrator validates it again before creating a process or connection.

### 5.4 Continuation ownership

Paging and continuation tokens are opaque, query-bound, tree-bound, protocol-
session-bound, expiring, and bounded in number. State invalidation revokes tokens
whose underlying projection is stale.

### 5.5 Tree and child termination

The semantic service distinguishes:

- terminate complete root tree;
- disconnect one protocol session;
- terminate one debuggee when supported;
- detach without terminating an attached debuggee;
- forced transport disposal after graceful timeout.

Child termination never silently terminates the entire tree unless adapter semantics
or explicit policy require it.

## 6. Required implementation order

Phases determine dependency order, not reduced scope.

### Phase 0: Shared prerequisites and generated protocol baseline

- Harden `AgentEventSerializer` registration.
- Add the dedicated background-handle kind and serialization.
- Pin and license the canonical DAP schema.
- Build deterministic protocol code generation.
- Generate wire contracts, contexts, descriptors, and feature inventory.

### Phase 1: Adapter catalog and policy

- Implement declaration attributes and diagnostics.
- Generate descriptors and direct DI delegates.
- Add provenance and trust policy.
- Implement explicit cross-package catalog registration.
- Implement factory probing, selection, endpoint resolution, and cache isolation.

### Phase 2: Protocol and transports

- Implement typed dispatch, framing, correlation, cancellation, and reverse requests.
- Implement the one-pump Environment transport.
- Implement approved socket and in-memory transports.
- Implement strict malformed-input and bounded diagnostic behavior.

### Phase 3: Trees and lifecycle

- Implement per-runtime manager and runtime identity.
- Implement root/child state machines.
- Implement typed initial configuration.
- Implement atomic tree/handle publication.
- Implement ownership, cancellation, teardown, and historical-ID rejection.

### Phase 4: Complete debugging semantics

- Implement every breakpoint family and propagation.
- Implement execution and reverse execution.
- Implement inspection, mutation, memory, disassembly, and source operations.
- Implement complete event reconciliation, capabilities, invalidation, and progress.

### Phase 5: HPD integration

- Implement handles, observer, durable/live event publisher, and host request broker.
- Implement authorization, result metadata, output, artifacts, and content fallback.
- Implement debugger projections and trusted host extensions.

### Phase 6: Qualification and product integration

- Run Native AOT, trimming, fuzz, race, security, resource, and soak suites.
- Require materially different real adapters in CI.
- Publish compatibility and feature matrices.
- Integrate TUI and other product consumers through the semantic service.

## 7. Acceptance conditions

The architecture is ready for implementation when:

1. The twelve required corrections in section 4 are incorporated.
2. The updated proposal is declared canonical.
3. Earlier competing proposals are marked historical.
4. The DAP schema revision and licensing policy are pinned.
5. Event registry hardening and the new background-handle kind are approved as
   shared framework changes.
6. Protocol code-generation mechanics cannot produce duplicate compiled types.
7. Runtime identity and tree/handle publication contracts are explicit.
8. Environment transport has one tagged-output pump.
9. Adapter provenance participates in authorization.
10. Raw traces remain host-only.
11. Complete feature and test matrices are checked in before capability advertising.

## 8. Final recommendation

Adopt the updated implementation proposal as the canonical architecture after the
required corrections.

The final design is a genuine second-mover system:

```text
complete generated protocol truth
    + generated stable adapter catalog
    + constructor-injected runtime factories
    + Environment-owned execution
    + authorized endpoint transports
    + per-runtime root/child trees
    + race-safe lifecycle and breakpoint state
    + bounded HPD events, handles, output, and artifacts
    + comprehensive semantic debugging behavior
    + Native AOT qualification
```

It is more complete than the earlier Native DAP proposal, more runtime-correct than
the original generated-adapter design, and more maintainable than a hand-registered
DI-only catalog. Once corrected, it should replace the earlier active documents
rather than coexist with them as another competing specification.
