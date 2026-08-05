# Runtime command contract

`IEnvironmentRuntime` is a pre-release public contract. The engine-control-plane
creation/deletion, authority-binding, execution-unit cleanup, and host-deletion commands are an
intentional breaking expansion made before external adoption. No compatibility
adapter is provided.

The runtime command surface owns reconciliation identity and cleanup:

- desired state is reconciled to stable runtime resource identities;
- materially changed desired state advances `ResourceMetadata.Generation`;
- providers report accepted, immutable-conflict, or rejected reconciliation
  through `ResourceStatus.ReconciliationOutcome`;
- every created resource records its provider owner, and later commands route
  only to that provider;
- engine socket authority is planned through the owning engine provider and
  remains an explicit sensitive `AuthorityBinding` tied to the exact source
  engine generation; realization revalidates that generation and engine
  reconfiguration is rejected while derived authority remains active;
- accepted engine-authority plans have opaque runtime-owned IDs, expire, bind
  the canonical approved specification to the source engine generation, and
  can be consumed exactly once;
- host deletion stops active work, finalizes content, revokes authority,
  deletes units and engines, and only then deletes the host;
- host deletion has overall and per-operation deadlines and returns
  `RuntimeHostDeletionResult` with truthful diagnostics;
- host stop retains the logical host but rejects with
  `hpd.environment.runtime-host.dependents-active` before invoking a provider
  while any execution unit, process, authority, engine, projection, or network
  dependency remains owned. Callers must explicitly remove dependents first;
  host deletion remains the command that performs ordered destructive cleanup;
- explicit engine deletion routes to the recorded provider owner, rejects while
  derived authority remains active, and clears stable runtime engine identity;
- `FailOperation`, `MarkDegradedAndRetain`, and `BestEffortRelease` have distinct
  behavior; degraded retention updates the cached host status;
- protected hosts reject deletion before cleanup begins;
- changing the provider owner requires explicit host deletion and recreation.

Execution-unit reconciliation and observation follow these additional rules:

- `ExecutionUnitSpec.ReconciliationKey` is an optional caller-owned logical
  identity. A missing key requests a new ephemeral unit; the same non-empty key
  on the same host incarnation reconciles one retained unit.
- identical desired state retains resource ID and generation. A material change
  advances the accepted generation only after provider acceptance.
- material changes are rejected with a provider-neutral immutable conflict while
  the unit has active processes, authority bindings, realized projections,
  network memberships, published endpoints, or an uncertain/degraded observation.
  Callers must perform ordered deletion and recreation.
- material host changes are rejected while the runtime owns dependent units,
  engines, or authorities. Explicit host deletion performs ordered cleanup and
  prevents prior-generation dependents from becoming orphaned.
- execution-unit list/get operations are observation operations, not cache reads.
  Each unit is refreshed through its recorded provider and its observed
  generation, assigned-host identity, target handle, and namespace opaque handle
  are validated before the cache changes.
- each provider observation has a five-second bound. Timeout, provider failure,
  or identity mismatch retains ownership and returns the last snapshot marked
  degraded with a structured diagnostic. The wall-clock bound is enforced even
  when a provider ignores cancellation; an abandoned provider task cannot update
  runtime state later. Caller cancellation remains distinguishable and is
  propagated.

Retained process lifecycle follows these rules:

- `StartProcessAsync` creates a runtime-owned `ProcessInvocation` resource and
  returns its snapshot. Live `IProcessInvocationHandle` objects remain internal
  runtime/provider capabilities and are never the public runtime identity.
- a provider must explicitly implement `IRetainedProcessProvider`; the runtime
  fails closed instead of treating an ephemeral process handle as durable.
- list, get, stop, wait, output, and delete commands route only through the
  provider that created the process.
- processes remain observable after terminal completion until explicit deletion.
  Deleting a running process is rejected.
- a primary retained process is projected into its owning execution unit.
  Start adds active ownership and transitions the unit to running; terminal
  completion removes active ownership and preserves the primary result.
- one runtime-owned pump consumes each provider output stream. Reads replay by
  process sequence from independently byte-bounded stdout/stderr retention, and
  `follow` waits for that same pump rather than opening another provider reader.
  Providers whose transports do not support unsolicited events must repeatedly
  poll their cursor-based output operation until terminal status and output drain.
  Retained oversized chunks are tail-bounded and marked truncated.
- process observation is wall-clock bounded even when a provider ignores its
  cancellation token. Observation failure degrades the retained snapshot without
  releasing ownership. Provider observation must query its authoritative process
  service rather than merely return a cached runtime ledger entry.
- start observation failure performs bounded compensating stop/wait/release before
  returning. Explicit deletion completes bounded local pump/handle cleanup before
  irreversibly releasing provider state, so a failed attempt remains retryable.
- execution-unit and host deletion stop and release their retained processes
  before releasing the owning unit or host.

These rules are part of the contract shape. A future compatibility requirement
should be handled through normal semantic versioning rather than parallel legacy
methods.
