using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Tracks consecutive errors and terminates execution when threshold is exceeded.
/// Uses centralized OnErrorAsync hook for cleaner error handling.
/// </summary>
/// <remarks>
/// <para><b>V2 IMPROVEMENTS:</b></para>
/// <list type="bullet">
/// <item>Uses OnErrorAsync hook instead of analyzing tool results in AfterIterationAsync</item>
/// <item>Immediate state updates - no GetPendingState() needed!</item>
/// <item>Single AgentContext instance - no manual synchronization</item>
/// <item>Typed contexts provide compile-time safety</item>
/// </list>
///
/// <para><b>Migration from V1:</b></para>
/// <list type="bullet">
/// <item>Removed BeforeIterationAsync threshold check (OnErrorAsync handles termination)</item>
/// <item>AfterIterationAsync only resets counter on success (no error detection)</item>
/// <item>OnErrorAsync increments counter and triggers termination</item>
/// <item>No more GetPendingState() - context.State is always current!</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // Register via AgentBuilder
/// var agent = new AgentBuilder()
///     .WithMiddleware(new ErrorTrackingMiddleware { MaxConsecutiveErrors = 3 })
///     .Build();
/// </code>
/// </example>
public class ErrorTrackingMiddleware : IAgentMiddleware
{
    //     
    // CONFIGURATION (not state - set at registration time)
    //     

    /// <summary>
    /// Maximum number of consecutive errors allowed before triggering termination.
    /// Default: 3 (matches typical agent configuration).
    /// </summary>
    public int MaxConsecutiveErrors { get; set; } = 3;

    /// <summary>
    /// Custom termination message template.
    /// Placeholders: {count}, {max}
    /// </summary>
    public string TerminationMessageTemplate { get; set; } =
        "Maximum consecutive errors ({count}/{max}) exceeded. " +
        "Stopping execution to prevent infinite error loop.";

    //
    // HOOKS
    //

    /// <summary>
    /// Called when ANY error occurs (model call, tool call, iteration).
    /// Increments failure counter and triggers termination if threshold exceeded.
    /// </summary>
    public Task OnErrorAsync(ErrorContext context, CancellationToken cancellationToken)
    {
        //   Immediate state update - visible to all subsequent hooks!
        context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.IncrementFailures());

        // Check if threshold exceeded
        var currentFailures = context.GetMiddlewareState<ErrorTrackingStateData>()?
            .ConsecutiveFailures ?? 0;

        if (currentFailures >= MaxConsecutiveErrors)
        {
            TriggerTermination(context, currentFailures);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Called AFTER all tools complete for this iteration.
    /// Resets failure counter on successful iteration.
    /// </summary>
    public Task AfterIterationAsync(
        AfterIterationContext context,
        CancellationToken cancellationToken)
    {
        // Only reset if ALL tools succeeded
        if (context.AllToolsSucceeded)
        {
            //   Immediate state update - no GetPendingState() needed!
            context.UpdateMiddlewareState<ErrorTrackingStateData>(s => s.ResetFailures());
        }

        return Task.CompletedTask;
    }

    //
    // HELPERS
    //

    /// <summary>
    /// Triggers termination, preventing further LLM calls and signaling the agent loop.
    /// </summary>
    private void TriggerTermination(ErrorContext context, int errorCount)
    {
        // Format termination message using template
        var formattedMessage = TerminationMessageTemplate
            .Replace("{count}", errorCount.ToString())
            .Replace("{max}", MaxConsecutiveErrors.ToString());

        // Message for user visibility (with emoji)
        var userMessage = $"  {formattedMessage}";

        // Update state to signal termination
        context.UpdateState(s => s with
        {
            IsTerminated = true,
            TerminationReason = formattedMessage
        });

        // Emit StateSnapshotEvent so tests can verify error count
        // This captures the state AFTER error tracking has updated the count
        try
        {
            // MaxIterations comes from continuation permission (if extended) or default config
            // Extract both values using Analyze to ensure fresh reads
            var (maxIterations, completedFunctions) = context.Analyze(s => (
                s.MiddlewareState.ContinuationPermission()?.CurrentExtendedLimit ?? 50,
                s.CompletedFunctions.ToList()
            ));

            var snapshot = new StateSnapshotEvent(
                CurrentIteration: context.Iteration,
                MaxIterations: maxIterations,
                IsTerminated: true,
                TerminationReason: formattedMessage,
                ConsecutiveErrorCount: errorCount,
                CompletedFunctions: completedFunctions,
                AgentName: context.AgentName);

            context.Emit(snapshot);
        }
        catch (Exception ex)
        {
            // Emit failed - event coordinator may not be configured
            // This is non-fatal for termination logic
        }

        // Emit TextDeltaEvent for user visibility
        try
        {
            context.Emit(new TextDeltaEvent(userMessage, Guid.NewGuid().ToString()));
        }
        catch (InvalidOperationException)
        {
            // EventCoordinator not configured - event emission is optional
        }

        // Emit observability event
        try
        {
            context.Emit(new MaxConsecutiveErrorsExceededEvent(
                AgentName: context.AgentName,
                ConsecutiveErrors: errorCount,
                MaxConsecutiveErrors: MaxConsecutiveErrors,
                Iteration: context.Iteration,
                Timestamp: DateTimeOffset.UtcNow));
        }
        catch (InvalidOperationException)
        {
            // EventCoordinator not configured - event emission is optional
        }
    }
}

/// <summary>
/// Event emitted when max consecutive errors threshold is exceeded.
/// Used for observability and telemetry.
/// </summary>
public record MaxConsecutiveErrorsExceededEvent(
    string AgentName,
    int ConsecutiveErrors,
    int MaxConsecutiveErrors,
    int Iteration,
    DateTimeOffset Timestamp) : AgentEvent, IObservabilityEvent, IErrorEvent
{
    /// <inheritdoc />
    public string ErrorMessage => $"Maximum consecutive errors ({ConsecutiveErrors}/{MaxConsecutiveErrors}) exceeded";

    /// <inheritdoc />
    public Exception? Exception => null;
}
