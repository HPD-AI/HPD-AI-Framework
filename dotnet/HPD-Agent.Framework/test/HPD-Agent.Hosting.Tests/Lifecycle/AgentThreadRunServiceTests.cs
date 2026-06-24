using FluentAssertions;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Hosting.Tests.Lifecycle;

public class AgentThreadRunServiceTests : IDisposable
{
    private readonly InMemorySessionStore _store = new();
    private readonly TestSessionManager _manager;
    private readonly AgentThreadRunService _service;

    public AgentThreadRunServiceTests()
    {
        _manager = new TestSessionManager(_store);
        _service = new AgentThreadRunService(_manager);
    }

    public void Dispose() => _manager.Dispose();

    [Fact]
    public async Task ListRunsAsync_ProjectsRunLifecycleAndBackgroundState()
    {
        await CreateThreadAsync("session-1", "main");

#pragma warning disable MEAI001 // Experimental API - Background Responses
        var token = ResponseContinuationToken.FromBytes(new byte[] { 1, 2, 3 });
#pragma warning restore MEAI001

        await _store.AppendThreadEventAsync("session-1", "main", new ThreadRunStartedEvent(
            "run-1",
            "agent-1",
            DateTimeOffset.Parse("2026-05-28T10:00:00Z"))
        {
            EventId = "evt-run-started",
            SessionId = "session-1",
            ThreadId = "main"
        });
        await _store.AppendThreadEventAsync("session-1", "main", new BackgroundOperationStartedEvent(
            token,
            OperationStatus.InProgress,
            "op-1")
        {
            EventId = "evt-background-started",
            SessionId = "session-1",
            ThreadId = "main"
        });
        await _store.AppendThreadEventAsync("session-1", "main", new BackgroundTaskStartedEvent
        {
            EventId = "evt-task-started",
            SessionId = "session-1",
            ThreadId = "main",
            TaskId = "task-1",
            Name = "compile",
            SourceKind = BackgroundTaskSourceKind.ToolCall,
            NotificationPolicy = BackgroundTaskNotificationPolicy.OnFault,
            Invocation = Invocation(),
            StartedAt = DateTimeOffset.Parse("2026-05-28T10:00:01Z")
        });
        await _store.AppendThreadEventAsync("session-1", "main", new BackgroundTaskCompletedEvent
        {
            EventId = "evt-task-completed",
            SessionId = "session-1",
            ThreadId = "main",
            TaskId = "task-1",
            Name = "compile",
            SourceKind = BackgroundTaskSourceKind.ToolCall,
            NotificationPolicy = BackgroundTaskNotificationPolicy.OnFault,
            Invocation = Invocation(),
            CompletedAt = DateTimeOffset.Parse("2026-05-28T10:00:02Z"),
            DurationMilliseconds = 1000
        });
        await _store.AppendThreadEventAsync("session-1", "main", new ThreadRunCompletedEvent(
            "run-1",
            "agent-1",
            false)
        {
            EventId = "evt-run-completed",
            SessionId = "session-1",
            ThreadId = "main"
        });

        var result = await _service.ListRunsAsync("agent-1", "session-1", "main");

        result.Status.Should().Be(AgentServiceStatus.Success);
        var run = result.Value.Should().ContainSingle().Subject;
        run.RuntimeRunId.Should().Be("run-1");
        run.Status.Should().Be("completed");
        run.BackgroundOperation.Should().NotBeNull();
        run.BackgroundOperation!.OperationId.Should().Be("op-1");
        run.BackgroundTasks.Should().ContainSingle(task =>
            task.TaskId == "task-1" && task.Status == "completed");
    }

    [Fact]
    public async Task GetActiveRunAsync_MergesInMemoryActiveRun_WhenStartEventIsNotDurableYet()
    {
        await CreateThreadAsync("session-1", "main");
        _manager.TryStartThreadRun("agent-1", "session-1", "main", out var active).Should().BeTrue();

        var result = await _service.GetActiveRunAsync("agent-1", "session-1", "main");

        result.Status.Should().Be(AgentServiceStatus.Success);
        result.Value.Should().NotBeNull();
        result.Value!.RuntimeRunId.Should().Be(active.RuntimeRunId);
        result.Value.Status.Should().Be("active");
    }

    private async Task CreateThreadAsync(string sessionId, string threadId)
    {
        var session = new HPD.Agent.Session(sessionId);
        var thread = session.CreateThread(threadId);

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
}
