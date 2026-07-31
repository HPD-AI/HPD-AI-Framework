
namespace HPD.Base;

/// <summary>
/// Event emitted after a committed BASE record mutation.
/// </summary>
public sealed record BaseRecordMutationEvent : BaseEvent
{
    /// <summary>Resource affected by the mutation.</summary>
    public required EventResource Resource { get; init; }

    /// <summary>BASE operation that produced the mutation.</summary>
    public required BaseOperationKind Operation { get; init; }

    /// <summary>Top-level fields changed by the mutation when known.</summary>
    public string[]? ChangedFields { get; init; }

    /// <summary>Record snapshot before the mutation when available and safe.</summary>
    public RecordSnapshot? Before { get; init; }

    /// <summary>Record snapshot after the mutation when available and safe.</summary>
    public RecordSnapshot? After { get; init; }
}

/// <summary>
/// Resource identity carried by BASE events.
/// </summary>
public sealed record EventResource
{
    /// <summary>Kind of BASE resource affected by the event.</summary>
    public required EventResourceKind Kind { get; init; }

    /// <summary>Collection id when the resource belongs to a collection.</summary>
    public string? CollectionId { get; init; }

    /// <summary>Record id when the resource is a record.</summary>
    public RecordId? RecordId { get; init; }

    /// <summary>Optional stable resource path.</summary>
    public string? ResourcePath { get; init; }
}

/// <summary>
/// Safe summary of the principal that caused a BASE event.
/// </summary>
public sealed record EventPrincipalSummary
{
    /// <summary>Authentication state of the principal.</summary>
    public PrincipalAuthenticationState AuthenticationState { get; init; }

    /// <summary>Subject id when known.</summary>
    public string? SubjectId { get; init; }

    /// <summary>Kind of subject that caused the event.</summary>
    public AccessSubjectKind SubjectKind { get; init; }

    /// <summary>Tenant id associated with the principal when known.</summary>
    public string? TenantId { get; init; }

    /// <summary>Authentication source when known.</summary>
    public string? AuthSource { get; init; }

    /// <summary>Whether the principal is a service principal.</summary>
    public bool IsServicePrincipal { get; init; }

    /// <summary>Whether the principal has admin authentication state.</summary>
    public bool IsAdmin { get; init; }
}

/// <summary>
/// Snapshot of a record included with a BASE event.
/// </summary>
public sealed record RecordSnapshot
{
    /// <summary>Collection that owns the record.</summary>
    public required string CollectionId { get; init; }

    /// <summary>Record id.</summary>
    public required RecordId Id { get; init; }

    /// <summary>Record payload when included.</summary>
    public RecordPayload? Payload { get; init; }

    /// <summary>Record metadata when included.</summary>
    public RecordMetadata? Metadata { get; init; }

    /// <summary>Payload fields included in the snapshot when known.</summary>
    public string[]? IncludedFields { get; init; }

    /// <summary>Whether the snapshot was redacted.</summary>
    public bool Redacted { get; init; }
}
