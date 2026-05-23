namespace HPD.Execution.AppleVirtualization.DevKit;

public enum AppleVirtualizationDevKitDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record AppleVirtualizationDevKitDiagnostic
{
    public required AppleVirtualizationDevKitDiagnosticSeverity Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Variable { get; init; }
    public string? Path { get; init; }
}

public sealed record AppleVirtualizationDevKitValidationResult
{
    public required bool IsValid { get; init; }
    public IReadOnlyList<AppleVirtualizationDevKitDiagnostic> Diagnostics { get; init; } = Array.Empty<AppleVirtualizationDevKitDiagnostic>();
}
