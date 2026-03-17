using System.ComponentModel;
using HPD.Events;

namespace HPD.Agent.Middleware;

/// <summary>
/// Abstract base class for all typed hook contexts.
/// Provides shared functionality: state access, event emission, identity properties.
/// </summary>
/// <remarks>
/// <para><b>Design Philosophy:</b></para>
/// <para>
/// Hook contexts are lightweight wrappers around <see cref="AgentContext"/> that expose
/// only the properties relevant to each specific hook. This provides compile-time safety
/// by preventing access to properties that aren't available in a given hook.
/// </para>
/// <para><b>Key Benefits:</b></para>
/// <list type="bullet">
/// <item>  Compile-time safety - only valid properties exposed</item>
/// <item>  Better IDE autocomplete - no NULL-able properties to check</item>
/// <item>  Clear documentation - hook signature shows exactly what's available</item>
/// <item>  Easier testing - mock only the properties that matter for each hook</item>
/// </list>
/// </remarks>
public abstract class HookContext
{
    /// <summary>
    /// The underlying agent context (shared across all hooks).
    /// </summary>
    internal AgentContext Base { get; }

    //
    // ALWAYS AVAILABLE (forwarded for convenience)
    //

    /// <summary>
    /// Name of the agent executing this operation.
    /// </summary>
    public string AgentName => Base.AgentName;

    /// <summary>
    /// Unique identifier for this conversation/session.
    /// </summary>
    public string? ConversationId => Base.ConversationId;

    /// <summary>
    /// The session metadata container.
    /// Access session.Store for infrastructure operations (asset upload, etc.).
    /// Does NOT contain messages - messages are in <see cref="Branch"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Example - Content Upload:</b></para>
    /// <code>
    /// public async Task BeforeIterationAsync(BeforeIterationContext context, ...)
    /// {
    ///     var contentStore = context.Session?.Store?.GetContentStore(context.Session.Id);
    ///     if (contentStore != null)
    ///     {
    ///         var id = await contentStore.PutAsync(context.Session.Id, bytes, "image/jpeg");
    ///     }
    /// }
    /// </code>
    /// </remarks>
    public Session? Session => Base.Session;

    /// <summary>
    /// The current branch being executed.
    /// Contains conversation messages and branch-scoped middleware state.
    /// </summary>
    /// <remarks>
    /// <para><b>Example - Access Messages:</b></para>
    /// <code>
    /// public async Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, ...)
    /// {
    ///     var messages = context.Branch?.Messages;
    ///     var branchId = context.BranchId;
    /// }
    /// </code>
    /// </remarks>
    public Branch? Branch => Base.Branch;

    /// <summary>
    /// Convenience property for getting the session ID.
    /// </summary>
    public string? SessionId => Session?.Id;

    /// <summary>
    /// Convenience property for getting the branch ID.
    /// </summary>
    public string? BranchId => Branch?.Id;

    /// <summary>
    /// Service provider for dependency injection (may be null if not configured).
    /// Use to access services like HttpClient, ILogger, IDistributedCache, etc.
    /// </summary>
    /// <remarks>
    /// <para><b>Example - HttpClient for Audio Provider:</b></para>
    /// <code>
    /// var httpClient = context.Services?.GetService(typeof(HttpClient)) as HttpClient;
    /// var ttsClient = new OpenAITextToSpeechClient(config, httpClient);
    /// </code>
    /// <para><b>Example - Logging:</b></para>
    /// <code>
    /// var logger = context.Services?.GetService(typeof(ILogger&lt;MyMiddleware&gt;)) as ILogger;
    /// logger?.LogInformation("Processing audio...");
    /// </code>
    /// </remarks>
    public IServiceProvider? Services => Base.Services;

    /// <summary>
    /// Current agent loop state. Reflects any updates from earlier middlewares.
    /// Internal access only - use <see cref="Analyze{T}"/> or read inside <see cref="UpdateState"/> for safe state access.
    /// </summary>
    /// <remarks>
    /// <para><b>Why is this internal?</b></para>
    /// <para>
    /// Direct state access was made internal in V2 to prevent the "stale state capture" footgun.
    /// Runtime detection of stale captures is fundamentally impossible in C# (see ThreadSafetyTests.cs),
    /// so we enforce safe patterns at compile-time instead.
    /// </para>
    ///
    /// <para><b>The Footgun (prevented by making this internal):</b></para>
    /// <code>
    /// //   NO LONGER COMPILES (State is internal)
    /// var errorState = context.State.MiddlewareState.ErrorTracking;
    /// await Work();  // State might change during this!
    /// context.UpdateState(s => s with {
    ///     MiddlewareState = s.MiddlewareState.WithErrorTracking(errorState)  // STALE!
    /// });
    /// </code>
    ///
    /// <para><b>Safe alternatives:</b></para>
    /// <code>
    /// //   Use Analyze() for conditionals
    /// var shouldStop = context.Analyze(s => s.ErrorCount >= 3);
    /// if (shouldStop) {
    ///     context.UpdateState(s => s with { IsTerminated = true });
    /// }
    ///
    /// //   Or read inside UpdateState for mutations
    /// context.UpdateState(s => {
    ///     var current = s.MiddlewareState.ErrorTracking ?? new();
    ///     return s with { /* ... */ };
    /// });
    /// </code>
    /// </remarks>
    internal AgentLoopState State => Base.State;

    /// <summary>
    /// Safely read state for conditional logic without risk of stale capture.
    /// </summary>
    /// <typeparam name="T">Return type of the analyzer function</typeparam>
    /// <param name="analyzer">Function that reads state and returns a value</param>
    /// <returns>The result of the analyzer function</returns>
    /// <remarks>
    /// <para><b>Use for conditionals:</b></para>
    /// <code>
    /// var shouldStop = context.Analyze(s =>
    ///     s.MiddlewareState.ErrorTracking?.ConsecutiveFailures >= 3
    /// );
    /// if (shouldStop) {
    ///     context.UpdateState(s => s with { IsTerminated = true });
    /// }
    /// </code>
    ///
    /// <para><b>Extract multiple values:</b></para>
    /// <code>
    /// var (errors, iteration) = context.Analyze(s => (
    ///     s.MiddlewareState.ErrorTracking?.ConsecutiveFailures ?? 0,
    ///     s.Iteration
    /// ));
    /// </code>
    ///
    /// <para><b>Why use Analyze()?</b></para>
    /// <para>
    /// The lambda pattern makes state reads explicit and prevents accidental stale
    /// captures. Even if you add 'await' later, the lambda executes at call time
    /// and gets fresh state.
    /// </para>
    ///
    /// <para><b>Performance:</b></para>
    /// <para>Zero overhead - the lambda is inlined by the JIT compiler.</para>
    ///
    /// <para><b>Best Practices:</b></para>
    /// <code>
    /// //   DO: Use Analyze() for extracting values before async operations
    /// var count = context.Analyze(s => s.ErrorCount);
    /// await LogAsync();
    /// if (count >= 3) { /* ... */ }
    ///
    /// //   DON'T: Extract values for later mutation (will be stale)
    /// var count = context.Analyze(s => s.ErrorCount);
    /// await Work();
    /// context.UpdateState(s => s with { ErrorCount = count + 1 });  // Uses old count!
    ///
    /// //   DO: Read inside UpdateState for mutations
    /// await Work();
    /// context.UpdateState(s => s with { ErrorCount = s.ErrorCount + 1 });  // Fresh read!
    /// </code>
    ///
    /// <para><b>TIP:</b> For simple middleware state reads, consider using
    /// <see cref="MiddlewareStateExtensions.GetMiddlewareState{TState}(HookContext)"/> instead.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if analyzer is null</exception>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public T Analyze<T>(Func<AgentLoopState, T> analyzer)
    {
        if (analyzer == null) throw new ArgumentNullException(nameof(analyzer));
        return analyzer(Base.State);
    }

    /// <summary>
    /// Updates agent state immutably.
    ///   CRITICAL: Updates are applied IMMEDIATELY - subsequent hooks see the updated state.
    /// </summary>
    /// <param name="transform">Function that transforms the current state to new state</param>
    /// <remarks>
    /// <para><b>TIP:</b> For simple middleware state updates, consider using
    /// <see cref="MiddlewareStateExtensions.UpdateMiddlewareState{TState}(HookContext, Func{TState, TState})"/> instead.</para>
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void UpdateState(Func<AgentLoopState, AgentLoopState> transform)
        => Base.UpdateState(transform);

    /// <summary>
    /// Emits an event to the agent's event stream for external handling.
    /// </summary>
    /// <param name="evt">The event to emit</param>
    /// <exception cref="InvalidOperationException">Thrown if EventCoordinator is not configured</exception>
    public void Emit(AgentEvent evt)
        => Base.Emit(evt);

    /// <summary>
    /// Attempts to emit an event to the agent's event stream.
    /// Silently does nothing if EventCoordinator is not configured.
    /// </summary>
    /// <param name="evt">The event to emit</param>
    /// <returns>True if event was emitted, false if EventCoordinator not configured</returns>
    public bool TryEmit(AgentEvent evt)
    {
        try
        {
            Base.Emit(evt);
            return true;
        }
        catch (InvalidOperationException)
        {
            // EventCoordinator not configured
            return false;
        }
    }

    /// <summary>
    /// Waits for a response event from external handlers.
    /// Used for interactive patterns: permissions, clarifications, approvals.
    /// </summary>
    /// <typeparam name="T">Type of response event expected</typeparam>
    /// <param name="requestId">Unique identifier for this request (must match response)</param>
    /// <param name="timeout">Maximum time to wait (default: 5 minutes)</param>
    /// <returns>The response event</returns>
    public Task<T> WaitForResponseAsync<T>(
        string requestId,
        TimeSpan? timeout = null) where T : AgentEvent
        => Base.WaitForResponseAsync<T>(requestId, timeout);

    /// <summary>
    /// Stream registry for managing interruptible operations.
    /// May be null if event coordination is not configured with stream support.
    /// </summary>
    /// <remarks>
    /// Used by audio middleware for stream interruption and priority streaming.
    /// Forwarded from the base AgentContext for convenience.
    /// </remarks>
    public IStreamRegistry? Streams => Base.Streams;

    /// <summary>
    /// Gets the event coordinator for hierarchical event bubbling in nested workflows.
    /// Used by source-generated MultiAgent and SubAgent wrappers to propagate events to parent.
    /// </summary>
    /// <remarks>
    /// <para><b>For source-generated code only.</b></para>
    /// <para>
    /// External code should use <see cref="Emit"/> and <see cref="WaitForResponseAsync{T}"/> instead.
    /// This property is public (not internal) because source-generated code runs in consumer assemblies.
    /// </para>
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public HPD.Events.IEventCoordinator? GetParentEventCoordinator()
    {
        // BidirectionalEventCoordinator now implements HPD.Events.IEventCoordinator
        return Base.EventCoordinator as HPD.Events.IEventCoordinator;
    }

    /// <summary>
    /// Gets the parent agent's chat client for SubAgent inheritance.
    /// Used by source-generated SubAgent wrappers when no provider is specified.
    /// </summary>
    /// <remarks>
    /// <para><b>For source-generated code only.</b></para>
    /// <para>
    /// This property is public (not internal) because source-generated code runs in consumer assemblies.
    /// </para>
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Microsoft.Extensions.AI.IChatClient? GetParentChatClient()
    {
        return Base.ParentChatClient;
    }

    /// <summary>
    /// Gets the parent agent's execution context for hierarchical event attribution.
    /// Used by source-generated SubAgent and MultiAgent wrappers.
    /// </summary>
    /// <remarks>
    /// <para><b>For source-generated code only.</b></para>
    /// <para>
    /// Returns the ExecutionContext from RootAgent, which contains:
    /// - AgentName, AgentId, ParentAgentId
    /// - AgentChain (full hierarchy path)
    /// - Depth (nesting level)
    /// </para>
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public AgentExecutionContext? GetParentExecutionContext()
    {
        return Agent.RootAgent?.ExecutionContext;
    }

    //
    // CONSTRUCTOR
    //

    /// <summary>
    /// Initializes a new hook context wrapping the base agent context.
    /// </summary>
    /// <param name="baseContext">The underlying agent context</param>
    protected HookContext(AgentContext baseContext)
    {
        Base = baseContext ?? throw new ArgumentNullException(nameof(baseContext));
    }
}
