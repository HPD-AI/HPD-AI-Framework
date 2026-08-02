
namespace HPD.Base;

/// <summary>Represents a operation context.</summary>
public sealed record OperationContext
{
    /// <summary>Gets or sets the operation.</summary>
    public required BaseOperationKind Operation { get; init; }
    /// <summary>Gets or sets the collection ID.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets or sets the record ID.</summary>
    public string? RecordId { get; init; }
    /// <summary>Gets or sets the tenant ID.</summary>
    public string? TenantId { get; init; }
    /// <summary>Gets or sets the project ID.</summary>
    public string? ProjectId { get; init; }
    /// <summary>Gets or sets the mode.</summary>
    public OperationMode Mode { get; init; } = OperationMode.User;
    /// <summary>Gets or sets the correlation ID.</summary>
    public string? CorrelationId { get; init; }
    /// <summary>Gets or sets the now.</summary>
    public DateTimeOffset Now { get; init; }
    /// <summary>Gets or sets the request.</summary>
    public RequestContext? Request { get; init; }
    /// <summary>Gets or sets the capability hints.</summary>
    public Dictionary<string, string>? CapabilityHints { get; init; }
}

/// <summary>Represents a request context.</summary>
public sealed record RequestContext
{
    /// <summary>Gets or sets the method.</summary>
    public string? Method { get; init; }
    /// <summary>Gets or sets the route.</summary>
    public string? Route { get; init; }
    /// <summary>Gets or sets the client name.</summary>
    public string? ClientName { get; init; }
    /// <summary>Gets or sets the client version.</summary>
    public string? ClientVersion { get; init; }
    /// <summary>Gets or sets the ip address.</summary>
    public string? IpAddress { get; init; }
    /// <summary>Gets or sets the user agent.</summary>
    public string? UserAgent { get; init; }
    /// <summary>Gets or sets the query keys.</summary>
    public string[]? QueryKeys { get; init; }
    /// <summary>Gets or sets the redacted.</summary>
    public bool Redacted { get; init; } = true;
}
