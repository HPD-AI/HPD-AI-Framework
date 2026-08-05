namespace HPD.Base;

/// <summary>Represents a base runtime validation result.</summary>
public sealed record BaseRuntimeValidationResult
{
    /// <summary>Gets or sets the succeeded.</summary>
    public required bool Succeeded { get; init; }
    /// <summary>Gets or sets the issues.</summary>
    public BaseRuntimeValidationIssue[]? Issues { get; init; }

    /// <summary>Gets the success.</summary>
    public static BaseRuntimeValidationResult Success { get; } = new() { Succeeded = true };
}
