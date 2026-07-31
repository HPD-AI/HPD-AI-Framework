namespace HPD.Base;

public sealed record BaseRuntimeValidationIssue
{
    public required BaseRuntimeValidationSeverity Severity { get; init; }
    public required BaseRuntimeValidationFailureKind Kind { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? TargetRef { get; init; }
    public string? TargetPath { get; init; }
}
