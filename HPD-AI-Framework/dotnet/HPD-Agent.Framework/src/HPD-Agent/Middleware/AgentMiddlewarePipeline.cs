using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace HPD.Agent.Middleware;

/// <summary>
/// Executes V2 middleware hooks in the correct order for each lifecycle phase.
/// Handles Before* hooks in registration order and After* hooks in reverse order.
/// </summary>
/// <remarks>
/// <para><b>V2 Improvements:</b></para>
/// <list type="bullet">
/// <item>  Typed context passing - each hook gets strongly-typed context</item>
/// <item>  Dual pattern support - automatically routes to simple or streaming LLM hooks</item>
/// <item>  Centralized error routing - OnErrorAsync called in reverse order</item>
/// <item>  Immutable request pattern - preserve original for debugging/retry</item>
/// </list>
///
/// <para><b>Execution Order:</b></para>
/// <para>
/// Before* hooks run in registration order (first registered = first executed).<br/>
/// After* hooks run in reverse order (last registered = first executed, like stack unwinding).<br/>
/// OnErrorAsync runs in reverse order (error unwinding).
/// </para>
/// </remarks>
public class AgentMiddlewarePipeline
{
    private readonly IReadOnlyList<IAgentMiddleware> _middlewares;
    private readonly IReadOnlyList<IAgentMiddleware> _reversedMiddlewares;

    public AgentMiddlewarePipeline(IEnumerable<IAgentMiddleware> middlewares)
    {
        _middlewares = middlewares?.ToList() ?? throw new ArgumentNullException(nameof(middlewares));
        _reversedMiddlewares = _middlewares.Reverse().ToList();
    }

    public AgentMiddlewarePipeline(IReadOnlyList<IAgentMiddleware> middlewares)
    {
        _middlewares = middlewares ?? throw new ArgumentNullException(nameof(middlewares));
        _reversedMiddlewares = _middlewares.Reverse().ToList();
    }

    public bool IsEmpty => _middlewares.Count == 0;
    public int Count => _middlewares.Count;
    public IReadOnlyList<IAgentMiddleware> Middlewares => _middlewares;

    //
    // TURN LEVEL
    //

    public async Task ExecuteBeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        context.Base.SetMiddlewareExecuting(true);
        try
        {
            foreach (var middleware in _middlewares)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await middleware.BeforeMessageTurnAsync(context, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            // Always clear flag, even on exception
            context.Base.SetMiddlewareExecuting(false);
        }
    }

    public async Task ExecuteAfterMessageTurnAsync(
        AfterMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        context.Base.SetMiddlewareExecuting(true);
        try
        {
            List<Exception>? exceptions = null;

            foreach (var middleware in _reversedMiddlewares)
            {
                try
                {
                    await middleware.AfterMessageTurnAsync(context, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    exceptions ??= new List<Exception>();
                    exceptions.Add(ex);
                }
            }

            if (exceptions != null)
                throw new AggregateException("One or more After* hooks failed", exceptions);
        }
        finally
        {
            context.Base.SetMiddlewareExecuting(false);
        }
    }

    //
    // ITERATION LEVEL
    //

    public async Task ExecuteBeforeIterationAsync(
        BeforeIterationContext context,
        CancellationToken cancellationToken)
    {
        context.Base.SetMiddlewareExecuting(true);
        try
        {
            foreach (var middleware in _middlewares)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await middleware.BeforeIterationAsync(context, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            context.Base.SetMiddlewareExecuting(false);
        }
    }

    /// <summary>
    /// Executes WrapModelCallStreamingAsync hooks in chain order.
    /// Middleware can opt-in by returning non-null, or pass through by returning null.
    /// </summary>
    public async IAsyncEnumerable<ChatResponseUpdate> ExecuteModelCallStreamingAsync(
        ModelRequest request,
        Func<ModelRequest, IAsyncEnumerable<ChatResponseUpdate>> coreHandler,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Build handler chain in reverse order (last middleware wraps first)
        Func<ModelRequest, IAsyncEnumerable<ChatResponseUpdate>> handler = coreHandler;

        for (int i = _middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var previousHandler = handler;

            // Check if middleware provides streaming variant
            var streamingResult = middleware.WrapModelCallStreamingAsync(
                request,
                previousHandler,
                cancellationToken);

            if (streamingResult != null)
            {
                // Middleware provides streaming - use it
                handler = (req) => middleware.WrapModelCallStreamingAsync(
                    req,
                    previousHandler,
                    cancellationToken) ?? previousHandler(req);
            }
            else
            {
                // Middleware returns null - pass through without interception
                handler = previousHandler;
            }
        }

        await foreach (var update in handler(request).WithCancellation(cancellationToken))
        {
            yield return update;
        }
    }

    public async Task ExecuteBeforeToolExecutionAsync(
        BeforeToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        context.Base.SetMiddlewareExecuting(true);
        try
        {
            foreach (var middleware in _middlewares)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await middleware.BeforeToolExecutionAsync(context, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            context.Base.SetMiddlewareExecuting(false);
        }
    }

    public async Task ExecuteAfterIterationAsync(
        AfterIterationContext context,
        CancellationToken cancellationToken)
    {
        context.Base.SetMiddlewareExecuting(true);
        try
        {
            List<Exception>? exceptions = null;

            foreach (var middleware in _reversedMiddlewares)
            {
                try
                {
                    await middleware.AfterIterationAsync(context, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    exceptions ??= new List<Exception>();
                    exceptions.Add(ex);
                }
            }

            if (exceptions != null)
                throw new AggregateException("One or more After* hooks failed", exceptions);
        }
        finally
        {
            context.Base.SetMiddlewareExecuting(false);
        }
    }

    //
    // FUNCTION LEVEL
    //

    public async Task ExecuteBeforeParallelBatchAsync(
        BeforeParallelBatchContext context,
        CancellationToken cancellationToken)
    {
        context.Base.SetMiddlewareExecuting(true);
        try
        {
            foreach (var middleware in _middlewares)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await middleware.BeforeParallelBatchAsync(context, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            context.Base.SetMiddlewareExecuting(false);
        }
    }

    public async Task ExecuteBeforeFunctionAsync(
        BeforeFunctionContext context,
        CancellationToken cancellationToken)
    {
        context.Base.SetMiddlewareExecuting(true);
        try
        {
            foreach (var middleware in _middlewares)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Check if middleware should execute based on its scope
                if (!middleware.ShouldExecute(context))
                    continue;

                await middleware.BeforeFunctionAsync(context, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            context.Base.SetMiddlewareExecuting(false);
        }
    }

    public async Task<object?> ExecuteFunctionCallAsync(
        FunctionRequest request,
        Func<FunctionRequest, Task<object?>> coreHandler,
        CancellationToken cancellationToken)
    {
        // Build handler chain in reverse order (last middleware wraps first)
        Func<FunctionRequest, Task<object?>> handler = coreHandler;

        for (int i = _middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var previousHandler = handler;

            handler = async (req) =>
            {
                return await middleware.WrapFunctionCallAsync(req, previousHandler, cancellationToken)
                    .ConfigureAwait(false);
            };
        }

        return await handler(request).ConfigureAwait(false);
    }

    public async Task ExecuteAfterFunctionAsync(
        AfterFunctionContext context,
        CancellationToken cancellationToken)
    {
        context.Base.SetMiddlewareExecuting(true);
        try
        {
            List<Exception>? exceptions = null;

            foreach (var middleware in _reversedMiddlewares)
            {
                // Check if middleware should execute based on its scope
                if (!middleware.ShouldExecute(context))
                    continue;

                try
                {
                    await middleware.AfterFunctionAsync(context, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    exceptions ??= new List<Exception>();
                    exceptions.Add(ex);
                }
            }

            if (exceptions != null)
                throw new AggregateException("One or more After* hooks failed", exceptions);
        }
        finally
        {
            context.Base.SetMiddlewareExecuting(false);
        }
    }

    //
    // ERROR HANDLING (NEW IN V2)
    //

    /// <summary>
    /// Executes OnErrorAsync hooks in REVERSE order (error unwinding).
    /// Each hook sees the original error. If a hook throws, the original error is preserved.
    /// </summary>
    public async Task ExecuteOnErrorAsync(
        ErrorContext context,
        CancellationToken cancellationToken)
    {
        context.Base.SetMiddlewareExecuting(true);
        try
        {
            foreach (var middleware in _reversedMiddlewares)
            {
                try
                {
                    await middleware.OnErrorAsync(context, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Swallow errors in error handlers to preserve original error
                    // Could log this to observability system if needed
                }
            }
        }
        finally
        {
            context.Base.SetMiddlewareExecuting(false);
        }
    }

    //
    // INTERNAL DISPATCH METHODS (toolkit-scoped middleware — )
    //
    // These are raw loops that do NOT touch SetMiddlewareExecuting.
    // They are called from ContainerMiddleware hooks, which already run inside an outer
    // Execute*() call that has set the flag. Calling the public Execute*() variants from
    // inside another Execute*() would write false in the inner finally while the outer
    // hook is still running, breaking the SyncState() guard in AgentContext.
    // WrapFunctionCallAsync is chain-based (ExecuteFunctionCallAsync has no flag calls)
    // and does NOT need an internal Dispatch* variant.
    //

    internal async Task DispatchBeforeMessageTurnAsync(
        BeforeMessageTurnContext context, CancellationToken ct)
    {
        foreach (var m in _middlewares)
        {
            ct.ThrowIfCancellationRequested();
            await m.BeforeMessageTurnAsync(context, ct).ConfigureAwait(false);
        }
    }

    internal async Task DispatchAfterMessageTurnAsync(
        AfterMessageTurnContext context, CancellationToken ct)
    {
        List<Exception>? exceptions = null;
        foreach (var m in _reversedMiddlewares)
        {
            try { await m.AfterMessageTurnAsync(context, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { (exceptions ??= new List<Exception>()).Add(ex); }
        }
        if (exceptions != null)
            throw new AggregateException("One or more AfterMessageTurn hooks failed", exceptions);
    }

    internal async Task DispatchBeforeIterationAsync(
        BeforeIterationContext context, CancellationToken ct)
    {
        foreach (var m in _middlewares)
        {
            ct.ThrowIfCancellationRequested();
            await m.BeforeIterationAsync(context, ct).ConfigureAwait(false);
        }
    }

    internal async Task DispatchAfterIterationAsync(
        AfterIterationContext context, CancellationToken ct)
    {
        List<Exception>? exceptions = null;
        foreach (var m in _reversedMiddlewares)
        {
            try { await m.AfterIterationAsync(context, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { (exceptions ??= new List<Exception>()).Add(ex); }
        }
        if (exceptions != null)
            throw new AggregateException("One or more AfterIteration hooks failed", exceptions);
    }

    internal async Task DispatchBeforeToolExecutionAsync(
        BeforeToolExecutionContext context, CancellationToken ct)
    {
        foreach (var m in _middlewares)
        {
            ct.ThrowIfCancellationRequested();
            await m.BeforeToolExecutionAsync(context, ct).ConfigureAwait(false);
        }
    }

    internal async Task DispatchBeforeParallelBatchAsync(
        BeforeParallelBatchContext context, CancellationToken ct)
    {
        foreach (var m in _middlewares)
        {
            ct.ThrowIfCancellationRequested();
            await m.BeforeParallelBatchAsync(context, ct).ConfigureAwait(false);
        }
    }

    internal async Task DispatchBeforeFunctionAsync(
        BeforeFunctionContext context, CancellationToken ct)
    {
        foreach (var m in _middlewares)
        {
            ct.ThrowIfCancellationRequested();
            if (!m.ShouldExecute(context)) continue;
            await m.BeforeFunctionAsync(context, ct).ConfigureAwait(false);
        }
    }

    internal async Task DispatchAfterFunctionAsync(
        AfterFunctionContext context, CancellationToken ct)
    {
        List<Exception>? exceptions = null;
        foreach (var m in _reversedMiddlewares)
        {
            if (!m.ShouldExecute(context)) continue;
            try { await m.AfterFunctionAsync(context, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { (exceptions ??= new List<Exception>()).Add(ex); }
        }
        if (exceptions != null)
            throw new AggregateException("One or more AfterFunction hooks failed", exceptions);
    }

    internal async Task DispatchOnErrorAsync(
        ErrorContext context, CancellationToken ct)
    {
        foreach (var m in _reversedMiddlewares)
        {
            try { await m.OnErrorAsync(context, ct).ConfigureAwait(false); }
            catch { /* swallow — preserve original error */ }
        }
    }

}
