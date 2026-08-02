
namespace HPD.Base;

/// <summary>Represents a operation warning.</summary>
public sealed record OperationWarning
{
    /// <summary>Gets or sets the code.</summary>
    public required string Code { get; init; }
    /// <summary>Gets or sets the message.</summary>
    public required string Message { get; init; }
    /// <summary>Gets or sets the target.</summary>
    public string? Target { get; init; }
    /// <summary>Gets or sets the capability path.</summary>
    public string? CapabilityPath { get; init; }
}

/// <summary>Represents a operation diagnostics.</summary>
public sealed record OperationDiagnostics
{
    /// <summary>Gets or sets the trace ID.</summary>
    public string? TraceId { get; init; }
    /// <summary>Gets or sets the correlation ID.</summary>
    public string? CorrelationId { get; init; }
    /// <summary>Gets or sets the safe data.</summary>
    public Dictionary<string, string>? SafeData { get; init; }
}

/// <summary>Represents a revision info.</summary>
public sealed record RevisionInfo
{
    /// <summary>Gets or sets the revision.</summary>
    public string? Revision { get; init; }
    /// <summary>Gets or sets the etag.</summary>
    public string? ETag { get; init; }
    /// <summary>Gets or sets the last modified.</summary>
    public DateTimeOffset? LastModified { get; init; }
    /// <summary>Gets or sets the guarantee.</summary>
    public RevisionGuarantee Guarantee { get; init; }
}

/// <summary>Represents a event reference.</summary>
public sealed record EventReference
{
    /// <summary>Gets or sets the event ID.</summary>
    public required string EventId { get; init; }
    /// <summary>Gets or sets the type.</summary>
    public required string Type { get; init; }
    /// <summary>Gets or sets the stream.</summary>
    public string? Stream { get; init; }
    /// <summary>Gets or sets the resource.</summary>
    public string? Resource { get; init; }
    /// <summary>Gets or sets the published at.</summary>
    public DateTimeOffset? PublishedAt { get; init; }
    /// <summary>Gets or sets the guarantee.</summary>
    public EventDeliveryGuarantee Guarantee { get; init; }
}
