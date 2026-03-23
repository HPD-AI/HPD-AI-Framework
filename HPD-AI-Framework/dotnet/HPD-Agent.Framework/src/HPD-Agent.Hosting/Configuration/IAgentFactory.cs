using HPD.Agent;

namespace HPD.Agent.Hosting.Configuration;

/// <summary>
/// Factory interface for creating agents per-session.
/// Register in DI to override the default agent creation logic.
/// </summary>
/// <remarks>
/// Use this for advanced scenarios like:
/// - Multi-tenant (different API keys per tenant)
/// - Per-user model selection
/// - Dynamic toolkit loading based on session metadata
///
/// Resolution priority (highest to lowest):
/// 1. IAgentFactory from DI
/// 2. AgentConfig object (serializable — from JSON, DB, admin UI)
/// 3. AgentConfigPath file (serializable — loaded from disk)
/// 4. ConfigureAgent callback (runtime-only concerns)
/// 5. Empty builder (fallback)
/// </remarks>
public interface IAgentFactory
{
    /// <summary>
    /// Create an agent for the given session.
    /// </summary>
    /// <param name="sessionId">The session this agent will serve</param>
    /// <param name="store">The session store (pass to AgentBuilder.WithSessionStore)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A configured and built Agent instance</returns>
    Task<Agent> CreateAgentAsync(
        string sessionId,
        ISessionStore store,
        CancellationToken ct = default);
}
