using FluentAssertions;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Hosting.Tests.Lifecycle;

public class AgentBranchRunServiceTests : IDisposable
{
    private readonly InMemorySessionStore _store = new();
    private readonly TestSessionManager _manager;
    private readonly AgentBranchRunService _service;

    public AgentBranchRunServiceTests()
    {
        _manager = new TestSessionManager(_store);
        _service = new AgentBranchRunService(_manager);
    }

    public void Dispose() => _manager.Dispose();

    [Fact]
    public async Task ListRunsAsync_ProjectsRunLifecycleAndBackgroundState()
    {
        await CreateBranchAsync("session-1", "main");

#pragma warning disable MEAI001 // Experimental API - Background Responses
        var token = ResponseContinuationToken.FromBytes(new byte[] { 1, 2, 3 });
#pragma warning restore MEAI001

        await _store.AppendBranchEventAsync("session-1", "main", new BranchRunStartedEvent(
            "run-1",
            "agent-1",
            DateTimeOffset.Parse("2026-05-28T10:00:00Z"))
        {
            SessionId = "session-1",
            BranchId = "main"
        });
        await _store.AppendBranchEventAsync("session-1", "main", new BackgroundOperationStartedEvent(
            token,
            OperationStatus.InProgress,
            "op-1")
        {
            SessionId = "session-1",
            BranchId = "main"
        });
        await _store.AppendBranchEventAsync("session-1", "main", new ToolCallBackgroundTaskStartedEvent
        {
            SessionId = "session-1",
            BranchId = "main",
            TaskId = "task-1",
            Name = "compile",
            Invocation = Invocation(),
            StartedAt = DateTimeOffset.Parse("2026-05-28T10:00:01Z")
        });
        await _store.AppendBranchEventAsync("session-1", "main", new ToolCallBackgroundTaskCompletedEvent
        {
            SessionId = "session-1",
            BranchId = "main",
            TaskId = "task-1",
            Name = "compile",
            Invocation = Invocation(),
            CompletedAt = DateTimeOffset.Parse("2026-05-28T10:00:02Z"),
            DurationMilliseconds = 1000
        });
        await _store.AppendBranchEventAsync("session-1", "main", new BranchRunCompletedEvent(
            "run-1",
            "agent-1",
            false)
        {
            SessionId = "session-1",
            BranchId = "main"
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
        await CreateBranchAsync("session-1", "main");
        _manager.TryStartBranchRun("agent-1", "session-1", "main", out var active).Should().BeTrue();

        var result = await _service.GetActiveRunAsync("agent-1", "session-1", "main");

        result.Status.Should().Be(AgentServiceStatus.Success);
        result.Value.Should().NotBeNull();
        result.Value!.RuntimeRunId.Should().Be(active.RuntimeRunId);
        result.Value.Status.Should().Be("active");
    }

    private async Task CreateBranchAsync(string sessionId, string branchId)
    {
        var session = new HPD.Agent.Session(sessionId);
        var branch = session.CreateBranch(branchId);

        await _store.SaveSessionAsync(session);
        await _store.SaveInitialBranchAsync(session.Id, branch);
    }

    private static FunctionInvocationSnapshot Invocation() => new()
    {
        AgentName = "agent-1",
        FunctionCallId = "call-1",
        FunctionName = "tool",
        SessionId = "session-1",
        BranchId = "main"
    };

    private sealed class TestSessionManager : SessionManager
    {
        public TestSessionManager(ISessionStore store) : base(store) { }
    }
}
