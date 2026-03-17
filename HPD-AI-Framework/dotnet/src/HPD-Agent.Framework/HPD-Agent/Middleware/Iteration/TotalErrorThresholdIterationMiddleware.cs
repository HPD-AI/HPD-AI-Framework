using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Tracks total errors across iterations (regardless of type) and terminates when threshold is exceeded.
/// Complements ErrorTrackingMiddleware (which only counts consecutive errors of the same pattern).
///
/// This catches scenarios where the agent encounters different errors progressively:
/// - Iteration 1: Timeout error
/// - Iteration 2: Permission denied
/// - Iteration 3: Resource exhausted
/// After N such different errors, this middleware stops execution.
/// </summary>
/// <remarks>
/// <para><b>UNIFIED MIDDLEWARE:</b></para>
/// <para>
/// This middleware implements <see cref="IAgentMiddleware"/> and is stateless -
/// all state flows through the context via <see cref="TotalErrorThresholdStateData"/>.
/// This preserves Agent's thread-safety guarantee for concurrent RunAsync() calls.
/// </para>
///
/// <para><b>Error tracking happens in AfterIterationAsync:</b></para>
/// <list type="number">
/// <item>Counts any errors (regardless of type or consecutiveness)</item>
/// <item>Updates TotalErrorThresholdStateData via context.UpdateState()</item>
/// <item>Checks threshold and triggers termination if exceeded</item>
/// </list>
///
/// <para><b>Relationship to other middlewares:</b></para>
/// <list type="bullet">
/// <item>ErrorTrackingMiddleware: Stops on 3 CONSECUTIVE same errors</item>
/// <item>CircuitBreakerMiddleware: Stops on 3 IDENTICAL tool calls</item>
/// <item>TotalErrorThresholdMiddleware: Stops on N TOTAL errors (any type)</item>
/// </list>
///
/// <para><b>When triggered:</b></para>
/// <list type="number">
/// <item>Emits a <see cref="TotalErrorThresholdExceededEvent"/> for observability</item>
/// <item>Signals termination via Properties["IsTerminated"] = true</item>
/// <item>Provides a termination message with error count</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // Register via AgentBuilder
/// var agent = new AgentBuilder()
///     .WithErrorTracking(maxConsecutiveErrors: 3)        // Consecutive same errors
///     .WithCircuitBreaker(maxConsecutiveCalls: 3)        // Identical tool calls
///     .WithTotalErrorThreshold(maxTotalErrors: 10)       // Any/all errors combined
///     .Build();
///
/// // Or with custom configuration
/// var agent = new AgentBuilder()
///     .WithTotalErrorThreshold(config =>
///     {
///         config.MaxTotalErrors = 15;
///         config.TerminationMessageTemplate = "Encountered {count} errors total. Stopping.";
///     })
///     .Build();
/// </code>
/// </example>
public class TotalErrorThresholdMiddleware : IAgentMiddleware
{
    //      
    // CONFIGURATION (not state - set at registration time)
    //      

    /// <summary>
    /// Maximum total errors allowed before triggering termination.
    /// Default: 10 (allows for some failures but prevents total degradation).
    /// </summary>
    public int MaxTotalErrors { get; set; } = 10;

    /// <summary>
    /// Custom error detection function. If not provided, uses default error detection.
    /// Return true if the FunctionResultContent represents an error.
    /// </summary>
    public Func<FunctionResultContent, bool>? CustomErrorDetector { get; set; }

    /// <summary>
    /// Custom termination message template.
    /// Placeholders: {totalErrors}, {maxErrors}
    /// </summary>
    public string TerminationMessageTemplate { get; set; } =
        "Total error threshold ({totalErrors}/{maxErrors}) exceeded. " +
        "Encountered too many errors of varying types. Stopping execution.";

    //      
    // HOOKS (stateless - read/write via context)
    //      

    /// <summary>
    /// Called BEFORE the LLM call begins.
    /// Checks if we're already at threshold to prevent wasted LLM calls.
    /// </summary>
    public Task BeforeIterationAsync(
        BeforeIterationContext context,
        CancellationToken cancellationToken)
    {
        // Skip check on first iteration (no previous errors to check)
        if (context.Iteration == 0)
            return Task.CompletedTask;

        // Check threshold
        var currentErrorCount = context.GetMiddlewareState<TotalErrorThresholdStateData>()?
            .TotalErrorCount ?? 0;

        if (currentErrorCount >= MaxTotalErrors)
        {
            // Already at threshold - terminate before wasting LLM call
            TriggerTermination(context, currentErrorCount);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Called AFTER tool execution completes.
    /// Counts any errors and updates total error count.
    /// </summary>
    public Task AfterIterationAsync(
        AfterIterationContext context,
        CancellationToken cancellationToken)
    {
        // If no tool results, nothing to analyze
        if (context.ToolResults.Count == 0)
            return Task.CompletedTask;

        // Count ALL errors in this iteration (regardless of type or consecutiveness)
        var errorCount = context.ToolResults.Count(IsError);

        if (errorCount > 0)
        {
            // Update state with cumulative error count
            context.UpdateMiddlewareState<TotalErrorThresholdStateData>(s => s with
            {
                TotalErrorCount = s.TotalErrorCount + errorCount
            });

            // Check if this iteration puts us over threshold
            var currentTotalCount = context.GetMiddlewareState<TotalErrorThresholdStateData>()?
                .TotalErrorCount ?? 0;

            if (currentTotalCount >= MaxTotalErrors)
            {
                TriggerTermination(context, currentTotalCount);
            }
        }

        return Task.CompletedTask;
    }

    //      
    // HELPERS
    //      

    /// <summary>
    /// Determines if a function result represents an error.
    /// Uses custom detector if provided, otherwise uses default detection.
    /// </summary>
    private bool IsError(FunctionResultContent result)
    {
        // Use custom detector if provided
        if (CustomErrorDetector != null)
            return CustomErrorDetector(result);

        // Default error detection (same as ErrorTrackingMiddleware)
        return IsDefaultError(result);
    }

    /// <summary>
    /// Default error detection logic.
    /// Matches the pattern used by ErrorTrackingMiddleware.
    /// </summary>
    private static bool IsDefaultError(FunctionResultContent result)
    {
        // Primary signal: Exception present
        if (result.Exception != null)
            return true;

        // Secondary signal: Result contains error indicators
        var resultStr = result.Result?.ToString();
        if (string.IsNullOrEmpty(resultStr))
            return false;

        // Check for definitive error patterns (case-insensitive)
        return resultStr.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ||
               resultStr.StartsWith("Failed:", StringComparison.OrdinalIgnoreCase) ||
               // Exception indicators
               resultStr.Contains("exception occurred", StringComparison.OrdinalIgnoreCase) ||
               resultStr.Contains("unhandled exception", StringComparison.OrdinalIgnoreCase) ||
               resultStr.Contains("exception was thrown", StringComparison.OrdinalIgnoreCase) ||
               // Rate limit indicators
               resultStr.Contains("rate limit exceeded", StringComparison.OrdinalIgnoreCase) ||
               resultStr.Contains("rate limited", StringComparison.OrdinalIgnoreCase) ||
               resultStr.Contains("quota exceeded", StringComparison.OrdinalIgnoreCase) ||
               resultStr.Contains("quota reached", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Triggers termination by setting termination flag and reason in context.
    /// Emits event for observability.
    /// </summary>
    private void TriggerTermination(HookContext context, int totalErrorCount)
    {
        // Format termination message with current counts
        var message = TerminationMessageTemplate
            .Replace("{totalErrors}", totalErrorCount.ToString())
            .Replace("{maxErrors}", MaxTotalErrors.ToString());

        // Signal termination via state update
        context.UpdateState(s => s with
        {
            IsTerminated = true,
            TerminationReason = message
        });

        // Emit event for observability
        // Emit TextDeltaEvent for user visibility (matching original behavior)
        try
        {
            context.Emit(new TextDeltaEvent(message, Guid.NewGuid().ToString()));
        }
        catch (InvalidOperationException)
        {
            // EventCoordinator not configured - event emission is optional
        }

        // Emit observability event
        try
        {
            context.Emit(new TotalErrorThresholdExceededEvent(
                AgentName: context.AgentName,
                TotalErrorCount: totalErrorCount,
                MaxTotalErrors: MaxTotalErrors,
                Iteration: 0, // V2 TODO: Not all contexts have Iteration
                Timestamp: DateTimeOffset.UtcNow));
        }
        catch (InvalidOperationException)
        {
            // EventCoordinator not configured - event emission is optional
        }
    }
}

/// <summary>
/// Event emitted when total error threshold is exceeded.
/// Used for observability and telemetry.
/// </summary>
public record TotalErrorThresholdExceededEvent(
    string AgentName,
    int TotalErrorCount,
    int MaxTotalErrors,
    int Iteration,
    DateTimeOffset Timestamp) : AgentEvent, IObservabilityEvent, IErrorEvent
{
    /// <inheritdoc />
    public string ErrorMessage => $"Total error threshold ({TotalErrorCount}/{MaxTotalErrors}) exceeded";

    /// <inheritdoc />
    public Exception? Exception => null;
}
