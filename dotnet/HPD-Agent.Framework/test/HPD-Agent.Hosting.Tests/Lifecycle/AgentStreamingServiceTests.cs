using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Hosting.Tests.Infrastructure;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Hosting.Tests.Lifecycle;

public sealed class AgentStreamingServiceTests : IAsyncLifetime
{
    private readonly InMemorySessionStore _sessionStore = new(HPD.Agent.Serialization.CoreAgentEventComposition.Instance.Codec);
    private readonly InMemoryAgentStore _agentStore = new();
    private readonly TestSessionManager _sessionManager;
    private readonly TestAgentManager _agentManager;
    private readonly AgentStreamingService _service;

    public AgentStreamingServiceTests()
    {
        _sessionManager = new TestSessionManager(_sessionStore);
        _agentManager = new TestAgentManager(_agentStore, _sessionStore);
        _service = new AgentStreamingService(_sessionManager, _agentManager);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _sessionManager.Dispose();
        await _agentManager.DisposeAsync();
    }

    [Fact]
    public async Task RebaseSeedProvider_ReencodesAuthoritativeActiveExecution()
    {
        _sessionManager.TryReserveThreadExecution("agent", "session", "thread", out var reserved)
            .Should().BeTrue();
        _sessionManager.ActivateThreadExecution("session", "thread", reserved.ThreadExecutionId)
            .Should().BeTrue();
        var provider = new HostedThreadJournalRebaseSeedProvider(_sessionManager);

        var seeds = await provider.CreateSeedEventsAsync(new ThreadKey("session", "thread"));

        var started = seeds.Should().ContainSingle().Which
            .Should().BeOfType<ThreadExecutionStartedEvent>().Subject;
        started.ThreadExecutionId.Should().Be(reserved.ThreadExecutionId);
        started.AgentId.Should().Be("agent");
        started.ThreadSequenceNumber.Should().Be(0);
        started.SessionId.Should().Be("session");
        started.ThreadId.Should().Be("thread");
    }

    [Fact]
    public void ApplyRouteScope_PreservesRunConfigContextProperties()
    {
        var workspaceOverride = new Dictionary<string, object>
        {
            ["version"] = 1,
            ["defaultRootId"] = "default"
        };
        var runConfig = new AgentRunConfig
        {
            Clients = new AgentClientsConfig { Chat = new ChatClientConfig
            {
                Provider = new ProviderReference
                {
                    Key = "openrouter", Backend = "platform",
                    Authentication = new ApiKeyProviderAuthentication { SecretKey = "openrouter:ApiKey" }
                },
                ModelName = "model-1"
            } },
            Context = new AgentContextRunConfig
            {
                Properties = new Dictionary<string, object>
                {
                    ["workspace"] = workspaceOverride
                }
            }
        };
        var input = new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, "run tests")],
            ClientInputId = "client-input-1",
            AgentId = "client-agent",
            SessionId = "client-session",
            ThreadId = "client-thread",
            ThreadExecutionId = "client-run",
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
        messages.ThreadExecutionId.Should().Be("route-run");
        messages.ClientInputId.Should().Be("client-input-1");
        messages.RunConfig.Should().BeSameAs(runConfig);
        messages.RunConfig!.Context!.Properties.Should().ContainKey("workspace");
        messages.RunConfig.Context.Properties!["workspace"].Should().BeSameAs(workspaceOverride);
    }

    [Fact]
    public void ApplyRouteScope_PreservesOperationNotificationRunConfig()
    {
        var runConfig = new AgentRunConfig
        {
            Clients = new AgentClientsConfig { Chat = new ChatClientConfig
            {
                Provider = new ProviderReference
                {
                    Key = "openrouter", Backend = "platform",
                    Authentication = new ApiKeyProviderAuthentication { SecretKey = "openrouter:ApiKey" }
                },
                ModelName = "model-1"
            } }
        };
        var input = new AgentOperationNotificationInputEvent(
            [
                new AgentOperationNotification
                {
                    NotificationId = "notification-1",
                    OperationId = "operation-1",
                    Name = "compile",
                    ProviderStatus = "completed",
                    Summary = "Operation completed."
                }
            ])
        {
            ClientInputId = "client-input-1",
            AgentId = "client-agent",
            SessionId = "client-session",
            ThreadId = "client-thread",
            ThreadExecutionId = "client-run",
            RunConfig = runConfig
        };

        var scoped = _service.ApplyRouteScope(
            input,
            "route-agent",
            "route-session",
            "route-thread",
            "route-run");

        var notification = scoped.Should().BeOfType<AgentOperationNotificationInputEvent>().Subject;
        notification.AgentId.Should().Be("route-agent");
        notification.SessionId.Should().Be("route-session");
        notification.ThreadId.Should().Be("route-thread");
        notification.ThreadExecutionId.Should().Be("route-run");
        notification.ClientInputId.Should().Be("client-input-1");
        notification.RunConfig.Should().BeSameAs(runConfig);
    }

    [Fact]
    public async Task EstimateContextUsageAsync_ReturnsThreadUsageForScopedThread()
    {
        var (sessionId, threadId) = await _sessionManager.CreateSessionAsync("agent-1", "session-usage");
        await _sessionStore.AppendThreadEventAsync(
            sessionId,
            threadId,
            ThreadEventFactory.ContentAdded(
                sessionId,
                threadId,
                "usage-message",
                new TextContent("12345678"),
                role: ChatRole.User.Value));

        var result = await _service.EstimateContextUsageAsync(
            "agent-1",
            sessionId,
            threadId,
            new AgentRunConfig());

        result.Status.Should().Be(AgentServiceStatus.Success);
        result.Value!.SessionId.Should().Be(sessionId);
        result.Value.ThreadId.Should().Be(threadId);
        result.Value.EffectiveInputTokens.Should().Be(2);
        result.Value.UsageRatio.Should().BeNull();
    }

    [Fact]
    public async Task GetThreadStateAsync_DoesNotReviveAnUnownedHistoricalRun()
    {
        var (sessionId, threadId) = await _sessionManager.CreateSessionAsync("agent-1", "session-orphaned-run");
        await _sessionStore.AppendThreadEventAsync(
            sessionId,
            threadId,
            new ThreadExecutionStartedEvent("orphaned-run", "agent-1", DateTimeOffset.UtcNow)
            {
                SessionId = sessionId,
                ThreadId = threadId
            });

        var result = await _service.GetThreadStateAsync("agent-1", sessionId, threadId);

        result.Status.Should().Be(AgentServiceStatus.Success);
        result.Value!.ActiveExecution.Should().BeNull();
        result.Value.ObservedCursor.Should().Be(new ThreadJournalCursor(1, 3));

        var repeated = await _service.GetThreadStateAsync("agent-1", sessionId, threadId);

        repeated.Value!.ObservedCursor.Should().Be(new ThreadJournalCursor(1, 3),
            "recovery must not append a second terminal fact");
    }

    [Fact]
    public async Task GetThreadStateAsync_DoesNotFinalizeALiveControlledChildExecution()
    {
        var (sessionId, threadId) = await _sessionManager.CreateSessionAsync(
            "child-agent",
            "session-live-child");
        var route = new ThreadKey(sessionId, threadId);
        await using var childAgent = new Agent(
            new AgentConfig
            {
                Name = "child-agent",
                SessionStore = _sessionStore,
                EventComposition = CoreAgentEventComposition.Instance
            },
            baseClient: null,
            mergedOptions: null);
        var controller = ThreadExecutionControllerRegistry.For(_sessionStore);
        var acquired = await controller.TryAcquireAsync(
            new ThreadExecutionStartRequest(route, "child-run-1", childAgent));
        acquired.Acquired.Should().BeTrue();

        _sessionManager.GetActiveThreadExecution(sessionId, threadId).Should().BeNull(
            "a sub-agent child is live only in the core controller, not the hosting slot");

        var before = await _sessionStore.CollectThreadEventsAsync(route);

        var result = await _service.GetThreadStateAsync("child-agent", sessionId, threadId);
        result.Status.Should().Be(AgentServiceStatus.Success);
        var afterFirst = await _sessionStore.CollectThreadEventsAsync(route);
        afterFirst.Should().HaveSameCount(before,
            "a live child must not be finalized as HostExecutionLost by state reconciliation");
        afterFirst.OfType<ThreadExecutionFinishedEvent>().Should().BeEmpty();

        var repeated = await _service.GetThreadStateAsync("child-agent", sessionId, threadId);
        repeated.Status.Should().Be(AgentServiceStatus.Success);
        var afterSecond = await _sessionStore.CollectThreadEventsAsync(route);
        afterSecond.Should().HaveSameCount(afterFirst,
            "repeated reconciliation while the child is live must not append a second terminal fact");

        await controller.ReleaseAsync(
            acquired.Lease!,
            new ThreadExecutionTerminalResult(ThreadExecutionOutcome.Cancelled));
    }

    [Fact]
    public async Task GetThreadStateAsync_ProjectsPendingRequestsFromDurableJournal()
    {
        var (sessionId, threadId) = await _sessionManager.CreateSessionAsync("agent-1", "session-pending-request");
        var stored = await _agentManager.CreateDefinitionAsync(
            new AgentConfig
            {
                Name = "agent-1",
                MaxAgenticIterations = 1,
                Clients = new AgentClientsConfig
                {
                    Chat = new ChatClientConfig
                    {
                        Provider = TestAgentFactory.TestSelection(),
                        ModelName = "test-model"
                    }
                }
            },
            "agent-1");
        _sessionManager.TryReserveThreadExecution(stored.Id, sessionId, threadId, out var execution)
            .Should().BeTrue();
        _sessionManager.ActivateThreadExecution(sessionId, threadId, execution.ThreadExecutionId)
            .Should().BeTrue();
        var request = new PermissionRequestEvent(
            "permission-1",
            "test",
            "function",
            null,
            "call-1",
            null)
        {
            SessionId = sessionId,
            ThreadId = threadId,
            ThreadExecutionId = execution.ThreadExecutionId
        };
        await _sessionStore.AppendThreadEventsAsync(
            new ThreadKey(sessionId, threadId),
            [
                new ThreadExecutionStartedEvent(execution.ThreadExecutionId, stored.Id, execution.StartedAt),
                request
            ]);

        var result = await _service.GetThreadStateAsync(stored.Id, sessionId, threadId);

        var pending = result.Value!.PendingRequests.Should().ContainSingle().Subject;
        pending.Request.Should().BeOfType<PermissionRequestEvent>();
        pending.Request.ThreadExecutionId.Should().Be(execution.ThreadExecutionId);
        pending.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        _sessionManager.ReleaseThreadExecution(sessionId, threadId, execution.ThreadExecutionId)
            .Should().BeTrue();
    }

    [Fact]
    public async Task ObserveThreadEventsAsync_RejectsUnknownHierarchyBeforeInstallingInbox()
    {
        var result = await _service.ObserveThreadEventsAsync(
            "agent-1",
            new ThreadKey("session", "thread"),
            (AgentEventHierarchy)99);

        result.Status.Should().Be(AgentServiceStatus.ValidationError);
        result.ErrorCode.Should().Be("InvalidEventHierarchy");
    }

    [Theory]
    [InlineData("", "thread")]
    [InlineData("session", " ")]
    public async Task ObserveThreadEventsAsync_RejectsIncompleteThreadKey(string sessionId, string threadId)
    {
        var result = await _service.ObserveThreadEventsAsync(
            "agent-1",
            new ThreadKey(sessionId, threadId));

        result.Status.Should().Be(AgentServiceStatus.ValidationError);
        result.ErrorCode.Should().Be("InvalidThreadKey");
    }

    private sealed class TestSessionManager(ISessionStore store) : SessionManager(store);

    private sealed class TestAgentManager(IAgentStore store, ISessionStore sessionStore) : AgentManager(store)
    {
        public FakeChatClient ChatClient { get; } = new();

        protected override Task<Agent> BuildAgentAsync(string agentId, CancellationToken ct)
        {
            var registry = new TestProviderRegistry(ChatClient);
            return new AgentBuilder(new AgentConfig
                {
                    Name = agentId,
                    MaxAgenticIterations = 1,
                    Clients = new AgentClientsConfig
                    {
                        Chat = new ChatClientConfig
                        {
                            Provider = TestAgentFactory.TestSelection(),
                            ModelName = "test-model"
                        }
                    }
                }, registry)
                .WithAgentId(agentId)
                .WithEventComposition(CoreAgentEventComposition.Instance)
                .WithSessionStore(sessionStore)
                .BuildAsync(ct);
        }

        protected override TimeSpan GetIdleTimeout() => TimeSpan.FromMinutes(30);
    }
}
