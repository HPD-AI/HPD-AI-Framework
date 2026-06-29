using HPD.Base;

namespace HPD.Base.Health;

public sealed record DiagnosticDescriptor
{
    public required string Id { get; init; }
    public required string Code { get; init; }
    public DiagnosticSeverity Severity { get; init; }
    public string? TargetRef { get; init; }
    public required string Message { get; init; }
    public string? PublicMessage { get; init; }
    public string? TargetPath { get; init; }
    public DiagnosticCategory Category { get; init; }
    public string? Remediation { get; init; }
    public string[]? RelatedFeatureIds { get; init; }
    public VisibilityLevel Visibility { get; init; }
    public DateTimeOffset EmittedAt { get; init; }
}
