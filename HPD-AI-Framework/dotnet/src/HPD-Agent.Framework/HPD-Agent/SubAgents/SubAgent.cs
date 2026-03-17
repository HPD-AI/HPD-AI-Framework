using HPD.Agent;

namespace HPD.Agent;

/// <summary>
/// Represents a callable sub-agent - another agent that can be invoked as a tool/function.
/// Created via SubAgentFactory.Create() and processed by source generator.
/// Similar to Microsoft's AsAIFunction() but with compile-time validation.
/// </summary>
public class SubAgent
{
    /// <summary>
    /// Sub-agent name (REQUIRED - becomes AIFunction name shown to parent agent)
    /// </summary>
    public string Name { get; internal set; } = string.Empty;

    /// <summary>
    /// Description shown in tool list (REQUIRED - becomes AIFunction description).
    /// This is what the parent agent sees when browsing available sub-agent tools.
    /// Example: "Specialized weather forecasting agent"
    /// </summary>
    public string Description { get; internal set; } = string.Empty;

    /// <summary>
    /// Agent configuration - defines the sub-agent's behavior, provider, Toolkits, etc.
    /// This is used to build the actual Agent instance at runtime.
    /// </summary>
    public AgentConfig AgentConfig { get; internal set; } = null!;

    /// <summary>
    /// Thread handling strategy for sub-agent invocations.
    /// Determines how conversation context is managed across multiple calls.
    /// </summary>
    public SubAgentSessionMode SessionMode { get; internal set; } = SubAgentSessionMode.Stateless;

    /// <summary>
    /// Optional: Shared session ID for stateful multi-turn conversations.
    /// Only used when SessionMode = SharedSession.
    /// WARNING: Do not use shared sessions concurrently - can cause race conditions.
    /// </summary>
    public string? SharedSessionId { get; set; }

    /// <summary>
    /// Optional: Shared branch ID to use alongside SharedSessionId.
    /// Only used when SessionMode = SharedSession. Defaults to "main" if not set.
    /// </summary>
    public string? SharedBranchId { get; set; }

    /// <summary>
    /// Toolkit types to register with the sub-agent (e.g., typeof(FileSystemToolkit), typeof(WebSearchToolkit)).
    /// These Toolkits will be available as tools for the sub-agent to use.
    /// This is a runtime-only property and is not serializable (similar to Skills' References).
    /// </summary>
    public Type[] ToolkitTypes { get; set; } = Array.Empty<Type>();
}

/// <summary>
/// Defines how sub-agent threads are managed across invocations.
/// Mirrors Microsoft's AsAIFunction() thread handling patterns.
/// </summary>
public enum SubAgentSessionMode
{
    /// <summary>
    /// Stateless - New thread created per invocation (default).
    /// Each call is independent, no context preserved.
    /// Use when: Sub-agent calls are unrelated (e.g., "What's weather in NYC?" then "What's weather in SF?")
    /// </summary>
    Stateless,

    /// <summary>
    /// Stateful - Shared thread across all invocations.
    /// Agent remembers previous conversation context.
    /// Use when: Multi-turn conversations where context matters (e.g., "What's weather in SF?" then "How about tomorrow?")
    /// WARNING: Avoid concurrent usage - not thread-safe!
    /// </summary>
    SharedSession,

    /// <summary>
    /// Per-session - Inherits the parent agent's current session and branch as read-only context.
    /// The sub-agent sees the parent's conversation history but does not write back to it.
    /// Use when: The task benefits from full conversation context (e.g., summarization, analysis of what was said).
    /// Falls back to stateless if no parent session is available.
    /// </summary>
    PerSession
}
