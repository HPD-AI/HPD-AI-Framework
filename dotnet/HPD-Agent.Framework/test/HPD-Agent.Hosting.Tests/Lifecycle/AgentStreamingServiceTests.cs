using FluentAssertions;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Hosting.Tests.Infrastructure;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Hosting.Tests.Lifecycle;

public sealed class AgentStreamingServiceTests : IDisposable
{
    private readonly InMemorySessionStore _sessionStore = new();
    private readonly InMemoryAgentStore _agentStore = new();
    private readonly TestSessionManager _sessionManager;
    private readonly TestAgentManager _agentManager;
    private readonly AgentStreamingService _service;

    public AgentStreamingServiceTests()
    {
        _sessionManager = new TestSessionManager(_sessionStore);
        _agentManager = new TestAgentManager(_agentStore);
        _service = new AgentStreamingService(_sessionManager, _agentManager);
    }

    public void Dispose()
    {
        _sessionManager.Dispose();
        _agentManager.Dispose();
    }

    [Fact]
    public void ApplyRouteScope_PreservesRunConfigContextOverrides()
    {
        var workspaceOverride = new Dictionary<string, object>
        {
            ["version"] = 1,
            ["defaultRootId"] = "default"
        };
        var runConfig = new AgentRunConfig
        {
            ProviderKey = "openrouter",
            ModelId = "model-1",
            ContextOverrides = new Dictionary<string, object>
            {
                ["workspace"] = workspaceOverride
            }
        };
        var input = new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, "run tests")],
            ClientInputId = "client-input-1",
            AgentId = "client-agent",
            SessionId = "client-session",
            ThreadId = "client-thread",
            RuntimeRunId = "client-run",
            RunConfig = runConfig
        };

        var scoped = _service.ApplyRouteScope(
            input,
            "route-agent",
            "route-session",
            "route-thread",
            "route-run");

        var messages = scoped.Should().BeOfType<UserMessagesInputEvent>().Subject;
        messages.AgentId.Should().Be("route-agent");
        messages.SessionId.Should().Be("route-session");
        messages.ThreadId.Should().Be("route-thread");
        messages.RuntimeRunId.Should().Be("route-run");
        messages.ClientInputId.Should().Be("client-input-1");
        messages.RunConfig.Should().BeSameAs(runConfig);
        messages.RunConfig!.ContextOverrides.Should().ContainKey("workspace");
        messages.RunConfig.ContextOverrides!["workspace"].Should().BeSameAs(workspaceOverride);
    }

    [Fact]
    public void ApplyRouteScope_PreservesBackgroundNotificationRunConfig()
    {
        var runConfig = new AgentRunConfig
        {
            ProviderKey = "openrouter",
            ModelId = "model-1"
        };
        var input = new BackgroundTaskNotificationInputEvent(
            [
                new BackgroundTaskNotification(
                    "notification-1",
                    ["task-1"],
                    "Background task completed.")
            ])
        {
            ClientInputId = "client-input-1",
            AgentId = "client-agent",
            SessionId = "client-session",
            ThreadId = "client-thread",
            RuntimeRunId = "client-run",
            RunConfig = runConfig
        };

        var scoped = _service.ApplyRouteScope(
            input,
            "route-agent",
            "route-session",
            "route-thread",
            "route-run");

        var notification = scoped.Should().BeOfType<BackgroundTaskNotificationInputEvent>().Subject;
        notification.AgentId.Should().Be("route-agent");
        notification.SessionId.Should().Be("route-session");
        notification.ThreadId.Should().Be("route-thread");
        notification.RuntimeRunId.Should().Be("route-run");
        notification.ClientInputId.Should().Be("client-input-1");
        notification.RunConfig.Should().BeSameAs(runConfig);
    }

    [Fact]
    public async Task EstimateContextUsageAsync_ReturnsThreadUsageForScopedThread()
    {
        var (sessionId, threadId) = await _sessionManager.CreateSessionAsync("session-usage");
        var thread = (await _sessionStore.LoadThreadAsync(sessionId, threadId))!;
        thread.AddMessage(new ChatMessage(ChatRole.User, "12345678"));
        await _sessionStore.SaveInitialThreadAsync(sessionId, thread);

        var result = await _service.EstimateContextUsageAsync(
            "agent-1",
            sessionId,
            threadId,
            new AgentRunConfig
            {
                Compaction = new CompactionRunConfig
                {
                    ModelContext = new ModelContextWindowOptions
                    {
                        ProviderKey = "openai",
                        ModelId = "small",
                        ContextWindow = 8
                    }
                }
            });

        result.Status.Should().Be(AgentServiceStatus.Success);
        result.Value!.SessionId.Should().Be(sessionId);
        result.Value.ThreadId.Should().Be(threadId);
        result.Value.EffectiveInputTokens.Should().Be(2);
        result.Value.UsageRatio.Should().Be(0.25);
    }

    [Fact]
    public async Task GetThreadStateAsync_DoesNotReviveAnUnownedHistoricalRun()
    {
        var (sessionId, threadId) = await _sessionManager.CreateSessionAsync("session-orphaned-run");
        await _sessionStore.AppendThreadEventAsync(
            sessionId,
            threadId,
            new ThreadRunStartedEvent("orphaned-run", "agent-1", DateTimeOffset.UtcNow)
            {
                SessionId = sessionId,
                ThreadId = threadId
            });

        var result = await _service.GetThreadStateAsync("agent-1", sessionId, threadId);

        result.Status.Should().Be(AgentServiceStatus.Success);
        result.Value!.ActiveRun.Should().BeNull();
        result.Value.Events.Should().ContainSingle(evt => evt is ThreadRunStartedEvent);
    }

    private sealed class TestSessionManager(ISessionStore store) : SessionManager(store);

    private sealed class TestAgentManager(IAgentStore store) : AgentManager(store)
    {
        protected override Task<Agent> BuildAgentAsync(string agentId, CancellationToken ct)
        {
            var chatClient = new FakeChatClient();
            var registry = new TestProviderRegistry(chatClient);
            return new AgentBuilder(new AgentConfig
                {
                    Name = agentId,
                    MaxAgenticIterations = 1,
                    Clients = new AgentClientConfig
                    {
                        Chat = new ClientProviderConfig
                        {
                            ProviderKey = "test",
                            ModelName = "test-model"
                        }
                    }
                }, registry)
                .WithAgentId(agentId)
                .WithSessionStore(new InMemorySessionStore())
                .BuildAsync(ct);
        }

        protected override TimeSpan GetIdleTimeout() => TimeSpan.FromMinutes(30);
    }
}
