using System.Reflection;
using System.Runtime.CompilerServices;
using HPD.Agent;
using HPD.Agent.EventComposition.AotFixture;
using HPD.Agent.EventComposition.AotFixtureTwo;
using HPD.Agent.Serialization;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

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

await using var continuationAgent = await new AgentBuilder(new AgentConfig
    {
        Name = "worker-agent",
        MaxAgenticIterations = 1
    })
    .WithChatClient(new ContinuationClient())
    .WithEventComposition(composition)
    .WithFileSessionStore(root)
    .BuildAsync();
await continuationAgent.CreateSessionAsync("subagent-aot-session");
var policy = SubAgentRunConfig.Inherit().CompilePolicy();
var parent = new ThreadKey("subagent-aot-session", "subagent-parent");
await firstStore.AppendThreadEventAsync(parent.SessionId, parent.ThreadId,
    new SubAgentCreationReservedEvent(new SubAgentCreationRecord
    {
        Key = new SubAgentCreationKey(parent, "call", CapabilityId.Create("aot:worker")),
        Request = new SubAgentCreationRequest
        {
            RoleName = "worker",
            ChildAgentId = "worker-agent",
            Context = SubAgentCreationContext.Fresh,
            InputFingerprint = "input",
            ExecutionPolicy = policy
        },
        LocalId = new SubAgentLocalId("worker-1"),
        ChildThread = new ThreadKey(parent.SessionId, "worker-thread"),
        InvocationId = "invocation",
        ThreadExecutionId = "execution",
        Phase = SubAgentCreationPhase.Reserved,
        Revision = 1,
        CreatedAt = DateTimeOffset.UtcNow
    }));
var replayedPolicy = (await reopenedStore.CollectThreadEventsAsync(parent.SessionId, parent.ThreadId))?
    .OfType<SubAgentCreationReservedEvent>().SingleOrDefault()?.Record.Request.ExecutionPolicy;
if (replayedPolicy != policy)
    return 6;

var childRoute = new ThreadKey(parent.SessionId, "worker-thread");
await firstStore.AppendThreadEventAsync(childRoute.SessionId, childRoute.ThreadId,
    new ThreadCreatedEvent(
        "worker-agent", null, null, null, null, DateTime.UtcNow,
        ThreadKind.SubAgent, ThreadVisibility.Hidden,
        parent.SessionId, parent.ThreadId, "worker",
        InvocationId: "invocation",
        ParentToolCallId: "call",
        ContextPolicy: "Fresh"));
await continuationAgent.RunAsync(new UserMessagesInputEvent
{
    SessionId = childRoute.SessionId,
    ThreadId = childRoute.ThreadId,
    Messages = [new ChatMessage(ChatRole.User, "continue")],
    RunConfig = replayedPolicy.CreateChildRunConfig(childDefaults: continuationAgent.Config)
});
var continuedEvents = await reopenedStore.CollectThreadEventsAsync(childRoute.SessionId, childRoute.ThreadId);
return continuedEvents?.OfType<ThreadExecutionFinishedEvent>()
    .Any(value => value.Outcome == ThreadExecutionOutcome.Succeeded) == true ? 0 : 7;

internal sealed class ContinuationClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) => Task.FromResult(
            new ChatResponse([new ChatMessage(ChatRole.Assistant, "continued")]));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent("continued")],
            FinishReason = ChatFinishReason.Stop
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(ProviderClientExecutionIdentity)
            ? new ProviderClientExecutionIdentity
            {
                ProviderKey = "aot",
                BackendKey = "local",
                Family = ProviderClientFamily.Chat,
                ModelName = "smoke",
                OperationAdapterKey = "aot/local/chat",
                UsageSemanticsKey = "aot",
                SafeConfigurationFingerprint = "normalized-by-runtime"
            }
            : null;
    public void Dispose() { }
}
