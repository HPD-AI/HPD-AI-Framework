using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace HPD.Agent.Middleware;

/// <summary>
/// Middleware interface for agent lifecycle hooks (V2).
/// All hooks have default no-op implementations - implement only what you need.
/// </summary>
/// <remarks>
/// <para><b>V2 Improvements:</b></para>
/// <list type="bullet">
/// <item>  Typed contexts - compile-time safety, no NULL properties</item>
/// <item>  Immutable requests - preserve original for debugging/retry</item>
/// <item>  Streaming-first architecture - all LLM calls stream by default</item>
/// <item>  Centralized errors - OnErrorAsync hook for all errors</item>
/// <item>  Immediate state updates - no scheduled updates</item>
/// </list>
///
/// <para><b>Lifecycle Overview:</b></para>
/// <code>
/// BeforeMessageTurnAsync(BeforeMessageTurnContext)
///   └─► [LOOP] BeforeIterationAsync(BeforeIterationContext)
///               └─► WrapModelCallStreamingAsync(ModelRequest) - streaming LLM call
///               └─► BeforeToolExecutionAsync(BeforeToolExecutionContext)
///                     └─► BeforeParallelBatchAsync(BeforeParallelBatchContext)
///                     └─► [LOOP] BeforeFunctionAsync(BeforeFunctionContext)
///                                  └─► WrapFunctionCallAsync(FunctionRequest)
///                                  └─► AfterFunctionAsync(AfterFunctionContext)
///               └─► AfterIterationAsync(AfterIterationContext)
///   └─► AfterMessageTurnAsync(AfterMessageTurnContext)
///
/// [ON ERROR]: OnErrorAsync(ErrorContext) - reverse order
/// </code>
///
/// <para><b>Execution Order:</b></para>
/// <para>
/// Before* hooks run in registration order.<br/>
/// After* hooks run in reverse registration order (stack unwinding).<br/>
/// Wrap* hooks run in chain order (last registered = outermost wrapper).<br/>
/// OnErrorAsync runs in reverse registration order (error unwinding).
/// </para>
/// </remarks>
public interface IAgentMiddleware
{
    //
    // TURN LEVEL (once per user message)
    //

    /// <summary>
    /// Called BEFORE processing a user message turn.
    /// Use for: RAG injection, memory retrieval, context augmentation.
    /// </summary>
    /// <param name="context">Typed context with UserMessage, ConversationHistory</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Called AFTER a message turn completes.
    /// Use for: Memory extraction, analytics, turn-level logging.
    /// </summary>
    /// <param name="context">Typed context with FinalResponse, TurnHistory</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AfterMessageTurnAsync(
        AfterMessageTurnContext context,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    //
    // ITERATION LEVEL (once per LLM call)
    //

    /// <summary>
    /// Called BEFORE each LLM iteration.
    /// Use for: Prompt modification, message injection, option tuning.
    /// </summary>
    /// <param name="context">Typed context with Iteration, Messages, Options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BeforeIterationAsync(
        BeforeIterationContext context,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Wraps the LLM model call with full streaming control.
    /// Use for: retry, caching, request modification, streaming transformation, progressive metrics.
    /// </summary>
    /// <remarks>
    /// <para><b>Streaming Architecture:</b></para>
    /// <para>
    /// The agent ALWAYS uses streaming for LLM calls. This hook gives middleware full control
    /// over the streaming response updates, or can simply pass through if no interception is needed.
    /// </para>
    ///
    /// <para><b>Null Semantics - Opt-in Pattern:</b></para>
    /// <para>
    /// Return <c>null</c> (default) to indicate "I don't need to intercept streaming."
    /// The pipeline will pass through the stream directly without invoking your middleware.
    /// Only override this method if your middleware specifically needs streaming access.
    /// </para>
    ///
    /// <para><b>Use Cases:</b></para>
    /// <list type="bullet">
    /// <item><b>Token-level inspection:</b> Progressive token counting, metrics collection</item>
    /// <item><b>Streaming transformation:</b> Modify or filter updates mid-stream</item>
    /// <item><b>Progressive synthesis:</b> TTS Quick Answer (speak first sentence while LLM continues)</item>
    /// <item><b>Retry/Caching:</b> Buffer stream for retry logic or cache storage</item>
    /// <item><b>Request modification:</b> Modify request before calling handler</item>
    /// </list>
    ///
    /// <para><b>Example: Progressive Token Counting</b></para>
    /// <code>
    /// public IAsyncEnumerable&lt;ChatResponseUpdate&gt;? WrapModelCallStreamingAsync(
    ///     ModelRequest request,
    ///     Func&lt;ModelRequest, IAsyncEnumerable&lt;ChatResponseUpdate&gt;&gt; handler,
    ///     [EnumeratorCancellation] CancellationToken ct)
    /// {
    ///     return CountTokensAsync(request, handler, ct);
    /// }
    ///
    /// private async IAsyncEnumerable&lt;ChatResponseUpdate&gt; CountTokensAsync(
    ///     ModelRequest request,
    ///     Func&lt;ModelRequest, IAsyncEnumerable&lt;ChatResponseUpdate&gt;&gt; handler,
    ///     [EnumeratorCancellation] CancellationToken ct)
    /// {
    ///     int tokenCount = 0;
    ///
    ///     await foreach (var update in handler(request).WithCancellation(ct))
    ///     {
    ///         // Count tokens as they stream
    ///         if (update.Contents != null)
    ///         {
    ///             foreach (var content in update.Contents)
    ///                 if (content is TextContent text)
    ///                     tokenCount += EstimateTokens(text.Text);
    ///         }
    ///
    ///         yield return update;
    ///     }
    ///
    ///     // Emit final count after stream completes
    ///     context.Emit(new TokenUsageEvent(tokenCount));
    /// }
    /// </code>
    ///
    /// <para><b>Example: Simple Retry (buffered)</b></para>
    /// <code>
    /// public IAsyncEnumerable&lt;ChatResponseUpdate&gt;? WrapModelCallStreamingAsync(
    ///     ModelRequest request,
    ///     Func&lt;ModelRequest, IAsyncEnumerable&lt;ChatResponseUpdate&gt;&gt; handler,
    ///     [EnumeratorCancellation] CancellationToken ct)
    /// {
    ///     return RetryAsync(request, handler, ct);
    /// }
    ///
    /// private async IAsyncEnumerable&lt;ChatResponseUpdate&gt; RetryAsync(
    ///     ModelRequest request,
    ///     Func&lt;ModelRequest, IAsyncEnumerable&lt;ChatResponseUpdate&gt;&gt; handler,
    ///     [EnumeratorCancellation] CancellationToken ct)
    /// {
    ///     for (int attempt = 0; attempt &lt; 3; attempt++)
    ///     {
    ///         var updates = new List&lt;ChatResponseUpdate&gt;();
    ///         try
    ///         {
    ///             await foreach (var update in handler(request).WithCancellation(ct))
    ///             {
    ///                 updates.Add(update);
    ///             }
    ///
    ///             // Success - replay buffered updates
    ///             foreach (var update in updates)
    ///                 yield return update;
    ///             yield break;
    ///         }
    ///         catch (Exception ex) when (ShouldRetry(ex) &amp;&amp; attempt &lt; 2)
    ///         {
    ///             await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
    ///         }
    ///     }
    /// }
    /// </code>
    /// </remarks>
    /// <param name="request">Immutable request object</param>
    /// <param name="handler">Next handler in chain - call this to continue the pipeline</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Null (default) to pass through without interception, or async enumerable to intercept streaming</returns>
    IAsyncEnumerable<ChatResponseUpdate>? WrapModelCallStreamingAsync(
        ModelRequest request,
        Func<ModelRequest, IAsyncEnumerable<ChatResponseUpdate>> handler,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        => null;  // null = "I don't need to intercept streaming"

    /// <summary>
    /// Called AFTER LLM returns but BEFORE tools execute.
    /// Use for: Tool validation, permission checks, tool filtering.
    /// </summary>
    /// <param name="context">Typed context with Response, ToolCalls</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BeforeToolExecutionAsync(
        BeforeToolExecutionContext context,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Called AFTER all tools complete for this iteration.
    /// Use for: Result aggregation, error recovery, telemetry.
    /// </summary>
    /// <param name="context">Typed context with ToolResults</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AfterIterationAsync(
        AfterIterationContext context,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    //
    // FUNCTION LEVEL (once per tool call)
    //

    /// <summary>
    /// Called BEFORE a batch of parallel functions execute.
    /// Use for: Batch-level permission checks, rate limiting.
    /// </summary>
    /// <param name="context">Typed context with ParallelFunctions</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BeforeParallelBatchAsync(
        BeforeParallelBatchContext context,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Called BEFORE each individual function executes.
    /// Use for: Argument validation, permission checks, logging.
    /// </summary>
    /// <param name="context">Typed context with Function, CallId, Arguments</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BeforeFunctionAsync(
        BeforeFunctionContext context,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Wraps function execution with full control.
    /// Use for: retry, caching, timeout, result transformation.
    /// </summary>
    /// <remarks>
    /// <para><b>Example: Function Retry</b></para>
    /// <code>
    /// public async Task&lt;object?&gt; WrapFunctionCallAsync(
    ///     FunctionRequest request,
    ///     Func&lt;FunctionRequest, Task&lt;object?&gt;&gt; handler,
    ///     CancellationToken ct)
    /// {
    ///     for (int attempt = 0; attempt &lt; 3; attempt++)
    ///     {
    ///         try { return await handler(request); }
    ///         catch (Exception ex) when (ShouldRetry(ex) &amp;&amp; attempt &lt; 2)
    ///         {
    ///             await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
    ///         }
    ///     }
    ///     return await handler(request);
    /// }
    /// </code>
    /// </remarks>
    /// <param name="request">Immutable request object</param>
    /// <param name="handler">Next handler in chain</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<object?> WrapFunctionCallAsync(
        FunctionRequest request,
        Func<FunctionRequest, Task<object?>> handler,
        CancellationToken cancellationToken)
        => handler(request);

    /// <summary>
    /// Called AFTER a function completes.
    /// Use for: Result transformation, error handling, telemetry.
    /// </summary>
    /// <param name="context">Typed context with Function, Result, Exception</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AfterFunctionAsync(
        AfterFunctionContext context,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    //
    // ERROR HANDLING (NEW IN V2)
    //

    /// <summary>
    /// Called when an error occurs during agent execution.
    /// Provides centralized error handling, logging, and recovery.
    /// </summary>
    /// <remarks>
    /// <para><b>Execution Order:</b></para>
    /// <para>
    /// OnErrorAsync executes in REVERSE registration order (like After* hooks)
    /// for proper error handler unwinding.
    /// </para>
    /// <para>
    /// Registration: [ErrorLogger, CircuitBreaker, Telemetry]<br/>
    /// Execution: Telemetry.OnErrorAsync → CircuitBreaker.OnErrorAsync → ErrorLogger.OnErrorAsync
    /// </para>
    ///
    /// <para><b>Error Propagation:</b></para>
    /// <list type="bullet">
    /// <item>By default, errors propagate after all OnErrorAsync handlers complete</item>
    /// <item>To swallow error and prevent termination, set <c>context.UpdateState(s => s with { IsTerminated = true })</c></item>
    /// <item>If OnErrorAsync itself throws, the original error is preserved and propagated</item>
    /// <item>Error handlers execute in sequence - each handler sees the original error</item>
    /// </list>
    ///
    /// <para><b>Use Cases:</b></para>
    /// <list type="bullet">
    /// <item>Centralized error logging</item>
    /// <item>Circuit breaker logic (count errors, terminate on threshold)</item>
    /// <item>Error transformation (e.g., sanitize error messages)</item>
    /// <item>Graceful degradation (swallow errors for non-critical operations)</item>
    /// </list>
    ///
    /// <para><b>Example: Circuit Breaker with Graceful Termination</b></para>
    /// <code>
    /// public Task OnErrorAsync(ErrorContext context, CancellationToken ct)
    /// {
    ///     if (context.Source == ErrorSource.ToolCall)
    ///     {
    ///         var errorState = context.State.MiddlewareState.ErrorTracking ?? new();
    ///         var newState = errorState.IncrementFailures();
    ///
    ///         if (newState.ConsecutiveFailures >= 3)
    ///         {
    ///             // Swallow error and gracefully terminate
    ///             context.UpdateState(s => s with
    ///             {
    ///                 IsTerminated = true,
    ///                 TerminationReason = "Circuit breaker: too many errors"
    ///             });
    ///         }
    ///         else
    ///         {
    ///             // Update error count but let error propagate
    ///             context.UpdateState(s => s with
    ///             {
    ///                 MiddlewareState = s.MiddlewareState.WithErrorTracking(newState)
    ///             });
    ///         }
    ///     }
    ///
    ///     return Task.CompletedTask;
    /// }
    /// </code>
    /// </remarks>
    /// <param name="context">Error context with error details and source</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task OnErrorAsync(
        ErrorContext context,
        CancellationToken cancellationToken)
        => Task.CompletedTask;
}
