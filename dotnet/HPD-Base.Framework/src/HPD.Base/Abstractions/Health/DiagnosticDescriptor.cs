
namespace HPD.Base;

/// <summary>Represents a diagnostic descriptor.</summary>
public sealed record DiagnosticDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the code.</summary>
    public required string Code { get; init; }
    /// <summary>Gets or sets the severity.</summary>
    public DiagnosticSeverity Severity { get; init; }
    /// <summary>Gets or sets the target ref.</summary>
    public string? TargetRef { get; init; }
    /// <summary>Gets or sets the message.</summary>
    public required string Message { get; init; }
    /// <summary>Gets or sets the public message.</summary>
    public string? PublicMessage { get; init; }
    /// <summary>Gets or sets the target path.</summary>
    public string? TargetPath { get; init; }
    /// <summary>Gets or sets the category.</summary>
    public DiagnosticCategory Category { get; init; }
    /// <summary>Gets or sets the remediation.</summary>
    public string? Remediation { get; init; }
    /// <summary>Gets or sets the related feature IDs.</summary>
    public string[]? RelatedFeatureIds { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; }
    /// <summary>Gets or sets the emitted at.</summary>
    public DateTimeOffset EmittedAt { get; init; }
}
