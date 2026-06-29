using System.Text.Json;
using HPD.Base.Policy;
using HPD.Base.Records;
using HPD.Base.Runtime;

namespace HPD.Base.Events;

public sealed record BaseEventEnvelope
{
    public required string EventId { get; init; }
    public required string Type { get; init; }
    public required string EnvelopeVersion { get; init; }
    public required EventResource Resource { get; init; }
    public required BaseOperationKind Operation { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public string? TenantId { get; init; }
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public EventPrincipalSummary? Principal { get; init; }
    public string[]? ChangedFields { get; init; }
    public RecordSnapshot? Before { get; init; }
    public RecordSnapshot? After { get; init; }
    public VisibilityLevel Visibility { get; init; }
    public string[]? Audience { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record EventResource
{
    public required EventResourceKind Kind { get; init; }
    public string? CollectionId { get; init; }
    public RecordId? RecordId { get; init; }
    public string? ResourcePath { get; init; }
}

public sealed record EventPrincipalSummary
{
    public PrincipalAuthenticationState AuthenticationState { get; init; }
    public string? SubjectId { get; init; }
    public AccessSubjectKind SubjectKind { get; init; }
    public string? TenantId { get; init; }
    public string? AuthSource { get; init; }
    public bool IsServicePrincipal { get; init; }
    public bool IsAdmin { get; init; }
}

public sealed record RecordSnapshot
{
    public required string CollectionId { get; init; }
    public required RecordId Id { get; init; }
    public RecordPayload? Payload { get; init; }
    public RecordMetadata? Metadata { get; init; }
    public string[]? IncludedFields { get; init; }
    public bool Redacted { get; init; }
}
