using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

internal sealed class AgentRuntimeFunctionExecutor : IRuntimeFunctionExecutor
{
    private readonly string _agentName;
    private readonly IChatClient? _baseClient;
    private readonly IServiceProvider? _serviceProvider;
    private readonly AgentConfig? _config;
    private readonly AgentChatClientResolver _chatClientResolver;
    private readonly AgentChatClientHandle? _defaultChatClient;
    private readonly MessageProcessor _messageProcessor;
    private readonly FunctionExecutionCore _functionExecutionCore;
    private readonly Middleware.AgentRuntimeContext _runtimeContext;
    private readonly HPD.Events.IEventCoordinator _eventCoordinator;

    public AgentRuntimeFunctionExecutor(
        string agentName,
        IChatClient? baseClient,
        IServiceProvider? serviceProvider,
        AgentConfig? config,
        AgentChatClientResolver chatClientResolver,
        AgentChatClientHandle? defaultChatClient,
        MessageProcessor messageProcessor,
        FunctionExecutionCore functionExecutionCore,
        Middleware.AgentRuntimeContext runtimeContext,
        HPD.Events.IEventCoordinator eventCoordinator)
    {
        _agentName = agentName ?? throw new ArgumentNullException(nameof(agentName));
        _baseClient = baseClient;
        _serviceProvider = serviceProvider;
        _config = config;
        _chatClientResolver = chatClientResolver;
        _defaultChatClient = defaultChatClient;
        _messageProcessor = messageProcessor ?? throw new ArgumentNullException(nameof(messageProcessor));
        _functionExecutionCore = functionExecutionCore ?? throw new ArgumentNullException(nameof(functionExecutionCore));
        _runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
        _eventCoordinator = eventCoordinator ?? throw new ArgumentNullException(nameof(eventCoordinator));
    }

    public async Task<IReadOnlyList<RuntimeFunctionExecutionResult>> ExecuteFunctionCallsAsync(
        IReadOnlyList<FunctionCallContent> functionCalls,
        AgentRunConfig? runConfig = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(functionCalls);

        var calls = functionCalls
            .Where(static call => !string.IsNullOrWhiteSpace(call.Name))
            .ToList();
        if (calls.Count == 0)
        {
            return Array.Empty<RuntimeFunctionExecutionResult>();
        }

        var effectiveRunConfig = runConfig ?? _runtimeContext.RunConfig ?? new AgentRunConfig();
        await using var chatLease = HasChatResolutionSource(effectiveRunConfig)
            ? await _chatClientResolver.ResolveAsync(
                new AgentChatClientResolutionRequest
                {
                    AgentConfig = _config ?? throw new InvalidOperationException("Agent configuration is not available."),
                    RunConfig = effectiveRunConfig,
                    AgentDefault = _defaultChatClient
                },
                cancellationToken).ConfigureAwait(false)
            : null;
        var effectiveOptions = effectiveRunConfig.Clients.Chat?.MergeWith(_messageProcessor.DefaultOptions)
            ?? _messageProcessor.DefaultOptions
            ?? new ChatOptions();
        var messages = new List<ChatMessage>();
        var runId = Guid.NewGuid().ToString("N");
        var state = AgentLoopState.Initial(
            messages,
            runId,
            _runtimeContext.RuntimeId,
            _agentName);

        var agentContext = new Middleware.AgentContext(
            agentName: _agentName,
            conversationId: _runtimeContext.RuntimeId,
            initialState: state,
            eventCoordinator: _eventCoordinator,
            threadEvents: _runtimeContext.ThreadEvents,
            session: null,
            thread: null,
            cancellationToken: cancellationToken,
            effectiveChatClient: chatLease?.Handle,
            chatClientResolver: _chatClientResolver,
            services: _serviceProvider,
            runtimeCapabilities: _runtimeContext.RuntimeCapabilities,
            traceId: null,
            parentAgentStore: _config?.AgentStore,
            config: _config,
            clientSet: _runtimeContext.ClientSet,
            contentStore: _runtimeContext.ContentStore,
            structEvents: _runtimeContext.StructEvents,
            inputHandler: async (input, ct) =>
                await _runtimeContext.RunAsync(input, ct).ConfigureAwait(false));

        var results = new List<RuntimeFunctionExecutionResult>(calls.Count);
        var batchId = Guid.NewGuid().ToString("N");
        for (var i = 0; i < calls.Count; i++)
        {
            var call = calls[i];
            var invocation = new ToolInvocationInfo(
                batchId,
                call.CallId,
                call.Name,
                i);

            var outcome = await _functionExecutionCore.ExecuteFunctionAsync(
                    call,
                    effectiveOptions,
                    effectiveRunConfig,
                    agentContext,
                    invocation,
                    cancellationToken)
                .ConfigureAwait(false);

            results.Add(ToRuntimeResult(outcome));

            if (outcome.ShouldTerminate)
            {
                break;
            }
        }

        return results;
    }

    private bool HasChatResolutionSource(AgentRunConfig runConfig)
        => runConfig.Clients.Chat?.Override is not null ||
           runConfig.Clients.Chat is not null ||
           _defaultChatClient is not null;

    private static RuntimeFunctionExecutionResult ToRuntimeResult(FunctionExecutionOutcome outcome)
    {
        return new RuntimeFunctionExecutionResult
        {
            CallId = outcome.CallId,
            FunctionName = outcome.FunctionName,
            Result = outcome.Result,
            Payload = outcome.ResultPayload,
            Exception = outcome.Exception,
            Succeeded = outcome.Exception is null &&
                !outcome.WasBlocked &&
                !outcome.WasUnknown &&
                !outcome.WasOutputTool,
            WasBlocked = outcome.WasBlocked,
            WasUnknown = outcome.WasUnknown,
            WasOutputTool = outcome.WasOutputTool,
            ResultMetadata = outcome.ResultMetadata
        };
    }
}
