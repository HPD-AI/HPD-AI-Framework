using Microsoft.Extensions.AI;
using System.Collections.Concurrent;
using HPD.Agent.Middleware;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.ComponentModel;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using HPD.Agent.Providers;
using HPD.Agent.StructuredOutput;
using HPD.Events;
using HPD.Events.Struct;
using ClientToolOperationOutcomeEvent = HPD.Agent.ClientTools.ClientToolOperationOutcomeEvent;
using IClientToolOperationRegistry = HPD.Agent.ClientTools.IClientToolOperationRegistry;


namespace HPD.Agent;

/// <summary>
/// Core Agent class implementing agentic behavior with function calling, middleware, and event coordination.
/// </code>
/// </summary>
public sealed partial class Agent : IAsyncDisposable
{
    internal ProviderComposition? ProviderComposition => _chatClientResolver.Composition;

    private readonly IChatClient? _baseClient;
    private readonly AgentChatClientHandle? _defaultChatClientHandle;
    private readonly AgentChatClientResolver _chatClientResolver;
    private readonly AgentProviderProfileIndex? _providerProfileIndex;
    private readonly ProviderClientManager<ITextToSpeechClient> _textToSpeechClientManager = new();
    private readonly ProviderClientManager<ISpeechToTextClient> _speechToTextClientManager = new();
    private readonly ProviderClientManager<IRealtimeClient> _realtimeClientManager = new();
    private readonly ProviderClientManager<IImageGenerator> _imageGeneratorManager = new();
    private readonly ProviderClientManager<IEmbeddingGenerator> _embeddingGeneratorManager = new();
    private readonly ProviderClientManager<IHostedFileClient> _hostedFileClientManager = new();
    private readonly InMemorySessionStore _ephemeralEventJournal;
    private readonly AgentClientSet? _clientSet;
    private readonly IAsyncDisposable? _providerRuntimeOwner;
    private readonly string _name;
    private readonly ChatClientMetadata _metadata;
    // OpenTelemetry Activity Source for telemetry
    private static readonly ActivitySource ActivitySource = new("HPD.Agent");
    // V2: AgentContext is now passed through middleware hooks, no need for AsyncLocal storage
    // AsyncLocal storage for root agent tracking in nested agent calls
    private static readonly AsyncLocal<Agent?> _rootAgent = new();
    //  CurrentSession AsyncLocal removed. Session/Thread are now passed explicitly to RunAsync.
    // If ambient access is needed, use AgentContext.Session/AgentContext.Thread in middleware.

    // Specialized component fields for delegation
    private readonly MessageProcessor _messageProcessor;
    private readonly FunctionExecutionCore _functionExecutionCore;
    private readonly FunctionCallProcessor _functionCallProcessor;
    private readonly AgentTurn _agentTurn;
    private readonly ChatModelTurnExecutor _chatModelTurnExecutor;
    private readonly RealtimeProviderProtocolParticipantV1 _realtimeProviderProtocolParticipant;
    private readonly HPD.Events.IEventCoordinator _eventCoordinator;
    private readonly StructEventHub _structEvents = new();
    private readonly IReadOnlyList<IDisposable> _eventSubscriptions;
    // Unified middleware pipeline
    private readonly AgentMiddlewarePipeline _middlewarePipeline;
    private readonly AgentInputDispatcher _inputDispatcher;
    private readonly object _structHandlerLock = new();
    private readonly List<RuntimeStructHandlerSubscription> _structHandlerSubscriptions = new();
    private readonly object _runtimeLock = new();
    private Channel<AgentInputEvent>? _runtimeInbox;
    private AgentWorkScheduler? _runtimeWorkScheduler;
    private CancellationTokenSource? _runtimeCts;
    private Task? _runtimeLoopTask;
    private Middleware.AgentRuntimeContext? _runtimeContext;
    private HPD.Events.IEventCoordinator? _runtimeEventCoordinator;
    private StructEventHub? _runtimeStructEvents;
    private AgentOperationNotificationDispatcher? _runtimeNotificationDispatcher;
    private bool _runtimeStarting;
    private bool _runtimeStopping;
    private ActiveRuntimeInput? _activeRuntimeInput;
    private readonly Dictionary<AgentInputEvent, TaskCompletionSource<AgentRuntimeInputOutcome>> _runtimeInputCompletions =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<AgentInputEvent> _cancelledRuntimeInputs =
        new(ReferenceEqualityComparer.Instance);
    private readonly ILogger? _agentLogger;

    // Provider registry for runtime provider switching via AgentRunConfig.ProviderKey/ModelId
    private readonly Providers.IProviderRegistry? _providerRegistry;

    // Store and generated-policy archiver used for event content retention.
    private readonly IContentStore? _contentStore;
    private readonly bool _ownsSessionStore;
    private readonly bool _ownsContentStore;
    private readonly IAgentEventContentArchiver _eventContentArchiver;

    // Service provider for creating new clients
    private readonly IServiceProvider? _serviceProvider;

    // Middleware state factories for cross-assembly state discovery
    // Passed from AgentBuilder, used for session persistence and schema validation
    private readonly ImmutableDictionary<string, MiddlewareStateFactory> _stateFactories;

    // HttpClients created by AgentBuilder for OpenAPI sources that did not provide their own.
    // Disposed when the Agent is disposed.
    private readonly IReadOnlyList<HttpClient>? _ownedHttpClients;
    private AgentCapabilityCatalog? _capabilityCatalog;
    private IReadOnlyList<IAgentCapabilitySource> _capabilitySources = [];
    private readonly AgentOperationRegistry _operationRegistry;
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _activeTurnCancellations = new();
    private readonly ConcurrentDictionary<long, Task> _toolHarnessExecutionCompletions = new();
    private readonly AgentResourceRegistry _agentResources;
    private long _nextActiveTurnId;
    private long _nextToolHarnessExecutionId;
    private readonly ContainerMiddleware? _containerMiddleware;
    private CancellationTokenSource? _skillWatchCancellation;
    private IReadOnlyList<Task> _skillWatchTasks = [];
    private int _disposeState;
    private readonly TaskCompletionSource _disposeCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task<bool> DrainAcceptedTurnContinuationsAsync(
        ActiveRuntimeInput? activeInput,
        List<ChatMessage> sharedMessages,
        List<ChatMessage> turnHistory,
        Session? session,
        Thread? thread,
        HPD.Events.IEventCoordinator eventCoordinator,
        CancellationToken cancellationToken)
    {
        if (activeInput is null)
            return false;

        var drained = false;
        while (activeInput.Continuations.Reader.TryRead(out var accepted))
        {
            var messages = accepted.Messages.ToArray();
            if (accepted.OperationNotification is null)
            {
                foreach (var message in messages)
                {
                    EnsureMessageIdentity(message);
                    message.WithPolicy(
                        AgentMessageSource.Steering,
                        AgentMessageVisibility.Transcript,
                        AgentMessagePersistence.ThreadHistory);
                }
            }
            else
                foreach (var message in messages) EnsureMessageIdentity(message);

            await CommitThreadMessagesAsync(
                    session,
                    thread,
                    messages,
                    accepted.ClientInputId,
                    eventCoordinator,
                    cancellationToken)
                .ConfigureAwait(false);
            sharedMessages.AddRange(messages);
            turnHistory.AddRange(messages);
            if (accepted.OperationNotification is not null)
            {
                await PublishAgentOperationNotificationDeliveredAsync(
                        accepted.OperationNotification,
                        eventCoordinator,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            drained = true;
        }

        return drained;
    }

    private bool TryFinishActiveInput(ActiveRuntimeInput? activeInput)
    {
        if (activeInput is null)
            return true;

        lock (_runtimeLock)
        {
            if (!ReferenceEquals(_activeRuntimeInput, activeInput))
                return true;

            activeInput.State = ActiveRuntimeInputState.Finishing;
            if (activeInput.Continuations.Reader.TryPeek(out _))
            {
                activeInput.State = ActiveRuntimeInputState.Accepting;
                return false;
            }

            activeInput.State = ActiveRuntimeInputState.Finished;
            return true;
        }
    }

    /// <summary>
    /// Agent configuration object containing all settings
    /// </summary>
    public AgentConfig? Config { get; private set; }

    /// <summary>
    /// Gets the base chat client used by this agent.
    /// This can be used by SubAgents to inherit the parent's client when no provider is specified.
    /// </summary>
    public IChatClient BaseClient => _baseClient
        ?? throw new InvalidOperationException(
            "This agent does not have a default chat client. Configure a provider/model on the agent or pass one in AgentRunConfig.");

    /// <summary>
    /// Gets the middleware state factories registered for this agent.
    /// Used for session persistence, schema validation, and cross-assembly state discovery.
    /// </summary>
    internal IReadOnlyDictionary<string, MiddlewareStateFactory> StateFactories => _stateFactories;

    /// <summary>
    /// Gets or sets the root agent in the current execution chain.
    /// Returns null if no root agent is set (single-agent execution).
    /// </summary>
    public static Agent? RootAgent
    {
        get => _rootAgent.Value;
        internal set => _rootAgent.Value = value;
    }

    //  CurrentSession property removed. Use Session/Thread passed explicitly via RunAsync parameters.
    // In middleware, access via AgentContext.Session and AgentContext.Thread.

    /// <summary>
    /// Metadata about this chat client, compatible with Microsoft.Extensions.AI patterns
    /// </summary>
    public ChatClientMetadata Metadata => _metadata;

    /// <summary>
    /// Provider from the configuration
    /// </summary>
    public string? ProviderKey => Config?.ResolveClientConfig(Providers.ProviderClientFamily.Chat)?.Provider?.Key;

    /// <summary>
    /// Model ID from the configuration
    /// </summary>
    public string? ModelId => Config?.ResolveClientConfig(Providers.ProviderClientFamily.Chat)?.ModelName;

    /// <summary>
    /// Live metadata for this agent (agent name, ID, hierarchy).
    /// Set during agent initialization to enable event attribution in multi-agent systems.
    /// </summary>
    private AgentMetadata? _agentMetadata;
    public AgentMetadata? AgentMetadata
    {
        get => _agentMetadata;
        set { _agentMetadata = value; }
    }

    /// <summary>
    /// Internal access to event coordinator for context setup and nested agent configuration.
    /// </summary>
    public HPD.Events.IEventCoordinator EventCoordinator => _eventCoordinator;

    /// <summary>
    /// Process-local realtime struct event hub owned by this agent.
    /// </summary>
    public IStructEventHub StructEvents => _structEvents;

    /// <summary>
    /// Internal access to event coordinator for middleware event emission.
    /// Use Emit() method for channel-aware routing.
    /// </summary>
    internal HPD.Events.IEventCoordinator MiddlewareEventCoordinator => _eventCoordinator;

    /// <summary>
    /// Sets the execution context for event attribution.
    /// Called when the execution context is lazily initialized (e.g., on first RunAsync).
    /// Thread-safe: Can be called from any session.
    /// </summary>
    /// <param name="executionContext">The execution context to attach to events</param>

    /// <summary>
    /// Extracts and merges ChatOptions from AgentRunConfig (for workflow compatibility).
    /// Preserves workflow-provided tools (e.g., handoff functions) while injecting conversation context.
    /// </summary>
    /// <param name="workflowOptions">Options from workflow (may contain handoff tools)</param>
    /// <param name="conversationContext">Additional context to inject (e.g., ConversationId)</param>
    /// <returns>Merged ChatOptions ready for agent execution</returns>
    private AgentEventPublisher CreateEventPublisher(
        ISessionStore store,
        HPD.Events.IEventCoordinator coordinator) =>
        new(store, coordinator, _eventContentArchiver);

    /// <summary>
    /// Initializes a new Agent instance from an AgentConfig object
    /// </summary>
    public Agent(
        AgentConfig config,
        IChatClient? baseClient,
        ChatOptions? mergedOptions,
        IReadOnlyDictionary<string, string>? functionToToolHarnessMap = null,
        IReadOnlyDictionary<string, string>? functionToSkillMap = null,
        IReadOnlyList<IAgentMiddleware>? middlewares = null,
        IServiceProvider? serviceProvider = null,
        IEnumerable<Func<HPD.Events.IEventCoordinator, IDisposable>>? eventSubscriptionFactories = null,
        Providers.IProviderRegistry? providerRegistry = null,
        IContentStore? contentStore = null,
        IReadOnlyDictionary<string, MiddlewareStateFactory>? stateFactories = null,
        IReadOnlyList<HttpClient>? ownedHttpClients = null,
        AgentClientSet? clientSet = null,
        IAsyncDisposable? providerRuntimeOwner = null,
        bool ownsSessionStore = false,
        bool ownsContentStore = false,
        IAsyncDisposable? agentResourceOwner = null)
    {
        _providerRegistry = providerRegistry;
        _contentStore = contentStore;
        _ownsSessionStore = ownsSessionStore;
        _ownsContentStore = ownsContentStore;
        _eventContentArchiver = new AgentEventContentArchiver(
            contentStore,
            diagnostic =>
            {
                _structEvents.Route<AgentEventArchiveDiagnostic>().CreateEmitter().Emit(in diagnostic);
                _agentLogger?.LogDebug(
                    diagnostic.Exception,
                    "Agent event content archival skipped or failed for {EventType}: {Reason}",
                    diagnostic.EventType.Name,
                    diagnostic.Reason);
            });
        _serviceProvider = serviceProvider;
        _stateFactories = stateFactories?.ToImmutableDictionary()
            ?? ImmutableDictionary<string, MiddlewareStateFactory>.Empty;
        _ownedHttpClients = ownedHttpClients;
        Config = config ?? throw new ArgumentNullException(nameof(config));
        _ephemeralEventJournal = new InMemorySessionStore(
            config.EventComposition?.Codec
            ?? throw new InvalidOperationException("AgentConfig.EventComposition must be resolved before Agent construction."));
        _clientSet = clientSet;
        _providerRuntimeOwner = providerRuntimeOwner;
        _agentResources = agentResourceOwner as AgentResourceRegistry ?? new AgentResourceRegistry([]);
        _baseClient = clientSet?.Chat ?? baseClient;
        _defaultChatClientHandle = _baseClient is null
            ? null
            : AgentChatClientHandle.Borrowed(
                _baseClient,
                AgentChatClientSource.BuilderDefault,
                clientSet?.GetResolvedConfig(Providers.ProviderClientFamily.Chat),
                clientSet?.GetExecutionIdentity(Providers.ProviderClientFamily.Chat));
        _chatClientResolver = new AgentChatClientResolver(providerRegistry, serviceProvider);
        _providerProfileIndex = _chatClientResolver.Composition is null
            ? null
            : AgentProviderProfileIndex.Create(config, _chatClientResolver.Composition);
        _name = config.Name ?? "Agent"; // Default to "Agent" to prevent null dictionary key exceptions

        // Initialize unified middleware pipeline
        // Note: Error handler is now passed directly to FunctionRetryMiddleware, not stored here
        if (Config.ErrorHandling == null) Config.ErrorHandling = new ErrorHandlingConfig();


        // Initialize Microsoft.Extensions.AI compliance metadata
        var chatConfig = config.ResolveClientConfig(Providers.ProviderClientFamily.Chat);
        _metadata = new ChatClientMetadata(
            providerName: chatConfig?.Provider?.Key,
            providerUri: null,
            defaultModelId: chatConfig?.ModelName
        );

        // Initialize unified middleware pipeline
        _middlewarePipeline = new AgentMiddlewarePipeline(middlewares ?? Array.Empty<IAgentMiddleware>());
        _containerMiddleware = middlewares?.OfType<ContainerMiddleware>().SingleOrDefault();
        _inputDispatcher = new AgentInputDispatcher(_middlewarePipeline);

        // Create event coordinator for Middleware events and human-in-the-loop
        // Direct use of HPD.Events.EventCoordinator (no wrapper)
        _eventCoordinator = new HPD.Events.Core.EventCoordinator();
        var operationThreadEvents = Config.SessionStore is null
            ? null
            : CreateEventPublisher(Config.SessionStore, _eventCoordinator);
        _operationRegistry = new AgentOperationRegistry(
            new AgentOperationEventSink(_eventCoordinator, operationThreadEvents),
            Config.OperationRetention);

        // Plan mode instructions now injected by AgentPlanAgentMiddleware (middleware-based)
        _messageProcessor = new MessageProcessor(
            config.SystemInstructions, // Use base instructions; middleware adds plan mode guidance
            mergedOptions ?? (chatConfig as ChatClientConfig)?.ToMicrosoftChatOptions());
        _functionExecutionCore = new FunctionExecutionCore(
            _middlewarePipeline,
            config.ErrorHandling,
            config.ServerConfiguredTools,
            config.AgenticLoop);
        _functionCallProcessor = new FunctionCallProcessor(
            _eventCoordinator, // Pass IEventCoordinator for decoupled event emission
            _middlewarePipeline, // Pass unified middleware pipeline for permission checks
            _functionExecutionCore,
            config.MaxAgenticIterations,
            config.ErrorHandling,
            config.ServerConfiguredTools,
            config.AgenticLoop,  // Pass agentic loop config for TerminateOnUnknownCalls
            _name,
            _stateFactories);
        _agentTurn = new AgentTurn(
            _baseClient,
            config.ConfigureOptions,
            config.ClientMiddleware?.Chat,
            serviceProvider);
        _chatModelTurnExecutor = new ChatModelTurnExecutor(_agentTurn);
        _realtimeProviderProtocolParticipant = new RealtimeProviderProtocolParticipantV1();

        // Resolve optional dependencies from service provider
        var loggerFactory = serviceProvider?.GetService(typeof(ILoggerFactory))
            as ILoggerFactory;

        _agentLogger = loggerFactory?.CreateLogger<Agent>();
        var subscriptions = eventSubscriptionFactories?
            .Select(factory => factory(_eventCoordinator))
            .ToList()
            ?? [];

        _eventSubscriptions = subscriptions;
    }

    /// <summary>
    /// Stable agent identity used for store lookup, hosted routing, and observability.
    /// Falls back to <see cref="Name"/> for non-store-backed agents.
    /// </summary>
    public string AgentId => Config?.AgentId ?? _name;

    /// <summary>
    /// Agent name
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// System instructions/persona
    /// </summary>
    public string? SystemInstructions => Config?.SystemInstructions ?? _messageProcessor.SystemInstructions;

    /// <summary>
    /// Default chat options
    /// </summary>
    public ChatOptions? DefaultOptions => _messageProcessor.DefaultOptions;

    internal void SetCapabilityCatalog(
        AgentCapabilityCatalog catalog,
        IEnumerable<(ISkillSource Source, SkillSourceContext Context)> sources,
        IReadOnlyList<IAgentCapabilitySource>? capabilitySources = null)
    {
        _capabilityCatalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _capabilitySources = capabilitySources ?? [];
        ArgumentNullException.ThrowIfNull(sources);
        var watchable = sources
            .Where(entry => entry.Source is IWatchableSkillSource)
            .ToArray();
        if (watchable.Length == 0 && _capabilitySources.Count == 0)
            return;
        _skillWatchCancellation = new CancellationTokenSource();
        _skillWatchTasks = watchable.Select(entry => WatchSkillSourceAsync(
            (IWatchableSkillSource)entry.Source,
            entry.Context,
            _skillWatchCancellation.Token))
            .Concat(_capabilitySources.Select(source => WatchCapabilitySourceAsync(
                source,
                _skillWatchCancellation.Token)))
            .ToArray();
    }

    private async Task WatchCapabilitySourceAsync(
        IAgentCapabilitySource source,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var enumerator = source.WatchAsync(cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            Task<bool> pendingInvalidation = enumerator.MoveNextAsync().AsTask();
            while (await pendingInvalidation.ConfigureAwait(false))
            {
                while (true)
                {
                    pendingInvalidation = enumerator.MoveNextAsync().AsTask();
                    var quietWindow = Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
                    var completed = await Task.WhenAny(pendingInvalidation, quietWindow).ConfigureAwait(false);
                    if (completed != pendingInvalidation)
                        break;
                    if (!await pendingInvalidation.ConfigureAwait(false))
                        break;
                }
                await RefreshCapabilitiesAsync("source-invalidation", cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _agentLogger?.LogWarning(
                exception,
                "Capability source watcher {SourceId} stopped; catalog epoch {Epoch} remains active.",
                source.Id,
                CapabilityEpoch);
        }
    }

    private async Task WatchSkillSourceAsync(
        IWatchableSkillSource source,
        SkillSourceContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var enumerator = source.WatchAsync(context, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            Task<bool> pendingChange = enumerator.MoveNextAsync().AsTask();
            while (await pendingChange.ConfigureAwait(false))
            {
                // Filesystem backends commonly emit several notifications for one logical write.
                // Reconciliation begins only after a quiet window, so the candidate publishes once.
                while (true)
                {
                    pendingChange = enumerator.MoveNextAsync().AsTask();
                    var quietWindow = Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
                    var completed = await Task.WhenAny(pendingChange, quietWindow).ConfigureAwait(false);
                    if (completed != pendingChange)
                        break;
                    if (!await pendingChange.ConfigureAwait(false))
                        break;
                }

                await RefreshCapabilitiesAsync("watch", cancellationToken).ConfigureAwait(false);
                if (pendingChange.IsCompletedSuccessfully && !pendingChange.Result)
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // A watcher is advisory. Preserve the last-known-good catalog and keep failures
            // bounded to host diagnostics; explicit capability refresh remains available.
            _agentLogger?.LogWarning(
                exception,
                "Skill source watcher stopped for harness {ToolHarness}; catalog epoch {Epoch} remains active.",
                context.OwnerToolHarnessName,
                CapabilityEpoch);
        }
    }

    /// <summary>Rereads every registered dynamic source and atomically publishes a complete validated catalog.</summary>
    public async ValueTask<AgentCapabilityRefreshResult> RefreshCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfShutdownStarted();
        return await RefreshCapabilitiesAsync("manual", cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<AgentCapabilityRefreshResult> RefreshCapabilitiesAsync(
        string reason,
        CancellationToken cancellationToken)
    {
        if (_capabilityCatalog is null)
            return new AgentCapabilityRefreshResult(false, 0, "This agent has no capability catalog.");

        var previousEpoch = CapabilityEpoch;
        var refreshStarted = EnrichOutputEvent(new AgentCapabilityRefreshStartedEvent(previousEpoch, reason));
        await _eventCoordinator.EmitAsync(refreshStarted, AgentEventRoutes.Create(refreshStarted), cancellationToken).ConfigureAwait(false);
        var result = await _capabilityCatalog.RefreshAsync(reason, cancellationToken).ConfigureAwait(false);
        if (!result.Published)
        {
            var refreshRejected = EnrichOutputEvent(new AgentCapabilityRefreshRejectedEvent(
                    result.Epoch,
                    BoundCapabilityRefreshError(result.Error),
                    reason));
            await _eventCoordinator.EmitAsync(refreshRejected, AgentEventRoutes.Create(refreshRejected), cancellationToken).ConfigureAwait(false);
            return result;
        }

        await using var lease = _capabilityCatalog.Acquire();
        _messageProcessor.ReplaceCapabilityFunctions(lease.Snapshot.Functions);
        _containerMiddleware?.ReplaceCapabilityFunctions(lease.Snapshot.Functions);
        var refreshPublished = EnrichOutputEvent(new AgentCapabilityRefreshPublishedEvent(
                previousEpoch,
                result.Epoch,
                reason));
        await _eventCoordinator.EmitAsync(refreshPublished, AgentEventRoutes.Create(refreshPublished), cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static string BoundCapabilityRefreshError(string? error)
    {
        const int maximumLength = 512;
        var bounded = string.IsNullOrWhiteSpace(error)
            ? "The replacement capability catalog failed validation."
            : error.Replace('\r', ' ').Replace('\n', ' ');
        return bounded.Length <= maximumLength ? bounded : bounded[..maximumLength];
    }

    /// <summary>Gets the currently published complete capability epoch.</summary>
    public long CapabilityEpoch => _capabilityCatalog?.CurrentEpoch ?? -1;

    /// <summary>
    /// Gets whether this agent currently has a continuous runtime input loop.
    /// </summary>
    public bool IsRunning
    {
        get
        {
            lock (_runtimeLock)
            {
                return !_runtimeStopping &&
                    _runtimeInbox != null &&
                    _runtimeLoopTask is { IsCompleted: false };
            }
        }
    }

    /// <summary>
    /// Agent middlewares applied to the agent lifecycle (message turns, iterations, functions).
    /// These are the unified IAgentMiddleware instances with built-in Collapsing support.
    /// </summary>
    public IReadOnlyList<IAgentMiddleware> Middlewares =>
        _middlewarePipeline.Middlewares;

    private AgentMiddlewarePipeline BuildTurnMiddlewarePipeline(AgentRunConfig runConfig)
    {
        if (runConfig.RuntimeMiddleware is not { Count: > 0 })
            return _middlewarePipeline;

        var middlewares = new List<IAgentMiddleware>(
            _middlewarePipeline.Middlewares.Count + runConfig.RuntimeMiddleware.Count);
        middlewares.AddRange(runConfig.RuntimeMiddleware);
        middlewares.AddRange(_middlewarePipeline.Middlewares);
        return new AgentMiddlewarePipeline(middlewares);
    }

    private FunctionCallProcessor CreateFunctionCallProcessorForPipeline(
        HPD.Events.IEventCoordinator eventCoordinator,
        AgentMiddlewarePipeline pipeline)
    {
        var functionExecutionCore = ReferenceEquals(pipeline, _middlewarePipeline)
            ? _functionExecutionCore
            : new FunctionExecutionCore(
                pipeline,
                Config?.ErrorHandling,
                Config?.ServerConfiguredTools,
                Config?.AgenticLoop);

        return new FunctionCallProcessor(
            eventCoordinator,
            pipeline,
            functionExecutionCore,
            Config?.MaxAgenticIterations ?? 10,
            Config?.ErrorHandling,
            Config?.ServerConfiguredTools,
            Config?.AgenticLoop,
            _name,
            _stateFactories);
    }

    private sealed class RuntimeStructHandlerSubscription(
        Agent agent,
        IDisposable innerSubscription,
        CancellationTokenSource drainCts,
        Task drainTask) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            innerSubscription.Dispose();
            drainCts.Cancel();
            drainCts.Dispose();
            agent.RemoveStructHandlerSubscription(this);
        }
    }

    private void RemoveStructHandlerSubscription(RuntimeStructHandlerSubscription subscription)
    {
        lock (_structHandlerLock)
        {
            _structHandlerSubscriptions.Remove(subscription);
        }
    }

    /// <summary>
    /// Registers a removable ordered subscriber for this agent owner's events.
    /// </summary>
    /// <remarks>
    /// Same-agent runtime events remain visible after bubbling, while events originating from
    /// independently owned subagents are excluded. Threadless same-owner events are included.
    /// The callback runs on a subscriber pump; publication does not await callback completion.
    /// Disposal stops observation without stopping execution or bubbling.
    /// </remarks>
    public IDisposable Subscribe<TEvent>(Func<TEvent, ValueTask> handler)
        where TEvent : AgentEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _eventCoordinator.Subscribe(handler);
    }

    /// <summary>Subscribes to events whose complete session/thread key equals <paramref name="thread"/>.</summary>
    /// <remarks>Threadless and descendant events are excluded. Callback execution is asynchronous to publication; disposal affects observation only.</remarks>
    public IDisposable Subscribe<TEvent>(ThreadKey thread, Func<TEvent, ValueTask> handler)
        where TEvent : AgentEvent =>
        Subscribe(thread, AgentEventHierarchy.ExactThread, handler);

    /// <summary>Subscribes to events in an explicitly selected hierarchy rooted at <paramref name="anchor"/>.</summary>
    /// <remarks>Matching uses the complete session/thread key, is transitive where selected, and excludes sibling branches. Live child events keep their own journal identity. Disposal affects observation only.</remarks>
    public IDisposable Subscribe<TEvent>(ThreadKey anchor, AgentEventHierarchy hierarchy, Func<TEvent, ValueTask> handler)
        where TEvent : AgentEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        ValidateThreadKey(anchor);
        return _eventCoordinator.Subscribe(handler, CreateHierarchyOptions(anchor, hierarchy));
    }

    /// <summary>
    /// Registers a removable ordered task subscriber for same-owner events, including threadless events but excluding independently owned descendants.
    /// </summary>
    public IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler)
        where TEvent : AgentEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Subscribe<TEvent>(evt => new ValueTask(handler(evt)));
    }

    /// <summary>Subscribes a task callback to events originating from exactly <paramref name="thread"/>.</summary>
    public IDisposable Subscribe<TEvent>(ThreadKey thread, Func<TEvent, Task> handler)
        where TEvent : AgentEvent => Subscribe<TEvent>(thread, AgentEventHierarchy.ExactThread, evt => new ValueTask(handler(evt)));

    /// <summary>Subscribes a task callback to an explicitly selected thread hierarchy.</summary>
    public IDisposable Subscribe<TEvent>(ThreadKey anchor, AgentEventHierarchy hierarchy, Func<TEvent, Task> handler)
        where TEvent : AgentEvent => Subscribe<TEvent>(anchor, hierarchy, evt => new ValueTask(handler(evt)));

    /// <summary>
    /// Registers a removable ordered action subscriber for same-owner events, including threadless events but excluding independently owned descendants.
    /// </summary>
    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : AgentEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Subscribe<TEvent>(evt =>
        {
            handler(evt);
            return ValueTask.CompletedTask;
        });
    }

    /// <summary>Subscribes an action to events originating from exactly <paramref name="thread"/>.</summary>
    public IDisposable Subscribe<TEvent>(ThreadKey thread, Action<TEvent> handler)
        where TEvent : AgentEvent => Subscribe<TEvent>(thread, AgentEventHierarchy.ExactThread, evt => handler(evt));

    /// <summary>Subscribes an action to an explicitly selected thread hierarchy.</summary>
    public IDisposable Subscribe<TEvent>(ThreadKey anchor, AgentEventHierarchy hierarchy, Action<TEvent> handler)
        where TEvent : AgentEvent => Subscribe<TEvent>(anchor, hierarchy, evt =>
        {
            handler(evt);
            return ValueTask.CompletedTask;
        });

    /// <summary>
    /// Registers a removable ordered catch-all subscriber for same-owner events.
    /// </summary>
    /// <remarks>Threadless same-owner events are included; independently owned descendant events require a keyed hierarchy or explicit infrastructure scope. Callbacks run on a pump and disposal affects observation only.</remarks>
    public IDisposable SubscribeAny(Func<AgentEvent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _eventCoordinator.Subscribe<AgentEvent>(handler);
    }

    /// <summary>Subscribes to every agent event originating from exactly <paramref name="thread"/>.</summary>
    public IDisposable SubscribeAny(ThreadKey thread, Func<AgentEvent, ValueTask> handler) =>
        SubscribeAny(thread, AgentEventHierarchy.ExactThread, handler);

    /// <summary>Subscribes to every agent event in an explicitly selected thread hierarchy.</summary>
    public IDisposable SubscribeAny(ThreadKey anchor, AgentEventHierarchy hierarchy, Func<AgentEvent, ValueTask> handler) =>
        Subscribe(anchor, hierarchy, handler);

    /// <summary>
    /// Registers a removable ordered catch-all task subscriber for same-owner events.
    /// </summary>
    public IDisposable SubscribeAny(Func<AgentEvent, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return SubscribeAny(evt => new ValueTask(handler(evt)));
    }

    /// <summary>Subscribes a task callback to every event from exactly one thread.</summary>
    public IDisposable SubscribeAny(ThreadKey thread, Func<AgentEvent, Task> handler) =>
        SubscribeAny(thread, AgentEventHierarchy.ExactThread, evt => new ValueTask(handler(evt)));

    /// <summary>Subscribes a task callback to every event in a selected hierarchy.</summary>
    public IDisposable SubscribeAny(ThreadKey anchor, AgentEventHierarchy hierarchy, Func<AgentEvent, Task> handler) =>
        SubscribeAny(anchor, hierarchy, evt => new ValueTask(handler(evt)));

    /// <summary>
    /// Registers a removable ordered catch-all action subscriber for same-owner events.
    /// </summary>
    public IDisposable SubscribeAny(Action<AgentEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return SubscribeAny(evt =>
        {
            handler(evt);
            return ValueTask.CompletedTask;
        });
    }

    /// <summary>Subscribes an action to every event from exactly one thread.</summary>
    public IDisposable SubscribeAny(ThreadKey thread, Action<AgentEvent> handler) =>
        SubscribeAny(thread, AgentEventHierarchy.ExactThread, handler);

    /// <summary>Subscribes an action to every event in a selected hierarchy.</summary>
    public IDisposable SubscribeAny(ThreadKey anchor, AgentEventHierarchy hierarchy, Action<AgentEvent> handler) =>
        SubscribeAny(anchor, hierarchy, evt =>
        {
            handler(evt);
            return ValueTask.CompletedTask;
        });

    /// <summary>Creates a caller-owned inbox for one complete session/thread key.</summary>
    /// <remarks>Threadless events and descendants are excluded. Disposal completes observation without affecting execution or bubbling.</remarks>
    public HPD.Events.EventInbox<TEvent> CreateEventInbox<TEvent>(
        ThreadKey thread,
        HPD.Events.EventInboxOptions? options = null)
        where TEvent : AgentEvent =>
        CreateEventInbox<TEvent>(thread, AgentEventHierarchy.ExactThread, options);

    /// <summary>Creates a caller-owned inbox for a selected thread hierarchy.</summary>
    /// <remarks>Transitive selections retain branch isolation and each event's own thread journal identity. Source order is retained per origin; no global sibling ordering is promised.</remarks>
    public HPD.Events.EventInbox<TEvent> CreateEventInbox<TEvent>(
        ThreadKey anchor,
        AgentEventHierarchy hierarchy,
        HPD.Events.EventInboxOptions? options = null)
        where TEvent : AgentEvent
    {
        ValidateThreadKey(anchor);
        ValidateHierarchy(hierarchy);
        if (_eventCoordinator is not HPD.Events.Core.EventCoordinator coordinator)
            throw new NotSupportedException("Routed agent inboxes require the built-in EventCoordinator.");
        return coordinator.CreateFilteredInbox<TEvent>(
            HPD.Events.EventOwnerScope.AllOwners,
            new AgentHierarchyDeliveryPolicy(anchor, hierarchy),
            options);
    }

    internal HPD.Events.DeliveryInbox<AgentEventDelivery> CreateEventDeliveryInbox(
        ThreadKey anchor,
        AgentEventHierarchy hierarchy,
        HPD.Events.EventInboxOptions? options = null)
    {
        ValidateThreadKey(anchor);
        ValidateHierarchy(hierarchy);
        if (_eventCoordinator is not HPD.Events.Core.EventCoordinator coordinator)
            throw new NotSupportedException("Routed agent inboxes require the built-in EventCoordinator.");
        return coordinator.CreateProjectedDeliveryInbox<AgentEvent, AgentEventDelivery>(
            HPD.Events.EventOwnerScope.AllOwners,
            new AgentHierarchyDeliveryPolicy(anchor, hierarchy),
            AgentDeliveryProjector.Instance,
            options);
    }

    /// <summary>Returns pending requests whose immutable routes match a selected thread hierarchy.</summary>
    /// <param name="anchor">The exact thread or hierarchy root to inspect.</param>
    /// <param name="hierarchy">The relative hierarchy included in the result.</param>
    public IReadOnlyList<HPD.Events.PendingRequestSnapshot> GetPendingRequests(
        ThreadKey anchor,
        AgentEventHierarchy hierarchy = AgentEventHierarchy.ExactThread)
    {
        ValidateThreadKey(anchor);
        ValidateHierarchy(hierarchy);
        var policy = new AgentHierarchyDeliveryPolicy(anchor, hierarchy);
        return _eventCoordinator.GetPendingRequests()
            .Where(snapshot => snapshot.Request is AgentEvent && policy.Includes(snapshot.Delivery))
            .ToArray();
    }

    private static HPD.Events.EventSubscriptionOptions CreateHierarchyOptions(
        ThreadKey anchor,
        AgentEventHierarchy hierarchy)
    {
        ValidateHierarchy(hierarchy);
        return new HPD.Events.EventSubscriptionOptions
        {
            OwnerScope = HPD.Events.EventOwnerScope.AllOwners,
            DeliveryPolicy = new AgentHierarchyDeliveryPolicy(anchor, hierarchy)
        };
    }

    private static void ValidateHierarchy(AgentEventHierarchy hierarchy)
    {
        if (!Enum.IsDefined(hierarchy))
            throw new ArgumentOutOfRangeException(nameof(hierarchy), hierarchy, "Unknown agent event hierarchy.");
    }

    private static void ValidateThreadKey(ThreadKey thread)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thread.SessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(thread.ThreadId);
    }

    /// <summary>
    /// Registers a removable handler for a process-local struct event type.
    /// Agent owns the subscription pump; callers do not need to run the event coordinator.
    /// </summary>
    public IDisposable ObserveStruct<TEvent>(Func<TEvent, ValueTask> handler)
        where TEvent : struct, AgentStructEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        var subscription = _structEvents.Route<TEvent>().Subscribe();
        var drainCts = new CancellationTokenSource();
        var drainTask = Task.Run(async () =>
        {
            while (!drainCts.IsCancellationRequested)
            {
                try
                {
                    if (!subscription.TryRead(out var evt))
                    {
                        await Task.Delay(1, drainCts.Token).ConfigureAwait(false);
                        continue;
                    }

                    await handler(evt).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (drainCts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _agentLogger?.LogError(ex,
                        "Agent struct handler failed processing {EventType}",
                        typeof(TEvent).Name);
                }
            }
        }, drainCts.Token);
        var runtimeSubscription = new RuntimeStructHandlerSubscription(
            this,
            subscription,
            drainCts,
            drainTask);

        lock (_structHandlerLock)
        {
            _structHandlerSubscriptions.Add(runtimeSubscription);
        }

        return runtimeSubscription;
    }

    /// <summary>
    /// Registers a removable handler for a process-local struct event type.
    /// Agent owns the subscription pump; callers do not need to run the event coordinator.
    /// </summary>
    public IDisposable ObserveStruct<TEvent>(Action<TEvent> handler)
        where TEvent : struct, AgentStructEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        return ObserveStruct<TEvent>(evt =>
        {
            handler(evt);
            return ValueTask.CompletedTask;
        });
    }

    // ── Span ID helpers ───────────────────────────────────────────────────────

    /// <summary>Generates a 128-bit OTel-compatible trace ID (32 lowercase hex chars).</summary>
    private static string GenerateTraceId()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>Generates a 64-bit OTel-compatible span ID (16 lowercase hex chars).</summary>
    private static string GenerateSpanId()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

    // ─────────────────────────────────────────────────────────────────────────

    private AgentEvent EnrichOutputEvent(AgentEvent evt)
    {
        var threadExecutionId = _activeRuntimeInput?.ThreadExecutionId;
        return evt with
        {
            Metadata = evt.Metadata ?? AgentMetadata,
            ThreadExecutionId = evt.ThreadExecutionId ?? threadExecutionId
        };
    }

    private async Task CommitThreadMessagesAsync(
        Session? session,
        Thread? thread,
        IEnumerable<ChatMessage> messages,
        string? clientInputId,
        HPD.Events.IEventCoordinator eventCoordinator,
        CancellationToken cancellationToken)
    {
        if (thread == null)
            return;

        var newMessages = new List<ChatMessage>();
        var existingIds = thread.Messages
            .Where(message => !string.IsNullOrWhiteSpace(message.MessageId))
            .Select(message => message.MessageId!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            EnsureMessageIdentity(message);

            if (existingIds.Add(message.MessageId!))
            {
                newMessages.Add(message);
            }
        }

        if (newMessages.Count == 0)
            return;

        var threadHistoryMessages = newMessages
            .Where(static message => message.GetPersistence() == AgentMessagePersistence.ThreadHistory)
            .ToArray();
        if (threadHistoryMessages.Length > 0)
            thread.AddMessages(threadHistoryMessages);

        var store = Config?.SessionStore;
        if (store == null)
            return;

        if (session != null)
        {
            session.LastActivity = thread.LastActivity;
            await store.SaveSessionAsync(session, cancellationToken).ConfigureAwait(false);
        }

        var publisher = CreateEventPublisher(store, eventCoordinator);
        foreach (var message in newMessages.Where(ShouldCommitMessageSnapshotToThread))
        {
            AgentMessagePolicy.StampDefaults(message);
            var events = ThreadMessageEventConverter.ToThreadEvents(
                    thread.SessionId,
                    thread.Id,
                    message,
                    clientInputId: clientInputId)
                .Select(EnrichOutputEvent)
                .ToArray();
            if (events.Length > 0)
            {
                await publisher.CommitAndPublishAsync(
                    new ThreadKey(thread.SessionId, thread.Id),
                    events,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ReconcileCommittedTurnHistoryAsync(
        Session? session,
        Thread? thread,
        List<ChatMessage> turnHistory,
        IReadOnlyList<string?> messageIdsBeforeAfterTurn,
        IReadOnlyDictionary<string, string> messageSnapshotsBeforeAfterTurn,
        HPD.Events.IEventCoordinator eventCoordinator,
        CancellationToken cancellationToken)
    {
        if (thread == null)
            return;

        var committedIds = messageIdsBeforeAfterTurn
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToArray();
        var committedIdSet = committedIds.ToHashSet(StringComparer.Ordinal);
        var finalizedExistingIds = turnHistory
            .Select(message => message.MessageId)
            .Where(id => id is not null && committedIdSet.Contains(id))
            .Select(id => id!)
            .ToArray();
        if (finalizedExistingIds.Length != finalizedExistingIds.Distinct(StringComparer.Ordinal).Count())
            throw new InvalidOperationException("AfterMessageTurnAsync produced duplicate committed message identities.");
        if (!committedIds.SequenceEqual(finalizedExistingIds, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "AfterMessageTurnAsync cannot remove or reorder committed messages. Append messages or replace a message while preserving its MessageId.");
        }

        var replacementMessages = new List<ChatMessage>();
        var appendedMessages = new List<ChatMessage>();

        for (var i = 0; i < turnHistory.Count; i++)
        {
            var message = turnHistory[i];
            if (string.IsNullOrWhiteSpace(message.MessageId) &&
                i < messageIdsBeforeAfterTurn.Count &&
                !string.IsNullOrWhiteSpace(messageIdsBeforeAfterTurn[i]))
            {
                message.MessageId = messageIdsBeforeAfterTurn[i];
                message.CreatedAt ??= thread.Messages.FirstOrDefault(m => m.MessageId == message.MessageId)?.CreatedAt
                    ?? DateTimeOffset.UtcNow;
            }

            EnsureMessageIdentity(message);

            var existingIndex = thread.Messages.FindIndex(existing => existing.MessageId == message.MessageId);
            if (existingIndex >= 0)
            {
                if (messageSnapshotsBeforeAfterTurn.TryGetValue(message.MessageId!, out var before) &&
                    !string.Equals(before, SerializeMessageSnapshot(message), StringComparison.Ordinal))
                {
                    thread.Messages[existingIndex] = message;
                    replacementMessages.Add(message);
                }
            }
            else
            {
                if (message.GetPersistence() != AgentMessagePersistence.ThreadHistory)
                    continue;
                thread.Messages.Add(message);
                appendedMessages.Add(message);
            }
        }

        if (replacementMessages.Count == 0 && appendedMessages.Count == 0)
            return;

        thread.LastActivity = DateTime.UtcNow;

        var store = Config?.SessionStore;
        if (store == null)
            return;

        if (session != null)
        {
            session.LastActivity = thread.LastActivity;
            await store.SaveSessionAsync(session, cancellationToken).ConfigureAwait(false);
        }

        var replacements = replacementMessages
            .Select(message => (AgentEvent)new ThreadMessageReplacedEvent(
                message.MessageId!,
                CloneMessageForThread(message),
                "after-message-turn-finalization"))
            .ToArray();
        var appendedEvents = appendedMessages
            .SelectMany(message =>
            {
                AgentMessagePolicy.StampDefaults(message);
                return ThreadMessageEventConverter.ToThreadEvents(
                        thread.SessionId,
                        thread.Id,
                        message,
                        clientInputId: null)
                    .Select(EnrichOutputEvent);
            });
        var finalizationEvents = replacements.Concat(appendedEvents).ToArray();
        if (finalizationEvents.Length > 0)
        {
            var publisher = CreateEventPublisher(store, eventCoordinator);
            await publisher.CommitAndPublishAsync(
                new ThreadKey(thread.SessionId, thread.Id),
                finalizationEvents,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private static string SerializeMessageSnapshot(ChatMessage message)
    {
        var serializable = CloneMessageForThread(message);
        serializable.AdditionalProperties = null;
        var messageJson = JsonSerializer.Serialize(
            serializable,
            Serialization.AgentEventJsonContext.Default.MicrosoftExtensionsAiChatMessage);
        if (message.AdditionalProperties is null || message.AdditionalProperties.Count == 0)
            return messageJson;

        var properties = string.Join(",", message.AdditionalProperties
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{SerializeSnapshotString(pair.Key)}:{CanonicalizeSnapshotValue(pair.Value)}"));
        return $"{messageJson}|{{{properties}}}";
    }

    private static string CanonicalizeSnapshotValue(object? value)
        => value switch
        {
            null => "null",
            string text => SerializeSnapshotString(text),
            bool boolean => boolean ? "true" : "false",
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
                => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!,
            JsonElement element => element.GetRawText(),
            System.Collections.IDictionary dictionary => "{" + string.Join(",", dictionary.Keys.Cast<object>()
                .OrderBy(key => Convert.ToString(key, System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal)
                .Select(key => $"{CanonicalizeSnapshotValue(key)}:{CanonicalizeSnapshotValue(dictionary[key])}")) + "}",
            System.Collections.IEnumerable sequence => "[" + string.Join(",", sequence.Cast<object?>().Select(CanonicalizeSnapshotValue)) + "]",
            _ => SerializeSnapshotString(value.ToString() ?? string.Empty)
        };

    private static string SerializeSnapshotString(string value) => JsonSerializer.Serialize(
        value,
        Serialization.AgentEventJsonContext.Default.String);

    private static void EnsureMessageIdentity(ChatMessage message)
    {
        message.MessageId ??= Guid.NewGuid().ToString();
        message.CreatedAt ??= DateTimeOffset.UtcNow;
    }

    private static void CollapseDuplicateMessageSnapshots(List<ChatMessage> messages)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var messageId = messages[i].MessageId;
            if (!string.IsNullOrWhiteSpace(messageId) && !seen.Add(messageId))
                messages.RemoveAt(i);
        }
    }

    private static bool ShouldCommitMessageSnapshotToThread(ChatMessage message)
        => message.GetPersistence() == AgentMessagePersistence.ThreadHistory &&
           message.Role != ChatRole.Assistant &&
           message.Role != ChatRole.Tool;

    private async Task<AgentEvent> CommitAndPublishThreadEventAsync(
        Thread? thread,
        AgentEvent evt,
        HPD.Events.IEventCoordinator eventCoordinator,
        CancellationToken cancellationToken)
    {
        evt = EnrichOutputEvent(evt);
        if (thread == null)
        {
            return await CreateEventPublisher(_ephemeralEventJournal, eventCoordinator)
                .CommitAndPublishAsync(new ThreadKey($"ephemeral:{AgentId}", "main"), evt, cancellationToken)
                .ConfigureAwait(false);
        }

        var store = Config?.SessionStore;
        if (store == null)
        {
            return await CreateEventPublisher(_ephemeralEventJournal, eventCoordinator)
                .CommitAndPublishAsync(new ThreadKey(thread.SessionId, thread.Id), evt, cancellationToken)
                .ConfigureAwait(false);
        }

        var publisher = CreateEventPublisher(store, eventCoordinator);
        return await publisher.CommitAndPublishAsync(
            new ThreadKey(thread.SessionId, thread.Id),
            evt,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendThreadFailureRuntimeEventAsync(
        Thread? thread,
        string? messageTurnId,
        string? conversationId,
        Exception exception,
        MessageTurnUsageSummary usage,
        HPD.Events.IEventCoordinator eventCoordinator)
    {
        if (string.IsNullOrWhiteSpace(messageTurnId))
            return;

        await CommitAndPublishThreadEventAsync(
            thread,
            new MessageTurnErrorEvent(messageTurnId, exception.Message, usage, exception)
            {
                ConversationId = conversationId,
                AgentId = AgentId,
                AgentName = _name,
                ErrorType = exception.GetType().FullName
            },
            eventCoordinator,
            CancellationToken.None).ConfigureAwait(false);
    }

    private async Task CommitPendingFailedProviderAttemptsAsync(
        MessageTurnUsageCollector collector,
        Thread? thread,
        string? messageTurnId,
        string? conversationId,
        int iteration,
        int inputMessageCount,
        bool isResume,
        int turnMessageCount,
        Exception exception,
        HPD.Events.IEventCoordinator eventCoordinator)
    {
        if (string.IsNullOrWhiteSpace(messageTurnId))
            return;
        var outcome = exception is OperationCanceledException
            ? ProviderOperationOutcome.Cancelled
            : ProviderOperationOutcome.Failed;
        foreach (var attempt in collector.GetPendingAttempts())
        {
            AgentEvent terminal = attempt.Family is ProviderClientFamily.Chat or ProviderClientFamily.Realtime
                ? new AgentTurnFinishedEvent(
                    messageTurnId, iteration, attempt.OperationId, attempt.LogicalOperationId, attempt.Attempt,
                    attempt.Family, outcome, null, attempt.ProviderKey, attempt.ModelId, null)
                : new ProviderOperationUsageEvent(
                    messageTurnId, attempt.OperationId, attempt.LogicalOperationId, attempt.Attempt,
                    attempt.OperationKind, attempt.Family, outcome, null,
                    attempt.ProviderKey, attempt.ModelId, null);
            await collector.CommitTerminalAsync(terminal, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static bool TryCreateProviderUsageMeasurement(
        AgentEvent evt,
        [NotNullWhen(true)] out ProviderUsageMeasurement? measurement)
    {
        measurement = evt switch
        {
            AgentTurnFinishedEvent model => new ProviderUsageMeasurement(
                model.EventId,
                model.MessageTurnId,
                model.ThreadSequenceNumber,
                model.OperationId,
                model.LogicalOperationId,
                model.Attempt,
                model.Family is ProviderClientFamily.Realtime
                    ? ProviderOperationKind.RealtimeModelResponse
                    : ProviderOperationKind.ChatModelResponse,
                model.Family,
                model.Outcome,
                model.Usage,
                model.ProviderKey,
                model.ModelId,
                model.ResponseId),
            ProviderOperationUsageEvent operation => new ProviderUsageMeasurement(
                operation.EventId,
                operation.MessageTurnId,
                operation.ThreadSequenceNumber,
                operation.OperationId,
                operation.LogicalOperationId,
                operation.Attempt,
                operation.OperationKind,
                operation.Family,
                operation.Outcome,
                operation.Usage,
                operation.ProviderKey,
                operation.ModelId,
                operation.ResponseId),
            _ => null
        };

        return measurement is not null;
    }

    private async Task<AgentEvent> CommitAgentThreadEventAsync(
        Thread? thread,
        AgentEvent evt,
        string? messageTurnId,
        string? conversationId,
        int iteration,
        int inputMessageCount,
        bool isResume,
        string? terminationReason,
        int turnMessageCount,
        HPD.Events.IEventCoordinator eventCoordinator,
        CancellationToken cancellationToken)
    {
        evt = EnrichOutputEvent(evt);
        if (thread == null)
        {
            return await CreateEventPublisher(_ephemeralEventJournal, eventCoordinator)
                .CommitAndPublishAsync(new ThreadKey($"ephemeral:{AgentId}", "main"), evt, cancellationToken)
                .ConfigureAwait(false);
        }

        var threadEvent = ThreadEventFactory.FromAgentEvent(
            thread.SessionId,
            thread.Id,
            evt,
            messageTurnId,
            conversationId,
            iteration,
            inputMessageCount,
            isResume,
            terminationReason,
            turnMessageCount);

        if (threadEvent is null)
            throw new InvalidOperationException($"Canonical event '{evt.GetType().Name}' could not be scoped to thread '{thread.Id}'.");

        return await CommitAndPublishThreadEventAsync(
            thread,
            threadEvent,
            eventCoordinator,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<AgentEvent> StageAgentThreadDeltaAsync(
        Thread thread,
        AgentEvent evt,
        string? messageTurnId,
        string? conversationId,
        int iteration,
        int inputMessageCount,
        bool isResume,
        int turnMessageCount,
        HPD.Events.IEventCoordinator eventCoordinator,
        CancellationToken cancellationToken)
    {
        var threadEvent = CreateAgentThreadEvent(
            thread, evt, messageTurnId, conversationId, iteration,
            inputMessageCount, isResume, turnMessageCount);
        var publisher = CreateEventPublisher(Config!.SessionStore!, eventCoordinator);
        return await publisher.StageAndPublishDeltaAsync(
            new ThreadKey(thread.SessionId, thread.Id), threadEvent, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AgentEvent> FinalizeAgentThreadDeltasAsync(
        Thread thread,
        AgentEvent evt,
        string? messageTurnId,
        string? conversationId,
        int iteration,
        int inputMessageCount,
        bool isResume,
        int turnMessageCount,
        HPD.Events.IEventCoordinator eventCoordinator,
        CancellationToken cancellationToken)
    {
        var threadEvent = CreateAgentThreadEvent(
            thread, evt, messageTurnId, conversationId, iteration,
            inputMessageCount, isResume, turnMessageCount);
        var publisher = CreateEventPublisher(Config!.SessionStore!, eventCoordinator);
        var result = await publisher.FinalizeAndPublishDeltasAsync(
            new ThreadKey(thread.SessionId, thread.Id), threadEvent, cancellationToken).ConfigureAwait(false);
        return result.CommittedEvents[^1];
    }

    private async Task FinalizeOutstandingAgentDeltasAsync(
        Thread? thread,
        HashSet<string> stagedTextMessages,
        HashSet<string> stagedReasoningMessages,
        string? messageTurnId,
        string? conversationId,
        int iteration,
        int inputMessageCount,
        bool isResume,
        int turnMessageCount,
        HPD.Events.IEventCoordinator eventCoordinator)
    {
        if (thread is null || Config?.SessionStore is not IThreadDeltaStore)
            return;

        foreach (var messageId in stagedTextMessages.ToArray())
        {
            await FinalizeAgentThreadDeltasAsync(
                thread, new TextMessageEndEvent(messageId), messageTurnId, conversationId,
                iteration, inputMessageCount, isResume, turnMessageCount,
                eventCoordinator, CancellationToken.None).ConfigureAwait(false);
            stagedTextMessages.Remove(messageId);
        }
        foreach (var messageId in stagedReasoningMessages.ToArray())
        {
            await FinalizeAgentThreadDeltasAsync(
                thread, new ReasoningMessageEndEvent(messageId), messageTurnId, conversationId,
                iteration, inputMessageCount, isResume, turnMessageCount,
                eventCoordinator, CancellationToken.None).ConfigureAwait(false);
            stagedReasoningMessages.Remove(messageId);
        }
    }

    private AgentEvent CreateAgentThreadEvent(
        Thread thread,
        AgentEvent evt,
        string? messageTurnId,
        string? conversationId,
        int iteration,
        int inputMessageCount,
        bool isResume,
        int turnMessageCount)
    {
        evt = EnrichOutputEvent(evt);
        return ThreadEventFactory.FromAgentEvent(
            thread.SessionId,
            thread.Id,
            evt,
            messageTurnId,
            conversationId,
            iteration,
            inputMessageCount,
            isResume,
            null,
            turnMessageCount)
            ?? throw new InvalidOperationException(
                $"Canonical event '{evt.GetType().Name}' could not be scoped to thread '{thread.Id}'.");
    }

    internal AgentInputResult.Control CancelRuntimeExecution(string threadExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadExecutionId);
        CancellationTokenSource? executionCancellation = null;
        AgentInputResult.Control result;
        lock (_runtimeLock)
        {
            var activeInput = _activeRuntimeInput;
            if (activeInput is null)
                result = new AgentInputResult.Control(AgentInputDisposition.NoActiveExecution, threadExecutionId);
            else if (!string.Equals(activeInput.ThreadExecutionId, threadExecutionId, StringComparison.Ordinal))
                result = new AgentInputResult.Control(
                    AgentInputDisposition.ActiveExecutionMismatch,
                    activeInput.ThreadExecutionId);
            else
            {
                executionCancellation = activeInput.Cancellation;
                result = new AgentInputResult.Control(AgentInputDisposition.Accepted, activeInput.ThreadExecutionId);
            }
        }

        executionCancellation?.Cancel();
        return result;
    }

    private async ValueTask<AgentInputResult> TrySteerAsync(
        UserMessagesInputEvent steering,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (steering.Messages.Count == 0 || steering.Messages.Any(static message => message is null))
            throw new ArgumentException("Steering requires at least one non-null message.", nameof(steering));
        if (steering.Messages.Any(static message => message.Role != ChatRole.User))
            throw new ArgumentException("Steering currently accepts only user-role messages.", nameof(steering));

        AgentInputResult? result = null;
        var startOrdinaryTurn = false;
        lock (_runtimeLock)
        {
            var activeInput = _activeRuntimeInput;
            if (activeInput is null)
            {
                if (string.IsNullOrWhiteSpace(steering.ThreadExecutionId))
                    startOrdinaryTurn = true;
                else
                    result = ControlResult(AgentInputDisposition.NoActiveExecution, steering.ThreadExecutionId);
            }
            else if (!string.IsNullOrWhiteSpace(steering.ThreadExecutionId) &&
                     !string.Equals(activeInput.ThreadExecutionId, steering.ThreadExecutionId, StringComparison.Ordinal))
                result = ControlResult(AgentInputDisposition.ActiveExecutionMismatch, activeInput.ThreadExecutionId);
            else if (activeInput.Input is not UserMessagesInputEvent)
                result = ControlResult(AgentInputDisposition.ActiveInputNotSteerable, activeInput.ThreadExecutionId);
            else if (activeInput.State != ActiveRuntimeInputState.Accepting)
                result = ControlResult(AgentInputDisposition.ExecutionFinishing, activeInput.ThreadExecutionId);
            else if (!activeInput.Continuations.Writer.TryWrite(
                         new AcceptedTurnContinuation(steering.Messages, steering.ClientInputId)))
                result = ControlResult(AgentInputDisposition.ExecutionFinishing, activeInput.ThreadExecutionId);
            else
                result = new AgentInputResult.Steered(activeInput.ThreadExecutionId);
        }

        if (startOrdinaryTurn)
        {
            return await RunCapturedInputAsync(
                    steering with { Delivery = AgentInputDelivery.Queue },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return result!;
    }

    private PreparedAgentWorkAdmission PrepareOperationNotificationAdmission(
        AgentOperationNotificationInputEvent input,
        AgentWorkScheduler scheduler)
    {
        var admitted = (AgentOperationNotificationInputEvent)AuthorizeWorkIdentity(
            CaptureInput(input),
            _inputDispatcher.GetRegistration(input.GetType()));
        var continuation = AgentOperationNotificationDispatcher.ToNotificationTurnInput(admitted);

        return scheduler.Prepare(
            admitted,
            tryCommitToActiveTurn: () =>
            {
                var activeInput = _activeRuntimeInput;
                if (activeInput is null ||
                    activeInput.State != ActiveRuntimeInputState.Accepting ||
                    activeInput.Input is not (UserMessagesInputEvent or AgentOperationNotificationInputEvent) ||
                    !string.Equals(activeInput.Input.SessionId, admitted.SessionId, StringComparison.Ordinal) ||
                    !string.Equals(activeInput.Input.ThreadId, admitted.ThreadId, StringComparison.Ordinal))
                {
                    return false;
                }

                return activeInput.Continuations.Writer.TryWrite(new AcceptedTurnContinuation(
                    continuation.Messages,
                    continuation.ClientInputId,
                    admitted));
            });
    }

    private static AgentInputResult ControlResult(AgentInputDisposition disposition, string? executionId)
        => new AgentInputResult.Control(disposition, executionId);

    private AgentInputEvent TargetActiveExecution(AgentInputEvent input)
    {
        if (!string.IsNullOrWhiteSpace(input.ThreadExecutionId))
            return input;
        lock (_runtimeLock)
            return input with { ThreadExecutionId = _activeRuntimeInput?.ThreadExecutionId };
    }

    private async Task<AgentTurnResult> RunMessagesInputAsync(
        UserMessagesInputEvent input,
        ActiveRuntimeInput? activeInput,
        HPD.Events.IEventCoordinator eventCoordinator,
        CancellationToken cancellationToken)
    {
        if (input.Session != null || input.Thread != null)
        {
            if (input.Session is null || input.Thread is null)
                throw new InvalidOperationException("UserMessagesInputEvent must provide both Session and Thread for process-local scoped runs.");

            var result = new AgentTurnResultBuilder();
            await foreach (var evt in RunTurnStreamAsync(
                input.Messages,
                input.Session,
                input.Thread,
                input.RunConfig,
                eventCoordinator,
                input.ClientInputId,
                activeInput,
                cancellationToken,
                input.InheritedChatClient,
                input.InheritedChatMode).ConfigureAwait(false))
            {
                result.Add(evt);
            }

            return result.Build();
        }

        if (!string.IsNullOrWhiteSpace(input.SessionId))
        {
            var (session, thread) = await LoadSessionAndThreadAsync(
                input.SessionId,
                input.ThreadId,
                cancellationToken).ConfigureAwait(false);

            var result = new AgentTurnResultBuilder();
            await foreach (var evt in RunTurnStreamAsync(
                input.Messages,
                session,
                thread,
                input.RunConfig,
                eventCoordinator,
                input.ClientInputId,
                activeInput,
                cancellationToken,
                input.InheritedChatClient,
                input.InheritedChatMode).ConfigureAwait(false))
            {
                result.Add(evt);
            }

            if (Config?.SessionStoreOptions?.PersistAfterTurn == true)
            {
                await SaveSessionAndThreadAsync(session, thread, cancellationToken).ConfigureAwait(false);
            }

            return result.Build();
        }

        var unsessionedResult = new AgentTurnResultBuilder();
        await foreach (var evt in RunTurnStreamAsync(
            input.Messages,
            null,
            null,
            input.RunConfig,
            eventCoordinator,
            input.ClientInputId,
            activeInput,
            cancellationToken,
            input.InheritedChatClient,
            input.InheritedChatMode).ConfigureAwait(false))
        {
            unsessionedResult.Add(evt);
        }

        return unsessionedResult.Build();
    }

    private HPD.Events.IEventCoordinator GetActiveEventCoordinator()
    {
        lock (_runtimeLock)
        {
            if (!_runtimeStopping &&
                _runtimeInbox != null &&
                _runtimeLoopTask is { IsCompleted: false } &&
                _runtimeEventCoordinator != null)
            {
                return _runtimeEventCoordinator;
            }
        }

        return _eventCoordinator;
    }

    private IStructEventHub GetActiveStructEvents()
    {
        lock (_runtimeLock)
        {
            if (!_runtimeStopping &&
                _runtimeInbox != null &&
                _runtimeLoopTask is { IsCompleted: false } &&
                _runtimeStructEvents != null)
            {
                return _runtimeStructEvents;
            }
        }

        return _structEvents;
    }

    private async Task<AgentInputResult> RunInputDirectAsync(
        AgentInputEvent input,
        AgentInputHandlerRegistration registration,
        CancellationToken cancellationToken)
        => await RunInputDirectAsync(input, registration, GetActiveEventCoordinator(), null, cancellationToken).ConfigureAwait(false);

    private async Task<AgentInputResult> RunInputDirectAsync(
        AgentInputEvent input,
        AgentInputHandlerRegistration registration,
        HPD.Events.IEventCoordinator eventCoordinator,
        ActiveRuntimeInput? activeInput,
        CancellationToken cancellationToken)
        => await _inputDispatcher.DispatchAsync(
                input,
                registration,
                CreateInputHandlingContext(eventCoordinator, activeInput),
                cancellationToken)
            .ConfigureAwait(false);

    private async Task<AgentInputResult> ExecuteCoordinatedInputAsync(
        AgentInputEvent input,
        AgentInputHandlerRegistration registration,
        HPD.Events.IEventCoordinator eventCoordinator,
        ActiveRuntimeInput? activeInput,
        CancellationToken cancellationToken)
    {
        if (registration.RoutingClass == AgentInputRoutingClass.Work &&
            input.WorkIdentityAuthority == AgentWorkIdentityAuthority.CoordinatorAssigned)
        {
            var reservation = input.WorkIdentityReservation as CoordinatorWorkReservation
                ?? throw new InvalidOperationException("Coordinator-assigned work lacks its promotion reservation.");
            var finished = false;
            try
            {
                await reservation.PromoteAsync(cancellationToken).ConfigureAwait(false);
                var result = await RunInputDirectAsync(
                        input, registration, eventCoordinator, activeInput, cancellationToken)
                    .ConfigureAwait(false);
                await reservation.FinishAsync(
                    ThreadExecutionOutcome.Succeeded, null, CancellationToken.None).ConfigureAwait(false);
                finished = true;
                if (input is AgentOperationNotificationInputEvent notification)
                {
                    await PublishAgentOperationNotificationDeliveredAsync(
                        notification, eventCoordinator, CancellationToken.None).ConfigureAwait(false);
                }
                return result;
            }
            catch (Exception exception)
            {
                if (!finished)
                {
                    await reservation.FinishAsync(
                        exception is OperationCanceledException
                            ? ThreadExecutionOutcome.Cancelled
                            : ThreadExecutionOutcome.Failed,
                        exception,
                        CancellationToken.None).ConfigureAwait(false);
                }
                throw;
            }
        }

        if (registration.RoutingClass != AgentInputRoutingClass.Work ||
            Config.SessionStore is not { } executionStore ||
            input.SessionId is not { Length: > 0 } executionSessionId ||
            input.ThreadId is not { Length: > 0 } executionThreadId)
        {
            if (registration.RoutingClass != AgentInputRoutingClass.Work)
                return await RunInputDirectAsync(
                        input, registration, eventCoordinator, activeInput, cancellationToken)
                    .ConfigureAwait(false);

            var executionId = input.ThreadExecutionId
                ?? throw new InvalidOperationException("Admitted unscoped work requires an execution identity.");
            await PublishScopedRuntimeEventAsync(new ThreadExecutionStartedEvent(
                executionId,
                input.AgentId ?? _name,
                DateTimeOffset.UtcNow)
            {
                SessionId = input.SessionId,
                ThreadId = input.ThreadId,
                ThreadExecutionId = executionId
            }, eventCoordinator, cancellationToken).ConfigureAwait(false);
            try
            {
                var unscopedResult = await RunInputDirectAsync(
                        input, registration, eventCoordinator, activeInput, cancellationToken)
                    .ConfigureAwait(false);
                await PublishScopedRuntimeEventAsync(new ThreadExecutionFinishedEvent(
                    executionId,
                    input.AgentId ?? _name,
                    ThreadExecutionOutcome.Succeeded,
                    DateTimeOffset.UtcNow)
                {
                    SessionId = input.SessionId,
                    ThreadId = input.ThreadId,
                    ThreadExecutionId = executionId
                }, eventCoordinator, CancellationToken.None).ConfigureAwait(false);
                if (input is AgentOperationNotificationInputEvent unscopedNotification)
                {
                    await PublishAgentOperationNotificationDeliveredAsync(
                            unscopedNotification, eventCoordinator, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                return unscopedResult;
            }
            catch (Exception exception)
            {
                await PublishScopedRuntimeEventAsync(new ThreadExecutionFinishedEvent(
                    executionId,
                    input.AgentId ?? _name,
                    exception is OperationCanceledException
                        ? ThreadExecutionOutcome.Cancelled
                        : ThreadExecutionOutcome.Failed,
                    DateTimeOffset.UtcNow,
                    exception is OperationCanceledException
                        ? null
                        : new ThreadExecutionError(exception.GetType().Name, exception.Message))
                {
                    SessionId = input.SessionId,
                    ThreadId = input.ThreadId,
                    ThreadExecutionId = executionId
                }, eventCoordinator, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        if (string.IsNullOrWhiteSpace(input.ThreadExecutionId))
            throw new InvalidOperationException("Coordinated work requires an admitted execution identity.");

        var controller = ThreadExecutionControllerRegistry.For(executionStore);
        var acquired = await controller.TryAcquireAsync(
            new ThreadExecutionStartRequest(
                new ThreadKey(executionSessionId, executionThreadId),
                input.ThreadExecutionId,
                this),
            cancellationToken).ConfigureAwait(false);
        if (!acquired.Acquired || acquired.Lease is null)
            throw new InvalidOperationException($"thread_execution_busy:{acquired.ActiveThreadExecutionId}");

        try
        {
            var result = await RunInputDirectAsync(
                    input, registration, eventCoordinator, activeInput, cancellationToken)
                .ConfigureAwait(false);
            await controller.ReleaseAsync(
                acquired.Lease,
                new ThreadExecutionTerminalResult(ThreadExecutionOutcome.Succeeded),
                CancellationToken.None).ConfigureAwait(false);
            if (input is AgentOperationNotificationInputEvent notification)
            {
                await PublishAgentOperationNotificationDeliveredAsync(
                        notification, eventCoordinator, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            return result;
        }
        catch (Exception exception)
        {
            await controller.ReleaseAsync(
                acquired.Lease,
                new ThreadExecutionTerminalResult(
                    exception is OperationCanceledException
                        ? ThreadExecutionOutcome.Cancelled
                        : ThreadExecutionOutcome.Failed,
                    exception.GetType().Name,
                    exception.Message),
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private AgentInputHandlingContext CreateInputHandlingContext(
        HPD.Events.IEventCoordinator eventCoordinator,
        ActiveRuntimeInput? activeInput)
        => new()
        {
            AgentName = _name,
            Config = Config ?? throw new InvalidOperationException("Agent configuration is not available."),
            EventCoordinator = eventCoordinator,
            Services = _serviceProvider,
            ClientSet = _clientSet,
            ContentStore = _contentStore,
            RuntimeCapabilities = _runtimeContext?.RuntimeCapabilities ?? new RuntimeCapabilityRegistry(),
            StructEvents = GetActiveStructEvents(),
            RuntimeRunConfig = _runtimeContext?.RunConfig,
            ChatClientResolver = _chatClientResolver,
            DefaultChatClient = _defaultChatClientHandle,
            ActiveInput = activeInput,
            RunMessagesAsync = RunMessagesInputAsync,
            TryResolveClientToolOperation = ResolveClientToolOperation
        };

    private bool ResolveClientToolOperation(ClientToolOperationOutcomeEvent input)
    {
        lock (_runtimeLock)
            return _runtimeContext?.TryResolveClientToolOperation(input) == true;
    }

    private async Task RunRuntimeLoopAsync(
        ChannelReader<AgentInputEvent> reader,
        HPD.Events.IEventCoordinator eventCoordinator,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var input in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                lock (_runtimeLock)
                {
                    if (_cancelledRuntimeInputs.Remove(input))
                        continue;
                }

                using var activeInputCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var activeInput = new ActiveRuntimeInput(input, activeInputCts);
                AgentInputResult result = new AgentInputResult.Completed(
                    AgentTurnResult.Empty,
                    input.ThreadExecutionId);
                Exception? runError = null;
                var runCancelled = false;
                lock (_runtimeLock)
                {
                    if (_activeRuntimeInput is not null)
                        throw new InvalidOperationException("The single-reader runtime already has an active input.");
                    _activeRuntimeInput = activeInput;
                }

                try
                {
                    var registration = _inputDispatcher.GetRegistration(input.GetType());
                    var inputResult = await ExecuteCoordinatedInputAsync(
                            input,
                            registration,
                            eventCoordinator,
                            activeInput,
                            activeInputCts.Token)
                        .ConfigureAwait(false);
                    result = inputResult;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (activeInputCts.IsCancellationRequested)
                {
                    runCancelled = true;
                    // The active input was interrupted; keep the runtime loop alive.
                }
                catch (Exception ex)
                {
                    runError = ex;
                    _agentLogger?.LogError(ex,
                        "Agent runtime loop failed processing input event {EventType}",
                        input.GetType().Name);
                }
                finally
                {
                    lock (_runtimeLock)
                    {
                        activeInput.State = ActiveRuntimeInputState.Finished;
                        activeInput.Continuations.Writer.TryComplete(runError);
                        if (ReferenceEquals(_activeRuntimeInput, activeInput))
                            _activeRuntimeInput = null;
                        if (_runtimeInputCompletions.Remove(input, out var completion))
                        {
                            completion.TrySetResult(new AgentRuntimeInputOutcome(result, runCancelled, runError));
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cooperative runtime shutdown.
        }
        finally
        {
            CompleteAbandonedRuntimeInputs();
        }
    }

    private void CompleteAbandonedRuntimeInputs()
    {
        List<TaskCompletionSource<AgentRuntimeInputOutcome>> abandoned;
        lock (_runtimeLock)
        {
            if (_runtimeInputCompletions.Count == 0)
                return;

            abandoned = _runtimeInputCompletions.Values.ToList();
            _runtimeInputCompletions.Clear();
        }

        var outcome = new AgentRuntimeInputOutcome(
            new AgentInputResult.Completed(AgentTurnResult.Empty, null),
            Cancelled: true,
            Error: null);
        foreach (var completion in abandoned)
            completion.TrySetResult(outcome);
    }

    private async ValueTask PublishAgentOperationNotificationDeliveredAsync(
        AgentOperationNotificationInputEvent input,
        HPD.Events.IEventCoordinator runtimeCoordinator,
        CancellationToken cancellationToken)
    {
        foreach (var notification in input.Notifications)
        {
            await PublishScopedRuntimeEventAsync(new AgentOperationNotificationDeliveredEvent
            {
                NotificationId = notification.NotificationId,
                DeliveredAt = DateTimeOffset.UtcNow,
                ThreadExecutionId = input.ThreadExecutionId,
                SessionId = input.SessionId,
                ThreadId = input.ThreadId
            }, runtimeCoordinator, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<AgentEvent> PublishScopedRuntimeEventAsync(
        AgentEvent evt,
        HPD.Events.IEventCoordinator runtimeCoordinator,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(evt.SessionId) || string.IsNullOrWhiteSpace(evt.ThreadId))
        {
            var codec = Config?.EventComposition?.Codec
                ?? throw new InvalidOperationException("Agent runtime has no event composition authority.");
            if (!codec.TryGetByType(evt.GetType(), out _))
                throw new InvalidOperationException($"Agent event type '{evt.GetType().FullName}' is not present in codec '{codec.Digest}'.");
            var live = evt with { ThreadSequenceNumber = 0 };
            await runtimeCoordinator.EmitAsync(live, AgentEventRoutes.Create(live), cancellationToken).ConfigureAwait(false);
            return live;
        }

        var store = Config?.SessionStore;
        if (store is null)
        {
            var stateless = ThreadEventValidation.PrepareForAppend(
                evt.SessionId,
                evt.ThreadId,
                evt) with { ThreadSequenceNumber = 0 };
            var codec = Config?.EventComposition?.Codec
                ?? throw new InvalidOperationException("Agent runtime has no event composition authority.");
            if (!codec.TryGetByType(stateless.GetType(), out _))
                throw new InvalidOperationException($"Agent event type '{stateless.GetType().FullName}' is not present in codec '{codec.Digest}'.");
            await runtimeCoordinator.EmitAsync(stateless, AgentEventRoutes.Create(stateless), cancellationToken).ConfigureAwait(false);
            return stateless;
        }

        return await CreateEventPublisher(store, runtimeCoordinator).PublishAsync(
            new ThreadKey(evt.SessionId, evt.ThreadId),
            EnrichOutputEvent(evt),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the agent's continuous runtime input loop.
    /// </summary>
    public async Task StartAsync(AgentRunConfig? runConfig = null, CancellationToken cancellationToken = default)
    {
        ThrowIfShutdownStarted();
        cancellationToken.ThrowIfCancellationRequested();

        Middleware.AgentRuntimeContext runtimeContext;
        HPD.Events.IEventCoordinator runtimeCoordinator;
        IAgentEventPublisher? runtimeThreadEvents;
        StructEventHub runtimeStructEvents;
        CancellationTokenSource runtimeCts;
        Channel<AgentInputEvent> runtimeInbox;
        AgentWorkScheduler? runtimeWorkScheduler;
        AgentOperationNotificationDispatcher runtimeNotificationDispatcher;

        lock (_runtimeLock)
        {
            if (_runtimeStarting || (!_runtimeStopping && _runtimeLoopTask is { IsCompleted: false }))
            {
                _runtimeNotificationDispatcher?.UpdateRunConfig(
                    AgentRunConfigSnapshot.Capture(runConfig, _chatClientResolver.Composition));
                return;
            }

            _runtimeStarting = true;
            _runtimeCts?.Dispose();
            runtimeCts = new CancellationTokenSource();
            runtimeCoordinator = _eventCoordinator.CreateChild(HPD.Events.EventChildOwnership.InheritOwner);
            runtimeThreadEvents = Config?.SessionStore is { } store
                ? CreateEventPublisher(store, runtimeCoordinator)
                : null;
            runtimeStructEvents = new StructEventHub();
            runtimeInbox = Channel.CreateUnbounded<AgentInputEvent>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
            runtimeWorkScheduler = new AgentWorkScheduler(_runtimeLock, runtimeInbox.Writer);
            runtimeContext = new Middleware.AgentRuntimeContext(
                _name,
                Config ?? throw new InvalidOperationException("Agent configuration is not available."),
                _serviceProvider,
                runtimeCoordinator,
                runtimeStructEvents,
                runtimeInbox.Writer,
                async (runtimeInput, ct) =>
                {
                    using var receipt = await SubmitRuntimeInputAsync(runtimeInput, ct).ConfigureAwait(false);
                },
                HasActiveRuntimeInputs,
                runtimeCts.Token,
                runtimeThreadEvents,
                _clientSet,
                runConfig,
                _contentStore);
            runtimeContext.RuntimeCapabilities.Set<IRuntimeFunctionExecutor>(
                new AgentRuntimeFunctionExecutor(
                    _name,
                    _baseClient,
                    _serviceProvider,
                    Config,
                    _chatClientResolver,
                    _defaultChatClientHandle,
                    _messageProcessor,
                    _functionExecutionCore,
                    runtimeContext,
                    runtimeCoordinator));
            runtimeContext.RuntimeCapabilities.Set(_operationRegistry);
            runtimeContext.RuntimeCapabilities.Set<IClientToolOperationRegistry>(runtimeContext);
            runtimeNotificationDispatcher = new AgentOperationNotificationDispatcher(
                _eventCoordinator,
                runtimeThreadEvents,
                input => PrepareOperationNotificationAdmission(input, runtimeWorkScheduler),
                AgentRunConfigSnapshot.Capture(runConfig, _chatClientResolver.Composition));

            _runtimeCts = runtimeCts;
            _runtimeEventCoordinator = runtimeCoordinator;
            _runtimeStructEvents = runtimeStructEvents;
            _runtimeContext = runtimeContext;
            _runtimeWorkScheduler = runtimeWorkScheduler;
            _runtimeNotificationDispatcher = runtimeNotificationDispatcher;
            _runtimeInbox = null;
            _runtimeStopping = false;
        }

        try
        {
            var beforeStart = runtimeContext.AsBeforeStart();
            await _middlewarePipeline.ExecuteBeforeStartAsync(beforeStart, cancellationToken).ConfigureAwait(false);

            if (beforeStart.CancelStart)
            {
                throw new InvalidOperationException(
                    beforeStart.CancelReason ?? "Agent runtime start was cancelled by middleware.");
            }

            Task runtimeLoopTask;
            lock (_runtimeLock)
            {
                _runtimeInbox = runtimeInbox;
                runtimeLoopTask = Task.Run(
                    () => RunRuntimeLoopAsync(runtimeInbox.Reader, runtimeCoordinator, runtimeCts.Token),
                    CancellationToken.None);
                _runtimeLoopTask = runtimeLoopTask;
            }

            runtimeContext.MarkStarted();

            await _middlewarePipeline.ExecuteAfterStartedAsync(
                runtimeContext.AsAfterStarted(),
                cancellationToken).ConfigureAwait(false);

            runtimeContext.SealRuntimeCapabilities();

            lock (_runtimeLock)
            {
                if (ReferenceEquals(_runtimeContext, runtimeContext))
                    _runtimeStarting = false;
            }
        }
        catch
        {
            await StopRuntimeAsync(
                runtimeContext,
                runtimeCoordinator,
                runtimeStructEvents,
                runtimeCts,
                RuntimeStopReason.Faulted,
                runBeforeStop: false,
                cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Stops accepting new runtime inputs and drains queued work.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Middleware.AgentRuntimeContext? runtimeContext;
        HPD.Events.IEventCoordinator? runtimeCoordinator;
        StructEventHub? runtimeStructEvents;
        CancellationTokenSource? runtimeCts;

        lock (_runtimeLock)
        {
            runtimeContext = _runtimeContext;
            runtimeCoordinator = _runtimeEventCoordinator;
            runtimeStructEvents = _runtimeStructEvents;
            runtimeCts = _runtimeCts;

            if (_runtimeStopping || runtimeContext == null || runtimeCoordinator == null || runtimeStructEvents == null || runtimeCts == null)
                return;

            _runtimeStopping = true;
        }

        await StopRuntimeAsync(
            runtimeContext,
            runtimeCoordinator,
            runtimeStructEvents,
            runtimeCts,
            RuntimeStopReason.UserRequested,
            runBeforeStop: true,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task StopRuntimeAsync(
        Middleware.AgentRuntimeContext runtimeContext,
        HPD.Events.IEventCoordinator runtimeCoordinator,
        StructEventHub runtimeStructEvents,
        CancellationTokenSource runtimeCts,
        RuntimeStopReason reason,
        bool runBeforeStop,
        CancellationToken cancellationToken)
    {
        Task? runtimeTask;
        AgentOperationNotificationDispatcher? runtimeNotificationDispatcher;
        AgentWorkScheduler? runtimeWorkScheduler;
        var drainPendingInputs = true;
        TimeSpan? drainTimeout = null;
        Exception? stopError = null;
        List<Exception>? exceptions = null;


        try
        {
            if (runBeforeStop)
            {
                var beforeStop = runtimeContext.AsBeforeStop(reason);
                await _middlewarePipeline.ExecuteBeforeStopAsync(beforeStop, cancellationToken).ConfigureAwait(false);

                drainPendingInputs = beforeStop.DrainPendingInputs;
                drainTimeout = beforeStop.DrainTimeout;
            }
        }
        catch (OperationCanceledException)
        {
            // Stop has already been committed by StopAsync. Cancellation can skip the
            // graceful phase, but it cannot abandon the runtime in a stopping state.
            drainPendingInputs = false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopError = ex;
            exceptions ??= new List<Exception>();
            exceptions.Add(ex);
        }

        lock (_runtimeLock)
        {
            runtimeTask = _runtimeLoopTask;
            runtimeNotificationDispatcher = _runtimeNotificationDispatcher;
            runtimeWorkScheduler = _runtimeWorkScheduler;
            _runtimeStopping = true;
        }

        if (runtimeNotificationDispatcher is not null)
        {
            try
            {
                await runtimeNotificationDispatcher.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                stopError ??= ex;
                exceptions ??= new List<Exception>();
                exceptions.Add(ex);
            }
        }

        if (runtimeWorkScheduler is not null)
        {
            try
            {
                await runtimeWorkScheduler.StopPreparing().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                stopError ??= ex;
                exceptions ??= new List<Exception>();
                exceptions.Add(ex);
            }
        }

        runtimeContext.CompleteInputWriter();

        if (!drainPendingInputs)
        {
            runtimeCts.Cancel();
            CancelActiveRuntimeInputs();
        }

        if (runtimeTask != null)
        {
            try
            {
                var waitTask = runtimeTask;
                if (drainTimeout is { } timeout)
                    waitTask = waitTask.WaitAsync(timeout, cancellationToken);
                else
                    waitTask = waitTask.WaitAsync(cancellationToken);

                await waitTask.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
                runtimeCts.Cancel();
                CancelActiveRuntimeInputs();

                try
                {
                    // The caller token bounds graceful drain. Once it expires we cancel
                    // locally owned work and await convergence before disposing anything
                    // that the runtime may still publish through.
                    await runtimeTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected when forced cancellation terminates the runtime loop.
                }
                catch (Exception innerEx)
                {
                    stopError ??= innerEx;
                    exceptions ??= new List<Exception>();
                    exceptions.Add(innerEx);
                }
            }
            catch (Exception ex)
            {
                stopError ??= ex;
                exceptions ??= new List<Exception>();
                exceptions.Add(ex);
            }
        }

        runtimeCts.Cancel();
        runtimeContext.MarkStopped();

        try
        {
            await runtimeContext.DisposeRegisteredResourcesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            stopError ??= ex;
            exceptions ??= new List<Exception>();
            exceptions.Add(ex);
        }

        try
        {
            await _middlewarePipeline.ExecuteAfterStoppedAsync(
                runtimeContext.AsAfterStopped(reason, stopError),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            exceptions ??= new List<Exception>();
            exceptions.Add(ex);
        }

        lock (_runtimeLock)
        {
            if (ReferenceEquals(_runtimeContext, runtimeContext))
            {
                runtimeNotificationDispatcher = _runtimeNotificationDispatcher;
                _runtimeInbox = null;
                _runtimeLoopTask = null;
                _runtimeCts = null;
                _runtimeContext = null;
                _runtimeWorkScheduler = null;
                _runtimeEventCoordinator = null;
                _runtimeStructEvents = null;
                _runtimeNotificationDispatcher = null;
                _runtimeStarting = false;
                _runtimeStopping = false;
            }
            else
            {
                runtimeNotificationDispatcher = null;
            }
        }

        runtimeCts.Dispose();
        (runtimeCoordinator as IDisposable)?.Dispose();
        runtimeStructEvents.Dispose();

        if (exceptions is { Count: > 0 })
            throw new AggregateException("One or more runtime stop operations failed.", exceptions);
    }

    private void CancelActiveRuntimeInputs()
    {
        CancellationTokenSource? activeInputCts;
        lock (_runtimeLock)
        {
            activeInputCts = _activeRuntimeInput?.Cancellation;
        }

        if (activeInputCts is not null)
        {
            try
            {
                activeInputCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Input completed concurrently.
            }
        }
    }

    private bool HasActiveRuntimeInputs()
    {
        lock (_runtimeLock)
        {
            return _activeRuntimeInput is not null;
        }
    }

    /// <summary>
    /// Sends a semantic input event to the agent.
    /// </summary>
    /// <returns>The semantic admission or completion result.</returns>
    public Task<AgentInputResult> RunAsync(AgentInputEvent input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return RunCapturedInputAsync(CaptureInput(input), cancellationToken);
    }

    private async Task<AgentInputResult> RunCapturedInputAsync(
        AgentInputEvent input,
        CancellationToken cancellationToken)
    {
        var registration = _inputDispatcher.GetRegistration(input.GetType());

        if (input is UserMessagesInputEvent { Delivery: AgentInputDelivery.Steer } steering)
            return await TrySteerAsync(steering, cancellationToken).ConfigureAwait(false);

        input = AuthorizeWorkIdentity(input, registration);

        return await RunCapturedInputCoreAsync(input, registration, cancellationToken).ConfigureAwait(false);
    }

    internal AgentInputEvent AuthorizeCoordinatorAssignedWork(
        AgentInputEvent input,
        CoordinatorWorkReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(reservation);
        if (string.IsNullOrWhiteSpace(input.ThreadExecutionId))
            throw new ArgumentException("Coordinator-assigned work requires an execution ID.", nameof(input));
        if (!reservation.Matches(input))
            throw new InvalidOperationException("Coordinator work does not match its execution reservation.");
        return input with
        {
            WorkIdentityAuthority = AgentWorkIdentityAuthority.CoordinatorAssigned,
            WorkIdentityReservation = reservation,
            WorkIdentityValidated = false
        };
    }

    private AgentInputEvent AuthorizeWorkIdentity(
        AgentInputEvent input,
        AgentInputHandlerRegistration registration)
    {
        if (registration.RoutingClass != AgentInputRoutingClass.Work || input.WorkIdentityValidated)
            return input;

        if (input.WorkIdentityAuthority == AgentWorkIdentityAuthority.CoordinatorAssigned)
        {
            if (input.WorkIdentityReservation is not CoordinatorWorkReservation reservation ||
                !reservation.Matches(input) ||
                string.IsNullOrWhiteSpace(input.ThreadExecutionId))
            {
                throw new InvalidOperationException("Coordinator-assigned work lacks a valid reservation proof.");
            }
            return input with { WorkIdentityValidated = true };
        }

        if (!string.IsNullOrWhiteSpace(input.ThreadExecutionId))
            throw new InvalidOperationException("Framework-allocated work must not supply a thread execution ID.");
        return input with
        {
            ThreadExecutionId = Guid.NewGuid().ToString("N"),
            WorkIdentityValidated = true
        };
    }

    private async Task<AgentInputResult> RunCapturedInputCoreAsync(
        AgentInputEvent input,
        AgentInputHandlerRegistration registration,
        CancellationToken cancellationToken)
    {

        if (registration.RoutingClass == AgentInputRoutingClass.Work)
        {
            ChannelWriter<AgentInputEvent>? runtimeWriter;
            bool runtimeTransitioning;

            lock (_runtimeLock)
            {
                runtimeTransitioning = _runtimeStarting || _runtimeStopping;
                runtimeWriter = !_runtimeStopping &&
                    _runtimeInbox != null &&
                    _runtimeLoopTask is { IsCompleted: false }
                        ? _runtimeInbox.Writer
                        : null;
            }

            if (runtimeWriter is not null)
            {
                using var receipt = await SubmitRuntimeInputAsync(input, cancellationToken).ConfigureAwait(false);
                var outcome = await receipt.Completion.ConfigureAwait(false);
                if (outcome.Error is not null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(outcome.Error).Throw();
                if (outcome.Cancelled)
                    throw new OperationCanceledException(receipt.CallerToken);
                return outcome.Result;
            }

            if (runtimeTransitioning)
                throw new InvalidOperationException("Agent runtime is starting or stopping and cannot accept user input.");
        }

        return await ExecuteCoordinatedInputAsync(
                input, registration, GetActiveEventCoordinator(), null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Admits one captured work item to the running runtime.</summary>
    internal async ValueTask<RuntimeInputReceipt> SubmitRuntimeInputAsync(
        AgentInputEvent input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        input = CaptureInput(input);
        var registration = _inputDispatcher.GetRegistration(input.GetType());
        if (registration.RoutingClass != AgentInputRoutingClass.Work)
            throw new ArgumentException("Only work inputs can be enqueued as runtime work.", nameof(input));
        input = AuthorizeWorkIdentity(input, registration);

        AgentWorkScheduler? runtimeWorkScheduler;
        var completion = new TaskCompletionSource<AgentRuntimeInputOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_runtimeLock)
        {
            runtimeWorkScheduler = !_runtimeStopping &&
                _runtimeLoopTask is { IsCompleted: false }
                    ? _runtimeWorkScheduler ?? throw new InvalidOperationException("Agent runtime scheduler is unavailable.")
                    : null;

            if (runtimeWorkScheduler is null)
                throw new InvalidOperationException("Agent runtime is not running and cannot accept queued input.");
        }

        CancellationTokenRegistration cancellationRegistration = default;
        using var prepared = runtimeWorkScheduler.Prepare(
            input,
            reserveCompletion: () =>
            {
                if (!_runtimeInputCompletions.TryAdd(input, completion))
                    throw new InvalidOperationException("The same input instance is already queued in this runtime.");
            },
            abortCompletion: () =>
            {
                _runtimeInputCompletions.Remove(input);
                _cancelledRuntimeInputs.Remove(input);
            });
        try
        {
            if (cancellationToken.CanBeCanceled)
            {
                cancellationRegistration = cancellationToken.Register(() =>
                {
                    CancellationTokenSource? activeCancellation = null;
                    lock (_runtimeLock)
                    {
                        if (ReferenceEquals(_activeRuntimeInput?.Input, input))
                        {
                            activeCancellation = _activeRuntimeInput.Cancellation;
                        }
                        else if (_runtimeInputCompletions.Remove(input, out var queuedCompletion))
                        {
                            _cancelledRuntimeInputs.Add(input);
                            queuedCompletion.TrySetCanceled(cancellationToken);
                        }
                    }

                    activeCancellation?.Cancel();
                });
            }

            cancellationToken.ThrowIfCancellationRequested();
            prepared.CommitVisible();
            return new RuntimeInputReceipt(
                input,
                completion.Task,
                cancellationToken,
                cancellationRegistration);
        }
        catch (Exception ex)
        {
            cancellationRegistration.Dispose();
            completion.TrySetException(ex);
            throw;
        }
    }

    /// <summary>
    /// Routes a response event to the request session matching its request ID.
    /// </summary>
    public async Task<AgentRespondResult> AnswerRequestAsync(
        IAgentResponseEvent response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();

        if (response is not HPD.Events.Event responseEvent)
            throw new ArgumentException("Response must also be an HPD.Events.Event.", nameof(response));

        return await CompleteRequestResponseAsync(response, responseEvent, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Attempts to route a response event to the request session matching its request ID.
    /// </summary>
    public async Task<AgentRespondResult> TryAnswerRequestAsync(
        IAgentResponseEvent response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();

        if (response is not HPD.Events.Event responseEvent)
            throw new ArgumentException("Response must also be an HPD.Events.Event.", nameof(response));

        return await CompleteRequestResponseAsync(response, responseEvent, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<AgentRespondResult> CompleteRequestResponseAsync(
        IAgentResponseEvent response,
        HPD.Events.Event responseEvent,
        CancellationToken cancellationToken)
    {
        var coordinator = GetActiveEventCoordinator();
        if (responseEvent is not AgentEvent agentResponse ||
            string.IsNullOrWhiteSpace(agentResponse.SessionId) ||
            string.IsNullOrWhiteSpace(agentResponse.ThreadId))
        {
            return coordinator.Respond(response.RequestId, responseEvent).ToAgentResult();
        }

        var store = Config?.SessionStore
            ?? throw new InvalidOperationException(
                "A scoped response requires the configured session store so it can commit before request completion.");
        var publisher = CreateEventPublisher(store, coordinator);
        var key = new ThreadKey(agentResponse.SessionId, agentResponse.ThreadId);

        var result = await coordinator.RespondAsync(
            response.RequestId,
            responseEvent,
            async (accepted, _) =>
            {
                return await publisher.CommitAndPublishAsync(
                    key,
                    EnrichOutputEvent((AgentEvent)accepted),
                    CancellationToken.None).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
        return result.ToAgentResult();
    }

    /// <summary>
    /// Sends a process-local struct event to the agent's struct event path.
    /// </summary>
    public ValueTask RunAsync<TEvent>(
        TEvent input,
        CancellationToken cancellationToken = default)
        where TEvent : struct, AgentStructEvent
    {
        var structEvents = GetActiveStructEvents();
        structEvents.Route<TEvent>().CreateEmitter().Emit(input);

        if (!ReferenceEquals(structEvents, _structEvents))
            _structEvents.Route<TEvent>().CreateEmitter().Emit(input);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Sends user text input to the agent.
    /// </summary>
    /// <returns>The completed turn result, including final text, emitted events, and completion metadata.</returns>
    public Task<AgentTurnResult> RunAsync(
        string userMessage,
        string? sessionId = null,
        string? threadId = "main",
        AgentRunConfig? runConfig = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);

        return RunTextAsync(new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, userMessage)],
            SessionId = sessionId,
            ThreadId = threadId,
            RunConfig = runConfig
        }, cancellationToken);
    }

    private async Task<AgentTurnResult> RunTextAsync(
        UserMessagesInputEvent input,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(input, cancellationToken).ConfigureAwait(false);
        return result is AgentInputResult.Completed completed
            ? completed.TurnResult
            : throw new InvalidOperationException("Queued user input did not complete as a turn.");
    }

    private AgentInputEvent CaptureInput(AgentInputEvent input)
    {
        var runConfig = AgentRunConfigSnapshot.Capture(input.RunConfig, _chatClientResolver.Composition);
        return input switch
        {
            UserMessagesInputEvent messages => messages with
            {
                RunConfig = runConfig,
                Messages = messages.Messages.ToArray()
            },
            AgentOperationNotificationInputEvent notification => notification with
            {
                RunConfig = runConfig,
                Notifications = notification.Notifications.ToArray()
            },
            _ => input with { RunConfig = runConfig }
        };
    }

    internal IChatClient CreateSpecializedChatClient(
        AgentRunConfig runConfig,
        ChatClientConfig? chat,
        ClientFamilyInheritanceMode inheritance) =>
        new AgentSpecializedChatClient(
            _chatClientResolver,
            Config ?? throw new InvalidOperationException("Agent configuration is not available."),
            runConfig,
            _defaultChatClientHandle,
            chat,
            inheritance);

    internal AgentRunConfig CaptureRunConfig(AgentRunConfig? runConfig) =>
        AgentRunConfigSnapshot.Capture(runConfig, _chatClientResolver.Composition) ?? new AgentRunConfig();

    /// <summary>
    /// - Accepts PreparedTurn (functional preparation from MessageProcessor.PrepareTurnAsync)
    /// - Uses AgentDecisionEngine (pure, testable) for all decision logic
    /// - Executes decisions INLINE to preserve real-time streaming
    /// - State managed via immutable AgentLoopState for testability
    /// </summary>
    private async IAsyncEnumerable<AgentEvent> RunAgenticLoopInternal(
        PreparedTurn turn,
        List<ChatMessage> turnHistory,
        TaskCompletionSource<IReadOnlyList<ChatMessage>> historyCompletionSource,
        Session? session = null,
        Thread? thread = null,
        Dictionary<string, object>? initialContextProperties = null,
        AgentRunConfig? runConfig = null,
        HPD.Events.IEventCoordinator? eventCoordinator = null,
        string? clientInputId = null,
        ActiveRuntimeInput? activeInput = null,
        ProviderOperationAccountingBridge? accountingBridge = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        AgentChatClientHandle? inheritedChatClient = null,
        ClientFamilyInheritanceMode inheritedChatMode = ClientFamilyInheritanceMode.UseOwn)
    {
        eventCoordinator ??= _eventCoordinator;
        var orchestrationStartTime = DateTime.UtcNow;

        // Create orchestration activity to group all agent turns and function calls
        using var orchestrationActivity = ActivitySource.StartActivity(
            "agent.orchestration",
            ActivityKind.Internal);

        orchestrationActivity?.SetTag("agent.id", AgentId);
        orchestrationActivity?.SetTag("agent.name", _name);
        orchestrationActivity?.SetTag("agent.provider", ProviderKey);
        orchestrationActivity?.SetTag("agent.model", ModelId);

        // Track root agent for event bubbling across nested agent calls
        var previousRootAgent = RootAgent;
        RootAgent ??= this;

        // Initialize execution context if this agent does not already have one.
        // SubAgent wrappers stamp child metadata before running; direct/root agent
        // runs get a root metadata record here.
        if (AgentMetadata == null)
        {
            AgentMetadata = new AgentMetadata
            {
                AgentName = _name,
                AgentId = AgentId,
                ParentAgentId = null,
                AgentChain = new[] { _name },
                Depth = 0
            };
        }

        IReadOnlyList<ChatMessage> messages = turn.MessagesForLLM;
        var newInputMessages = turn.NewInputMessages;

        // Create linked cancellation token for turn timeout
        using var turnCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var activeTurnId = Interlocked.Increment(ref _nextActiveTurnId);
        if (!_activeTurnCancellations.TryAdd(activeTurnId, turnCts))
            throw new InvalidOperationException("Failed to register the active agent turn.");
        if (Volatile.Read(ref _disposeState) != 0)
        {
            _activeTurnCancellations.TryRemove(activeTurnId, out _);
            throw new ObjectDisposedException(nameof(Agent));
        }
        if (Config?.AgenticLoop?.MaxTurnDuration is { } turnTimeout)
        {
            turnCts.CancelAfter(turnTimeout);
        }
        var effectiveCancellationToken = turnCts.Token;

        // Generate IDs for this message turn
        var messageTurnId = Guid.NewGuid().ToString();

        // Generate OTel-compatible trace/span IDs for this turn.
        // traceId is shared across every event in this execution.
        // turnSpanId is the root span; iteration and tool-call spans nest beneath it.
        var traceId    = GenerateTraceId();
        var turnSpanId = GenerateSpanId();

        // Extract conversation ID from turn.Options, session, or generate new one
        string conversationId;
        if (turn.Options?.AdditionalProperties?.TryGetValue("ConversationId", out var convIdObj) == true && convIdObj is string convId)
        {
            conversationId = convId;
        }
        else if (session != null)
        {
            // Use session ID as conversation ID ( ConversationId removed from Session)
            conversationId = session.Id;
        }
        else
        {
            conversationId = Guid.NewGuid().ToString();
        }

        var isResumeTurn = newInputMessages.Count == 0 && thread?.Messages.Count > 0;
        AgentChatClientLease? chatClientLease = null;
        AgentClientSet? runClientSet = null;
        var toolHarnessExecutionScope = ToolHarnessExecutionScope.Create(
            _serviceProvider,
            exception => _agentLogger?.LogError(exception, "ToolHarness execution cleanup failed after input completion."));
        var toolHarnessExecutionId = Interlocked.Increment(ref _nextToolHarnessExecutionId);
        if (!_toolHarnessExecutionCompletions.TryAdd(toolHarnessExecutionId, toolHarnessExecutionScope.Completion))
            throw new InvalidOperationException("Failed to track ToolHarness execution cleanup.");
        _ = toolHarnessExecutionScope.Completion.ContinueWith(
            (_, state) =>
            {
                var tuple = ((ConcurrentDictionary<long, Task> Completions, long Id))state!;
                if (tuple.Completions.TryGetValue(tuple.Id, out var completion) && completion.IsCompletedSuccessfully)
                    tuple.Completions.TryRemove(tuple.Id, out _);
            },
            (_toolHarnessExecutionCompletions, toolHarnessExecutionId),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        var toolHarnessDeactivationReason = ToolHarnessDeactivationReason.Failed;

        try
        {
            // Emit MESSAGE TURN started event
            yield return new MessageTurnStartedEvent(
                messageTurnId,
                conversationId,
                AgentId,
                _name)
            {
                TraceId      = traceId,
                SpanId       = turnSpanId,
                ParentSpanId = null   // root span
            };
            using var providerAccountingScope = accountingBridge?.Collector is { } collector
                ? ProviderOperationAccountingScope.Push(collector)
                : null;

            AgentLoopState state;
            IEnumerable<ChatMessage> effectiveMessages;
            ChatOptions? effectiveOptions;
            // Shared mutable message list - all contexts reference this same list (zero-sync architecture)
            List<ChatMessage> sharedMessages;

            // Load persistent middleware state from session + thread (split by scope).
            var sessionState = MiddlewareState.LoadFromSession(session, _stateFactories);
            var threadState = MiddlewareState.LoadFromThread(thread, _stateFactories);
            var persistentState = sessionState.Merge(threadState);

            // Initialize state from durable thread history. Crash recovery is represented by
            // thread events and run/tool projections, not a separate uncommitted loop snapshot.
            sharedMessages = new List<ChatMessage>(messages);
            state = AgentLoopState.Initial(sharedMessages, messageTurnId, conversationId, this.Name, persistentState);

            // Use PreparedTurn's already-prepared messages and options
            effectiveMessages = turn.MessagesForLLM;
            effectiveOptions = turn.Options;
            var capabilityOverlay = AgentTurnCapabilityOverlay.Compose(
                turn.CatalogLease?.Snapshot,
                effectiveOptions?.Tools,
                runConfig?.RuntimeTools,
                runConfig?.Tools?.Additional);
            effectiveOptions = effectiveOptions?.Clone() ?? new ChatOptions();
            effectiveOptions.Tools = capabilityOverlay.Tools.ToArray();
            yield return new AgentTurnCapabilitiesPinnedEvent
            {
                Identity = capabilityOverlay.Identity,
                TraceId = traceId,
                SpanId = turnSpanId
            };

            //
            // BUILD CONFIGURATION & DECISION ENGINE (common to both paths)
            //

            var config = BuildDecisionConfiguration(effectiveOptions);
            var decisionEngine = new AgentDecisionEngine();

            // INITIALIZE TURN HISTORY: Add only NEW input messages (Option 2 pattern)
            // All NEW messages from this turn will be saved to session at the end
            // PreparedTurn separates MessagesForLLM (full history) from NewInputMessages (to persist)

            foreach (var msg in newInputMessages)
            {
                turnHistory.Add(msg);
            }

            ChatResponse? lastResponse = null;

            // Collect all response updates to build final history
            var responseUpdates = new List<ChatResponseUpdate>();
            string? currentAssistantMessageId = null;

            var effectiveRunConfig = runConfig ?? new AgentRunConfig();
            if (ResolveModelTransport(effectiveRunConfig) is Middleware.AgentModelTransport.Chat)
            {
                chatClientLease = await _chatClientResolver.ResolveAsync(
                    new AgentChatClientResolutionRequest
                    {
                        AgentConfig = Config ?? throw new InvalidOperationException("Agent configuration is not available."),
                        RunConfig = effectiveRunConfig,
                        BuilderDefault = _defaultChatClientHandle,
                        ParentResolved = inheritedChatClient,
                        ParentInheritance = inheritedChatMode
                    },
                    effectiveCancellationToken).ConfigureAwait(false);
                if (chatClientLease.Handle.ExecutionIdentity is null)
                    throw new AgentRunConfigurationException(
                        "subagent_provider_attribution_missing",
                        "clients.chat",
                        "The selected chat client must declare a safe provider/backend/adapter execution identity.");
            }
            runClientSet = await ResolveRunClientSetV9Async(effectiveRunConfig, effectiveCancellationToken)
                .ConfigureAwait(false);
            var effectiveClientSet = runClientSet ?? _clientSet;

            // Resolve background responses settings from AgentRunConfig → Config → false
            var allowBackgroundResponses = runConfig?.BackgroundResponses?.Allow
                ?? Config?.BackgroundResponses?.DefaultAllow
                ?? false;

            // BACKGROUND RESPONSES VALIDATION: Log warnings for common mistakes
            // Philosophy: "Let it flow" - warn via logging but don't block, allow graceful degradation
            ValidateBackgroundResponsesUsage(runConfig, allowBackgroundResponses, newInputMessages.Count);

            // Apply background responses settings to effectiveOptions
            // Note: This requires pragma suppression for experimental M.E.AI feature
            if (allowBackgroundResponses || runConfig?.BackgroundResponses?.ContinuationToken != null)
            {
                effectiveOptions = ApplyBackgroundResponsesOptions(
                    effectiveOptions,
                    allowBackgroundResponses,
                    runConfig?.BackgroundResponses?.ContinuationToken);
            }

            // OBSERVABILITY: Start telemetry and logging

            var turnStopwatch = System.Diagnostics.Stopwatch.StartNew();


            // INITIALIZE AGENT CONTEXT (V2 - Single unified context for entire turn)
            // This replaces the dual-context system (turnContext + middlewareContext)
            var agentContext = new Middleware.AgentContext(
                agentName: _name,
                conversationId: conversationId,
                initialState: state,
                eventCoordinator: eventCoordinator,
                threadEvents: Config?.SessionStore is { } turnStore
                    ? CreateEventPublisher(turnStore, eventCoordinator)
                    : null,
                session: session,
                thread: thread,
                cancellationToken: effectiveCancellationToken,
                effectiveChatClient: chatClientLease?.Handle,
                chatClientResolver: _chatClientResolver,
                services: toolHarnessExecutionScope.Services,
                runtimeCapabilities: _runtimeContext?.RuntimeCapabilities,
                traceId: traceId,                // Propagate trace ID to all middleware-emitted events
                threadExecutionId: activeInput?.ThreadExecutionId,
                agentId: AgentId,
                parentAgentMetadata: AgentMetadata,
                parentAgentStore: Config?.AgentStore,
                config: Config,
                clientSet: effectiveClientSet,
                contentStore: _contentStore,
                structEvents: GetActiveStructEvents(),
                inputHandler: async (input, ct) =>
                    _ = await RunAsync(TargetActiveExecution(input), ct).ConfigureAwait(false),
                toolHarnessExecutionScope: toolHarnessExecutionScope,
                agentResources: _agentResources.Resources);

            // IMPORTANT: Create runConfig instance ONCE and reuse it throughout the entire turn
            // Middleware may modify the consolidated per-run concern objects.
            // We must use the SAME instance for BeforeMessageTurnAsync and BeforeIterationAsync
            var turnPipeline = BuildTurnMiddlewarePipeline(effectiveRunConfig);
            var functionCallProcessor = ReferenceEquals(turnPipeline, _middlewarePipeline)
                ? _functionCallProcessor
                : CreateFunctionCallProcessorForPipeline(
                    eventCoordinator ?? _eventCoordinator,
                    turnPipeline);

            // MIDDLEWARE: BeforeMessageTurnAsync (turn-level hook)
            // Pass shared message list - middleware mutations are visible to all immediately
            var beforeTurnContext = agentContext.AsBeforeMessageTurn(
                inputMessages: newInputMessages,
                conversationHistory: sharedMessages,  // SAME shared list, no copy
                runConfig: effectiveRunConfig);

            var originalUserInputs = newInputMessages
                .Where(static message => message.GetSource() == AgentMessageSource.UserInput)
                .ToArray();
            var originalRuntimeInputs = newInputMessages
                .Where(static message => message.GetSource() != AgentMessageSource.UserInput)
                .ToArray();

            await turnPipeline.ExecuteBeforeMessageTurnAsync(beforeTurnContext, effectiveCancellationToken);

            // V2: State updates are immediate - no GetPendingState() needed!
            state = agentContext.State;

            if (beforeTurnContext.UserInputMessages.Count != originalUserInputs.Length ||
                beforeTurnContext.RuntimeContextMessages.Count != originalRuntimeInputs.Length)
            {
                throw new InvalidOperationException(
                    "Before-message-turn middleware may replace current input messages but cannot add or remove them.");
            }

            foreach (var runtimeMessage in beforeTurnContext.RuntimeContextMessages)
            {
                if (runtimeMessage.GetSource() == AgentMessageSource.BackgroundNotification &&
                    (runtimeMessage.Role != ChatRole.System ||
                     runtimeMessage.GetVisibility() != AgentMessageVisibility.Hidden ||
                     runtimeMessage.GetPersistence() != AgentMessagePersistence.ModelContextOnly))
                {
                    throw new InvalidOperationException(
                        "Background notification middleware must preserve system role, hidden visibility, and model-context-only persistence.");
                }
            }

            var inputReplacements = originalUserInputs
                .Zip(beforeTurnContext.UserInputMessages)
                .Concat(originalRuntimeInputs.Zip(beforeTurnContext.RuntimeContextMessages));
            foreach (var (originalMessage, replacementMessage) in inputReplacements)
            {
                if (ReferenceEquals(originalMessage, replacementMessage))
                    continue;

                for (var i = 0; i < turnHistory.Count; i++)
                {
                    if (ReferenceEquals(turnHistory[i], originalMessage))
                    {
                        turnHistory[i] = replacementMessage;
                        break;
                    }
                }

                for (var i = 0; i < sharedMessages.Count; i++)
                {
                    if (ReferenceEquals(sharedMessages[i], originalMessage))
                    {
                        sharedMessages[i] = replacementMessage;
                        break;
                    }
                }

                if (effectiveMessages is List<ChatMessage> effectiveList &&
                    !ReferenceEquals(effectiveList, sharedMessages))
                {
                    for (var i = 0; i < effectiveList.Count; i++)
                    {
                        if (ReferenceEquals(effectiveList[i], originalMessage))
                        {
                            effectiveList[i] = replacementMessage;
                            break;
                        }
                    }
                }
            }

            await CommitThreadMessagesAsync(
                session,
                thread,
                turnHistory,
                clientInputId,
                eventCoordinator,
                effectiveCancellationToken).ConfigureAwait(false);

            var realtimeTranscriptTargetMessageId = ResolveRealtimeTranscriptTargetMessageId(turnHistory);

            // Shared reference architecture: No sync needed!
            // state.CurrentMessages already sees middleware changes via MessagesRef
            // effectiveMessages updated to point to same shared list for downstream use
            effectiveMessages = sharedMessages;

            // MAIN AGENTIC LOOP (Hybrid: Pure Decisions + Inline Execution)
            // NOTE: Iteration limit enforcement is handled by ContinuationPermissionMiddleware.
            // The middleware checks the limit and requests user permission to continue.
            // This allows clean separation: loop continues until middleware signals termination.

            while (!state.IsTerminated)
            {
                if (await DrainAcceptedTurnContinuationsAsync(
                        activeInput,
                        sharedMessages,
                        turnHistory,
                        session,
                        thread,
                        eventCoordinator,
                        effectiveCancellationToken).ConfigureAwait(false))
                {
                    lastResponse = null;
                }

                // Generate message ID for this iteration
                var assistantMessageId = Guid.NewGuid().ToString();
                currentAssistantMessageId = assistantMessageId;
                var iterSpanId         = GenerateSpanId();

                // Emit iteration start
                yield return new AgentTurnStartedEvent(state.Iteration)
                {
                    TraceId      = traceId,
                    SpanId       = iterSpanId,
                    ParentSpanId = turnSpanId
                };

                // Emit state snapshot for testing/debugging
                yield return new StateSnapshotEvent(
                    CurrentIteration: state.Iteration,
                    MaxIterations: state.MiddlewareState.ContinuationPermission()?.CurrentExtendedLimit ?? config.MaxIterations,
                    IsTerminated: state.IsTerminated,
                    TerminationReason: state.TerminationReason,
                    ConsecutiveErrorCount: state.MiddlewareState.ErrorTracking()?.ConsecutiveFailures ?? 0,
                    CompletedFunctions: new List<string>(state.CompletedFunctions),
                    AgentName: _name)
                { TraceId = traceId };

                //
                // FUNCTIONAL CORE: Pure Decision (No I/O)
                //

                var decision = decisionEngine.DecideNextAction(state, lastResponse, config);

                //
                // OBSERVABILITY: Emit iteration and decision events
                //

                // Emit iteration start event
                yield return new IterationStartEvent(
                    AgentName: _name,
                    Iteration: state.Iteration,
                    MaxIterations: config.MaxIterations,
                    CurrentMessageCount: state.CurrentMessages.Count,
                    HistoryMessageCount: 0, // History is part of CurrentMessages
                    TurnHistoryMessageCount: state.TurnHistory.Count,
                    CompletedFunctionsCount: state.CompletedFunctions.Count)
                { TraceId = traceId };

                // Emit decision event
                yield return new AgentDecisionEvent(
                    AgentName: _name,
                    DecisionType: decision.GetType().Name,
                    Iteration: state.Iteration,
                    ConsecutiveFailures: state.MiddlewareState.ErrorTracking()?.ConsecutiveFailures ?? 0,
                    CompletedFunctionsCount: state.CompletedFunctions.Count)
                { TraceId = traceId };

                // NOTE: Circuit breaker events are now emitted directly by CircuitBreakerIterationMiddleware
                // via context.PublishAsync() in BeforeToolExecutionAsync.

                //
                // ARCHITECTURAL DECISION: Inline Execution for Zero-Latency Streaming
                //
                //
                // LLM calls and tool execution happen INLINE (not extracted to methods)
                // to preserve real-time streaming. Extracting would add 200-3000ms latency
                // due to buffering events before returning them.
                //

                if (decision is AgentDecision.CallLLM)
                {

                    // Select the provider input for this iteration.
                    IEnumerable<ChatMessage> messagesToSend;
                    int messageCountToSend;

                    if (state.InnerClientTracksHistory && state.Iteration > 0)
                    {
                        // Server manages history - send only delta (new messages since last call)
                        messagesToSend = state.CurrentMessages.Skip(state.MessagesSentToInnerClient);
                        messageCountToSend = state.CurrentMessages.Count;  // Total count including previous
                    }
                    else if (state.Iteration == 0)
                    {
                        // The first iteration begins with the prepared thread history and new input.
                        // BeforeIteration middleware performs any final provider-input projection.
                        messagesToSend = effectiveMessages;
                        messageCountToSend = effectiveMessages.Count();
                    }
                    else
                    {
                        // Subsequent iterations begin from current loop history. BeforeIteration
                        // middleware owns the final provider-bound projection.
                        messagesToSend = state.CurrentMessages;
                        messageCountToSend = state.CurrentMessages.Count;
                    }

                    var modelVisibleMessages = messagesToSend as List<ChatMessage> ?? messagesToSend.ToList();

                    // ═══════════════════════════════════════════════════════════════
                    // RUNTIME TOOL MODE OVERRIDE (for structured output tool/union mode)
                    // Forces LLM to call a tool - provider-enforced, not prompt-based
                    // Only apply on first iteration - subsequent iterations follow same mode
                    // ═══════════════════════════════════════════════════════════════
                    // Public Tools.Mode takes precedence over internal RuntimeToolMode.
                    var toolModeOverride = runConfig?.Tools?.Mode ?? runConfig?.RuntimeToolMode;
                    if (toolModeOverride != null && state.Iteration == 0)
                    {
                        effectiveOptions = effectiveOptions?.Clone() ?? new ChatOptions();
                        effectiveOptions.ToolMode = toolModeOverride;
                    }

                    // UPDATE AGENT CONTEXT STATE (sync state changes from previous iteration)
                    agentContext.SyncState(state);

                    // Snapshot the message set before BeforeIteration middleware runs so the
                    // observability event can report only newly injected context, not chat history.
                    var preIterationMessages = modelVisibleMessages.ToList();

                    // CREATE TYPED ITERATION CONTEXT (V2)
                    // Note: Tool Collapsing is handled by ToolCollapsingMiddleware in BeforeIterationAsync
                    // The middleware will filter tools and emit CollapsedToolsVisibleEvent
                    // Pass shared message list - middleware mutations visible to all immediately
                    var beforeIterationContext = agentContext.AsBeforeIteration(
                        iteration: state.Iteration,
                        messages: modelVisibleMessages,
                        options: effectiveOptions ?? new ChatOptions(),
                        runConfig: effectiveRunConfig);  // Use the SAME instance from BeforeMessageTurnAsync

                    // EXECUTE BEFORE ITERATION MIDDLEWARES
                    await turnPipeline.ExecuteBeforeIterationAsync(
                        beforeIterationContext,
                        effectiveCancellationToken).ConfigureAwait(false);

                    // V2: State updates are immediate - no GetPendingState() needed!
                    state = agentContext.State;

                    // BeforeIteration middleware owns the exact model-visible list. This lets
                    // middleware resolve durable HPD content refs for the provider request
                    // without rewriting persisted thread history.
                    messagesToSend = beforeIterationContext.Messages;
                    var CollapsedOptions = beforeIterationContext.Options;


                    // Helper for toolharness name lookup in events
                    // Try collapsed tools first, then fall back to original (pre-collapse) tools
                    string? LookupToolHarness(string? functionName)
                    {
                        var result = functionCallProcessor.LookupToolHarnessName(functionName, CollapsedOptions?.Tools);
                        if (result == null)
                        {
                            // Function not found in collapsed view - try original tools
                            result = functionCallProcessor.LookupToolHarnessName(functionName, effectiveOptions?.Tools);
                        }
                        return result;
                    }

                    ToolCallType? LookupCallType(string? functionName)
                    {
                        var result = functionCallProcessor.LookupToolCallType(functionName, CollapsedOptions?.Tools);
                        if (result == null)
                            result = functionCallProcessor.LookupToolCallType(functionName, effectiveOptions?.Tools);
                        return result;
                    }

                    // Streaming state
                    var assistantContents = new List<AIContent>();
                    var toolRequests = new List<FunctionCallContent>();
                    bool messageStarted = false;
                    bool reasoningMessageStarted = false;
                    AgentOperation? providerResponseOperation = FindProviderResponseOperation(
                        runConfig?.BackgroundResponses?.ContinuationToken);
                    ResponseContinuationToken? lastContinuationToken = null;
                    Middleware.AgentModelTurnRequest? currentModelRequest = null;
                    Middleware.IAgentModelTurnExecutor? currentModelTurnExecutor = null;
                    var logicalModelOperationId = Guid.NewGuid().ToString("N");
                    var physicalModelAttempt = 0;

                    // Execute LLM call (unless skipped by Middleware)

                    if (beforeIterationContext.SkipLLMCall)
                    {
                        // Use cached/provided response from Middleware
                        if (beforeIterationContext.OverrideResponse != null)
                        {
                            assistantContents.AddRange(beforeIterationContext.OverrideResponse.Contents);

                            // Emit events for middleware-provided response (matching normal LLM flow)
                            foreach (var content in beforeIterationContext.OverrideResponse.Contents)
                            {
                                if (content is TextReasoningContent reasoning && !string.IsNullOrEmpty(reasoning.Text))
                                {
	                                    if (!reasoningMessageStarted)
	                                    {
	                                        yield return new ReasoningMessageStartEvent(
	                                            MessageId: assistantMessageId,
	                                            Role: "assistant")
                                        { TraceId = traceId };
                                        reasoningMessageStarted = true;
                                    }

	                                    yield return new ReasoningDeltaEvent(
	                                        Text: reasoning.Text,
	                                        MessageId: assistantMessageId,
	                                        ProtectedData: reasoning.ProtectedData)
	                                    { TraceId = traceId };
                                }
                                else if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                                {
	                                    if (reasoningMessageStarted)
	                                    {
	                                        yield return new ReasoningMessageEndEvent(
	                                            MessageId: assistantMessageId)
                                        { TraceId = traceId };
                                        reasoningMessageStarted = false;
                                    }

                                    if (!messageStarted)
                                    {
                                        yield return new TextMessageStartEvent(assistantMessageId, "assistant", AgentMessageSource.AssistantOutput, AgentMessageVisibility.Transcript) { TraceId = traceId };
                                        messageStarted = true;
                                    }
                                    yield return new TextDeltaEvent(textContent.Text, assistantMessageId) { TraceId = traceId };
                                }
                                else if (content is FunctionCallContent functionCall)
                                {
	                                    if (reasoningMessageStarted)
	                                    {
	                                        yield return new ReasoningMessageEndEvent(
	                                            MessageId: assistantMessageId)
                                        { TraceId = traceId };
                                        reasoningMessageStarted = false;
                                    }

                                    if (!messageStarted)
                                    {
                                        yield return new TextMessageStartEvent(assistantMessageId, "assistant", AgentMessageSource.AssistantOutput, AgentMessageVisibility.Transcript) { TraceId = traceId };
                                        messageStarted = true;
                                    }
                                    yield return new ToolCallStartEvent(
                                        functionCall.CallId,
                                        functionCall.Name ?? string.Empty,
                                        assistantMessageId,
                                        LookupToolHarness(functionCall.Name),
                                        LookupCallType(functionCall.Name))
                                    {
                                        TraceId      = traceId,
                                        SpanId       = GenerateSpanId(),
                                        ParentSpanId = iterSpanId
                                    };

                                    if (functionCall.Arguments != null && functionCall.Arguments.Count > 0)
                                    {
                                        var argsJson = FunctionCallArgumentSerializer.Serialize(functionCall);
                                        yield return new ToolCallArgsEvent(functionCall.CallId, argsJson) { TraceId = traceId };
                                    }
                                }
                            }

	                            if (reasoningMessageStarted)
	                            {
	                                yield return new ReasoningMessageEndEvent(
	                                    MessageId: assistantMessageId)
                                { TraceId = traceId };
                                reasoningMessageStarted = false;
                            }
                        }
                        // Tool calls come from the override response
                        if (beforeIterationContext.OverrideResponse != null)
                        {
                            toolRequests.AddRange(beforeIterationContext.OverrideResponse.Contents
                                .OfType<FunctionCallContent>());
                        }
                    }
                    else
                    {
                        // CREATE MODEL REQUEST (V2 - immutable request pattern)
                        var selectedTransport = ResolveModelTransport(effectiveRunConfig);
                        var chatModel = selectedTransport is Middleware.AgentModelTransport.Chat
                            ? chatClientLease?.Client
                            : null;
                        var realtimeModel = selectedTransport is Middleware.AgentModelTransport.Realtime &&
                            effectiveClientSet is not null
                            ? await effectiveClientSet.GetRealtimeAsync(
                                effectiveCancellationToken).ConfigureAwait(false)
                            : null;

                        if (selectedTransport is Middleware.AgentModelTransport.Chat && chatModel is null)
                        {
                            throw new InvalidOperationException(
                                "No chat model is configured for this agent run. Configure Clients.Chat on AgentConfig or AgentRunConfig, including Clients.Chat.Override when supplying a client directly.");
                        }

                        if (selectedTransport is Middleware.AgentModelTransport.Realtime && realtimeModel is null)
                        {
                            throw new InvalidOperationException(
                                "No realtime model is configured for this agent run. Configure Clients.Realtime on AgentConfig or AgentRunConfig.");
                        }

                        var modelMessages = ProjectMessagesForModelHistory(
                            messagesToSend,
                            Config?.IncludeReasoningInModelHistory == true);

                        var modelRequest = new Middleware.AgentModelTurnRequest
                        {
                            Transport = selectedTransport,
                            ChatModel = chatModel,
                            RealtimeModel = realtimeModel,
                            Messages = modelMessages,
                            Options = CollapsedOptions,
                            State = agentContext.State,
                            Iteration = state.Iteration,
                            EventFlows = eventCoordinator.EventFlows,
                            RunConfig = effectiveRunConfig,
                            EventCoordinator = eventCoordinator,
                            EventPublisher = agentContext.PublishAsync,
                            StructEvents = GetActiveStructEvents(),
                            Session = agentContext.Session,
                            ContentStore = _contentStore,
                            ClientSet = effectiveClientSet
                        };

                        var modelTurnExecutor = selectedTransport is Middleware.AgentModelTransport.Realtime
                            ? (Middleware.IAgentModelTurnExecutor)_realtimeProviderProtocolParticipant
                            : _chatModelTurnExecutor;
                        currentModelRequest = modelRequest;
                        currentModelTurnExecutor = modelTurnExecutor;

                        var contextMessages = BuildContextMessageSnapshots(
                            modelRequest.Messages,
                            preIterationMessages);
                        var toolSnapshots = BuildToolContextSnapshots(modelRequest.Options);

                        yield return new IterationContextSnapshotEvent(
                            AgentName: _name,
                            Iteration: state.Iteration,
                            TotalMessageCount: modelRequest.Messages.Count,
                            ContextMessageCount: contextMessages.Count,
                            ContextMessages: contextMessages,
                            Instructions: modelRequest.Options?.Instructions,
                            ToolCount: toolSnapshots.Count,
                            Tools: toolSnapshots,
                            Timestamp: DateTimeOffset.UtcNow)
                        {
                            TraceId = traceId,
                            SpanId = GenerateSpanId(),
                            ParentSpanId = iterSpanId
                        };

                        yield return BuildMiddlewareStateSnapshotEvent(
                            agentName: _name,
                            stateFactories: _stateFactories,
                            state: modelRequest.State.MiddlewareState,
                            sessionId: agentContext.Session?.Id,
                            threadId: agentContext.Thread?.Id,
                            iteration: state.Iteration,
                            phase: "before_model_call",
                            batchId: null,
                            functionCallId: null,
                            toolCallIndex: null) with
                        {
                            TraceId = traceId,
                            SpanId = GenerateSpanId(),
                            ParentSpanId = iterSpanId
                        };

                        // [AGENT] DEBUG: Log exact payload being sent to LLM
                        if (_agentLogger?.IsEnabled(LogLevel.Debug) == true)
                        {
                            _agentLogger.LogDebug(
                                "[AGENT] Iteration {Iteration} - EXACT PAYLOAD TO LLM:\n" +
                                "  Messages ({MessageCount}):\n{Messages}\n" +
                                "  Tools ({ToolCount}): {Tools}\n" +
                                "  Instructions: {Instructions}",
                                state.Iteration,
                                modelRequest.Messages.Count,
                                FormatMessagesForLLMLogging(modelRequest.Messages),
                                modelRequest.Options?.Tools?.Count ?? 0,
                                modelRequest.Options?.Tools != null
                                    ? string.Join(", ", modelRequest.Options.Tools.OfType<AIFunction>().Select(t => t.Name))
                                    : "<none>",
                                modelRequest.Options?.Instructions?.Length > 200
                                    ? modelRequest.Options.Instructions.Substring(0, 200) + "..."
                                    : modelRequest.Options?.Instructions ?? "<none>");
                        }

                        // Check if we should coalesce deltas (run options override config default)
                        bool coalesceDeltas = effectiveRunConfig.Streaming?.CoalesceDeltas ?? Config?.CoalesceDeltas ?? false;

                        static ChatResponseUpdate? ToChatResponseUpdate(Middleware.AgentModelUpdate modelUpdate)
                        {
                            if (modelUpdate.ChatUpdate is { } chatUpdate)
                                return chatUpdate;

                            return modelUpdate switch
                            {
                                Middleware.AgentTextDeltaUpdate text when !string.IsNullOrEmpty(text.Text) =>
                                    new ChatResponseUpdate
                                    {
                                        Contents = [new TextContent(text.Text)],
                                        FinishReason = text.IsFinal ? ChatFinishReason.Stop : null
                                    },
                                Middleware.AgentReasoningDeltaUpdate reasoning when !string.IsNullOrEmpty(reasoning.Text) =>
                                    new ChatResponseUpdate
                                    {
                                        Contents = [new TextReasoningContent(reasoning.Text)],
                                        FinishReason = reasoning.IsFinal ? ChatFinishReason.Stop : null
                                    },
                                Middleware.AgentToolCallUpdate toolCall when toolCall.IsFinal =>
                                    new ChatResponseUpdate
                                    {
                                        Contents = [toolCall.Call]
                                    },
                                Middleware.AgentResponseLifecycleUpdate lifecycle
                                    when lifecycle.State is Middleware.AgentModelResponseState.Failed =>
                                    throw lifecycle.Error ?? new InvalidOperationException("Realtime model response failed."),
                                Middleware.AgentResponseLifecycleUpdate lifecycle
                                    when lifecycle.State is Middleware.AgentModelResponseState.Cancelled =>
                                    throw lifecycle.Error ?? new OperationCanceledException("Realtime model response was cancelled."),
                                Middleware.AgentResponseLifecycleUpdate lifecycle
                                    when lifecycle.State is Middleware.AgentModelResponseState.Completed =>
                                    new ChatResponseUpdate { FinishReason = ChatFinishReason.Stop },
                                Middleware.AgentAudioDeltaUpdate => null,
                                Middleware.AgentUsageUpdate usage when usage.Usage is not null =>
                                    new ChatResponseUpdate
                                    {
                                        Contents = [new UsageContent(usage.Usage)]
                                    },
                                _ => null
                            };
                        }

                        async IAsyncEnumerable<Middleware.AgentModelUpdate> ExecuteAccountedModelAttempt(
                            Middleware.AgentModelTurnRequest request,
                            [EnumeratorCancellation] CancellationToken attemptCancellationToken = default)
                        {
                            var attemptNumber = Interlocked.Increment(ref physicalModelAttempt);
                            var family = request.Transport is Middleware.AgentModelTransport.Realtime
                                ? Providers.ProviderClientFamily.Realtime
                                : Providers.ProviderClientFamily.Chat;
                            var selected = Config?.ResolveClientConfig(family, effectiveRunConfig.Clients);
                            var selectedIdentity = family is Providers.ProviderClientFamily.Chat
                                ? chatClientLease?.Handle.ExecutionIdentity
                                : effectiveClientSet?.GetExecutionIdentity(family);
                            if (selectedIdentity is null)
                                throw new AgentRunConfigurationException(
                                    "subagent_provider_attribution_missing",
                                    $"clients.{family}",
                                    "The selected runtime client has no safe execution identity.");
                            var operationId = Guid.NewGuid().ToString("N");
                            ProviderOperationAccountingScope.Current?.RegisterAttempt(new(
                                operationId, logicalModelOperationId, attemptNumber,
                                family is Providers.ProviderClientFamily.Realtime
                                    ? ProviderOperationKind.RealtimeModelResponse
                                    : ProviderOperationKind.ChatModelResponse,
                                family,
                                selectedIdentity.ProviderKey,
                                selectedIdentity.ModelName));
                            ProviderUsageAccumulator? usageAccumulator = null;
                            UsageUpdateSemantics ResolveAttemptUsageSemantics()
                            {
                                var declaration = family is ProviderClientFamily.Realtime
                                    ? request.RealtimeModel?.GetService(typeof(ProviderStreamingUsageSemanticsDeclaration))
                                        as ProviderStreamingUsageSemanticsDeclaration
                                    : request.ChatModel?.GetService(typeof(ProviderStreamingUsageSemanticsDeclaration))
                                        as ProviderStreamingUsageSemanticsDeclaration;
                                return ProviderStreamingUsageSemanticsCatalog.Resolve(
                                    selectedIdentity.ProviderKey, family, declaration);
                            }
                            var transcriptionAttempts = new Dictionary<string, (string OperationId, UsageDetails? Usage)>(StringComparer.Ordinal);
                            string? attemptModelId = selectedIdentity.ModelName;
                            string? attemptResponseId = null;
                            await using var attemptEnumerator = modelTurnExecutor
                                .RunAsync(request, attemptCancellationToken)
                                .GetAsyncEnumerator(attemptCancellationToken);
                            while (true)
                            {
                                bool moved = false;
                                Exception? failure = null;
                                try
                                {
                                    moved = await attemptEnumerator.MoveNextAsync().ConfigureAwait(false);
                                }
                                catch (Exception exception)
                                {
                                    failure = exception;
                                }

                                if (failure is not null)
                                {
                                    foreach (var (itemId, transcription) in transcriptionAttempts)
                                    {
                                        accountingBridge.EnqueueTerminal(new ProviderOperationUsageEvent(
                                            messageTurnId, transcription.OperationId, itemId, 1,
                                            ProviderOperationKind.RealtimeInputTranscription,
                                            ProviderClientFamily.Realtime,
                                            failure is OperationCanceledException
                                                ? ProviderOperationOutcome.Cancelled
                                                : ProviderOperationOutcome.Failed,
                                            transcription.Usage, selected?.Provider?.Key, selected?.ModelName, itemId));
                                    }
                                    transcriptionAttempts.Clear();
                                    accountingBridge?.EnqueueTerminal(new AgentTurnFinishedEvent(
                                        messageTurnId, state.Iteration, operationId, logicalModelOperationId,
                                        attemptNumber, family,
                                        failure is OperationCanceledException
                                            ? ProviderOperationOutcome.Cancelled
                                            : ProviderOperationOutcome.Failed,
                                        usageAccumulator?.Usage, selected?.Provider?.Key, attemptModelId, attemptResponseId)
                                    { TraceId = traceId, SpanId = iterSpanId, ParentSpanId = turnSpanId });
                                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                                }

                                if (!moved)
                                {
                                    foreach (var (itemId, transcription) in transcriptionAttempts)
                                    {
                                        accountingBridge.EnqueueTerminal(new ProviderOperationUsageEvent(
                                            messageTurnId, transcription.OperationId, itemId, 1,
                                            ProviderOperationKind.RealtimeInputTranscription,
                                            ProviderClientFamily.Realtime, ProviderOperationOutcome.Unknown,
                                            transcription.Usage, selected?.Provider?.Key, selected?.ModelName, itemId));
                                    }
                                    transcriptionAttempts.Clear();
                                    accountingBridge?.EnqueueTerminal(new AgentTurnFinishedEvent(
                                        messageTurnId, state.Iteration, operationId, logicalModelOperationId,
                                        attemptNumber, family, ProviderOperationOutcome.Succeeded,
                                        usageAccumulator?.Usage, selected?.Provider?.Key, attemptModelId, attemptResponseId)
                                    { TraceId = traceId, SpanId = iterSpanId, ParentSpanId = turnSpanId });
                                    yield break;
                                }

                                var current = attemptEnumerator.Current;
                                if (current.ChatUpdate is { } chatUpdate)
                                {
                                    attemptModelId = chatUpdate.ModelId ?? attemptModelId;
                                    attemptResponseId = chatUpdate.ResponseId ?? attemptResponseId;
                                    foreach (var usageContent in chatUpdate.Contents.OfType<UsageContent>())
                                    {
                                        (usageAccumulator ??= new ProviderUsageAccumulator(
                                            ResolveAttemptUsageSemantics()))
                                            .Observe(usageContent.Details);
                                    }
                                }
                                if (current is Middleware.AgentUsageUpdate { Usage: { } reportedUsage })
                                {
                                    (usageAccumulator ??= new ProviderUsageAccumulator(
                                        ResolveAttemptUsageSemantics()))
                                        .Observe(reportedUsage);
                                }
                                if (current is AgentInputTranscriptUpdate transcript)
                                {
                                    var itemId = string.IsNullOrWhiteSpace(transcript.ItemId)
                                        ? $"transcription-{transcriptionAttempts.Count + 1}"
                                        : transcript.ItemId;
                                    if (!transcriptionAttempts.TryGetValue(itemId, out var transcription))
                                    {
                                        transcription = (Guid.NewGuid().ToString("N"), null);
                                        transcriptionAttempts.Add(itemId, transcription);
                                        ProviderOperationAccountingScope.Current?.RegisterAttempt(new(
                                            transcription.OperationId, itemId, 1,
                                            ProviderOperationKind.RealtimeInputTranscription,
                                            ProviderClientFamily.Realtime,
                                            selected?.Provider?.Key, selected?.ModelName));
                                    }
                                    if (transcript.Usage is not null)
                                    {
                                        transcription = (transcription.OperationId, transcript.Usage);
                                        transcriptionAttempts[itemId] = transcription;
                                    }
                                    if (transcript.Stage is AgentInputTranscriptStage.Final or AgentInputTranscriptStage.Failed)
                                    {
                                        accountingBridge.EnqueueTerminal(new ProviderOperationUsageEvent(
                                            messageTurnId, transcription.OperationId, itemId, 1,
                                            ProviderOperationKind.RealtimeInputTranscription,
                                            ProviderClientFamily.Realtime,
                                            transcript.Stage is AgentInputTranscriptStage.Failed
                                                ? ProviderOperationOutcome.Failed
                                                : ProviderOperationOutcome.Succeeded,
                                            transcription.Usage, selected?.Provider?.Key, selected?.ModelName, itemId));
                                        transcriptionAttempts.Remove(itemId);
                                    }
                                }
                                yield return current;
                            }
                        }

                        if (coalesceDeltas)
                        {
                            // COALESCE MODE: Buffer all updates, then emit coalesced events
                            await foreach (var modelUpdate in turnPipeline.ExecuteModelTurnStreamingAsync(
                                modelRequest,
                                (req) => ExecuteAccountedModelAttempt(req, effectiveCancellationToken),
                                effectiveCancellationToken))
                            {
                                if (modelUpdate is AgentInputTranscriptUpdate transcriptUpdate &&
                                    realtimeTranscriptTargetMessageId is { } transcriptMessageId)
                                {
                                    var transcriptEvent = CreateRealtimeTranscriptEvent(
                                        transcriptUpdate,
                                        transcriptMessageId,
                                        traceId);
                                    if (transcriptEvent != null)
                                    {
                                        yield return transcriptEvent;
                                    }

                                    if (transcriptUpdate.Stage is AgentInputTranscriptStage.Final &&
                                        !string.IsNullOrWhiteSpace(transcriptUpdate.Text))
                                    {
                                        if (ProjectRealtimeTranscriptIntoMessages(
                                            transcriptMessageId,
                                            transcriptUpdate.Text,
                                            turnHistory,
                                            sharedMessages) &&
                                            thread is not null)
                                        {
                                            await CommitAndPublishThreadEventAsync(
                                                thread,
                                                ThreadEventFactory.TextDelta(
                                                    thread.SessionId,
                                                    thread.Id,
                                                    messageTurnId,
                                                    transcriptMessageId,
                                                    transcriptUpdate.Text.Trim(),
                                                    state.Iteration),
                                                eventCoordinator,
                                                effectiveCancellationToken).ConfigureAwait(false);
                                        }
                                    }

                                    continue;
                                }

                                var update = ToChatResponseUpdate(modelUpdate);
                                if (update is null)
                                    continue;

                                // Store update for building final history
                                responseUpdates.Add(update);

                                // Check for background operation continuation token (M.E.AI 10.1.1+ strongly-typed)
#pragma warning disable MEAI001 // Experimental API - Background Responses
                                var continuationToken = update.ContinuationToken;
                                if (continuationToken != null)
                                {
                                    lastContinuationToken = continuationToken;

                                    if (providerResponseOperation is null && allowBackgroundResponses)
                                    {
                                        var now = DateTimeOffset.UtcNow;
                                        providerResponseOperation = await _operationRegistry.RegisterAsync(
                                            new AgentOperationSnapshot
                                        {
                                            OperationId = Guid.NewGuid().ToString("N"),
                                            ProviderOperationId = assistantMessageId,
                                            SourceKind = AgentOperationSourceKind.ProviderOperation,
                                            Name = "model.response",
                                            Address = new AgentExecutionAddress(
                                                AgentId, session?.Id ?? string.Empty, thread?.Id ?? string.Empty),
                                            OriginatingThreadExecutionId = activeInput?.ThreadExecutionId,
                                            ProviderStatus = AgentOperationProviderStatus.Running,
                                            ObservationStatus = AgentOperationObservationStatus.Attached,
                                            Control = new AgentOperationControl(
                                                assistantMessageId, AgentOperationKind.Provider,
                                                AgentOperationCapabilities.None),
                                            Notification = new AgentOperationNotificationPolicy
                                            {
                                                IncludeTerminal = true,
                                                DeduplicationKey = $"model.response:{assistantMessageId}"
                                            },
                                            RegisteredAt = now,
                                            StartedAt = now,
                                            UpdatedAt = now,
                                            Version = 0
                                        }, observer: new ProviderResponseObservation(continuationToken),
                                            cancellationToken: effectiveCancellationToken).ConfigureAwait(false);
                                    }
                                    else if (providerResponseOperation?.Observer is ProviderResponseObservation observation)
                                        observation.ContinuationToken = continuationToken;
                                }
#pragma warning restore MEAI001

                                // Accumulate content without emitting events yet
                                if (update.Contents != null)
                                {
                                    foreach (var content in update.Contents)
                                    {
                                        if (content is TextReasoningContent reasoning && !string.IsNullOrEmpty(reasoning.Text))
                                        {
                                            assistantContents.Add(reasoning);
                                        }
                                        else if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                                        {
                                            assistantContents.Add(textContent);
                                        }
                                        else if (content is FunctionCallContent functionCall)
                                        {
                                            toolRequests.Add(FunctionExecutionCore.NormalizeProviderFunctionCall(functionCall));
                                            assistantContents.Add(functionCall);
                                        }
                                    }
                                }
                            }

                            // Now coalesce and emit events
                            var coalescedContents = CoalesceTextContents(assistantContents);

                            foreach (var content in coalescedContents)
                            {
                                if (content is TextReasoningContent reasoning)
                                {
	                                    if (!reasoningMessageStarted)
	                                    {
	                                        yield return new ReasoningMessageStartEvent(
	                                            MessageId: assistantMessageId,
	                                            Role: "assistant")
                                        { TraceId = traceId };
                                        reasoningMessageStarted = true;
                                    }
	                                    yield return new ReasoningDeltaEvent(
	                                        Text: reasoning.Text,
	                                        MessageId: assistantMessageId,
	                                        ProtectedData: reasoning.ProtectedData)
	                                    { TraceId = traceId };
                                }
                                else if (content is TextContent textContent)
                                {
	                                    if (reasoningMessageStarted)
	                                    {
	                                        yield return new ReasoningMessageEndEvent(
	                                            MessageId: assistantMessageId)
                                        { TraceId = traceId };
                                        reasoningMessageStarted = false;
                                    }
                                    if (!messageStarted)
                                    {
                                        yield return new TextMessageStartEvent(assistantMessageId, "assistant", AgentMessageSource.AssistantOutput, AgentMessageVisibility.Transcript) { TraceId = traceId };
                                        messageStarted = true;
                                    }
                                    yield return new TextDeltaEvent(textContent.Text, assistantMessageId) { TraceId = traceId };
                                }
                                else if (content is FunctionCallContent functionCall)
                                {
	                                    if (reasoningMessageStarted)
	                                    {
	                                        yield return new ReasoningMessageEndEvent(
	                                            MessageId: assistantMessageId)
                                        { TraceId = traceId };
                                        reasoningMessageStarted = false;
                                    }
                                    if (!messageStarted)
                                    {
                                        yield return new TextMessageStartEvent(assistantMessageId, "assistant", AgentMessageSource.AssistantOutput, AgentMessageVisibility.Transcript) { TraceId = traceId };
                                        messageStarted = true;
                                    }

                                    yield return new ToolCallStartEvent(
                                        functionCall.CallId,
                                        functionCall.Name ?? string.Empty,
                                        assistantMessageId,
                                        LookupToolHarness(functionCall.Name),
                                        LookupCallType(functionCall.Name))
                                    {
                                        TraceId      = traceId,
                                        SpanId       = GenerateSpanId(),
                                        ParentSpanId = iterSpanId
                                    };

                                    if (functionCall.Arguments != null && functionCall.Arguments.Count > 0)
                                    {
                                        var argsJson = FunctionCallArgumentSerializer.Serialize(functionCall);

                                        yield return new ToolCallArgsEvent(functionCall.CallId, argsJson) { TraceId = traceId };
                                    }
                                }
                            }

	                            if (reasoningMessageStarted)
	                            {
	                                yield return new ReasoningMessageEndEvent(
	                                    MessageId: assistantMessageId)
                                { TraceId = traceId };
                                reasoningMessageStarted = false;
                            }
                        }
                        else
                        {
                            // STREAMING MODE: Emit immediately (existing behavior)
                            await foreach (var modelUpdate in turnPipeline.ExecuteModelTurnStreamingAsync(
                                modelRequest,
                                (req) => ExecuteAccountedModelAttempt(req, effectiveCancellationToken),
                                effectiveCancellationToken))
                            {
                                if (modelUpdate is AgentInputTranscriptUpdate transcriptUpdate &&
                                    realtimeTranscriptTargetMessageId is { } transcriptMessageId)
                                {
                                    var transcriptEvent = CreateRealtimeTranscriptEvent(
                                        transcriptUpdate,
                                        transcriptMessageId,
                                        traceId);
                                    if (transcriptEvent != null)
                                    {
                                        yield return transcriptEvent;
                                    }

                                    if (transcriptUpdate.Stage is AgentInputTranscriptStage.Final &&
                                        !string.IsNullOrWhiteSpace(transcriptUpdate.Text))
                                    {
                                        if (ProjectRealtimeTranscriptIntoMessages(
                                            transcriptMessageId,
                                            transcriptUpdate.Text,
                                            turnHistory,
                                            sharedMessages) &&
                                            thread is not null)
                                        {
                                            await CommitAndPublishThreadEventAsync(
                                                thread,
                                                ThreadEventFactory.TextDelta(
                                                    thread.SessionId,
                                                    thread.Id,
                                                    messageTurnId,
                                                    transcriptMessageId,
                                                    transcriptUpdate.Text.Trim(),
                                                    state.Iteration),
                                                eventCoordinator,
                                                effectiveCancellationToken).ConfigureAwait(false);
                                        }
                                    }

                                    continue;
                                }

                                var update = ToChatResponseUpdate(modelUpdate);
                                if (update is null)
                                    continue;

                                // Store update for building final history
                                responseUpdates.Add(update);

                                // Check for background operation continuation token (M.E.AI 10.1.1+ strongly-typed)
#pragma warning disable MEAI001 // Experimental API - Background Responses
                                var continuationToken = update.ContinuationToken;
                                if (continuationToken != null)
                                {
                                    lastContinuationToken = continuationToken;

                                    if (providerResponseOperation is null && allowBackgroundResponses)
                                    {
                                        var now = DateTimeOffset.UtcNow;
                                        providerResponseOperation = await _operationRegistry.RegisterAsync(
                                            new AgentOperationSnapshot
                                        {
                                            OperationId = Guid.NewGuid().ToString("N"),
                                            ProviderOperationId = assistantMessageId,
                                            SourceKind = AgentOperationSourceKind.ProviderOperation,
                                            Name = "model.response",
                                            Address = new AgentExecutionAddress(
                                                AgentId, session?.Id ?? string.Empty, thread?.Id ?? string.Empty),
                                            OriginatingThreadExecutionId = activeInput?.ThreadExecutionId,
                                            ProviderStatus = AgentOperationProviderStatus.Running,
                                            ObservationStatus = AgentOperationObservationStatus.Attached,
                                            Control = new AgentOperationControl(
                                                assistantMessageId, AgentOperationKind.Provider,
                                                AgentOperationCapabilities.None),
                                            Notification = new AgentOperationNotificationPolicy
                                            {
                                                IncludeTerminal = true,
                                                DeduplicationKey = $"model.response:{assistantMessageId}"
                                            },
                                            RegisteredAt = now,
                                            StartedAt = now,
                                            UpdatedAt = now,
                                            Version = 0
                                        }, observer: new ProviderResponseObservation(continuationToken),
                                            cancellationToken: effectiveCancellationToken).ConfigureAwait(false);
                                    }
                                    else if (providerResponseOperation?.Observer is ProviderResponseObservation observation)
                                        observation.ContinuationToken = continuationToken;
                                }
#pragma warning restore MEAI001

                                // Process contents and emit internal events
                                if (update.Contents != null)
                                {
                                    foreach (var content in update.Contents)
                                    {
                                        if (content is TextReasoningContent reasoning && !string.IsNullOrEmpty(reasoning.Text))
                                        {
	                                            if (!reasoningMessageStarted)
	                                            {
	                                                yield return new ReasoningMessageStartEvent(
	                                                    MessageId: assistantMessageId,
	                                                    Role: "assistant")
                                                { TraceId = traceId };
                                                reasoningMessageStarted = true;
                                            }

	                                            yield return new ReasoningDeltaEvent(
	                                                Text: reasoning.Text,
	                                                MessageId: assistantMessageId,
	                                                ProtectedData: reasoning.ProtectedData)
	                                            { TraceId = traceId };
                                            assistantContents.Add(reasoning);
                                        }
                                        else if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                                        {
	                                            if (reasoningMessageStarted)
	                                            {
	                                                yield return new ReasoningMessageEndEvent(
	                                                    MessageId: assistantMessageId)
                                                { TraceId = traceId };
                                                reasoningMessageStarted = false;
                                            }

                                            if (!messageStarted)
                                            {
                                                yield return new TextMessageStartEvent(assistantMessageId, "assistant", AgentMessageSource.AssistantOutput, AgentMessageVisibility.Transcript) { TraceId = traceId };
                                                messageStarted = true;
                                            }

                                            assistantContents.Add(textContent);
                                            yield return new TextDeltaEvent(textContent.Text, assistantMessageId) { TraceId = traceId };
                                        }
                                        else if (content is FunctionCallContent functionCall)
                                        {
                                            if (!messageStarted)
                                            {
                                                yield return new TextMessageStartEvent(assistantMessageId, "assistant", AgentMessageSource.AssistantOutput, AgentMessageVisibility.Transcript) { TraceId = traceId };
                                                messageStarted = true;
                                            }

                                            yield return new ToolCallStartEvent(
                                                functionCall.CallId,
                                                functionCall.Name ?? string.Empty,
                                                assistantMessageId,
                                                LookupToolHarness(functionCall.Name),
                                                LookupCallType(functionCall.Name))
                                            {
                                                TraceId      = traceId,
                                                SpanId       = GenerateSpanId(),
                                                ParentSpanId = iterSpanId
                                            };

                                            if (functionCall.Arguments != null && functionCall.Arguments.Count > 0)
                                            {
                                                var argsJson = FunctionCallArgumentSerializer.Serialize(functionCall);

                                                yield return new ToolCallArgsEvent(functionCall.CallId, argsJson) { TraceId = traceId };
                                            }

                                            toolRequests.Add(FunctionExecutionCore.NormalizeProviderFunctionCall(functionCall));
                                            assistantContents.Add(functionCall);
                                        }
                                    }
                                }
                                // Check for stream completion
                                if (update.FinishReason != null)
                                {
	                                    if (reasoningMessageStarted)
	                                    {
	                                        yield return new ReasoningMessageEndEvent(
	                                            MessageId: assistantMessageId)
                                        { TraceId = traceId };
                                        reasoningMessageStarted = false;
                                    }
                                }
                            }
                        }

                        // Capture ConversationId from the agent turn response and update session
                        if (_agentTurn.LastResponseConversationId != null)
                        {
                            if (session != null)
                            {
                                //  Store provider conversation ID in metadata (ConversationId removed from Session)
                                session.AddMetadata("ProviderConversationId", _agentTurn.LastResponseConversationId);
                            }
                        }
                        else if (state.InnerClientTracksHistory)
                        {
                            // Service stopped returning ConversationId - disable tracking
                            state = state.DisableHistoryTracking();
                        }
                    } // End of else block (LLM call not skipped)

                    if (providerResponseOperation is not null && lastContinuationToken == null)
                    {
                        await _operationRegistry.TransitionAsync(
                            providerResponseOperation.Snapshot.OperationId,
                            new AgentOperationTransition
                            {
                                ProviderStatus = AgentOperationProviderStatus.Completed,
                                Completion = new AgentOperationCompletion("Model response completed."),
                                ProviderDeduplicationKey = $"model.response.completed:{assistantMessageId}"
                            }, effectiveCancellationToken).ConfigureAwait(false);
                    }

                    // Close the message if we started one (applies to both middleware and normal flow)
                    if (messageStarted)
                    {
                        yield return new TextMessageEndEvent(assistantMessageId) { TraceId = traceId };
                    }

                    // V2: Sync state after LLM call (middleware may have updated it)
                    state = agentContext.State;

                    // Materialize and account for this model call exactly once before any
                    // tool/non-tool branching or response-update clearing. ToChatResponse()
                    // aggregates every MEAI UsageContent reported by this call.
                    var iterationResponse = responseUpdates.Count > 0
                        ? ConstructChatResponseFromUpdates(responseUpdates)
                        : null;
                    var iterationUsage = iterationResponse?.Usage;
                    state = state.WithAccumulatedUsage(iterationUsage);
                    agentContext.SyncState(state);

                    var clientFamily = currentModelRequest?.Transport is Middleware.AgentModelTransport.Realtime
                        ? Providers.ProviderClientFamily.Realtime
                        : Providers.ProviderClientFamily.Chat;
                    var resolvedClientConfig = Config?.ResolveClientConfig(
                        clientFamily,
                        effectiveRunConfig.Clients);

                    foreach (var terminalAttempt in accountingBridge?.DrainTerminals() ?? [])
                    {
                        yield return terminalAttempt;
                    }

                    // Check for early termination from BeforeIteration middleware (e.g., ContinuationPermissionMiddleware)
                    if (state.IsTerminated)
                    {
                        break;
                    }

                    // If there are tool requests, execute them immediately
                    if (toolRequests.Count > 0)
                    {
                        // Coalesce text content before creating the message
                        var coalescedContents = CoalesceTextContents(assistantContents);

                        // Create assistant message with tool calls
                        var assistantMessage = new ChatMessage(ChatRole.Assistant, coalescedContents)
                        {
                            MessageId = assistantMessageId
                        };

                        // Add to shared message list - visible to all contexts immediately
                        sharedMessages.Add(assistantMessage);

                        // Only enable history tracking for real server-managed conversations (conv_... prefix).
                        // Responses API returns the response ID (resp_...) as ConversationId for one-shot calls —
                        // those do NOT support the stateful delta pattern; always send full history for them.
                        var lastConvId = _agentTurn.LastResponseConversationId;
                        if (lastConvId != null && lastConvId.StartsWith("conv_", StringComparison.OrdinalIgnoreCase))
                        {
                            state = state.EnableHistoryTracking(messageCountToSend, lastConvId);
                        }

                        // Commit the full observed assistant message. Model-history filtering happens
                        // only when projecting thread/shared messages into the model turn request.
                        var historyContents = coalescedContents.ToList();

                        // Add to history if there's ANY content (text OR tool calls)
                        if (historyContents.Count > 0)
                        {
                            var historyMessage = new ChatMessage(ChatRole.Assistant, historyContents)
                            {
                                MessageId = assistantMessageId
                            };
                            turnHistory.Add(historyMessage);
                            await CommitThreadMessagesAsync(
                                session,
                                thread,
                                [historyMessage],
                                clientInputId: null,
                                eventCoordinator,
                                effectiveCancellationToken).ConfigureAwait(false);
                        }

                        var effectiveOptionsForTools = beforeIterationContext.Options;

                        // UPDATE AGENT CONTEXT STATE before tool execution hook
                        agentContext.SyncState(state);

                        // EXECUTE BEFORE TOOL EXECUTION MIDDLEWARES (V2)
                        // Allows middlewares (e.g., circuit breaker) to inspect pending
                        // tool calls and prevent execution if needed.
                        var assistantResponse = new ChatMessage(ChatRole.Assistant, assistantContents);
                        var beforeToolContext = agentContext.AsBeforeToolExecution(
                            response: assistantResponse,
                            toolCalls: toolRequests.AsReadOnly(),
                            runConfig: effectiveRunConfig);

                        await turnPipeline.ExecuteBeforeToolExecutionAsync(
                            beforeToolContext,
                            effectiveCancellationToken).ConfigureAwait(false);

                        // V2: Sync state after middleware
                        state = agentContext.State;

                        // Check if middleware signaled to skip tool execution (e.g., circuit breaker)
                        if (beforeToolContext.SkipToolExecution)
                        {
                            // Check for termination
                            if (state.IsTerminated)
                            {
                                break; // Exit the main loop WITHOUT executing tools
                            }

                            // If not terminated, continue to next iteration without executing tools
                            responseUpdates.Clear();
                            continue;
                        }

                        var executionResult = await functionCallProcessor.ExecuteToolsAsync(
                            sharedMessages,
                            toolRequests,
                            effectiveOptionsForTools,
                            state,
                            effectiveRunConfig,
                            agentContext,
                            effectiveCancellationToken).ConfigureAwait(false);

                        // Extract structured results from ToolExecutionResult
                        var toolResultMessage = executionResult.Message;
                        var successfulFunctions = executionResult.SuccessfulFunctions;

                        // ═══════════════════════════════════════════════════════════════
                        // OUTPUT TOOL TERMINATION (structured output tool mode)
                        // When an output tool is called, terminate immediately.
                        // RunStructuredStreamAsync captures the args and handles parsing.
                        // ═══════════════════════════════════════════════════════════════
                        if (executionResult.OutputToolCalled)
                        {
                            // Emit ToolCallEndEvent for output tools so RunStructuredStreamAsync knows args are complete
                            foreach (var toolRequest in toolRequests)
                            {
                                if (functionCallProcessor.IsOutputToolByName(toolRequest.Name, effectiveOptionsForTools?.Tools))
                                {
                                    yield return new ToolCallEndEvent(
                                        toolRequest.CallId,
                                        assistantMessageId,
                                        toolRequest.Name,
                                        FunctionCallArgumentSerializer.Serialize(toolRequest))
                                    { TraceId = traceId };
                                }
                            }
                            state = state.Terminate("Output tool called - structured output complete");
                            break;
                        }

                        if (currentModelRequest?.Transport is Middleware.AgentModelTransport.Realtime &&
                            currentModelTurnExecutor is Middleware.IAgentInteractiveModelTurnExecutor interactiveModelTurnExecutor)
                        {
                            await interactiveModelTurnExecutor.SubmitToolResultsAsync(
                                    toolResultMessage.Contents
                                        .OfType<FunctionResultContent>()
                                        .ToList()
                                        .AsReadOnly(),
                                    currentModelRequest,
                                    effectiveCancellationToken)
                                .ConfigureAwait(false);
                        }

                        // SYNC STATE: Get any updates from middleware (e.g., error tracking)
                        // During tool execution, OnErrorAsync may have updated error counts
                        state = agentContext.State;

                        // EXECUTE AFTER ITERATION MIDDLEWARES (V2 - post-tool execution)
                        var afterIterationContext = agentContext.AsAfterIteration(
                            iteration: state.Iteration,
                            toolResults: toolResultMessage.Contents
                                .OfType<FunctionResultContent>()
                                .ToList()
                                .AsReadOnly(),
                            runConfig: effectiveRunConfig);

                        await turnPipeline.ExecuteAfterIterationAsync(
                            afterIterationContext,
                            effectiveCancellationToken).ConfigureAwait(false);

                        // V2: Sync state after middleware (middleware may have updated state)
                        state = agentContext.State;

                        // Check if middleware signaled termination
                        if (state.IsTerminated)
                        {
                            break;
                        }

                        // UPDATE STATE WITH COMPLETED FUNCTIONS
                        foreach (var functionName in successfulFunctions)
                        {
                            state = state.CompleteFunction(functionName);
                        }

                        // ALWAYS add unfiltered results to sharedMessages (LLM needs to see container expansions)
                        sharedMessages.Add(toolResultMessage);

                        // Add all results to turnHistory (middleware will filter ephemeral results in AfterMessageTurnAsync)
                        turnHistory.Add(toolResultMessage);
                        await CommitThreadMessagesAsync(
                            session,
                            thread,
                            [toolResultMessage],
                            clientInputId: null,
                            eventCoordinator,
                            effectiveCancellationToken).ConfigureAwait(false);

                        // Build callId → toolharnessName / callType mappings for result events
                        var callIdToToolHarness = toolRequests.ToDictionary(
                            tr => tr.CallId,
                            tr => LookupToolHarness(tr.Name));
                        var callIdToCallType = toolRequests.ToDictionary(
                            tr => tr.CallId,
                            tr => LookupCallType(tr.Name));
                        var callIdToToolRequest = toolRequests.ToDictionary(
                            tr => tr.CallId,
                            tr => tr);

                        // EMIT TOOL RESULT EVENTS
                        foreach (var content in toolResultMessage.Contents)
                        {
                            if (content is FunctionResultContent result)
                            {
                                if (!callIdToToolRequest.TryGetValue(result.CallId, out var toolRequest))
                                {
                                    throw new InvalidOperationException(
                                        $"Tool result '{result.CallId}' does not match any pending tool request.");
                                }

                                yield return new ToolCallEndEvent(
                                    result.CallId,
                                    assistantMessageId,
                                    toolRequest.Name,
                                    FunctionCallArgumentSerializer.Serialize(toolRequest))
                                { TraceId = traceId };
                                callIdToToolHarness.TryGetValue(result.CallId, out var toolharnessName);
                                callIdToCallType.TryGetValue(result.CallId, out var callType);
                                if (!executionResult.ResultPayloads.TryGetValue(result.CallId, out var resultPayload))
                                {
                                    throw new InvalidOperationException(
                                        $"Missing normalized tool result payload for call '{result.CallId}'.");
                                }

                                yield return new ToolCallResultEvent(result.CallId, resultPayload, toolharnessName, callType, toolRequest.Name) { TraceId = traceId };
                            }
                        }
                        // Shared reference: state.CurrentMessages already sees the changes via MessagesRef

                        // Build ChatResponse for decision engine (after execution)
                        lastResponse = new ChatResponse(sharedMessages.Where(m => m.Role == ChatRole.Assistant).ToList());

                        // Clear responseUpdates after building the response
                        responseUpdates.Clear();
                    }
                    else
                    {
                        // No tools called - we're done
                        // SYNC STATE: Get any updates from middleware
                        state = agentContext.State;

                        // Call AfterIterationAsync with empty ToolResults for final iteration (V2)
                        var afterIterationContext = agentContext.AsAfterIteration(
                            iteration: state.Iteration,
                            toolResults: Array.Empty<FunctionResultContent>(),
                            runConfig: effectiveRunConfig);

                        await turnPipeline.ExecuteAfterIterationAsync(
                            afterIterationContext,
                            effectiveCancellationToken).ConfigureAwait(false);

                        // V2: Sync state after middleware
                        state = agentContext.State;

                        var finalResponse = iterationResponse
                            ?? ConstructChatResponseFromUpdates(responseUpdates);
                        lastResponse = finalResponse;

                        // Add final assistant message to turnHistory before clearing responseUpdates
                        // This ensures the assistant's response is persisted to the session
                        if (finalResponse.Messages.Count > 0)
                        {
                            var finalAssistantMessage = finalResponse.Messages[0];
                            finalAssistantMessage.MessageId = assistantMessageId;
                            if (finalAssistantMessage.Contents.Count > 0)
                            {
                                var existingTurnIndex = turnHistory.FindIndex(
                                    message => message.MessageId == assistantMessageId);
                                if (existingTurnIndex >= 0)
                                {
                                    // Realtime transports may have already materialized this streamed
                                    // assistant message. Preserve its journal identity and replace the
                                    // in-memory snapshot instead of creating a duplicate identity.
                                    turnHistory[existingTurnIndex] = finalAssistantMessage;
                                    var existingSharedIndex = sharedMessages.FindIndex(
                                        message => message.MessageId == assistantMessageId);
                                    if (existingSharedIndex >= 0)
                                        sharedMessages[existingSharedIndex] = finalAssistantMessage;
                                    else
                                        sharedMessages.Add(finalAssistantMessage);
                                }
                                else
                                {
                                    sharedMessages.Add(finalAssistantMessage);
                                    turnHistory.Add(finalAssistantMessage);
                                    await CommitThreadMessagesAsync(
                                        session,
                                        thread,
                                        [finalAssistantMessage],
                                        clientInputId: null,
                                        eventCoordinator,
                                        effectiveCancellationToken).ConfigureAwait(false);
                                }
                            }
                        }

                        // Clear responseUpdates after constructing final response
                        responseUpdates.Clear();

                        // Update history tracking if we have a real server-managed conversation ID (conv_...).
                        // Responses API returns resp_... as ConversationId — that is a one-shot response ID,
                        // not a server-side conversation; do not enable delta mode for those.
                        var lastRespConvId = _agentTurn.LastResponseConversationId;
                        if (lastRespConvId != null && lastRespConvId.StartsWith("conv_", StringComparison.OrdinalIgnoreCase))
                        {
                            // Track the history boundary associated with the provider request.
                            state = state.EnableHistoryTracking(messageCountToSend, lastRespConvId);
                        }

                        if (TryFinishActiveInput(activeInput))
                            state = state.Terminate("Completed successfully");
                    }
                }
                else if (decision is AgentDecision.Complete complete)
                {
                    // Completion - extract final message if needed
                    lastResponse = complete.FinalResponse;
                    if (TryFinishActiveInput(activeInput))
                        state = state.Terminate("Completed successfully");
                }
                else if (decision is AgentDecision.Terminate terminateDecision)
                {
                    state = state.Terminate(terminateDecision.Reason);
                }
                else
                {
                    throw new InvalidOperationException($"Unknown decision type: {decision.GetType().Name}");
                }

                // Check if middleware signaled termination (e.g., circuit breaker, error threshold)
                // This is a safety check in case the break statements inside nested blocks didn't exit properly
                if (state.IsTerminated)
                {
                    break;
                }

                // Advance to next iteration
                state = state.NextIteration();

            }

            if (responseUpdates.Any())
            {
                var finalResponse = ConstructChatResponseFromUpdates(responseUpdates);

                if (finalResponse.Messages.Count > 0)
                {
                    var finalAssistantMessage = finalResponse.Messages[0];
                    if (currentAssistantMessageId is not null)
                        finalAssistantMessage.MessageId = currentAssistantMessageId;

                    if (finalAssistantMessage.Contents.Count > 0)
                    {
                        // Add final message to shared list and turnHistory for consistency
                        sharedMessages.Add(finalAssistantMessage);

                        // Also add to turnHistory for session persistence
                        turnHistory.Add(finalAssistantMessage);
                        await CommitThreadMessagesAsync(
                            session,
                            thread,
                            [finalAssistantMessage],
                            clientInputId: null,
                            eventCoordinator,
                            effectiveCancellationToken).ConfigureAwait(false);
                    }
                }
            }

            // Provider work and durable turn-history rewrites must settle before accounting closes.
            agentContext.SyncState(state);
            CollapseDuplicateMessageSnapshots(turnHistory);
            var messageIdsBeforeAccountingClose = turnHistory
                .Select(message => message.MessageId)
                .ToList();
            var messageSnapshotsBeforeAccountingClose = turnHistory
                .Where(message => !string.IsNullOrWhiteSpace(message.MessageId))
                .ToDictionary(
                    message => message.MessageId!,
                    SerializeMessageSnapshot,
                    StringComparer.Ordinal);
            Middleware.AfterMessageTurnContext? completedTurnContext = null;
            if (lastResponse is not null)
            {
                completedTurnContext = agentContext.AsAfterMessageTurn(
                    finalResponse: lastResponse,
                    turnHistory: turnHistory,
                    runConfig: effectiveRunConfig,
                    triggerSource: beforeTurnContext.TriggerSource,
                    userInputMessages: beforeTurnContext.UserInputMessages.ToArray(),
                    runtimeContextMessages: beforeTurnContext.RuntimeContextMessages.ToArray());
                // Async-iterator yields restore the caller's ExecutionContext between MoveNext calls.
                // Re-establish the turn collector for the complete finalizer unwind so provider work
                // dispatched by AfterMessageTurnAsync is owned by this closing message turn.
                using var finalizationAccountingScope = accountingBridge?.Collector is { } finalizationCollector
                    ? ProviderOperationAccountingScope.Push(finalizationCollector)
                    : null;
                await turnPipeline.ExecuteAfterMessageTurnAsync(
                    completedTurnContext,
                    effectiveCancellationToken).ConfigureAwait(false);
                foreach (var runtimeMessage in completedTurnContext.RuntimeContextMessages)
                {
                    if (runtimeMessage.GetSource() != AgentMessageSource.BackgroundNotification)
                        continue;
                    var finalRuntimeMessage = completedTurnContext.TurnHistory.FirstOrDefault(message =>
                        ReferenceEquals(message, runtimeMessage) ||
                        (!string.IsNullOrWhiteSpace(runtimeMessage.MessageId) &&
                         string.Equals(message.MessageId, runtimeMessage.MessageId, StringComparison.Ordinal)));
                    if (finalRuntimeMessage is null ||
                        finalRuntimeMessage.GetSource() != AgentMessageSource.BackgroundNotification ||
                        finalRuntimeMessage.Role != ChatRole.System ||
                        finalRuntimeMessage.GetVisibility() != AgentMessageVisibility.Hidden ||
                        finalRuntimeMessage.GetPersistence() != AgentMessagePersistence.ModelContextOnly)
                    {
                        throw new InvalidOperationException(
                            "After-message-turn middleware must preserve background notifications as hidden, system-role, model-context-only runtime context.");
                    }
                }
            }

            state = agentContext.State;
            await ReconcileCommittedTurnHistoryAsync(
                session,
                thread,
                turnHistory,
                messageIdsBeforeAccountingClose,
                messageSnapshotsBeforeAccountingClose,
                eventCoordinator,
                effectiveCancellationToken).ConfigureAwait(false);

            // Emit MESSAGE TURN finished event after all turn-owned provider work has settled.
            turnStopwatch.Stop();
            yield return new MessageTurnFinishedEvent(
                messageTurnId,
                conversationId,
                AgentId,
                _name,
                turnStopwatch.Elapsed,
                MessageTurnUsageSummary.Empty)
            {
                TraceId      = traceId,
                SpanId       = turnSpanId,
                ParentSpanId = null,
                Iteration = state.Iteration,
                TerminationReason = state.TerminationReason,
                TurnMessageCount = turnHistory.Count
            };

            // Record orchestration telemetry metrics
            orchestrationActivity?.SetTag("agent.total_iterations", state.Iteration);
            orchestrationActivity?.SetTag("agent.total_function_calls", state.CompletedFunctions.Count);
            orchestrationActivity?.SetTag("agent.termination_reason", state.TerminationReason ?? "completed");
            orchestrationActivity?.SetTag("agent.was_terminated", state.IsTerminated);


            // Emit agent completion event
            yield return new AgentCompletionEvent(
                _name,
                state.Iteration,
                turnStopwatch.Elapsed,
                DateTimeOffset.UtcNow)
            { TraceId = traceId };

            // PERSISTENCE: Save persistent middleware state ( split by scope)
            if (session != null)
            {
                try
                {
                    // Save session-scoped state (permissions, preferences) to Session
                    state.MiddlewareState.SaveToSession(session, _stateFactories);
                }
                catch (Exception)
                {
                    // Ignore errors - middleware state persistence is not critical to execution
                }
            }
            if (thread != null)
            {
                // Thread-scoped middleware state is a canonical projection fact. It must use
                // the same commit-before-publish path and may not disappear on append failure.
                state.MiddlewareState.SaveToThread(thread, _stateFactories);

                if (Config?.SessionStore != null && thread.MiddlewareState.Count > 0)
                {
                    await CommitAndPublishThreadEventAsync(
                        thread,
                        ThreadEventFactory.ThreadMiddlewareStateCommitted(
                            thread.SessionId,
                            thread.Id,
                            thread.MiddlewareState),
                        eventCoordinator,
                        effectiveCancellationToken).ConfigureAwait(false);
                }
            }

            historyCompletionSource.TrySetResult(turnHistory);
            toolHarnessDeactivationReason = ToolHarnessDeactivationReason.Completed;
        }
        finally
        {
            var cleanupFailures = new List<Exception>();
            if (toolHarnessDeactivationReason == ToolHarnessDeactivationReason.Failed && effectiveCancellationToken.IsCancellationRequested)
                toolHarnessDeactivationReason = ToolHarnessDeactivationReason.Cancelled;
            try { await toolHarnessExecutionScope.ReleaseForegroundAsync(toolHarnessDeactivationReason).ConfigureAwait(false); }
            catch (Exception ex) { cleanupFailures.Add(ex); }
            _activeTurnCancellations.TryRemove(activeTurnId, out _);
            if (chatClientLease is not null)
                try { await chatClientLease.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { cleanupFailures.Add(ex); }
            if (runClientSet is not null)
                try { await runClientSet.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { cleanupFailures.Add(ex); }
            if (turn.CatalogLease is not null)
                try { await turn.CatalogLease.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { cleanupFailures.Add(ex); }
            RootAgent = previousRootAgent;
            if (cleanupFailures.Count > 0)
            {
                var cleanupFailure = new AggregateException("Agent execution cleanup failed.", cleanupFailures);
                if (toolHarnessDeactivationReason == ToolHarnessDeactivationReason.Completed)
                    throw cleanupFailure;
                _agentLogger?.LogError(cleanupFailure, "Agent cleanup also failed while preserving the original execution failure.");
            }
        }
    }

    /// <summary>
    /// Checks if a function result is successful (no exception, no error message).
    /// </summary>
    private static bool IsFunctionResultSuccessful(FunctionResultContent result)
    {
        // Exception present = failure
        if (result.Exception != null)
            return false;

        // Check if result looks like an error message
        var resultStr = result.Result?.ToString();
        return !IsLikelyErrorString(resultStr);
    }

    /// <summary>
    /// Heuristic to detect error strings in function results.
    /// </summary>
    private static bool IsLikelyErrorString(string? s) =>
        !string.IsNullOrEmpty(s) &&
        (s.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ||
         s.StartsWith("Failed:", StringComparison.OrdinalIgnoreCase) ||
         s.Contains("exception occurred", StringComparison.OrdinalIgnoreCase) ||
         s.Contains("unhandled exception", StringComparison.OrdinalIgnoreCase) ||
         s.Contains("exception was thrown", StringComparison.OrdinalIgnoreCase) ||
         s.Contains("rate limit exceeded", StringComparison.OrdinalIgnoreCase) ||
         s.Contains("rate limited", StringComparison.OrdinalIgnoreCase) ||
         s.Contains("quota exceeded", StringComparison.OrdinalIgnoreCase) ||
         s.Contains("quota reached", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Coalesces consecutive <see cref="TextContent"/> items and consecutive
    /// <see cref="TextReasoningContent"/> items within a list of <see cref="AIContent"/>.
    /// Used by the coalesce-mode event emission and tool-call message building paths
    /// (which operate on raw content lists, not <see cref="ChatResponseUpdate"/> streams).
    /// <para>
    /// Reasoning chunks are merged unless a chunk carries <see cref="TextReasoningContent.ProtectedData"/>,
    /// which terminates the current run (its data is transferred to the merged result).
    /// This matches the Microsoft.Extensions.AI <c>CoalesceContent</c> semantics.
    /// </para>
    /// </summary>
    private static List<AIContent> CoalesceTextContents(List<AIContent> contents)
    {
        return ThreadMessageEventConverter.CoalesceTextContents(contents);
    }

    private static List<ChatMessage> ProjectMessagesForModelHistory(
        IEnumerable<ChatMessage> messages,
        bool includeReasoningInModelHistory)
    {
        var source = messages.ToList();
        if (includeReasoningInModelHistory)
        {
            ValidateFunctionCallHistory(source);
            return source;
        }

        var projected = new List<ChatMessage>(source.Count);
        foreach (var message in source)
        {
            if (!message.Contents.Any(c => c is TextReasoningContent))
            {
                projected.Add(message);
                continue;
            }

            var contents = message.Contents
                .Where(c => c is not TextReasoningContent)
                .ToList();

            if (contents.Count == 0)
                continue;

            var coalesced = CoalesceTextContents(contents);
            var clone = new ChatMessage(message.Role, coalesced)
            {
                AuthorName = message.AuthorName,
                MessageId = message.MessageId,
                CreatedAt = message.CreatedAt,
                AdditionalProperties = message.AdditionalProperties,
                RawRepresentation = message.RawRepresentation
            };
            projected.Add(clone);
        }

        ValidateFunctionCallHistory(projected);
        return projected;
    }

    private static void ValidateFunctionCallHistory(IReadOnlyList<ChatMessage> messages)
    {
        var seenCallIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is FunctionCallContent call)
                {
                    seenCallIds.Add(call.CallId);
                    continue;
                }

                if (content is FunctionResultContent result &&
                    !seenCallIds.Contains(result.CallId))
                {
                    throw new InvalidOperationException(
                        $"Model history contains tool result '{result.CallId}' without an earlier matching function call.");
                }
            }
        }
    }

    /// <summary>
    /// Builds a <see cref="ChatResponse"/> from buffered streaming updates using the
    /// built-in <see cref="ChatResponseExtensions.ToChatResponse"/> from Microsoft.Extensions.AI.
    /// That method handles message grouping (by MessageId/Role), content coalescing
    /// (TextContent, TextReasoningContent with ProtectedData preservation, DataContent, etc.),
    /// and UsageContent → <see cref="ChatResponse.Usage"/> extraction.
    /// </summary>
    private static ChatResponse ConstructChatResponseFromUpdates(List<ChatResponseUpdate> updates)
    {
        return updates.ToChatResponse();
    }

    /// <summary>Returns immutable snapshots of all operations currently known to this agent.</summary>
    public IReadOnlyList<AgentOperationSnapshot> ListOperations() => _operationRegistry.Snapshot();

    /// <summary>Requests provider cancellation for one operation without conflating it with observation shutdown.</summary>
    public ValueTask CancelOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default) =>
        _operationRegistry.RequestCancellationAsync(operationId, cancellationToken);

    /// <summary>Supplies protocol-neutral input to an operation currently awaiting provider input.</summary>
    public ValueTask SupplyOperationInputAsync(
        string operationId,
        AgentOperationInput input,
        CancellationToken cancellationToken = default) =>
        _operationRegistry.SupplyInputAsync(operationId, input, cancellationToken);

    /// <summary>
    /// Asynchronously stops accepted work and releases observers, transports, capability
    /// revisions, providers, event infrastructure, and owned clients in dependency order.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            await _disposeCompletion.Task.ConfigureAwait(false);
            return;
        }
        try
        {
            await DisposeCoreAsync().ConfigureAwait(false);
            _disposeCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            _disposeCompletion.TrySetException(exception);
            throw;
        }
    }

    private async ValueTask DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        async ValueTask AttemptAsync(Func<ValueTask> cleanup)
        {
            try { await cleanup().ConfigureAwait(false); }
            catch (Exception ex) { failures.Add(ex); }
        }

        await AttemptAsync(async () => await StopAsync().ConfigureAwait(false)).ConfigureAwait(false);

        _skillWatchCancellation?.Cancel();
        if (_skillWatchTasks.Count > 0)
        {
            try { await Task.WhenAll(_skillWatchTasks).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { failures.Add(ex); }
        }
        _skillWatchCancellation?.Dispose();

        RuntimeStructHandlerSubscription[] structSubscriptions;
        lock (_structHandlerLock)
        {
            structSubscriptions = _structHandlerSubscriptions.ToArray();
            _structHandlerSubscriptions.Clear();
        }

        foreach (var subscription in structSubscriptions)
            try { subscription.Dispose(); } catch (Exception ex) { failures.Add(ex); }

        foreach (var subscription in _eventSubscriptions)
            try { subscription.Dispose(); } catch (Exception ex) { failures.Add(ex); }

        await AttemptAsync(() => DrainActiveTurnsAsync(Config.Shutdown)).ConfigureAwait(false);
        await AttemptAsync(() => _operationRegistry.ShutdownAsync(Config.Shutdown)).ConfigureAwait(false);
        try { await DrainToolHarnessExecutionCompletionsAsync().ConfigureAwait(false); }
        catch (Exception ex) { failures.Add(ex); }
        await AttemptAsync(() => _agentResources.DisposeAsync()).ConfigureAwait(false);

        if (_capabilityCatalog is not null)
        {
            try
            {
                var leakedRevisionOwners = await _capabilityCatalog
                    .ShutdownAsync(Config.Shutdown.LeaseLeaks)
                    .ConfigureAwait(false);
                if (leakedRevisionOwners > 0)
                    _agentLogger?.LogWarning(
                        "Agent shutdown found {LeakedRevisionOwnerCount} capability revision owner(s) still pinned by leaked leases; policy {LeaseLeakPolicy} was applied.",
                        leakedRevisionOwners,
                        Config.Shutdown.LeaseLeaks);
            }
            catch (Exception ex) { failures.Add(ex); }
        }
        foreach (var source in _capabilitySources)
            await AttemptAsync(source.DisposeAsync).ConfigureAwait(false);
        if (_providerRuntimeOwner is not null)
            await AttemptAsync(_providerRuntimeOwner.DisposeAsync).ConfigureAwait(false);
        if (_clientSet != null)
            await AttemptAsync(_clientSet.DisposeAsync).ConfigureAwait(false);
        else
            try { _baseClient?.Dispose(); } catch (Exception ex) { failures.Add(ex); }
        await AttemptAsync(_chatClientResolver.DisposeAsync).ConfigureAwait(false);
        await AttemptAsync(_textToSpeechClientManager.DisposeAsync).ConfigureAwait(false);
        await AttemptAsync(_speechToTextClientManager.DisposeAsync).ConfigureAwait(false);
        await AttemptAsync(_realtimeClientManager.DisposeAsync).ConfigureAwait(false);
        await AttemptAsync(_imageGeneratorManager.DisposeAsync).ConfigureAwait(false);
        await AttemptAsync(_embeddingGeneratorManager.DisposeAsync).ConfigureAwait(false);
        await AttemptAsync(_hostedFileClientManager.DisposeAsync).ConfigureAwait(false);
        await AttemptAsync(_realtimeProviderProtocolParticipant.DisposeAsync).ConfigureAwait(false);
        if (_ownedHttpClients != null)
            foreach (var client in _ownedHttpClients)
                try { client.Dispose(); } catch (Exception ex) { failures.Add(ex); }
        if (_eventCoordinator is IAsyncDisposable asyncEventCoordinator)
            await AttemptAsync(asyncEventCoordinator.DisposeAsync).ConfigureAwait(false);
        else
            try { (_eventCoordinator as IDisposable)?.Dispose(); } catch (Exception ex) { failures.Add(ex); }
        if (_ownsContentStore)
            await AttemptAsync(() => DisposeOwnedStoreAsync(_contentStore)).ConfigureAwait(false);
        if (_ownsSessionStore)
            await AttemptAsync(() => DisposeOwnedStoreAsync(Config.SessionStore)).ConfigureAwait(false);

        if (failures.Count > 0)
            throw new AggregateException("Agent shutdown encountered one or more cleanup failures.", failures);
    }

    private static async ValueTask DisposeOwnedStoreAsync(object? store)
    {
        if (store is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else
            (store as IDisposable)?.Dispose();
    }

    private void ThrowIfShutdownStarted() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

    private async ValueTask DrainActiveTurnsAsync(AgentShutdownOptions options)
    {
        var gracefulDeadline = DateTimeOffset.UtcNow + options.GracefulDrainTimeout;
        while (!_activeTurnCancellations.IsEmpty && DateTimeOffset.UtcNow < gracefulDeadline)
            await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
        if (_activeTurnCancellations.IsEmpty)
            return;

        foreach (var cancellation in _activeTurnCancellations.Values)
            cancellation.Cancel();

        var cancellationDeadline = DateTimeOffset.UtcNow + options.CancellationDrainTimeout;
        while (!_activeTurnCancellations.IsEmpty && DateTimeOffset.UtcNow < cancellationDeadline)
            await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
        if (!_activeTurnCancellations.IsEmpty)
        {
            _agentLogger?.LogWarning(
                "Agent shutdown abandoned {Count} turn(s) that retained resources after both drain deadlines; policy {Policy}.",
                _activeTurnCancellations.Count,
                options.LeaseLeaks);
        }
    }

    private async ValueTask DrainToolHarnessExecutionCompletionsAsync()
    {
        if (_toolHarnessExecutionCompletions.IsEmpty)
            return;

        var completions = Task.WhenAll(_toolHarnessExecutionCompletions.Values);
        await completions.ConfigureAwait(false);
    }

    /// <summary>
    /// Builds lightweight configuration for decision engine from full agent config.
    /// </summary>
    /// <param name="options">Chat options containing tool list</param>
    /// <returns>Configuration with only fields needed for decision-making</returns>
    private AgentConfiguration BuildDecisionConfiguration(ChatOptions? options)
    {
        // Extract available tool names from options
        var availableTools = new HashSet<string>(StringComparer.Ordinal);

        if (options?.Tools != null)
        {
            foreach (var tool in options.Tools)
            {
                if (tool is AIFunction func && !string.IsNullOrEmpty(func.Name))
                    availableTools.Add(func.Name);
            }
        }

        // Build configuration from AgentConfig fields
        return AgentConfiguration.FromAgentConfig(
            Config,
            Config?.MaxAgenticIterations ?? 10,
            availableTools);
    }

    #region Testing and Advanced API

    /// <summary>
    /// Runs the agentic loop and streams  agent events.
    /// The agent is stateless; all conversation state is managed externally or in session parameters.
    /// </summary>
    /// <param name="messages">The conversation messages</param>
    /// <param name="options">Chat options including tools</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stream of agent events</returns>
    internal async IAsyncEnumerable<AgentEvent> RunAgenticLoopAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var effectiveOptions = options ?? DefaultOptions;

        // Prepare turn (stateless - no thread)
        var inputMessages = messages.ToList();
        if (_capabilityCatalog is not null)
            await _capabilityCatalog.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        var catalogLease = _capabilityCatalog?.Acquire();
        PreparedTurn turn;
        try
        {
            turn = await _messageProcessor.PrepareTurnAsync(
                thread: null,
                inputMessages,
                effectiveOptions,
                Name,
                cancellationToken,
                catalogLease?.Snapshot.Functions);
            turn = turn with { CatalogLease = catalogLease };
        }
        catch
        {
            if (catalogLease is not null)
                await catalogLease.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        var turnHistory = new List<ChatMessage>();
        var historyCompletionSource = new TaskCompletionSource<IReadOnlyList<ChatMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accountingBridge = new ProviderOperationAccountingBridge();

        await foreach (var evt in RunAgenticLoopInternal(
            turn,
            turnHistory,
            historyCompletionSource,
            session: null,
            initialContextProperties: null,
            clientInputId: null,
            accountingBridge: accountingBridge,
            cancellationToken: cancellationToken))
        {
            var outputEvent = EnrichOutputEvent(evt);
            await _eventCoordinator.EmitAsync(outputEvent, AgentEventRoutes.Create(outputEvent), cancellationToken).ConfigureAwait(false);
            yield return outputEvent;
        }
    }

    #endregion



    /// <summary>
    /// Creates a new conversation session and thread.
    /// </summary>
    /// <param name="sessionId">Optional session ID. If null, a GUID is generated.</param>
    /// <param name="threadId">Optional thread ID. If null, a GUID is generated.</param>
    /// <returns>A tuple of (Session, Thread) for the new conversation</returns>
    internal (Session Session, Thread Thread) CreateSession(string? sessionId = null, string? threadId = null)
    {
        var session = sessionId is null ? new Session() : new Session(sessionId);
        var thread = session.CreateThread(AgentId, threadId);
        return (session, thread);
    }


    //──────────────────────────────────────────────────────────────────
    // INTERNAL TURN STREAM ENGINE
    //──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the agent with a string message.
    /// Convenience overload that wraps the message as a user ChatMessage.
    /// </summary>
    /// <param name="userMessage">The user's message text</param>
    /// <param name="session">Optional session containing conversation history</param>
    /// <param name="options">Optional per-invocation run options for customization</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stream of agent events</returns>
    /// <remarks>
    /// <para>
    /// <b>Provider Switching Priority:</b>
    /// 1. options.Clients.Chat.Override (highest - direct client override)
    /// 2. options.ProviderKey + options.ModelId (via registry)
    /// 3. Agent's default client (lowest)
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// // Simple stateless call
    /// await agent.RunTurnStreamAsync("Hello");
    ///
    /// // With session + thread
    /// var (session, thread) = agent.CreateSession();
    /// await agent.RunTurnStreamAsync("Hello", session, thread);
    ///
    /// // With options
    /// var options = new AgentRunConfig
    /// {
    ///     ProviderKey = "anthropic",
    ///     ModelId = "claude-opus",
    ///     Chat = new ChatClientConfig { Temperature = 0.7 }
    /// };
    /// await agent.RunTurnStreamAsync("Hello", session, options);
    /// </code>
    /// </para>
    /// </remarks>
    internal IAsyncEnumerable<AgentEvent> RunTurnStreamAsync(
        string userMessage,
        Session? session = null,
        Thread? thread = null,
        AgentRunConfig? options = null,
        CancellationToken cancellationToken = default)
    {
        return RunTurnStreamAsync(
            [new ChatMessage(ChatRole.User, userMessage)],
            session,
            thread,
            options,
            cancellationToken);
    }

    /// <summary>
    /// Runs the agent with a Thread (session is accessed via thread.Session).
    /// </summary>
    /// <param name="userMessage">The user's message text</param>
    /// <param name="thread">Thread to run on (must have Session set via Session.CreateThread() or LoadSessionAndThreadAsync)</param>
    /// <param name="options">Optional per-invocation run options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stream of agent events</returns>
    /// <exception cref="InvalidOperationException">Thrown if thread.Session is null</exception>
    internal IAsyncEnumerable<AgentEvent> RunTurnStreamAsync(
        string userMessage,
        Thread thread,
        AgentRunConfig? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(thread);
        if (thread.Session is null)
            throw new InvalidOperationException(
                "Thread.Session is null. Threads must be created via Session.CreateThread() or loaded via LoadSessionAndThreadAsync().");

        return RunTurnStreamAsync(userMessage, thread.Session, thread, options, cancellationToken);
    }

    /// <summary>
    /// Runs the agent with messages and a Thread (session is accessed via thread.Session).
    /// </summary>
    /// <param name="messages">Messages to process</param>
    /// <param name="thread">Thread to run on (must have Session set)</param>
    /// <param name="options">Optional per-invocation run options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stream of agent events</returns>
    /// <exception cref="InvalidOperationException">Thrown if thread.Session is null</exception>
    internal IAsyncEnumerable<AgentEvent> RunTurnStreamAsync(
        IEnumerable<ChatMessage> messages,
        Thread thread,
        AgentRunConfig? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(thread);
        if (thread.Session is null)
            throw new InvalidOperationException(
                "Thread.Session is null. Threads must be created via Session.CreateThread() or loaded via LoadSessionAndThreadAsync().");

        return RunTurnStreamAsync(messages, thread.Session, thread, options, cancellationToken);
    }

    /// <summary>
    /// Runs the agent with messages. This is the core RunAsync implementation.
    /// All other RunAsync overloads delegate to this method.
    /// </summary>
    /// <param name="messages">Messages to process</param>
    /// <param name="session">Optional session containing conversation history. If null, runs stateless.</param>
    /// <param name="options">Optional per-invocation run options for customization</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stream of agent events</returns>
    /// <remarks>
    /// <para>
    /// <b>Session Behavior:</b>
    /// - If session is null: Runs stateless (no history persistence)
    /// - If session is provided: History is maintained across calls
    /// </para>
    /// <para>
    /// <b>Options:</b>
    /// Use <see cref="AgentRunConfig"/> for per-invocation customization:
    /// - Provider switching via Clients.Chat provider/model configuration or a client override
    /// - System instruction overrides
    /// - Chat parameters (temperature, tokens, etc.) via Chat property
    /// - Client tool configuration via ClientToolInput
    /// - Context overrides and runtime middleware
    /// </para>
    /// </remarks>
    internal async IAsyncEnumerable<AgentEvent> RunTurnStreamAsync(
        IEnumerable<ChatMessage> messages,
        Session? session = null,
        Thread? thread = null,
        AgentRunConfig? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var evt in RunTurnStreamAsync(
            messages,
            session,
            thread,
            options,
            _eventCoordinator,
            clientInputId: null,
            activeInput: null,
            cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            yield return evt;
        }
    }

    private async IAsyncEnumerable<AgentEvent> RunTurnStreamAsync(
        IEnumerable<ChatMessage> messages,
        Session? session,
        Thread? thread,
        AgentRunConfig? options,
        HPD.Events.IEventCoordinator eventCoordinator,
        string? clientInputId,
        ActiveRuntimeInput? activeInput,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        AgentChatClientHandle? inheritedChatClient = null,
        ClientFamilyInheritanceMode inheritedChatMode = ClientFamilyInheritanceMode.UseOwn)
    {
        ThrowIfShutdownStarted();
        // Validation
        if (thread != null)
        {
            var hasMessages = messages?.Any() ?? false;
            var hasHistory = thread.Messages.Count > 0;

            if (!hasMessages && !hasHistory)
            {
                throw new InvalidOperationException(
                    "Cannot run agent with empty thread and no messages.");
            }
        }

        // Resolve chat options from AgentRunConfig and apply system instruction overrides.
        var baseDefaultOptions =
            (Config?.ResolveClientConfig(Providers.ProviderClientFamily.Chat) as ChatClientConfig)?.ToMicrosoftChatOptions();
        var chatOptions = options?.Clients.Chat?.MergeWith(baseDefaultOptions) ?? baseDefaultOptions;
        chatOptions = ApplySystemInstructionOverrides(chatOptions, options);

        // Prepare turn
        var inputMessages = messages?.ToList() ?? new List<ChatMessage>();
        if (_capabilityCatalog is not null)
            await _capabilityCatalog.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        var catalogLease = _capabilityCatalog?.Acquire();
        PreparedTurn turn;
        try
        {
            turn = await _messageProcessor.PrepareTurnAsync(
                thread,
                inputMessages,
                chatOptions,
                Name,
                cancellationToken,
                catalogLease?.Snapshot.Functions);
            turn = turn with { CatalogLease = catalogLease };
        }
        catch
        {
            if (catalogLease is not null)
                await catalogLease.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        var turnHistory = new List<ChatMessage>();
        var historyCompletionSource = new TaskCompletionSource<IReadOnlyList<ChatMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Build initial context properties from AgentRunConfig
        var initialProperties = BuildInitialContextProperties(options);

        // Execute agentic loop
        var accountingBridge = new ProviderOperationAccountingBridge();
        var internalStream = RunAgenticLoopInternal(
            turn,
            turnHistory,
            historyCompletionSource,
            session: session,
            thread: thread,
            initialContextProperties: initialProperties,
            runConfig: options,
            eventCoordinator: eventCoordinator,
            clientInputId: clientInputId,
            activeInput: activeInput,
            accountingBridge: accountingBridge,
            cancellationToken: cancellationToken,
            inheritedChatClient: inheritedChatClient,
            inheritedChatMode: inheritedChatMode);

        await using var enumerator = internalStream.GetAsyncEnumerator(cancellationToken);
        string? messageTurnId = null;
        string? conversationId = session?.Id;
        var currentIteration = 0;
        var isResume = inputMessages.Count == 0 && thread?.Messages.Count > 0;
        var turnFinished = false;
        var stagedTextMessages = new HashSet<string>(StringComparer.Ordinal);
        var stagedReasoningMessages = new HashSet<string>(StringComparer.Ordinal);
        MessageTurnUsageCollector? usageCollector = null;
        using var usageSubscription = eventCoordinator.SubscribeAny(committedEvent =>
        {
            if (committedEvent is AgentEvent agentEvent &&
                TryCreateProviderUsageMeasurement(agentEvent, out var measurement) &&
                usageCollector is not null &&
                string.Equals(measurement.MessageTurnId, messageTurnId, StringComparison.Ordinal))
            {
                usageCollector.TryAcceptCommitted(measurement);
            }

            return ValueTask.CompletedTask;
        });

        try
        {
            while (true)
            {
                AgentEvent evt;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                        break;

                    evt = enumerator.Current;
                }
                catch (Exception ex)
                {
                    await FinalizeOutstandingAgentDeltasAsync(
                        thread, stagedTextMessages, stagedReasoningMessages,
                        messageTurnId, conversationId, currentIteration,
                        inputMessages.Count, isResume, turnHistory.Count,
                        eventCoordinator).ConfigureAwait(false);
                    if (!turnFinished)
                    {
                        foreach (var terminalAttempt in accountingBridge.DrainTerminals())
                        {
                            if (usageCollector is not null)
                                await usageCollector.CommitTerminalAsync(terminalAttempt, CancellationToken.None).ConfigureAwait(false);
                            else
                                await CommitAgentThreadEventAsync(
                                    thread, terminalAttempt, messageTurnId, conversationId, currentIteration,
                                    inputMessages.Count, isResume, null, turnHistory.Count,
                                    eventCoordinator, CancellationToken.None).ConfigureAwait(false);
                        }
                        if (usageCollector is not null)
                        {
                            await CommitPendingFailedProviderAttemptsAsync(
                                usageCollector, thread, messageTurnId, conversationId, currentIteration,
                                inputMessages.Count, isResume, turnHistory.Count, ex, eventCoordinator).ConfigureAwait(false);
                        }
                        await AppendThreadFailureRuntimeEventAsync(
                            thread,
                            messageTurnId,
                            conversationId,
                            ex,
                            usageCollector is null
                                ? MessageTurnUsageSummary.Empty
                                : await usageCollector.CloseAsync(CancellationToken.None).ConfigureAwait(false),
                            eventCoordinator).ConfigureAwait(false);
                    }

                    throw;
                }

            if (evt is MessageTurnStartedEvent started)
            {
                messageTurnId = started.MessageTurnId;
                conversationId = started.ConversationId;
                usageCollector = new MessageTurnUsageCollector(started.MessageTurnId);
                accountingBridge.Collector = usageCollector;
                usageCollector.ConfigureCommitter(async (terminalEvent, commitCancellationToken) =>
                {
                    var committed = await CommitAgentThreadEventAsync(
                        thread, terminalEvent, messageTurnId, conversationId, currentIteration,
                        inputMessages.Count, isResume, null, turnHistory.Count,
                        eventCoordinator, commitCancellationToken).ConfigureAwait(false);
                    if (TryCreateProviderUsageMeasurement(committed, out var measurement))
                        usageCollector.TryAcceptCommitted(measurement);
                    return committed;
                });
            }
            else if (evt is AgentTurnStartedEvent agentTurnStarted)
            {
                currentIteration = agentTurnStarted.Iteration;
            }
            else if (evt is AgentTurnFinishedEvent agentTurnFinished)
            {
                currentIteration = agentTurnFinished.Iteration;
            }
            else if (evt is MessageTurnFinishedEvent finished)
            {
                evt = finished with
                {
                    Usage = usageCollector is null
                        ? MessageTurnUsageSummary.Empty
                        : await usageCollector.CloseAsync(cancellationToken).ConfigureAwait(false)
                };
                turnFinished = true;
            }

            if (evt is TextMessageStartEvent
                {
                    Source: AgentMessageSource.AssistantOutput,
                    Persistence: AgentMessagePersistence.ThreadHistory
                } textStart)
            {
                stagedTextMessages.Add(textStart.MessageId);
            }
            else if (evt is ReasoningMessageStartEvent reasoningStart &&
                     StringComparer.OrdinalIgnoreCase.Equals(reasoningStart.Role, "assistant"))
            {
                stagedReasoningMessages.Add(reasoningStart.MessageId);
            }

            var useStagedDeltaLifecycle = thread is not null && Config?.SessionStore is IThreadDeltaStore;
            AgentEvent outputEvent;
            if (useStagedDeltaLifecycle &&
                (evt is TextDeltaEvent textDelta && stagedTextMessages.Contains(textDelta.MessageId) ||
                 evt is ReasoningDeltaEvent reasoningDelta && stagedReasoningMessages.Contains(reasoningDelta.MessageId)))
            {
                outputEvent = await StageAgentThreadDeltaAsync(
                    thread!, evt, messageTurnId, conversationId, currentIteration,
                    inputMessages.Count, isResume, turnHistory.Count, eventCoordinator,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (useStagedDeltaLifecycle &&
                     (evt is TextMessageEndEvent textEnd && stagedTextMessages.Contains(textEnd.MessageId) ||
                      evt is ReasoningMessageEndEvent reasoningEnd && stagedReasoningMessages.Contains(reasoningEnd.MessageId)))
            {
                outputEvent = await FinalizeAgentThreadDeltasAsync(
                    thread!, evt, messageTurnId, conversationId, currentIteration,
                    inputMessages.Count, isResume, turnHistory.Count, eventCoordinator,
                    cancellationToken).ConfigureAwait(false);
                if (evt is TextMessageEndEvent completedText)
                    stagedTextMessages.Remove(completedText.MessageId);
                else if (evt is ReasoningMessageEndEvent completedReasoning)
                    stagedReasoningMessages.Remove(completedReasoning.MessageId);
            }
            else
            {
                outputEvent = await CommitAgentThreadEventAsync(
                    thread, evt, messageTurnId, conversationId, currentIteration,
                    inputMessages.Count, isResume, null, turnHistory.Count,
                    eventCoordinator, cancellationToken).ConfigureAwait(false);
            }

            // The iterator owns model-call commits, so accept their committed identity
            // synchronously. The coordinator subscription remains necessary for
            // message-turn-owned operations published directly by middleware (audio,
            // hosted operations, and future provider families).
            if (TryCreateProviderUsageMeasurement(outputEvent, out var committedMeasurement) &&
                string.Equals(committedMeasurement.MessageTurnId, messageTurnId, StringComparison.Ordinal))
            {
                usageCollector?.TryAcceptCommitted(committedMeasurement);
            }

            // Custom streaming callback if provided
            if (options?.Streaming?.Callback != null)
            {
                try
                {
                    await options.Streaming.Callback(outputEvent).ConfigureAwait(false);
                }
                catch (Exception ex) when (!turnFinished && !cancellationToken.IsCancellationRequested)
                {
                    await FinalizeOutstandingAgentDeltasAsync(
                        thread, stagedTextMessages, stagedReasoningMessages,
                        messageTurnId, conversationId, currentIteration,
                        inputMessages.Count, isResume, turnHistory.Count,
                        eventCoordinator).ConfigureAwait(false);
                    await AppendThreadFailureRuntimeEventAsync(
                        thread,
                        messageTurnId,
                        conversationId,
                        ex,
                        usageCollector is null
                            ? MessageTurnUsageSummary.Empty
                            : await usageCollector.CloseAsync(CancellationToken.None).ConfigureAwait(false),
                        eventCoordinator).ConfigureAwait(false);
                    throw;
                }
            }

                yield return outputEvent;
            }
        }
        finally
        {
            await FinalizeOutstandingAgentDeltasAsync(
                thread, stagedTextMessages, stagedReasoningMessages,
                messageTurnId, conversationId, currentIteration,
                inputMessages.Count, isResume, turnHistory.Count,
                eventCoordinator).ConfigureAwait(false);
        }
    }

    #region Structured Output

    /// <summary>
    /// Runs the agent with structured output, yielding typed results.
    /// This is the primary implementation - all other overloads delegate to this.
    /// Preserves all request events (permissions, continuations, custom events).
    /// </summary>
    /// <typeparam name="T">The output type. Must be a reference type for JSON deserialization.</typeparam>
    /// <param name="messages">Messages to process</param>
    /// <param name="session">Optional session for conversation history</param>
    /// <param name="options">Per-invocation run options (includes StructuredOutput config)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stream of agent events including StructuredResultEvent&lt;T&gt;</returns>
    /// <remarks>
    /// <para><b>Generic Constraint:</b> The <c>where T : class</c> constraint is required because:</para>
    /// <list type="bullet">
    /// <item>JSON deserialization returns null for invalid input - structs can't be null</item>
    /// <item>Partial results may have uninitialized fields - reference types handle this gracefully</item>
    /// <item>Consistent with M.E.AI's structured output patterns</item>
    /// </list>
    /// <para>If you need to return a primitive or struct, wrap it in a class:</para>
    /// <code>public record CountResult(int Count);</code>
    /// </remarks>
    internal async IAsyncEnumerable<AgentEvent> RunStructuredStreamAsync<T>(
        IEnumerable<ChatMessage> messages,
        Session? session = null,
        Thread? thread = null,
        AgentRunConfig? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
    {
        // Ensure StructuredOutput options exist
        options ??= new AgentRunConfig();
        options.StructuredOutput ??= new StructuredOutputOptions();

        var structuredOpts = options.StructuredOutput;
        var schemaName = structuredOpts.SchemaName ?? typeof(T).Name;

        // Resolve serializer options (AOT-safe pattern)
        var serializerOptions = ResolveSerializerOptions(structuredOpts);

        // Configure ChatOptions based on mode
        ConfigureStructuredOutputOptions<T>(options, serializerOptions);

        // State for streaming
        var textAccumulator = new StringBuilder();
        var debounceStopwatch = Stopwatch.StartNew();
        string? lastPartialJson = null;  // Compare JSON strings, not object references
        string? outputToolCallId = null;  // Track output tool call ID for tool mode
        Type? matchedUnionType = null;   // Track which union type was matched (union mode only)

        // Determine the mode we're operating in
        var isToolMode = structuredOpts.Mode.Equals("tool", StringComparison.OrdinalIgnoreCase);
        var isNativeUnionMode = !isToolMode && structuredOpts.UnionTypes is { Length: > 0 };

        // Check if tool mode is using union types (merged union behavior)
        var isToolModeWithUnionTypes = isToolMode && structuredOpts.UnionTypes is { Length: > 0 };
        var outputToolName = structuredOpts.ToolName ?? $"return_{schemaName}";

        // Build set of output tool names for tool mode with union types
        HashSet<string>? unionToolNames = null;
        Dictionary<string, Type>? unionToolTypeMap = null;
        if (isToolModeWithUnionTypes && structuredOpts.UnionTypes != null)
        {
            unionToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            unionToolTypeMap = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            foreach (var unionType in structuredOpts.UnionTypes)
            {
                var toolName = $"return_{unionType.Name}";
                unionToolNames.Add(toolName);
                unionToolTypeMap[toolName] = unionType;
            }
        }

        // Observability: Track metrics
        var messageId = Guid.NewGuid().ToString("N")[..12];
        var startTime = Stopwatch.GetTimestamp();
        var parseAttemptCount = 0;

        // Emit start event for observability
        var outputMode = isToolMode ? "tool" : (isNativeUnionMode ? "native-union" : "native");
        yield return new StructuredOutputStartEvent(
            MessageId: messageId,
            OutputTypeName: schemaName,
            OutputMode: outputMode);

        await foreach (var evt in RunTurnStreamAsync(messages, session, thread, options, cancellationToken))
        {
            // ═══════════════════════════════════════════════════════════════
            // PASS-THROUGH: All request events (built-in + custom)
            // Uses interface check - supports PermissionRequestEvent,
            // ContinuationRequestEvent, ClarificationRequestEvent, and any
            // custom events implementing IAgentRequestEvent
            // ═══════════════════════════════════════════════════════════════
            if (evt is IAgentRequestEvent)
            {
                yield return evt;
                continue;
            }

            // ═══════════════════════════════════════════════════════════════
            // TOOL MODE: Capture output tool arguments
            // ═══════════════════════════════════════════════════════════════
            if (isToolMode)
            {
                if (evt is ToolCallStartEvent toolStart)
                {
                    // Check if this is our output tool (single tool mode) or a union output tool.
                    if (isToolMode && !isToolModeWithUnionTypes && toolStart.Name == outputToolName)
                    {
                        // Single type tool mode: use the single return tool
                        outputToolCallId = toolStart.CallId;
                        continue;
                    }
                    else if (isToolModeWithUnionTypes && unionToolNames!.Contains(toolStart.Name))
                    {
                        // Tool mode with union types: check against union tool names
                        outputToolCallId = toolStart.CallId;
                        matchedUnionType = unionToolTypeMap![toolStart.Name];
                        continue;
                    }
                }

                if (evt is ToolCallArgsEvent argsEvt && argsEvt.CallId == outputToolCallId)
                {
                    // Tool mode: Args arrive COMPLETE (M.E.AI accumulates internally)
                    // No streaming partials possible - just store for final parsing
                    textAccumulator.Clear();
                    textAccumulator.Append(argsEvt.ArgsJson);
                    continue;
                }

                if (evt is ToolCallEndEvent toolEnd && toolEnd.CallId == outputToolCallId)
                {
                    // Final parse for tool mode
                    var finalJson = textAccumulator.ToString();
                    var elapsed = Stopwatch.GetElapsedTime(startTime);
                    var resultTypeName = isToolModeWithUnionTypes && matchedUnionType != null
                        ? matchedUnionType.Name
                        : schemaName;

                    // Emit observability complete event
                    yield return new StructuredOutputCompleteEvent(
                        MessageId: messageId,
                        OutputTypeName: resultTypeName,
                        TotalParseAttempts: parseAttemptCount,
                        FinalJsonLength: finalJson.Length,
                        Duration: elapsed);

                    if (isToolModeWithUnionTypes && matchedUnionType != null)
                    {
                        // Tool mode with union types: deserialize to the specific union type, then cast to T
                        yield return EmitUnionResult<T>(finalJson, matchedUnionType, serializerOptions);
                    }
                    else
                    {
                        yield return EmitFinalResult<T>(finalJson, schemaName, serializerOptions);
                    }
                    yield break;
                }
            }

            // ═══════════════════════════════════════════════════════════════
            // NATIVE MODE: Accumulate text deltas
            // ═══════════════════════════════════════════════════════════════
            if (evt is TextDeltaEvent delta)
            {
                textAccumulator.Append(delta.Text);

                // Debounced partial parsing
                if (structuredOpts.StreamPartials &&
                    debounceStopwatch.ElapsedMilliseconds >= structuredOpts.PartialDebounceMs)
                {
                    if (TryParsePartial<T>(textAccumulator.ToString(), serializerOptions, out var partial, out var closedJson) &&
                        closedJson != lastPartialJson)
                    {
                        lastPartialJson = closedJson;
                        debounceStopwatch.Restart();
                        parseAttemptCount++;

                        // Emit observability event for partial parse
                        yield return new StructuredOutputPartialEvent(
                            MessageId: messageId,
                            OutputTypeName: schemaName,
                            ParseAttempt: parseAttemptCount,
                            AccumulatedJsonLength: textAccumulator.Length);

                        yield return new StructuredResultEvent<T>(partial, IsPartial: true, closedJson);
                    }
                }
                continue;
            }

            // ═══════════════════════════════════════════════════════════════
            // NATIVE MODE STREAM END: Final validation
            // In tool mode, ignore TextMessageEndEvent - we wait for output tool
            // ═══════════════════════════════════════════════════════════════
            if (evt is TextMessageEndEvent && !isToolMode)
            {
                var finalJson = textAccumulator.ToString();
                var elapsed = Stopwatch.GetElapsedTime(startTime);

                // Emit observability complete event
                yield return new StructuredOutputCompleteEvent(
                    MessageId: messageId,
                    OutputTypeName: schemaName,
                    TotalParseAttempts: parseAttemptCount,
                    FinalJsonLength: finalJson.Length,
                    Duration: elapsed);

                // Use appropriate emitter based on whether this is native union mode
                if (isNativeUnionMode)
                {
                    yield return EmitNativeUnionResult<T>(finalJson, structuredOpts.UnionTypes!, serializerOptions);
                }
                else
                {
                    yield return EmitFinalResult<T>(finalJson, schemaName, serializerOptions);
                }
                yield break;
            }

            // Pass through other events (observability, etc.)
            yield return evt;
        }

        // Stream ended without explicit end event - try to parse what we have
        // Only for native mode - tool mode must receive an output tool call
        if (textAccumulator.Length > 0 && !isToolMode)
        {
            var finalJson = textAccumulator.ToString();
            var elapsed = Stopwatch.GetElapsedTime(startTime);

            // Emit observability complete event
            yield return new StructuredOutputCompleteEvent(
                MessageId: messageId,
                OutputTypeName: schemaName,
                TotalParseAttempts: parseAttemptCount,
                FinalJsonLength: finalJson.Length,
                Duration: elapsed);

            // Use appropriate emitter based on whether this is native union mode
            if (isNativeUnionMode)
            {
                yield return EmitNativeUnionResult<T>(finalJson, structuredOpts.UnionTypes!, serializerOptions);
            }
            else
            {
                yield return EmitFinalResult<T>(finalJson, schemaName, serializerOptions);
            }
        }
    }

    /// <summary>
    /// Convenience overload: Runs structured output from a string message.
    /// </summary>
    internal IAsyncEnumerable<AgentEvent> RunStructuredStreamAsync<T>(
        string userMessage,
        Session? session = null,
        Thread? thread = null,
        AgentRunConfig? options = null,
        CancellationToken cancellationToken = default) where T : class
        => RunStructuredStreamAsync<T>(
            new[] { new ChatMessage(ChatRole.User, userMessage) },
            session, thread, options, cancellationToken);

    /// <summary>
    /// Runs structured output from a text input and dispatches results through Agent subscribers.
    /// </summary>
    public Task RunStructuredAsync<T>(
        string userMessage,
        string? sessionId = null,
        string? threadId = "main",
        AgentRunConfig? runConfig = null,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);

        return RunStructuredAsync<T>(new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, userMessage)],
            SessionId = sessionId,
            ThreadId = threadId,
            RunConfig = runConfig
        }, cancellationToken);
    }

    /// <summary>
    /// Runs structured output from an input event and dispatches results through Agent subscribers.
    /// </summary>
    public async Task RunStructuredAsync<T>(
        AgentInputEvent input,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(input);

        var (messages, session, thread, options) = await ResolveStructuredInputAsync(input, cancellationToken)
            .ConfigureAwait(false);

        await foreach (var evt in RunStructuredStreamAsync<T>(
            messages,
            session,
            thread,
            options,
            cancellationToken).ConfigureAwait(false))
        {
            if (IsStructuredOutputEvent<T>(evt))
            {
                await CommitAndPublishThreadEventAsync(
                    thread,
                    evt,
                    GetActiveEventCoordinator(),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<(IEnumerable<ChatMessage> Messages, Session? Session, Thread? Thread, AgentRunConfig Options)> ResolveStructuredInputAsync(
        AgentInputEvent input,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ChatMessage> messages;
        Session? session = null;
        Thread? thread = null;
        AgentRunConfig? options;

        switch (input)
        {
            case UserMessagesInputEvent messageInput:
                messages = messageInput.Messages;
                options = messageInput.RunConfig;
                if (messageInput.Session != null || messageInput.Thread != null)
                {
                    if (messageInput.Session is null || messageInput.Thread is null)
                        throw new InvalidOperationException("UserMessagesInputEvent must provide both Session and Thread for process-local scoped runs.");

                    session = messageInput.Session;
                    thread = messageInput.Thread;
                }
                else if (!string.IsNullOrWhiteSpace(messageInput.SessionId))
                {
                    (session, thread) = await LoadSessionAndThreadAsync(messageInput.SessionId, messageInput.ThreadId, cancellationToken)
                        .ConfigureAwait(false);
                }
                break;

            default:
                throw new NotSupportedException(
                    $"Event type {input.GetType().Name} cannot be used as structured agent input.");
        }

        options ??= new AgentRunConfig();
        options.StructuredOutput ??= new StructuredOutputOptions();

        return (messages, session, thread, options);
    }

    private static bool IsStructuredOutputEvent<T>(AgentEvent evt) where T : class
    {
        return evt is StructuredOutputStartEvent
            or StructuredOutputPartialEvent
            or StructuredOutputCompleteEvent
            or StructuredResultEvent<T>;
    }

    // ═══════════════════════════════════════════════════════════════
    // STRUCTURED OUTPUT PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves serializer options, ensuring they're ready for GetTypeInfo().
    /// Falls back to AIJsonUtilities.DefaultOptions if not specified.
    /// </summary>
    private static JsonSerializerOptions ResolveSerializerOptions(StructuredOutputOptions opts)
    {
        var options = opts.SerializerOptions ?? AIJsonUtilities.DefaultOptions;
        options.MakeReadOnly(); // Required before GetTypeInfo()
        return options;
    }

    private void ConfigureStructuredOutputOptions<T>(
        AgentRunConfig options,
        JsonSerializerOptions serializerOptions) where T : class
    {
        options.Clients.Chat ??= new ChatClientConfig();
        var chatOptions = options.Clients.Chat;
        var structuredOpts = options.StructuredOutput!;
        var schemaName = structuredOpts.SchemaName ?? typeof(T).Name;
        var schemaDesc = structuredOpts.SchemaDescription ?? $"Response of type {schemaName}";

        if (structuredOpts.Mode.Equals("tool", StringComparison.OrdinalIgnoreCase))
        {
            // Tool mode: Create output tool(s) and add to RuntimeTools
            // RuntimeTools are merged with agent's configured tools in RunAsync
            options.RuntimeTools ??= new List<AITool>();

            if (structuredOpts.UnionTypes is { Length: > 0 })
            {
                // Multiple types specified → create one tool per type (union behavior)
                // LLM chooses which type to return by calling the corresponding tool
                foreach (var unionType in structuredOpts.UnionTypes)
                {
                    var tool = CreateOutputToolForType(unionType, serializerOptions);
                    options.RuntimeTools.Add(tool);
                }
            }
            else
            {
                // Single type from generic parameter T
                var outputTool = CreateOutputTool<T>(structuredOpts, serializerOptions);
                options.RuntimeTools.Add(outputTool);
            }

            // Force LLM to call one of the return tools (provider-enforced, not prompt-based)
            // This ensures the LLM cannot output free text - it MUST call a return tool
            options.RuntimeToolMode = ChatToolMode.RequireAny;
        }
        else if (structuredOpts.Mode.Equals("native", StringComparison.OrdinalIgnoreCase))
        {
            // Native mode: Set response format with JSON schema
            if (structuredOpts.UnionTypes is { Length: > 0 })
            {
                // Native union mode: Create anyOf schema combining all union types
                // Provider enforces schema validation, supports streaming partials
                var anyOfSchema = CreateAnyOfSchema(
                    structuredOpts.UnionTypes,
                    schemaName,
                    schemaDesc,
                    serializerOptions);

                chatOptions.RuntimeResponseFormat = ChatResponseFormat.ForJsonSchema(
                    anyOfSchema,
                    schemaName: schemaName,
                    schemaDescription: schemaDesc);
            }
            else
            {
                // Single type native mode: Use provided serializerOptions for consistent schema generation
                chatOptions.RuntimeResponseFormat = ChatResponseFormat.ForJsonSchema<T>(
                    serializerOptions,
                    schemaName: schemaName,
                    schemaDescription: schemaDesc);
            }
        }
        else
        {
            throw new InvalidOperationException(
                $"Unsupported structured output mode '{structuredOpts.Mode}'. Use 'native' or 'tool'.");
        }
    }

    /// <summary>
    /// Creates an output tool using HPDAIFunctionFactory.
    /// Output tools are never executed - calling one terminates the agent run
    /// and the arguments ARE the structured output.
    /// </summary>
    private AIFunction CreateOutputTool<T>(
        StructuredOutputOptions options,
        JsonSerializerOptions serializerOptions) where T : class
    {
        var schemaName = options.SchemaName ?? typeof(T).Name;
        var toolName = options.ToolName ?? $"return_{schemaName}";
        var description = options.SchemaDescription ?? $"Submit the final {schemaName} result";

        // Use HPDAIFunctionFactory - our existing factory that supports AdditionalProperties
        return HPDAIFunctionFactory.Create(
            invocation: (_, _, _) => Task.FromResult<object?>(null), // Output tools never execute
            options: new HPDAIFunctionFactoryOptions
            {
                Name = toolName,
                Description = description,
                SchemaProvider = () => AIJsonUtilities.CreateJsonSchema(
                    typeof(T),
                    description: description,
                    serializerOptions: serializerOptions), // Use provided options for AOT compatibility
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["Kind"] = "Output",
                    ["OutputType"] = typeof(T).FullName
                }
            });
    }

    /// <summary>
    /// Creates an output tool for a specific type (used in union mode).
    /// Non-generic version that accepts a Type parameter.
    /// </summary>
    private AIFunction CreateOutputToolForType(
        Type outputType,
        JsonSerializerOptions serializerOptions)
    {
        var typeName = outputType.Name;
        var toolName = $"return_{typeName}";
        var description = $"Submit a {typeName} result";

        return HPDAIFunctionFactory.Create(
            invocation: (_, _, _) => Task.FromResult<object?>(null), // Output tools never execute
            options: new HPDAIFunctionFactoryOptions
            {
                Name = toolName,
                Description = description,
                SchemaProvider = () => AIJsonUtilities.CreateJsonSchema(
                    outputType,
                    description: description,
                    serializerOptions: serializerOptions),
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["Kind"] = "Output",
                    ["OutputType"] = outputType.FullName
                }
            });
    }

    /// <summary>
    /// Attempts to parse partial JSON into a typed result.
    /// Uses AOT-safe GetTypeInfo() pattern for deserialization.
    /// Returns the closed JSON string for deduplication (comparing JSON, not object references).
    /// </summary>
    private static bool TryParsePartial<T>(
        string json,
        JsonSerializerOptions serializerOptions,
        [NotNullWhen(true)] out T? result,
        [NotNullWhen(true)] out string? closedJson) where T : class
    {
        result = default;
        closedJson = null;

        var closed = PartialJsonCloser.TryClose(json);
        if (closed == null)
            return false;

        try
        {
            // AOT-safe: Use GetTypeInfo() instead of generic Deserialize<T>()
            var typeInfo = (JsonTypeInfo<T>)serializerOptions.GetTypeInfo(typeof(T));
            result = JsonSerializer.Deserialize(closed, typeInfo);
            if (result != null)
            {
                closedJson = closed;
                return true;
            }
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static AgentEvent EmitFinalResult<T>(
        string rawJson,
        string typeName,
        JsonSerializerOptions serializerOptions) where T : class
    {
        // Strip markdown fences if present
        var json = StripMarkdownFences(rawJson);

        try
        {
            // AOT-safe: Use GetTypeInfo() instead of generic Deserialize<T>()
            var typeInfo = (JsonTypeInfo<T>)serializerOptions.GetTypeInfo(typeof(T));
            var result = JsonSerializer.Deserialize(json, typeInfo);

            if (result == null)
            {
                return new StructuredOutputErrorEvent(
                    json,
                    "Deserialization returned null",
                    typeName);
            }
            else
            {
                return new StructuredResultEvent<T>(result, IsPartial: false, json);
            }
        }
        catch (JsonException ex)
        {
            return new StructuredOutputErrorEvent(
                json,
                ex.Message,
                typeName,
                ex);
        }
    }

    /// <summary>
    /// Emits a structured result for union mode.
    /// Deserializes to the specific union type, then casts to the base type T.
    /// </summary>
    private static AgentEvent EmitUnionResult<T>(
        string rawJson,
        Type unionType,
        JsonSerializerOptions serializerOptions) where T : class
    {
        // Strip markdown fences if present
        var json = StripMarkdownFences(rawJson);
        var typeName = unionType.Name;

        try
        {
            // Non-generic deserialization for the specific union type
            var typeInfo = serializerOptions.GetTypeInfo(unionType);
            var result = JsonSerializer.Deserialize(json, typeInfo);

            if (result == null)
            {
                return new StructuredOutputErrorEvent(
                    json,
                    "Deserialization returned null",
                    typeName);
            }

            // Cast to T (the base type)
            if (result is T typedResult)
            {
                return new StructuredResultEvent<T>(typedResult, IsPartial: false, json);
            }
            else
            {
                return new StructuredOutputErrorEvent(
                    json,
                    $"Result of type {result.GetType().Name} is not assignable to {typeof(T).Name}",
                    typeName);
            }
        }
        catch (JsonException ex)
        {
            return new StructuredOutputErrorEvent(
                json,
                ex.Message,
                typeName,
                ex);
        }
    }

    private static string StripMarkdownFences(string json)
    {
        var span = json.AsSpan().Trim();

        if (span.StartsWith("```"))
        {
            var newlineIdx = span.IndexOf('\n');
            if (newlineIdx > 0)
                span = span[(newlineIdx + 1)..];

            if (span.EndsWith("```"))
                span = span[..^3].Trim();
        }

        return span.ToString();
    }

    /// <summary>
    /// Creates an anyOf JSON schema combining multiple union types.
    /// Used for native mode union support where the provider enforces schema validation.
    /// </summary>
    private static JsonElement CreateAnyOfSchema(
        Type[] unionTypes,
        string schemaName,
        string schemaDescription,
        JsonSerializerOptions serializerOptions)
    {
        var anyOfSchemas = new JsonArray();

        foreach (var unionType in unionTypes)
        {
            // Generate schema for each union type
            var typeSchema = AIJsonUtilities.CreateJsonSchema(
                unionType,
                description: unionType.Name,
                serializerOptions: serializerOptions);

            anyOfSchemas.Add(typeSchema);
        }

        // Create combined schema with anyOf
        var combinedSchema = new JsonObject
        {
            ["title"] = schemaName,
            ["description"] = schemaDescription,
            ["anyOf"] = anyOfSchemas
        };

        return JsonSerializer.SerializeToElement(combinedSchema);
    }

    /// <summary>
    /// Tries to detect which union type matches the given JSON by attempting deserialization.
    /// Returns the first type that successfully deserializes and is assignable to T.
    /// </summary>
    private static (T? result, Type? matchedType) TryDeserializeUnionType<T>(
        string json,
        Type[] unionTypes,
        JsonSerializerOptions serializerOptions) where T : class
    {
        foreach (var unionType in unionTypes)
        {
            try
            {
                var typeInfo = serializerOptions.GetTypeInfo(unionType);
                var result = JsonSerializer.Deserialize(json, typeInfo);

                if (result is T typedResult)
                {
                    return (typedResult, unionType);
                }
            }
            catch (JsonException)
            {
                // This type doesn't match, try next
                continue;
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Emits a structured result for native union mode.
    /// Tries to deserialize to each union type until one matches.
    /// </summary>
    private static AgentEvent EmitNativeUnionResult<T>(
        string rawJson,
        Type[] unionTypes,
        JsonSerializerOptions serializerOptions) where T : class
    {
        var json = StripMarkdownFences(rawJson);

        var (result, matchedType) = TryDeserializeUnionType<T>(json, unionTypes, serializerOptions);

        if (result != null && matchedType != null)
        {
            return new StructuredResultEvent<T>(result, IsPartial: false, json);
        }

        // No type matched
        var typeNames = string.Join(", ", unionTypes.Select(t => t.Name));
        return new StructuredOutputErrorEvent(
            json,
            $"JSON did not match any union type. Expected one of: {typeNames}",
            "UnionType");
    }

    #endregion

    /// <summary>
    /// Builds initial context properties dictionary from AgentRunConfig.
    /// Merges client tool input and context properties into a single dictionary.
    /// </summary>
    private static Dictionary<string, object>? BuildInitialContextProperties(AgentRunConfig? options)
    {
        if (options == null)
            return null;

        Dictionary<string, object>? properties = null;

        // Add ClientToolInput if present
        if (options.Tools?.ClientInput != null)
        {
            properties ??= new Dictionary<string, object>();
            properties["AgentClientInput"] = options.Tools.ClientInput;
        }

        // Add AgentRunConfig itself for middleware access
        properties ??= new Dictionary<string, object>();
        properties["AgentRunConfig"] = options;

        // Merge context overrides
        if (options.Context?.Properties != null)
        {
            properties ??= new Dictionary<string, object>();
            foreach (var kvp in options.Context.Properties)
            {
                properties[kvp.Key] = kvp.Value;
            }
        }

        return properties;
    }


    private static Middleware.AgentModelTransport ResolveModelTransport(AgentRunConfig runConfig)
        => runConfig.Clients.Transport switch
        {
            AgentModelTransportMode.Chat or AgentModelTransportMode.Auto => Middleware.AgentModelTransport.Chat,
            AgentModelTransportMode.Realtime => Middleware.AgentModelTransport.Realtime,
            _ => throw new InvalidOperationException($"Unsupported model transport '{runConfig.Clients.Transport}'.")
        };

    /// <summary>
    /// Resolves system instructions considering AgentRunConfig overrides.
    /// Priority: AgentRunConfig.SystemInstructions > Config.SystemInstructions
    /// Applies the ordered system-instruction override and append policy.
    /// </summary>
    /// <param name="options">Per-invocation options</param>
    /// <returns>Resolved system instructions</returns>
    private string? ResolveSystemInstructions(AgentRunConfig? options)
    {
        // Use override if provided, otherwise fall back to config
        var instructions = options?.SystemInstructions?.Override
            ?? Config?.SystemInstructions
            ?? _messageProcessor.SystemInstructions;

        // Append additional instructions if provided
        if (!string.IsNullOrEmpty(options?.SystemInstructions?.Append))
        {
            instructions = string.IsNullOrEmpty(instructions)
                ? options.SystemInstructions.Append
                : $"{instructions}\n\n{options.SystemInstructions.Append}";
        }

        return instructions;
    }

    private static AgentClientsConfig? CreateRunClientOverrides(AgentRunConfig? options) => options?.Clients;

    /// <summary>
    /// Applies system instruction overrides from AgentRunConfig to ChatOptions.
    /// Creates a new ChatOptions instance with the resolved instructions.
    /// </summary>
    /// <param name="chatOptions">Base chat options (can be null)</param>
    /// <param name="runConfig">Per-invocation options</param>
    /// <returns>ChatOptions with resolved system instructions</returns>
    private ChatOptions? ApplySystemInstructionOverrides(ChatOptions? chatOptions, AgentRunConfig? runConfig)
    {
        // If no overrides, return as-is
        if (runConfig == null ||
            (string.IsNullOrEmpty(runConfig.SystemInstructions?.Override) &&
             string.IsNullOrEmpty(runConfig.SystemInstructions?.Append)))
        {
            return chatOptions;
        }

        var resolvedInstructions = ResolveSystemInstructions(runConfig);
        if (string.IsNullOrEmpty(resolvedInstructions))
            return chatOptions;

        var newOptions = chatOptions?.Clone() ?? new ChatOptions();
        newOptions.Instructions = resolvedInstructions;
        return newOptions;
    }

    /// <summary>
    /// Validates background responses usage and logs warnings for common mistakes.
    /// Philosophy: "Let it flow" - warn but don't block, allow graceful degradation.
    /// </summary>
    /// <param name="runConfig">Per-run options</param>
    /// <param name="allowBackgroundResponses">Resolved background responses setting</param>
    /// <param name="messageCount">Number of input messages</param>
    private void ValidateBackgroundResponsesUsage(
        AgentRunConfig? runConfig,
        bool allowBackgroundResponses,
        int messageCount)
    {
        // Skip validation if no background-related settings are used
        if (!allowBackgroundResponses && runConfig?.BackgroundResponses?.ContinuationToken == null)
            return;

        // Warning 1: Messages provided with ContinuationToken (messages will be ignored)
        if (runConfig?.BackgroundResponses?.ContinuationToken != null && messageCount > 0)
        {
            _agentLogger?.LogWarning(
                "Background responses: Messages provided with ContinuationToken will be ignored during polling. " +
                "When polling with a token, only the token is used - messages are not sent to the provider.");
        }

        // Warning 2: ContinuationToken provided without AllowBackgroundResponses explicitly set
        // This might indicate the user doesn't realize they're in polling mode
        if (runConfig?.BackgroundResponses?.ContinuationToken != null && runConfig.BackgroundResponses.Allow != true)
        {
            _agentLogger?.LogInformation(
                "Background responses: ContinuationToken provided without AllowBackgroundResponses=true. " +
                "Token will be used for polling, but consider explicitly enabling background responses.");
        }

        // Warning 3: AutoPollToCompletion enabled with manual ContinuationToken
        // Auto-poll handles polling automatically - manual token might cause confusion
        if (Config?.BackgroundResponses?.AutoPollToCompletion == true && runConfig?.BackgroundResponses?.ContinuationToken != null)
        {
            _agentLogger?.LogWarning(
                "Background responses: Manual ContinuationToken provided with AutoPollToCompletion enabled. " +
                "Auto-poll mode handles polling automatically. Manual token usage may cause unexpected behavior.");
        }
    }

    /// <summary>
    /// Applies background responses settings to ChatOptions.
    /// Sets AllowBackgroundResponses and ContinuationToken for M.E.AI providers.
    /// </summary>
    /// <param name="chatOptions">Base chat options (can be null)</param>
    /// <param name="allowBackground">Whether to allow background responses</param>
    /// <param name="continuationToken">Continuation token for polling/resumption</param>
    /// <returns>ChatOptions with background responses settings applied</returns>
#pragma warning disable MEAI001 // Experimental API - Background Responses
    private static ChatOptions ApplyBackgroundResponsesOptions(
        ChatOptions? chatOptions,
        bool allowBackground,
        ResponseContinuationToken? continuationToken)
    {
        var newOptions = chatOptions?.Clone() ?? new ChatOptions();
        newOptions.AllowBackgroundResponses = allowBackground;
        newOptions.ContinuationToken = continuationToken;

        return newOptions;
    }
#pragma warning restore MEAI001

    //──────────────────────────────────────────────────────────────────
    // AUTO-POLL BACKGROUND RESPONSES SUPPORT
    //──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the agent with automatic polling for background operations.
    /// When enabled, this method internally uses background mode + polling to complete long-running operations,
    /// providing HTTP timeout resilience without changing caller code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method is useful for scenarios where:
    /// - HTTP gateways have timeout limits (30-60s)
    /// - Serverless functions have execution limits
    /// - You want transparent timeout resilience
    /// </para>
    /// <para>
    /// Configuration is controlled by:
    /// - <see cref="BackgroundResponsesConfig.AutoPollToCompletion"/> - Enables auto-polling
    /// - <see cref="BackgroundResponsesConfig.DefaultPollingInterval"/> - Interval between polls
    /// - <see cref="BackgroundResponsesConfig.DefaultTimeout"/> - Maximum wait time
    /// - <see cref="BackgroundResponsesConfig.MaxPollAttempts"/> - Maximum poll attempts
    /// </para>
    /// </remarks>
    /// <param name="messages">Messages to process</param>
    /// <param name="session">Session metadata and store reference</param>
    /// <param name="thread">Thread containing conversation messages</param>
    /// <param name="options">Optional per-invocation run options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stream of internal agent events</returns>
    internal async IAsyncEnumerable<AgentEvent> RunWithAutoPollAsync(
        IEnumerable<ChatMessage> messages,
        Session? session = null,
        Thread? thread = null,
        AgentRunConfig? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var config = Config?.BackgroundResponses;
        var autoPoll = config?.AutoPollToCompletion ?? false;

        if (!autoPoll)
        {
            // Auto-poll not enabled, just run normally
            await foreach (var evt in RunTurnStreamAsync(messages, session, thread, options, cancellationToken))
            {
                yield return evt;
            }
            yield break;
        }

        // Auto-poll mode: Enable background responses and poll until completion
        options ??= new AgentRunConfig();
        options.BackgroundResponses ??= new BackgroundResponsesRunConfig();
        options.BackgroundResponses.Allow = true;

        var pollInterval = options.BackgroundResponses.PollingInterval ?? config!.DefaultPollingInterval;
        var timeout = options.BackgroundResponses.Timeout ?? config!.DefaultTimeout;
        var maxAttempts = config!.MaxPollAttempts;

        ResponseContinuationToken? lastToken = null;
        string? operationId = null;
        var startTime = DateTimeOffset.UtcNow;
        var attempts = 0;
        var isFirstRun = true;

        while (true)
        {
            // Check timeout
            if (timeout.HasValue && DateTimeOffset.UtcNow - startTime > timeout.Value)
            {
                if (operationId is not null)
                    await FailProviderResponseOperationAsync(
                        operationId, "provider_response_timeout",
                        $"Model response timed out after {timeout.Value}.").ConfigureAwait(false);
                yield break;
            }

            // Check max attempts (only after first run)
            if (!isFirstRun && attempts >= maxAttempts)
            {
                if (operationId is not null)
                    await FailProviderResponseOperationAsync(
                        operationId, "provider_response_poll_limit",
                        $"Model response exceeded {maxAttempts} poll attempts.").ConfigureAwait(false);
                yield break;
            }

            // Set continuation token for polling (not on first run)
            if (!isFirstRun && lastToken != null)
            {
                options.BackgroundResponses!.ContinuationToken = lastToken;
                attempts++;

                if (operationId is not null)
                    await _operationRegistry.TransitionAsync(operationId, new AgentOperationTransition
                    {
                        ProviderStatus = AgentOperationProviderStatus.Running,
                        ProviderDeduplicationKey = $"model.response.poll:{attempts}"
                    }, cancellationToken).ConfigureAwait(false);
            }

            // Run the agent
            var messagesForRun = isFirstRun ? messages : Enumerable.Empty<ChatMessage>();
            lastToken = null;

            await foreach (var evt in RunTurnStreamAsync(messagesForRun, session, thread, options, cancellationToken))
            {
                yield return evt;

                if (evt is AgentOperationRegisteredEvent
                    {
                        Operation.SourceKind: AgentOperationSourceKind.ProviderOperation,
                        Operation.Name: "model.response"
                    } registered)
                    operationId = registered.Operation.OperationId;
            }

            if (operationId is null)
                operationId = _operationRegistry.Snapshot()
                    .Where(static candidate => candidate.SourceKind == AgentOperationSourceKind.ProviderOperation &&
                        candidate.Name == "model.response" &&
                        candidate.ProviderStatus is not AgentOperationProviderStatus.Completed and
                            not AgentOperationProviderStatus.Failed and not AgentOperationProviderStatus.Cancelled)
                    .OrderByDescending(static candidate => candidate.RegisteredAt)
                    .Select(static candidate => candidate.OperationId)
                    .FirstOrDefault();
            lastToken = operationId is null ? null : GetProviderResponseContinuation(operationId);

            isFirstRun = false;

            // If no token, operation completed
            if (lastToken == null)
            {
                if (operationId is not null && _operationRegistry.TryGet(operationId, out var completed) &&
                    completed!.Snapshot.ProviderStatus is not AgentOperationProviderStatus.Completed and
                        not AgentOperationProviderStatus.Failed and not AgentOperationProviderStatus.Cancelled)
                {
                    await _operationRegistry.TransitionAsync(operationId, new AgentOperationTransition
                    {
                        ProviderStatus = AgentOperationProviderStatus.Completed,
                        Completion = new AgentOperationCompletion("Model response completed."),
                        ProviderDeduplicationKey = $"model.response.completed:{operationId}"
                    }, cancellationToken).ConfigureAwait(false);
                }
                yield break;
            }

            // Wait before next poll
            await Task.Delay(pollInterval, cancellationToken);
        }
    }

    private AgentOperation? FindProviderResponseOperation(ResponseContinuationToken? continuationToken)
    {
        if (continuationToken is null)
            return null;
        return _operationRegistry.LiveOperations().FirstOrDefault(operation =>
            operation.Observer is ProviderResponseObservation observation &&
            observation.Matches(continuationToken));
    }

    private ResponseContinuationToken? GetProviderResponseContinuation(string operationId) =>
        _operationRegistry.TryGet(operationId, out var operation) &&
        operation!.Observer is ProviderResponseObservation observation
            ? observation.ContinuationToken
            : null;

    private ValueTask<AgentOperationSnapshot> FailProviderResponseOperationAsync(
        string operationId,
        string code,
        string message) =>
        _operationRegistry.TransitionAsync(operationId, new AgentOperationTransition
        {
            ProviderStatus = AgentOperationProviderStatus.Failed,
            Failure = new AgentOperationFailure(code, message),
            ProviderDeduplicationKey = $"model.response.failed:{operationId}:{code}"
        }, CancellationToken.None);

    private sealed class ProviderResponseObservation(ResponseContinuationToken continuationToken) : IAsyncDisposable
    {
        internal ResponseContinuationToken? ContinuationToken { get; set; } = continuationToken;

        internal bool Matches(ResponseContinuationToken candidate) =>
            ContinuationToken is { } current && current.ToBytes().Span.SequenceEqual(candidate.ToBytes().Span);

        public ValueTask DisposeAsync()
        {
            ContinuationToken = null;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Runs the agent with automatic polling for background operations (string message overload).
    /// </summary>
    internal IAsyncEnumerable<AgentEvent> RunWithAutoPollAsync(
        string userMessage,
        Session? session = null,
        Thread? thread = null,
        AgentRunConfig? options = null,
        CancellationToken cancellationToken = default)
    {
        return RunWithAutoPollAsync(
            [new ChatMessage(ChatRole.User, userMessage)],
            session,
            thread,
            options,
            cancellationToken);
    }

    //──────────────────────────────────────────────────────────────────
    // SESSION-BASED API (New simplified API)
    //──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the agent statelessly with a list of messages (no session, no persistence).
    /// Suitable for one-off calls, sub-agents, and multi-agent graph nodes.
    /// </summary>
    internal IAsyncEnumerable<AgentEvent> RunTurnStreamAsync(
        IEnumerable<ChatMessage> messages,
        AgentRunConfig? options = null,
        CancellationToken cancellationToken = default)
    {
        return RunTurnStreamAsync(messages, session: null, thread: null, options, cancellationToken);
    }

    /// <summary>
    /// Runs the agent against an existing session. The session must have been created
    /// with <see cref="CreateSessionAsync"/> before calling this method.
    /// </summary>
    /// <param name="userMessage">The user's message text</param>
    /// <param name="sessionId">Session identifier — must already exist in the store</param>
    /// <param name="threadId">Thread identifier. Defaults to "main" if the session has only one thread.</param>
    /// <param name="options">Optional per-invocation run options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stream of agent events</returns>
    /// <exception cref="InvalidOperationException">Thrown if no session store is configured</exception>
    /// <exception cref="SessionNotFoundException">Thrown if the session or thread does not exist in the store</exception>
    /// <exception cref="AmbiguousThreadException">Thrown if threadId is omitted and the session has multiple threads</exception>
    /// <remarks>
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// await agent.CreateSessionAsync("user-123");
    ///
    /// await agent.RunTurnStreamAsync("Hello!", "user-123");
    /// await agent.RunTurnStreamAsync("Follow up", "user-123");  // Continues conversation
    /// </code>
    /// </para>
    /// </remarks>
    internal async IAsyncEnumerable<AgentEvent> RunTurnStreamAsync(
        string userMessage,
        string sessionId,
        string? threadId = null,
        AgentRunConfig? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (session, thread) = await LoadSessionAndThreadAsync(sessionId, threadId, cancellationToken);

        await foreach (var evt in RunTurnStreamAsync(userMessage, session, thread, options, cancellationToken))
        {
            yield return evt;
        }

        // Auto-save if configured
        if (Config.SessionStoreOptions?.PersistAfterTurn == true)
        {
            await SaveSessionAndThreadAsync(session, thread, cancellationToken);
        }
    }

    /// <summary>
    /// Convenience overload for running with a single ChatMessage and session ID.
    /// Useful for sending messages with typed content (ImageContent, AudioContent, etc.)
    /// The session must have been created with <see cref="CreateSessionAsync"/> before calling this method.
    /// </summary>
    /// <param name="message">Single chat message to send</param>
    /// <param name="sessionId">Session identifier — must already exist in the store</param>
    /// <param name="threadId">Thread identifier. Defaults to "main" if the session has only one thread.</param>
    /// <param name="options">Optional run options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stream of agent events</returns>
    /// <exception cref="SessionNotFoundException">Thrown if the session or thread does not exist in the store</exception>
    /// <remarks>
    /// <para>
    /// <b>Example - Send image to vision model:</b>
    /// <code>
    /// var image = await ImageContent.FromFileAsync("photo.jpg");
    /// await agent.RunTurnStreamAsync(
    ///     new ChatMessage(ChatRole.User, [new TextContent("What's in this?"), image]),
    ///     sessionId);
    /// </code>
    /// </para>
    /// </remarks>
    internal async IAsyncEnumerable<AgentEvent> RunTurnStreamAsync(
        ChatMessage message,
        string sessionId,
        string? threadId = null,
        AgentRunConfig? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (session, thread) = await LoadSessionAndThreadAsync(sessionId, threadId, cancellationToken);

        await foreach (var evt in RunTurnStreamAsync(new[] { message }, session, thread, options, cancellationToken))
        {
            yield return evt;
        }

        // Auto-save if configured
        if (Config.SessionStoreOptions?.PersistAfterTurn == true)
        {
            await SaveSessionAndThreadAsync(session, thread, cancellationToken);
        }
    }

    /// <summary>
    /// Loads a session and thread by ID from the configured store.
    /// Throws <see cref="SessionNotFoundException"/> if the session or thread does not exist.
    /// Use <see cref="CreateSessionAsync"/> to create a session before calling this or RunAsync.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="threadId">Thread identifier. Defaults to "main" if the session has only one thread.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The loaded session and thread</returns>
    /// <exception cref="InvalidOperationException">Thrown if no session store is configured</exception>
    /// <exception cref="SessionNotFoundException">Thrown if the session or thread does not exist in the store</exception>
    /// <exception cref="AmbiguousThreadException">Thrown if threadId is omitted and the session has multiple threads</exception>
    internal async Task<(Session session, Thread thread)> LoadSessionAndThreadAsync(
        string sessionId,
        string? threadId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var store = Config.SessionStore
            ?? throw new InvalidOperationException(
                "No session store configured. Use WithSessionStore() on AgentBuilder to configure persistence.");

        // If threadId was not specified, check for ambiguity
        if (threadId is null)
        {
            var threadIds = (await store.CollectThreadDescriptorsAsync(
                    sessionId, cancellationToken: cancellationToken).ConfigureAwait(false))
                .Select(descriptor => descriptor.Key.ThreadId)
                .ToList();
            if (threadIds.Count > 1)
            {
                throw new AmbiguousThreadException(sessionId, threadIds);
            }
            threadId = "main";
        }

        var session = await store.LoadSessionAsync(sessionId, cancellationToken)
            ?? throw new SessionNotFoundException(sessionId);
        session.Store = store;

        var thread = await store.ProjectThreadAsync(
                sessionId,
                threadId,
                ThreadProjectionPurpose.ModelContext,
                cancellationToken)
            ?? throw new SessionNotFoundException(sessionId, threadId);

        // Ensure back-reference is set on loaded threads
        thread.Session = session;

        var events = await store.CollectThreadEventsAsync(
            new ThreadKey(sessionId, threadId),
            cancellationToken).ConfigureAwait(false);
        if (events is not null)
        {
            await _operationRegistry.RehydrateAsync(events).ConfigureAwait(false);
            await _capabilityCatalog.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            await _capabilityCatalog.ReconcileAsync(
                _operationRegistry.LiveOperations(), cancellationToken).ConfigureAwait(false);
        }

        return (session, thread);
    }

    /// <summary>
    /// Saves session metadata and thread to the configured store.
    /// </summary>
    internal async Task SaveSessionAndThreadAsync(
        Session session,
        Thread thread,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(thread);

        var store = Config.SessionStore
            ?? throw new InvalidOperationException(
                "No session store configured. Use WithSessionStore() on AgentBuilder to configure persistence.");

        await store.SaveSessionAsync(session, cancellationToken);

        var descriptor = await store.GetThreadAsync(
            new ThreadKey(session.Id, thread.Id),
            cancellationToken).ConfigureAwait(false);
        if (descriptor == null)
        {
            await store.SaveInitialThreadAsync(session.Id, thread, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Creates a new session and its default "main" thread in the configured store.
    /// Must be called before <c>RunTurnStreamAsync(userMessage, sessionId)</c> when a store is configured.
    /// </summary>
    /// <param name="sessionId">Session identifier. If null, a new GUID is generated.</param>
    /// <param name="metadata">Optional metadata to attach to the session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created session ID.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no session store is configured, or if a session with the same ID already exists.</exception>
    public async Task<string> CreateSessionAsync(
        string? sessionId = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var store = Config.SessionStore
            ?? throw new InvalidOperationException(
                "No session store configured. Use WithSessionStore() on AgentBuilder to configure persistence.");

        var id = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString() : sessionId;

        var existing = await store.LoadSessionAsync(id, cancellationToken);
        if (existing != null)
            throw new InvalidOperationException(
                $"Session '{id}' already exists. Session IDs must be unique — use a different ID or load the existing session.");

        var session = new Session(id);
        var thread = session.CreateThread(AgentId, "main");
        session.Store = store;

        if (metadata != null)
        {
            foreach (var kvp in metadata)
                session.AddMetadata(kvp.Key, kvp.Value);
        }

        await store.SaveSessionAsync(session, cancellationToken);
        await store.SaveInitialThreadAsync(id, thread, cancellationToken);

        return id;
    }

    /// <summary>
    /// Creates an empty thread in an existing session.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="threadId">Thread identifier. If null, a new GUID is generated.</param>
    /// <param name="name">Optional display name for the thread.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created thread ID.</returns>
    public async Task<string> CreateThreadAsync(
        string sessionId,
        string? threadId = null,
        string? name = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var store = Config.SessionStore
            ?? throw new InvalidOperationException(
                "No session store configured. Use WithSessionStore() on AgentBuilder to configure persistence.");

        var session = await store.LoadSessionAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Session '{sessionId}' not found.");
        session.Store = store;

        var id = string.IsNullOrWhiteSpace(threadId) ? Guid.NewGuid().ToString() : threadId;
        if (await store.GetThreadAsync(new ThreadKey(sessionId, id), cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException(
                $"Thread '{id}' already exists in session '{sessionId}'.");
        }

        var thread = session.CreateThread(AgentId, id);
        if (!string.IsNullOrWhiteSpace(name))
        {
            thread.Name = name;
        }

        session.LastActivity = DateTime.UtcNow;
        await store.SaveInitialThreadAsync(sessionId, thread, cancellationToken)
            .ConfigureAwait(false);
        await store.SaveSessionAsync(session, cancellationToken)
            .ConfigureAwait(false);

        return id;
    }

    //
    // V3 THREAD MANAGEMENT (Session + Thread Architecture)
    //

    /// <summary>
    /// Load thread from session store (V3 API).
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="threadId">Thread identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Thread if found, null otherwise</returns>
    internal async Task<Thread?> ProjectThreadAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        var store = Config?.SessionStore;
        if (store == null)
            return null;

        return await store.ProjectThreadAsync(
            sessionId,
            threadId,
            ThreadProjectionPurpose.ModelContext,
            cancellationToken);
    }

    /// <summary>
    /// List all thread IDs in a session (V3 API).
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of thread IDs</returns>
    internal async Task<List<string>> ListThreadsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var store = Config?.SessionStore;
        if (store == null)
            return [];

        return (await store.CollectThreadDescriptorsAsync(
                sessionId, cancellationToken: cancellationToken).ConfigureAwait(false))
            .Select(descriptor => descriptor.Key.ThreadId)
            .ToList();
    }

    /// <summary>
    /// Delete a thread from a session. SessionId is derived from thread.SessionId.
    /// </summary>
    /// <param name="thread">Thread to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    internal async Task DeleteThreadAsync(
        Thread thread,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(thread);

        var store = Config?.SessionStore;
        if (store != null)
        {
            await store.DeleteThreadAsync(thread.SessionId, thread.Id, cancellationToken);
        }
    }

    /// <summary>
    /// Fork a thread at a specific message id.
    /// Creates a new thread with messages up to the fork point, plus thread-scoped middleware state.
    /// The new thread inherits the source thread's Session reference.
    /// </summary>
    /// <param name="sourceThread">Source thread to fork from</param>
    /// <param name="newThreadId">New thread ID</param>
    /// <param name="fromMessageId">Message id to fork at (inclusive). Null forks from root before any messages.</param>
    /// <param name="metadata">Optional metadata to attach to the new thread.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Newly created thread with Session back-reference set</returns>
    /// <remarks>
    /// <para><b>Behavior:</b></para>
    /// <list type="bullet">
    /// <item>Messages: Copied up to and including fromMessageId</item>
    /// <item>Thread-scoped middleware state: COPIED from source (then diverges)</item>
    /// <item>Session-scoped middleware state: SHARED (not copied, same Session object)</item>
    /// <item>Session back-reference: Copied from source thread</item>
    /// </list>
    /// </remarks>
    internal Task<Thread> ForkThreadAsync(
        Thread sourceThread,
        string newThreadId,
        string? fromMessageId,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
        => ForkThreadAsync(
            sourceThread,
            newThreadId,
            fromMessageId,
            ThreadForkOptions.FromMetadata(metadata),
            cancellationToken);

    internal async Task<Thread> ForkThreadAsync(
        Thread sourceThread,
        string newThreadId,
        string? fromMessageId,
        ThreadForkOptions forkOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceThread);
        ArgumentNullException.ThrowIfNull(forkOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(newThreadId);
        if (forkOptions.Metadata?.Keys.Any(static key => key is
                "forkOperationId" or "forkSourceSessionId" or "forkSourceThreadId" or "forkRequestFingerprint") == true)
            throw new InvalidOperationException("thread_fork_reserved_metadata_key");
        var store = Config.SessionStore
            ?? throw new InvalidOperationException(
                "No session store configured. Use WithSessionStore() on AgentBuilder to configure persistence.");

        var fromMessageIndex = string.IsNullOrWhiteSpace(fromMessageId)
            ? (int?)null
            : ResolveForkMessageIndex(sourceThread, fromMessageId);

        // Create the fork thread metadata and projected read model.
        // The durable copied history is written later by cloning the source event prefix.
        var now = DateTime.UtcNow;
        var newThread = new Thread(sourceThread.SessionId, newThreadId, AgentId)
        {
            ForkedFrom = sourceThread.Id,
            ForkedAtMessageId = string.IsNullOrWhiteSpace(fromMessageId) ? null : fromMessageId,
            ForkedAtMessageIndex = fromMessageIndex,
            Session = sourceThread.Session, // Inherit Session back-reference
            CreatedAt = now,
            LastActivity = now,
            ChildThreads = new List<string>()
        };

        // Build ancestor chain: copy parent's ancestors and add parent
        var ancestors = new Dictionary<string, string>();
        if (sourceThread.Ancestors != null)
        {
            foreach (var kvp in sourceThread.Ancestors)
            {
                ancestors[kvp.Key] = kvp.Value;
            }
        }
        // Add the source thread as an ancestor
        var depth = ancestors.Count;
        ancestors[depth.ToString()] = sourceThread.Id;
        newThread.Ancestors = ancestors;

        int? copyThroughMessageIndex = null;
        ThreadJournalCursor sourceForkBoundary;

        // Populate the in-memory read model up to and including fork point.
        // Root forks start from an empty read model.
        // The stored fork point remains the user's requested message, but the copied
        // event prefix may expand through the rest of the same turn/tool-call group so
        // a fork cannot hydrate with half of a tool interaction.
        if (fromMessageIndex is int resolvedMessageIndex)
        {
            var copyThroughIndex = ExpandForkCopyThroughIndex(sourceThread.Messages, resolvedMessageIndex);
            copyThroughMessageIndex = copyThroughIndex;
            newThread.Messages.AddRange(sourceThread.Messages.Take(copyThroughIndex + 1).Select(CloneMessageForThread));
        }

        var sourceKey = new ThreadKey(sourceThread.SessionId, sourceThread.Id);
        var sourceDescriptor = await store.GetThreadAsync(sourceKey, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Source thread '{sourceThread.Id}' no longer exists.");
        if (copyThroughMessageIndex is int boundaryIndex)
        {
            var boundaryMessages = sourceThread.Messages.Take(boundaryIndex + 1).ToArray();
            var messageIds = boundaryMessages
                .Select(static message => message.MessageId)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Select(static id => id!)
                .ToHashSet(StringComparer.Ordinal);
            var turnIds = boundaryMessages
                .Select(GetMessageTurnId)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Select(static id => id!)
                .ToHashSet(StringComparer.Ordinal);
            var sequence = await ResolveForkCopyThroughSequenceAsync(
                store, sourceKey, messageIds, turnIds, cancellationToken).ConfigureAwait(false) ?? 0;
            sourceForkBoundary = new ThreadJournalCursor(sourceDescriptor.Generation, sequence);
        }
        else
        {
            sourceForkBoundary = ThreadJournalCursor.Start(sourceDescriptor.Generation);
        }

        var forkOperationId = forkOptions.OperationId ?? Guid.NewGuid().ToString("N");
        var effectiveSubAgentOptions = forkOptions.SubAgents ?? Config.DefaultSubAgentForkOptions;
        var requestFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            sourceKey.SessionId,
            sourceKey.ThreadId,
            newThread.SessionId,
            newThread.Id,
            sourceForkBoundary.Generation,
            sourceForkBoundary.SequenceNumber,
            effectiveSubAgentOptions.Policy,
            effectiveSubAgentOptions.DescendantPolicy,
            CanonicalizeForkCompaction(forkOptions.Compaction),
            CanonicalizeForkMetadata(forkOptions.Metadata)))));
        var forkOperationStore = new JournalThreadForkOperationStore(store, sourceKey);
        var forkOperation = await forkOperationStore.GetThreadForkOperationAsync(
            forkOperationId, cancellationToken).ConfigureAwait(false);
        if (forkOperation is null)
        {
            forkOperation = new ThreadForkOperationRecord
            {
                OperationId = forkOperationId,
                Source = sourceKey,
                Target = new ThreadKey(newThread.SessionId, newThread.Id),
                SourceBoundary = sourceForkBoundary,
                RequestFingerprint = requestFingerprint,
                SubAgentPolicy = effectiveSubAgentOptions.Policy,
                Status = ThreadForkOperationStatus.Prepared,
                Revision = 1,
                PreparedChildren = [],
                ChildOutcomes = []
            };
            await forkOperationStore.WriteThreadForkOperationAsync(
                forkOperation,
                new ThreadForkOperationWriteCondition(0),
                cancellationToken).ConfigureAwait(false);
        }
        else if (!string.Equals(forkOperation.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("thread_fork_operation_payload_conflict");
        }
        else if (forkOperation.Status == ThreadForkOperationStatus.Aborted)
        {
            throw new InvalidOperationException($"thread_fork_operation_aborted:{forkOperation.Error}");
        }
        else if (forkOperation.Status is ThreadForkOperationStatus.Committed or ThreadForkOperationStatus.ReconciliationRequired)
        {
            var existing = await store.ProjectThreadAsync(
                forkOperation.Target.SessionId,
                forkOperation.Target.ThreadId,
                ThreadProjectionPurpose.ForkConstruction,
                cancellationToken).ConfigureAwait(false);
            return existing ?? throw new InvalidOperationException("thread_fork_committed_target_missing");
        }
        else if ((forkOperation.Status is ThreadForkOperationStatus.ParentPreparing or ThreadForkOperationStatus.ReadyToCommit) &&
                 await store.GetThreadEventHeadAsync(forkOperation.Target, cancellationToken).ConfigureAwait(false) is not null)
        {
            await ValidateForkTargetOwnershipAsync(
                store, forkOperation.Target, forkOperation, cancellationToken).ConfigureAwait(false);
            forkOperation = forkOperation with
            {
                Status = ThreadForkOperationStatus.Committed,
                Revision = forkOperation.Revision + 1
            };
            await forkOperationStore.WriteThreadForkOperationAsync(
                forkOperation,
                new ThreadForkOperationWriteCondition(forkOperation.Revision - 1),
                cancellationToken).ConfigureAwait(false);
            return await store.ProjectThreadAsync(
                forkOperation.Target.SessionId,
                forkOperation.Target.ThreadId,
                ThreadProjectionPurpose.ForkConstruction,
                cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("thread_fork_committed_target_missing");
        }
        newThread.Metadata["forkOperationId"] = forkOperationId;
        newThread.Metadata["forkSourceSessionId"] = sourceKey.SessionId;
        newThread.Metadata["forkSourceThreadId"] = sourceKey.ThreadId;
        newThread.Metadata["forkRequestFingerprint"] = requestFingerprint;

        // Copy thread-scoped middleware state (session-scoped state is shared via Session object)
        foreach (var kvp in sourceThread.MiddlewareState)
        {
            newThread.MiddlewareState[kvp.Key] = kvp.Value;
        }

        try
        {
            if (forkOptions.Metadata != null)
            {
                var extensionMetadata = new Dictionary<string, object>(forkOptions.Metadata, StringComparer.Ordinal);
                newThread.ApplyRuntimeMetadata(extensionMetadata);
                foreach (var kvp in extensionMetadata)
                    newThread.Metadata[kvp.Key] = kvp.Value;
            }
        }
        catch (Exception exception)
        {
            await AbortForkOperationAsync(forkOperationStore, forkOperation, exception).ConfigureAwait(false);
            throw;
        }

        IReadOnlyList<AgentEvent>? targetJournalEvents = null;
        try
        {
            if (!_middlewarePipeline.IsEmpty)
            {
                var forkEventCoordinator = GetActiveEventCoordinator();
                var sessionState = MiddlewareState.LoadFromSession(sourceThread.Session, _stateFactories);
                var threadState = MiddlewareState.LoadFromThread(newThread, _stateFactories);
                var persistentState = sessionState.Merge(threadState);
                var forkState = AgentLoopState.Initial(
                    newThread.Messages,
                    runId: Guid.NewGuid().ToString("N"),
                    conversationId: sourceThread.SessionId,
                    agentName: _name,
                    persistentState: persistentState);

                var forkContext = new Middleware.AgentContext(
                    agentName: _name,
                    conversationId: sourceThread.SessionId,
                    initialState: forkState,
                    eventCoordinator: forkEventCoordinator,
                    threadEvents: Config?.SessionStore is { } forkStore
                        ? CreateEventPublisher(forkStore, forkEventCoordinator)
                        : null,
                    session: sourceThread.Session,
                    thread: newThread,
                    cancellationToken: cancellationToken,
                    effectiveChatClient: _defaultChatClientHandle,
                    chatClientResolver: _chatClientResolver,
                    services: _serviceProvider,
                    runtimeCapabilities: _runtimeContext?.RuntimeCapabilities,
                    agentId: AgentId,
                    parentAgentMetadata: AgentMetadata,
                    parentAgentStore: Config?.AgentStore,
                    config: Config,
                    clientSet: _clientSet,
                    contentStore: _contentStore,
                    structEvents: GetActiveStructEvents());

                var beforeForkCommitContext = forkContext.AsBeforeThreadForkCommit(
                    sourceThread,
                    newThread,
                    fromMessageIndex,
                    string.IsNullOrWhiteSpace(fromMessageId) ? null : fromMessageId,
                    forkOptions);

                await _middlewarePipeline.ExecuteBeforeThreadForkCommitAsync(
                    beforeForkCommitContext,
                    cancellationToken).ConfigureAwait(false);

                targetJournalEvents = beforeForkCommitContext.HistoricalEvents;

                forkContext.State.MiddlewareState.SaveToThread(newThread, _stateFactories);
                if (sourceThread.Session != null)
                    forkContext.State.MiddlewareState.SaveToSession(sourceThread.Session, _stateFactories);
            }
        }
        catch (Exception exception)
        {
            await AbortForkOperationAsync(forkOperationStore, forkOperation, exception).ConfigureAwait(false);
            throw;
        }

        var targetKey = new ThreadKey(newThread.SessionId, newThread.Id);
        IReadOnlyList<AgentEvent> registryEvents;
        try
        {
            if (forkOperation.Status == ThreadForkOperationStatus.Prepared)
            {
                forkOperation = forkOperation with
                {
                    Status = ThreadForkOperationStatus.ChildrenPreparing,
                    Revision = forkOperation.Revision + 1
                };
                await forkOperationStore.WriteThreadForkOperationAsync(
                    forkOperation,
                    new ThreadForkOperationWriteCondition(forkOperation.Revision - 1),
                    cancellationToken).ConfigureAwait(false);
            }
            registryEvents = await PlanSubAgentRegistryForForkAsync(
                store,
                new ThreadKey(sourceThread.SessionId, sourceThread.Id),
                targetKey,
                sourceForkBoundary,
                forkOperationId,
                requestFingerprint,
                forkOptions.SubAgents ?? Config.DefaultSubAgentForkOptions,
                forkOperationStore,
                cancellationToken).ConfigureAwait(false);
            forkOperation = await forkOperationStore.GetThreadForkOperationAsync(
                forkOperationId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("thread_fork_operation_missing");
            if (forkOperation.Status == ThreadForkOperationStatus.Aborted)
                throw new InvalidOperationException($"thread_fork_operation_aborted:{forkOperation.Error}");
            if (forkOperation.Status is ThreadForkOperationStatus.Committed or ThreadForkOperationStatus.ReconciliationRequired)
            {
                await ValidateForkTargetOwnershipAsync(
                    store, forkOperation.Target, forkOperation, cancellationToken).ConfigureAwait(false);
                return await store.ProjectThreadAsync(
                    forkOperation.Target.SessionId,
                    forkOperation.Target.ThreadId,
                    ThreadProjectionPurpose.ForkConstruction,
                    cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("thread_fork_committed_target_missing");
            }
        }
        catch (Exception exception)
        {
            await AbortForkOperationAsync(forkOperationStore, forkOperation, exception).ConfigureAwait(false);
            throw;
        }
        List<AgentEvent> plannedTargetEvents;
        try
        {
        var projectedEntries = registryEvents.OfType<SubAgentRegistrySeedEvent>()
            .SelectMany(static evt => evt.Entries)
            .ToArray();
        var preparedChildren = forkOperation.PreparedChildren.Concat(projectedEntries
            .OfType<SubAgentAvailableChild>()
            .Select(static entry => entry.Child.ChildThread))
            .Distinct()
            .ToArray();
        var sourceRegistryForOutcomes = await new SubAgentChildRegistry(store)
            .ProjectAsync(sourceKey, sourceForkBoundary, cancellationToken).ConfigureAwait(false);
        var outcomes = new List<SubAgentForkChildOutcome>();
        foreach (var entry in projectedEntries)
        {
            var projectedChild = (entry as SubAgentAvailableChild)?.Child;
            string? childSeed = null;
            ThreadJournalCursor? childBoundary = null;
            if (effectiveSubAgentOptions.Policy == SubAgentForkPolicy.ForkDirectChildren &&
                projectedChild is { } executable)
            {
                var admittedChild = forkOperation.ChildOutcomes.FirstOrDefault(outcome =>
                    outcome.OwningParent == sourceKey &&
                    outcome.Target == executable.ChildThread &&
                    string.Equals(outcome.LocalId, executable.LocalId.Value, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException("thread_fork_prepared_child_missing");
                childSeed = admittedChild.TargetSeedFingerprint
                    ?? throw new InvalidOperationException("thread_fork_prepared_child_seed_missing");
                childBoundary = admittedChild.SourceBoundary
                    ?? throw new InvalidOperationException("thread_fork_prepared_child_boundary_missing");
            }
            outcomes.Add(new SubAgentForkChildOutcome(
                entry.LocalId.Value,
                effectiveSubAgentOptions.Policy,
                sourceRegistryForOutcomes.Entries.GetValueOrDefault(entry.LocalId) is SubAgentAvailableChild sourceAvailable
                    ? sourceAvailable.Child.ChildThread
                    : null,
                projectedChild?.ChildThread,
                entry.Availability,
                childSeed,
                childBoundary,
                sourceKey,
                targetKey));
        }
        foreach (var admittedOutcome in forkOperation.ChildOutcomes.Where(outcome =>
                     sourceRegistryForOutcomes.Entries.Values.Any(entry =>
                         outcome.OwningParent == sourceKey &&
                         string.Equals(entry.LocalId.Value, outcome.LocalId, StringComparison.Ordinal))))
        {
            var plannedOutcome = outcomes.FirstOrDefault(outcome =>
                outcome.OwningParent == admittedOutcome.OwningParent &&
                string.Equals(outcome.LocalId, admittedOutcome.LocalId, StringComparison.Ordinal));
            if (plannedOutcome is null || plannedOutcome != admittedOutcome)
                throw new InvalidOperationException("thread_fork_child_seed_changed");
        }
        outcomes.InsertRange(0, forkOperation.ChildOutcomes.Where(admitted =>
            outcomes.All(planned => planned.OwningParent != admitted.OwningParent ||
                !string.Equals(planned.LocalId, admitted.LocalId, StringComparison.Ordinal))));
        newThread.Preparation = new ThreadPreparationDescriptor(
            forkOperationId, sourceKey, requestFingerprint);
        if (targetJournalEvents is not null)
        {
            if (targetJournalEvents.Any(IsThreadStructuralEvent))
                throw new InvalidOperationException("thread_fork_middleware_structural_event_forbidden");
            plannedTargetEvents = [ThreadEventFactory.ThreadCreated(newThread)];
            plannedTargetEvents.AddRange(targetJournalEvents.Select(evt =>
                CloneEventForThread(evt, newThread.SessionId, newThread.Id)));
            if (newThread.MiddlewareState.Count > 0)
            {
                plannedTargetEvents.Add(ThreadEventFactory.ThreadMiddlewareStateCommitted(
                    newThread.SessionId,
                    newThread.Id,
                    newThread.MiddlewareState));
            }
        }
        else if (copyThroughMessageIndex is int copiedIndex)
        {
            plannedTargetEvents = (await BuildForkedThreadEventsFromSourceAsync(
                store, sourceThread, newThread, copiedIndex, cancellationToken).ConfigureAwait(false)).ToList();
        }
        else
        {
            plannedTargetEvents = [ThreadEventFactory.ThreadCreated(newThread)];
            if (newThread.MiddlewareState.Count > 0)
            {
                plannedTargetEvents.Add(ThreadEventFactory.ThreadMiddlewareStateCommitted(
                    newThread.SessionId, newThread.Id, newThread.MiddlewareState));
            }
        }
        plannedTargetEvents.AddRange(registryEvents);
        var targetSeedFingerprint = ComputeTargetSeedFingerprint(store.EventCodec, plannedTargetEvents);
        if (forkOperation.TargetSeedFingerprint is { } admittedTargetSeed &&
            !string.Equals(admittedTargetSeed, targetSeedFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("thread_fork_target_seed_changed");
        if (forkOperation.Status == ThreadForkOperationStatus.ChildrenPreparing)
        {
            forkOperation = forkOperation with
            {
                Status = ThreadForkOperationStatus.ParentPreparing,
                Revision = forkOperation.Revision + 1,
                PreparedChildren = preparedChildren,
                ChildOutcomes = outcomes.ToArray(),
                TargetSeedFingerprint = targetSeedFingerprint
            };
            await forkOperationStore.WriteThreadForkOperationAsync(
                forkOperation,
                new ThreadForkOperationWriteCondition(forkOperation.Revision - 1),
                cancellationToken).ConfigureAwait(false);
        }
        if (forkOperation.Status == ThreadForkOperationStatus.ParentPreparing)
        {
            forkOperation = forkOperation with
            {
                Status = ThreadForkOperationStatus.ReadyToCommit,
                Revision = forkOperation.Revision + 1
            };
            await forkOperationStore.WriteThreadForkOperationAsync(
                forkOperation,
                new ThreadForkOperationWriteCondition(forkOperation.Revision - 1),
                cancellationToken).ConfigureAwait(false);
        }
        newThread.Preparation = new ThreadPreparationDescriptor(
            forkOperationId, sourceKey, requestFingerprint, forkOperation.TargetSeedFingerprint);
        plannedTargetEvents[0] = ThreadEventFactory.ThreadCreated(newThread);
        }
        catch (Exception exception)
        {
            await AbortForkOperationAsync(forkOperationStore, forkOperation, exception).ConfigureAwait(false);
            throw;
        }

        try
        {
            await store.AppendThreadEventsAsync(
                targetKey,
                plannedTargetEvents,
                new ThreadAppendCondition(ThreadJournalCursor.Start(1)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            try
            {
                await ValidateForkTargetOwnershipAsync(
                    store, targetKey, forkOperation, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                await AbortForkOperationAsync(forkOperationStore, forkOperation, exception).ConfigureAwait(false);
                throw;
            }
        }

        forkOperation = forkOperation with
        {
            Status = ThreadForkOperationStatus.Committed,
            Revision = forkOperation.Revision + 1
        };
        await forkOperationStore.WriteThreadForkOperationAsync(
            forkOperation,
            new ThreadForkOperationWriteCondition(forkOperation.Revision - 1),
            CancellationToken.None).ConfigureAwait(false);

        // Update the direct lineage edge. Fork groups are projected from session graph state.
        try
        {
            if (!sourceThread.ChildThreads.Contains(newThread.Id))
            {
                sourceThread.ChildThreads.Add(newThread.Id);
                sourceThread.LastActivity = now;
                await store.AppendThreadUpdatedAsync(sourceThread, cancellationToken);
            }
            if (sourceThread.Session != null)
            {
                sourceThread.Session.LastActivity = now;
                await store.SaveSessionAsync(sourceThread.Session, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            forkOperation = forkOperation with
            {
                Status = ThreadForkOperationStatus.ReconciliationRequired,
                Revision = forkOperation.Revision + 1,
                Error = exception.Message
            };
            await forkOperationStore.WriteThreadForkOperationAsync(
                forkOperation,
                new ThreadForkOperationWriteCondition(forkOperation.Revision - 1),
                CancellationToken.None).ConfigureAwait(false);
        }

        return newThread;
    }

    /// <summary>
    /// Fork a thread from its latest message (string-based API).
    /// Creates a new thread with the full current source thread history, plus thread-scoped middleware state.
    /// Returns the authoritative durable fork result.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="sourceThreadId">Source thread to fork from</param>
    /// <param name="newThreadId">New thread identifier</param>
    /// <param name="metadata">Optional metadata to attach to the new thread.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The durable operation identity, boundary, target, status, and child outcomes.</returns>
    public async Task<ThreadForkResult> ForkThreadAsync(
        string sessionId,
        string sourceThreadId,
        string newThreadId,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
        => await ForkThreadAsync(
            sessionId,
            sourceThreadId,
            newThreadId,
            ThreadForkOptions.FromMetadata(metadata),
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Fork a thread from its latest message (string-based API).
    /// Creates a new thread with the full current source thread history, plus thread-scoped middleware state.
    /// Returns the authoritative durable fork result.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="sourceThreadId">Source thread to fork from</param>
    /// <param name="newThreadId">New thread identifier</param>
    /// <param name="forkOptions">Options for the fork operation.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The durable operation identity, boundary, target, status, and child outcomes.</returns>
    public async Task<ThreadForkResult> ForkThreadAsync(
        string sessionId,
        string sourceThreadId,
        string newThreadId,
        ThreadForkOptions forkOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(forkOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceThreadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newThreadId);

        var store = Config.SessionStore
            ?? throw new InvalidOperationException(
                "No session store configured. Use WithSessionStore() on AgentBuilder to configure persistence.");

        var sourceThread = await store.ProjectThreadAsync(
                sessionId,
                sourceThreadId,
                ThreadProjectionPurpose.ForkConstruction,
                cancellationToken)
            ?? throw new InvalidOperationException($"Thread '{sourceThreadId}' not found in session '{sessionId}'.");

        var latestMessageId = sourceThread.Messages.LastOrDefault()?.MessageId;
        if (string.IsNullOrWhiteSpace(latestMessageId))
        {
            throw new InvalidOperationException(
                $"Thread '{sourceThreadId}' in session '{sessionId}' has no messages to fork from.");
        }

        return await ForkThreadAsync(
            sessionId,
            sourceThreadId,
            newThreadId,
            latestMessageId,
            forkOptions,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fork a thread at a specific message id (string-based API).
    /// Creates a new thread with messages up to the fork point, plus thread-scoped middleware state.
    /// Returns the authoritative durable fork result.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="sourceThreadId">Source thread to fork from</param>
    /// <param name="newThreadId">New thread identifier</param>
    /// <param name="fromMessageId">Message id to fork at (inclusive)</param>
    /// <param name="metadata">Optional metadata to attach to the new thread.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The durable operation identity, boundary, target, status, and child outcomes.</returns>
    public async Task<ThreadForkResult> ForkThreadAsync(
        string sessionId,
        string sourceThreadId,
        string newThreadId,
        string? fromMessageId,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
        => await ForkThreadAsync(
            sessionId,
            sourceThreadId,
            newThreadId,
            fromMessageId,
            ThreadForkOptions.FromMetadata(metadata),
            cancellationToken).ConfigureAwait(false);

    public async Task<ThreadForkResult> ForkThreadAsync(
        string sessionId,
        string sourceThreadId,
        string newThreadId,
        string? fromMessageId,
        ThreadForkOptions forkOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(forkOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceThreadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newThreadId);

        var store = Config.SessionStore
            ?? throw new InvalidOperationException(
                "No session store configured. Use WithSessionStore() on AgentBuilder to configure persistence.");

        // Load session and source thread
        var session = await store.LoadSessionAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Session '{sessionId}' not found.");
        session.Store = store;

        var sourceThread = await store.ProjectThreadAsync(
                sessionId,
                sourceThreadId,
                ThreadProjectionPurpose.ForkConstruction,
                cancellationToken)
            ?? throw new InvalidOperationException($"Thread '{sourceThreadId}' not found in session '{sessionId}'.");
        sourceThread.Session = session;

        // Fork using the object-based method
        var sourceDescriptor = await store.GetThreadAsync(
            new ThreadKey(sessionId, sourceThreadId), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Thread '{sourceThreadId}' not found in session '{sessionId}'.");
        var boundary = ThreadJournalCursor.Start(sourceDescriptor.Generation);
        if (!string.IsNullOrWhiteSpace(fromMessageId))
        {
            var index = ResolveForkMessageIndex(sourceThread, fromMessageId);
            var through = ExpandForkCopyThroughIndex(sourceThread.Messages, index);
            var messages = sourceThread.Messages.Take(through + 1).ToArray();
            var messageIds = messages.Select(static message => message.MessageId)
                .Where(static id => !string.IsNullOrWhiteSpace(id)).Select(static id => id!)
                .ToHashSet(StringComparer.Ordinal);
            var turnIds = messages.Select(GetMessageTurnId)
                .Where(static id => !string.IsNullOrWhiteSpace(id)).Select(static id => id!)
                .ToHashSet(StringComparer.Ordinal);
            var sequence = await ResolveForkCopyThroughSequenceAsync(
                store,
                new ThreadKey(sessionId, sourceThreadId),
                messageIds,
                turnIds,
                cancellationToken).ConfigureAwait(false) ?? 0;
            boundary = new ThreadJournalCursor(sourceDescriptor.Generation, sequence);
        }
        var operationId = forkOptions.OperationId ?? Guid.NewGuid().ToString("N");
        forkOptions = forkOptions with { OperationId = operationId };
        var newThread = await ForkThreadAsync(sourceThread, newThreadId, fromMessageId, forkOptions, cancellationToken)
            .ConfigureAwait(false);
        var operation = await new JournalThreadForkOperationStore(
                store, new ThreadKey(sessionId, sourceThreadId))
            .GetThreadForkOperationAsync(operationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("thread_fork_operation_missing_after_commit");
        return new ThreadForkResult
        {
            OperationId = operation.OperationId,
            Source = operation.Source,
            Target = operation.Target,
            SourceBoundary = operation.SourceBoundary,
            SubAgentPolicy = operation.SubAgentPolicy,
            Status = operation.Status,
            Children = operation.ChildOutcomes
        };
    }

    private static int ResolveForkMessageIndex(
        Thread sourceThread,
        string fromMessageId)
    {
        var index = sourceThread.Messages.FindIndex(message =>
            string.Equals(message.MessageId, fromMessageId, StringComparison.Ordinal));

        if (index >= 0)
            return index;

        throw new MessageNotPresentOnThreadException(
            sourceThread.SessionId,
            sourceThread.Id,
            fromMessageId,
            []);
    }

    private static ChatMessage CloneMessageForThread(ChatMessage message)
    {
        var clone = new ChatMessage(message.Role, message.Contents.ToArray())
        {
            MessageId = message.MessageId,
            AuthorName = message.AuthorName,
            CreatedAt = message.CreatedAt,
            RawRepresentation = message.RawRepresentation,
            AdditionalProperties = message.AdditionalProperties is null
                ? null
                : new AdditionalPropertiesDictionary(message.AdditionalProperties)
        };

        return clone;
    }

    private static async Task<IReadOnlyList<AgentEvent>> BuildForkedThreadEventsFromSourceAsync(
        ISessionStore store,
        Thread sourceThread,
        Thread newThread,
        int copyThroughMessageIndex,
        CancellationToken cancellationToken)
    {
        var sourceKey = new ThreadKey(sourceThread.SessionId, sourceThread.Id);
        if (await store.GetThreadAsync(sourceKey, cancellationToken).ConfigureAwait(false) is null)
            throw new InvalidOperationException(
                $"Cannot fork thread '{sourceThread.Id}' because its journal is missing.");

        var copiedMessages = sourceThread.Messages.Take(copyThroughMessageIndex + 1).ToList();
        var copiedMessageIds = copiedMessages
            .Select(message => message.MessageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);

        var copiedTurnIds = copiedMessages
            .Select(GetMessageTurnId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);

        var copyThroughSequence = await ResolveForkCopyThroughSequenceAsync(
            store,
            sourceKey,
            copiedMessageIds,
            copiedTurnIds,
            cancellationToken).ConfigureAwait(false);

        var result = new List<AgentEvent> { ThreadEventFactory.ThreadCreated(newThread) };

        if (copyThroughSequence is long sequenceNumber)
        {
            var sourceDescriptor = await store.GetThreadAsync(sourceKey, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Source thread '{sourceKey.ThreadId}' no longer exists.");
            await foreach (var batch in store.ReadThreadEventsAsync(
                sourceKey,
                new ThreadEventReadRequest(
                    ThreadJournalCursor.Start(sourceDescriptor.Generation),
                    Through: sequenceNumber),
                cancellationToken).ConfigureAwait(false))
            {
                var copiedEvents = batch.Events
                    .Where(evt => !IsThreadStructuralEvent(evt))
                    .Select(evt => CloneEventForThread(evt, newThread.SessionId, newThread.Id))
                    .ToArray();
                if (copiedEvents.Length > 0) result.AddRange(copiedEvents);
            }
        }

        var derivedEvents = new List<AgentEvent>();

        if (newThread.MiddlewareState.Count > 0)
        {
            derivedEvents.Add(ThreadEventFactory.ThreadMiddlewareStateCommitted(
                newThread.SessionId,
                newThread.Id,
                newThread.MiddlewareState));
        }

        result.AddRange(derivedEvents);
        return result;
    }

    private static AgentEvent CloneEventForThread(AgentEvent evt, string sessionId, string threadId) =>
        evt with
        {
            EventId = Guid.NewGuid().ToString("N"),
            SessionId = sessionId,
            ThreadId = threadId,
            ThreadSequenceNumber = 0
        };

    private static async ValueTask<long?> ResolveForkCopyThroughSequenceAsync(
        ISessionStore store,
        ThreadKey sourceThread,
        IReadOnlySet<string> copiedMessageIds,
        IReadOnlySet<string> copiedTurnIds,
        CancellationToken cancellationToken)
    {
        long? copyThroughSequence = null;
        var activeThreadExecutionIds = new HashSet<string>(StringComparer.Ordinal);
        var activeAtBoundary = new HashSet<string>(StringComparer.Ordinal);
        var completionPositions = new Dictionary<string, long>(StringComparer.Ordinal);
        var descriptor = await store.GetThreadAsync(sourceThread, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Source thread '{sourceThread.ThreadId}' no longer exists.");

        await foreach (var batch in store.ReadThreadEventsAsync(
            sourceThread,
            new ThreadEventReadRequest(ThreadJournalCursor.Start(descriptor.Generation)),
            cancellationToken).ConfigureAwait(false))
        {
            foreach (var evt in batch.Events)
            {
                if (evt is ThreadExecutionStartedEvent started)
                    activeThreadExecutionIds.Add(started.ThreadExecutionId);
                else if (evt is ThreadExecutionFinishedEvent completed)
                {
                    activeThreadExecutionIds.Remove(completed.ThreadExecutionId);
                    completionPositions[completed.ThreadExecutionId] = evt.ThreadSequenceNumber;
                }

                if (EventDefinesForkCopyBoundary(evt, copiedMessageIds, copiedTurnIds))
                {
                    copyThroughSequence = evt.ThreadSequenceNumber;
                    activeAtBoundary = new HashSet<string>(activeThreadExecutionIds, StringComparer.Ordinal);
                }
            }
        }

        if (copyThroughSequence is not long sequenceNumber)
            return null;

        foreach (var threadExecutionId in activeAtBoundary)
        {
            if (completionPositions.TryGetValue(threadExecutionId, out var completionPosition) &&
                completionPosition > sequenceNumber)
            {
                sequenceNumber = completionPosition;
            }
        }

        return sequenceNumber;
    }

    private static bool EventDefinesForkCopyBoundary(
        AgentEvent evt,
        IReadOnlySet<string> copiedMessageIds,
        IReadOnlySet<string> copiedTurnIds)
    {
        if (IsThreadStructuralEvent(evt))
            return false;

        if (!string.IsNullOrWhiteSpace(evt.EventFlowId) && copiedTurnIds.Contains(evt.EventFlowId))
            return true;

        return evt switch
        {
            MessageTurnStartedEvent data => copiedTurnIds.Contains(data.MessageTurnId),
            MessageTurnFinishedEvent data => copiedTurnIds.Contains(data.MessageTurnId),
            MessageTurnErrorEvent data => !string.IsNullOrWhiteSpace(data.MessageTurnId) &&
                                          copiedTurnIds.Contains(data.MessageTurnId),
            ContentAddedEvent data => copiedMessageIds.Contains(data.MessageId),
            TextMessageStartEvent data => copiedMessageIds.Contains(data.MessageId),
            TextDeltaEvent data => copiedMessageIds.Contains(data.MessageId),
            TextMessageEndEvent data => copiedMessageIds.Contains(data.MessageId),
            ThreadMessageReplacedEvent data => copiedMessageIds.Contains(data.MessageId),
            UserMessageEvent data => copiedMessageIds.Contains(data.MessageId),
            ReasoningMessageStartEvent data => copiedMessageIds.Contains(data.MessageId),
            ReasoningDeltaEvent data => copiedMessageIds.Contains(data.MessageId),
            ReasoningMessageEndEvent data => copiedMessageIds.Contains(data.MessageId),
            ToolCallStartEvent data => copiedMessageIds.Contains(data.MessageId),
            ToolCallResultEvent data => !string.IsNullOrWhiteSpace(data.MessageId) &&
                                        copiedMessageIds.Contains(data.MessageId),
            _ => false
        };
    }

    private static bool IsThreadStructuralEvent(AgentEvent evt) =>
        evt is ThreadCreatedEvent or ThreadUpdatedEvent or ThreadMiddlewareStateCommittedEvent or
            SubAgentChildRegisteredEvent or SubAgentChildDetachedEvent or SubAgentChildRemappedEvent or
            SubAgentChildUnavailableEvent or SubAgentRegistrySeedEvent or
            SubAgentControllerGrantedEvent or SubAgentCreationReservedEvent or SubAgentCreationAdvancedEvent or
            SubAgentChildControllerAuthorityEvent or
            ThreadForkOperationChangedEvent;

    private static string CanonicalizeForkCompaction(ThreadForkCompaction compaction) => compaction switch
    {
        InheritThreadForkCompaction => "inherit",
        DisableThreadForkCompaction => "disabled",
        ApplyThreadForkCompaction apply => "enabled:" + CanonicalizeJson(
            JsonSerializer.SerializeToElement(apply.Compaction, SessionJsonContext.Combined.CompactionSpecification)),
        _ => throw new InvalidOperationException("thread_fork_compaction_invalid")
    };

    private static string CanonicalizeForkMetadata(IReadOnlyDictionary<string, object>? metadata)
    {
        if (metadata is null || metadata.Count == 0) return "{}";
        return "{" + string.Join(',', metadata.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => QuoteJson(pair.Key) + ":" + CanonicalizeMetadataValue(pair.Value))) + "}";
    }

    private static string CanonicalizeMetadataValue(object? value) => value switch
    {
        null => "null",
        JsonElement element => CanonicalizeJson(element),
        string text => QuoteJson(text),
        bool flag => flag ? "true" : "false",
        byte number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        sbyte number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        short number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ushort number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        int number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        uint number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        long number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ulong number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        float number => number.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        double number => number.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        decimal number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        char character => QuoteJson(character.ToString()),
        Guid guid => QuoteJson(guid.ToString("D")),
        DateTime dateTime => QuoteJson(dateTime.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
        DateTimeOffset dateTimeOffset => QuoteJson(dateTimeOffset.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
        Enum enumValue => QuoteJson(enumValue.ToString()),
        IReadOnlyDictionary<string, object> dictionary => CanonicalizeForkMetadata(dictionary),
        IDictionary<string, object> dictionary => CanonicalizeForkMetadata(
            new Dictionary<string, object>(dictionary, StringComparer.Ordinal)),
        System.Collections.IDictionary dictionary => CanonicalizeUntypedMetadataDictionary(dictionary),
        System.Collections.IEnumerable sequence => CanonicalizeUntypedMetadataSequence(sequence),
        _ => throw new InvalidOperationException($"thread_fork_metadata_type_unsupported:{value.GetType().FullName}")
    };

    private static string CanonicalizeUntypedMetadataDictionary(System.Collections.IDictionary dictionary)
    {
        var entries = new List<KeyValuePair<string, object?>>();
        foreach (System.Collections.DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not string key)
                throw new InvalidOperationException("thread_fork_metadata_dictionary_key_invalid");
            entries.Add(new KeyValuePair<string, object?>(key, entry.Value));
        }
        return "{" + string.Join(',', entries.OrderBy(static entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => QuoteJson(entry.Key) + ":" + CanonicalizeMetadataValue(entry.Value))) + "}";
    }

    private static string CanonicalizeUntypedMetadataSequence(System.Collections.IEnumerable sequence)
    {
        var values = new List<string>();
        foreach (var item in sequence)
            values.Add(CanonicalizeMetadataValue(item));
        return "[" + string.Join(',', values) + "]";
    }

    private static string QuoteJson(string value) => $"\"{JsonEncodedText.Encode(value)}\"";

    private static string CanonicalizeJson(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(',', value.EnumerateObject()
            .OrderBy(static property => property.Name, StringComparer.Ordinal)
            .Select(property => QuoteJson(property.Name) + ":" + CanonicalizeJson(property.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(',', value.EnumerateArray().Select(CanonicalizeJson)) + "]",
        _ => value.GetRawText()
    };

    private static string ComputeTargetSeedFingerprint(
        Serialization.AgentEventCodec codec,
        IReadOnlyList<AgentEvent> events)
    {
        var canonical = events.Select(evt =>
        {
            AgentEvent normalized = evt with
            {
                EventId = string.Empty,
                ThreadSequenceNumber = 0,
                Timestamp = DateTimeOffset.UnixEpoch,
                ExchangeTimestampNs = 0
            };
            if (normalized is ThreadCreatedEvent { Preparation: { } preparation } created)
                normalized = created with
                {
                    EventId = string.Empty,
                    ThreadSequenceNumber = 0,
                    Timestamp = DateTimeOffset.UnixEpoch,
                    ExchangeTimestampNs = 0,
                    CreatedAt = DateTime.UnixEpoch,
                    Preparation = preparation with
                    {
                        TargetSeedFingerprint = null,
                        SourceBoundary = null
                    }
                };
            return codec.Serialize(normalized);
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', canonical))));
    }

    private static async ValueTask ValidateForkTargetOwnershipAsync(
        ISessionStore store,
        ThreadKey target,
        ThreadForkOperationRecord operation,
        CancellationToken cancellationToken)
    {
        var head = await store.GetThreadEventHeadAsync(target, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("thread_fork_target_missing");
        var staged = new List<AgentEvent>();
        await foreach (var batch in store.ReadThreadEventsAsync(
            target,
            new ThreadEventReadRequest(ThreadJournalCursor.Start(head.Generation), head.ThreadSequenceNumber),
            cancellationToken).ConfigureAwait(false))
        {
            staged.AddRange(batch.Events);
        }
        var created = staged.OfType<ThreadCreatedEvent>().FirstOrDefault();
        if (created?.Preparation is not { } preparation ||
            !string.Equals(preparation.OperationId, operation.OperationId, StringComparison.Ordinal) ||
            preparation.Source != operation.Source ||
            !string.Equals(preparation.RequestFingerprint, operation.RequestFingerprint, StringComparison.Ordinal) ||
            !string.Equals(preparation.TargetSeedFingerprint, operation.TargetSeedFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("thread_fork_target_collision");
        if (!string.Equals(
                ComputeTargetSeedFingerprint(store.EventCodec, staged),
                operation.TargetSeedFingerprint,
                StringComparison.Ordinal))
            throw new InvalidOperationException("thread_fork_target_seed_mismatch");
    }

    private static async ValueTask AbortForkOperationAsync(
        IThreadForkOperationStore store,
        ThreadForkOperationRecord operation,
        Exception exception)
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var latest = await store.GetThreadForkOperationAsync(operation.OperationId, CancellationToken.None)
                .ConfigureAwait(false) ?? operation;
            if (latest.Status is ThreadForkOperationStatus.Committed or
                ThreadForkOperationStatus.ReconciliationRequired or
                ThreadForkOperationStatus.Aborted)
                return;
            var aborted = latest with
            {
                Status = ThreadForkOperationStatus.Aborted,
                Revision = latest.Revision + 1,
                Error = exception.Message
            };
            try
            {
                await store.WriteThreadForkOperationAsync(
                    aborted,
                    new ThreadForkOperationWriteCondition(latest.Revision),
                    CancellationToken.None).ConfigureAwait(false);
                return;
            }
            catch (InvalidOperationException conflict) when (
                conflict.Message == "thread_fork_operation_conflict" && attempt < 15) { }
        }
        throw new InvalidOperationException("thread_fork_abort_conflict", exception);
    }

    private static async ValueTask<IReadOnlyList<AgentEvent>> PlanSubAgentRegistryForForkAsync(
        ISessionStore store,
        ThreadKey source,
        ThreadKey target,
        ThreadJournalCursor sourceBoundary,
        string forkOperationId,
        string requestFingerprint,
        SubAgentForkOptions options,
        IThreadForkOperationStore operationStore,
        CancellationToken cancellationToken)
    {
        var sourceRegistry = await new SubAgentChildRegistry(store)
            .ProjectAsync(source, sourceBoundary, cancellationToken).ConfigureAwait(false);
        if (sourceRegistry.Entries.Count == 0) return Array.Empty<AgentEvent>();
        var entries = new List<SubAgentRegistryEntry>();
        var grants = new List<AgentEvent>();
        foreach (var sourceEntry in sourceRegistry.Entries.Values.OrderBy(static entry => entry.LocalId.Value, StringComparer.Ordinal))
        {
            if (sourceEntry is SubAgentChildTombstone existingTombstone)
            {
                entries.Add(existingTombstone with { });
                continue;
            }
            var child = ((SubAgentAvailableChild)sourceEntry).Child;
            SubAgentRegistryEntry projectedEntry;
            SubAgentChildReference? projectedChild = null;
            if (options.Policy == SubAgentForkPolicy.Detach)
            {
                projectedEntry = new SubAgentChildTombstone
                {
                    LocalId = child.LocalId,
                    RoleName = child.RoleName,
                    Availability = SubAgentChildAvailability.Detached,
                    Reason = "This subagent was not carried into the current conversation branch. Start a new role action to continue independently.",
                    CreatedAt = child.CreatedAt,
                    ExecutionPolicyFingerprint = child.ExecutionPolicy.Fingerprint
                };
            }
            else
            {
                projectedChild = options.Policy switch
                {
                    SubAgentForkPolicy.Share => child,
                    SubAgentForkPolicy.ForkDirectChildren => await ForkDirectRuntimeChildAsync(
                        store, child, target, source, forkOperationId, requestFingerprint, options, operationStore, cancellationToken).ConfigureAwait(false),
                    _ => throw new ArgumentOutOfRangeException(nameof(options))
                };
                projectedEntry = new SubAgentAvailableChild { Child = projectedChild };
            }
            entries.Add(projectedEntry);
            var rootOperation = await operationStore.GetThreadForkOperationAsync(forkOperationId, cancellationToken)
                .ConfigureAwait(false) ?? throw new InvalidOperationException("thread_fork_operation_missing");
            if (source != rootOperation.Source && options.Policy is not SubAgentForkPolicy.ForkDirectChildren)
            {
                await EnsureForkChildOutcomeAsync(
                    operationStore,
                    forkOperationId,
                    new SubAgentForkChildOutcome(
                        projectedEntry.LocalId.Value,
                        options.Policy,
                        child.ChildThread,
                        projectedChild?.ChildThread,
                        projectedEntry.Availability,
                        OwningParent: source,
                        Controller: target),
                    cancellationToken).ConfigureAwait(false);
            }
            if (options.Policy == SubAgentForkPolicy.Share && projectedChild is { } shared)
            {
                await SubAgentControllerAuthority.GrantAsync(
                    store,
                    shared.ChildThread,
                    target,
                    shared.LocalId,
                    forkOperationId,
                    rootOperation.Source,
                    cancellationToken).ConfigureAwait(false);
                grants.Add(new SubAgentControllerGrantedEvent(shared.LocalId, shared.ChildThread)
                {
                    SessionId = target.SessionId,
                    ThreadId = target.ThreadId
                });
            }
        }
        var seed = new SubAgentRegistrySeedEvent(
            entries,
            [],
            grants.OfType<SubAgentControllerGrantedEvent>().Select(static grant => grant.LocalId).ToArray())
        {
            SessionId = target.SessionId,
            ThreadId = target.ThreadId
        };
        return [seed, .. grants];
    }

    private static async ValueTask<SubAgentChildReference> ForkDirectRuntimeChildAsync(
        ISessionStore store,
        SubAgentChildReference source,
        ThreadKey targetParent,
        ThreadKey sourceParent,
        string forkOperationId,
        string requestFingerprint,
        SubAgentForkOptions options,
        IThreadForkOperationStore operationStore,
        CancellationToken cancellationToken)
    {
        var sourceKey = source.ChildThread;
        var descriptor = await store.GetThreadAsync(sourceKey, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("subagent_fork_boundary_unavailable");
        var operationSuffix = forkOperationId[..Math.Min(12, forkOperationId.Length)];
        var parentDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{targetParent.SessionId}\u001f{targetParent.ThreadId}")))[..16].ToLowerInvariant();
        var targetSessionId = source.CreationContext == SubAgentCreationContext.Isolated
            ? $"fork/{operationSuffix}/isolated/{parentDigest}/{source.LocalId.Value}"
            : targetParent.SessionId;
        var targetKey = new ThreadKey(
            targetSessionId,
            $"{targetParent.ThreadId}/subagent/{source.LocalId.Value}/{forkOperationId[..Math.Min(12, forkOperationId.Length)]}");
        var targetAlreadyExists = await store.GetThreadEventHeadAsync(targetKey, cancellationToken)
            .ConfigureAwait(false) is not null;
        var admittedOperation = await operationStore.GetThreadForkOperationAsync(forkOperationId, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidOperationException("thread_fork_operation_missing");
        var admittedOutcome = admittedOperation.ChildOutcomes.FirstOrDefault(outcome =>
            outcome.OwningParent == sourceParent &&
            string.Equals(outcome.LocalId, source.LocalId.Value, StringComparison.Ordinal));
        var childMetadata = descriptor.Metadata.Count == 0
            ? new Dictionary<string, object>(StringComparer.Ordinal)
            : new Dictionary<string, object>(descriptor.Metadata, StringComparer.Ordinal);
        childMetadata["forkOperationId"] = forkOperationId;
        childMetadata["forkSourceSessionId"] = admittedOperation.Source.SessionId;
        childMetadata["forkSourceThreadId"] = admittedOperation.Source.ThreadId;
        childMetadata["forkRequestFingerprint"] = requestFingerprint;
        var created = new ThreadCreatedEvent(
            descriptor.DefaultAgent.AgentId,
            descriptor.Name,
            descriptor.Description,
            descriptor.Tags.ToList(),
            childMetadata,
            DateTime.UtcNow,
            ThreadKind.SubAgent,
            ThreadVisibility.Hidden,
            targetParent.SessionId,
            targetParent.ThreadId,
            source.RoleName,
            InvocationId: source.CreationInvocationId,
            ParentToolCallId: source.ParentToolCallId,
            ContextPolicy: source.CreationContext.ToString(),
            ForkedFrom: sourceKey.ThreadId)
        {
            SessionId = targetKey.SessionId,
            ThreadId = targetKey.ThreadId,
            Preparation = new ThreadPreparationDescriptor(
                forkOperationId,
                admittedOperation.Source,
                childMetadata["forkRequestFingerprint"].ToString()!)
        };
        var sourceEvents = new List<AgentEvent>();
        await foreach (var batch in store.ReadThreadEventsAsync(
            sourceKey,
            new ThreadEventReadRequest(
                ThreadJournalCursor.Start(descriptor.Generation),
                admittedOutcome?.SourceBoundary?.SequenceNumber),
            cancellationToken).ConfigureAwait(false))
        {
            sourceEvents.AddRange(batch.Events);
        }
        var active = new HashSet<string>(StringComparer.Ordinal);
        long? latestCompletedBoundary = null;
        foreach (var evt in sourceEvents)
        {
            if (evt is ThreadExecutionStartedEvent started)
                active.Add(started.ThreadExecutionId);
            else if (evt is ThreadExecutionFinishedEvent finished)
            {
                active.Remove(finished.ThreadExecutionId);
                latestCompletedBoundary = evt.ThreadSequenceNumber;
            }
        }
        long? through = admittedOutcome?.SourceBoundary?.SequenceNumber;
        if (admittedOutcome is null && active.Count > 0)
            through = latestCompletedBoundary
                ?? throw new InvalidOperationException("subagent_fork_boundary_unavailable");
        var staged = new List<AgentEvent> { created };
        staged.AddRange(sourceEvents
            .Where(evt => through is null || evt.ThreadSequenceNumber <= through.Value)
            .Where(static evt => !IsThreadStructuralEvent(evt))
            .Select(evt => CloneEventForThread(evt, targetKey.SessionId, targetKey.ThreadId)));
        var childBoundary = new ThreadJournalCursor(
            descriptor.Generation,
            through ?? sourceEvents.LastOrDefault()?.ThreadSequenceNumber ?? 0);
        if (admittedOutcome?.SourceBoundary is { } admittedBoundary && admittedBoundary != childBoundary)
            throw new InvalidOperationException("thread_fork_child_boundary_changed");
        var descendantEvents = await PlanSubAgentRegistryForForkAsync(
            store,
            sourceKey,
            targetKey,
            childBoundary,
            forkOperationId,
            requestFingerprint,
            new SubAgentForkOptions
            {
                Policy = options.DescendantPolicy,
                DescendantPolicy = SubAgentForkPolicy.Detach
            },
            operationStore,
            cancellationToken).ConfigureAwait(false);
        staged.AddRange(descendantEvents);
        var childSeedFingerprint = ComputeTargetSeedFingerprint(store.EventCodec, staged);
        if (admittedOutcome?.TargetSeedFingerprint is { } admittedSeed &&
            !string.Equals(admittedSeed, childSeedFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("thread_fork_child_seed_changed");
        staged[0] = created with
        {
            Preparation = created.Preparation! with
            {
                TargetSeedFingerprint = childSeedFingerprint,
                SourceBoundary = childBoundary
            }
        };
        var childOutcome = new SubAgentForkChildOutcome(
            source.LocalId.Value,
            SubAgentForkPolicy.ForkDirectChildren,
            sourceKey,
            targetKey,
            SubAgentChildAvailability.Available,
            childSeedFingerprint,
            childBoundary,
            sourceParent,
            targetParent);
        await EnsureForkChildOutcomeAsync(
            operationStore, forkOperationId, childOutcome, cancellationToken).ConfigureAwait(false);
        if (targetAlreadyExists)
        {
            await ValidateForkChildTargetAsync(
                store, targetKey, forkOperationId, childBoundary, childSeedFingerprint, CancellationToken.None)
                .ConfigureAwait(false);
            return source with { ChildThread = targetKey };
        }
        if (source.CreationContext == SubAgentCreationContext.Isolated)
        {
            var isolatedSession = new Session(targetSessionId)
            {
                Preparation = new SessionPreparationDescriptor(
                    forkOperationId,
                    admittedOperation.Source,
                    childMetadata["forkRequestFingerprint"].ToString()!,
                    childSeedFingerprint)
            };
            isolatedSession.Metadata["forkOperationId"] = forkOperationId;
            isolatedSession.Metadata["forkSourceSessionId"] = admittedOperation.Source.SessionId;
            isolatedSession.Metadata["forkSourceThreadId"] = admittedOperation.Source.ThreadId;
            isolatedSession.Metadata["forkTargetSeedFingerprint"] = childSeedFingerprint;
            var preparation = await store.TryPrepareSessionAsync(isolatedSession, cancellationToken).ConfigureAwait(false);
            if (preparation == SessionPreparationResult.Conflict)
                throw new InvalidOperationException("session_fork_target_collision");
        }
        try
        {
            await store.AppendThreadEventsAsync(
                targetKey,
                staged,
                new ThreadAppendCondition(ThreadJournalCursor.Start(1)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await ValidateForkChildTargetAsync(
                store, targetKey, forkOperationId, childBoundary, childSeedFingerprint, CancellationToken.None)
                .ConfigureAwait(false);
        }
        return source with { ChildThread = targetKey };
    }

    private static async ValueTask EnsureForkChildOutcomeAsync(
        IThreadForkOperationStore operationStore,
        string forkOperationId,
        SubAgentForkChildOutcome childOutcome,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var operation = await operationStore.GetThreadForkOperationAsync(forkOperationId, cancellationToken)
                .ConfigureAwait(false) ?? throw new InvalidOperationException("thread_fork_operation_missing");
            var admitted = operation.ChildOutcomes.FirstOrDefault(outcome =>
                outcome.OwningParent == childOutcome.OwningParent &&
                string.Equals(outcome.LocalId, childOutcome.LocalId, StringComparison.Ordinal));
            if (admitted is not null)
            {
                if (admitted != childOutcome)
                    throw new InvalidOperationException("thread_fork_child_seed_changed");
                return;
            }
            var advanced = operation with
            {
                Revision = operation.Revision + 1,
                PreparedChildren = childOutcome.Target is { } target && !operation.PreparedChildren.Contains(target)
                    ? operation.PreparedChildren.Append(target).ToArray()
                    : operation.PreparedChildren,
                ChildOutcomes = operation.ChildOutcomes.Append(childOutcome).ToArray()
            };
            try
            {
                await operationStore.WriteThreadForkOperationAsync(
                    advanced,
                    new ThreadForkOperationWriteCondition(operation.Revision),
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (InvalidOperationException conflict) when (
                conflict.Message == "thread_fork_operation_conflict" && attempt < 15) { }
        }
        throw new InvalidOperationException("thread_fork_operation_conflict");
    }

    private static async ValueTask ValidateForkChildTargetAsync(
        ISessionStore store,
        ThreadKey target,
        string forkOperationId,
        ThreadJournalCursor sourceBoundary,
        string targetSeedFingerprint,
        CancellationToken cancellationToken)
    {
        var existingEvents = new List<AgentEvent>();
        await foreach (var batch in store.ReadThreadEventsAsync(
                           target,
                           new ThreadEventReadRequest(ThreadJournalCursor.Start(1)),
                           cancellationToken).ConfigureAwait(false))
            existingEvents.AddRange(batch.Events);
        var existingCreated = existingEvents.OfType<ThreadCreatedEvent>().FirstOrDefault();
        if (existingCreated?.Preparation is not { } preparation ||
            !string.Equals(preparation.OperationId, forkOperationId, StringComparison.Ordinal) ||
            preparation.SourceBoundary != sourceBoundary ||
            !string.Equals(preparation.TargetSeedFingerprint, targetSeedFingerprint, StringComparison.Ordinal) ||
            !string.Equals(
                ComputeTargetSeedFingerprint(store.EventCodec, existingEvents),
                targetSeedFingerprint,
                StringComparison.Ordinal))
            throw new InvalidOperationException("thread_fork_target_collision");
    }

    private static int ExpandForkCopyThroughIndex(IReadOnlyList<ChatMessage> messages, int requestedIndex)
    {
        if (requestedIndex < 0 || requestedIndex >= messages.Count)
            return requestedIndex;

        var copyThroughIndex = requestedIndex;
        var turnIds = new HashSet<string>(StringComparer.Ordinal);
        var toolCallIds = new HashSet<string>(StringComparer.Ordinal);

        AddMessageGroupKeys(messages[requestedIndex], turnIds, toolCallIds);

        var changed = true;
        while (changed)
        {
            changed = false;

            for (var i = 0; i < messages.Count; i++)
            {
                var message = messages[i];
                if (!MessageMatchesAnyGroup(message, turnIds, toolCallIds))
                    continue;

                if (i > copyThroughIndex)
                {
                    copyThroughIndex = i;
                    changed = true;
                }

                if (AddMessageGroupKeys(message, turnIds, toolCallIds))
                    changed = true;
            }
        }

        return copyThroughIndex;
    }

    private static bool AddMessageGroupKeys(
        ChatMessage message,
        HashSet<string> turnIds,
        HashSet<string> toolCallIds)
    {
        var changed = false;

        if (GetMessageTurnId(message) is { } turnId)
            changed |= turnIds.Add(turnId);

        foreach (var callId in GetToolCallIds(message))
            changed |= toolCallIds.Add(callId);

        return changed;
    }

    private static bool MessageMatchesAnyGroup(
        ChatMessage message,
        HashSet<string> turnIds,
        HashSet<string> toolCallIds)
    {
        if (GetMessageTurnId(message) is { } turnId && turnIds.Contains(turnId))
            return true;

        return GetToolCallIds(message).Any(toolCallIds.Contains);
    }

    private static string? GetMessageTurnId(ChatMessage message) =>
        message.AdditionalProperties?.TryGetValue<string>(
            "hpd.messageTurnId",
            out var turnId) == true
            ? turnId
            : null;

    private static IEnumerable<string> GetToolCallIds(ChatMessage message) =>
        message.Contents
            .Select(content => content switch
            {
                ToolCallContent toolCall => toolCall.CallId,
                ToolResultContent toolResult => toolResult.CallId,
                _ => null
            })
            .Where(callId => !string.IsNullOrWhiteSpace(callId))
            .Select(callId => callId!);

    private static AgentEvent? CreateRealtimeTranscriptEvent(
        AgentInputTranscriptUpdate update,
        string messageId,
        string? traceId)
    {
        AgentEvent? evt = update.Stage switch
        {
            AgentInputTranscriptStage.Partial when !string.IsNullOrWhiteSpace(update.Text) =>
                new UserAudioTranscriptDeltaEvent(
                    update.Text,
                    messageId,
                    update.ItemId,
                    update.ContentIndex),
            AgentInputTranscriptStage.Final when !string.IsNullOrWhiteSpace(update.Text) =>
                new UserAudioTranscriptCompletedEvent(
                    update.Text,
                    messageId,
                    update.ItemId,
                    update.ContentIndex),
            AgentInputTranscriptStage.Failed =>
                new UserAudioTranscriptFailedEvent(
                    messageId,
                    update.Error?.Message ?? "Realtime user input transcription failed.",
                    update.ItemId,
                    update.ContentIndex),
            _ => null
        };

        return evt is null
            ? null
            : evt with { TraceId = traceId };
    }

    private static string? ResolveRealtimeTranscriptTargetMessageId(
        IReadOnlyList<ChatMessage> newInputMessages)
    {
        foreach (var message in newInputMessages)
        {
            if (message.Contents.Any(IsRealtimeTranscriptTargetContent))
            {
                return message.MessageId;
            }
        }

        return null;
    }

    private static bool IsRealtimeTranscriptTargetContent(AIContent content) => content switch
    {
        AudioContent audio => AudioContent.IsAudioMediaType(audio.MediaType),
        DataContent data => AudioContent.IsAudioMediaType(data.MediaType),
        UriContent uri => uri.HasTopLevelMediaType("audio"),
        HostedFileContent hosted => hosted.HasTopLevelMediaType("audio"),
        _ => false
    };

    private static bool ProjectRealtimeTranscriptIntoMessages(
        string messageId,
        string transcript,
        IList<ChatMessage> turnHistory,
        IList<ChatMessage> sharedMessages)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return false;

        ChatMessage? replacement = null;

        for (var i = 0; i < turnHistory.Count; i++)
        {
            var message = turnHistory[i];
            if (!string.Equals(message.MessageId, messageId, StringComparison.Ordinal))
                continue;

            replacement = AppendTranscriptToMessage(message, transcript);
            if (!ReferenceEquals(replacement, message))
            {
                turnHistory[i] = replacement;
            }
            else
            {
                return false;
            }

            break;
        }

        if (replacement is null)
            return false;

        for (var i = 0; i < sharedMessages.Count; i++)
        {
            if (string.Equals(sharedMessages[i].MessageId, messageId, StringComparison.Ordinal))
            {
                sharedMessages[i] = replacement;
                break;
            }
        }

        return true;
    }

    private static ChatMessage AppendTranscriptToMessage(ChatMessage message, string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return message;

        var normalizedTranscript = transcript.Trim();
        var contents = message.Contents.ToList();
        var existingTexts = contents
            .OfType<TextContent>()
            .Select(content => content.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        if (existingTexts.Any(text => string.Equals(text, normalizedTranscript, StringComparison.Ordinal)))
            return message;

        var updatedContents = contents.ToList();
        var transcriptContent = new TextContent(normalizedTranscript);
        if (updatedContents.Count > 0 && !updatedContents.OfType<TextContent>().Any())
        {
            updatedContents.Insert(0, transcriptContent);
        }
        else
        {
            updatedContents.Add(transcriptContent);
        }

        return new ChatMessage(message.Role, updatedContents)
        {
            MessageId = message.MessageId,
            AuthorName = message.AuthorName,
            CreatedAt = message.CreatedAt,
            RawRepresentation = message.RawRepresentation,
            AdditionalProperties = message.AdditionalProperties is null
                ? null
                : new AdditionalPropertiesDictionary(message.AdditionalProperties)
        };
    }

    /// <summary>
    /// Delete a specific thread (string-based API).
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="threadId">Thread identifier to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task DeleteThreadAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        var store = Config.SessionStore
            ?? throw new InvalidOperationException(
                "No session store configured. Use WithSessionStore() on AgentBuilder to configure persistence.");

        //  Protect "main" thread from deletion
        if (threadId == "main")
        {
            throw new InvalidOperationException("Cannot delete the 'main' thread.");
        }

        // Load the thread to delete
        var thread = await store.ProjectThreadAsync(
            sessionId,
            threadId,
            ThreadProjectionPurpose.ThreadHistory,
            cancellationToken);
        if (thread == null)
        {
            throw new InvalidOperationException($"Thread '{threadId}' not found in session '{sessionId}'.");
        }

        //  Prevent deletion if thread has children (referential integrity)
        if (thread.ChildThreads.Count > 0)
        {
            throw new InvalidOperationException(
                $"Cannot delete thread with {thread.ChildThreads.Count} child threads. " +
                $"Delete children first: {string.Join(", ", thread.ChildThreads)}");
        }

        //  Perform deletion. Fork group ordering is graph-derived after deletion.
        // Note: No session locking at Agent level - locking should be done by the caller (e.g., ThreadEndpoints)

        // Remove from parent's ChildThreads list
        if (thread.ForkedFrom != null)
        {
            var parent = await store.ProjectThreadAsync(
                sessionId,
                thread.ForkedFrom,
                ThreadProjectionPurpose.ThreadHistory,
                cancellationToken);
            if (parent != null && parent.ChildThreads.Contains(threadId))
            {
                parent.ChildThreads.Remove(threadId);
                parent.LastActivity = DateTime.UtcNow;
                await store.AppendThreadUpdatedAsync(parent, cancellationToken);
            }
        }

        // Delete the thread (after all updates complete)
        await store.DeleteThreadAsync(sessionId, threadId, cancellationToken);
    }

    /// <summary>
    /// Save session metadata manually (advanced use).
    /// </summary>
    /// <param name="session">Session to save</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task SaveSessionAsync(
        Session session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var store = Config.SessionStore
            ?? throw new InvalidOperationException(
                "No session store configured. Use WithSessionStore() on AgentBuilder to configure persistence.");

        await store.SaveSessionAsync(session, cancellationToken);
    }

    /// <summary>
    /// Delete entire session (all threads + content).
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task DeleteSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var store = Config.SessionStore
            ?? throw new InvalidOperationException(
                "No session store configured. Use WithSessionStore() on AgentBuilder to configure persistence.");

        await store.DeleteSessionAsync(sessionId, cancellationToken);
    }

    /// <summary>
    /// List all session IDs.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of session IDs</returns>
    public async Task<List<string>> ListSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        var store = Config.SessionStore;
        if (store == null)
            return [];

        return await store.ListSessionIdsAsync(cancellationToken);
    }

    //
    // ITERATION Middleware SUPPORT
    //

    // V2: ProcessIterationMiddleWareSignals removed - state updates are immediate in V2

    /// <summary>
    /// [DEBUG] Formats messages for logging to verify exact LLM payload.
    /// Shows role, text preview, function calls with args, and function results.
    /// </summary>
    private static string FormatMessagesForLLMLogging(IReadOnlyList<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            sb.AppendLine($"    [{i}] {msg.Role}:");

            foreach (var content in msg.Contents)
            {
                switch (content)
                {
                    case TextContent tc:
                        var textPreview = tc.Text?.Length > 100
                            ? tc.Text.Substring(0, 100) + "..."
                            : tc.Text;
                        sb.AppendLine($"         Text: \"{textPreview}\"");
                        break;

                    case FunctionCallContent fcc:
                        var argsPreview = fcc.Arguments != null
                            ? string.Join(", ", fcc.Arguments.Select(kv => $"{kv.Key}={kv.Value}"))
                            : "<no args>";
                        if (argsPreview.Length > 100) argsPreview = argsPreview.Substring(0, 100) + "...";
                        sb.AppendLine($"         FunctionCall: {fcc.Name}({argsPreview}) [CallId: {fcc.CallId}]");
                        break;

                    case FunctionResultContent frc:
                        var resultPreview = frc.Result?.ToString() ?? "<null>";
                        if (resultPreview.Length > 100) resultPreview = resultPreview.Substring(0, 100) + "...";
                        sb.AppendLine($"         FunctionResult: [CallId: {frc.CallId}] => \"{resultPreview}\"");
                        break;

                    default:
                        sb.AppendLine($"         {content.GetType().Name}");
                        break;
                }
            }
        }
        return sb.ToString();
    }

    private static IReadOnlyList<ContextMessageSnapshot> BuildContextMessageSnapshots(
        IReadOnlyList<ChatMessage> finalMessages,
        IReadOnlyList<ChatMessage> preIterationMessages)
    {
        var preExisting = new HashSet<ChatMessage>(preIterationMessages, ReferenceEqualityComparer.Instance);
        var contextMessages = new List<ContextMessageSnapshot>();

        foreach (var message in finalMessages)
        {
            if (preExisting.Contains(message))
                continue;

            var text = message.Text;
            if (string.IsNullOrWhiteSpace(text))
                continue;

            contextMessages.Add(new ContextMessageSnapshot(
                Role: message.Role.ToString(),
                Text: text));
        }

        return contextMessages;
    }

    internal static MiddlewareStateSnapshotEvent BuildMiddlewareStateSnapshotEvent(
        string agentName,
        IReadOnlyDictionary<string, MiddlewareStateFactory> stateFactories,
        MiddlewareState state,
        string? sessionId,
        string? threadId,
        int iteration,
        string phase,
        string? batchId,
        string? functionCallId,
        int? toolCallIndex)
    {
        var entries = BuildMiddlewareStateEntrySnapshots(stateFactories, state);
        return new MiddlewareStateSnapshotEvent(
            AgentName: agentName,
            SessionId: sessionId,
            ThreadId: threadId,
            Iteration: iteration,
            Phase: phase,
            BatchId: batchId,
            FunctionCallId: functionCallId,
            ToolCallIndex: toolCallIndex,
            StateCount: entries.Count,
            States: entries,
            Timestamp: DateTimeOffset.UtcNow)
        {
            SessionId = sessionId,
            ThreadId = threadId
        };
    }

    internal static async Task EmitMiddlewareStateChangedAsync(
        string agentName,
        IReadOnlyDictionary<string, MiddlewareStateFactory> stateFactories,
        Middleware.AgentContext context,
        MiddlewareState before,
        MiddlewareState after,
        string phase,
        string? batchId,
        string? functionCallId,
        int? toolCallIndex)
    {
        var changes = BuildMiddlewareStateChanges(stateFactories, before, after);
        if (changes.Count == 0)
            return;

        await context.PublishAsync(new MiddlewareStateChangedEvent(
            AgentName: agentName,
            SessionId: context.Session?.Id,
            ThreadId: context.Thread?.Id,
            Iteration: context.State.Iteration,
            Phase: phase,
            BatchId: batchId,
            FunctionCallId: functionCallId,
            ToolCallIndex: toolCallIndex,
            ChangeCount: changes.Count,
            Changes: changes,
            Timestamp: DateTimeOffset.UtcNow)
        {
            SessionId = context.Session?.Id,
            ThreadId = context.Thread?.Id
        });
    }

    internal static IReadOnlyList<MiddlewareStateChange> BuildMiddlewareStateChanges(
        IReadOnlyDictionary<string, MiddlewareStateFactory> stateFactories,
        MiddlewareState before,
        MiddlewareState after)
    {
        var beforeEntries = BuildMiddlewareStateEntrySnapshots(stateFactories, before)
            .ToDictionary(entry => entry.Key, StringComparer.Ordinal);
        var afterEntries = BuildMiddlewareStateEntrySnapshots(stateFactories, after)
            .ToDictionary(entry => entry.Key, StringComparer.Ordinal);
        var keys = beforeEntries.Keys
            .Concat(afterEntries.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var changes = new List<MiddlewareStateChange>();

        foreach (var key in keys)
        {
            var hadBefore = beforeEntries.TryGetValue(key, out var beforeEntry);
            var hasAfter = afterEntries.TryGetValue(key, out var afterEntry);
            var referenceEntry = afterEntry ?? beforeEntry;
            if (referenceEntry is null)
                continue;

            var changeType =
                !hadBefore ? "added" :
                !hasAfter ? "removed" :
                MiddlewareStateEntryPayloadEquals(beforeEntry!, afterEntry!) ? null :
                "updated";

            if (changeType is null)
                continue;

            changes.Add(new MiddlewareStateChange(
                Key: key,
                Type: referenceEntry.Type,
                PropertyName: referenceEntry.PropertyName,
                Scope: referenceEntry.Scope,
                Persistent: referenceEntry.Persistent,
                Version: referenceEntry.Version,
                ChangeType: changeType,
                Before: beforeEntry?.Json,
                After: afterEntry?.Json,
                Error: afterEntry?.Error ?? beforeEntry?.Error,
                Redacted: referenceEntry.Redacted));
        }

        return changes;
    }

    private static IReadOnlyList<MiddlewareStateEntrySnapshot> BuildMiddlewareStateEntrySnapshots(
        IReadOnlyDictionary<string, MiddlewareStateFactory> stateFactories,
        MiddlewareState state)
    {
        if (state.States.Count == 0)
            return [];

        var entries = new List<MiddlewareStateEntrySnapshot>(state.States.Count);
        foreach (var (key, value) in state.States.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            stateFactories.TryGetValue(key, out var factory);
            var json = default(JsonElement?);
            string? error = null;

            if (value is JsonElement element)
            {
                json = element.Clone();
            }
            else if (factory is not null && value is not null)
            {
                try
                {
                    using var document = JsonDocument.Parse(factory.Serialize(value));
                    json = document.RootElement.Clone();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }
            else if (value is null)
            {
                error = "State value is null.";
            }
            else
            {
                error = "No registered middleware state factory.";
            }

            entries.Add(new MiddlewareStateEntrySnapshot(
                Key: key,
                Type: factory?.StateType.FullName ?? value?.GetType().FullName ?? "unknown",
                PropertyName: factory?.PropertyName ?? key,
                Scope: factory?.Scope ?? StateScope.Thread,
                Persistent: factory?.Persistent ?? false,
                Version: factory?.Version ?? 0,
                Json: json,
                Error: error,
                Redacted: false));
        }

        return entries;
    }

    private static bool MiddlewareStateEntryPayloadEquals(
        MiddlewareStateEntrySnapshot before,
        MiddlewareStateEntrySnapshot after)
    {
        return string.Equals(before.Json?.GetRawText(), after.Json?.GetRawText(), StringComparison.Ordinal)
            && string.Equals(before.Error, after.Error, StringComparison.Ordinal)
            && before.Redacted == after.Redacted;
    }

    private static IReadOnlyList<ToolContextSnapshot> BuildToolContextSnapshots(ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 } tools)
            return [];

        var snapshots = new List<ToolContextSnapshot>(tools.Count);
        foreach (var tool in tools)
        {
            string? toolharnessName = null;
            ToolCallType? callType = null;
            var isContainer = false;

            if (tool.AdditionalProperties is { } properties)
            {
                if (tool is AIFunction typedFunction &&
                    properties.TryGetValue(HPDCapabilityMetadata.AdditionalPropertiesKey, out var typedValue) &&
                    typedValue is HPDCapabilityMetadata typedMetadata)
                {
                    callType = FunctionExecutionCore.LookupToolCallType(typedFunction);
                    isContainer = typedMetadata.Kind is
                        HPDCapabilityKind.SkillActivation or HPDCapabilityKind.ToolHarnessActivation;
                }

                if (properties.TryGetValue("ToolHarnessName", out var toolharnessValue) && toolharnessValue is string h)
                    toolharnessName = h;
                else if (properties.TryGetValue("ParentToolHarness", out var parentValue) && parentValue is string p)
                    toolharnessName = p;

                if (callType is null && properties.TryGetValue("IsContainer", out var containerValue) && containerValue is bool container)
                    isContainer = container;

                if (callType is null && properties.TryGetValue("CapabilityType", out var capabilityValue) && capabilityValue is string capability)
                {
                    callType = capability switch
                    {
                        "Function" => ToolCallType.Function,
                        "Skill" => ToolCallType.Skill,
                        "SubAgent" => ToolCallType.SubAgent,
                        "MultiAgent" => ToolCallType.MultiAgent,
                        "MCPServer" => ToolCallType.McpServer,
                        "OpenApi" => ToolCallType.OpenApi,
                        _ => null
                    };
                }
            }

            snapshots.Add(new ToolContextSnapshot(
                Name: tool.Name,
                Description: tool.Description,
                ToolHarnessName: toolharnessName,
                CallType: callType,
                IsContainer: isContainer,
                InputSchemaJson: tool is AIFunctionDeclaration function
                    && function.JsonSchema.ValueKind != JsonValueKind.Undefined
                    ? function.JsonSchema.GetRawText()
                    : null));
        }

        return snapshots;
    }
}

#region Agent Decision Engine
/// <summary>
/// Pure decision engine for agent execution loop.
/// Contains ZERO I/O operations - all decisions are deterministic and testable.
/// This is the "Functional Core" of the agent architecture.
/// </summary>
internal sealed class AgentDecisionEngine
{
    /// <summary>
    /// Decides what the agent should do next based on current state.
    /// This is a pure function - same inputs always produce same output.
    /// </summary>
    /// <param name="state">Current immutable state</param>
    /// <param name="lastResponse">Response from last LLM call (null on first iteration)</param>
    /// <param name="config">Agent configuration (max iterations, available tools, etc.)</param>
    /// <returns>Decision for what action to take next</returns>
    public AgentDecision DecideNextAction(
        AgentLoopState state,
        ChatResponse? lastResponse,
        AgentConfiguration config)
    {
        // Check: Already terminated by external source (e.g., permission Middleware, manual termination)
        if (state.IsTerminated)
            return new AgentDecision.Terminate(state.TerminationReason ?? "Terminated");

        // If no response yet, must call LLM

        if (lastResponse == null)
            return AgentDecision.CallLLM.Instance;

        // Check if response has any tool calls
        bool hasToolCalls = lastResponse.Messages
            .Any(m => m.Contents.OfType<FunctionCallContent>().Any());

        if (!hasToolCalls)
            return new AgentDecision.Complete(lastResponse);
        // Check if all requested tools are available (optional)

        if (config.TerminateOnUnknownCalls && config.AvailableTools != null)
        {
            var toolRequests = ExtractToolRequestsFromResponse(lastResponse);
            var unknownTools = toolRequests
                .Where(req => !config.AvailableTools.Contains(req.Name))
                .Select(req => req.Name)
                .ToList();

            if (unknownTools.Count > 0)
            {
                return new AgentDecision.Terminate(
                    $"Unknown tools requested: {string.Join(", ", unknownTools)}");
            }
        }
        // If response had tool calls, they will be executed inline
        // and we need to call the LLM again with the results

        return AgentDecision.CallLLM.Instance;
    }

    /// <summary>
    /// Extracts tool/function call requests from LLM response.
    /// Searches all messages and all contents for FunctionCallContent.
    /// </summary>
    /// <param name="response">LLM response to parse</param>
    /// <returns>List of tool requests (empty if none found)</returns>
    private static IReadOnlyList<AgentToolCallRequest> ExtractToolRequestsFromResponse(
        ChatResponse response)
    {
        var requests = new List<AgentToolCallRequest>();

        foreach (var message in response.Messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is FunctionCallContent fcc && !string.IsNullOrEmpty(fcc.Name))
                {
                    var immutableArgs = (fcc.Arguments ?? new Dictionary<string, object?>())
                        .ToImmutableDictionary();

                    requests.Add(new AgentToolCallRequest(
                        fcc.Name,
                        fcc.CallId,
                        immutableArgs));
                }
            }
        }

        return requests;
    }
}

/// <summary>
/// Discriminated union representing all possible agent decisions.
/// The decision engine returns one of these sealed record types.
/// Pattern matching ensures exhaustive handling of all cases.
/// </summary>
internal abstract record AgentDecision
{
    /// <summary>
    /// Decision: Call the LLM with current conversation messages.
    /// This is the default action when starting a new iteration or after tool execution.
    /// </summary>
    public sealed record CallLLM : AgentDecision
    {
        // Singleton pattern - only one instance needed
        public static readonly CallLLM Instance = new();
        private CallLLM() { }
    }

    /// <summary>
    /// Decision: Agent completed successfully (no more tools to execute).
    /// The LLM provided a text response without requesting any tool calls.
    /// </summary>
    /// <param name="FinalResponse">The final response from the LLM</param>
    public sealed record Complete(ChatResponse FinalResponse) : AgentDecision;

    /// <summary>
    /// Decision: Terminate agent execution with specified reason.
    /// Can be triggered by:
    /// - Max iterations reached (checked by middleware)
    /// - Circuit breaker triggered (via CircuitBreakerIterationMiddleware)
    /// - Too many consecutive errors (via ErrorTrackingIterationMiddleware)
    /// - External termination (e.g., permission denied via middleware)
    /// </summary>
    /// <param name="Reason">Human-readable termination reason</param>
    public sealed record Terminate(string Reason) : AgentDecision;

    // Private constructor prevents external inheritance
    private AgentDecision() { }
}

/// <summary>
/// Represents a request to invoke a tool/function.
/// Contains all information needed to execute the tool.
/// </summary>
/// <param name="Name">Name of the tool to invoke</param>
/// <param name="CallId">Unique identifier for this specific invocation (for correlation)</param>
/// <param name="Arguments">Dictionary of argument names to values</param>
internal sealed record AgentToolCallRequest(
    string Name,
    string CallId,
    IReadOnlyDictionary<string, object?> Arguments)
{
    /// <summary>
    /// Creates a ToolCallRequest with immutable arguments dictionary.
    /// </summary>
    public static AgentToolCallRequest Create(string name, string callId, IDictionary<string, object?>? arguments = null)
    {
        var immutableArgs = arguments != null
            ? arguments.ToImmutableDictionary()
            : ImmutableDictionary<string, object?>.Empty;

        return new AgentToolCallRequest(name, callId, immutableArgs);
    }
}

/// <summary>
/// Immutable snapshot of agent execution loop state.
/// Consolidates all 11 state variables that were scattered in RunAgenticLoopInternal.
/// Thread-safe and testable - enables pure decision-making logic.
/// </summary>
public sealed record AgentLoopState
{
    /// <summary>
    /// Unique identifier for this agent run/turn.
    /// </summary>
    public required string RunId { get; init; }

    /// <summary>
    /// Conversation ID this run belongs to.
    /// </summary>
    public required string ConversationId { get; init; }

    /// <summary>
    /// Name of the agent executing this run.
    /// </summary>
    public required string AgentName { get; init; }

    /// <summary>
    /// When this agent run started (UTC).
    /// </summary>
    public required DateTime StartTime { get; init; }

    /// <summary>
    /// Internal mutable reference to the shared message list used during runtime execution.
    /// NOT serialized - only exists during active agent execution.
    /// When middleware modifies this list, all contexts see the changes immediately.
    /// Excluded from record equality comparison via [JsonIgnore].
    /// </summary>
    [JsonIgnore]
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal List<ChatMessage>? MessagesRef { get; init; }

    /// <summary>
    /// Messages in the current conversation context (full history).
    /// This is the complete conversation that gets sent to the LLM.
    ///
    /// RUNTIME: Returns shared mutable reference (MessagesRef).
    /// SERIALIZATION: Returns snapshot copy (_deserializedMessages).
    /// DESERIALIZATION: Stores defensive copy in _deserializedMessages.
    /// </summary>
    [JsonPropertyName("currentMessages")]
    public IReadOnlyList<ChatMessage> CurrentMessages
    {
        get
        {
            // Runtime: return shared reference (everyone sees same list)
            if (MessagesRef != null)
                return MessagesRef;

            // Deserialization: return stored defensive copy
            return (IReadOnlyList<ChatMessage>?)_deserializedMessages ?? Array.Empty<ChatMessage>();
        }
        init
        {
            // Store defensive copy for deserialization path
            // JsonSerializer uses this path when restoring serialized loop state.
            _deserializedMessages = value?.ToList();
        }
    }

    /// <summary>
    /// Deserialized message storage used when restoring serialized loop state.
    /// Null during runtime execution (MessagesRef is used instead).
    /// </summary>
    private List<ChatMessage>? _deserializedMessages;

    /// <summary>
    /// Messages accumulated during this agent turn (for response history).
    /// These messages represent what was added during this RunAsync call.
    /// </summary>
    public required IReadOnlyList<ChatMessage> TurnHistory { get; init; }

    /// <summary>
    /// Current iteration number (0-based).
    /// Each iteration represents one LLM call + tool execution cycle.
    /// </summary>
    public required int Iteration { get; init; }

    /// <summary>
    /// Whether the loop has been terminated (by any mechanism).
    /// Once true, the loop will exit on next check.
    /// </summary>
    public required bool IsTerminated { get; init; }

    /// <summary>
    /// Human-readable reason for termination (if terminated).
    /// Examples: "Max iterations reached", "Circuit breaker triggered", etc.
    /// </summary>
    public string? TerminationReason { get; init; }

    //
    // FUNCTION TRACKING
    //

    /// <summary>
    /// Functions completed in this run (for telemetry and deduplication).
    /// Tracks which functions have been successfully executed.
    /// </summary>
    public required ImmutableHashSet<string> CompletedFunctions { get; init; }

    //
    // HISTORY OPTIMIZATION STATE
    //

    /// <summary>
    /// Whether the LLM service manages conversation history server-side.
    /// When true, we only send delta messages (significant token savings).
    /// Detected automatically when service returns a ConversationId.
    /// </summary>
    public required bool InnerClientTracksHistory { get; init; }

    /// <summary>
    /// Number of messages already sent to the server (for delta sending).
    /// Used to calculate which messages to send when InnerClientTracksHistory is true.
    /// </summary>
    public required int MessagesSentToInnerClient { get; init; }

    /// <summary>
    /// The last response ID returned by the provider (e.g. Responses API "resp_..." ID).
    /// Passed back as previous_response_id on delta iterations so the provider can link
    /// the new input to its server-side history.
    /// </summary>
    public string? LastProviderResponseId { get; init; }

    //
    // STREAMING STATE
    //

    /// <summary>
    /// Last assistant message ID (for event correlation).
    /// Used to link events (text deltas, reasoning, etc.) to specific messages.
    /// </summary>
    public string? LastAssistantMessageId { get; init; }

    /// <summary>
    /// Accumulated streaming updates (for final response construction).
    /// Collected during LLM streaming to build complete ChatResponse.
    /// </summary>
    public required IReadOnlyList<ChatResponseUpdate> ResponseUpdates { get; init; }

    //
    // MIDDLEWARE STATE (extensible, owned by middlewares)
    //

    /// <summary>
    /// Source-generated middleware state container.
    /// Provides strongly-typed properties for each middleware state type marked with [MiddlewareState].
    /// </summary>
    public MiddlewareState MiddlewareState { get; init; }
        = new MiddlewareState();

    //
    // USAGE TRACKING
    //

    /// <summary>
    /// Token usage accumulated across all LLM iterations in this turn.
    /// Sums InputTokenCount, OutputTokenCount, CachedInputTokenCount, ReasoningTokenCount, etc.
    /// Null until the first iteration completes with usage data.
    /// For per-iteration breakdown see <see cref="IterationUsage"/>.
    /// </summary>
    public UsageDetails? AccumulatedUsage { get; init; }

    /// <summary>
    /// Per-iteration token usage, one entry per LLM call in this turn.
    /// Index 0 = first LLM call, index 1 = after first tool round-trip, etc.
    /// Entries are null if the provider did not return usage for that iteration.
    /// </summary>
    public ImmutableList<UsageDetails?> IterationUsage { get; init; }
        = ImmutableList<UsageDetails?>.Empty;

    //
    // FACTORY METHOD
    //

    /// <summary>
    /// Creates initial state for runtime execution with shared message reference.
    /// This is the primary factory used by Agent.cs during normal execution.
    /// </summary>
    /// <param name="messagesRef">Shared mutable list - all contexts will reference this same list</param>
    /// <param name="runId">Unique identifier for this run</param>
    /// <param name="conversationId">Conversation identifier</param>
    /// <param name="agentName">Name of the agent</param>
    /// <param name="persistentState">Middleware state to restore (if resuming)</param>
    /// <returns>State with shared reference to messages</returns>
    internal static AgentLoopState Initial(
        List<ChatMessage> messagesRef,
        string runId,
        string conversationId,
        string agentName,
        MiddlewareState? persistentState = null) => new()
    {
        RunId = runId,
        ConversationId = conversationId,
        AgentName = agentName,
        StartTime = DateTime.UtcNow,
        // Store shared reference (internal, not serialized)
        // The getter will return MessagesRef automatically.
        MessagesRef = messagesRef,
        TurnHistory = ImmutableList<ChatMessage>.Empty,
        Iteration = 0,
        IsTerminated = false,
        TerminationReason = null,
        CompletedFunctions = ImmutableHashSet<string>.Empty,
        InnerClientTracksHistory = false,
        MessagesSentToInnerClient = 0,
        LastAssistantMessageId = null,
        ResponseUpdates = ImmutableList<ChatResponseUpdate>.Empty,
        Version = 1,
        MiddlewareState = persistentState ?? new MiddlewareState()
    };

    /// <summary>
    /// Creates initial state with a defensive message copy.
    /// </summary>
    /// <param name="messages">Messages to copy (immutable snapshot)</param>
    /// <param name="runId">Unique identifier for this run</param>
    /// <param name="conversationId">Conversation identifier</param>
    /// <param name="agentName">Name of the agent</param>
    /// <param name="persistentState">Middleware state to restore (if resuming)</param>
    /// <returns>State with defensive copy of messages (no shared reference)</returns>
    public static AgentLoopState InitialSafe(
        IReadOnlyList<ChatMessage> messages,
        string runId,
        string conversationId,
        string agentName,
        MiddlewareState? persistentState = null) => new()
    {
        RunId = runId,
        ConversationId = conversationId,
        AgentName = agentName,
        StartTime = DateTime.UtcNow,
        // No shared reference - use init setter (creates defensive copy)
        CurrentMessages = messages,
        TurnHistory = ImmutableList<ChatMessage>.Empty,
        Iteration = 0,
        IsTerminated = false,
        TerminationReason = null,
        CompletedFunctions = ImmutableHashSet<string>.Empty,
        InnerClientTracksHistory = false,
        MessagesSentToInnerClient = 0,
        LastAssistantMessageId = null,
        ResponseUpdates = ImmutableList<ChatResponseUpdate>.Empty,
        Version = 1,
        MiddlewareState = persistentState ?? new MiddlewareState()
    };

    //
    // STATE TRANSITIONS (Immutable Updates)
    // All methods return NEW instances - never mutate existing state
    //

    /// <summary>
    /// Advances to the next iteration.
    /// </summary>
    public AgentLoopState NextIteration() =>
        this with { Iteration = Iteration + 1 };

    /// <summary>
    /// Updates the current conversation messages (creates new state with defensive copy).
    /// Used when need to replace the entire message list (rare).
    /// WARNING: This breaks shared reference! Use with caution.
    /// In most cases, middleware should mutate the shared list in-place.
    /// </summary>
    public AgentLoopState WithMessages(IReadOnlyList<ChatMessage> messages) =>
        this with
        {
            MessagesRef = null,  // Clear shared reference
            CurrentMessages = messages  // Calls init, creates defensive copy
        };

    /// <summary>
    /// Appends a message to the turn history.
    /// Turn history tracks what was added during this RunAsync call.
    /// </summary>
    public AgentLoopState AppendToTurnHistory(ChatMessage message)
    {
        var updatedHistory = new List<ChatMessage>(TurnHistory) { message };
        return this with { TurnHistory = updatedHistory };
    }

    /// <summary>
    /// Terminates the loop with the specified reason.
    /// </summary>
    public AgentLoopState Terminate(string reason) =>
        this with { IsTerminated = true, TerminationReason = reason };



    /// <summary>
    /// Records a function completion for telemetry tracking (successful calls only).
    /// </summary>
    /// <param name="functionName">Name of the completed function</param>
    /// <returns>New state with updated function tracking</returns>
    public AgentLoopState CompleteFunction(string functionName) =>
        this with { CompletedFunctions = CompletedFunctions.Add(functionName) };

    /// <summary>
    /// Enables server-side history tracking after detecting ConversationId in response.
    /// Significant token savings for multi-turn conversations.
    /// </summary>
    /// <param name="messageCount">Number of messages sent to server</param>
    /// <param name="responseId">The provider response ID to pass as previous_response_id on the next call</param>
    public AgentLoopState EnableHistoryTracking(int messageCount, string? responseId = null) =>
        this with
        {
            InnerClientTracksHistory = true,
            MessagesSentToInnerClient = messageCount,
            LastProviderResponseId = responseId
        };

    /// <summary>
    /// Updates the last provider response ID (for delta continuation after tool calls).
    /// </summary>
    public AgentLoopState WithLastProviderResponseId(string responseId) =>
        this with { LastProviderResponseId = responseId };

    /// <summary>
    /// Disables server-side history tracking (fall back to sending full history).
    /// </summary>
    public AgentLoopState DisableHistoryTracking() =>
        this with
        {
            InnerClientTracksHistory = false,
            MessagesSentToInnerClient = 0
        };

    /// <summary>
    /// Sets the last assistant message ID (for event correlation).
    /// </summary>
    public AgentLoopState WithLastAssistantMessageId(string messageId) =>
        this with { LastAssistantMessageId = messageId };

    /// <summary>
    /// Accumulates a streaming response update.
    /// Used during LLM streaming to collect all deltas.
    /// </summary>
    public AgentLoopState AccumulateResponseUpdate(ChatResponseUpdate update)
    {
        var updatedUpdates = new List<ChatResponseUpdate>(ResponseUpdates) { update };
        return this with { ResponseUpdates = updatedUpdates };
    }

    /// <summary>
    /// Clears accumulated response updates (after building final response).
    /// </summary>
    public AgentLoopState ClearResponseUpdates() =>
        this with { ResponseUpdates = ImmutableList<ChatResponseUpdate>.Empty };

    /// <summary>
    /// Records usage from a completed iteration:
    /// - Appends to <see cref="IterationUsage"/> (per-iteration breakdown)
    /// - Adds into <see cref="AccumulatedUsage"/> (running total across the turn)
    /// No-ops if iterationUsage is null (provider returned no usage data) but still
    /// appends a null entry so IterationUsage indices stay aligned with iteration numbers.
    /// </summary>
    public AgentLoopState WithAccumulatedUsage(UsageDetails? iterationUsage)
    {
        var newIterationUsage = IterationUsage.Add(iterationUsage);

        if (iterationUsage == null)
            return this with { IterationUsage = newIterationUsage };

        var total = AccumulatedUsage ?? new UsageDetails();
        total.Add(iterationUsage);
        return this with { AccumulatedUsage = total, IterationUsage = newIterationUsage };
    }

    /// <summary>
    /// Serialized loop-state schema version.
    /// </summary>
    public int Version { get; init; } = 2;

    /// <summary>
    /// Serializes this loop state to JSON.
    /// Uses Microsoft.Extensions.AI's built-in serialization for ChatMessage and AIContent.
    /// Handles immutable collections, polymorphic content, and all message types automatically.
    /// </summary>
    public string Serialize()
    {
        return JsonSerializer.Serialize(this, (JsonTypeInfo<object?>)AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(object)));
    }

    /// <summary>
    /// Deserializes state from JSON.
    /// Uses Microsoft.Extensions.AI's built-in deserialization.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when deserialization fails</exception>
    public static AgentLoopState Deserialize(string json)
    {
        return JsonSerializer.Deserialize<AgentLoopState>(json, (JsonTypeInfo<AgentLoopState>)AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(AgentLoopState)))
            ?? throw new InvalidOperationException("Failed to deserialize AgentLoopState");
    }
}

/// <summary>
/// Configuration data for the agent decision engine (pure data, no behavior).
/// Contains all settings needed for decision-making logic.
/// Immutable and easily testable.
/// </summary>
/// <remarks>
internal sealed record AgentConfiguration
{
    /// <summary>
    /// Maximum iterations before forced termination.
    /// Each iteration = one LLM call + tool execution cycle.
    /// Prevents runaway loops and excessive costs.
    /// </summary>
    public required int MaxIterations { get; init; }

    /// <summary>
    /// Whether to terminate on unknown tool requests (vs. pass through for multi-agent scenarios).
    ///
    /// When true: If LLM requests a tool that doesn't exist, terminate immediately.
    /// When false: Unknown tools are passed through (enables multi-agent handoffs).
    /// </summary>
    public required bool TerminateOnUnknownCalls { get; init; }

    /// <summary>
    /// Set of tool names available for execution.
    /// Used to detect unknown tool requests when TerminateOnUnknownCalls is true.
    /// </summary>
    public required IReadOnlySet<string> AvailableTools { get; init; }

    /// <summary>
    /// Factory method: Create configuration from AgentConfig.
    /// Extracts only the fields needed for decision-making.
    /// </summary>
    /// <param name="config">Full agent configuration</param>
    /// <param name="maxIterations">Max iterations (from Agent constructor parameter)</param>
    /// <param name="availableTools">Set of available tool names</param>
    /// <returns>Lightweight configuration for decision engine</returns>
    public static AgentConfiguration FromAgentConfig(
        AgentConfig? config,
        int maxIterations,
        IReadOnlySet<string> availableTools)
    {
        return new AgentConfiguration
        {
            MaxIterations = maxIterations,
            TerminateOnUnknownCalls = config?.AgenticLoop?.TerminateOnUnknownCalls ?? false,
            AvailableTools = availableTools
        };
    }

    /// <summary>
    /// Factory method: Create default configuration for testing.
    /// </summary>
    /// <param name="maxIterations">Maximum iterations (default: 10)</param>
    /// <param name="availableTools">Available tool names (default: empty)</param>
    /// <param name="terminateOnUnknownCalls">Whether to terminate on unknown tools (default: false)</param>
    /// <returns>Configuration with sensible defaults for testing</returns>
    public static AgentConfiguration Default(
        int maxIterations = 10,
        IReadOnlySet<string>? availableTools = null,
        bool terminateOnUnknownCalls = false)
    {
        return new AgentConfiguration
        {
            MaxIterations = maxIterations,
            TerminateOnUnknownCalls = terminateOnUnknownCalls,
            AvailableTools = availableTools ?? new HashSet<string>()
        };
    }
}

#endregion

// Function map helpers moved into `FunctionCallProcessor` to reduce indirection.

#region Content Extraction Utilities

/// <summary>
/// High-performance content extraction and Middlewareing utilities.
/// Optimized with manual iteration to avoid LINQ overhead.
/// Eliminates duplication of content extraction logic across Agent, MessageProcessor,
/// and AgentDocumentProcessor.
/// </summary>
internal static class ContentExtractor
{
    /// <summary>
    /// Creates canonical string for content comparison (deduplication).
    /// Covers all major content types to prevent duplicate message appending.
    /// </summary>
    /// <param name="contents">The content list to canonicalize</param>
    /// <returns>A deterministic string representation of the contents</returns>
    public static string Canonicalize(IList<AIContent> contents)
    {
        var sb = new StringBuilder();
        foreach (var c in contents)
        {
            switch (c)
            {
                case TextReasoningContent r:
                    // Check reasoning first since it derives from TextContent
                    sb.Append("|R:").Append(r.Text);
                    break;
                case TextContent t:
                    sb.Append("|T:").Append(t.Text);
                    break;
                case FunctionCallContent fc:
                    sb.Append("|F:").Append(fc.Name).Append(":").Append(fc.CallId).Append(":");
                    sb.Append(FunctionCallArgumentSerializer.Serialize(fc));
                    break;
                case FunctionResultContent fr:
                    sb.Append("|FR:").Append(fr.CallId).Append(":");
                    sb.Append(fr.Result?.ToString() ?? "null");
                    if (fr.Exception != null)
                    {
                        sb.Append(":EX:").Append(fr.Exception.Message);
                    }
                    break;
                case DataContent data:
                    // DataContent covers images, audio, and generic data
                    sb.Append("|D:").Append(data.MediaType ?? "unknown").Append(":");
                    if (!data.Data.IsEmpty)
                    {
                        sb.Append(HashDataBytes(data.Data));
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Extracts only text content (ignores function calls, data, etc.).
    /// Used for text-only comparison and deduplication.
    /// </summary>
    /// <param name="contents">The content list to extract text from</param>
    /// <returns>Combined text from all TextContent and TextReasoningContent items</returns>
    public static string ExtractTextOnly(IList<AIContent> contents)
    {
        var sb = new StringBuilder();
        foreach (var c in contents)
        {
            switch (c)
            {
                case TextReasoningContent r:
                    sb.Append(r.Text);
                    break;
                case TextContent t:
                    sb.Append(t.Text);
                    break;
                    // Ignore function calls, function results, and data content for text comparison
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Extracts text from a message (handles multiple TextContent items).
    /// Uses LINQ for simplicity when performance is not critical.
    /// </summary>
    /// <param name="message">The message to extract text from</param>
    /// <returns>Combined text from all non-empty TextContent items, space-separated</returns>
    public static string ExtractText(ChatMessage message)
    {
        var textContents = message.Contents
            .OfType<TextContent>()
            .Select(tc => tc.Text)
            .Where(text => !string.IsNullOrEmpty(text));

        return string.Join(" ", textContents);
    }

    /// <summary>
    /// Extracts all function call names from contents (optimized).
    /// Manual iteration to avoid LINQ overhead.
    /// </summary>
    /// <param name="contents">The content list to extract function names from</param>
    /// <returns>List of function call names (may contain duplicates)</returns>
    public static List<string> ExtractFunctionNames(IList<AIContent> contents)
    {
        var names = new List<string>();
        for (int i = 0; i < contents.Count; i++)
        {
            if (contents[i] is FunctionCallContent fc && !string.IsNullOrEmpty(fc.Name))
                names.Add(fc.Name);
        }
        return names;
    }

    /// <summary>
    /// Extracts all function call names from message history (optimized).
    /// Returns a dictionary mapping agent names to their function calls.
    /// </summary>
    /// <param name="history">The message history to scan</param>
    /// <param name="agentName">The agent name to attribute function calls to</param>
    /// <returns>Dictionary mapping agent name to list of function call names</returns>
    public static Dictionary<string, List<string>> ExtractFunctionCallsFromHistory(
        IReadOnlyList<ChatMessage> history,
        string agentName)
    {
        var metadata = new Dictionary<string, List<string>>();

        foreach (var message in history)
        {
            if (message.Role != ChatRole.Assistant)
                continue;

            // Manual iteration instead of LINQ chain (OfType + Select + Where + ToList + Any)
            List<string>? functionCalls = null;
            for (int i = 0; i < message.Contents.Count; i++)
            {
                if (message.Contents[i] is FunctionCallContent fc &&
                    !string.IsNullOrEmpty(fc.Name))
                {
                    (functionCalls ??= []).Add(fc.Name);
                }
            }

            if (functionCalls is { Count: > 0 })
            {
                // Append to existing list instead of overwriting
                if (!metadata.TryGetValue(agentName, out var existingList))
                {
                    metadata[agentName] = functionCalls;
                }
                else
                {
                    // Add unique function calls to avoid duplicates (manual loop instead of LINQ)
                    foreach (var fc in functionCalls)
                    {
                        if (!existingList.Contains(fc))
                            existingList.Add(fc);
                    }
                }
            }
        }

        return metadata;
    }

    /// <summary>
    /// Middlewares out specific content types (e.g., remove reasoning to save tokens).
    /// </summary>
    /// <typeparam name="TExclude">The content type to exclude</typeparam>
    /// <param name="contents">The content list to Middleware</param>
    /// <returns>New list with excluded type removed</returns>
    public static List<AIContent> MiddlewareByType<TExclude>(
        IList<AIContent> contents) where TExclude : AIContent
    {
        var Middlewareed = new List<AIContent>(contents.Count);
        for (int i = 0; i < contents.Count; i++)
        {
            if (contents[i] is not TExclude)
                Middlewareed.Add(contents[i]);
        }
        return Middlewareed;
    }

    /// <summary>
    /// Computes a deterministic SHA-256 hash of byte data for content comparison.
    /// Stable across processes and prevents collisions better than GetHashCode().
    /// </summary>
    private static string HashDataBytes(ReadOnlyMemory<byte> data)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(data.ToArray());
        return Convert.ToHexString(hash);
    }
}

#endregion

#region FunctionCallProcessor

/// <summary>
/// Handles all function calling logic, including multi-turn execution and Middleware pipelines.
/// </summary>
internal class FunctionCallProcessor
{
    private readonly HPD.Events.IEventCoordinator _eventCoordinator;
    private readonly AgentMiddlewarePipeline _middlewarePipeline;
    private readonly ErrorHandlingConfig? _errorHandlingConfig;
    private readonly IList<AITool>? _serverConfiguredTools;
    private readonly AgenticLoopConfig? _agenticLoopConfig;
    private readonly FunctionExecutionCore _functionExecutionCore;
    private readonly string _agentName;
    private readonly IReadOnlyDictionary<string, MiddlewareStateFactory> _stateFactories;

    public FunctionCallProcessor(
        HPD.Events.IEventCoordinator eventCoordinator,
        AgentMiddlewarePipeline middlewarePipeline,
        FunctionExecutionCore functionExecutionCore,
        int maxFunctionCalls,
        ErrorHandlingConfig? errorHandlingConfig = null,
        IList<AITool>? serverConfiguredTools = null,
        AgenticLoopConfig? agenticLoopConfig = null,
        string agentName = "Agent",
        IReadOnlyDictionary<string, MiddlewareStateFactory>? stateFactories = null)
    {
        _eventCoordinator = eventCoordinator ?? throw new ArgumentNullException(nameof(eventCoordinator));
        _middlewarePipeline = middlewarePipeline ?? throw new ArgumentNullException(nameof(middlewarePipeline));
        _functionExecutionCore = functionExecutionCore ?? throw new ArgumentNullException(nameof(functionExecutionCore));
        _errorHandlingConfig = errorHandlingConfig;
        _serverConfiguredTools = serverConfiguredTools;
        _agenticLoopConfig = agenticLoopConfig;
        _agentName = agentName;
        _stateFactories = stateFactories ?? ImmutableDictionary<string, MiddlewareStateFactory>.Empty;
    }

    // Helpers moved here from FunctionMapBuilder to keep lookup logic next to caller
    private static Dictionary<string, AIFunction>? BuildMergedMap(
        IList<AITool>? serverTools,
        IList<AITool>? requestTools) =>
        FunctionExecutionCore.BuildMergedMap(serverTools, requestTools);

    private static AIFunction? FindFunction(
        string name,
        Dictionary<string, AIFunction>? map) =>
        FunctionExecutionCore.FindFunction(name, map);

    /// <summary>
    /// Checks if a function by name is an output tool (structured output tool mode).
    /// </summary>
    public bool IsOutputToolByName(string? functionName, IList<AITool>? tools)
    {
        return _functionExecutionCore.IsOutputToolByName(functionName, tools);
    }

    /// <summary>
    /// Checks if a function is an output tool (structured output tool mode).
    /// Output tools are never executed - their arguments ARE the structured output.
    /// </summary>
    private static bool IsOutputTool(AIFunction? function)
    {
        return FunctionExecutionCore.IsOutputTool(function);
    }

    /// <summary>
    /// Gets the toolharness name for a function from its metadata.
    /// Used by Agent class for event emission.
    /// </summary>
    public string? LookupToolHarnessName(string? functionName, IList<AITool>? tools)
    {
        return _functionExecutionCore.LookupToolHarnessName(functionName, tools);
    }

    /// <summary>
    /// Gets the ToolCallType for a function from its AdditionalProperties["CapabilityType"].
    /// Used by Agent class for event emission.
    /// </summary>
    public ToolCallType? LookupToolCallType(string? functionName, IList<AITool>? tools)
    {
        return _functionExecutionCore.LookupToolCallType(functionName, tools);
    }

    /// <summary>
    /// Executes function calls with automatic routing between sequential/parallel execution.
    /// Handles container detection, permission checking, and result aggregation.
    /// This is the new consolidated API that replaces ToolScheduler.
    /// </summary>
    /// <param name="currentHistory">Current conversation messages</param>
    /// <param name="toolRequests">Function calls to execute</param>
    /// <param name="options">Chat options containing tool definitions</param>
    /// <param name="agentLoopState">Current agent loop state</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Structured result with message, expansions, and successful functions</returns>
    public async Task<ToolExecutionResult> ExecuteToolsAsync(
        List<ChatMessage> currentHistory,
        List<FunctionCallContent> toolRequests,
        ChatOptions? options,
        AgentLoopState agentLoopState,
        AgentRunConfig runConfig,
        Middleware.AgentContext agentContext,
        CancellationToken cancellationToken)
    {
        // Check if any tool request is an output tool (structured output termination)
        var functionMap = BuildMergedMap(_serverConfiguredTools, options?.Tools);
        bool outputToolCalled = toolRequests.Any(tr =>
            !string.IsNullOrEmpty(tr.Name) && IsOutputTool(FindFunction(tr.Name, functionMap)));

        // Route to appropriate execution strategy
        // For single tool calls, inline execution (no parallel overhead)
        if (toolRequests.Count <= 1)
        {
            var result = await ExecuteSequentiallyAsync(
                currentHistory, toolRequests, options, agentLoopState, runConfig, agentContext,
                cancellationToken).ConfigureAwait(false);
            return result with { OutputToolCalled = outputToolCalled };
        }

        // For multiple tools, use parallel execution with throttling
        var parallelResult = await ExecuteInParallelAsync(
            currentHistory, toolRequests, options, agentLoopState, runConfig, agentContext,
            cancellationToken).ConfigureAwait(false);
        return parallelResult with { OutputToolCalled = outputToolCalled };
    }

    /// <summary>
    /// Executes tools sequentially (used for single tools).
    /// No permission duplication - checks once per tool.
    /// </summary>
    private async Task<ToolExecutionResult> ExecuteSequentiallyAsync(
        List<ChatMessage> currentHistory,
        List<FunctionCallContent> toolRequests,
        ChatOptions? options,
        AgentLoopState agentLoopState,
        AgentRunConfig runConfig,
        Middleware.AgentContext agentContext,
        CancellationToken cancellationToken)
    {
        var allContents = new List<AIContent>();
        var resultPayloads = new Dictionary<string, ToolResultPayload>(StringComparer.Ordinal);
        var batchId = Guid.NewGuid().ToString("N");

        for (var i = 0; i < toolRequests.Count; i++)
        {
            var toolRequest = toolRequests[i];
            if (string.IsNullOrEmpty(toolRequest.Name))
                continue;

            var invocation = new ToolInvocationInfo(
                batchId,
                toolRequest.CallId,
                toolRequest.Name,
                i);

            var preparation = await _functionExecutionCore.PrepareFunctionAsync(
                toolRequest,
                options,
                runConfig,
                agentContext,
                invocation,
                cancellationToken).ConfigureAwait(false);

            var outcome = preparation.ImmediateOutcome;
            if (outcome is null)
            {
                var bodyResult = await _functionExecutionCore.ExecuteFunctionBodyAsync(
                    preparation,
                    agentContext,
                    cancellationToken).ConfigureAwait(false);

                outcome = await _functionExecutionCore.CompleteFunctionAsync(
                    bodyResult,
                    runConfig,
                    agentContext,
                    cancellationToken).ConfigureAwait(false);
            }

            if (outcome.ShouldTerminate)
                break;

            if (outcome.WasOutputTool)
                continue;

            var functionResult = FunctionExecutionCore.ToFunctionResultContent(outcome);
            allContents.Add(functionResult);
            resultPayloads[outcome.CallId] = outcome.ResultPayload;
        }

        // Extract successful functions
        var successfulFunctions = ExtractSuccessfulFunctions(allContents, toolRequests);

        return new ToolExecutionResult(
            new ChatMessage(ChatRole.Tool, allContents),
            successfulFunctions,
            resultPayloads);
    }

    /// <summary>
    /// Executes tools in parallel with throttling.
    /// Permission checking is handled individually per tool via middleware pipeline.
    /// </summary>
    private async Task<ToolExecutionResult> ExecuteInParallelAsync(
        List<ChatMessage> currentHistory,
        List<FunctionCallContent> toolRequests,
        ChatOptions? options,
        AgentLoopState agentLoopState,
        AgentRunConfig runConfig,
        Middleware.AgentContext agentContext,
        CancellationToken cancellationToken)
    {
        // Assign stable model-order invocation identities before authority preparation.
        var batchId = Guid.NewGuid().ToString("N");
        var invocationByCallId = new Dictionary<string, ToolInvocationInfo>(StringComparer.Ordinal);

        for (var i = 0; i < toolRequests.Count; i++)
        {
            var toolRequest = toolRequests[i];
            if (string.IsNullOrEmpty(toolRequest.Name))
                continue;

            var invocation = new ToolInvocationInfo(
                batchId,
                toolRequest.CallId,
                toolRequest.Name,
                i);
            invocationByCallId[toolRequest.CallId] = invocation;

        }

        // Constructor-free authority preparation completes in model order before batch mediation.
        var preparations = new List<FunctionExecutionPreparation>(toolRequests.Count);
        foreach (var toolRequest in toolRequests)
        {
            var invocation = invocationByCallId.GetValueOrDefault(toolRequest.CallId)
                ?? new ToolInvocationInfo(batchId, toolRequest.CallId, toolRequest.Name, preparations.Count);
            var preparation = await _functionExecutionCore.PrepareFunctionAsync(
                toolRequest,
                options,
                runConfig,
                agentContext,
                invocation,
                cancellationToken,
                admit: false).ConfigureAwait(false);
            preparations.Add(preparation);
            if (preparation.ImmediateOutcome?.ShouldTerminate == true)
                break;
        }

        var parallelFunctions = preparations
            .Where(static preparation => preparation.Function is not null && preparation.ImmediateOutcome is null)
            .Select(static preparation => new ParallelFunctionInfo(
                preparation.Function!,
                preparation.FunctionCall.CallId,
                preparation.Arguments,
                preparation.Invocation,
                preparation.ResolvedInvocation))
            .ToArray();

        var batchContext = agentContext.AsBeforeParallelBatch(
            parallelFunctions,
            runConfig);

        var beforeParallelBatchHookState = agentContext.State.MiddlewareState;
        await agentContext.PublishAsync(Agent.BuildMiddlewareStateSnapshotEvent(
            agentName: _agentName,
            stateFactories: _stateFactories,
            state: beforeParallelBatchHookState,
            sessionId: agentContext.Session?.Id,
            threadId: agentContext.Thread?.Id,
            iteration: agentContext.State.Iteration,
            phase: "before_parallel_batch",
            batchId: batchId,
            functionCallId: null,
            toolCallIndex: null));

        // Execute BeforeParallelBatchAsync middleware hooks on the authoritative turn context.
        await _middlewarePipeline.ExecuteBeforeParallelBatchAsync(
            batchContext, cancellationToken).ConfigureAwait(false);

        agentLoopState = agentContext.State;
        await Agent.EmitMiddlewareStateChangedAsync(
            agentName: _agentName,
            stateFactories: _stateFactories,
            agentContext,
            beforeParallelBatchHookState,
            agentLoopState.MiddlewareState,
            phase: "before_parallel_batch",
            batchId: batchId,
            functionCallId: null,
            toolCallIndex: null);

        var beforeParallelExecutionState = agentLoopState.MiddlewareState;

        // Admission reuses the exact authority-prepared facts and runs serially in model order.
        for (var index = 0; index < preparations.Count; index++)
        {
            preparations[index] = await _functionExecutionCore.AdmitPreparedFunctionAsync(
                preparations[index], cancellationToken).ConfigureAwait(false);
            if (preparations[index].ImmediateOutcome?.ShouldTerminate == true)
                break;
        }

        // Function bodies and WrapFunctionCall middleware may run in parallel because they only receive
        // FunctionRequest and the narrow FunctionExecutionContext, not a live mutable HookContext.
        var maxParallel = _agenticLoopConfig?.MaxParallelFunctions ?? System.Environment.ProcessorCount * 4;
        using var semaphore = new SemaphoreSlim(maxParallel);

        var bodyTasks = preparations
            .Select((preparation, index) => (Preparation: preparation, Index: index))
            .Where(item => item.Preparation.ImmediateOutcome is null)
            .Select(async item =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var result = await _functionExecutionCore.ExecuteFunctionBodyAsync(
                    item.Preparation,
                    agentContext,
                    cancellationToken).ConfigureAwait(false);
                return (item.Index, Result: result);
            }
            catch (Exception ex)
            {
                var result = new FunctionBodyExecutionResult(
                    item.Preparation,
                    $"Error executing tool: {ex.Message}",
                    new ToolResultMetadata(),
                    ex);
                return (item.Index, Result: result);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        var bodyResultsByIndex = new Dictionary<int, FunctionBodyExecutionResult>();
        foreach (var completed in await Task.WhenAll(bodyTasks).ConfigureAwait(false))
        {
            bodyResultsByIndex[completed.Index] = completed.Result;
        }

        // AfterFunction runs serially in model order. This is the state commit phase.
        var allContents = new List<AIContent>();
        var resultPayloads = new Dictionary<string, ToolResultPayload>(StringComparer.Ordinal);
        for (var i = 0; i < preparations.Count; i++)
        {
            var preparation = preparations[i];
            var outcome = preparation.ImmediateOutcome;

            if (outcome is null)
            {
                var bodyResult = bodyResultsByIndex[i];
                outcome = await _functionExecutionCore.CompleteFunctionAsync(
                    bodyResult,
                    runConfig,
                    agentContext,
                    cancellationToken).ConfigureAwait(false);
            }

            if (outcome.ShouldTerminate)
                break;

            if (outcome.WasOutputTool)
                continue;

            allContents.Add(FunctionExecutionCore.ToFunctionResultContent(outcome));
            resultPayloads[outcome.CallId] = outcome.ResultPayload;
        }

        // Extract successful functions
        var successfulFunctions = ExtractSuccessfulFunctions(allContents, toolRequests);

        var afterParallelExecutionState = agentContext.State.MiddlewareState;
        await agentContext.PublishAsync(Agent.BuildMiddlewareStateSnapshotEvent(
            agentName: _agentName,
            stateFactories: _stateFactories,
            state: afterParallelExecutionState,
            sessionId: agentContext.Session?.Id,
            threadId: agentContext.Thread?.Id,
            iteration: agentContext.State.Iteration,
            phase: "after_parallel_batch",
            batchId: batchId,
            functionCallId: null,
            toolCallIndex: null));
        await Agent.EmitMiddlewareStateChangedAsync(
            agentName: _agentName,
            stateFactories: _stateFactories,
            agentContext,
            beforeParallelExecutionState,
            afterParallelExecutionState,
            phase: "after_parallel_batch",
            batchId: batchId,
            functionCallId: null,
            toolCallIndex: null);

        return new ToolExecutionResult(
            new ChatMessage(ChatRole.Tool, allContents),
            successfulFunctions,
            resultPayloads);
    }

    /// <summary>
    /// Extracts successful function names from execution results.
    /// Only includes functions that completed without errors.
    /// </summary>
    private static HashSet<string> ExtractSuccessfulFunctions(
        IList<AIContent> resultContents,
        IList<FunctionCallContent> toolRequests)
    {
        var successful = new HashSet<string>();

        foreach (var content in resultContents)
        {
            if (content is FunctionResultContent frc && IsFunctionResultSuccessful(frc))
            {
                // Find the tool name from the original request
                foreach (var toolRequest in toolRequests)
                {
                    if (toolRequest.CallId == frc.CallId && !string.IsNullOrEmpty(toolRequest.Name))
                    {
                        successful.Add(toolRequest.Name);
                        break;
                    }
                }
            }
        }

        return successful;
    }

    /// <summary>
    /// Determines if a function result indicates success.
    /// Checks for exceptions and error-like result strings.
    /// </summary>
    private static bool IsFunctionResultSuccessful(FunctionResultContent result)
    {
        // Exception present = failure
        if (result.Exception != null)
            return false;

        // Check if result looks like an error message
        var resultStr = result.Result?.ToString();
        return !IsLikelyErrorString(resultStr);
    }

    /// <summary>
    /// Heuristic to detect error strings in function results.
    /// Mirrors the error detection logic used in Agent.
    /// </summary>
    private static bool IsLikelyErrorString(string? s) =>
        !string.IsNullOrEmpty(s) &&
        (s.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ||
         s.StartsWith("Failed:", StringComparison.OrdinalIgnoreCase) ||
         s.Contains("exception occurred", StringComparison.OrdinalIgnoreCase) ||
         s.Contains("unhandled exception", StringComparison.OrdinalIgnoreCase) ||
         s.Contains("exception was thrown", StringComparison.OrdinalIgnoreCase) ||
         s.Contains("rate limit exceeded", StringComparison.OrdinalIgnoreCase) ||
         s.Contains("rate limited", StringComparison.OrdinalIgnoreCase) ||
         s.Contains("quota exceeded", StringComparison.OrdinalIgnoreCase) ||
         s.Contains("quota reached", StringComparison.OrdinalIgnoreCase) ||
         s.Contains("timeout", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Formats an exception for inclusion in function results sent to the LLM.
    /// Respects IncludeDetailedErrorsInChat security setting.
    /// </summary>
    private string FormatErrorForLLM(Exception exception, string functionName)
    {
        if (_errorHandlingConfig?.IncludeDetailedErrorsInChat == true)
        {
            // Include full exception details (potential security risk)
            return $"Error invoking function '{functionName}': {exception.Message}";
        }
        else
        {
            // Generic error message (safe for LLM consumption)
            // Full exception still available via FunctionResultContent.Exception
            return $"Error: Function '{functionName}' failed.";
        }
    }



    /// <summary>
    /// Prepares the various chat message lists after a response from the inner client and before invoking functions
    /// </summary>
}

#endregion

#region PreparedTurn

/// <summary>
/// Encapsulates all prepared state for a single agent turn.
/// Separates message preparation (functional, pure) from execution (I/O, stateful).
/// </summary>
internal record PreparedTurn
{
    /// <summary>Gets the immutable capability-catalog lease pinned for this complete turn.</summary>
    internal AgentCapabilityLease? CatalogLease { get; init; }

    /// <summary>
    /// Prepared thread history and new input for the first iteration.
    /// Iteration middleware may project this list before provider invocation.
    /// </summary>
    public required IReadOnlyList<ChatMessage> MessagesForLLM { get; init; }

    /// <summary>
    /// NEW input messages only (what the caller provided).
    /// Used for persistence - these are the messages to add to session history.
    /// </summary>
    public required IReadOnlyList<ChatMessage> NewInputMessages { get; init; }

    /// <summary>
    /// Final chat options after merging defaults, applying Middlewares, and adding system instructions.
    /// </summary>
    public ChatOptions? Options { get; init; }
}

#endregion

#region MessageProcessor

/// <summary>
/// Handles all pre-processing of chat messages and options before sending to the LLM.
/// </summary>
internal class MessageProcessor
{
    private readonly string? _systemInstructions;
    private ChatOptions? _defaultOptions;
    private readonly object _optionsLock = new();

    public MessageProcessor(
        string? systemInstructions,
        ChatOptions? defaultOptions)
    {
        _systemInstructions = systemInstructions;
        _defaultOptions = defaultOptions;
    }

    /// <summary>
    /// Gets the system instructions configured for this processor.
    /// </summary>
    public string? SystemInstructions => _systemInstructions;

    /// <summary>
    /// Gets the default chat options configured for this processor.
    /// </summary>
    public ChatOptions? DefaultOptions => _defaultOptions;

    internal void ReplaceCapabilityFunctions(IEnumerable<AIFunction> functions)
    {
        ArgumentNullException.ThrowIfNull(functions);
        lock (_optionsLock)
        {
            var replacement = _defaultOptions?.Clone() ?? new ChatOptions();
            var retained = replacement.Tools?
                .Where(tool => tool is not AIFunction function ||
                    function.AdditionalProperties?.ContainsKey(
                        HPDCapabilityMetadata.AdditionalPropertiesKey) != true)
                .ToList() ?? [];
            retained.AddRange(functions);
            replacement.Tools = retained;
            _defaultOptions = replacement;
        }
    }

    /// <summary>
    /// Prepares a complete turn for execution.
    /// Loads thread history, merges options, and adds system instructions.
    /// </summary>
    /// <param name="thread">Thread containing conversation history (null for stateless execution).</param>
    /// <param name="inputMessages">NEW messages from the caller (to be added to history).</param>
    /// <param name="options">Chat options to merge with defaults.</param>
    /// <param name="agentName">Agent name for logging/Middlewareing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>PreparedTurn with all state needed for execution.</returns>
    public async Task<PreparedTurn> PrepareTurnAsync(
        Thread? thread,
        IEnumerable<ChatMessage> inputMessages,
        ChatOptions? options,
        string agentName,
        CancellationToken cancellationToken,
        IReadOnlyList<AIFunction>? pinnedCapabilities = null)
    {
        var inputMessagesList = inputMessages.ToList();
        var messagesForLLM = new List<ChatMessage>();

        // STEP 1: Load thread history
        if (thread != null)
        {
            messagesForLLM.AddRange(thread.Messages);
        }

        // STEP 2: Add new input messages
        messagesForLLM.AddRange(inputMessagesList);

        // STEP 3: Merge options and add system instructions
        // Every turn owns its options snapshot. Catalog pinning and visibility middleware mutate
        // this object, so sharing the builder defaults would allow reloads or concurrent turns to
        // change the capabilities advertised by an in-flight response.
        var effectiveOptions = MergeOptions(options)?.Clone();
        if (pinnedCapabilities is not null)
        {
            effectiveOptions ??= new ChatOptions();
            effectiveOptions.Tools ??= [];
            for (var index = effectiveOptions.Tools.Count - 1; index >= 0; index--)
            {
                if (effectiveOptions.Tools[index] is AIFunction function &&
                    function.AdditionalProperties?.ContainsKey(
                        HPDCapabilityMetadata.AdditionalPropertiesKey) == true)
                {
                    effectiveOptions.Tools.RemoveAt(index);
                }
            }
            foreach (var function in pinnedCapabilities)
                effectiveOptions.Tools.Add(function);
        }

        // Add system instructions to ChatOptions.Instructions (Microsoft's pattern)
        // This follows the official Microsoft.Extensions.AI pattern used by ChatClientAgent
        if (!string.IsNullOrEmpty(_systemInstructions))
        {
            effectiveOptions ??= new ChatOptions();

            // Avoid duplicate injection if system instructions already present
            if (string.IsNullOrWhiteSpace(effectiveOptions.Instructions))
            {
                effectiveOptions.Instructions = _systemInstructions;
            }
            else if (!effectiveOptions.Instructions.Contains(_systemInstructions))
            {
                effectiveOptions.Instructions = $"{_systemInstructions}\n{effectiveOptions.Instructions}";
            }
        }

        // STEP 4: Apply prompt Middlewares
        var preparedMessages = await ApplyPromptMiddlewaresAsync(
            messagesForLLM,
            effectiveOptions,
            agentName,
            cancellationToken).ConfigureAwait(false);

        // STEP 5: Return PreparedTurn
        return new PreparedTurn
        {
            MessagesForLLM = preparedMessages.ToList(),
            NewInputMessages = inputMessagesList,
            Options = effectiveOptions
        };
    }

    /// <summary>
    /// Merges provided options with default options.
    /// </summary>
    private ChatOptions? MergeOptions(ChatOptions? providedOptions)
    {
        ChatOptions? defaults;
        lock (_optionsLock)
            defaults = _defaultOptions;
        if (defaults == null)
            return providedOptions;

        if (providedOptions == null)
            return defaults;

        var merged = defaults.Clone();
        merged.Tools = (providedOptions.Tools is { Count: > 0 })
            ? providedOptions.Tools
            : defaults.Tools;
        merged.ToolMode = providedOptions.ToolMode ?? defaults.ToolMode;
        merged.AllowMultipleToolCalls = providedOptions.AllowMultipleToolCalls ?? defaults.AllowMultipleToolCalls;
        merged.MaxOutputTokens = providedOptions.MaxOutputTokens ?? defaults.MaxOutputTokens;
        merged.Temperature = providedOptions.Temperature ?? defaults.Temperature;
        merged.TopP = providedOptions.TopP ?? defaults.TopP;
        merged.TopK = providedOptions.TopK ?? defaults.TopK;
        merged.FrequencyPenalty = providedOptions.FrequencyPenalty ?? defaults.FrequencyPenalty;
        merged.PresencePenalty = providedOptions.PresencePenalty ?? defaults.PresencePenalty;
        merged.ResponseFormat = providedOptions.ResponseFormat ?? defaults.ResponseFormat;
        merged.Reasoning = providedOptions.Reasoning ?? defaults.Reasoning;
        merged.Seed = providedOptions.Seed ?? defaults.Seed;
        merged.StopSequences = providedOptions.StopSequences ?? defaults.StopSequences;
        merged.ModelId = providedOptions.ModelId ?? defaults.ModelId;
        merged.Instructions = providedOptions.Instructions ?? defaults.Instructions;
        merged.ConversationId = providedOptions.ConversationId ?? defaults.ConversationId;
        merged.AllowBackgroundResponses = providedOptions.AllowBackgroundResponses ?? defaults.AllowBackgroundResponses;
        merged.ContinuationToken = providedOptions.ContinuationToken ?? defaults.ContinuationToken;
        merged.RawRepresentationFactory = providedOptions.RawRepresentationFactory ?? defaults.RawRepresentationFactory;
        merged.AdditionalProperties = MergeDictionaries(defaults.AdditionalProperties, providedOptions.AdditionalProperties);
        return merged;
    }

    /// <summary>
    /// Applies the registered prompt middlewares pipeline.
    /// NOTE: This is now a no-op - prompt middleware is handled via the unified AgentMiddlewarePipeline.
    /// </summary>
    private Task<IEnumerable<ChatMessage>> ApplyPromptMiddlewaresAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        string agentName,
        CancellationToken cancellationToken)
    {
        // Prompt middlewares are now handled via BeforeMessageTurnAsync in the unified pipeline
        return Task.FromResult(messages);
    }

    /// <summary>
    /// Applies post-invocation middlewares to process results, extract memories, etc.
    /// NOTE: This is now a no-op - post-invoke middleware is handled via the unified AgentMiddlewarePipeline.
    /// </summary>
    public Task ApplyPostInvokeMiddlewaresAsync(
        IEnumerable<ChatMessage> requestMessages,
        IEnumerable<ChatMessage>? responseMessages,
        Exception? exception,
        ChatOptions? options,
        string agentName,
        CancellationToken cancellationToken)
    {
        // Post-invoke middlewares are now handled via AfterMessageTurnAsync in the unified pipeline
        return Task.CompletedTask;
    }

    /// <summary>
    /// Merges two dictionaries, with the second taking precedence.
    /// </summary>
    private static AdditionalPropertiesDictionary? MergeDictionaries(
        AdditionalPropertiesDictionary? first,
        AdditionalPropertiesDictionary? second)
    {
        if (first == null) return second;
        if (second == null) return first;

        var merged = new AdditionalPropertiesDictionary(first);
        foreach (var kvp in second)
        {
            merged[kvp.Key] = kvp.Value;
        }
        return merged;
    }
}

#endregion

#region AgentTurn
/// <summary>
/// Manages a single streaming call to the LLM and translates the raw output into TurnEvents
/// </summary>
internal class AgentTurn
{
    private readonly IChatClient? _baseClient;
    private readonly Action<ChatOptions>? _configureOptions;
    private readonly List<Func<IChatClient, IServiceProvider?, IChatClient>>? _middleware;
    private readonly IServiceProvider? _serviceProvider;

    /// <summary>
    /// The ConversationId from the most recent response (if the service manages history server-side).
    /// Null if the service doesn't track conversation history.
    /// </summary>
    public string? LastResponseConversationId { get; private set; }

    /// <summary>
    /// Initializes a new instance of AgentTurn
    /// </summary>
    /// <param name="baseClient">The underlying chat client to use for LLM calls</param>
    /// <param name="configureOptions">Optional callback to configure options before each LLM call</param>
    /// <param name="middleware">Optional middleware to wrap the client dynamically on each request</param>
    /// <param name="serviceProvider">Optional service provider for middleware dependency injection</param>
    public AgentTurn(
        IChatClient? baseClient,
        Action<ChatOptions>? configureOptions = null,
        List<Func<IChatClient, IServiceProvider?, IChatClient>>? middleware = null,
        IServiceProvider? serviceProvider = null)
    {
        _baseClient = baseClient;
        _configureOptions = configureOptions;
        _middleware = middleware;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Runs a single turn with the LLM and yields ChatResponseUpdates representing the response.
    /// Captures ConversationId from the response for server-side history tracking optimization.
    /// </summary>
    /// <param name="messages">The conversation history to send to the LLM</param>
    /// <param name="options">Optional chat options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stream of ChatResponseUpdates representing the LLM's response</returns>
    public async IAsyncEnumerable<ChatResponseUpdate> RunAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Reset ConversationId at start of new turn
        LastResponseConversationId = null;

        await foreach (var update in RunAsyncCore(messages, options, overrideClient: null, cancellationToken))
        {
            // Capture ConversationId from first update that has one
            if (LastResponseConversationId == null && update.ConversationId != null)
            {
                LastResponseConversationId = update.ConversationId;
            }

            yield return update;
        }
    }

    /// <summary>
    /// Runs a single turn with an optional override client for runtime provider switching.
    /// </summary>
    /// <param name="messages">The conversation history to send to the LLM</param>
    /// <param name="options">Optional chat options</param>
    /// <param name="overrideClient">Optional override client (for AgentRunConfig provider switching)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stream of ChatResponseUpdates representing the LLM's response</returns>
    public async IAsyncEnumerable<ChatResponseUpdate> RunAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        IChatClient? overrideClient,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Reset ConversationId at start of new turn
        LastResponseConversationId = null;

        await foreach (var update in RunAsyncCore(messages, options, overrideClient, cancellationToken))
        {
            // Capture ConversationId from first update that has one
            if (LastResponseConversationId == null && update.ConversationId != null)
            {
                LastResponseConversationId = update.ConversationId;
            }

            yield return update;
        }
    }

    private async IAsyncEnumerable<ChatResponseUpdate> RunAsyncCore(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        IChatClient? overrideClient,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Apply runtime options configuration callback if configured
        if (_configureOptions != null && options != null)
        {
            _configureOptions(options);
        }

        // Apply middleware dynamically (if any)
        // This allows runtime provider switching - new providers automatically get wrapped
        // Use override client if provided (from AgentRunConfig), otherwise use base client
        var effectiveClient = overrideClient ?? _baseClient;
        if (effectiveClient == null)
        {
            throw new InvalidOperationException(
                "No chat model is configured for this agent run. Configure Clients.Chat on AgentConfig or AgentRunConfig, including Clients.Chat.Override when supplying a client directly.");
        }

        if (_middleware != null && _middleware.Count > 0)
        {
            for (var i = _middleware.Count - 1; i >= 0; i--)
            {
                var mw = _middleware[i];
                effectiveClient = mw(effectiveClient, _serviceProvider);
                if (effectiveClient == null)
                {
                    throw new InvalidOperationException("Chat client middleware returned null");
                }
            }
        }

        // Get the streaming response from the effective client (base or wrapped)
        var stream = effectiveClient.GetStreamingResponseAsync(messages, options, cancellationToken);

        IAsyncEnumerator<ChatResponseUpdate>? enumerator = null;
        Exception? errorToYield = null;

        try
        {
            enumerator = stream.GetAsyncEnumerator(cancellationToken);

            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    errorToYield = ex;
                    break;
                }

                if (!hasNext)
                    break;

                var update = enumerator.Current;

                // Yield the update directly
                yield return update;
            }
        }
        finally
        {
            if (enumerator != null)
            {
                try
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Ignore disposal errors
                }
            }
        }

        // If there was an error, throw it after cleanup
        if (errorToYield != null)
        {
            throw errorToYield;
        }
    }
}

#endregion

#region Error Formatting Helper

/// <summary>
/// Helper class for formatting detailed error messages with provider-specific information.
/// </summary>
internal static class ErrorFormatter
{
    /// <summary>
    /// Formats an exception with detailed error information for display to users.
    /// Extracts provider-specific error details using the error handler.
    /// </summary>
    internal static string FormatDetailedError(Exception ex, ErrorHandling.IProviderErrorHandler? errorHandler)
    {
        var sb = new StringBuilder();

        // Try to get provider-specific error details
        var providerDetails = errorHandler?.ParseError(ex);

        if (providerDetails != null)
        {
            // Use structured error information from provider
            sb.AppendLine($"[{providerDetails.Category}] {providerDetails.Message}");

            if (providerDetails.StatusCode.HasValue)
                sb.AppendLine($"HTTP Status: {providerDetails.StatusCode}");

            if (!string.IsNullOrEmpty(providerDetails.ErrorCode))
                sb.AppendLine($"Error Code: {providerDetails.ErrorCode}");

            if (!string.IsNullOrEmpty(providerDetails.ErrorType))
                sb.AppendLine($"Error Type: {providerDetails.ErrorType}");

            if (!string.IsNullOrEmpty(providerDetails.RequestId))
                sb.AppendLine($"Request ID: {providerDetails.RequestId}");

            if (providerDetails.RetryAfter.HasValue)
                sb.AppendLine($"Retry After: {providerDetails.RetryAfter.Value.TotalSeconds:F1}s");

            if (providerDetails.RawDetails != null && providerDetails.RawDetails.Count > 0)
            {
                foreach (var kvp in providerDetails.RawDetails)
                {
                    sb.AppendLine($"{kvp.Key}: {kvp.Value}");
                }
            }
        }
        else
        {
            // Fallback to basic error message with exception type
            sb.AppendLine($"[{ex.GetType().Name}] {ex.Message}");

            if (ex is HttpRequestException httpEx && httpEx.StatusCode.HasValue)
                sb.AppendLine($"HTTP Status: {(int)httpEx.StatusCode}");
        }

        // Add inner exception if present
        if (ex.InnerException != null)
        {
            sb.AppendLine($"Inner Exception: {ex.InnerException.Message}");
        }

        return sb.ToString().TrimEnd();
    }
}

#endregion

#region Tool Execution Result Types
/// <summary>
/// Structured result from tool execution, replacing the 5-tuple return type.
/// Provides strongly-typed access to execution outcomes.
/// </summary>
internal record ToolExecutionResult(
    ChatMessage Message,
    HashSet<string> SuccessfulFunctions,
    IReadOnlyDictionary<string, ToolResultPayload> ResultPayloads,
    bool OutputToolCalled = false);

#endregion
