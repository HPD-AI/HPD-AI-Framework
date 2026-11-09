using HPD.Agent.ErrorHandling;

namespace HPD.Agent.Middleware;

/// <summary>
/// Source of an error in agent execution.
/// Used to route errors to appropriate handlers.
/// </summary>
public enum ErrorSource
{
    /// <summary>Error during LLM model call</summary>
    ModelCall,

    /// <summary>Error during tool/function execution</summary>
    ToolCall,

    /// <summary>Error during iteration processing</summary>
    Iteration,

    /// <summary>Error during message turn processing</summary>
    MessageTurn,

    /// <summary>Error while dispatching an agent input event</summary>
    Input,

    /// <summary>Error during runtime start, stop, or resource lifecycle</summary>
    Runtime,

    /// <summary>Error while preparing or committing a thread fork</summary>
    ThreadFork
}

/// <summary>
/// Context for OnError hook.
/// Provides centralized error handling, logging, and recovery.
/// </summary>
/// <remarks>
/// <para><b>Execution Order:</b></para>
/// <para>
/// OnErrorAsync executes in REVERSE registration order (like After* hooks)
/// for proper error handler unwinding.
/// </para>
/// <para>
/// Registration: [ErrorLogger, CircuitBreaker, Telemetry]<br/>
/// Execution: Telemetry.OnErrorAsync → CircuitBreaker.OnErrorAsync → ErrorLogger.OnErrorAsync
/// </para>
///
/// <para><b>Error Propagation:</b></para>
/// <list type="bullet">
/// <item>By default, errors propagate after all OnErrorAsync handlers complete</item>
/// <item>Each terminal exception is reported once, even when it crosses multiple execution boundaries</item>
/// <item>Caller cancellation uses the cancellation lifecycle and is not reported as an agent failure</item>
/// <item>To stop further agent iterations after observing an error, update the state with IsTerminated = true</item>
/// <item>If OnErrorAsync itself throws, the original error is preserved and propagated</item>
/// <item>Error handlers execute in sequence - each handler sees the original error</item>
/// </list>
///
/// <para><b>Use Cases:</b></para>
/// <list type="bullet">
/// <item>Centralized error logging</item>
/// <item>Circuit breaker logic (count errors, terminate on threshold)</item>
/// <item>Error transformation (e.g., sanitize error messages)</item>
/// <item>Graceful degradation (swallow errors for non-critical operations)</item>
/// </list>
///
/// <para><b>Example: Circuit Breaker with Graceful Termination</b></para>
/// <code>
/// public Task OnErrorAsync(ErrorContext context, CancellationToken ct)
/// {
///     if (context.Source == ErrorSource.ToolCall)
///     {
///         var failures = context.GetMiddlewareState&lt;MyErrorState&gt;()?.Count ?? 0;
///         context.UpdateMiddlewareState&lt;MyErrorState&gt;(state => state with
///         {
///             Count = state.Count + 1
///         });
///
///         if (failures + 1 >= 3)
///         {
///             // Stop further iterations after the current error is observed.
///             context.UpdateState(s => s with
///             {
///                 IsTerminated = true,
///                 TerminationReason = "Circuit breaker: too many errors"
///             });
///         }
///     }
///
///     return Task.CompletedTask;
/// }
/// </code>
/// </remarks>
public sealed class ErrorContext : HookContext
{
    /// <summary>
    /// The exception that occurred.
    ///   Always available (never NULL)
    /// </summary>
    public Exception Error { get; }

    /// <summary>
    /// Where the error originated.
    ///   Always available
    /// </summary>
    public ErrorSource Source { get; }

    /// <summary>
    /// Current iteration number (if error during iteration).
    ///   Always available
    /// </summary>
    public int Iteration { get; }

    /// <summary>
    /// Gets the run configuration associated with the failed operation, when available.
    /// </summary>
    public AgentRunConfig? RunConfig { get; }

    //
    // HELPERS
    //

    /// <summary>
    /// True if error came from LLM model call.
    /// </summary>
    public bool IsModelError => Source == ErrorSource.ModelCall;

    /// <summary>
    /// True if error came from tool/function execution.
    /// </summary>
    public bool IsToolError => Source == ErrorSource.ToolCall;

    /// <summary>
    /// True if error came from iteration processing.
    /// </summary>
    public bool IsIterationError => Source == ErrorSource.Iteration;

    /// <summary>
    /// True if error came from message turn processing.
    /// </summary>
    public bool IsMessageTurnError => Source == ErrorSource.MessageTurn;

    /// <summary>
    /// True if error came from input dispatch.
    /// </summary>
    public bool IsInputError => Source == ErrorSource.Input;

    /// <summary>
    /// True if error came from runtime lifecycle processing.
    /// </summary>
    public bool IsRuntimeError => Source == ErrorSource.Runtime;

    /// <summary>
    /// True if error came from thread fork processing.
    /// </summary>
    public bool IsThreadForkError => Source == ErrorSource.ThreadFork;

    /// <summary>
    /// Parsed error details from provider-specific error handler.
    /// Lazily computed on first access using GenericErrorHandler.
    /// </summary>
    public ProviderErrorDetails? ErrorDetails => _errorDetails ??= ParseErrorDetails();

    private ProviderErrorDetails? _errorDetails;

    private ProviderErrorDetails? ParseErrorDetails()
    {
        var handler = new GenericErrorHandler();
        return handler.ParseError(Error);
    }

    /// <summary>
    /// Error category (e.g., ModelNotFound, RateLimitRetryable, AuthError).
    /// Convenience property that delegates to ErrorDetails.
    /// </summary>
    public ErrorCategory? ErrorCategory => ErrorDetails?.Category;

    /// <summary>
    /// True if this is a model not found error.
    /// </summary>
    public bool IsModelNotFoundError => ErrorCategory == ErrorHandling.ErrorCategory.ModelNotFound;

    /// <summary>
    /// True if this error is retryable (transient, rate limit, server error).
    /// </summary>
    public bool IsRetryableError => ErrorCategory is
        ErrorHandling.ErrorCategory.Transient or
        ErrorHandling.ErrorCategory.RateLimitRetryable or
        ErrorHandling.ErrorCategory.ServerError;

    internal ErrorContext(
        AgentContext baseContext,
        Exception error,
        ErrorSource source,
        int iteration,
        AgentRunConfig? runConfig = null)
        : base(baseContext)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
        Source = source;
        Iteration = iteration;
        RunConfig = runConfig;
    }
}
