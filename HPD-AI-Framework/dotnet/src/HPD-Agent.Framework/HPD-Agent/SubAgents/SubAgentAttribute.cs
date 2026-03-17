using HPD.Agent;

/// <summary>
/// Marks a method as a sub-agent with typed context for source generator detection.
/// Use this when you need context-aware dynamic descriptions or conditional evaluation.
/// </summary>
/// <typeparam name="TMetadata">The context type that implements IToolMetadata</typeparam>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class SubAgentAttribute<TMetadata> : Attribute where TMetadata : IToolMetadata
{
    /// <summary>
    /// The context type used by this sub-agent for compile-time validation.
    /// </summary>
    public Type ContextType => typeof(TMetadata);
}

/// <summary>
/// Marks a method as a sub-agent for source generator detection.
/// Sub-agents are callable agents that can be invoked as tools/functions by parent agents.
/// Use SubAgent&lt;TMetadata&gt; if you need context-aware features.
/// </summary>
/// <remarks>
/// Pattern: [SubAgent] public SubAgent MethodName() => SubAgentFactory.Create(...)
/// The source generator will:
/// 1. Detect methods marked with [SubAgent]
/// 2. Extract the AgentConfig from the method body
/// 3. Generate AIFunction wrapper that builds and invokes the sub-agent
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public class SubAgentAttribute : Attribute
{
}
