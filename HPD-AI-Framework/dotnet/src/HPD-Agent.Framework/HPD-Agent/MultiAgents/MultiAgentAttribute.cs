using HPD.Agent;

namespace HPD.Agent;

/// <summary>
/// Marks a method as a multi-agent workflow capability.
/// The method must return AgentWorkflowInstance (sync) or Task&lt;AgentWorkflowInstance&gt; (async).
/// </summary>
/// <remarks>
/// Pattern: [MultiAgent("Description")] public AgentWorkflowInstance WorkflowName() => builder.Build();
/// The source generator will:
/// 1. Detect methods marked with [MultiAgent]
/// 2. Generate AIFunction wrapper that invokes the workflow
/// 3. Set up event bubbling to parent agent's EventCoordinator
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public class MultiAgentAttribute : Attribute
{
    /// <summary>
    /// Description shown to the LLM when this workflow is available.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Custom name for the workflow tool. Defaults to method name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Whether to stream events during execution. Default: true.
    /// </summary>
    public bool StreamEvents { get; set; } = true;

    /// <summary>
    /// Timeout for workflow execution in seconds. Default: 300 (5 min).
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Creates a new MultiAgent attribute with no description.
    /// </summary>
    public MultiAgentAttribute() { }

    /// <summary>
    /// Creates a new MultiAgent attribute with a description.
    /// </summary>
    /// <param name="description">Description shown to the LLM when this workflow is available.</param>
    public MultiAgentAttribute(string description) => Description = description;
}

/// <summary>
/// Marks a method as a multi-agent workflow with typed context for source generator detection.
/// Use this when you need context-aware dynamic descriptions or conditional evaluation.
/// </summary>
/// <typeparam name="TMetadata">The context type that implements IToolMetadata</typeparam>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class MultiAgentAttribute<TMetadata> : Attribute where TMetadata : IToolMetadata
{
    /// <summary>
    /// The context type used by this multi-agent for compile-time validation.
    /// </summary>
    public Type ContextType => typeof(TMetadata);

    /// <summary>
    /// Description shown to the LLM when this workflow is available.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Custom name for the workflow tool. Defaults to method name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Whether to stream events during execution. Default: true.
    /// </summary>
    public bool StreamEvents { get; set; } = true;

    /// <summary>
    /// Timeout for workflow execution in seconds. Default: 300 (5 min).
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Creates a new MultiAgent attribute with no description.
    /// </summary>
    public MultiAgentAttribute() { }

    /// <summary>
    /// Creates a new MultiAgent attribute with a description.
    /// </summary>
    /// <param name="description">Description shown to the LLM when this workflow is available.</param>
    public MultiAgentAttribute(string description) => Description = description;
}
