using System.Text.Json;

namespace HPD.Base.Results;

public sealed record BaseError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Target { get; init; }
    public string? Detail { get; init; }
    public string? Hint { get; init; }
    public ErrorCategory Category { get; init; }
    public ValidationIssue[]? Validation { get; init; }
    public ConflictInfo? Conflict { get; init; }
    public CapabilityErrorInfo? Capability { get; init; }
    public PolicyErrorInfo? Policy { get; init; }
    public StoreErrorInfo? Store { get; init; }
    public string? TraceId { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record ValidationIssue
{
    public required string Path { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public JsonElement? RejectedValue { get; init; }
    public Dictionary<string, string>? Parameters { get; init; }
}
