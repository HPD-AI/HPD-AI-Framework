namespace HPD.Agent.Extensions;

/// <summary>
/// Extension methods for accessing agent-specific context from any event.
/// Enables cross-domain event consumption (e.g., Graph reading agent events).
/// </summary>
public static class EventExtensions
{
    /// <summary>
    /// Get agent execution context from any event.
    /// Checks AgentEvent.ExecutionContext first (strongly typed),
    /// then falls back to Extensions dictionary (cross-domain enrichment).
    /// </summary>
    /// <param name="evt">Event to extract context from</param>
    /// <returns>Agent execution context if available, null otherwise</returns>
    /// <example>
    /// <code>
    /// // In GraphOrchestrator
    /// await foreach (var evt in _coordinator.ReadAllAsync())
    /// {
    ///     var agentContext = evt.GetExecutionContext();
    ///     if (agentContext != null)
    ///     {
    ///         Console.WriteLine($"Agent: {agentContext.AgentName}");
    ///     }
    /// }
    /// </code>
    /// </example>
    public static AgentExecutionContext? GetExecutionContext(this HPD.Events.Event evt)
    {
        // Primary: strongly typed AgentEvent field (fast, type-safe)
        if (evt is AgentEvent agentEvt)
            return agentEvt.ExecutionContext;

        // Fallback: extension dictionary (for cross-domain scenarios)
        if (evt.Extensions?.TryGetValue("ExecutionContext", out var ctx) == true)
            return ctx as AgentExecutionContext;

        return null;
    }
}
