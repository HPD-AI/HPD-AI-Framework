namespace HPD.Base;

/// <summary>Represents a event publish result.</summary>
public sealed record EventPublishResult
{
    /// <summary>Gets or sets the event ID.</summary>
    public required string EventId { get; init; }
    /// <summary>Gets or sets the stream.</summary>
    public string? Stream { get; init; }
    /// <summary>Gets or sets the published at.</summary>
    public DateTimeOffset? PublishedAt { get; init; }
    /// <summary>Gets or sets the guarantee.</summary>
    public EventDeliveryGuarantee Guarantee { get; init; }
}
