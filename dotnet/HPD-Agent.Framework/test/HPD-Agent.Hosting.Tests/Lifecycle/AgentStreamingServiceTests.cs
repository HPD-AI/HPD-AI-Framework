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
        _agentManager = new TestAgentManager(_agentStore, _sessionStore);
        _service = new AgentStreamingService(_sessionManager, _agentManager);
    }

    public void Dispose()
    {
        _sessionManager.Dispose();
        _agentManager.Dispose();
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
    public void ApplyRouteScope_PreservesRunConfigContextOverrides()
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
                ProviderKey = "openrouter",
                ModelName = "model-1"
            } },
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
        messages.RunConfig!.ContextOverrides.Should().ContainKey("workspace");
        messages.RunConfig.ContextOverrides!["workspace"].Should().BeSameAs(workspaceOverride);
    }

    [Fact]
    public void ApplyRouteScope_PreservesBackgroundNotificationRunConfig()
    {
        var runConfig = new AgentRunConfig
        {
            Clients = new AgentClientsConfig { Chat = new ChatClientConfig
            {
                ProviderKey = "openrouter",
                ModelName = "model-1"
            } }
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
            ThreadExecutionId = "client-run",
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
                        ProviderKey = "test",
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
    public async Task ObserveThreadEventsAsync_SubscribesBeforeRuntimeConstruction_AndReceivesLiveEvents()
    {
        var (sessionId, threadId) = await _sessionManager.CreateSessionAsync(
            "agent-1",
            "session-live-observation");
        var stored = await _agentManager.CreateDefinitionAsync(new AgentConfig
        {
            Name = "agent-1",
            Clients = new AgentClientsConfig
            {
                Chat = new ChatClientConfig { ProviderKey = "test", ModelName = "test-model" }
            }
        }, "agent-1");

        var result = await _service.ObserveThreadEventsAsync(stored.Id, sessionId, threadId);

        result.Status.Should().Be(AgentServiceStatus.Success);
        _agentManager.GetRuntimeAgent(stored.Id, sessionId, threadId).Should().BeNull();
        await using var observation = result.Value!;
        var runtime = await _agentManager.GetOrBuildAgentRuntimeAsync(stored.Id, sessionId, threadId);
        var live = new TextDeltaEvent("live", "message-live")
        {
            SessionId = sessionId,
            ThreadId = threadId
        };
        await runtime.EventCoordinator.EmitAsync(live);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = await observation.LiveEvents.Reader.ReadAsync(timeout.Token);
        received.Should().BeSameAs(live);
    }

    [Fact]
    public async Task SubmitInputAsync_AllowsSelectedAgentDifferentFromThreadDefault_AndRecordsExecutingAgent()
    {
        var (sessionId, threadId) = await _sessionManager.CreateSessionAsync("agent-1", "session-hosted-run");
        var stored = await _agentManager.CreateDefinitionAsync(
            new AgentConfig
            {
                Name = "agent-1",
                MaxAgenticIterations = 1,
                Clients = new AgentClientsConfig
                {
                    Chat = new ChatClientConfig
                    {
                        ProviderKey = "test",
                        ModelName = "test-model"
                    }
                }
            },
            "agent-1");
        _agentManager.ChatClient.EnqueueTextResponse("done");

        var submitted = await _service.SubmitInputAsync(
            stored.Id,
            sessionId,
            threadId,
            new UserMessagesInputEvent
            {
                Messages = [new ChatMessage(ChatRole.User, "hello")]
            });

        submitted.Status.Should().Be(AgentServiceStatus.Success);
        var threadExecutionId = submitted.Value!.ThreadExecutionId;
        var descriptor = await _sessionStore.GetThreadAsync(new ThreadKey(sessionId, threadId));
        descriptor!.DefaultAgent.AgentId.Should().Be("agent-1");
        descriptor.DefaultAgent.AgentId.Should().NotBe(stored.Id);

        var observed = new List<AgentEvent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var batch in _sessionStore.ObserveThreadEventsAsync(
            new ThreadKey(sessionId, threadId),
            ThreadJournalCursor.Start(1),
            new ThreadObservationOptions(),
            timeout.Token))
        {
            observed.AddRange(batch.Events);
            if (observed.OfType<ThreadExecutionFinishedEvent>().Any(evt => evt.ThreadExecutionId == threadExecutionId))
                break;
        }

        var startedIndex = observed.FindIndex(evt => evt is ThreadExecutionStartedEvent started && started.ThreadExecutionId == threadExecutionId);
        var completedIndex = observed.FindIndex(evt => evt is ThreadExecutionFinishedEvent completed && completed.ThreadExecutionId == threadExecutionId);
        startedIndex.Should().BeGreaterThanOrEqualTo(0);
        completedIndex.Should().BeGreaterThan(startedIndex);
        observed.OfType<ThreadExecutionStartedEvent>()
            .Single(evt => evt.ThreadExecutionId == threadExecutionId)
            .AgentId.Should().Be(stored.Id);

        await WaitUntilAsync(
            () => _sessionManager.GetActiveThreadExecution(sessionId, threadId) is null,
            TimeSpan.FromSeconds(5));
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!predicate())
        {
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("Condition was not satisfied before the test timeout.");
            await Task.Delay(10);
        }
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
                            ProviderKey = "test",
                            ModelName = "test-model"
                        }
                    }
                }, registry)
                .WithAgentId(agentId)
                .WithSessionStore(sessionStore)
                .BuildAsync(ct);
        }

        protected override TimeSpan GetIdleTimeout() => TimeSpan.FromMinutes(30);
    }
}
