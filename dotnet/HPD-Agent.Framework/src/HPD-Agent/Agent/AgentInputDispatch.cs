using HPD.Agent.ClientTools;
using HPD.Agent.Middleware;
using HPD.Events;
using Microsoft.Extensions.DependencyInjection;
using HPD.Events.Struct;
using Microsoft.Extensions.AI;
using static HPD.Agent.AgentInputResults;

namespace HPD.Agent;

internal interface IAgentInputHandler<TInput>
    where TInput : AgentInputEvent
{
    ValueTask<AgentInputResult> HandleAsync(
        TInput input,
        AgentInputHandlingContext context,
        CancellationToken cancellationToken);
}

internal interface IAgentInputHandler
{
    Type InputType { get; }

    ValueTask<AgentInputResult> HandleAsync(
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

    public ValueTask<AgentInputResult> HandleAsync(
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

internal enum AgentInputDelivery
{
    QueuedWork,
    ActiveControl
}

internal sealed record AgentInputHandlerRegistration(
    Type InputType,
    AgentInputDelivery Delivery,
    IAgentInputHandler Handler);

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

    public ActiveRuntimeInput? ActiveInput { get; init; }
    public required Func<UserMessagesInputEvent, ActiveRuntimeInput?, IEventCoordinator, CancellationToken, Task<AgentTurnResult>> RunMessagesAsync { get; init; }
    public required Func<InterruptionRequestEvent, CancellationToken, Task<AgentInputResult>> InterruptAsync { get; init; }
    public required Func<SteeringInputEvent, CancellationToken, Task<AgentInputResult>> SteerAsync { get; init; }
    public required Func<ClientToolBackgroundOperationOutcomeEvent, bool> TryResolveClientToolBackgroundOperation { get; init; }
    public Func<BackgroundTaskNotificationInputEvent, IEventCoordinator, CancellationToken, ValueTask>? PublishBackgroundTaskNotificationDelivered { get; init; }
}

internal sealed class AgentInputDispatcher
{
    private static readonly IReadOnlyDictionary<Type, AgentInputHandlerRegistration> BuiltInRegistrations =
        CreateBuiltInHandlers().ToDictionary(static registration => registration.InputType);
    private readonly IReadOnlyDictionary<Type, AgentInputHandlerRegistration> _registrations;
    private readonly AgentMiddlewarePipeline _middleware;

    public AgentInputDispatcher(AgentMiddlewarePipeline middleware)
    {
        _middleware = middleware ?? throw new ArgumentNullException(nameof(middleware));

        _registrations = BuiltInRegistrations;
    }

    internal static AgentInputHandlerRegistration GetBuiltInRegistration(Type inputType)
        => BuiltInRegistrations.TryGetValue(inputType, out var registration)
            ? registration
            : throw new NotSupportedException($"Event type {inputType.Name} cannot be used as agent input.");

    internal AgentInputHandlerRegistration GetRegistration(Type inputType)
        => _registrations.TryGetValue(inputType, out var registration)
            ? registration
            : throw new NotSupportedException($"Event type {inputType.Name} cannot be used as agent input.");

    public async ValueTask<AgentInputResult> DispatchAsync(
        AgentInputEvent input,
        AgentInputHandlerRegistration admittedRegistration,
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
            var cancelledResult = new AgentInputResult
            {
                Disposition = AgentInputDisposition.Completed,
                ThreadExecutionId = input.ThreadExecutionId
            };
            await _middleware.ExecuteAfterInputAsync(
                    new AfterInputContext(
                        before.Input,
                        context,
                        cancelledResult.TurnResult,
                        null,
                        cancelled: true,
                        DateTimeOffset.UtcNow - startedAt),
                    CancellationToken.None)
                .ConfigureAwait(false);
            return cancelledResult;
        }

        AgentInputResult? result = null;
        Exception? error = null;
        var effectiveInput = before.Input;
        try
        {
            var effectiveRegistration = GetRegistration(effectiveInput.GetType());
            if (effectiveRegistration.Delivery != admittedRegistration.Delivery)
                throw new InvalidOperationException(
                    $"Input middleware cannot replace '{input.GetType().Name}' ({admittedRegistration.Delivery}) " +
                    $"with '{effectiveInput.GetType().Name}' ({effectiveRegistration.Delivery}).");

            result = await effectiveRegistration.Handler.HandleAsync(effectiveInput, context, cancellationToken).ConfigureAwait(false);
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
                        result?.TurnResult ?? AgentTurnResult.Empty,
                        error,
                        cancelled: error is OperationCanceledException,
                        DateTimeOffset.UtcNow - startedAt),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static IEnumerable<AgentInputHandlerRegistration> CreateBuiltInHandlers()
    {
        yield return Register(AgentInputDelivery.QueuedWork, new UserMessagesInputHandler());
        yield return Register(AgentInputDelivery.QueuedWork, new CompactThreadInputHandler());
        yield return Register(AgentInputDelivery.QueuedWork, new BackgroundTaskNotificationInputHandler());
        yield return Register(AgentInputDelivery.ActiveControl, new ClientToolBackgroundOperationOutcomeInputHandler());
        yield return Register(AgentInputDelivery.ActiveControl, new InterruptionInputHandler());
        yield return Register(AgentInputDelivery.ActiveControl, new SteeringInputHandler());
    }

    private static AgentInputHandlerRegistration Register<TInput>(
        AgentInputDelivery delivery,
        IAgentInputHandler<TInput> handler)
        where TInput : AgentInputEvent
    {
        var adapter = new AgentInputHandlerAdapter<TInput>(handler);
        return new AgentInputHandlerRegistration(typeof(TInput), delivery, adapter);
    }
}

internal sealed class CompactThreadInputHandler : IAgentInputHandler<CompactThreadInputEvent>
{
    public async ValueTask<AgentInputResult> HandleAsync(
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
                    BuilderDefault = context.DefaultChatClient,
                    SpecializedChat = summarizing.Summarizer
                },
                cancellationToken).ConfigureAwait(false)
            : null;
        var engineContext = new ThreadCompactionContext(
            thread,
            thread.Messages,
            publisher,
            chatLease?.Client,
            context.Services?.GetService<IThreadJournalRebaseSeedProvider>(),
            CreateSummarizerOptions(chatLease?.Handle.ResolvedConfig as ChatClientConfig));
        await new ThreadCompactionEngine().ExecuteAsync(
                engineContext,
                input.Request.Compaction,
                context.AgentName,
                iteration: 0,
                CompactionOrigin.Explicit,
                input.Request.Continuation,
                cancellationToken)
            .ConfigureAwait(false);
        return Completed(input);
    }

    private static ChatOptions? CreateSummarizerOptions(ChatClientConfig? config)
    {
        var options = config?.ToMicrosoftChatOptions() ?? new ChatOptions();
        options.Tools = [];
        options.ToolMode = ChatToolMode.None;
        return options;
    }
}

internal sealed class UserMessagesInputHandler : IAgentInputHandler<UserMessagesInputEvent>
{
    public async ValueTask<AgentInputResult> HandleAsync(
        UserMessagesInputEvent input,
        AgentInputHandlingContext context,
        CancellationToken cancellationToken)
    {
        var turn = await context.RunMessagesAsync(input, context.ActiveInput, context.EventCoordinator, cancellationToken)
            .ConfigureAwait(false);
        return Completed(input, turn);
    }
}

internal sealed class BackgroundTaskNotificationInputHandler : IAgentInputHandler<BackgroundTaskNotificationInputEvent>
{
    public async ValueTask<AgentInputResult> HandleAsync(
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

        var result = await context.RunMessagesAsync(userInput, context.ActiveInput, context.EventCoordinator, cancellationToken)
            .ConfigureAwait(false);
        if (context.PublishBackgroundTaskNotificationDelivered is { } publishDelivered)
        {
            await publishDelivered(input, context.EventCoordinator, cancellationToken).ConfigureAwait(false);
        }
        return Completed(input, result);
    }
}

internal sealed class ClientToolBackgroundOperationOutcomeInputHandler :
    IAgentInputHandler<ClientToolBackgroundOperationOutcomeEvent>
{
    public ValueTask<AgentInputResult> HandleAsync(
        ClientToolBackgroundOperationOutcomeEvent input,
        AgentInputHandlingContext context,
        CancellationToken cancellationToken)
    {
        if (!context.TryResolveClientToolBackgroundOperation(input))
        {
            throw new InvalidOperationException(
                $"No client tool background operation '{input.ClientOperationId}' is active.");
        }

        return ValueTask.FromResult(new AgentInputResult
        {
            Disposition = AgentInputDisposition.Accepted,
            ThreadExecutionId = input.ThreadExecutionId
        });
    }
}

internal sealed class InterruptionInputHandler : IAgentInputHandler<InterruptionRequestEvent>
{
    public async ValueTask<AgentInputResult> HandleAsync(
        InterruptionRequestEvent input,
        AgentInputHandlingContext context,
        CancellationToken cancellationToken)
    {
        return await context.InterruptAsync(input, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class SteeringInputHandler : IAgentInputHandler<SteeringInputEvent>
{
    public async ValueTask<AgentInputResult> HandleAsync(
        SteeringInputEvent input,
        AgentInputHandlingContext context,
        CancellationToken cancellationToken)
        => await context.SteerAsync(input, cancellationToken).ConfigureAwait(false);
}

internal static class AgentInputResults
{
    internal static AgentInputResult Completed(AgentInputEvent input, AgentTurnResult? turn = null)
        => new()
        {
            Disposition = AgentInputDisposition.Completed,
            TurnResult = turn ?? AgentTurnResult.Empty,
            ThreadExecutionId = input.ThreadExecutionId
        };
}
