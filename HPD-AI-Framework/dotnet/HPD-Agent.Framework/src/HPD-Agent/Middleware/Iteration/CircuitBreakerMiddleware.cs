using System.Text.Json;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Prevents infinite loops by detecting repeated identical function calls.
/// Triggers circuit breaker when the same tool is called with identical arguments
/// more than <see cref="MaxConsecutiveCalls"/> times consecutively.
/// </summary>
/// <remarks>
/// <para><b>STATELESS MIDDLEWARE:</b></para>
/// <para>
/// This middleware is stateless - all state flows through the context via
/// <see cref="CircuitBreakerState"/>. This preserves Agent's thread-safety
/// guarantee for concurrent RunAsync() calls.
/// </para>
///
/// <para><b>Lifecycle:</b></para>
/// <para>
/// This middleware uses the <see cref="IAgentMiddleware.BeforeToolExecutionAsync"/> hook
/// which runs AFTER the LLM returns tool calls but BEFORE any tools execute.
/// This allows predictive checking: we calculate what the count WOULD BE if the tool executes.
/// </para>
///
/// <para><b>Key behavior:</b></para>
/// <list type="number">
/// <item>Computes function signature from tool name + serialized arguments</item>
/// <item>Compares against last signature for that tool (from CircuitBreakerState)</item>
/// <item>Calculates predicted count (current + 1 if identical, else 1)</item>
/// <item>Triggers if predicted count >= MaxConsecutiveCalls</item>
/// </list>
///
/// <para><b>When triggered:</b></para>
/// <list type="number">
/// <item>Sets <see cref="BeforeToolExecutionContext.SkipToolExecution"/> to true</item>
/// <item>Emits a <see cref="TextDeltaEvent"/> for user visibility</item>
/// <item>Emits a <see cref="CircuitBreakerTriggeredEvent"/> for observability</item>
/// <item>Signals termination via UpdateState</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // Register via AgentBuilder
/// var agent = new AgentBuilder()
///     .WithMiddleware(new CircuitBreakerMiddleware { MaxConsecutiveCalls = 3 })
///     .Build();
/// </code>
/// </example>
public class CircuitBreakerMiddleware : IAgentMiddleware
{
    //     
    // CONFIGURATION (not state - set at registration time)
    //     

    /// <summary>
    /// Maximum number of consecutive identical calls allowed before triggering.
    /// Default: 3 (matches typical agent configuration).
    /// </summary>
    public int MaxConsecutiveCalls { get; set; } = 3;

    /// <summary>
    /// Custom termination message template.
    /// Placeholders: {toolName}, {count}
    /// </summary>
    public string TerminationMessageTemplate { get; set; } =
        "  Circuit breaker triggered: Function '{toolName}' with same arguments would be called {count} times consecutively. " +
        "Stopping to prevent infinite loop.";

    //     
    // HOOKS
    //     

    /// <summary>
    /// Called AFTER LLM returns tool calls but BEFORE tools execute.
    /// Checks if executing these tools would exceed the threshold.
    /// Also updates state to track the tool calls (V2 fix).
    /// </summary>
    public Task BeforeToolExecutionAsync(
        BeforeToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        // No tool calls = nothing to check
        if (context.ToolCalls.Count == 0)
            return Task.CompletedTask;

        // Read state using Analyze for safety
        var cbState = context.Analyze(s =>
            s.MiddlewareState.CircuitBreaker() ?? new()
        );

        foreach (var toolCall in context.ToolCalls)
        {
            var toolName = toolCall.Name ?? "_unknown";
            var signature = ComputeFunctionSignature(toolCall);

            // Calculate what the count WOULD BE if we execute this tool
            var predictedCount = cbState.GetPredictedCount(toolName, signature);

            // Check if executing this tool would exceed the limit
            if (predictedCount >= MaxConsecutiveCalls)
            {
                TriggerCircuitBreaker(context, toolName, predictedCount);
                return Task.CompletedTask;
            }
        }

        // V2 FIX: Update state here since AfterIterationContext doesn't have ToolCalls
        // Record all tool calls that we're about to execute
        context.UpdateMiddlewareState<CircuitBreakerStateData>(currentState =>
        {
            var updatedState = currentState;

            foreach (var toolCall in context.ToolCalls)
            {
                var toolName = toolCall.Name ?? "_unknown";
                var signature = ComputeFunctionSignature(toolCall);
                updatedState = updatedState.RecordToolCall(toolName, signature);
            }

            return updatedState;
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Called AFTER tool execution completes.
    /// Updates circuit breaker state with executed tool signatures.
    /// </summary>
    /// <remarks>
    /// V2: AfterIterationContext only has ToolResults, not ToolCalls.
    /// Need to extract tool names from results. For now, this is a limitation -
    /// we may need to store tool calls in state during BeforeToolExecution.
    /// </remarks>
    public Task AfterIterationAsync(
        AfterIterationContext context,
        CancellationToken cancellationToken)
    {
        // V2 TODO: AfterIterationContext doesn't have ToolCalls
        // We need to refactor this to store state in BeforeToolExecutionAsync
        // For now, we can't update the circuit breaker state here
        return Task.CompletedTask;
    }

    //     
    // HELPERS
    //     

    /// <summary>
    /// Computes a deterministic signature for a function call.
    /// </summary>
    internal static string ComputeFunctionSignature(FunctionCallContent toolCall)
    {
        var name = toolCall.Name ?? "_unknown";

        // Serialize arguments to JSON for consistent comparison
        string argsJson;
        if (toolCall.Arguments == null || toolCall.Arguments.Count == 0)
        {
            argsJson = "{}";
        }
        else
        {
            try
            {
                // Sort keys for deterministic ordering
                var sortedArgs = toolCall.Arguments
                    .OrderBy(kvp => kvp.Key)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                argsJson = JsonSerializer.Serialize(sortedArgs);
            }
            catch
            {
                // Fallback if serialization fails
                argsJson = string.Join(",", toolCall.Arguments.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            }
        }

        return $"{name}({argsJson})";
    }

    /// <summary>
    /// Triggers the circuit breaker, preventing tool execution and terminating the loop.
    /// </summary>
    private void TriggerCircuitBreaker(BeforeToolExecutionContext context, string toolName, int count)
    {
        // Skip tool execution
        context.SkipToolExecution = true;

        // Format termination message
        var message = TerminationMessageTemplate
            .Replace("{toolName}", toolName)
            .Replace("{count}", count.ToString());

        // Provide final response (for LLM flow compatibility)
        context.OverrideResponse = new ChatMessage(
            ChatRole.Assistant,
            message);

        // Signal termination via state update
        context.UpdateState(s => s with
        {
            IsTerminated = true,
            TerminationReason = $"Circuit breaker: '{toolName}' with same arguments would be called {count} times consecutively"
        });

        // Emit TextDeltaEvent for user visibility
        try
        {
            context.Emit(new TextDeltaEvent(message, Guid.NewGuid().ToString()));
        }
        catch (InvalidOperationException)
        {
            // EventCoordinator not configured - event emission is optional
        }

        // Emit CircuitBreakerTriggeredEvent for observability
        try
        {
            context.Emit(new CircuitBreakerTriggeredEvent(
                AgentName: context.AgentName,
                FunctionName: toolName,
                ConsecutiveCount: count,
                Iteration: 0, // V2 TODO: BeforeToolExecutionContext doesn't have Iteration
                Timestamp: DateTimeOffset.UtcNow));
        }
        catch (InvalidOperationException)
        {
            // EventCoordinator not configured - event emission is optional
        }
    }
}
