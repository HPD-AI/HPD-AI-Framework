namespace HPDAgent.Graph.Abstractions.Validation;

/// <summary>
/// Declarative schema for handler input validation.
/// Validated before handler execution (fail-fast).
/// </summary>
public sealed record InputSchema
{
    /// <summary>Expected input type.</summary>
    public required Type Type { get; init; }

    /// <summary>Whether this input is required.</summary>
    public bool Required { get; init; } = true;

    /// <summary>Optional custom validator.</summary>
    public IInputValidator? Validator { get; init; }

    /// <summary>Default value if input missing and not required.</summary>
    public object? DefaultValue { get; init; }

    /// <summary>Human-readable description.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// Custom validation logic for inputs.
/// </summary>
public interface IInputValidator
{
    /// <summary>
    /// Validate an input value.
    /// </summary>
    /// <param name="inputName">Name of the input being validated</param>
    /// <param name="value">Value to validate</param>
    /// <returns>Validation result</returns>
    ValidationResult Validate(string inputName, object? value);
}

/// <summary>
/// Result of input validation.
/// </summary>
public sealed record ValidationResult
{
    public required bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public static ValidationResult Success()
        => new() { IsValid = true };

    public static ValidationResult Failure(params string[] errors)
        => new() { IsValid = false, Errors = errors };
}
