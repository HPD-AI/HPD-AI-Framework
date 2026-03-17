using HPD.MultiAgent.Config;
using HPDAgent.Graph.Abstractions;
using HPDAgent.Graph.Abstractions.Graph;

namespace HPD.MultiAgent;

/// <summary>
/// Output mode for an agent node.
/// </summary>
public enum AgentOutputMode
{
    /// <summary>
    /// Buffer the agent's text response to a string.
    /// Outputs: { "answer": "&lt;full response&gt;" }
    /// </summary>
    String,

    /// <summary>
    /// Parse the agent's response to a structured type.
    /// Each property becomes a separate output key.
    /// </summary>
    Structured,

    /// <summary>
    /// Agent returns one of multiple union types for routing.
    /// Outputs: { "matched_type": "TypeName", "result": {...} }
    /// </summary>
    Union,

    /// <summary>
    /// Agent calls a handoff function to decide routing.
    /// Outputs: { "handoff_target": "targetNodeId" }
    /// </summary>
    Handoff
}

/// <summary>
/// Structured output mode (how the LLM is prompted).
/// </summary>
public enum StructuredOutputMode
{
    /// <summary>
    /// Native JSON schema in ResponseFormat (supports streaming partials).
    /// </summary>
    Native,

    /// <summary>
    /// Tool-based structured output (can mix with other tools).
    /// </summary>
    Tool
}

/// <summary>
/// Configuration options for an agent node in a multi-agent workflow.
/// </summary>
public sealed class AgentNodeOptions
{
    /// <summary>
    /// Output mode for this agent (default: String).
    /// </summary>
    public AgentOutputMode OutputMode { get; set; } = AgentOutputMode.String;

    /// <summary>
    /// Key used to look up input from upstream nodes.
    /// Default: null (auto-resolve from "question", "input", or first available string).
    /// </summary>
    public string? InputKey { get; set; }

    /// <summary>
    /// Key used for the primary output value.
    /// Default: "answer" for String mode, property names for Structured mode.
    /// </summary>
    public string? OutputKey { get; set; }

    /// <summary>
    /// Handlebars template for constructing input from multiple upstream values.
    /// When set, InputKey is ignored.
    /// </summary>
    public string? InputTemplate { get; set; }

    /// <summary>
    /// Additional system instructions appended to the agent's base instructions.
    /// </summary>
    public string? AdditionalSystemInstructions { get; set; }

    /// <summary>
    /// Per-invocation timeout for this agent.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Maximum concurrent executions of this node (for map operations).
    /// </summary>
    public int? MaxConcurrentExecutions { get; set; }

    /// <summary>
    /// Retry policy for this agent node.
    /// </summary>
    public RetryPolicy? RetryPolicy { get; set; }

    /// <summary>
    /// Context instances to inject into toolkits at runtime.
    /// Key: toolkit name, Value: context instance.
    /// </summary>
    public Dictionary<string, object>? ContextInstances { get; set; }

    // === Structured Output Configuration ===

    /// <summary>
    /// The structured output type (for Structured mode).
    /// </summary>
    public Type? StructuredType { get; set; }

    /// <summary>
    /// Union types for routing (for Union mode).
    /// </summary>
    public Type[]? UnionTypes { get; set; }

    /// <summary>
    /// Structured output mode (Native or Tool).
    /// Default: Native (supports streaming partials).
    /// </summary>
    public StructuredOutputMode StructuredOutputMode { get; set; } = StructuredOutputMode.Native;

    // === Handoff Configuration ===

    /// <summary>
    /// Handoff targets for Handoff mode.
    /// Key: target node ID, Value: description for the LLM.
    /// </summary>
    public Dictionary<string, string>? HandoffTargets { get; set; }

    // === Error Handling Configuration ===

    /// <summary>
    /// Error handling mode for this agent.
    /// Default: Stop (fail the workflow).
    /// </summary>
    public ErrorMode ErrorMode { get; set; } = ErrorMode.Stop;

    /// <summary>
    /// Fallback agent ID when ErrorMode is Fallback.
    /// </summary>
    public string? FallbackAgentId { get; set; }

    // === Approval Configuration ===

    /// <summary>
    /// Approval configuration for human-in-the-loop workflows.
    /// When set, the node will request approval after execution.
    /// </summary>
    public ApprovalConfig? Approval { get; set; }

    // === Fluent Configuration Methods ===

    /// <summary>
    /// Configure this agent for structured output of a single type.
    /// </summary>
    public AgentNodeOptions StructuredOutput<T>(StructuredOutputMode mode = StructuredOutputMode.Native)
    {
        OutputMode = AgentOutputMode.Structured;
        StructuredType = typeof(T);
        StructuredOutputMode = mode;
        return this;
    }

    /// <summary>
    /// Configure this agent for union output (multiple possible types).
    /// </summary>
    public AgentNodeOptions UnionOutput<T1, T2>(StructuredOutputMode mode = StructuredOutputMode.Native)
    {
        OutputMode = AgentOutputMode.Union;
        UnionTypes = new[] { typeof(T1), typeof(T2) };
        StructuredOutputMode = mode;
        return this;
    }

    /// <summary>
    /// Configure this agent for union output (multiple possible types).
    /// </summary>
    public AgentNodeOptions UnionOutput<T1, T2, T3>(StructuredOutputMode mode = StructuredOutputMode.Native)
    {
        OutputMode = AgentOutputMode.Union;
        UnionTypes = new[] { typeof(T1), typeof(T2), typeof(T3) };
        StructuredOutputMode = mode;
        return this;
    }

    /// <summary>
    /// Configure this agent for union output (multiple possible types).
    /// </summary>
    public AgentNodeOptions UnionOutput<T1, T2, T3, T4>(StructuredOutputMode mode = StructuredOutputMode.Native)
    {
        OutputMode = AgentOutputMode.Union;
        UnionTypes = new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4) };
        StructuredOutputMode = mode;
        return this;
    }

    /// <summary>
    /// Configure this agent for union output (multiple possible types).
    /// </summary>
    public AgentNodeOptions UnionOutput<T1, T2, T3, T4, T5>(StructuredOutputMode mode = StructuredOutputMode.Native)
    {
        OutputMode = AgentOutputMode.Union;
        UnionTypes = new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5) };
        StructuredOutputMode = mode;
        return this;
    }

    /// <summary>
    /// Configure retry policy with default exponential backoff.
    /// </summary>
    public AgentNodeOptions WithRetry(
        int maxAttempts,
        BackoffStrategy strategy = BackoffStrategy.Exponential)
    {
        RetryPolicy = new RetryPolicy
        {
            MaxAttempts = maxAttempts,
            InitialDelay = TimeSpan.FromSeconds(1),
            Strategy = strategy
        };
        return this;
    }

    /// <summary>
    /// Configure retry policy with full customization.
    /// </summary>
    public AgentNodeOptions WithRetry(RetryPolicy policy)
    {
        RetryPolicy = policy;
        return this;
    }

    /// <summary>
    /// Configure retry for transient failures only.
    /// </summary>
    public AgentNodeOptions WithRetryTransient(
        int maxAttempts,
        BackoffStrategy strategy = BackoffStrategy.Exponential)
    {
        RetryPolicy = new RetryPolicy
        {
            MaxAttempts = maxAttempts,
            InitialDelay = TimeSpan.FromSeconds(1),
            Strategy = strategy,
            RetryableExceptions = new[] { typeof(TimeoutException), typeof(HttpRequestException) }
        };
        return this;
    }

    /// <summary>
    /// Set per-invocation timeout.
    /// </summary>
    public AgentNodeOptions WithTimeout(TimeSpan timeout)
    {
        Timeout = timeout;
        return this;
    }

    /// <summary>
    /// Add context instance for a toolkit.
    /// </summary>
    public AgentNodeOptions WithContext<TContext>(string toolkitName, TContext context) where TContext : class
    {
        ContextInstances ??= new Dictionary<string, object>();
        ContextInstances[toolkitName] = context;
        return this;
    }

    /// <summary>
    /// Set additional system instructions.
    /// </summary>
    public AgentNodeOptions WithInstructions(string additionalInstructions)
    {
        AdditionalSystemInstructions = additionalInstructions;
        return this;
    }

    /// <summary>
    /// Set the input key to look for from upstream nodes.
    /// </summary>
    public AgentNodeOptions WithInputKey(string key)
    {
        InputKey = key;
        return this;
    }

    /// <summary>
    /// Set the output key for the agent's response.
    /// </summary>
    public AgentNodeOptions WithOutputKey(string key)
    {
        OutputKey = key;
        return this;
    }

    /// <summary>
    /// Set a Handlebars template for constructing input.
    /// </summary>
    public AgentNodeOptions WithInputTemplate(string template)
    {
        InputTemplate = template;
        return this;
    }

    // === Error Handling Fluent Methods ===

    /// <summary>
    /// Configure error handling to stop the workflow on failure.
    /// </summary>
    public AgentNodeOptions OnErrorStop()
    {
        ErrorMode = ErrorMode.Stop;
        return this;
    }

    /// <summary>
    /// Configure error handling to skip this agent and continue.
    /// </summary>
    public AgentNodeOptions OnErrorSkip()
    {
        ErrorMode = ErrorMode.Skip;
        return this;
    }

    /// <summary>
    /// Configure error handling to isolate the error and continue with partial data.
    /// </summary>
    public AgentNodeOptions OnErrorIsolate()
    {
        ErrorMode = ErrorMode.Isolate;
        return this;
    }

    /// <summary>
    /// Configure error handling to use a fallback agent.
    /// </summary>
    /// <param name="fallbackAgentId">The ID of the fallback agent to use.</param>
    public AgentNodeOptions OnErrorFallback(string fallbackAgentId)
    {
        if (string.IsNullOrWhiteSpace(fallbackAgentId))
            throw new ArgumentException("Fallback agent ID cannot be empty", nameof(fallbackAgentId));

        ErrorMode = ErrorMode.Fallback;
        FallbackAgentId = fallbackAgentId;
        return this;
    }

    // === Handoff Fluent Methods ===

    /// <summary>
    /// Add a handoff target for this agent.
    /// The agent will be able to call handoff_to_{targetId}() to route to the target.
    /// </summary>
    /// <param name="targetId">The node ID to hand off to.</param>
    /// <param name="description">Description of when to use this handoff (for the LLM).</param>
    public AgentNodeOptions WithHandoff(string targetId, string description)
    {
        if (string.IsNullOrWhiteSpace(targetId))
            throw new ArgumentException("Target ID cannot be empty", nameof(targetId));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description cannot be empty", nameof(description));

        OutputMode = AgentOutputMode.Handoff;
        HandoffTargets ??= new Dictionary<string, string>();
        HandoffTargets[targetId] = description;
        return this;
    }

    /// <summary>
    /// Add multiple handoff targets at once.
    /// </summary>
    public AgentNodeOptions WithHandoffs(params (string TargetId, string Description)[] handoffs)
    {
        if (handoffs == null || handoffs.Length == 0)
            throw new ArgumentException("At least one handoff target is required", nameof(handoffs));

        OutputMode = AgentOutputMode.Handoff;
        HandoffTargets ??= new Dictionary<string, string>();

        foreach (var (targetId, description) in handoffs)
        {
            HandoffTargets[targetId] = description;
        }

        return this;
    }

    // === Approval Fluent Methods ===

    /// <summary>
    /// Require approval before this agent's output is used.
    /// Always requires approval (unconditional).
    /// </summary>
    /// <param name="message">Message to display in the approval request.</param>
    /// <param name="timeout">How long to wait for approval. Default: 5 minutes.</param>
    public AgentNodeOptions RequiresApproval(
        string message = "Approval required",
        TimeSpan? timeout = null)
    {
        Approval = new ApprovalConfig
        {
            Condition = _ => true,
            Message = _ => message,
            Timeout = timeout ?? TimeSpan.FromMinutes(5)
        };
        return this;
    }

    /// <summary>
    /// Require approval when a condition is met.
    /// </summary>
    /// <param name="when">Condition that determines if approval is needed.</param>
    /// <param name="message">Message to display (can be dynamic based on context).</param>
    /// <param name="timeout">How long to wait for approval. Default: 5 minutes.</param>
    public AgentNodeOptions RequiresApproval(
        Func<ApprovalContext, bool> when,
        Func<ApprovalContext, string>? message = null,
        TimeSpan? timeout = null)
    {
        if (when == null)
            throw new ArgumentNullException(nameof(when));

        Approval = new ApprovalConfig
        {
            Condition = when,
            Message = message ?? (_ => "Approval required"),
            Timeout = timeout ?? TimeSpan.FromMinutes(5)
        };
        return this;
    }

    /// <summary>
    /// Require approval with full configuration.
    /// </summary>
    public AgentNodeOptions RequiresApproval(ApprovalConfig config)
    {
        Approval = config ?? throw new ArgumentNullException(nameof(config));
        return this;
    }

    /// <summary>
    /// Require approval when a specific output field equals a value.
    /// </summary>
    /// <param name="field">The output field to check.</param>
    /// <param name="value">The value that triggers approval.</param>
    /// <param name="message">Message to display. Default: "Approval required for {field}={value}".</param>
    public AgentNodeOptions RequiresApprovalWhen(
        string field,
        object value,
        string? message = null)
    {
        Approval = new ApprovalConfig
        {
            Condition = ctx => ctx.Outputs.TryGetValue(field, out var v) && Equals(v, value),
            Message = _ => message ?? $"Approval required: {field} = {value}",
            Timeout = TimeSpan.FromMinutes(5)
        };
        return this;
    }

    /// <summary>
    /// Require approval when a specific output field exists.
    /// </summary>
    /// <param name="field">The output field that triggers approval when present.</param>
    /// <param name="message">Message to display.</param>
    public AgentNodeOptions RequiresApprovalWhenExists(
        string field,
        string? message = null)
    {
        Approval = new ApprovalConfig
        {
            Condition = ctx => ctx.HasOutput(field),
            Message = _ => message ?? $"Approval required: {field} is present",
            Timeout = TimeSpan.FromMinutes(5)
        };
        return this;
    }
}
