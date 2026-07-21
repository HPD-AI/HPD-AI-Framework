using FluentAssertions;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Hosting.Tests.Lifecycle;

public class AgentThreadExecutionServiceTests : IDisposable
{
    private readonly InMemorySessionStore _store = new();
    private readonly TestSessionManager _manager;
    private readonly AgentThreadExecutionService _service;

    public AgentThreadExecutionServiceTests()
    {
        _manager = new TestSessionManager(_store);
        _service = new AgentThreadExecutionService(_manager);
    }

    public void Dispose() => _manager.Dispose();

    [Fact]
    public async Task ListExecutionsAsync_ProjectsExecutionLifecycleAndBackgroundState()
    {
        await CreateThreadAsync("session-1", "main");

#pragma warning disable MEAI001 // Experimental API - Background Responses
        var token = ResponseContinuationToken.FromBytes(new byte[] { 1, 2, 3 });
#pragma warning restore MEAI001

        await _store.AppendThreadEventAsync("session-1", "main", new ThreadExecutionStartedEvent(
            "execution-1",
            "agent-1",
            DateTimeOffset.Parse("2026-05-28T10:00:00Z"))
        {
            EventId = "evt-execution-started",
            SessionId = "session-1",
            ThreadId = "main"
        });
        await _store.AppendThreadEventAsync("session-1", "main", new ModelBackgroundOperationStartedEvent(
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
            Notification = new BackgroundTaskNotificationRule.OnFinalStateRule(Faulted: true),
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
            Notification = new BackgroundTaskNotificationRule.OnFinalStateRule(Faulted: true),
            Invocation = Invocation(),
            CompletedAt = DateTimeOffset.Parse("2026-05-28T10:00:02Z"),
            DurationMilliseconds = 1000
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
        execution.ModelBackgroundOperation.Should().NotBeNull();
        execution.ModelBackgroundOperation!.OperationId.Should().Be("op-1");
        execution.BackgroundTasks.Should().ContainSingle(task =>
            task.TaskId == "task-1" && task.Status == "completed");
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
}
