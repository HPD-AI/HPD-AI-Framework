using Microsoft.Extensions.AI;
using HPD.Agent.ClientTools;
using HPD.Events;
using HPD.Events.Struct;

namespace HPD.Agent.Middleware;

/// <summary>
/// Single unified context for the entire agent execution.
/// This is the core context object that flows through all middleware hooks.
/// </summary>
/// <remarks>
/// <para><b>Design Philosophy:</b></para>
/// <para>
/// AgentContext represents a single source of truth for agent execution.
/// Unlike the V1 architecture with separate turnContext and middlewareContext instances,
/// V2 uses a single context instance created at turn start and shared across all hooks.
/// </para>
/// <para><b>Key Improvements:</b></para>
/// <list type="bullet">
/// <item>  Single context instance - no manual synchronization needed</item>
/// <item>  Immediate state updates - updates visible to all subsequent hooks instantly</item>
/// <item>  Type-safe views - factory methods create typed contexts for each hook</item>
/// <item>  No scheduled updates - no awkward GetPendingState() pattern</item>
/// </list>
/// </remarks>
public sealed class AgentContext
{
    //
    // SHARED STATE (always synchronized, no manual sync needed)
    //

    private AgentLoopState _state;
    private readonly object _stateLock = new();
    private volatile bool _middlewareExecuting = false;
    private int _stateGeneration = 0;
    private readonly IEventCoordinator _events;
    private readonly IAgentEventPublisher? _threadEvents;
    private readonly IStructEventHub _structEvents;
    private readonly CancellationToken _cancellationToken;
    private readonly AgentChatClientHandle? _effectiveChatClient;
    private readonly AgentChatClientResolver? _chatClientResolver;
    private readonly AgentMetadata? _parentAgentMetadata;
    private readonly IAgentStore? _parentAgentStore;
    private readonly AgentConfig? _config;
    private readonly AgentClientSet? _clientSet;
    private readonly IContentStore? _contentStore;
    private readonly Session? _session;
    private readonly Thread? _thread;
    private readonly Func<AgentInputEvent, CancellationToken, ValueTask>? _inputHandler;
    private readonly IServiceProvider? _services;
    private readonly IRuntimeCapabilityRegistry _runtimeCapabilities;
    private readonly ToolHarnessExecutionScope? _toolHarnessExecutionScope;
    private readonly IReadOnlyDictionary<Type, object> _agentResources;

    //
    // INTERNAL ACCESS (for adapters)
    //

    /// <summary>
    /// Event coordinator (internal access for adapters).
    /// </summary>
    internal IEventCoordinator EventCoordinator => _events;
    internal IAgentEventPublisher? ThreadEvents => _threadEvents;

    internal IStructEventHub StructEvents => _structEvents;
    internal ToolHarnessPipelineRegistry? ToolHarnessPipelines => _toolHarnessExecutionScope?.Registry;
    internal ToolHarnessExecutionScope? ToolHarnessExecutionScope => _toolHarnessExecutionScope;
    internal string? CanonicalWorkspaceIdentity => _toolHarnessExecutionScope?.CanonicalWorkspaceIdentity;
    internal IReadOnlyDictionary<Type, object> AgentResources => _agentResources;

    /// <summary>
    /// Effective chat-client handle for this invocation.
    /// </summary>
    internal AgentChatClientHandle? EffectiveChatClient => _effectiveChatClient;
    internal AgentChatClientResolver? ChatClientResolver => _chatClientResolver;

    /// <summary>
    /// Parent agent's metadata (for SubAgent and MultiAgent event attribution).
    /// </summary>
    internal AgentMetadata? ParentAgentMetadata => _parentAgentMetadata;

    /// <summary>
    /// Parent agent's definition store (for stored-agent subagent resolution).
    /// </summary>
    internal IAgentStore? ParentAgentStore => _parentAgentStore;

    /// <summary>
    /// Agent configuration for middleware that needs agent-level client-family defaults.
    /// </summary>
    public AgentConfig? Config => _config;

    /// <summary>
    /// Provider-created client-family instances resolved for this agent build.
    /// </summary>
    internal AgentClientSet? ClientSet => _clientSet;

    /// <summary>
    /// Explicit content store configured for this agent.
    /// Content visibility is controlled by backend scope and metadata, not by the session store.
    /// </summary>
    public IContentStore? ContentStore => _contentStore;

    //
    // IDENTITY (immutable)
    //

    /// <summary>
    /// Name of the agent executing this operation.
    /// </summary>
    public string AgentName { get; }

    /// <summary>
    /// Unique identifier for this conversation/session.
    /// Used for scoping permissions, memory, and other per-conversation state.
    /// </summary>
    public string? ConversationId { get; }

    /// <summary>
    /// OTel-compatible trace ID (32 hex chars) shared across all events in this turn.
    /// Automatically stamped onto every event emitted via <see cref="Emit"/>.
    /// </summary>
    public string? TraceId { get; }

    /// <summary>The accepted thread execution that owns this middleware context.</summary>
    public string? ThreadExecutionId { get; }

    /// <summary>
    /// The session metadata container.
    /// May be null if no session was provided.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Session contains metadata and session-scoped middleware state (permissions, preferences).
    /// Messages live in <see cref="Thread"/> instead.
    /// Middleware should use <see cref="ContentStore"/> for uploads, artifacts, and other content.
    /// </para>
    /// <para><b>Example:</b></para>
    /// <code>
    /// public async Task BeforeIterationAsync(BeforeIterationContext context, ...)
    /// {
    ///     var contentStore = context.ContentStore;
    ///     if (contentStore != null)
    ///     {
    ///         // Upload/retrieve thread-scoped content, etc.
    ///     }
    /// }
    /// </code>
    /// </remarks>
    public Session? Session => _session;

    /// <summary>
    /// The current thread being executed.
    /// Contains conversation messages and thread-scoped middleware state.
    /// May be null if no session was provided.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Thread contains the conversation messages for this specific conversation path.
    /// Multiple threads can exist in one session (for exploring alternatives).
    /// </para>
    /// <para><b>Example:</b></para>
    /// <code>
    /// public async Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, ...)
    /// {
    ///     var messages = context.Thread?.Messages;
    ///     var threadId = context.Thread?.Id;
    /// }
    /// </code>
    /// </remarks>
    public Thread? Thread => _thread;

    /// <summary>
    /// Service provider for dependency injection (may be null if not configured).
    /// Use to access services like HttpClient, ILogger, IDistributedCache, etc.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Enables middleware to access registered services for HttpClient pooling,
    /// logging, caching, and other infrastructure concerns.
    /// </para>
    /// <para><b>Example - Audio Provider with HttpClient:</b></para>
    /// <code>
    /// var httpClient = context.Services?.GetService(typeof(HttpClient)) as HttpClient;
    /// var runtime = context.Services?.GetService(typeof(ProviderFamilyClientRuntime));
    /// // The family-neutral runtime constructs every provider family asynchronously.
    /// </code>
    /// </remarks>
    public IServiceProvider? Services => _services;

    /// <summary>
    /// Runtime-scoped capabilities published by middleware for tools/functions.
    /// </summary>
    public IRuntimeCapabilityRegistry RuntimeCapabilities => _runtimeCapabilities;

    //
    // STREAM MANAGEMENT (always available, may be null if not configured)
    //

    /// <summary>
    /// Stream registry for managing interruptible audio/streaming operations.
    /// Provides stream lifecycle management for audio and streaming operations.
    /// </summary>
    /// <remarks>
    /// Used by audio middleware for stream interruption and priority streaming.
    /// </remarks>
    public IEventFlowRegistry EventFlows => _events.EventFlows;

    //
    // STATE ACCESS (always available)
    //

    /// <summary>
    /// Current agent loop state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// State is immutable. To update, use <see cref="UpdateState"/>.
    /// </para>
    /// <para>
    /// Includes: ActiveSkillInstructions, CompletedFunctions, MiddlewareStates,
    /// ExpandedSkillContainers, expandedCollapsedToolHarnessContainers, etc.
    /// </para>
    /// </remarks>
    public AgentLoopState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Safely read state for conditional logic without risk of stale capture.
    /// </summary>
    /// <typeparam name="T">Return type of the analyzer function</typeparam>
    /// <param name="analyzer">Function that reads state and returns a value</param>
    /// <returns>The result of the analyzer function</returns>
    /// <remarks>
    /// This method provides the same safe state access pattern as HookContext.Analyze().
    /// Use this in tests or internal code where AgentContext is directly accessed.
    /// </remarks>
    public T Analyze<T>(Func<AgentLoopState, T> analyzer)
    {
        if (analyzer == null) throw new ArgumentNullException(nameof(analyzer));
        lock (_stateLock)
        {
            return analyzer(_state);
        }
    }

    /// <summary>
    /// Updates agent state immutably with defense-in-depth guards.
    /// </summary>
    /// <remarks>
    /// <para><b>RECOMMENDED PATTERN (async-safe):</b></para>
    /// <code>
    /// context.UpdateState(s =>
    /// {
    ///     var current = s.MiddlewareState.ErrorTracking ?? new();
    ///     var updated = current.IncrementFailures();
    ///     return s with
    ///     {
    ///         MiddlewareState = s.MiddlewareState.WithErrorTracking(updated)
    ///     };
    /// });
    /// </code>
    ///
    /// <para><b>COMPACT PATTERN (for simple transforms):</b></para>
    /// <code>
    /// context.UpdateState(s => s with { CurrentIteration = s.CurrentIteration + 1 });
    /// </code>
    ///
    /// <para><b>DANGEROUS (will throw at runtime):</b></para>
    /// <code>
    /// //   DANGEROUS: Reading state outside lambda
    /// var state = context.State.MiddlewareState.ErrorTracking ?? new();
    /// var updated = state.IncrementFailures();
    ///
    /// // If you add await here, state could become stale!
    /// await SomeAsyncWork();  // ← State might change via SyncState during this gap
    ///
    /// context.UpdateState(s => s with
    /// {
    ///     MiddlewareState = s.MiddlewareState.WithErrorTracking(updated)  // Uses stale 'updated'
    /// });
    /// // This WILL throw: "State was modified before UpdateState was called"
    /// // The generation counter detects that SyncState() was called between read and update
    /// </code>
    ///
    /// <para><b>Thread Safety:</b></para>
    /// <para>
    /// UpdateState is protected by two complementary mechanisms:
    /// 1. _middlewareExecuting flag - Prevents Agent.cs from calling SyncState() during middleware execution
    /// 2. State generation counter - Detects stale reads (state captured before async gap or SyncState call)
    /// </para>
    /// <para>
    /// The generation counter increments on every SyncState() and UpdateState() call. If the generation
    /// changed between capturing state and calling UpdateState, an exception is thrown. This catches:
    /// - Async gaps where middleware reads state, awaits, then updates with stale data
    /// - Background tasks that update state after middleware completes
    /// - Concurrent modifications during async operations
    /// </para>
    ///
    /// <para><b>CRITICAL: Updates are applied IMMEDIATELY</b></para>
    /// <para>
    /// Subsequent hooks see the updated state immediately. This is different from V1
    /// which scheduled updates for later. Middleware is responsible for validating
    /// updates before calling this method. There is no rollback mechanism.
    /// </para>
    /// </remarks>
    /// <param name="transform">Function that transforms the current state to new state</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if state was modified before UpdateState was called (generation counter mismatch).
    /// This indicates stale state was captured - use block-scoped lambda pattern instead.
    /// </exception>
    public void UpdateState(Func<AgentLoopState, AgentLoopState> transform)
    {
        if (transform == null) throw new ArgumentNullException(nameof(transform));

        lock (_stateLock)
        {
            // GUARD: Detect nested or out-of-band state modifications during transform execution.
            var generationBefore = _stateGeneration;
            var stateBefore = _state;
            var stateAfter = transform(stateBefore);

            if (_stateGeneration != generationBefore)
            {
                throw new InvalidOperationException(
                    "State was modified during UpdateState transform execution.\n\n" +
                    "This indicates a nested or out-of-band modification occurred while your transform was running.\n\n" +
                    "This is a critical state mutation bug - please report this with stack trace.\n" +
                    $"Expected generation: {generationBefore}, actual: {_stateGeneration}");
            }

            _state = stateAfter;
            _stateGeneration++;
            //   Updates visible to ALL subsequent hooks (same instance!)
            //   No scheduled updates - no awkward GetPendingState() needed
        }
    }

    /// <summary>
    /// Synchronizes the internal state with an external state object.
    /// Used by Agent.cs to sync state changes from the main loop.
    /// </summary>
    /// <param name="newState">The new state to synchronize</param>
    internal void SyncState(AgentLoopState newState)
    {
        // GUARD: Fail-fast on Agent.cs timing bugs
        if (_middlewareExecuting)
        {
            throw new InvalidOperationException(
                "CRITICAL BUG: SyncState() called during middleware execution.\n\n" +
                "SyncState() must ONLY be called BETWEEN middleware phases:\n" +
                "  ✓ After ExecuteBeforeIterationAsync() completes\n" +
                "  ✓ Before next middleware phase starts\n\n" +
                "This indicates a timing error in Agent.cs.\n" +
                $"Stack trace:\n{System.Environment.StackTrace}");
        }

        lock (_stateLock)
        {
            _state = newState ?? throw new ArgumentNullException(nameof(newState));
            _stateGeneration++;  // Increment generation to invalidate any captured state references
        }
    }

    /// <summary>
    /// Sets the middleware execution flag.
    /// Used by AgentMiddlewarePipeline to track when middleware is executing.
    /// </summary>
    /// <param name="executing">True if middleware is executing, false otherwise</param>
    internal void SetMiddlewareExecuting(bool executing)
    {
        _middlewareExecuting = executing;
    }

    public async ValueTask<AgentEvent> PublishAsync(
        AgentEvent evt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (TraceId is not null && evt.TraceId is null)
            evt = evt with { TraceId = TraceId };
        if (ThreadExecutionId is not null && evt.ThreadExecutionId is null)
            evt = evt with { ThreadExecutionId = ThreadExecutionId };

        if (_thread is not null)
        {
            evt = ThreadEventValidation.PrepareForAppend(_thread.SessionId, _thread.Id, evt);
            if (_threadEvents is not null)
            {
                return await _threadEvents.PublishAsync(
                    new ThreadKey(_thread.SessionId, _thread.Id),
                    evt,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return await PublishLiveAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<AgentEvent> PublishLiveAsync(
        AgentEvent evt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (_threadEvents is not null)
            return await _threadEvents.PublishLiveAsync(evt, cancellationToken).ConfigureAwait(false);
        var codec = _config?.EventComposition?.Codec
            ?? throw new InvalidOperationException("Agent context has no event composition authority.");
        if (!codec.TryGetByType(evt.GetType(), out _))
            throw new InvalidOperationException($"Agent event type '{evt.GetType().FullName}' is not present in codec '{codec.Digest}'.");
        var live = evt with { ThreadSequenceNumber = 0 };
        await _events.EmitAsync(live, cancellationToken).ConfigureAwait(false);
        return live;
    }

    /// <summary>
    /// Starts a request session and waits for its matching response.
    /// </summary>
    public async Task<TResponse> RequestAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan? timeout = null)
        where TRequest : AgentEvent, IAgentRequestEvent<TResponse>
        where TResponse : AgentEvent, IAgentResponseEvent
    {
        ArgumentNullException.ThrowIfNull(request);

        if (TraceId is not null && request.TraceId is null)
            request = (TRequest)(request with { TraceId = TraceId });
        if (ThreadExecutionId is not null && request.ThreadExecutionId is null)
            request = (TRequest)(request with { ThreadExecutionId = ThreadExecutionId });

        if (_thread is not null)
        {
            request = (TRequest)ThreadEventValidation.PrepareForAppend(
                _thread.SessionId,
                _thread.Id,
                request);
        }

        var handle = _events.RegisterRequest<TRequest, TResponse>(
            request,
            new HPD.Events.RequestOptions
            {
                Timeout = timeout,
                CancellationToken = _cancellationToken
            });

        var durableRequestPublished = false;
        try
        {
            if (_thread is not null && _threadEvents is not null)
            {
                await _threadEvents.CommitAndPublishAsync(
                    new ThreadKey(_thread.SessionId, _thread.Id),
                    request,
                    _cancellationToken).ConfigureAwait(false);
                durableRequestPublished = true;
            }
            else
            {
                await _events.EmitAsync(request, _cancellationToken).ConfigureAwait(false);
            }

            return (TResponse)await handle.Response.ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            if (durableRequestPublished)
                await CommitRequestTerminalAsync(request, AgentRequestTerminalKind.Expired, "The request deadline elapsed.").ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException)
        {
            if (durableRequestPublished)
                await CommitRequestTerminalAsync(request, AgentRequestTerminalKind.Cancelled, "The owning execution was cancelled.").ConfigureAwait(false);
            throw;
        }
        catch
        {
            handle.Cancel("Request publication or wait failed.");
            if (durableRequestPublished)
                await CommitRequestTerminalAsync(request, AgentRequestTerminalKind.Cancelled, "Request publication or wait failed.").ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask CommitRequestTerminalAsync(
        AgentEvent requestEvent,
        AgentRequestTerminalKind terminalKind,
        string reason)
    {
        if (_thread is null || _threadEvents is null || requestEvent is not IAgentRequestEvent request)
            return;

        await _threadEvents.CommitAndPublishAsync(
            new ThreadKey(_thread.SessionId, _thread.Id),
            new AgentRequestTerminatedEvent(
                request.RequestId,
                request.SourceName,
                terminalKind,
                reason,
                DateTimeOffset.UtcNow)
            {
                ThreadExecutionId = requestEvent.ThreadExecutionId,
                TraceId = requestEvent.TraceId
            },
            CancellationToken.None).ConfigureAwait(false);
    }

    public async ValueTask RunAsync(
        AgentInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (_inputHandler is not null)
        {
            await _inputHandler(input, cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException("This agent context does not support runtime input.");
    }

    //
    // CONSTRUCTOR (internal - created by Agent.cs)
    //

    /// <summary>
    /// Creates a new agent context for middleware execution.
    /// </summary>
    /// <param name="agentName">Name of the agent</param>
    /// <param name="conversationId">Unique identifier for the conversation</param>
    /// <param name="initialState">Initial agent loop state</param>
    /// <param name="eventCoordinator">Event coordinator for event emission</param>
    /// <param name="session">Session metadata (may be null)</param>
    /// <param name="thread">Current thread (may be null)</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <param name="effectiveChatClient">Effective invocation client and ownership handle.</param>
    /// <param name="services">Service provider for dependency injection (may be null)</param>
    /// <param name="traceId">OTel-compatible trace ID shared across all events in this turn.</param>
    internal AgentContext(
        string agentName,
        string? conversationId,
        AgentLoopState initialState,
        IEventCoordinator eventCoordinator,
        Session? session,
        Thread? thread,
        CancellationToken cancellationToken,
        IAgentEventPublisher? threadEvents = null,
        AgentChatClientHandle? effectiveChatClient = null,
        AgentChatClientResolver? chatClientResolver = null,
        IServiceProvider? services = null,
        IRuntimeCapabilityRegistry? runtimeCapabilities = null,
        string? traceId = null,
        string? threadExecutionId = null,
        string? agentId = null,
        AgentMetadata? parentAgentMetadata = null,
        IAgentStore? parentAgentStore = null,
        AgentConfig? config = null,
        AgentClientSet? clientSet = null,
        IContentStore? contentStore = null,
        IStructEventHub? structEvents = null,
        Func<AgentInputEvent, CancellationToken, ValueTask>? inputHandler = null,
        ToolHarnessExecutionScope? toolHarnessExecutionScope = null,
        IReadOnlyDictionary<Type, object>? agentResources = null)
    {
        AgentName = agentName ?? throw new ArgumentNullException(nameof(agentName));
        ConversationId = conversationId;
        TraceId = traceId;
        ThreadExecutionId = threadExecutionId;
        _config = config;
        _clientSet = clientSet;
        _contentStore = contentStore;
        _state = initialState ?? throw new ArgumentNullException(nameof(initialState));
        _events = eventCoordinator ?? throw new ArgumentNullException(nameof(eventCoordinator));
        _threadEvents = threadEvents;
        _structEvents = structEvents ?? new StructEventHub();
        _session = session;
        _thread = thread;
        _inputHandler = inputHandler;
        _cancellationToken = cancellationToken;
        _effectiveChatClient = effectiveChatClient;
        _chatClientResolver = chatClientResolver;
        _parentAgentMetadata = parentAgentMetadata ?? CreateRootAgentMetadata(agentName, agentId);
        _parentAgentStore = parentAgentStore;
        _services = services;
        _runtimeCapabilities = runtimeCapabilities ?? new RuntimeCapabilityRegistry();
        _toolHarnessExecutionScope = toolHarnessExecutionScope;
        _agentResources = agentResources ?? new Dictionary<Type, object>();
    }

    private static AgentMetadata? CreateRootAgentMetadata(string agentName, string? agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            return null;

        return new AgentMetadata
        {
            AgentName = agentName,
            AgentId = agentId,
            ParentAgentId = null,
            AgentChain = [agentName],
            Depth = 0
        };
    }

    //
    // FACTORY METHODS FOR TYPE-SAFE HOOK CONTEXTS
    //

    /// <summary>
    /// Creates a typed context for BeforeMessageTurn hook.
    /// </summary>
    internal BeforeMessageTurnContext AsBeforeMessageTurn(
        ChatMessage? userMessage,
        List<ChatMessage> conversationHistory,
        AgentRunConfig runConfig)
        => new(this, userMessage, conversationHistory, runConfig);

    /// <summary>
    /// Creates a typed context for AfterMessageTurn hook.
    /// </summary>
    internal AfterMessageTurnContext AsAfterMessageTurn(
        ChatResponse finalResponse,
        List<ChatMessage> turnHistory,
        AgentRunConfig runConfig)
        => new(this, finalResponse, turnHistory, runConfig);

    /// <summary>
    /// Creates a typed context for BeforeIteration hook.
    /// </summary>
    internal BeforeIterationContext AsBeforeIteration(
        int iteration,
        List<ChatMessage> messages,
        ChatOptions options,
        AgentRunConfig runConfig)
        => new(this, iteration, messages, options, runConfig);

    /// <summary>
    /// Creates a typed context for BeforeToolExecution hook.
    /// </summary>
    internal BeforeToolExecutionContext AsBeforeToolExecution(
        ChatMessage response,
        IReadOnlyList<FunctionCallContent> toolCalls,
        AgentRunConfig runConfig)
        => new(this, response, toolCalls, runConfig);

    /// <summary>
    /// Creates a typed context for AfterIteration hook.
    /// </summary>
    internal AfterIterationContext AsAfterIteration(
        int iteration,
        IReadOnlyList<FunctionResultContent> toolResults,
        AgentRunConfig runConfig)
        => new(this, iteration, toolResults, runConfig);

    /// <summary>
    /// Creates a typed context for BeforeParallelBatch hook.
    /// </summary>
    internal BeforeParallelBatchContext AsBeforeParallelBatch(
        IReadOnlyList<ParallelFunctionInfo> parallelFunctions,
        AgentRunConfig runConfig)
        => new(this, parallelFunctions, runConfig);

    /// <summary>
    /// Creates a typed context for BeforeFunction hook.
    /// </summary>
    internal BeforeFunctionContext AsBeforeFunction(
        AIFunction? function,
        string callId,
        IReadOnlyDictionary<string, object?> arguments,
        AgentRunConfig runConfig,
        string? toolharnessName = null,
        string? skillName = null,
        ToolInvocationInfo? invocation = null,
        IClientToolOperationRegistry? clientToolOperations = null,
        ResolvedFunctionInvocation? invocationMode = null)
        => new(
            this,
            function,
            callId,
            arguments,
            toolharnessName,
            skillName,
            runConfig,
            invocation,
            clientToolOperations,
            invocationMode);

    /// <summary>
    /// Creates a typed context for AfterFunction hook.
    /// </summary>
    internal AfterFunctionContext AsAfterFunction(
        AIFunction? function,
        string callId,
        object? result,
        Exception? exception,
        AgentRunConfig runConfig,
        string? toolharnessName = null,
        string? skillName = null,
        ToolInvocationInfo? invocation = null,
        ToolResultMetadata? resultMetadata = null,
        ResolvedFunctionInvocation? invocationMode = null)
        => new(this, function, callId, result, exception, runConfig, toolharnessName, skillName, invocation, resultMetadata, invocationMode);

    /// <summary>
    /// Creates a typed context for BeforeThreadForkCommit hook.
    /// </summary>
    internal BeforeThreadForkCommitContext AsBeforeThreadForkCommit(
        Thread sourceThread,
        Thread targetThread,
        int? forkedAtMessageIndex,
        string? forkedAtMessageId,
        ThreadForkOptions? forkOptions = null)
        => new(this, sourceThread, targetThread, forkedAtMessageIndex, forkedAtMessageId, forkOptions);

    /// <summary>
    /// Creates a typed context for OnError hook.
    /// </summary>
    internal ErrorContext AsError(
        Exception error,
        ErrorSource source,
        int iteration)
        => new(this, error, source, iteration);
}
