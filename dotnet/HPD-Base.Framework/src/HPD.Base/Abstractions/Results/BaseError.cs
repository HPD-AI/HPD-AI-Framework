using System.Text.Json;

namespace HPD.Base;

/// <summary>Represents a base error.</summary>
public sealed record BaseError
{
    /// <summary>Gets or sets the code.</summary>
    public required string Code { get; init; }
    /// <summary>Gets or sets the message.</summary>
    public required string Message { get; init; }
    /// <summary>Gets or sets the target.</summary>
    public string? Target { get; init; }
    /// <summary>Gets or sets the detail.</summary>
    public string? Detail { get; init; }
    /// <summary>Gets or sets the hint.</summary>
    public string? Hint { get; init; }
    /// <summary>Gets or sets the category.</summary>
    public ErrorCategory Category { get; init; }
    /// <summary>Gets or sets the validation.</summary>
    public ValidationIssue[]? Validation { get; init; }
    /// <summary>Gets or sets the conflict.</summary>
    public ConflictInfo? Conflict { get; init; }
    /// <summary>Gets or sets the capability.</summary>
    public CapabilityErrorInfo? Capability { get; init; }
    /// <summary>Gets or sets the policy.</summary>
    public PolicyErrorInfo? Policy { get; init; }
    /// <summary>Gets or sets the store.</summary>
    public StoreErrorInfo? Store { get; init; }
    /// <summary>Gets the safe store disposition for a failed destructive restore.</summary>
    public BaseRestoreFailureDisposition? RestoreFailureDisposition { get; init; }
    /// <summary>Gets or sets the trace ID.</summary>
    public string? TraceId { get; init; }
    /// <summary>Gets or sets the correlation ID.</summary>
    public string? CorrelationId { get; init; }
}

/// <summary>Represents a validation issue.</summary>
public sealed record ValidationIssue
{
    /// <summary>Gets or sets the path.</summary>
    public required string Path { get; init; }
    /// <summary>Gets or sets the code.</summary>
    public required string Code { get; init; }
    /// <summary>Gets or sets the message.</summary>
    public required string Message { get; init; }
    /// <summary>Gets or sets the rejected value.</summary>
    public JsonElement? RejectedValue { get; init; }
    /// <summary>Gets or sets the parameters.</summary>
    public Dictionary<string, string>? Parameters { get; init; }
}
