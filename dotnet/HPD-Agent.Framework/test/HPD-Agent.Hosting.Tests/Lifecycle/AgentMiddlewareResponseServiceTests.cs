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
    public async Task AnswerRequestAsync_UsesThreadRuntime_NotUnscopedAgent()
    {
        var (sessionId, threadId) = await _sessionManager.CreateSessionAsync("session-1");
        var stored = await _agentManager.CreateDefinitionAsync(MakeConfig("agent-1"), "agent-1");
        await _agentManager.GetOrBuildAgentAsync(stored.Id);

        var result = await _service.AnswerRequestAsync(
            stored.Id,
            sessionId,
            threadId,
            new PermissionResponseEvent("permission-1", "test", Approved: true));

        result.Status.Should().Be(AgentServiceStatus.Conflict);
        result.ErrorCode.Should().Be("ThreadRuntimeNotActive");
    }

    [Fact]
    public async Task AnswerRequestAsync_TargetsThreadRuntime_WhenBuilt()
    {
        var (sessionId, threadId) = await _sessionManager.CreateSessionAsync("session-1");
        var stored = await _agentManager.CreateDefinitionAsync(MakeConfig("agent-1"), "agent-1");
        await _agentManager.GetOrBuildAgentRuntimeAsync(stored.Id, sessionId, threadId);

        var result = await _service.AnswerRequestAsync(
            stored.Id,
            sessionId,
            threadId,
            new PermissionResponseEvent("permission-1", "test", Approved: true));

        result.Status.Should().Be(AgentServiceStatus.Conflict);
        result.ErrorCode.Should().BeNull();
    }

    private static AgentConfig MakeConfig(string name) => new()
    {
        Name = name,
        MaxAgenticIterations = 5,
        Clients = new AgentClientConfig
        {
            Chat = new ClientProviderConfig
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
