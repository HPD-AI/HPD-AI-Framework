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

internal enum AgentInputRoutingClass
{
    Work,
    SessionControl,
    ActiveControl
}

internal sealed record AgentInputHandlerRegistration(
    Type InputType,
    AgentInputRoutingClass RoutingClass,
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
    public required Func<ClientToolOperationOutcomeEvent, bool> TryResolveClientToolOperation { get; init; }
    public Func<AgentOperationNotificationInputEvent, IEventCoordinator, CancellationToken, ValueTask>? PublishAgentOperationNotificationDelivered { get; init; }
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
            var cancelledResult = new AgentInputResult.Completed(
                AgentTurnResult.Empty,
                input.ThreadExecutionId);
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
            if (effectiveRegistration.RoutingClass != admittedRegistration.RoutingClass)
                throw new InvalidOperationException(
                    $"Input middleware cannot replace '{input.GetType().Name}' ({admittedRegistration.RoutingClass}) " +
                    $"with '{effectiveInput.GetType().Name}' ({effectiveRegistration.RoutingClass}).");

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
                        result is AgentInputResult.Completed completed
                            ? completed.TurnResult
                            : AgentTurnResult.Empty,
                        error,
                        cancelled: error is OperationCanceledException,
                        DateTimeOffset.UtcNow - startedAt),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static IEnumerable<AgentInputHandlerRegistration> CreateBuiltInHandlers()
    {
        yield return Register(AgentInputRoutingClass.Work, new UserMessagesInputHandler());
        yield return Register(AgentInputRoutingClass.Work, new CompactThreadInputHandler());
        yield return Register(AgentInputRoutingClass.Work, new AgentOperationNotificationInputHandler());
        yield return Register(AgentInputRoutingClass.SessionControl, new AudioSessionInputHandler());
        yield return Register(AgentInputRoutingClass.ActiveControl, new ClientToolOperationOutcomeInputHandler());
    }

    private static AgentInputHandlerRegistration Register<TInput>(
        AgentInputRoutingClass routingClass,
        IAgentInputHandler<TInput> handler)
        where TInput : AgentInputEvent
    {
        var adapter = new AgentInputHandlerAdapter<TInput>(handler);
        return new AgentInputHandlerRegistration(typeof(TInput), routingClass, adapter);
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

internal sealed class AgentOperationNotificationInputHandler : IAgentInputHandler<AgentOperationNotificationInputEvent>
{
    public async ValueTask<AgentInputResult> HandleAsync(
        AgentOperationNotificationInputEvent input,
        AgentInputHandlingContext context,
        CancellationToken cancellationToken)
    {
        var userInput = AgentOperationNotificationDispatcher.ToUserMessagesInput(input) with
        {
            AgentId = input.AgentId,
            SessionId = input.SessionId,
            ThreadId = input.ThreadId,
            ThreadExecutionId = input.ThreadExecutionId,
            RunConfig = input.RunConfig
        };

        var result = await context.RunMessagesAsync(userInput, context.ActiveInput, context.EventCoordinator, cancellationToken)
            .ConfigureAwait(false);
        if (context.PublishAgentOperationNotificationDelivered is { } publishDelivered)
        {
            await publishDelivered(input, context.EventCoordinator, cancellationToken).ConfigureAwait(false);
        }
        return Completed(input, result);
    }
}

internal sealed class ClientToolOperationOutcomeInputHandler :
    IAgentInputHandler<ClientToolOperationOutcomeEvent>
{
    public ValueTask<AgentInputResult> HandleAsync(
        ClientToolOperationOutcomeEvent input,
        AgentInputHandlingContext context,
        CancellationToken cancellationToken)
    {
        if (!context.TryResolveClientToolOperation(input))
        {
            throw new InvalidOperationException(
                $"No client tool background operation '{input.ClientOperationId}' is active.");
        }

        return ValueTask.FromResult<AgentInputResult>(
            new AgentInputResult.Control(AgentInputDisposition.Accepted, input.ThreadExecutionId));
    }
}

internal sealed class AudioSessionInputHandler : IAgentInputHandler<AudioSessionInputEvent>
{
    public async ValueTask<AgentInputResult> HandleAsync(
        AudioSessionInputEvent input,
        AgentInputHandlingContext context,
        CancellationToken cancellationToken)
    {
        if (input.ThreadExecutionId is not null)
        {
            return new AgentInputResult.AudioSession(new AudioSessionInputResult.Rejected(
                AudioSessionInputDisposition.ScopeMismatch,
                "thread-execution-id-forbidden"));
        }

        if (!context.RuntimeCapabilities.TryGet<IAudioSessionInputRuntime>(out var runtime))
        {
            return new AgentInputResult.AudioSession(new AudioSessionInputResult.Rejected(
                AudioSessionInputDisposition.CapabilityNotInstalled,
                "audio-capability-not-installed"));
        }

        var result = await runtime.ExecuteAsync(input, context.ClientSet, cancellationToken).ConfigureAwait(false);
        if (result is AudioSessionInputResult.InputTurnCommitted committed &&
            committed.TryTakeAdmittedMessage() is { } message)
        {
            var accepted = await runtime.AcceptSemanticAsync(
                committed.AudioSessionId, committed.CandidateId, cancellationToken).ConfigureAwait(false);
            if (accepted is AudioSemanticAdmissionResult.Conflict conflict)
            {
                return new AgentInputResult.AudioSession(new AudioSessionInputResult.InputTurnDiscarded(
                    committed.AudioSessionId, committed.CandidateId, committed.Revision, conflict.SafeCode));
            }
            if (accepted is AudioSemanticAdmissionResult.OutcomeUnknown unknown)
            {
                return new AgentInputResult.AudioSession(new AudioSessionInputResult.OutcomeUnknown(
                    committed.DurableSemanticOperationId ?? unknown.SafeCode,
                    committed.AudioSessionId,
                    committed.Revision));
            }

            try
            {
                await context.RunMessagesAsync(new UserMessagesInputEvent
                {
                    AgentId = input.AgentId,
                    SessionId = input.SessionId,
                    ThreadId = input.ThreadId,
                    ClientInputId = committed.DurableSemanticOperationId,
                    Delivery = AgentInputDelivery.Queue,
                    Messages = [message]
                }, null, context.EventCoordinator, cancellationToken).ConfigureAwait(false);
                await runtime.AcknowledgeSemanticAsync(
                    committed.AudioSessionId, committed.CandidateId, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                await runtime.WithdrawSemanticAsync(
                    committed.AudioSessionId, committed.CandidateId, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        return new AgentInputResult.AudioSession(result);
    }
}

internal static class AgentInputResults
{
    internal static AgentInputResult Completed(AgentInputEvent input, AgentTurnResult? turn = null)
        => new AgentInputResult.Completed(turn ?? AgentTurnResult.Empty, input.ThreadExecutionId);
}
