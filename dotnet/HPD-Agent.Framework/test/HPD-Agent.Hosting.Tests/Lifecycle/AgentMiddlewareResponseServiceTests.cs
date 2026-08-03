using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Hosting.Tests.Infrastructure;
using HPD.Agent.Providers;
using HPD.Agent.Hosting.Lifecycle;

namespace HPD.Agent.Hosting.Tests.Lifecycle;

public class AgentMiddlewareResponseServiceTests : IDisposable
{
    private readonly InMemorySessionStore _sessionStore = new();
    private readonly InMemoryAgentStore _agentStore = new();
    private readonly TestSessionManager _sessionManager;
    private readonly TestAgentManager _agentManager;
    private readonly AgentMiddlewareResponseService _service;

    public AgentMiddlewareResponseServiceTests()
    {
        _sessionManager = new TestSessionManager(_sessionStore);
        _agentManager = new TestAgentManager(_agentStore);
        _service = new AgentMiddlewareResponseService(_sessionManager, _agentManager);
    }

    public void Dispose()
    {
        _sessionManager.Dispose();
        _agentManager.Dispose();
    }

    [Fact]
    public async Task AnswerRequestAsync_WithoutDurableRequest_ReturnsTypedNotFound()
    {
        var (sessionId, threadId) = await _sessionManager.CreateSessionAsync("agent-1", "session-1");
        var stored = await _agentManager.CreateDefinitionAsync(MakeConfig("agent-1"), "agent-1");
        await _agentManager.GetOrBuildAgentAsync(stored.Id);

        var result = await _service.AnswerRequestAsync(
            stored.Id,
            sessionId,
            threadId,
            new PermissionResponseEvent("permission-1", "test", Approved: true));

        result.Status.Should().Be(AgentServiceStatus.Success);
        result.Value!.Status.Should().Be(AgentRespondStatus.NotFound);
    }

    [Fact]
    public async Task AnswerRequestAsync_TargetsThreadRuntime_WhenBuilt()
    {
        var (sessionId, threadId) = await _sessionManager.CreateSessionAsync("agent-1", "session-1");
        var stored = await _agentManager.CreateDefinitionAsync(MakeConfig("agent-1"), "agent-1");
        await _agentManager.GetOrBuildAgentRuntimeAsync(stored.Id, sessionId, threadId);

        var result = await _service.AnswerRequestAsync(
            stored.Id,
            sessionId,
            threadId,
            new PermissionResponseEvent("permission-1", "test", Approved: true));

        result.Status.Should().Be(AgentServiceStatus.Success);
        result.Value!.Status.Should().Be(AgentRespondStatus.NotFound);
    }

    [Fact]
    public async Task AnswerRequestAsync_CommitsCanonicalResponseBeforeReleasingWaiter()
    {
        var (sessionId, threadId) = await _sessionManager.CreateSessionAsync("agent-1", "session-1");
        var stored = await _agentManager.CreateDefinitionAsync(MakeConfig("agent-1"), "agent-1");
        var runtime = await _agentManager.GetOrBuildAgentRuntimeAsync(stored.Id, sessionId, threadId);
        var request = new PermissionRequestEvent(
            "permission-1",
            "test",
            "function",
            null,
            "call-1",
            null);
        var handle = runtime.EventCoordinator.RegisterRequest<PermissionRequestEvent, PermissionResponseEvent>(request);

        var result = await _service.AnswerRequestAsync(
            stored.Id,
            sessionId,
            threadId,
            new PermissionResponseEvent("permission-1", "test", Approved: true));

        result.Status.Should().Be(AgentServiceStatus.Success);
        result.Value!.Status.Should().Be(AgentRespondStatus.Accepted);
        var completed = (PermissionResponseEvent)await handle.Response;
        completed.ThreadSequenceNumber.Should().Be(1);
        completed.SessionId.Should().Be(sessionId);
        completed.ThreadId.Should().Be(threadId);

        var key = new ThreadKey(sessionId, threadId);
        var replay = await runtime.Config!.SessionStore!.CollectThreadEventsAsync(key);
        replay.Should().ContainSingle();
        replay![0].Should().Be(completed);
    }

    private static AgentConfig MakeConfig(string name) => new()
    {
        Name = name,
        MaxAgenticIterations = 5,
        Clients = new AgentClientsConfig
        {
            Chat = new ProviderClientConfig
            {
                ProviderKey = "test",
                ModelName = "test-model"
            }
        }
    };

    private sealed class TestSessionManager : SessionManager
    {
        public TestSessionManager(ISessionStore store) : base(store)
        {
        }
    }

    private sealed class TestAgentManager : AgentManager
    {
        public TestAgentManager(IAgentStore store) : base(store)
        {
        }

        protected override async Task<Agent> BuildAgentAsync(string agentId, CancellationToken ct)
        {
            var stored = await AgentStore.LoadAsync(agentId, ct)
                ?? new StoredAgent
                {
                    Id = agentId,
                    Name = agentId,
                    Config = MakeConfig(agentId)
                };

            var registry = new TestProviderRegistry(new FakeChatClient());
            return await new AgentBuilder(stored.Config, registry)
                .WithAgentId(stored.Id)
                .WithSessionStore(new InMemorySessionStore())
                .BuildAsync(ct);
        }

        protected override TimeSpan GetIdleTimeout() => TimeSpan.FromMinutes(30);
    }
}
