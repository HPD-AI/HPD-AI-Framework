namespace HPD.Base.AspNetCore.Results;

/// <summary>
/// Carries HTTP-specific result mapping hints for HPD.BASE endpoint responses.
/// </summary>
public sealed record HPDBaseHttpResultMappingContext
{
    /// <summary>
    /// Gets whether the route is an admin route.
    /// </summary>
    public bool IsAdmin { get; init; }

    /// <summary>
    /// Gets a location URI to emit for created resources.
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Gets the safe correlation id to echo when Runtime did not provide one.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Gets a Retry-After value to emit for HTTP retryable responses.
    /// </summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>
    /// Gets preference tokens applied by the HTTP projection.
    /// </summary>
    public string[]? PreferenceApplied { get; init; }
}
