namespace HPD.Base;

/// <summary>Represents a conflict info.</summary>
public sealed record ConflictInfo
{
    /// <summary>Gets or sets the kind.</summary>
    public required ConflictKind Kind { get; init; }
    /// <summary>Gets or sets the resource.</summary>
    public string? Resource { get; init; }
    /// <summary>Gets or sets the expected revision.</summary>
    public string? ExpectedRevision { get; init; }
    /// <summary>Gets or sets the actual revision.</summary>
    public string? ActualRevision { get; init; }
    /// <summary>Gets or sets the constraint.</summary>
    public string? Constraint { get; init; }
}

/// <summary>Represents a capability error info.</summary>
public sealed record CapabilityErrorInfo
{
    /// <summary>Gets or sets the capability.</summary>
    public required string Capability { get; init; }
    /// <summary>Gets or sets the reason.</summary>
    public required CapabilityFailureReason Reason { get; init; }
    /// <summary>Gets or sets the module ID.</summary>
    public string? ModuleId { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public string? StoreId { get; init; }
    /// <summary>Gets or sets the required version.</summary>
    public string? RequiredVersion { get; init; }
    /// <summary>Gets or sets the current version.</summary>
    public string? CurrentVersion { get; init; }
}

/// <summary>Represents a policy error info.</summary>
public sealed record PolicyErrorInfo
{
    /// <summary>Gets or sets the policy ID.</summary>
    public string? PolicyId { get; init; }
    /// <summary>Gets or sets the reason code.</summary>
    public string? ReasonCode { get; init; }
    /// <summary>Gets or sets the obligations.</summary>
    public string[]? Obligations { get; init; }
}

/// <summary>Represents a store error info.</summary>
public sealed record StoreErrorInfo
{
    /// <summary>Gets or sets the store ID.</summary>
    public string? StoreId { get; init; }
    /// <summary>Gets or sets the native code.</summary>
    public string? NativeCode { get; init; }
    /// <summary>Gets or sets the native subcode.</summary>
    public string? NativeSubcode { get; init; }
    /// <summary>Gets or sets the native category.</summary>
    public string? NativeCategory { get; init; }
    /// <summary>Gets or sets the native message.</summary>
    public string? NativeMessage { get; init; }
    /// <summary>Gets or sets the retryable.</summary>
    public bool Retryable { get; init; }
}
