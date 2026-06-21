namespace HPD.Agent.Middleware.Function;

/// <summary>
/// Middleware that enforces a timeout for function execution.
/// If a function takes longer than the configured timeout, it will be cancelled.
/// </summary>
/// <remarks>
/// <para><b>Timeout Enforcement:</b></para>
/// <para>
/// Creates a linked cancellation token source with the specified timeout.
/// If the function doesn't complete within the timeout period, the operation
/// is cancelled and a TimeoutException is thrown.
/// </para>
///
/// <para><b>Usage:</b></para>
/// <para>
/// This middleware should be registered INSIDE RetryMiddleware  (so retries happen before timeout),
/// but OUTSIDE permissions middleware (so timeout wraps actual execution).
/// </para>
///
/// <para><b>Example Registration Order:</b></para>
/// <code>
/// .WithFunctionRetry()   // Outermost - retry the entire timeout operation
/// .WithFunctionTimeout() // Middle - timeout individual attempts
/// .WithPermissions()     // Innermost - check permissions before execution
/// </code>
/// </remarks>
public class FunctionTimeoutMiddleware : IAgentMiddleware
{
    private readonly TimeSpan _timeout;

    /// <summary>
    /// Creates a new function timeout middleware with the specified timeout.
    /// </summary>
    /// <param name="timeout">Maximum time allowed for function execution</param>
    public FunctionTimeoutMiddleware(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentException("Timeout must be greater than zero", nameof(timeout));

        _timeout = timeout;
    }

    /// <summary>
    /// Wraps function execution with timeout enforcement.
    /// </summary>
    public async Task<object?> WrapFunctionCallAsync(
        FunctionRequest request,
        Func<FunctionRequest, Task<object?>> handler,
        CancellationToken cancellationToken)
    {
        try
        {
            // Use Task.WaitAsync to enforce timeout
            var task = handler(request);
            return await task.WaitAsync(_timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            // Re-throw with more descriptive message
            throw new TimeoutException(
                $"Function '{request.Function?.Name ?? "Unknown"}' timed out after {_timeout.TotalSeconds:F1} seconds");
        }
    }
}
