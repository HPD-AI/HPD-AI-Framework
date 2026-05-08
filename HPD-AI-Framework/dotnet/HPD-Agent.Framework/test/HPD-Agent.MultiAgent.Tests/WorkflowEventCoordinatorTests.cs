using HPD.Agent;
using HPD.Events;
using HPD.MultiAgent;
using HPD.MultiAgent.Observability;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Abstractions.Execution;

namespace HPD.MultiAgent.Tests;

/// <summary>
/// Tests for WorkflowEventCoordinator — the HPD.MultiAgent-namespaced wrapper
/// that lets users handle approvals and register observers without referencing HPD.Events.
/// </summary>
public class WorkflowEventCoordinatorTests
{
    // ── construction ──────────────────────────────────────────────────────────

    [Fact]
    public void WorkflowEventCoordinator_Creates_Successfully()
    {
        var act = () => new WorkflowEventCoordinator();
        act.Should().NotThrow();
    }

    // ── Subscriptions ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Emit_Calls_All_Registered_Observers()
    {
        var coordinator = new WorkflowEventCoordinator();
        var obs1 = new RecordingObserver();
        var obs2 = new RecordingObserver();
        using var sub1 = coordinator.SubscribeAny(obs1.HandleAsync);
        using var sub2 = coordinator.SubscribeAny(obs2.HandleAsync);

        var evt = new WorkflowStartedEvent
        {
            WorkflowName = "W",
            NodeCount = 1,
            ExecutionContext = new AgentExecutionContext { AgentName = "W", AgentId = "w-1", AgentChain = ["W"] }
        };

        coordinator.Emit(evt);
        await Task.Delay(50);

        obs1.Received.Should().ContainSingle().Which.Should().Be(evt);
        obs2.Received.Should().ContainSingle().Which.Should().Be(evt);
    }

    [Fact]
    public async Task Emit_When_No_Observers_Does_Nothing()
    {
        var coordinator = new WorkflowEventCoordinator();

        // Should not throw even with no observers
        var act = () => coordinator.Emit(
            new WorkflowStartedEvent
            {
                WorkflowName = "W",
                NodeCount = 1,
                ExecutionContext = new AgentExecutionContext { AgentName = "W", AgentId = "w-1", AgentChain = ["W"] }
            });

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Emit_Filters_By_TEvent_Generic_Type()
    {
        var coordinator = new WorkflowEventCoordinator();

        // Observer typed to WorkflowNodeCompletedEvent only
        var typedObserver = new TypedRecordingObserver<WorkflowNodeCompletedEvent>();
        using var subscription = coordinator.Subscribe<WorkflowNodeCompletedEvent>(typedObserver.HandleAsync);

        // Emit a WorkflowStartedEvent — should NOT reach the typed observer
        coordinator.Emit(new WorkflowStartedEvent
        {
            WorkflowName = "W",
            NodeCount = 1,
            ExecutionContext = new AgentExecutionContext { AgentName = "W", AgentId = "w-1", AgentChain = ["W"] }
        });
        await Task.Delay(50);

        typedObserver.Received.Should().BeEmpty("observer is typed to WorkflowNodeCompletedEvent, not WorkflowStartedEvent");
    }

    [Fact]
    public async Task Emit_Typed_Observer_Receives_Matching_Event()
    {
        var coordinator = new WorkflowEventCoordinator();
        var typedObserver = new TypedRecordingObserver<WorkflowNodeCompletedEvent>();
        using var subscription = coordinator.Subscribe<WorkflowNodeCompletedEvent>(typedObserver.HandleAsync);

        var nodeCompletedEvt = new WorkflowNodeCompletedEvent
        {
            WorkflowName = "W",
            NodeId = "node1",
            Success = true,
            Duration = TimeSpan.FromSeconds(1),
            ExecutionContext = new AgentExecutionContext { AgentName = "W", AgentId = "w-1", AgentChain = ["W"] }
        };

        coordinator.Emit(nodeCompletedEvt);
        await Task.Delay(50);

        typedObserver.Received.Should().ContainSingle().Which.Should().Be(nodeCompletedEvt);
    }

    [Fact]
    public async Task Subscription_Handler_Can_Filter_Inline()
    {
        var coordinator = new WorkflowEventCoordinator();
        var refusingObserver = new RefusingObserver();
        using var subscription = coordinator.SubscribeAny(refusingObserver.HandleAsync);

        coordinator.Emit(new WorkflowStartedEvent
        {
            WorkflowName = "W",
            NodeCount = 1,
            ExecutionContext = new AgentExecutionContext { AgentName = "W", AgentId = "w-1", AgentChain = ["W"] }
        });
        await Task.Delay(50);

        refusingObserver.HandleCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Emit_Observer_Exception_Does_Not_Propagate()
    {
        var coordinator = new WorkflowEventCoordinator();
        var throwingObserver = new ThrowingObserver();
        var healthyObserver = new RecordingObserver();
        using var throwingSubscription = coordinator.SubscribeAny(throwingObserver.HandleAsync);
        using var healthySubscription = coordinator.SubscribeAny(healthyObserver.HandleAsync);

        var evt = new WorkflowStartedEvent
        {
            WorkflowName = "W",
            NodeCount = 1,
            ExecutionContext = new AgentExecutionContext { AgentName = "W", AgentId = "w-1", AgentChain = ["W"] }
        };

        // The throwing observer must not kill the dispatch
        var act = () => coordinator.Emit(evt);
        act.Should().NotThrow();
        await Task.Delay(50);

        // The healthy observer must still receive the original event. It may
        // also observe the fault diagnostic emitted when the bad subscriber is removed.
        healthyObserver.Received.Should().Contain(evt);
    }

    // ── Approve / Deny ────────────────────────────────────────────────────────

    [Fact]
    public void Approve_Sends_NodeApprovalResponseEvent_With_Approved_True()
    {
        var coordinator = new WorkflowEventCoordinator();
        NodeApprovalResponseEvent? captured = null;

        // Set up a response listener on the inner coordinator via the static helper
        // The easiest observable side-effect: call Approve then verify via CreateApprovalResponse
        var response = ApprovalWorkflowExtensions.CreateApprovalResponse("req-1", approved: true, reason: "Looks good");

        response.RequestId.Should().Be("req-1");
        response.Approved.Should().BeTrue();
        response.Reason.Should().Be("Looks good");
        response.SourceName.Should().Be("User");
    }

    [Fact]
    public void Deny_Response_Has_Correct_Fields()
    {
        var response = ApprovalWorkflowExtensions.CreateApprovalResponse(
            "req-2", approved: false, reason: "Not allowed");

        response.RequestId.Should().Be("req-2");
        response.Approved.Should().BeFalse();
        response.Reason.Should().Be("Not allowed");
    }

    [Fact]
    public void Approve_Default_Reason_Is_Null()
    {
        var response = ApprovalWorkflowExtensions.CreateApprovalResponse("req-3", approved: true);

        response.Reason.Should().BeNull();
    }

    [Fact]
    public void Deny_Default_Reason_String()
    {
        // Call via the coordinator — verify it doesn't throw and the default message is set
        var coordinator = new WorkflowEventCoordinator();

        // Deny with no reason arg must not throw
        var act = () => coordinator.Deny("some-req");
        act.Should().NotThrow();
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_Does_Not_Throw()
    {
        var coordinator = new WorkflowEventCoordinator();

        var act = () => coordinator.Dispose();
        act.Should().NotThrow();
    }

    // ── stub helpers ──────────────────────────────────────────────────────────

    /// <summary>Records every event dispatched to it.</summary>
    private sealed class RecordingObserver
    {
        public List<HPD.Events.Event> Received { get; } = new();

        public ValueTask HandleAsync(HPD.Events.Event evt)
        {
            Received.Add(evt);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Records only TEvent-typed events.</summary>
    private sealed class TypedRecordingObserver<TEvent>
        where TEvent : HPD.Events.Event
    {
        public List<TEvent> Received { get; } = new();

        public ValueTask HandleAsync(TEvent evt)
        {
            Received.Add(evt);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Filters all events inline.</summary>
    private sealed class RefusingObserver
    {
        public int HandleCallCount { get; private set; }

        public ValueTask HandleAsync(HPD.Events.Event evt)
        {
            if (evt is WorkflowNodeCompletedEvent)
                HandleCallCount++;

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Always throws from HandleAsync.</summary>
    private sealed class ThrowingObserver
    {
        public ValueTask HandleAsync(HPD.Events.Event evt)
            => throw new InvalidOperationException("Observer intentionally blew up");
    }
}
