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
AdminInspect
}
