using FluentAssertions;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Hosting.Tests.Lifecycle;

public class AgentThreadExecutionServiceTests : IDisposable
{
    private readonly InMemorySessionStore _store = new(HPD.Agent.Serialization.CoreAgentEventComposition.Instance.Codec);
    private readonly TestSessionManager _manager;
    private readonly TestAgentManager _agentManager;
    private readonly AgentThreadExecutionService _service;

    public AgentThreadExecutionServiceTests()
    {
        _manager = new TestSessionManager(_store);
        _agentManager = new TestAgentManager();
        _service = new AgentThreadExecutionService(_manager, _agentManager);
    }

    public void Dispose()
    {
        _manager.Dispose();
        _agentManager.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task ListExecutionsAsync_ProjectsExecutionLifecycleAndUnifiedOperations()
    {
        await CreateThreadAsync("session-1", "main");

        await _store.AppendThreadEventAsync("session-1", "main", new ThreadExecutionStartedEvent(
            "execution-1",
            "agent-1",
            DateTimeOffset.Parse("2026-05-28T10:00:00Z"))
        {
            EventId = "evt-execution-started",
            SessionId = "session-1",
            ThreadId = "main"
        });
        var registeredAt = DateTimeOffset.Parse("2026-05-28T10:00:01Z");
        var operation = new AgentOperationSnapshot
        {
            OperationId = "op-1",
            ProviderOperationId = "provider-op-1",
            SourceKind = AgentOperationSourceKind.LocalTool,
            Name = "compile",
            Address = new AgentExecutionAddress("agent-1", "session-1", "main"),
            OriginatingThreadExecutionId = "execution-1",
            Invocation = Invocation(),
            ProviderStatus = AgentOperationProviderStatus.Running,
            ObservationStatus = AgentOperationObservationStatus.Attached,
            Control = new AgentOperationControl("op-1", AgentOperationKind.Task,
                AgentOperationCapabilities.Cancel),
            Notification = new AgentOperationNotificationPolicy(),
            RegisteredAt = registeredAt,
            StartedAt = registeredAt,
            UpdatedAt = registeredAt,
            Version = 0
        };
        await _store.AppendThreadEventAsync("session-1", "main", new AgentOperationRegisteredEvent
        {
            EventId = "evt-operation-registered",
            SessionId = "session-1",
            ThreadId = "main",
            ThreadExecutionId = "execution-1",
            Operation = operation
        });
        await _store.AppendThreadEventAsync("session-1", "main", new AgentOperationTransitionedEvent
        {
            EventId = "evt-operation-completed",
            SessionId = "session-1",
            ThreadId = "main",
            ThreadExecutionId = "execution-1",
            OperationId = "op-1",
            PreviousVersion = 0,
            Operation = operation with
            {
                ProviderStatus = AgentOperationProviderStatus.Completed,
                UpdatedAt = DateTimeOffset.Parse("2026-05-28T10:00:02Z"),
                FinishedAt = DateTimeOffset.Parse("2026-05-28T10:00:02Z"),
                Completion = new AgentOperationCompletion("compiled"),
                Version = 1
            }
        });
        await _store.AppendThreadEventAsync("session-1", "main", new ThreadExecutionFinishedEvent(
            "execution-1",
            "agent-1",
            ThreadExecutionOutcome.Succeeded,
            DateTimeOffset.Parse("2026-05-28T10:00:03Z"))
        {
            EventId = "evt-execution-completed",
            SessionId = "session-1",
            ThreadId = "main"
        });

        var result = await _service.ListExecutionsAsync("agent-1", "session-1", "main");

        result.Status.Should().Be(AgentServiceStatus.Success);
        var execution = result.Value.Should().ContainSingle().Subject;
        execution.ThreadExecutionId.Should().Be("execution-1");
        execution.Status.Should().Be("succeeded");
        execution.Operations.Should().ContainSingle(projected =>
            projected.OperationId == "op-1" &&
            projected.ProviderOperationId == "provider-op-1" &&
            projected.ProviderStatus == "completed" &&
            projected.CompletionSummary == "compiled");
    }

    private async Task CreateThreadAsync(string sessionId, string threadId)
    {
        var session = new HPD.Agent.Session(sessionId);
        var thread = session.CreateThread("test-agent", threadId);

        await _store.SaveSessionAsync(session);
        await _store.SaveInitialThreadAsync(session.Id, thread);
    }

    private static FunctionInvocationSnapshot Invocation() => new()
    {
        AgentName = "agent-1",
        FunctionCallId = "call-1",
        FunctionName = "tool",
        SessionId = "session-1",
        ThreadId = "main"
    };

    private sealed class TestSessionManager : SessionManager
    {
        public TestSessionManager(ISessionStore store) : base(store) { }
    }

    private sealed class TestAgentManager : AgentManager
    {
        public TestAgentManager() : base(new InMemoryAgentStore()) { }

        protected override Task<Agent> BuildAgentAsync(string agentId, CancellationToken ct) =>
            throw new NotSupportedException("This projection test does not resolve a runtime agent.");

        protected override TimeSpan GetIdleTimeout() => TimeSpan.FromMinutes(30);
    }
}
