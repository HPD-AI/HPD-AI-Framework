using System.Reflection;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.EventComposition.AotFixture;
using HPD.Agent.EventComposition.AotFixtureTwo;
using HPD.Agent.Middleware;
using HPD.Agent.Serialization;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

var applicationIdentity = Assembly.GetExecutingAssembly().GetName().Name!;
if (!AgentEventCompositionHost.TryGetApplication(applicationIdentity, out var composition))
    return 1;
if (!composition.Codec.TryGetByType(typeof(AotFixtureEvent), out var descriptor) ||
    descriptor.Durability != AgentEventDurability.Durable ||
    descriptor.ContentPolicy?.Kind != "aot-fixture")
    return 2;
if (!composition.Codec.TryGetByType(typeof(AotFixtureTwoEvent), out _) ||
    !composition.Codec.TryGetByType(typeof(AotSmokeLocalEvent), out _))
    return 4;

if (args.Length == 2 && string.Equals(args[1], "--continue", StringComparison.Ordinal))
    return await ContinueAfterRestartAsync(Path.GetFullPath(args[0]), composition);

var content = new InMemoryContentStore();
using var coordinator = new HPD.Events.Core.EventCoordinator();
var publisher = new AgentEventPublisher(
    new InMemorySessionStore(composition.Codec),
    coordinator,
    new AgentEventContentArchiver(content));
var retained = new AotFixtureEvent("archived")
{
    EventId = "retained-event",
    SessionId = "aot-session",
    ThreadId = "main"
};
await publisher.PublishLiveAsync(retained);
await publisher.PublishLiveAsync(retained);
if ((await content.QueryAsync(ContentScope.Create("aot-session"))).Count != 1)
    return 5;

_ = new AgentBuilder().WithInMemorySessionStore();
_ = new AgentBuilder().WithFileSessionStore(Path.Combine(Path.GetTempPath(), "hpd-builder-dx"));

var root = args.Length == 1
    ? Path.GetFullPath(args[0])
    : Path.Combine(Path.GetTempPath(), $"hpd-event-aot-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);

var firstStore = new FileSessionStore(root, composition.Codec);
await firstStore.AppendThreadEventAsync(
    "aot-session",
    "main",
    new AotFixtureEvent("persisted"));

var reopenedStore = new FileSessionStore(root, composition.Codec);
var events = await reopenedStore.CollectThreadEventsAsync("aot-session", "main");
if (events?.OfType<AotFixtureEvent>().SingleOrDefault()?.Value != "persisted")
    return 3;

const string subAgentSessionId = "subagent-aot-session";
var durableSession = new Session(subAgentSessionId);
await firstStore.SaveSessionAsync(durableSession);
var parentThread = durableSession.CreateThread("parent-agent", "subagent-parent");
await firstStore.SaveInitialThreadAsync(durableSession.Id, parentThread);
parentThread.Session = durableSession;
durableSession.Store = firstStore;
var policy = SubAgentRunConfig.Inherit().CompilePolicy();
var parent = new ThreadKey(subAgentSessionId, parentThread.Id);
var childRoute = new ThreadKey(parent.SessionId, "worker-thread");
await firstStore.AppendThreadEventAsync(childRoute.SessionId, childRoute.ThreadId,
    new ThreadCreatedEvent(
        "worker-agent", null, null, null, null, DateTime.UtcNow,
        ThreadKind.SubAgent, ThreadVisibility.Hidden,
        parent.SessionId, parent.ThreadId, "worker",
        InvocationId: "invocation",
        ParentToolCallId: "call",
        ContextPolicy: "Fresh"));
await new SubAgentChildRegistry(firstStore).RegisterAsync(parent, new SubAgentChildReference
{
    LocalId = new SubAgentLocalId("worker-1"),
    RoleName = "worker",
    CapabilityId = CapabilityId.Create("aot:worker"),
    ChildAgentId = "worker-agent",
    ChildThread = childRoute,
    CreationContext = SubAgentCreationContext.Fresh,
    CreationInvocationId = "invocation",
    ParentToolCallId = "call",
    ExecutionPolicy = policy,
    CreatedAt = DateTimeOffset.UtcNow
});

var processPath = Environment.ProcessPath;
if (string.IsNullOrWhiteSpace(processPath))
    return 8;
using var restart = Process.Start(new ProcessStartInfo
{
    FileName = processPath,
    UseShellExecute = false,
    ArgumentList = { root, "--continue" }
});
if (restart is null)
    return 8;
await restart.WaitForExitAsync();
return restart.ExitCode;

static async Task<int> ContinueAfterRestartAsync(
    string root,
    AgentEventComposition composition)
{
    const string sessionId = "subagent-aot-session";
    const string parentThreadId = "subagent-parent";
    var restartStore = new FileSessionStore(root, composition.Codec);
    var durableSession = await restartStore.LoadSessionAsync(sessionId);
    var parentThread = await restartStore.ProjectThreadAsync(
        sessionId, parentThreadId, ThreadProjectionPurpose.ThreadHistory);
    if (durableSession is null || parentThread is null)
        return 6;
    durableSession.Store = restartStore;
    parentThread.Session = durableSession;

    var childRoute = new ThreadKey(sessionId, "worker-thread");
    var inheritedClient = new ContinuationClient("current-controller");
    var childDefaultClient = new ContinuationClient("child-builder");
    await using var childAgent = await new AgentBuilder(new AgentConfig
        {
            Name = "worker-agent",
            MaxAgenticIterations = 1
        })
        .WithChatClient(childDefaultClient)
        .WithEventComposition(composition)
        .WithFileSessionStore(root)
        .BuildAsync();
    await using var resolver = new ContinuationResolver(childAgent);
    using var services = new ServiceCollection()
        .AddSingleton<IAgentRuntimeResolver>(resolver)
        .BuildServiceProvider();
    await using var controllerClients = AgentClientSet.ForChat(
        inheritedClient,
        executionIdentity: inheritedClient.Identity);
    var function = AIFunctionFactory.Create(
        (string value) => value,
        new AIFunctionFactoryOptions { Name = SubAgentsFunctionFactory.FunctionName });
    var state = AgentLoopState.InitialSafe([], "run", "conversation", "parent-agent");
    using var eventCoordinator = new HPD.Events.Core.EventCoordinator();
    var agentContext = new AgentContext(
        "parent-agent", "conversation", state, eventCoordinator,
        durableSession, parentThread, CancellationToken.None,
        effectiveChatClient: AgentChatClientHandle.Borrowed(
            inheritedClient, AgentChatClientSource.BuilderDefault,
            executionIdentity: inheritedClient.Identity),
        services: services,
        config: new AgentConfig { Name = "parent-agent" },
        clientSet: controllerClients);
    var before = agentContext.AsBeforeFunction(
        function, "restart-tool-call", new Dictionary<string, object?>(), new AgentRunConfig(), null, null);
    var functionContext = new FunctionExecutionContext(before, new FunctionRequest
    {
        Function = function,
        CallId = "restart-tool-call",
        Arguments = new Dictionary<string, object?>(),
        State = state,
        ResultMetadata = new ToolResultMetadata(),
        EventCoordinator = eventCoordinator
    });
    using var continueJson = JsonDocument.Parse("""{"child":"worker-1","input":"continue after restart"}""");
    var continueResult = await SubAgentRuntime.ControlAsync(
        "continue", continueJson.RootElement, functionContext, CancellationToken.None);
    var operation = continueResult as SubAgentOperationResult;
    var continuedEvents = await restartStore.CollectThreadEventsAsync(childRoute.SessionId, childRoute.ThreadId);
    return operation?.Status == SubAgentOperationStatus.Completed &&
           inheritedClient.CallCount == 1 &&
           childDefaultClient.CallCount == 0 &&
           resolver.LeaseCount == 1 &&
           continuedEvents?.OfType<SubAgentContinuationReceiptEvent>().Any() == true &&
           continuedEvents.OfType<ThreadExecutionFinishedEvent>()
               .Any(value => value.Outcome == ThreadExecutionOutcome.Succeeded) ? 0 : 7;
}

internal sealed class ContinuationClient(string response) : IChatClient
{
    private int _callCount;
    internal int CallCount => Volatile.Read(ref _callCount);
    internal ProviderClientExecutionIdentity Identity { get; } = new()
    {
        ProviderKey = "aot",
        BackendKey = response,
        Family = ProviderClientFamily.Chat,
        ModelName = "smoke",
        OperationAdapterKey = $"aot/{response}/chat",
        UsageSemanticsKey = "aot",
        SafeConfigurationFingerprint = "normalized-by-runtime"
    };

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, response)]));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        Interlocked.Increment(ref _callCount);
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(response)],
            FinishReason = ChatFinishReason.Stop
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(ProviderClientExecutionIdentity)
            ? Identity
            : null;
    public void Dispose() { }
}

internal sealed class ContinuationResolver(Agent agent) : IAgentRuntimeResolver, IAsyncDisposable
{
    private int _leaseCount;
    internal int LeaseCount => Volatile.Read(ref _leaseCount);

    public Task<IAgentRuntimeLease> GetOrBuildAsync(
        string agentId, string sessionId, string threadId,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _leaseCount);
        return Task.FromResult<IAgentRuntimeLease>(new Lease(agent));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed class Lease(Agent value) : IAgentRuntimeLease
    {
        public Agent Agent { get; } = value;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
