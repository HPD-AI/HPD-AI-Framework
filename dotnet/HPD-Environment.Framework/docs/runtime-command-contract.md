# Runtime command contract

`IEnvironmentRuntime` is a pre-release public contract. The engine-control-plane,
authority-binding, execution-unit cleanup, and host-deletion commands are an
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

These rules are part of the contract shape. A future compatibility requirement
should be handled through normal semantic versioning rather than parallel legacy
methods.
