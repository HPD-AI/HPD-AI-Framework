using HPD.Base;

namespace HPD.Base.Runtime;

public sealed record OperationContext
{
    public required BaseOperationKind Operation { get; init; }
    public required string CollectionId { get; init; }
    public string? RecordId { get; init; }
    public string? TenantId { get; init; }
    public string? ProjectId { get; init; }
    public OperationMode Mode { get; init; } = OperationMode.User;
    public string? CorrelationId { get; init; }
    public DateTimeOffset Now { get; init; }
    public RequestContext? Request { get; init; }
    public Dictionary<string, string>? CapabilityHints { get; init; }
}

public sealed record RequestContext
{
    public string? Method { get; init; }
    public string? Route { get; init; }
    public string? ClientName { get; init; }
    public string? ClientVersion { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string[]? QueryKeys { get; init; }
    public bool Redacted { get; init; } = true;
}
