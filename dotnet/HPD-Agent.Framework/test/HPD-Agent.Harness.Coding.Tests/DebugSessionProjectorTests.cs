using HPD.Agent;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugSessionProjectorTests
{
    [Fact]
    public void Projector_reconstructs_tree_children_stop_continue_exit_and_artifact_history()
    {
        AgentEvent[] events =
        [
            new DebugTreeStartedEvent
            {
                DebugTreeId = "tree", DebugSessionId = "root", AdapterId = "adapter",
                EnvironmentId = "env", SemanticStartKind = DebugSemanticStartKind.DirectLaunch, AdapterStartMethod = DebugAdapterStartMethod.Launch, ExecutionPlannerId = "test", ThreadSequenceNumber = 1
            },
            new DebugChildSessionStartedEvent
            {
                DebugTreeId = "tree", DebugSessionId = "child", AdapterId = "adapter",
                ParentDebugSessionId = "root", AdapterStartMethod = DebugAdapterStartMethod.Attach, ThreadSequenceNumber = 2
            },
            new DebugSessionStoppedEvent
            {
                DebugTreeId = "tree", DebugSessionId = "child", AdapterId = "adapter",
                AdapterThreadId = 7, Reason = "breakpoint", ThreadSequenceNumber = 3
            },
            new DebugOutputAvailableEvent
            {
                DebugTreeId = "tree", DebugSessionId = "child", AdapterId = "adapter",
                FirstSequence = 1, LastSequence = 4, Category = "StandardError",
                ContentScope = "scope", ContentId = "content", ThreadSequenceNumber = 4
            },
            new DebugSessionContinuedEvent
            {
                DebugTreeId = "tree", DebugSessionId = "child", AdapterId = "adapter",
                AdapterThreadId = 7, ThreadSequenceNumber = 5
            },
            new DebugSessionExitedEvent
            {
                DebugTreeId = "tree", DebugSessionId = "child", AdapterId = "adapter",
                ExitCode = 9, ThreadSequenceNumber = 6
            },
            new DebugBreakpointChangedEvent
            {
                DebugTreeId = "tree", DebugSessionId = "child", AdapterId = "adapter",
                Reason = "changed", BreakpointId = 3, Verified = true, SourcePath = "/a.cs",
                Line = 10, ThreadSequenceNumber = 7
            },
            new DebugSessionSummaryEvent
            {
                DebugTreeId = "tree", DebugSessionId = "child", AdapterId = "adapter",
                FinalStatus = "Terminated", ExitCode = 9, DurationMilliseconds = 100,
                ChildSessionCount = 0, RetainedOutputBytes = 12, DroppedOutputRecords = 0,
                DroppedOutputBytes = 0, ProjectionFailures = 0, ThreadSequenceNumber = 8
            }
        ];

        var projection = DebugSessionProjector.Project(events);
        var tree = projection.Trees["tree"];
        tree.EnvironmentId.Should().Be("env");
        tree.Sessions["child"].ParentDebugSessionId.Should().Be("root");
        tree.Sessions["child"].Status.Should().Be("Terminated");
        tree.Sessions["child"].ExitCode.Should().Be(9);
        tree.Sessions["child"].BreakpointHistory.Should().ContainSingle();
        tree.Sessions["child"].FinalSummary.Should().NotBeNull();
        tree.Artifacts.Should().ContainSingle().Which.ContentId.Should().Be("content");
    }

    [Fact]
    public void Projector_ignores_unrelated_events()
    {
        DebugSessionProjector.Project([new UnrelatedEvent()])
            .Trees.Should().BeEmpty();
    }

    [Fact]
    public void Existing_thread_projector_ignores_debugger_events_harmlessly()
    {
        var debugEvent = new DebugSessionStoppedEvent
        {
            DebugTreeId = "tree", DebugSessionId = "debug", AdapterId = "adapter", Reason = "pause"
        };
        ThreadExecutionProjector.IsProjectionEvent(debugEvent).Should().BeFalse();
        ThreadExecutionProjector.Project("agent", "session", "thread", [debugEvent]).Should().BeEmpty();
    }

    private sealed record UnrelatedEvent : AgentEvent;
}
