namespace HPD.Base;

/// <summary>
/// Names the closed set of kernel operation families.
/// </summary>
public enum BaseOperationKind
{
    /// <summary>Identifies list.</summary>
List,
    /// <summary>Identifies query.</summary>
Query,
    /// <summary>Identifies get.</summary>
Get,
    /// <summary>Identifies create.</summary>
Create,
    /// <summary>Identifies patch.</summary>
Patch,
    /// <summary>Identifies replace.</summary>
Replace,
    /// <summary>Identifies upsert.</summary>
Upsert,
    /// <summary>Identifies delete.</summary>
Delete,
    /// <summary>Identifies a host-authorized administrative purge.</summary>
    Purge,
    /// <summary>Identifies batch.</summary>
    Batch,
    /// <summary>Identifies one bounded transaction-bound selection mutation.</summary>
    SelectionMutation,
    /// <summary>Identifies schema read.</summary>
SchemaRead,
    /// <summary>Identifies schema write.</summary>
SchemaWrite,
    /// <summary>Identifies file read.</summary>
FileRead,
    /// <summary>Identifies file write.</summary>
FileWrite,
    /// <summary>Identifies realtime subscribe.</summary>
RealtimeSubscribe,
    /// <summary>Identifies admin inspect.</summary>
    AdminInspect,
    /// <summary>Identifies host backup creation or validation.</summary>
    AdminBackup,
    /// <summary>Identifies destructive host restore.</summary>
    AdminRestore,
    /// <summary>Identifies policy-safe vector ranking.</summary>
    VectorQuery,
    /// <summary>Identifies vector-index rebuild administration.</summary>
    VectorRebuild,
    /// <summary>Identifies authorized acquisition of an exported logical-subject reference.</summary>
    SubjectAcquire,
    /// <summary>Identifies mutation-bound validation of an exported logical-subject reference.</summary>
    SubjectValidate,
    /// <summary>Identifies destructive exported-subject authority-epoch rotation.</summary>
    SubjectEpochRotate,
    /// <summary>Identifies an authorized durable subject-lifecycle feed read.</summary>
    SubjectLifecycleRead,
    /// <summary>Identifies an identified durable subject-lifecycle checkpoint advance.</summary>
    SubjectLifecycleCheckpoint,
    /// <summary>Identifies an authorized current subject-lifecycle reconciliation read.</summary>
    SubjectLifecycleReconcile,
    /// <summary>Identifies the generated constrained subject tombstone mutation.</summary>
    SubjectLifecycleTombstone,
    /// <summary>Identifies generated uncoordinated final subject retirement.</summary>
    SubjectLifecycleFinalizeRetirement,
    /// <summary>Performs one identified subject-lifecycle maintenance operation.</summary>
    SubjectLifecycleMaintenance,
    /// <summary>Identifies one graph-installed registered module mutation.</summary>
    ModuleMutation
}
