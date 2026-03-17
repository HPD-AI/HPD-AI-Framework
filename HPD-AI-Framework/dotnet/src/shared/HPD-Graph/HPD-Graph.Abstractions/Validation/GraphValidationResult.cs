namespace HPDAgent.Graph.Abstractions.Validation;

/// <summary>
/// Result of graph validation.
/// </summary>
public sealed record GraphValidationResult
{
    /// <summary>
    /// Whether the graph is valid.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Validation errors (empty if valid).
    /// </summary>
    public IReadOnlyList<GraphValidationError> Errors { get; init; } = Array.Empty<GraphValidationError>();

    /// <summary>
    /// Validation warnings (non-blocking issues).
    /// </summary>
    public IReadOnlyList<GraphValidationWarning> Warnings { get; init; } = Array.Empty<GraphValidationWarning>();

    /// <summary>
    /// Create a successful validation result.
    /// </summary>
    public static GraphValidationResult Success(IReadOnlyList<GraphValidationWarning>? warnings = null)
    {
        return new GraphValidationResult
        {
            IsValid = true,
            Warnings = warnings ?? Array.Empty<GraphValidationWarning>()
        };
    }

    /// <summary>
    /// Create a failed validation result.
    /// </summary>
    public static GraphValidationResult Failure(IReadOnlyList<GraphValidationError> errors, IReadOnlyList<GraphValidationWarning>? warnings = null)
    {
        return new GraphValidationResult
        {
            IsValid = false,
            Errors = errors,
            Warnings = warnings ?? Array.Empty<GraphValidationWarning>()
        };
    }
}

/// <summary>
/// Graph validation error (blocks execution).
/// </summary>
public sealed record GraphValidationError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? NodeId { get; init; }
    public string? EdgeId { get; init; }
}

/// <summary>
/// Graph validation warning (non-blocking).
/// </summary>
public sealed record GraphValidationWarning
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? NodeId { get; init; }
    public string? EdgeId { get; init; }
}
