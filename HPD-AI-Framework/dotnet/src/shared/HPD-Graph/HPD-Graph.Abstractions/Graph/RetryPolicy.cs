namespace HPDAgent.Graph.Abstractions.Graph;

/// <summary>
/// Backoff strategy for retries.
/// </summary>
public enum BackoffStrategy
{
    /// <summary>
    /// Constant delay between retries.
    /// </summary>
    Constant,

    /// <summary>
    /// Exponential backoff (delay doubles each time).
    /// </summary>
    Exponential,

    /// <summary>
    /// Linear backoff (delay increases by fixed amount).
    /// </summary>
    Linear,

    /// <summary>
    /// Jittered exponential backoff (prevents thundering herd).
    /// Adds random jitter (50-150% of base delay) to prevent synchronized retries.
    /// Recommended for high-concurrency scenarios (AWS best practice).
    /// </summary>
    JitteredExponential
}

/// <summary>
/// Retry policy for a node.
/// </summary>
public sealed record RetryPolicy
{
    /// <summary>
    /// Maximum number of attempts (including initial attempt).
    /// Example: MaxAttempts = 3 means 1 initial + 2 retries.
    /// </summary>
    public required int MaxAttempts { get; init; }

    /// <summary>
    /// Initial delay before first retry.
    /// </summary>
    public required TimeSpan InitialDelay { get; init; }

    /// <summary>
    /// Backoff strategy.
    /// </summary>
    public BackoffStrategy Strategy { get; init; } = BackoffStrategy.Exponential;

    /// <summary>
    /// Maximum delay between retries (caps exponential/linear growth).
    /// </summary>
    public TimeSpan? MaxDelay { get; init; }

    /// <summary>
    /// Which exception types should trigger a retry.
    /// Null = retry all exceptions.
    /// </summary>
    public IReadOnlyList<Type>? RetryableExceptions { get; init; }

    /// <summary>
    /// Calculate delay for a specific attempt.
    /// </summary>
    /// <param name="attemptNumber">Attempt number (1 = first retry, 2 = second retry, etc.)</param>
    /// <returns>Delay before this attempt</returns>
    public TimeSpan GetDelay(int attemptNumber)
    {
        if (attemptNumber <= 0)
        {
            return TimeSpan.Zero;
        }

        var delay = Strategy switch
        {
            BackoffStrategy.Constant => InitialDelay,
            BackoffStrategy.Exponential => TimeSpan.FromMilliseconds(
                InitialDelay.TotalMilliseconds * Math.Pow(2, attemptNumber - 1)),
            BackoffStrategy.Linear => TimeSpan.FromMilliseconds(
                InitialDelay.TotalMilliseconds * attemptNumber),
            BackoffStrategy.JitteredExponential => TimeSpan.FromMilliseconds(
                InitialDelay.TotalMilliseconds * Math.Pow(2, attemptNumber - 1) *
                (0.5 + Random.Shared.NextDouble())), // 50-150% of base delay
            _ => InitialDelay
        };

        if (MaxDelay.HasValue && delay > MaxDelay.Value)
        {
            return MaxDelay.Value;
        }

        return delay;
    }

    /// <summary>
    /// Check if an exception should trigger a retry.
    /// </summary>
    public bool ShouldRetry(Exception exception)
    {
        if (RetryableExceptions == null || RetryableExceptions.Count == 0)
        {
            return true; // Retry all exceptions
        }

        var exceptionType = exception.GetType();
        return RetryableExceptions.Any(t => t.IsAssignableFrom(exceptionType));
    }
}
