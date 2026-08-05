using System.Collections.Immutable;
using HPD.Agent.ClientTools;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Middleware;

//
// TURN LEVEL CONTEXTS
//

/// <summary>
/// Context for BeforeMessageTurn hook.
/// Available properties: UserMessage, ThreadHistory, RunConfig
/// </summary>
public sealed class BeforeMessageTurnContext : HookContext
{
    /// <summary>
    /// The new user message that initiated this turn.
    /// </summary>
    /// <remarks>
    /// This can be <see langword="null"/> in continuation scenarios, such as resuming from a checkpoint
    /// without new user input. Middleware may replace this value when transforming the current input
    /// message, for example when converting uploaded content into provider-specific references. Replacing
    /// this value updates the turn-owned message lists used for model input and persistence.
    /// </remarks>
    public ChatMessage? UserMessage { get; set; }

    /// <summary>
    /// The active thread's model-visible message history for this turn.
    /// </summary>
    /// <remarks>
    /// This is a shared mutable list. Middleware can add, insert, remove, or reorder messages to shape
    /// what the model sees for the turn. Changes are visible to subsequent middleware and to the agent
    /// loop immediately. Use this for context injection, compaction, planning state, and other thread
    /// history transformations. Use <see cref="UserMessage"/> when transforming only the current input.
    /// </remarks>
    public List<ChatMessage> ThreadHistory { get; }

    /// <summary>
    /// Agent run options for this turn.
    ///   Always available (never NULL)
    /// Contains configuration like ClientToolInput, MaxIterations, etc.
    /// </summary>
    public AgentRunConfig RunConfig { get; }

    internal BeforeMessageTurnContext(
        AgentContext baseContext,
        ChatMessage? userMessage,
        List<ChatMessage> conversationHistory,
        AgentRunConfig runConfig)
        : base(baseContext)
    {
        UserMessage = userMessage; // Can be null in continuation scenarios
        ThreadHistory = conversationHistory ?? throw new ArgumentNullException(nameof(conversationHistory));
        RunConfig = runConfig ?? throw new ArgumentNullException(nameof(runConfig));
    }
}

/// <summary>
/// Context for AfterMessageTurn hook.
/// Available properties: FinalResponse, TurnHistory, RunConfig, TurnUsage
/// </summary>
public sealed class AfterMessageTurnContext : HookContext
{
    /// <summary>
    /// Final assistant response for this turn.
    ///   Always available (never NULL)
    /// </summary>
    public ChatResponse FinalResponse { get; }

    /// <summary>
    /// Messages that will be persisted to the thread after this turn completes.
    ///   Always available (never NULL)
    /// MUTABLE - middleware can filter/modify before persistence
    /// </summary>
    public List<ChatMessage> TurnHistory { get; }

    /// <summary>
    /// Original run options for this turn.
    ///   Always available (never NULL)
    /// READ-ONLY - represents the user's original intent for this run.
    /// Use for logging, metrics, and turn-level decisions based on user context.
    /// </summary>
    public AgentRunConfig RunConfig { get; }

    /// <summary>
    /// Token usage accumulated across all LLM iterations in this turn.
    /// Sums InputTokenCount, OutputTokenCount, CachedInputTokenCount, ReasoningTokenCount, etc.
    /// Null if the provider did not return usage data for any iteration.
    /// For per-iteration breakdown see <see cref="IterationUsage"/>.
    /// </summary>
    public UsageDetails? TurnUsage => State.AccumulatedUsage;

    /// <summary>
    /// Per-iteration token usage, one entry per LLM call in this turn.
    /// Index 0 = first LLM call, index 1 = after first tool round-trip, etc.
    /// Entries are null if the provider did not return usage for that iteration.
    /// </summary>
    public ImmutableList<UsageDetails?> IterationUsage => State.IterationUsage;

    internal AfterMessageTurnContext(
        AgentContext baseContext,
        ChatResponse finalResponse,
        List<ChatMessage> turnHistory,
        AgentRunConfig runConfig)
        : base(baseContext)
    {
        FinalResponse = finalResponse ?? throw new ArgumentNullException(nameof(finalResponse));
        TurnHistory = turnHistory ?? throw new ArgumentNullException(nameof(turnHistory));
        RunConfig = runConfig ?? throw new ArgumentNullException(nameof(runConfig));
    }
}

//
// ITERATION LEVEL CONTEXTS
//

/// <summary>
/// Context for BeforeIteration hook.
/// Available properties: Iteration, Messages, Options, RunConfig, PreviousIterationsUsage, PreviousIterationUsage
/// </summary>
public sealed class BeforeIterationContext : HookContext
{
    /// <summary>
    /// Current iteration number (0-based).
    ///   Always available
    /// </summary>
    public int Iteration { get; }

    /// <summary>
    /// Messages to send to the LLM for this iteration - shared mutable reference.
    ///   Always available (never NULL)
    /// MUTABLE - add context, modify history in-place.
    /// Changes are visible to Agent.cs LLM call immediately.
    /// </summary>
    public List<ChatMessage> Messages { get; }

    /// <summary>
    /// Chat options for this LLM call.
    ///   Always available (never NULL)
    /// MUTABLE - modify tools, instructions, temperature
    /// </summary>
    public ChatOptions Options { get; }

    /// <summary>
    /// Original run options for this turn.
    ///   Always available (never NULL)
    /// READ-ONLY - represents the user's original intent for this run.
    /// Use for iteration-specific decisions based on user preferences and context.
    /// Examples: Adapt temperature, filter tools, or access context properties for tenant/user info.
    /// </summary>
    public AgentRunConfig RunConfig { get; }

    //
    // CONTROL SIGNALS
    //

    /// <summary>
    /// Set to true to skip the LLM call.
    /// When skipping, populate OverrideResponse with the cached/computed response.
    /// </summary>
    public bool SkipLLMCall { get; set; }

    /// <summary>
    /// When SkipLLMCall is true, this provides the response to use instead.
    /// </summary>
    public ChatMessage? OverrideResponse { get; set; }

    //
    // HELPERS
    //

    /// <summary>
    /// True if this is the first iteration (before any tool calls).
    /// </summary>
    public bool IsFirstIteration => Iteration == 0;

    /// <summary>
    /// Token usage accumulated from all previous iterations in this turn.
    /// Useful for making cost-aware decisions before launching the next LLM call —
    /// e.g. trigger compaction or bail early if tokens are already high.
    /// Null on the first iteration (no previous LLM calls yet).
    /// </summary>
    public UsageDetails? PreviousIterationsUsage => State.AccumulatedUsage;

    /// <summary>
    /// Per-iteration token usage for all completed iterations so far.
    /// Empty on the first iteration. Indices align with iteration numbers.
    /// </summary>
    public ImmutableList<UsageDetails?> PreviousIterationUsage => State.IterationUsage;

    internal BeforeIterationContext(
        AgentContext baseContext,
        int iteration,
        List<ChatMessage> messages,
        ChatOptions options,
        AgentRunConfig runConfig)
        : base(baseContext)
    {
        Iteration = iteration;
        Messages = messages ?? throw new ArgumentNullException(nameof(messages));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        RunConfig = runConfig ?? throw new ArgumentNullException(nameof(runConfig));
    }
}

/// <summary>
/// Context for BeforeToolExecution hook.
/// Available properties: Response, ToolCalls, RunConfig
/// </summary>
public sealed class BeforeToolExecutionContext : HookContext
{
    /// <summary>
    /// LLM response for this iteration.
    ///   Always available (never NULL)
    /// </summary>
    public ChatMessage Response { get; }

    /// <summary>
    /// Tool calls requested by LLM in this iteration.
    ///   Always available (never NULL, but may be empty)
    /// </summary>
    public IReadOnlyList<FunctionCallContent> ToolCalls { get; }

    /// <summary>
    /// Original run options for this turn.
    ///   Always available (never NULL)
    /// READ-ONLY - represents the user's original intent for this run.
    /// Use for permission checks, disabled tool mode, and tool-level validation.
    /// </summary>
    public AgentRunConfig RunConfig { get; }

    //
    // CONTROL SIGNALS
    //

    /// <summary>
    /// Set to true to skip ALL pending tool executions.
    /// When skipping, set OverrideResponse with an appropriate message.
    /// </summary>
    public bool SkipToolExecution { get; set; }

    /// <summary>
    /// When SkipToolExecution is true, this provides the response to use instead.
    /// </summary>
    public ChatMessage? OverrideResponse { get; set; }

    internal BeforeToolExecutionContext(
        AgentContext baseContext,
        ChatMessage response,
        IReadOnlyList<FunctionCallContent> toolCalls,
        AgentRunConfig runConfig)
        : base(baseContext)
    {
        Response = response ?? throw new ArgumentNullException(nameof(response));
        ToolCalls = toolCalls ?? throw new ArgumentNullException(nameof(toolCalls));
        RunConfig = runConfig ?? throw new ArgumentNullException(nameof(runConfig));
    }
}

/// <summary>
/// Context for AfterIteration hook.
/// Available properties: Iteration, ToolResults, RunConfig
/// </summary>
public sealed class AfterIterationContext : HookContext
{
    /// <summary>
    /// Current iteration number (0-based).
    ///   Always available
    /// </summary>
    public int Iteration { get; }

    /// <summary>
    /// Results from tool execution.
    ///   Always available (never NULL, but may be empty)
    /// </summary>
    public IReadOnlyList<FunctionResultContent> ToolResults { get; }

    /// <summary>
    /// Original run options for this turn.
    ///   Always available (never NULL)
    /// READ-ONLY - represents the user's original intent for this run.
    /// Use for error tracking, metrics collection, and iteration-level logging with user context.
    /// </summary>
    public AgentRunConfig RunConfig { get; }

    //
    // HELPERS
    //

    /// <summary>
    /// True if all tool calls succeeded (no exceptions).
    /// </summary>
    public bool AllToolsSucceeded => ToolResults.All(r => r.Exception == null);

    /// <summary>
    /// True if any tool call failed (has exception).
    /// </summary>
    public bool AnyToolFailed => ToolResults.Any(r => r.Exception != null);

    internal AfterIterationContext(
        AgentContext baseContext,
        int iteration,
        IReadOnlyList<FunctionResultContent> toolResults,
        AgentRunConfig runConfig)
        : base(baseContext)
    {
        Iteration = iteration;
        ToolResults = toolResults ?? throw new ArgumentNullException(nameof(toolResults));
        RunConfig = runConfig ?? throw new ArgumentNullException(nameof(runConfig));
    }
}

//
// FUNCTION LEVEL CONTEXTS
//

/// <summary>
/// Context for BeforeParallelBatch hook.
/// Available properties: ParallelFunctions, RunConfig
/// </summary>
public sealed class BeforeParallelBatchContext : HookContext
{
    /// <summary>
    /// Information about functions being executed in parallel.
    ///   Always available (never NULL, always has at least 2 functions)
    /// </summary>
    public IReadOnlyList<ParallelFunctionInfo> ParallelFunctions { get; }

    /// <summary>
    /// Runtime-assigned identifier for this model-emitted tool-call batch.
    /// </summary>
    public string? BatchId => ParallelFunctions.Count > 0
        ? ParallelFunctions[0].Invocation?.BatchId
        : null;

    /// <summary>
    /// Original run options for this turn.
    ///   Always available (never NULL)
    /// READ-ONLY - represents the user's original intent for this run.
    /// Use for rate limiting, batch-level validation, and parallel execution control based on user tier/context.
    /// </summary>
    public AgentRunConfig RunConfig { get; }

    internal BeforeParallelBatchContext(
        AgentContext baseContext,
        IReadOnlyList<ParallelFunctionInfo> parallelFunctions,
        AgentRunConfig runConfig)
        : base(baseContext)
    {
        ParallelFunctions = parallelFunctions ?? throw new ArgumentNullException(nameof(parallelFunctions));
        RunConfig = runConfig ?? throw new ArgumentNullException(nameof(runConfig));
    }
}

/// <summary>
/// Context for BeforeFunction hook.
/// Available properties: Function, FunctionCallId, Arguments, ToolHarnessName, SkillName, RunConfig
/// </summary>
public sealed class BeforeFunctionContext : HookContext
{
    /// <summary>
    /// The function being invoked.
    ///   Can be NULL when LLM calls an unknown/unavailable function (unless TerminateOnUnknownCalls is enabled)
    /// </summary>
    public AIFunction? Function { get; }

    /// <summary>
    /// Unique call ID for this function invocation.
    ///   Always available (never NULL)
    /// </summary>
    public string FunctionCallId { get; }

    /// <summary>
    /// Runtime-assigned invocation metadata for this tool call, if it belongs to a batch.
    /// </summary>
    public ToolInvocationInfo? Invocation { get; }

    /// <summary>
    /// Model order of this tool call within its batch, if available.
    /// </summary>
    public int? ToolCallIndex => Invocation?.ToolCallIndex;

    /// <summary>
    /// Arguments passed to this function call.
    ///   Always available (never NULL, but may be empty)
    /// </summary>
    public IReadOnlyDictionary<string, object?> Arguments { get; }

    /// <summary>
    /// Name of the ToolHarness that contains this function, if any.
    /// May be NULL if function is not part of a ToolHarness.
    /// </summary>
    public string? ToolHarnessName { get; }

    /// <summary>
    /// Name of the skill that referenced this function, if any.
    /// May be NULL if function is not part of a skill.
    /// </summary>
    public string? SkillName { get; }

    /// <summary>
    /// Original run options for this turn.
    ///   Always available (never NULL)
    /// READ-ONLY - represents the user's original intent for this run.
    /// Use for permission validation, disabled tool mode, and function-level authorization.
    /// </summary>
    public AgentRunConfig RunConfig { get; }

    /// <summary>
    /// Runtime-owned background task registry available to function interception middleware.
    /// May be null when the function is not running inside an agent runtime.
    /// </summary>
    public IAgentBackgroundTaskRegistry? BackgroundTasks { get; }

    /// <summary>
    /// Runtime-owned background handle registry available to function interception middleware.
    /// May be null when the function is not running inside an agent runtime.
    /// </summary>
    public IAgentBackgroundHandleRegistry? BackgroundHandles { get; }

    /// <summary>
    /// Runtime-owned registry for client-owned background tool operations.
    /// May be null when the function is not running inside an agent runtime.
    /// </summary>
    public IClientToolBackgroundOperationRegistry? ClientToolBackgroundOperations { get; }

    //
    // CONTROL SIGNALS
    //

    /// <summary>
    /// Set to true to block THIS function from executing.
    /// The function will not run; OverrideResult will be used as the result.
    /// </summary>
    public bool BlockExecution { get; set; }

    /// <summary>
    /// When BlockExecution is true, this provides the result to use instead.
    /// </summary>
    public object? OverrideResult { get; set; }

    //
    // HELPERS
    //

    /// <summary>
    /// True if this function is part of a skill.
    /// </summary>
    public bool IsSkillFunction => SkillName != null;

    /// <summary>
    /// True if this function is part of a ToolHarness.
    /// </summary>
    public bool IsToolHarnessFunction => ToolHarnessName != null;

    internal BeforeFunctionContext(
        AgentContext baseContext,
        AIFunction? function,
        string callId,
        IReadOnlyDictionary<string, object?> arguments,
        string? toolharnessName,
        string? skillName,
        AgentRunConfig runConfig,
        ToolInvocationInfo? invocation = null,
        IAgentBackgroundTaskRegistry? backgroundTasks = null,
        IAgentBackgroundHandleRegistry? backgroundHandles = null,
        IClientToolBackgroundOperationRegistry? clientToolBackgroundOperations = null)
        : base(baseContext)
    {
        Function = function; // Can be null for unknown functions
        FunctionCallId = callId ?? throw new ArgumentNullException(nameof(callId));
        Invocation = invocation;
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        ToolHarnessName = toolharnessName;
        SkillName = skillName;
        RunConfig = runConfig ?? throw new ArgumentNullException(nameof(runConfig));
        BackgroundTasks = backgroundTasks;
        BackgroundHandles = backgroundHandles;
        ClientToolBackgroundOperations = clientToolBackgroundOperations;
    }
}

/// <summary>
/// Context for AfterFunction hook.
/// Available properties: Function, FunctionCallId, Result, Exception, ToolHarnessName, SkillName, RunConfig
/// </summary>
public sealed class AfterFunctionContext : HookContext
{
    /// <summary>
    /// The function that was invoked.
    ///   Can be NULL when an unknown function was called
    /// </summary>
    public AIFunction? Function { get; }

    /// <summary>
    /// Unique call ID for this function invocation.
    ///   Always available (never NULL)
    /// </summary>
    public string FunctionCallId { get; }

    /// <summary>
    /// Runtime-assigned invocation metadata for this tool call, if it belongs to a batch.
    /// </summary>
    public ToolInvocationInfo? Invocation { get; }

    /// <summary>
    /// Model order of this tool call within its batch, if available.
    /// </summary>
    public int? ToolCallIndex => Invocation?.ToolCallIndex;

    /// <summary>
    /// Original result returned by the function body before AfterFunction middleware transformations.
    /// </summary>
    public object? OriginalResult { get; }

    /// <summary>
    /// Result of the function execution as it will be sent back to the model.
    /// NULL if function threw an exception.
    /// MUTABLE - middleware can transform the model-facing result.
    /// </summary>
    public object? Result { get; set; }

    /// <summary>
    /// Per-call structured metadata recorded by the function body or wrapping middleware.
    /// This is not the function result and is intended for state commits, events, and diagnostics.
    /// </summary>
    public ToolResultMetadata ResultMetadata { get; }

    /// <summary>
    /// Exception from function execution (if failed).
    /// NULL if function succeeded.
    /// MUTABLE - middleware can transform/wrap exceptions
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// Name of the ToolHarness that contains this function, if any.
    /// May be NULL if function is not part of a ToolHarness.
    /// </summary>
    public string? ToolHarnessName { get; }

    /// <summary>
    /// Name of the skill that referenced this function, if any.
    /// May be NULL if function is not part of a skill.
    /// </summary>
    public string? SkillName { get; }

    /// <summary>
    /// Original run options for this turn.
    ///   Always available (never NULL)
    /// READ-ONLY - represents the user's original intent for this run.
    /// Use for audit logging, metrics, and result transformation based on user context.
    /// </summary>
    public AgentRunConfig RunConfig { get; }

    //
    // HELPERS
    //

    /// <summary>
    /// True if the function succeeded (no exception).
    /// </summary>
    public bool IsSuccess => Exception == null;

    /// <summary>
    /// True if the function failed (has exception).
    /// </summary>
    public bool IsFailure => Exception != null;

    /// <summary>
    /// True if this function is part of a skill.
    /// </summary>
    public bool IsSkillFunction => SkillName != null;

    /// <summary>
    /// True if this function is part of a ToolHarness.
    /// </summary>
    public bool IsToolHarnessFunction => ToolHarnessName != null;

    internal AfterFunctionContext(
        AgentContext baseContext,
        AIFunction? function,
        string callId,
        object? result,
        Exception? exception,
        AgentRunConfig runConfig,
        string? toolharnessName = null,
        string? skillName = null,
        ToolInvocationInfo? invocation = null,
        ToolResultMetadata? resultMetadata = null)
        : base(baseContext)
    {
        Function = function; // Can be null for unknown functions
        FunctionCallId = callId ?? throw new ArgumentNullException(nameof(callId));
        Invocation = invocation;
        OriginalResult = result;
        Result = result;
        Exception = exception;
        ResultMetadata = resultMetadata ?? new ToolResultMetadata();
        ToolHarnessName = toolharnessName;
        SkillName = skillName;
        RunConfig = runConfig ?? throw new ArgumentNullException(nameof(runConfig));
    }
}

//
// THREAD LIFECYCLE CONTEXTS
//

/// <summary>
/// Context for the BeforeThreadForkCommit hook.
/// Available properties: SourceThread, TargetThread, ForkedAtMessageIndex, ForkedAtMessageId, ForkOptions.
/// </summary>
public sealed class BeforeThreadForkCommitContext : HookContext
{
    /// <summary>
    /// Thread being forked from.
    /// </summary>
    public Thread SourceThread { get; }

    /// <summary>
    /// New thread being created. This thread has not been persisted yet.
    /// </summary>
    public Thread TargetThread { get; }

    /// <summary>
    /// Resolved source message index where the fork occurs. The fork includes this message.
    /// Null means the fork starts from the root before any source messages.
    /// </summary>
    public int? ForkedAtMessageIndex { get; }

    /// <summary>
    /// Stable id of the source message at the fork point, if available.
    /// </summary>
    public string? ForkedAtMessageId { get; }

    /// <summary>
    /// Typed options used to create the fork.
    /// </summary>
    public ThreadForkOptions ForkOptions { get; }

    /// <summary>
    /// Complete canonical event sequence for the new target journal when middleware has
    /// deliberately reshaped the target. Null preserves the ordinary source-event copy path.
    /// </summary>
    public IReadOnlyList<AgentEvent>? TargetJournalEvents { get; set; }

    internal BeforeThreadForkCommitContext(
        AgentContext baseContext,
        Thread sourceThread,
        Thread targetThread,
        int? forkedAtMessageIndex,
        string? forkedAtMessageId,
        ThreadForkOptions? forkOptions = null)
        : base(baseContext)
    {
        SourceThread = sourceThread ?? throw new ArgumentNullException(nameof(sourceThread));
        TargetThread = targetThread ?? throw new ArgumentNullException(nameof(targetThread));
        ForkedAtMessageIndex = forkedAtMessageIndex;
        ForkedAtMessageId = forkedAtMessageId;
        ForkOptions = forkOptions ?? ThreadForkOptions.Default;
    }
}

//
// HELPER TYPES
//

/// <summary>
/// Runtime-assigned metadata for a model-emitted tool/function invocation.
/// </summary>
public sealed record ToolInvocationInfo(
    string BatchId,
    string CallId,
    string? FunctionName,
    int ToolCallIndex);

/// <summary>
/// Mutable, per-call metadata side channel for function results.
/// </summary>
public sealed class ToolResultMetadata
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, object?> Values => _values;

    public void Set<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _values[key] = value;
    }

    public bool TryGet<T>(string key, out T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (_values.TryGetValue(key, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }
}

/// <summary>
/// Information about a function being executed in parallel.
/// </summary>
public sealed record ParallelFunctionInfo(
    AIFunction Function,
    string CallId,
    IReadOnlyDictionary<string, object?> Arguments,
    ToolInvocationInfo? Invocation = null)
{
    /// <summary>
    /// Name of the function being called.
    /// </summary>
    public string FunctionName => Function.Name;
}
