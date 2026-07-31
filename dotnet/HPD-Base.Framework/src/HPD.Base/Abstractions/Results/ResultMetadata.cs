
namespace HPD.Base;

public sealed record OperationWarning
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Target { get; init; }
    public string? CapabilityPath { get; init; }
}

public sealed record OperationDiagnostics
{
    public string? TraceId { get; init; }
    public string? CorrelationId { get; init; }
    public Dictionary<string, string>? SafeData { get; init; }
}

public sealed record RevisionInfo
{
    public string? Revision { get; init; }
    public string? ETag { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public RevisionGuarantee Guarantee { get; init; }
}

public sealed record EventReference
{
    public required string EventId { get; init; }
    public required string Type { get; init; }
    public string? Stream { get; init; }
    public string? Resource { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public EventDeliveryGuarantee Guarantee { get; init; }
}
