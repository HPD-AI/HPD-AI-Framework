using HPD.Agent.ClientTools;
using HPD.Agent.Middleware;
using HPD.Events;
using Microsoft.Extensions.DependencyInjection;
using HPD.Events.Struct;

namespace HPD.Agent;

internal interface IAgentInputHandler<TInput>
    where TInput : AgentInputEvent
{
    ValueTask<AgentTurnResult> HandleAsync(
        TInput input,
        AgentInputHandlingContext context,
        CancellationToken cancellationToken);
}

internal interface IAgentInputHandler
{
    Type InputType { get; }

    ValueTask<AgentTurnResult> HandleAsync(
        AgentInputEvent input,
        AgentInputHandlingContext context,
        CancellationToken cancellationToken);
}

internal sealed class AgentInputHandlerAdapter<TInput> : IAgentInputHandler
    where TInput : AgentInputEvent
{
    private readonly IAgentInputHandler<TInput> _handler;

    public AgentInputHandlerAdapter(IAgentInputHandler<TInput> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public Type InputType => typeof(TInput);

    public ValueTask<AgentTurnResult> HandleAsync(
        AgentInputEvent input,
        AgentInputHandlingContext context,
        CancellationToken cancellationToken)
    {
        if (input is not TInput typed)
        {
            throw new ArgumentException(
                $"Input handler for '{typeof(TInput).Name}' cannot handle '{input.GetType().Name}'.",
                nameof(input));
        }

        return _handler.HandleAsync(typed, context, cancellationToken);
    }
}

internal sealed class AgentInputHandlingContext
{
    public required string AgentName { get; init; }
    public required AgentConfig Config { get; init; }
    public required IEventCoordinator EventCoordinator { get; init; }
    public IServiceProvider? Services { get; init; }
    public AgentClientSet? ClientSet { get; init; }
    public IContentStore? ContentStore { get; init; }
    public IRuntimeCapabilityRegistry RuntimeCapabilities { get; init; } = new RuntimeCapabilityRegistry();
    public IStructEventHub StructEvents { get; init; } = new StructEventHub();
    public AgentRunConfig? RuntimeRunConfig { get; init; }
    public AgentChatClientResolver ChatClientResolver { get; init; } = new(null, null);
    public AgentChatClientHandle? DefaultChatClient { get; init; }

    public required Func<UserMessagesInputEvent, IEventCoordinator, CancellationToken, Task<AgentTurnResult>> RunMessagesAsync { get; init; }
    public required Func<InterruptionRequestEvent, CancellationToken, Task> InterruptAsync { get; init; }
    public required Func<ClientToolBackgroundOperationOutcomeEvent, bool> TryResolveClientToolBackgroundOperation { get; init; }
    public Func<BackgroundTaskNotificationInputEvent, IEventCoordinator, CancellationToken, ValueTask>? PublishBackgroundTaskNotificationDelivered { get; init; }
}

internal sealed class AgentInputDispatcher
{
    private readonly IReadOnlyDictionary<Type, IAgentInputHandler> _handlers;
    private readonly AgentMiddlewarePipeline _middleware;

    public AgentInputDispatcher(AgentMiddlewarePipeline middleware)
    {
        _middleware = middleware ?? throw new ArgumentNullException(nameof(middleware));

        var map = new Dictionary<Type, IAgentInputHandler>();
        foreach (var handler in CreateBuiltInHandlers())
        {
            if (!map.TryAdd(handler.InputType, handler))
            {
                throw new InvalidOperationException(
                    $"An agent input handler for '{handler.InputType.Name}' is already registered.");
            }
        }

        _handlers = map;
    }

    public async ValueTask<AgentTurnResult> DispatchAsync(
        AgentInputEvent input,
        AgentInputHandlingContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        var startedAt = DateTimeOffset.UtcNow;
        var before = new BeforeInputContext(input, context);
        await _middleware.ExecuteBeforeInputAsync(before, cancellationToken).ConfigureAwait(false);

        if (before.Cancelled)
        {
            var cancelledResult = AgentTurnResult.Empty;
            await _middleware.ExecuteAfterInputAsync(
                    new AfterInputContext(
                        before.Input,
                        context,
                        cancelledResult,
                        null,
                        cancelled: true,
                        DateTimeOffset.UtcNow - startedAt),
                    CancellationToken.None)
                .ConfigureAwait(false);
            return cancelledResult;
        }

        AgentTurnResult? result = null;
        Exception? error = null;
        var effectiveInput = before.Input;
        try
        {
            if (!_handlers.TryGetValue(effectiveInput.GetType(), out var handler))
            {
                throw new NotSupportedException(
                    $"Event type {effectiveInput.GetType().Name} cannot be used as agent input.");
            }

            result = await handler.HandleAsync(effectiveInput, context, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            await _middleware.ExecuteAfterInputAsync(
                    new AfterInputContext(
                        effectiveInput,
                        context,
                        result ?? AgentTurnResult.Empty,
                        error,
                        cancelled: error is OperationCanceledException,
                        DateTimeOffset.UtcNow - startedAt),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static IEnumerable<IAgentInputHandler> CreateBuiltInHandlers()
    {
        yield return new AgentInputHandlerAdapter<UserMessagesInputEvent>(new UserMessagesInputHandler());
        yield return new AgentInputHandlerAdapter<CompactThreadInputEvent>(new CompactThreadInputHandler());
        yield return new AgentInputHandlerAdapter<BackgroundTaskNotificationInputEvent>(new BackgroundTaskNotificationInputHandler());
        yield return new AgentInputHandlerAdapter<ClientToolBackgroundOperationOutcomeEvent>(new ClientToolBackgroundOperationOutcomeInputHandler());
        yield return new AgentInputHandlerAdapter<InterruptionRequestEvent>(new InterruptionInputHandler());
    }
}

internal sealed class CompactThreadInputHandler : IAgentInputHandler<CompactThreadInputEvent>
{
    public async ValueTask<AgentTurnResult> HandleAsync(
        CompactThreadInputEvent input,
        AgentInputHandlingContext context,
        CancellationToken cancellationToken)
    {
        if (context.Config.Compaction is null)
            throw new InvalidOperationException("Compaction is not configured for this agent.");

        Thread thread;
        if (input.Thread is not null || input.Session is not null)
        {
            if (input.Thread is null || input.Session is null)
                throw new InvalidOperationException("Process-local compaction requires both Session and Thread.");
            thread = input.Thread;
        }
        else
        {
            var store = context.Config.SessionStore
                ?? throw new InvalidOperationException("Scoped compaction requires a session store.");
            if (string.IsNullOrWhiteSpace(input.SessionId) || string.IsNullOrWhiteSpace(input.ThreadId))
                throw new InvalidOperationException("Explicit compaction requires a session and thread scope.");
            thread = await store.ProjectThreadAsync(
                    input.SessionId,
                    input.ThreadId,
                    ThreadProjectionPurpose.ModelContext,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Thread '{input.SessionId}/{input.ThreadId}' was not found.");
        }

        var publisher = context.Config.SessionStore is { } sessionStore
            ? new ThreadEventPublisher(sessionStore, context.EventCoordinator)
            : null;
        await using var chatLease = input.Request.Compaction.Strategy is SummarizingCompaction summarizing
            ? await context.ChatClientResolver.ResolveAsync(
                new AgentChatClientResolutionRequest
                {
                    AgentConfig = context.Config,
                    RunConfig = input.RunConfig ?? context.RuntimeRunConfig,
                    AgentDefault = context.DefaultChatClient,
                    DedicatedProvider = summarizing.Provider
                },
                cancellationToken).ConfigureAwait(false)
            : null;
        var engineContext = new ThreadCompactionContext(
            thread,
            thread.Messages,
            publisher,
            chatLease?.Client,
            context.Services?.GetService<IThreadJournalRebaseSeedProvider>());
        await new ThreadCompactionEngine().ExecuteAsync(
                engineContext,
                input.Request.Compaction,
                context.AgentName,
                iteration: 0,
                CompactionOrigin.Explicit,
                input.Request.Continuation,
                cancellationToken)
            .ConfigureAwait(false);
        return AgentTurnResult.Empty;
    }
}

internal sealed class UserMessagesInputHandler : IAgentInputHandler<UserMessagesInputEvent>
{
    public ValueTask<AgentTurnResult> HandleAsync(
        UserMessagesInputEvent input,
        AgentInputHandlingContext context,
        CancellationToken cancellationToken)
        => new(context.RunMessagesAsync(input, context.EventCoordinator, cancellationToken));
}

internal sealed class BackgroundTaskNotificationInputHandler : IAgentInputHandler<BackgroundTaskNotificationInputEvent>
{
    public async ValueTask<AgentTurnResult> HandleAsync(
        BackgroundTaskNotificationInputEvent input,
        AgentInputHandlingContext context,
        CancellationToken cancellationToken)
    {
        var userInput = BackgroundTaskNotificationDispatcher.ToUserMessagesInput(input) with
        {
            AgentId = input.AgentId,
            SessionId = input.SessionId,
            ThreadId = input.ThreadId,
            ThreadExecutionId = input.ThreadExecutionId,
            RunConfig = input.RunConfig
        };

        var result = await context.RunMessagesAsync(userInput, context.EventCoordinator, cancellationToken)
            .ConfigureAwait(false);
        if (context.PublishBackgroundTaskNotificationDelivered is { } publishDelivered)
        {
            await publishDelivered(input, context.EventCoordinator, cancellationToken).ConfigureAwait(false);
        }
        return result;
    }
}

internal sealed class ClientToolBackgroundOperationOutcomeInputHandler :
    IAgentInputHandler<ClientToolBackgroundOperationOutcomeEvent>
{
    public ValueTask<AgentTurnResult> HandleAsync(
        ClientToolBackgroundOperationOutcomeEvent input,
        AgentInputHandlingContext context,
        CancellationToken cancellationToken)
    {
        if (!context.TryResolveClientToolBackgroundOperation(input))
        {
            throw new InvalidOperationException(
                $"No client tool background operation '{input.ClientOperationId}' is active.");
        }

        return ValueTask.FromResult(AgentTurnResult.Empty);
    }
}

internal sealed class InterruptionInputHandler : IAgentInputHandler<InterruptionRequestEvent>
{
    public async ValueTask<AgentTurnResult> HandleAsync(
        InterruptionRequestEvent input,
        AgentInputHandlingContext context,
        CancellationToken cancellationToken)
    {
        await context.InterruptAsync(input, cancellationToken).ConfigureAwait(false);
        return AgentTurnResult.Empty;
    }
}
