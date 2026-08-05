namespace HPD.Base;

/// <summary>Represents a base runtime validation issue.</summary>
public sealed record BaseRuntimeValidationIssue
{
    /// <summary>Gets or sets the severity.</summary>
    public required BaseRuntimeValidationSeverity Severity { get; init; }
    /// <summary>Gets or sets the kind.</summary>
    public required BaseRuntimeValidationFailureKind Kind { get; init; }
    /// <summary>Gets or sets the code.</summary>
    public required string Code { get; init; }
    /// <summary>Gets or sets the message.</summary>
    public required string Message { get; init; }
    /// <summary>Gets or sets the target ref.</summary>
    public string? TargetRef { get; init; }
    /// <summary>Gets or sets the target path.</summary>
    public string? TargetPath { get; init; }
}
