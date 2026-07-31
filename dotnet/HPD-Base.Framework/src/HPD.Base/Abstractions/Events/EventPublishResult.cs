namespace HPD.Base;

public sealed record EventPublishResult
{
    public required string EventId { get; init; }
    public string? Stream { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public EventDeliveryGuarantee Guarantee { get; init; }
}
