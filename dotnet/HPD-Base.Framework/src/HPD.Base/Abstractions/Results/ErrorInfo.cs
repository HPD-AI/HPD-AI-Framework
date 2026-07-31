namespace HPD.Base.Results;

public sealed record ConflictInfo
{
    public required ConflictKind Kind { get; init; }
    public string? Resource { get; init; }
    public string? ExpectedRevision { get; init; }
    public string? ActualRevision { get; init; }
    public string? Constraint { get; init; }
}

public sealed record CapabilityErrorInfo
{
    public required string Capability { get; init; }
    public required CapabilityFailureReason Reason { get; init; }
    public string? ModuleId { get; init; }
    public string? StoreId { get; init; }
    public string? RequiredVersion { get; init; }
    public string? CurrentVersion { get; init; }
}

public sealed record PolicyErrorInfo
{
    public string? PolicyId { get; init; }
    public string? ReasonCode { get; init; }
    public string[]? Obligations { get; init; }
}

public sealed record StoreErrorInfo
{
    public string? StoreId { get; init; }
    public string? NativeCode { get; init; }
    public string? NativeSubcode { get; init; }
    public string? NativeCategory { get; init; }
    public string? NativeMessage { get; init; }
    public bool Retryable { get; init; }
}
