using System.Text.Json;
using System.Text.Json.Nodes;

namespace HPD.Agent;

/// <summary>
/// Identifies how a durable subagent definition obtains its configuration.
/// </summary>
public abstract record SubAgentConfigurationSource;

/// <summary>
/// Uses the configuration supplied by the ToolHarness declaration.
/// </summary>
/// <param name="Config">The complete child agent configuration.</param>
public sealed record SuppliedAgentConfiguration(AgentConfig Config) : SubAgentConfigurationSource;

/// <summary>
/// Creates the child definition from an immutable serializable snapshot of the parent configuration.
/// </summary>
public sealed record ParentAgentConfiguration : SubAgentConfigurationSource;

/// <summary>
/// Resolves an existing definition from the configured <see cref="IAgentStore"/>.
/// </summary>
public sealed record StoredAgentConfiguration : SubAgentConfigurationSource;

/// <summary>
/// Controls the agent depths from which a particular subagent capability may be invoked.
/// </summary>
public sealed class SubAgentAvailability
{
    private SubAgentAvailability(int? maximumChildDepth)
    {
        MaximumChildDepth = maximumChildDepth;
    }

    /// <summary>
    /// Makes the subagent callable only by a root agent. Invoking it creates a depth-one child.
    /// </summary>
    public static SubAgentAvailability RootOnly { get; } = new(1);

    /// <summary>
    /// Makes the subagent callable at every depth permitted by <see cref="AgentConfig.MaxSubAgentDepth"/>.
    /// </summary>
    public static SubAgentAvailability AnyAllowedDepth { get; } = new(null);

    /// <summary>
    /// Gets the deepest child depth this capability may create, or <see langword="null"/>
    /// when only the agent-wide depth limit applies.
    /// </summary>
    public int? MaximumChildDepth { get; }

    /// <summary>
    /// Creates a policy that permits this capability to create children through the supplied depth.
    /// </summary>
    /// <param name="maximumChildDepth">The deepest child depth that may be created. Must be at least one.</param>
    public static SubAgentAvailability ThroughDepth(int maximumChildDepth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumChildDepth, 1);
        return maximumChildDepth == 1 ? RootOnly : new(maximumChildDepth);
    }

    /// <summary>
    /// Determines whether the capability may be invoked by an agent at <paramref name="currentAgentDepth"/>.
    /// </summary>
    public bool AllowsInvocationFrom(int currentAgentDepth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentAgentDepth);
        return MaximumChildDepth is null || currentAgentDepth < MaximumChildDepth.Value;
    }
}

/// <summary>
/// Represents a callable sub-agent - another agent that can be invoked as a tool/function.
/// </summary>
public sealed class SubAgent
{
    /// <summary>
    /// Sub-agent name (REQUIRED - becomes AIFunction name shown to parent agent).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Description shown in tool list (REQUIRED - becomes AIFunction description).
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Stable identity used to store, reconstruct, and route the child agent.
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// Configuration source for the durable child definition.
    /// </summary>
    public required SubAgentConfigurationSource Configuration { get; init; }

    /// <summary>
    /// Context policy for sub-agent execution.
    /// </summary>
    public SubAgentContextPolicy ContextPolicy { get; init; } = SubAgentContextPolicy.Fork;

    /// <summary>
    /// Controls which values the child run inherits from the invoking parent's
    /// <see cref="AgentRunConfig"/> and applies any child-only overrides.
    /// </summary>
    public SubAgentRunConfig RunConfig { get; init; } = SubAgentRunConfig.Inherit();

    /// <summary>
    /// Controls the depths at which this subagent appears as an invocable tool.
    /// Defaults to <see cref="SubAgentAvailability.RootOnly"/>, which prevents accidental recursive delegation.
    /// </summary>
    public SubAgentAvailability Availability { get; init; } = SubAgentAvailability.RootOnly;

    /// <summary>
    /// Optional compaction applied when parent history is forked.
    /// </summary>
    public ThreadForkCompaction? ForkCompaction { get; init; }

    /// <summary>
    /// Defines whether this subagent runs synchronously, in the background, or lets the model choose per call.
    /// </summary>
    public AgentInvocationModePolicy InvocationModePolicy { get; init; } =
        AgentInvocationModePolicy.SynchronousOnly;

    /// <summary>
    /// Rule used when this subagent is invoked as runtime-owned background work.
    /// </summary>
    public BackgroundTaskNotificationRule BackgroundNotification { get; init; } =
        new BackgroundTaskNotificationRule.OnFinalStateRule(Completed: true, Faulted: true);

    /// <summary>
    /// ToolHarness types to register with the sub-agent.
    /// </summary>
    public Type[] ToolHarnessTypes { get; init; } = Array.Empty<Type>();

    /// <summary>
    /// Optional thread metadata defaults applied to subagent-created threads.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Returns an equivalent subagent declaration with the supplied parent run-configuration inheritance.
    /// </summary>
    /// <param name="runConfig">The inheritance selection and child-only overrides.</param>
    /// <returns>A new subagent declaration.</returns>
    public SubAgent WithRunConfig(SubAgentRunConfig runConfig)
    {
        ArgumentNullException.ThrowIfNull(runConfig);
        return new SubAgent
        {
            Name = Name,
            Description = Description,
            AgentId = AgentId,
            Configuration = Configuration,
            ContextPolicy = ContextPolicy,
            RunConfig = runConfig,
            Availability = Availability,
            ForkCompaction = ForkCompaction,
            InvocationModePolicy = InvocationModePolicy,
            BackgroundNotification = BackgroundNotification,
            ToolHarnessTypes = ToolHarnessTypes,
            Metadata = Metadata
        };
    }

    /// <summary>
    /// Returns an equivalent declaration with the supplied depth availability.
    /// </summary>
    public SubAgent WithAvailability(SubAgentAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(availability);
        return new SubAgent
        {
            Name = Name,
            Description = Description,
            AgentId = AgentId,
            Configuration = Configuration,
            ContextPolicy = ContextPolicy,
            RunConfig = RunConfig,
            Availability = availability,
            ForkCompaction = ForkCompaction,
            InvocationModePolicy = InvocationModePolicy,
            BackgroundNotification = BackgroundNotification,
            ToolHarnessTypes = ToolHarnessTypes,
            Metadata = Metadata
        };
    }

    public static SubAgent FromConfig(
        string agentId,
        string name,
        string description,
        AgentConfig agentConfig,
        SubAgentContextPolicy contextPolicy = SubAgentContextPolicy.Fork,
        ThreadForkCompaction? forkCompaction = null,
        params Type[] toolharnessTypes)
        => FromConfig(
            agentId,
            name,
            description,
            agentConfig,
            contextPolicy,
            metadata: null,
            invocationModePolicy: AgentInvocationModePolicy.SynchronousOnly,
            backgroundNotification: null,
            forkCompaction,
            toolharnessTypes);

    public static SubAgent FromConfig(
        string agentId,
        string name,
        string description,
        AgentConfig agentConfig,
        SubAgentContextPolicy contextPolicy,
        Dictionary<string, object>? metadata,
        ThreadForkCompaction? forkCompaction = null,
        params Type[] toolharnessTypes)
        => FromConfig(
            agentId,
            name,
            description,
            agentConfig,
            contextPolicy,
            metadata,
            AgentInvocationModePolicy.SynchronousOnly,
            backgroundNotification: null,
            forkCompaction,
            toolharnessTypes);

    /// <summary>
    /// Creates an inline-config subagent definition.
    /// </summary>
    /// <param name="agentId">The stable stored identity of the child agent.</param>
    /// <param name="name">The model-facing subagent tool name.</param>
    /// <param name="description">The model-facing subagent tool description.</param>
    /// <param name="agentConfig">The child agent configuration.</param>
    /// <param name="contextPolicy">The child context policy.</param>
    /// <param name="metadata">Optional metadata applied to subagent-created threads.</param>
    /// <param name="invocationModePolicy">The allowed synchronous/background invocation policy.</param>
    /// <param name="backgroundNotification">The notification rule used for background invocations.</param>
    /// <param name="forkCompaction">Optional compaction applied when parent history is forked.</param>
    /// <param name="toolharnessTypes">Tool harness types registered on the child agent.</param>
    /// <returns>The subagent definition.</returns>
    public static SubAgent FromConfig(
        string agentId,
        string name,
        string description,
        AgentConfig agentConfig,
        SubAgentContextPolicy contextPolicy,
        Dictionary<string, object>? metadata,
        AgentInvocationModePolicy invocationModePolicy,
        BackgroundTaskNotificationRule? backgroundNotification,
        ThreadForkCompaction? forkCompaction = null,
        params Type[] toolharnessTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ValidateNameAndDescription(name, description);
        ArgumentNullException.ThrowIfNull(agentConfig);

        SubAgentContexts.Validate(contextPolicy, forkCompaction);

        return new SubAgent
        {
            AgentId = agentId,
            Name = name,
            Description = description,
            Configuration = new SuppliedAgentConfiguration(agentConfig),
            ContextPolicy = contextPolicy,
            ForkCompaction = forkCompaction,
            InvocationModePolicy = invocationModePolicy,
            BackgroundNotification = backgroundNotification
                ?? new BackgroundTaskNotificationRule.OnFinalStateRule(Completed: true, Faulted: true),
            ToolHarnessTypes = toolharnessTypes ?? Array.Empty<Type>(),
            Metadata = metadata
        };
    }

    public static SubAgent FromAgentId(
        string agentId,
        string name,
        string description,
        SubAgentContextPolicy contextPolicy = SubAgentContextPolicy.Fork,
        ThreadForkCompaction? forkCompaction = null,
        params Type[] toolharnessTypes)
        => FromAgentId(
            agentId,
            name,
            description,
            contextPolicy,
            metadata: null,
            invocationModePolicy: AgentInvocationModePolicy.SynchronousOnly,
            backgroundNotification: null,
            forkCompaction,
            toolharnessTypes);

    /// <summary>
    /// Creates a stored-agent subagent definition.
    /// </summary>
    /// <param name="agentId">The stored child agent id.</param>
    /// <param name="name">The model-facing subagent tool name.</param>
    /// <param name="description">The model-facing subagent tool description.</param>
    /// <param name="contextPolicy">The child context policy.</param>
    /// <param name="metadata">Optional metadata applied to subagent-created threads.</param>
    /// <param name="invocationModePolicy">The allowed synchronous/background invocation policy.</param>
    /// <param name="backgroundNotification">The notification rule used for background invocations.</param>
    /// <param name="forkCompaction">Optional compaction applied when parent history is forked.</param>
    /// <param name="toolharnessTypes">Tool harness types registered on the child agent.</param>
    /// <returns>The subagent definition.</returns>
    public static SubAgent FromAgentId(
        string agentId,
        string name,
        string description,
        SubAgentContextPolicy contextPolicy,
        Dictionary<string, object>? metadata,
        ThreadForkCompaction? forkCompaction = null,
        params Type[] toolharnessTypes)
        => FromAgentId(
            agentId,
            name,
            description,
            contextPolicy,
            metadata,
            AgentInvocationModePolicy.SynchronousOnly,
            backgroundNotification: null,
            forkCompaction,
            toolharnessTypes);

    public static SubAgent FromAgentId(
        string agentId,
        string name,
        string description,
        SubAgentContextPolicy contextPolicy,
        Dictionary<string, object>? metadata,
        AgentInvocationModePolicy invocationModePolicy,
        BackgroundTaskNotificationRule? backgroundNotification,
        ThreadForkCompaction? forkCompaction = null,
        params Type[] toolharnessTypes)
    {
        ValidateNameAndDescription(name, description);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        SubAgentContexts.Validate(contextPolicy, forkCompaction);

        return new SubAgent
        {
            AgentId = agentId,
            Name = name,
            Description = description,
            Configuration = new StoredAgentConfiguration(),
            ContextPolicy = contextPolicy,
            ForkCompaction = forkCompaction,
            InvocationModePolicy = invocationModePolicy,
            BackgroundNotification = backgroundNotification
                ?? new BackgroundTaskNotificationRule.OnFinalStateRule(Completed: true, Faulted: true),
            ToolHarnessTypes = toolharnessTypes ?? Array.Empty<Type>(),
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a durable subagent definition from a snapshot of the effective parent configuration.
    /// </summary>
    public static SubAgent FromParent(
        string agentId,
        string name,
        string description,
        SubAgentContextPolicy contextPolicy = SubAgentContextPolicy.Fork,
        Dictionary<string, object>? metadata = null,
        AgentInvocationModePolicy invocationModePolicy = AgentInvocationModePolicy.SynchronousOnly,
        BackgroundTaskNotificationRule? backgroundNotification = null,
        ThreadForkCompaction? forkCompaction = null,
        params Type[] toolharnessTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ValidateNameAndDescription(name, description);
        SubAgentContexts.Validate(contextPolicy, forkCompaction);

        return new SubAgent
        {
            AgentId = agentId,
            Name = name,
            Description = description,
            Configuration = new ParentAgentConfiguration(),
            ContextPolicy = contextPolicy,
            ForkCompaction = forkCompaction,
            InvocationModePolicy = invocationModePolicy,
            BackgroundNotification = backgroundNotification
                ?? new BackgroundTaskNotificationRule.OnFinalStateRule(Completed: true, Faulted: true),
            ToolHarnessTypes = toolharnessTypes ?? [],
            Metadata = metadata
        };
    }

    private static void ValidateNameAndDescription(string name, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
    }

}

/// <summary>
/// Defines how a subagent invocation initializes its durable context.
/// </summary>
public enum SubAgentContextPolicy
{
    /// <summary>
    /// Forks the parent thread and inherits its history.
    /// </summary>
    Fork,

    /// <summary>
    /// Creates an empty child thread in the parent session.
    /// </summary>
    Fresh,

    /// <summary>
    /// Creates an empty child thread in a new isolated session.
    /// </summary>
    Isolated,

    /// <summary>
    /// Lets the model choose <see cref="Fork"/> or <see cref="Fresh"/> for each call.
    /// </summary>
    ModelChoice
}

/// <summary>
/// Defines the model-requested context for one subagent invocation.
/// </summary>
public enum SubAgentContext
{
    /// <summary>Forks and inherits the parent thread history.</summary>
    Fork,

    /// <summary>Starts with only the delegated input.</summary>
    Fresh
}

/// <summary>
/// Resolves author policy and model-requested subagent context.
/// </summary>
public static class SubAgentContexts
{
    /// <summary>
    /// Reads the optional model-facing <c>context</c> argument.
    /// </summary>
    public static SubAgentContext? ReadRequestedContext(JsonElement json)
    {
        if (!json.TryGetProperty("context", out var property))
            return null;
        if (property.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("context must be either 'fork' or 'fresh'.");

        return property.GetString()?.ToLowerInvariant() switch
        {
            "fork" => SubAgentContext.Fork,
            "fresh" => SubAgentContext.Fresh,
            _ => throw new InvalidOperationException("context must be either 'fork' or 'fresh'.")
        };
    }

    /// <summary>
    /// Adds the model-facing <c>context</c> choice when the definition allows it.
    /// </summary>
    public static JsonElement CreateSchema(JsonElement originalSchema, SubAgentContextPolicy policy)
    {
        if (policy != SubAgentContextPolicy.ModelChoice)
            return originalSchema.Clone();

        var schema = JsonNode.Parse(originalSchema.GetRawText()) as JsonObject ?? new JsonObject();
        if (schema["properties"] is not JsonObject properties)
        {
            properties = new JsonObject();
            schema["properties"] = properties;
        }

        properties["context"] = new JsonObject
        {
            ["type"] = "string",
            ["enum"] = new JsonArray("fork", "fresh"),
            ["description"] = "Whether the child should inherit the current conversation or start fresh. Use fresh unless prior conversation is required."
        };
        return JsonSerializer.SerializeToElement(schema);
    }

    /// <summary>
    /// Resolves the effective context for an invocation.
    /// </summary>
    public static SubAgentContextPolicy Resolve(
        SubAgentContextPolicy policy,
        SubAgentContext? requestedContext) => policy switch
        {
            SubAgentContextPolicy.Fork when requestedContext == SubAgentContext.Fresh =>
                throw new InvalidOperationException("This subagent always forks parent context."),
            SubAgentContextPolicy.Fork => SubAgentContextPolicy.Fork,
            SubAgentContextPolicy.Fresh when requestedContext == SubAgentContext.Fork =>
                throw new InvalidOperationException("This subagent always starts with fresh context."),
            SubAgentContextPolicy.Fresh => SubAgentContextPolicy.Fresh,
            SubAgentContextPolicy.Isolated when requestedContext is not null =>
                throw new InvalidOperationException("This subagent always uses isolated context."),
            SubAgentContextPolicy.Isolated => SubAgentContextPolicy.Isolated,
            SubAgentContextPolicy.ModelChoice => requestedContext == SubAgentContext.Fork
                ? SubAgentContextPolicy.Fork
                : SubAgentContextPolicy.Fresh,
            _ => throw new ArgumentOutOfRangeException(nameof(policy))
        };

    internal static void Validate(SubAgentContextPolicy policy, ThreadForkCompaction? forkCompaction)
    {
        if (forkCompaction is not null && policy is SubAgentContextPolicy.Fresh or SubAgentContextPolicy.Isolated)
            throw new ArgumentException("Fork compaction requires Fork or ModelChoice context.");
    }
}
