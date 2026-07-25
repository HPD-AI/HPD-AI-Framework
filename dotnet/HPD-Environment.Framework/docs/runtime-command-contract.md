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

These rules are part of the contract shape. A future compatibility requirement
should be handled through normal semantic versioning rather than parallel legacy
methods.
