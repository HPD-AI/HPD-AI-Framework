namespace HPD.Base.AspNetCore;

/// <summary>Identifies the audience of one BASE HTTP endpoint.</summary>
public enum HPDBaseEndpointAudience
{
    /// <summary>Unauthenticated host-selected operational discovery.</summary>
    Public,
    /// <summary>Ordinary host-authorized application-data access.</summary>
    Application,
    /// <summary>Validated administrative control-plane access.</summary>
    ControlPlane
}

/// <summary>Identifies the closed semantic operation of one BASE HTTP endpoint.</summary>
public enum HPDBaseEndpointOperation
{
    /// <summary>Reads metadata.</summary>
    MetadataRead,
    /// <summary>Reads health.</summary>
    HealthRead,
    /// <summary>Reads diagnostics.</summary>
    DiagnosticsRead,
    /// <summary>Reads records.</summary>
    RecordRead,
    /// <summary>Writes records.</summary>
    RecordWrite,
    /// <summary>Deletes records.</summary>
    RecordDelete,
    /// <summary>Executes a batch write.</summary>
    RecordBatchWrite,
    /// <summary>Executes a registered read.</summary>
    RegisteredRead,
    /// <summary>Reads files.</summary>
    FileRead,
    /// <summary>Writes files.</summary>
    FileWrite,
    /// <summary>Deletes files.</summary>
    FileDelete,
    /// <summary>Subscribes to realtime delivery.</summary>
    RealtimeSubscribe,
    /// <summary>Explains a policy decision.</summary>
    PolicyExplain,
    /// <summary>Executes policy-safe vector ranking.</summary>
    VectorQuery,
    /// <summary>Reads vector-index metadata.</summary>
    VectorMetadataRead,
    /// <summary>Rebuilds one vector index.</summary>
    VectorRebuild
    ,
    /// <summary>Reads the immutable cross-language generation snapshot.</summary>
    ClientGenerationRead,
    /// <summary>Administratively purges durable collection history.</summary>
    AdministrativePurge,
    /// <summary>Creates one confirmed backup artifact.</summary>
    BackupCreate,
    /// <summary>Validates one authenticated backup artifact.</summary>
    BackupValidate,
    /// <summary>Restores one authenticated backup artifact.</summary>
    BackupRestore
}

/// <summary>Describes one exact BASE HTTP endpoint.</summary>
public sealed record HPDBaseEndpointDescriptor
{
    /// <summary>Gets the stable endpoint identifier.</summary>
    public required string EndpointId { get; init; }
    /// <summary>Gets the endpoint audience.</summary>
    public required HPDBaseEndpointAudience Audience { get; init; }
    /// <summary>Gets the semantic operation.</summary>
    public required HPDBaseEndpointOperation Operation { get; init; }
    /// <summary>Gets the product-owned capability, or <see langword="null"/> for Public endpoints.</summary>
    public string? Capability { get; init; }
}

/// <summary>Names stable product capabilities for BASE HTTP endpoints.</summary>
public static class HPDBaseCapabilities
{
    /// <summary>Reads records.</summary>
    public const string RecordsRead = "base.records.read";
    /// <summary>Writes records.</summary>
    public const string RecordsWrite = "base.records.write";
    /// <summary>Deletes records.</summary>
    public const string RecordsDelete = "base.records.delete";
    /// <summary>Executes record batches.</summary>
    public const string RecordsBatchWrite = "base.records.batch.write";
    /// <summary>Reads files.</summary>
    public const string FilesRead = "base.files.read";
    /// <summary>Writes files.</summary>
    public const string FilesWrite = "base.files.write";
    /// <summary>Deletes files.</summary>
    public const string FilesDelete = "base.files.delete";
    /// <summary>Subscribes to realtime delivery.</summary>
    public const string RealtimeSubscribe = "base.realtime.subscribe";
    /// <summary>Reads administrative metadata.</summary>
    public const string AdministrationMetadataRead = "base.administration.metadata.read";
    /// <summary>Reads administrative health.</summary>
    public const string AdministrationHealthRead = "base.administration.health.read";
    /// <summary>Reads administrative diagnostics.</summary>
    public const string AdministrationDiagnosticsRead = "base.administration.diagnostics.read";
    /// <summary>Executes administrative registered reads.</summary>
    public const string AdministrationRecordsRead = "base.administration.records.read";
    /// <summary>Explains policy decisions.</summary>
    public const string PolicyExplain = "base.policy.explain";
    /// <summary>Executes vector ranking.</summary>
    public const string VectorQuery = "base.vector.query";
    /// <summary>Reads safe vector-index metadata.</summary>
    public const string VectorMetadataRead = "base.vector.metadata.read";
    /// <summary>Reads safe vector-index diagnostics.</summary>
    public const string VectorDiagnosticsRead = "base.vector.diagnostics.read";
    /// <summary>Rebuilds one vector index.</summary>
    public const string VectorRebuild = "base.vector.rebuild";
    /// <summary>Generates an Application client contract.</summary>
    public const string ClientGenerate = "base.client.generate";
    /// <summary>Generates a ControlPlane client contract.</summary>
    public const string AdministrationClientGenerate = "base.admin.client.generate";
    /// <summary>Administratively purges records and durable history.</summary>
    public const string AdministrationRecordsPurge = "base.admin.records.purge";
    /// <summary>Creates authenticated backup artifacts.</summary>
    public const string AdministrationBackupCreate = "base.admin.backup.create";
    /// <summary>Validates authenticated backup artifacts.</summary>
    public const string AdministrationBackupValidate = "base.admin.backup.validate";
    /// <summary>Restores authenticated backup artifacts.</summary>
    public const string AdministrationBackupRestore = "base.admin.backup.restore";
}
