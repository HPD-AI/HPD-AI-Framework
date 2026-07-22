# Evaluation of the HPD Debug Tool Harness Implementation Proposal

Status: Superseded historical evaluation  
Date: July 22, 2026  
Evaluates: `DEBUG-HARNESS-IMPLEMENTATION-PROPOSAL.md`  
Audience: HPD Agent framework, harness, environment, event, and Native AOT maintainers

> This review is retained as decision history. It is superseded by
> `DEBUG-HARNESS-IMPLEMENTATION-PROPOSAL-FINAL-EVALUATION.md` and is not current
> implementation guidance.

## 1. Executive conclusion

The implementation proposal is a strong debugger-domain design. Its protocol,
session-tree, breakpoint, event, background-lifecycle, race-handling, and feature-
completeness work should form the foundation of HPD debugging.

It should not be implemented unchanged. Its adapter declaration and registry design
places runtime deployment facts into compile-time attributes, creates a debugger-
specific source generator that ultimately composes through dependency injection
anyway, and initially constructs behavioral providers without DI. Its connection
contract is based on raw streams instead of HPD Environment process capabilities.
Its configuration layering permits arbitrary JSON too close to the model-facing
boundary, and its protocol request API uses `object?` despite Native AOT goals.

The recommended architecture keeps the proposal's comprehensive second-mover scope:

- the complete canonical DAP schema is supported and classified;
- all standard requests, responses, events, reverse requests, capabilities, and
  extension seams exist in the internal protocol layer;
- session trees, child sessions, every breakpoint family, advanced execution,
  native inspection, dynamic capabilities, invalidation, progress, output,
  artifacts, and product integration remain in scope;
- delivery phases order dependencies but do not define a reduced “v1” product.

The principal revision is to generate protocol truth from the official schema while
keeping adapter discovery, environment resolution, policy, and launch planning as
runtime DI services.

Model-facing progressive disclosure and exact skill composition are deliberately
deferred. The debugger core must expose typed semantic operations from which the
final harness design can be derived after the protocol and session API is concrete.
This evaluation therefore does not accept or reject the proposal's current
`[Collapse]` and skill grouping; it marks that layer as premature.

The resulting target is:

```text
Official debugAdapterProtocol.json
        |
        v
Generated canonical C# wire model, JSON metadata, and feature inventory
        |
        v
Typed DebugProtocolClient
        |
        v
HPD Environment or host-approved endpoint transport
        |
        v
Per-agent-runtime DebugSessionManager
        |
        v
Owned DebugSessionTree
   +----+-------------------+
   |                        |
root protocol session   child protocol sessions
   |                        |
   +---- desired state -----+
        |
        v
Background handle, observer, event publisher, host request broker
        |
        v
Typed semantic debugger service
        |
        v
Future model-facing harness and progressive disclosure design
```

## 2. Evaluation principles

This evaluation uses the following priorities, in order:

1. Correctness against the official DAP schema and lifecycle.
2. Deterministic behavior under adapter races and asynchronous events.
3. Isolation across HPD agents, sessions, threads, environments, and debug trees.
4. Native AOT and trimming without runtime reflection dependencies.
5. Reuse of HPD Environment, event, permission, background-resource, and content
   infrastructure.
6. Runtime extensibility for adapters and host policies.
7. Bounded memory, output, protocol, and journal behavior.
8. A stable typed semantic service suitable for more than one presentation layer.
9. Complete second-mover scope instead of a permanently constrained first release.

No backward-compatibility constraint applies. The library has no deployed consumer
base that requires preservation of an inferior public contract.

## 3. What the official DAP repository already provides

The checked-in DAP reference is a specification repository, not a .NET debugger
client. Its `debugAdapterProtocol.json` is nevertheless a major implementation
asset. At the evaluated revision it contains 192 definitions, including 46 request
types, 47 response types, 18 event types, and 44 argument types.

It already owns:

- protocol message structure;
- JSON field names;
- required and optional fields;
- standard request, response, and event bodies;
- client and adapter capabilities;
- open and closed enum semantics;
- reverse-request contracts;
- extension points;
- property-level protocol documentation;
- canonical feature vocabulary.

It does not provide:

- generated C# DTOs in this checkout;
- `System.Text.Json` Native AOT metadata;
- content-length framing;
- request correlation;
- transport implementations;
- adapter process discovery or launch;
- session state and ownership;
- event reconciliation;
- permissions or security policy;
- HPD Agent integration;
- background handles;
- content-store behavior;
- TUI or model-facing tools.

The repository's included TypeScript generator creates the specification Markdown
and table of contents. It is not a reusable C# protocol runtime. HPD should consume
the schema as build input rather than mistake the documentation generator for an
SDK.

## 4. Findings accepted from the implementation proposal

### 4.1 Protocol truth and behavioral evidence

The proposal correctly separates sources of truth:

- the official schema defines canonical protocol behavior;
- real adapters and `oh-my-pi` reveal compatibility problems and operational races;
- HPD defines hosting, ownership, security, persistence, and agent integration.

Reference-client omissions must not become HPD protocol omissions. Conversely, an
adapter quirk must not silently redefine canonical behavior; compatibility handling
must be explicit, adapter-scoped, and tested.

### 4.2 Protocol/session separation

`DebugProtocolClient` should own only wire concerns:

- sequence numbers;
- serialization;
- framing;
- correlation;
- cancellation and DAP cancel requests;
- response, event, and reverse-request dispatch;
- transport failure;
- bounded protocol diagnostics.

It must not own desired breakpoints, selected frames, active child selection,
session ownership, or agent events. Those are semantic session responsibilities.

Likewise, `IDebugSessionManager` must never parse byte framing.

### 4.3 Session trees

The root/child session-tree design is accepted.

A root debug operation owns:

- one stable tree identity;
- ownership scope;
- desired breakpoint and exception configuration;
- root protocol session;
- zero or more adapter-created child sessions;
- active-session selection within the tree;
- tree-wide output and artifact policy;
- lifecycle and termination policy.

Each protocol session independently owns:

- protocol connection;
- negotiated capabilities;
- adapter-confirmed breakpoints;
- status and timestamps;
- thread, frame, scope, variable, module, source, and memory projections;
- output buffer;
- progress operations;
- parent and child relationships;
- exit and failure details.

The design supports `startDebugging` without later replacing the identity or
breakpoint architecture.

### 4.4 Desired versus confirmed breakpoints

The tree owns desired breakpoint collections. Each protocol session owns its
adapter-confirmed results.

This distinction is required because DAP setters replace complete collections and
different child sessions can verify, move, or reject the same desired breakpoint
differently.

Mutations are serialized per tree and breakpoint kind. They must not use one global
lock. A versioned mutation performs:

1. read desired version;
2. apply the change;
3. publish the next desired version;
4. send the full replacement to applicable sessions;
5. retain confirmed results per session;
6. reconcile later breakpoint events.

Source, function, exception, data, and instruction breakpoint families remain in
the complete scope.

### 4.5 State invalidation

The proposal correctly treats cached debugger projections as stateful and
invalidatable.

- `continued` invalidates stack, frame, scope, and variable projections.
- `stopped` invalidates the previous stopped projection before new capture.
- `invalidated` marks the advertised areas stale.
- `memory` invalidates overlapping memory projections.
- `thread`, `module`, `loadedSource`, `process`, and `breakpoint` reconcile their
  corresponding projections.
- `capabilities` merges dynamic capability changes.

No stale projection may be returned as current. A result may explicitly identify a
historical snapshot, but it cannot masquerade as live state.

### 4.6 Race-safe execution control

Outcome waiters must be installed before sending any request that can immediately
produce a stop, exit, or termination event.

The required sequence is:

1. validate tree, session, thread, state, capability, and permission;
2. invalidate prior stop state where appropriate;
3. register the bounded tree/session outcome waiter;
4. send the request;
5. reconcile the response;
6. await stop, exit, termination, or observation timeout;
7. fetch follow-up state outside the protocol reader.

A successful continue or step whose observation window expires while the target is
still running is not a request timeout. It returns success, running state, and an
explicit `TimedOutWaitingForStop` indication.

### 4.7 Reader-loop discipline

There is exactly one protocol reader. Event dispatch must not synchronously await a
follow-up DAP request whose response requires that same reader.

Handlers may perform bounded in-memory reconciliation. Follow-up stack, source,
module, or variable requests are scheduled outside the reader loop. Handler failure
is isolated, observable, and cannot silently terminate protocol processing.

### 4.8 Background-resource ownership

Each root tree is represented by a dedicated `DebugSession` background-handle kind.
The handle implements readable, stoppable, artifact, and asynchronous-disposal
contracts.

It exposes bounded status, output, stop summary, child count, dropped-output
statistics, artifacts, and failure state. Stopping the handle terminates and
disposes the complete tree.

The dedicated handle kind is preferable to generic runtime metadata because the
library has no compatibility burden and first-class typing improves filtering,
serialization, projections, and TUI integration.

### 4.9 Invocation-context boundaries

`FunctionExecutionContext` is invocation-only. It may be used to resolve runtime
capabilities, establish ownership scope, publish synchronous operation events,
register the root handle, set result metadata, and capture immutable background
dependencies.

It must never be retained by a session, observer, protocol handler, or reverse-
request callback.

### 4.10 Event boundary design

The updated proposal's distinction among HPD event facilities is accepted.

`IDebugEventPublisher` exposes durable and live-only operations. Durable publication
uses the thread journal where available and otherwise degrades to live publication.
Live publication does not imply persistence or subscriber completion.

Durable events include decision-relevant lifecycle and state:

- root and child start;
- stopped and continued transitions;
- debuggee exit;
- terminal failure or termination;
- material breakpoint verification changes;
- accepted host-boundary requests and responses;
- final artifact references and summary.

Live-only events include high-frequency or reconstructible state:

- output nudges;
- progress updates;
- thread, module, and loaded-source churn;
- invalidation and memory-change notices;
- repeated capability snapshots.

`EventFlowId` remains an interruptible-flow identifier, not the debug-tree
correlation ID. Debug tree, protocol session, adapter, process, and debugger-thread
identifiers are explicit event fields.

### 4.11 Background-safe reverse requests

`runInTerminal` and other late adapter reverse requests cannot depend on the
originating `FunctionExecutionContext`.

`IDebugHostRequestBroker` uses race-safe ordering:

1. create and scope request;
2. register it before publication;
3. commit and publish when durable scope is available;
4. await with a bounded timeout;
5. commit an accepted response before releasing the adapter continuation;
6. cancel the registered request if publication or waiting fails.

The coordinator's existing request lifecycle events are reused instead of
duplicating debugger-specific request plumbing.

### 4.12 Feature matrix

A checked-in feature matrix is mandatory. It contains every canonical request,
response, event, reverse request, and capability, with HPD-specific ownership and
exposure decisions.

At minimum it records:

```text
Feature
Protocol kind and direction
Schema type
Required or related capability
Protocol/session/host owner
Semantic service exposure
Permission class
State precondition and mutation
Agent event effect
Delivery dependency
Implementation and test status
```

No client capability may be advertised until its behavior is implemented and
covered by the matrix.

## 5. Required architectural changes

### 5.1 Remove the debugger-specific adapter registry generator

The adapter attribute and generated-registry design should be removed.

Compile-time attributes can describe a command but cannot establish:

- whether it exists in the selected HPD environment;
- whether its version is supported;
- whether host policy permits execution;
- whether a workspace-local installation is trusted;
- whether credentials or approved endpoints are available;
- whether platform-specific invocation differs;
- whether the environment has been replaced since compilation.

The important facts are runtime facts. Cross-assembly generated registries also
require DI or builder composition, eliminating the main simplicity claimed for the
generator.

Behavioral providers must not require parameterless construction. Real providers
need logging, options, environment capabilities, endpoint resolution, policy, and
possibly secrets. Delaying DI support would knowingly ship the wrong extension
contract.

Replace the cold path with explicit runtime registration:

```csharp
services.AddHPDDebugging();
services.AddDebugAdapter<DebugPyAdapterFactory>();
services.AddDebugAdapter<NetCoreDbgAdapterFactory>();
```

Factories are thread-safe DI services. The immutable registry may be a singleton;
the live session manager is scoped to one agent runtime.

### 5.2 Generate protocol models, not adapter registrations

Introduce a deterministic build tool dedicated to
`debugAdapterProtocol.json`. It generates:

- canonical C# wire records;
- request descriptors pairing command, arguments, and response;
- source-generated JSON metadata;
- open-string enum representations;
- extension-data support;
- XML documentation;
- schema revision metadata;
- the baseline feature inventory.

Generated files contain the upstream protocol version and commit. CI regenerates
and fails on a diff. Generator output is mechanically formatted and never edited by
hand.

This generator is justified because its input is an authoritative external schema.
It is independent from adapter selection and deployment.

### 5.3 Replace the untyped request API

The proposed API combines a string command, `object?` arguments, and only a generic
response. That permits invalid combinations and invites reflection serialization.

Use generated descriptors:

```csharp
ValueTask<TResponse> SendAsync<TArguments, TResponse>(
    DapRequestDescriptor<TArguments, TResponse> descriptor,
    TArguments arguments,
    CancellationToken cancellationToken,
    TimeSpan? timeout = null);
```

The descriptor owns command name and both generated `JsonTypeInfo` values. A command
cannot accidentally use another command's arguments or response type.

Adapter-specific launch/attach arguments are the bounded exception. A factory
creates an owned `JsonElement` using factory-owned source-generated metadata before
returning its launch plan. The core never serializes an arbitrary runtime object.

### 5.4 Replace raw stream/process assumptions with HPD transport

The default stdio transport resolves `IProcessProvider` from runtime capabilities
and calls `StartAsync(ProcessInvocationSpec)`. It adapts
`IProcessInvocationHandle` to the debugger transport contract.

The contract separates:

- protocol reads;
- protocol writes;
- bounded diagnostic/stderr reads;
- stop and disposal;
- transport-liveness observation.

Protocol stdout and diagnostics can never share one read channel. A provider that
cannot separate them is unsupported for stdio DAP.

TCP, Unix-socket, callback, and other transports remain supported through a
host-approved `IDebugAdapterTransportFactory`. They do not bypass HPD policy.

### 5.5 Use opaque registered remote endpoints

Model or untrusted project input must not provide raw remote host, port, URI,
credential, tunnel, or socket path values.

Public semantic start/attach input uses an opaque endpoint ID. A singleton
`IDebugEndpointResolver` owns addresses, credentials, rotation, revocation, and
policy revision. Adapter factories receive an authorized resolved descriptor, not
untrusted address components.

Host diagnostic APIs may expose direct endpoints under explicit trusted policy;
that capability is not part of the ordinary semantic service contract.

### 5.6 Replace arbitrary per-call launch JSON

Do not expose raw launch/attach JSON or an unrestricted adapter-options dictionary
through the semantic API.

Configuration sources are:

1. adapter-package defaults;
2. trusted host adapter configuration;
3. per-agent runtime configuration;
4. typed semantic operation fields;
5. HPD-controlled invariants.

Project configuration is untrusted input and passes adapter-specific schema and
policy validation. Factories may understand open adapter extensions internally,
but those extensions do not become an untyped general model contract.

### 5.7 Define environment override policy

Environment overrides are deny-by-default and host-allowlisted. Defaults are:

- maximum 32 entries;
- 128 UTF-8 bytes per key;
- 4 KiB per value;
- no null/delete semantics;
- target-platform key comparison;
- reserved adapter, loader, credential, and protocol variables cannot be replaced;
- rejected values are never echoed in errors or events.

Factories receive only the filtered result.

### 5.8 Make adapter availability context-bound

Availability and selection are runtime operations. Their cache key includes:

- adapter ID and package version;
- environment identity and revision;
- target platform;
- canonical workspace root;
- project-marker fingerprint;
- launch policy revision;
- endpoint-catalog revision.

Positive results default to 30 seconds; negative results default to five seconds.
Concurrent identical probes are coalesced. Cached entries never include secrets,
addresses, environment values, or raw diagnostic output.

### 5.9 Use a per-agent-runtime session manager

Live debug state is not process-global. `IDebugSessionManager` is installed in the
agent runtime capability registry and disposed with that runtime.

Tree ownership includes:

```text
agent runtime registration ID
HPD session ID
HPD thread ID
debug tree ID
```

Protocol-session IDs identify members inside a tree. An operation must identify and
own a tree; it may select a specific member or use the tree's deterministic active
member. No tree-less process-global active session exists.

### 5.10 Complete owner lifecycle behavior

The lifecycle matrix is normative:

| Event | Required result |
|---|---|
| Agent turn ends | Tree remains live |
| Presentation-layer capability visibility expires | Tree remains live |
| Root debug handle stops | Entire tree terminates |
| HPD thread is deleted | Trees owned by that thread terminate |
| Agent runtime stops/disposes | Every owned tree terminates |
| Bound environment exits/disposes | Its trees fault and release transports |
| Debugging service provider disposes | All remaining resources terminate |
| Process restarts and history reloads | Historical IDs remain unavailable, never relaunched |

Cleanup uses internal bounded tokens rather than an already-cancelled caller token.
Failure in one tree cannot prevent disposal of others.

### 5.11 Separate operation cancellation from session lifetime

- Cancellation before tree publication aborts start and cleans up transport.
- Cancellation of one pending DAP request removes that waiter and sends DAP cancel
  when supported.
- A late response is ignored or reconciled according to request semantics.
- Cancellation after tree publication never terminates the tree implicitly.
- Execution-control cancellation does not invent running or stopped state.
- Explicit stop, adapter exit, environment loss, owner teardown, or service disposal
  ends the tree.
- Protocol readers and observers use tree lifetime tokens, not tool-call tokens.

### 5.12 Make configuration completion race-safe

The launch/attach state machine must support adapters that delay their response,
emit `initialized` early or late, require breakpoint configuration, or immediately
emit stop/termination events.

Configuration owns an exactly-once completion gate. Desired breakpoints and
exception filters are applied after `initialized` and before `configurationDone`.
The semantic API may provide initial desired configuration or expose a configuring
tree before completion. In either case, there is one well-defined operation that
atomically completes configuration and reconciles the resulting event.

The implementation must not deadlock by awaiting a launch response that the adapter
withholds until `configurationDone` while simultaneously preventing configuration.

### 5.13 Tighten malformed-protocol policy

A valid frame containing malformed JSON faults the protocol session by default.
Silently skipping it can strand a pending request and corrupt correlation state.

Missing or invalid framing also faults after a bounded diagnostic. Optional bounded
resynchronization is a diagnostic compatibility mode tied to a documented adapter
quirk, not the default production behavior.

Unknown well-formed events are safe to ignore after bounded telemetry. Unknown
reverse requests receive a not-supported response.

### 5.14 Remove ordinary raw custom requests from the semantic service

The complete internal protocol engine may dispatch custom requests for trusted host
extensions and tests. The ordinary semantic debugger service does not expose an
arbitrary request escape hatch.

Permission cannot make unknown effects understandable. Typed adapter extensions may
be added through registered host APIs without weakening the generic contract.

### 5.15 Use session authorization for normal control

Launch and attach require approval. That approval authorizes ordinary breakpoint
configuration and execution control for the bounded owned tree.

Separate approval remains required for:

- evaluation by default;
- variable/expression mutation;
- memory writes;
- new remote or process trust boundaries;
- `runInTerminal` not covered by the approved launch;
- adapter-defined privileged host operations.

Prompting for every continue, pause, breakpoint, and step would make debugging
unusable without adding a meaningful security boundary.

## 6. Output, artifacts, and content storage

### 6.1 Categorized output

Retain the proposal's record-oriented buffer and add:

- debug tree and protocol-session IDs;
- normalized and original categories;
- output group identity where available;
- UTF-8 byte length;
- truncation marker;
- dropped-before sequence count.

The buffer tracks maximum records, total bytes, maximum record bytes, oldest and
newest sequence, and dropped record/byte counters.

### 6.2 Event and storage limits

Large retained output does not imply large agent events. Live output notifications
default to 8–16 KiB after coalescing. The session buffer and content artifact may be
larger under separate bounds.

Raw protocol tracing is disabled by default, separately bounded, host-controlled,
and stored as an artifact. It is never written to ordinary logs or the durable
thread journal.

### 6.3 Optional content store

`FunctionExecutionContext.ContentStore` may be absent or fail.

When an artifact cannot be stored:

- return a bounded preview or tail;
- return `OutputTooLarge` or `ContentStoreUnavailable` as appropriate;
- never raise the inline ceiling;
- record a bounded diagnostic;
- do not fault the live debug tree.

Stored artifacts carry agent, session, thread, tree, and protocol-session scope and
inherit the configured store's retention policy.

## 7. Serialization and Native AOT

Use two generated contexts:

- `DapJsonContext` for canonical wire types;
- `DebugHarnessJsonContext` for semantic requests, results, snapshots, artifacts,
  and HPD agent events.

`AddHPDDebugging()` explicitly invokes idempotent debugger-event registration. Each
event discriminator is registered with its source-generated `JsonTypeInfo`.
Registration fails deterministically on a discriminator/type collision. Module
initializers and incidental static constructors are not used.

Native AOT acceptance requires:

- no reflection adapter scan;
- no runtime schema generation;
- no arbitrary-object protocol serialization;
- direct DI factories;
- generated DAP descriptors and JSON metadata;
- trimmed adapter factories remaining constructible;
- a published native executable completing a simulated root/child lifecycle.

## 8. Error model revisions

The normalized error model should include:

```text
InvalidRequest
AdapterNotFound
AdapterUnavailable
AdapterAmbiguous
InvalidConfiguration
PermissionDenied
RemoteEndpointDenied
SessionNotFound
SessionUnavailable
SessionOwnershipMismatch
InvalidSessionState
CapabilityUnavailable
TransportFailure
ProtocolViolation
RequestTimedOut
RequestCancelled
DebuggeeExited
AdapterExited
OutputTooLarge
ContentStoreUnavailable
InternalFailure
```

Expected operational failures return typed bounded results. Programmer errors and
violated internal invariants throw. Adapter stderr and raw payloads are retained
only in explicitly authorized diagnostics.

## 9. Default limits

All defaults are validated and host-overridable within hard ceilings:

| Limit | Default |
|---|---:|
| Initialize/start request | 30 seconds |
| Ordinary DAP request | 10 seconds |
| Adapter connection readiness | 10 seconds |
| Protocol write | 30 seconds |
| Continue/step observation | 30 seconds |
| Disconnect cleanup | 5 seconds per tree, 15 seconds total |
| Header/message size | 16 KiB / 4 MiB |
| Pending requests | 128 per protocol session |
| Recent semantic events | 256 per protocol session |
| Threads/frames/scopes per page | 100 / 100 / 64 |
| Variables/modules/instructions per page | 200 / 200 / 256 |
| Name/type/value text | 1 KiB / 1 KiB / 16 KiB |
| Evaluate output | 64 KiB inline |
| Memory read/write | 64 KiB / 4 KiB per operation |
| Adapter diagnostics retained | 64 KiB per protocol session |
| Debug output retained | 256 KiB per protocol session |
| Live output event | 16 KiB |
| Continuation tokens | 128 per tree, 5-minute expiry |
| Reverse requests | 16 concurrent, 60 per minute |

Limits count UTF-8 bytes where wire or storage size matters. A host may lower any
limit. Raising a hard ceiling requires trusted host policy and cannot come from
untrusted project or model input.

## 10. Comprehensive delivery strategy

Delivery phases establish dependency order. They do not define a reduced product
scope or justify omitting canonical features.

### Phase 0: Canonical protocol baseline

- Pin the official schema version and commit.
- Generate the complete wire model and `DapJsonContext`.
- Generate the baseline feature inventory.
- Check in the enriched HPD feature matrix.
- Classify every request, response, event, reverse request, and capability.
- Finalize public protocol/session/transport naming.

Exit condition: no canonical feature is unclassified and generated output is
deterministic.

### Phase 1: Protocol engine

- Implement framing and typed descriptor dispatch.
- Implement correlation, timeouts, cancellation, DAP cancel, and late responses.
- Implement events and reverse requests.
- Implement strict malformed-input and disconnect behavior.
- Add deterministic in-memory transport, fuzz, and property tests.

Exit condition: every protocol race and failure path settles pending operations
exactly once.

### Phase 2: Runtime adapter and transport architecture

- Implement DI registry, factories, selection, probing, and cache isolation.
- Implement launch policy and endpoint resolver.
- Implement HPD Environment stdio transport.
- Implement approved socket/callback transport seams.
- Validate launch plans and environment filtering.

Exit condition: adapter resolution and transport startup require no reflection,
raw model commands, or direct process coupling.

### Phase 3: Session trees and ownership

- Implement tree/session state machines.
- Implement root and child identities.
- Implement per-runtime manager and owner isolation.
- Implement handles, observers, teardown, and historical-ID rejection.
- Implement launch, attach, initialization, configuration, and failure cleanup.

Exit condition: concurrent owned trees operate independently and every teardown
path releases all resources.

### Phase 4: Complete breakpoint and execution behavior

- Implement desired and confirmed breakpoint stores for every canonical kind.
- Implement serialized mutation and child propagation.
- Implement continue, pause, all step/reverse/goto/restart operations.
- Implement race-safe outcome waiters and observation semantics.
- Implement exception configuration and thread termination.

Exit condition: all execution and breakpoint matrix rows have capability, state,
permission, race, and integration tests.

### Phase 5: Complete inspection and state reconciliation

- Implement threads, stack, scopes, variables, evaluate, source, modules, loaded
  sources, exception information, breakpoint locations, step-in targets, goto
  targets, completions, and location references.
- Implement variable/expression mutation.
- Implement memory and disassembly.
- Implement all canonical event reconciliation and dynamic capabilities.
- Implement projection invalidation.

Exit condition: no advertised client capability lacks behavior and stale state is
never returned as current.

### Phase 6: Reverse requests, events, output, and artifacts

- Implement background-safe `runInTerminal`.
- Implement `startDebugging` child creation.
- Implement `IDebugEventPublisher` and `IDebugHostRequestBroker`.
- Implement durable/live publication policy.
- Implement categorized output, progress, coalescing, content artifacts, and raw
  host-only tracing.
- Implement debugger projections.

Exit condition: long-running sessions operate after the initiating invocation has
ended without retaining invocation context or flooding journals.

### Phase 7: Semantic service and product integration

- Finalize typed semantic operations over the completed manager.
- Finalize the model-facing harness and progressive-disclosure design based on the
  real operation graph and schemas.
- Add TUI status, tree, session, breakpoint, stack, variable, output, progress,
  permission, and artifact views.
- Publish adapter and platform compatibility matrices.
- Add developer documentation and cookbook material.

Exit condition: every canonical feature is implemented, explicitly host-only, or
returns a typed unsupported reason; presentation layers use the same semantic core.

### Phase 8: Native AOT and production qualification

- Publish and execute full Native AOT smoke applications.
- Run at least two materially different adapters in required CI jobs.
- Run platform/environment-specific adapter suites.
- Run malformed-protocol, crash, cancellation, disposal, soak, and resource-limit
  suites.
- Audit warnings, generated schemas, event serialization, secrets, and logs.

Exit condition: the comprehensive feature matrix, AOT, isolation, security,
resource, and adapter compatibility gates pass.

## 11. Required test additions

### 11.1 Schema generation

- Every schema definition is classified.
- Generated C# compiles on supported target frameworks.
- Required and optional fields map correctly.
- Open enums preserve unknown strings.
- Extension data round-trips.
- Generated XML documentation is present.
- `DapJsonContext` covers every generated type.
- Regeneration from the pinned schema produces no diff.
- No semantic/model tool is generated from the wire schema.

### 11.2 Protocol

- Arbitrary header/body chunking.
- Multiple frames per read.
- UTF-8 content length.
- Out-of-order and duplicate responses.
- Cancellation/response/disconnect races.
- DAP cancel support and unsupported behavior.
- Reverse request success, rejection, timeout, and publication failure.
- Event handler isolation.
- Reader-loop reentrancy prohibition.
- Malformed frame and JSON policy.
- Oversized input and pending-request limits.
- Sequence overflow policy.

### 11.3 Adapter registry and policy

- Explicit, automatic, unavailable, no-match, and ambiguous selection.
- Duplicate ID rejection.
- Environment/workspace/policy cache isolation.
- Endpoint revocation invalidation.
- Concurrent probe coalescing.
- Launch-plan command and endpoint validation.
- Environment override allowlist and reserved-key behavior.
- Missing tools return bounded install guidance without raw probe output.

### 11.4 Session trees

- Root and multiple child sessions.
- Active-member selection within an explicitly owned tree.
- Cross-owner rejection.
- Initialization event before and after launch response.
- Configuration completion exactly once.
- Stop event in the same batch as response.
- Adapter crash isolation.
- Whole-tree and single-child termination policy.
- Historical IDs unavailable after reconstruction.
- Thread, agent, environment, handle, and service teardown.

### 11.5 Breakpoints and state

- Concurrent mutations never lose updates.
- Every breakpoint kind uses full replacement semantics.
- Desired state survives child creation.
- Confirmed state differs safely per child.
- Breakpoint events verify, move, change, and remove.
- Continue and invalidated events clear the correct projections.
- Memory invalidation respects overlap.
- Dynamic capabilities alter operation availability.

### 11.6 Events and artifacts

- Durable events commit before live publication.
- Live-only events never enter the journal.
- Terminal events are not lost to expired flows.
- Reverse-request responses commit before adapter continuation release.
- Output and progress coalesce and respect backpressure.
- Missing/failing content stores return bounded fallback.
- Secrets, expression text, values, memory, and raw payloads are absent from default
  logs and metrics.

### 11.7 Native AOT

- Generated protocol serialization.
- Debugger-event registration and round-trip.
- DI construction of built-in and external adapter factories.
- Root and child session lifecycle.
- Reverse requests.
- Content artifacts and bounded fallback.
- Trimmed execution without debugger-specific reflection warnings.

## 12. Model-facing design deliberately deferred

The implementation proposal currently selects `[Collapse]`, four always-visible
functions, and six skills. This evaluation does not adopt that design, but it also
does not replace it with a different final skill layout.

The model-facing contract should be finalized after:

- the canonical feature matrix exists;
- semantic operation types are concrete;
- permission classes are approved;
- session-tree and child-selection behavior is proven;
- real adapter integration reveals which operations naturally compose;
- schema-size and model-disclosure measurements are available.

The debugger core must therefore avoid embedding assumptions about skill names,
activation hierarchy, or always-visible functions. It exposes typed semantic
operations and metadata sufficient for a later harness to organize them without
changing protocol or session internals.

This is a sequencing decision, not a rejection of progressive disclosure.

## 13. Final recommendation

Revise the implementation proposal rather than discard it.

Retain its:

- comprehensive canonical DAP ambition;
- protocol/session separation;
- root and child session trees;
- desired and confirmed breakpoint architecture;
- state invalidation;
- event waiter ordering;
- background handles and observers;
- durable/live event distinction;
- background-safe host request broker;
- categorized output;
- feature matrix;
- complete advanced and native debugging scope.

Replace its:

- adapter attributes and generated registry with DI factories;
- optional protocol generation with mandatory canonical schema generation;
- `object?` request dispatch with generated typed descriptors;
- raw stream/process default with HPD Environment transport;
- direct endpoint fields with registered endpoint IDs and a resolver;
- arbitrary per-call JSON with typed semantic input and trusted configuration;
- process/global ambiguity with per-runtime owned trees;
- incomplete cancellation, storage, limits, and teardown policies with the
  normative contracts in this evaluation;
- ordinary raw custom-request exposure with trusted host-only extension APIs.

Defer the final model-facing harness and skill layout until the semantic core is
implemented far enough to measure and validate the disclosure design.

The desired outcome is not a reduced first version. It is a comprehensive,
capability-correct, Native AOT-compatible second-mover debugger architecture whose
internal protocol surface is complete, whose runtime behavior is safe under real
adapter races, and whose eventual model-facing API is derived from a proven core
rather than chosen prematurely.
