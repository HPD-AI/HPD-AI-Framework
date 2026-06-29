namespace HPD.Base;

/// <summary>
/// Names the closed set of kernel operation families.
/// </summary>
public enum BaseOperationKind
{
    List,
    Query,
    Get,
    Create,
    Patch,
    Replace,
    Upsert,
    Delete,
    Batch,
    Transaction,
    SchemaRead,
    SchemaWrite,
    FileRead,
    FileWrite,
    RealtimeSubscribe,
    AdminInspect
}
