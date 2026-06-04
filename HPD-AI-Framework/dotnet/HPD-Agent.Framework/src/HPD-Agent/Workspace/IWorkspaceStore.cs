namespace HPD.Agent;

/// <summary>
/// Durable workspace substrate for spaces, content objects, space attachments, and event streams.
/// Typed runtime repositories should sit above this contract instead of owning separate persistence.
/// </summary>
public interface IWorkspaceStore
{
    Task<WorkspaceSpaceInfo> CreateSpaceAsync(
        WorkspacePrincipalRef principal,
        CreateWorkspaceSpaceRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceSpaceInfo> CreateChildSpaceAsync(
        WorkspacePrincipalRef principal,
        string parentSpaceId,
        CreateWorkspaceSpaceRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceSpaceInfo?> GetSpaceAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        CancellationToken cancellationToken = default);

    Task DeleteSpaceAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        string? ifMatchVersion = null,
        bool recursive = false,
        CancellationToken cancellationToken = default);

    Task<WorkspaceSpaceInfo?> FindSpaceAsync(
        WorkspacePrincipalRef principal,
        WorkspaceSpaceQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkspaceSpaceInfo>> ListSpacesAsync(
        WorkspacePrincipalRef principal,
        WorkspaceSpaceQuery? query = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkspaceSpaceInfo>> ListChildSpacesAsync(
        WorkspacePrincipalRef principal,
        string parentSpaceId,
        WorkspaceSpaceQuery? query = null,
        CancellationToken cancellationToken = default);

    Task<WorkspaceSpaceAccessInfo> GrantAccessAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        GrantWorkspaceSpaceAccessRequest request,
        CancellationToken cancellationToken = default);

    Task RevokeAccessAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        WorkspacePrincipalRef grantee,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkspaceSpaceAccessInfo>> ListAccessAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        CancellationToken cancellationToken = default);

    Task<WorkspaceContentAttachmentInfo> WriteContentAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        string? existingAttachmentId,
        Stream data,
        WriteWorkspaceSpaceContentRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspacePendingContentWriteCleanupResult> CleanupPendingContentWritesAsync(
        WorkspacePrincipalRef principal,
        WorkspacePendingContentWriteCleanupRequest? request = null,
        CancellationToken cancellationToken = default);

    Task<WorkspaceEventStreamRepairResult> RepairEventStreamMetadataAsync(
        WorkspacePrincipalRef principal,
        WorkspaceEventStreamRepairRequest? request = null,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenContentAsync(
        WorkspacePrincipalRef principal,
        string contentId,
        string? version = null,
        CancellationToken cancellationToken = default);

    Task<WorkspaceContentInfo?> StatContentAsync(
        WorkspacePrincipalRef principal,
        string contentId,
        string? version = null,
        CancellationToken cancellationToken = default);

    Task<WorkspaceContentAttachmentInfo> AttachContentAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        string contentId,
        AttachWorkspaceContentRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkspaceContentAttachmentInfo>> ListContentAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        WorkspaceContentAttachmentQuery? query = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkspaceVisibleContentResult>> SearchContentAsync(
        WorkspacePrincipalRef principal,
        WorkspaceVisibleContentQuery query,
        CancellationToken cancellationToken = default);

    Task DetachContentAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        string attachmentId,
        string? ifMatchVersion = null,
        CancellationToken cancellationToken = default);

    Task<WorkspaceEventAppendResult> AppendEventAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        AppendWorkspaceEventRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<WorkspaceEventRecord> ReadEventsAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        WorkspaceEventStreamQuery query,
        CancellationToken cancellationToken = default);
}

public sealed record WorkspacePrincipalRef(string Kind, string Id)
{
    public static WorkspacePrincipalRef System { get; } = new("system", "system");
}

public sealed record CreateWorkspaceSpaceRequest
{
    public required string Kind { get; init; }
    public required string ExternalId { get; init; }
    public required string Name { get; init; }
    public string? Slug { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record WorkspaceSpaceInfo
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string ExternalId { get; init; }
    public required string Name { get; init; }
    public string? Slug { get; init; }
    public string? ParentSpaceId { get; init; }
    public required string Version { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record WorkspaceSpaceQuery
{
    public string? Kind { get; init; }
    public string? ExternalId { get; init; }
    public string? ParentSpaceId { get; init; }
}

public sealed record GrantWorkspaceSpaceAccessRequest
{
    public required WorkspacePrincipalRef Grantee { get; init; }
    public string Permission { get; init; } = WorkspacePermissions.Read;
    public string? Role { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record WorkspaceSpaceAccessInfo
{
    public required string Id { get; init; }
    public required string SpaceId { get; init; }
    public required WorkspacePrincipalRef Principal { get; init; }
    public required string Permission { get; init; }
    public string? Role { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public required WorkspacePrincipalRef CreatedBy { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public static class WorkspacePermissions
{
    public const string None = "none";
    public const string Read = "read";
    public const string Write = "write";
    public const string ReadWrite = "read_write";
    public const string Manage = "manage";
    public const string Owner = "owner";
}

public sealed record WriteWorkspaceSpaceContentRequest
{
    public string? IfMatchContentVersion { get; init; }
    public string? IfMatchAttachmentVersion { get; init; }
    public string ContentType { get; init; } = "application/octet-stream";
    public required string Role { get; init; }
    public required string Name { get; init; }
    public string? PathHint { get; init; }
    public string Permission { get; init; } = WorkspacePermissions.ReadWrite;
    public IReadOnlyDictionary<string, string>? ContentMetadata { get; init; }
    public IReadOnlyDictionary<string, string>? AttachmentMetadata { get; init; }
}

public sealed record WorkspacePendingContentWriteCleanupRequest
{
    public bool IncludeAborted { get; init; } = true;
    public TimeSpan? IncludePendingOlderThan { get; init; }
}

public sealed record WorkspacePendingContentWriteCleanupResult
{
    public int MatchedWrites { get; init; }
    public int DeletedVersions { get; init; }
    public int RemovedRecords { get; init; }
    public int FailedDeletes { get; init; }
}

public sealed record WorkspaceEventStreamRepairRequest
{
    public string? SpaceId { get; init; }
    public string? Role { get; init; }
}

public sealed record WorkspaceEventStreamRepairResult
{
    public int MatchedStreams { get; init; }
    public int RepairedStreams { get; init; }
    public int MissingBackendStreams { get; init; }
}

public sealed record WorkspaceContentInfo
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string ContentType { get; init; }
    public required string Checksum { get; init; }
    public required string StorageKey { get; init; }
    public long SizeBytes { get; init; }
    public string? Name { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record AttachWorkspaceContentRequest
{
    public required string Role { get; init; }
    public required string Name { get; init; }
    public string? PathHint { get; init; }
    public string Permission { get; init; } = "read";
    public string? ContentVersion { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record WorkspaceContentAttachmentInfo
{
    public required string Id { get; init; }
    public required string SpaceId { get; init; }
    public required string ContentId { get; init; }
    public required string ContentVersion { get; init; }
    public required string Role { get; init; }
    public required string Name { get; init; }
    public string? PathHint { get; init; }
    public required string Permission { get; init; }
    public required string Version { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record WorkspaceContentAttachmentQuery
{
    public string? Role { get; init; }
    public string? Name { get; init; }
}

public enum WorkspaceContentTraversalMode
{
    SpaceOnly,
    SpaceDescendants,
    AccessibleGraph
}

public sealed record WorkspaceVisibleContentQuery
{
    public WorkspaceContentTraversalMode TraversalMode { get; init; } = WorkspaceContentTraversalMode.AccessibleGraph;
    public string? SpaceId { get; init; }
    public string? SpaceKind { get; init; }
    public string? Role { get; init; }
    public string? Name { get; init; }
    public string? ContentType { get; init; }
    public int? Limit { get; init; }
}

public sealed record WorkspaceVisibleContentResult
{
    public required string ContentId { get; init; }
    public required string ContentVersion { get; init; }
    public required string SpaceId { get; init; }
    public required string SpaceKind { get; init; }
    public required string SpaceName { get; init; }
    public required string SpaceContentId { get; init; }
    public required string Name { get; init; }
    public required string Role { get; init; }
    public required string Permission { get; init; }
    public required string ContentType { get; init; }
    public required WorkspaceSpaceInfo Space { get; init; }
    public required WorkspaceContentAttachmentInfo Attachment { get; init; }
    public required WorkspaceContentInfo Content { get; init; }
}

public sealed record AppendWorkspaceEventRequest
{
    public required string Role { get; init; }
    public required ReadOnlyMemory<byte> Payload { get; init; }
    public string ContentType { get; init; } = "application/x-ndjson";
    public string Name { get; init; } = "events.jsonl";
    public long? ExpectedSequenceNumber { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record WorkspaceEventAppendResult
{
    public required string SpaceId { get; init; }
    public required string EventStreamAttachmentId { get; init; }
    public required string EventStreamContentId { get; init; }
    public long SequenceNumber { get; init; }
    public long NextSequenceNumber { get; init; }
}

public sealed record WorkspaceEventStreamQuery
{
    public required string Role { get; init; }
    public long? AfterSequenceNumber { get; init; }
    public int? Limit { get; init; }
}

public sealed record WorkspaceEventRecord
{
    public required string SpaceId { get; init; }
    public required string Role { get; init; }
    public long SequenceNumber { get; init; }
    public required ReadOnlyMemory<byte> Payload { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed class WorkspaceConflictException : Exception
{
    public WorkspaceConflictException(
        string message,
        string? expectedVersion = null,
        string? actualVersion = null)
        : base(message)
    {
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    public string? ExpectedVersion { get; }
    public string? ActualVersion { get; }
}

public sealed class WorkspaceAccessDeniedException : Exception
{
    public WorkspaceAccessDeniedException(string message)
        : base(message)
    {
    }
}
