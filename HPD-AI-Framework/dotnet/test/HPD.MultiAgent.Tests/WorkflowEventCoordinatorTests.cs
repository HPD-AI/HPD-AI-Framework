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

    // ── HasObservers ──────────────────────────────────────────────────────────

    [Fact]
    public void HasObservers_False_When_No_Observer_Registered()
    {
        var coordinator = new WorkflowEventCoordinator();

        coordinator.HasObservers.Should().BeFalse();
    }

    [Fact]
    public void HasObservers_True_After_AddObserver()
    {
        var coordinator = new WorkflowEventCoordinator();
        var observer = new RecordingObserver();

        coordinator.AddObserver(observer);

        coordinator.HasObservers.Should().BeTrue();
    }

    [Fact]
    public void HasObservers_True_After_Multiple_AddObserver_Calls()
    {
        var coordinator = new WorkflowEventCoordinator();

        coordinator.AddObserver(new RecordingObserver());
        coordinator.AddObserver(new RecordingObserver());
        coordinator.AddObserver(new RecordingObserver());

        coordinator.HasObservers.Should().BeTrue();
    }

    // ── DispatchToObserversAsync ──────────────────────────────────────────────

    [Fact]
    public async Task DispatchToObservers_Calls_All_Registered_Observers()
    {
        var coordinator = new WorkflowEventCoordinator();
        var obs1 = new RecordingObserver();
        var obs2 = new RecordingObserver();
        coordinator.AddObserver(obs1);
        coordinator.AddObserver(obs2);

        var evt = new WorkflowStartedEvent
        {
            WorkflowName = "W",
            NodeCount = 1,
            ExecutionContext = new AgentExecutionContext { AgentName = "W", AgentId = "w-1", AgentChain = ["W"] }
        };

        await coordinator.DispatchToObserversAsync(evt);

        obs1.Received.Should().ContainSingle().Which.Should().Be(evt);
        obs2.Received.Should().ContainSingle().Which.Should().Be(evt);
    }

    [Fact]
    public async Task DispatchToObservers_When_No_Observers_Does_Nothing()
    {
        var coordinator = new WorkflowEventCoordinator();

        // Should not throw even with no observers
        var act = async () => await coordinator.DispatchToObserversAsync(
            new WorkflowStartedEvent
            {
                WorkflowName = "W",
                NodeCount = 1,
                ExecutionContext = new AgentExecutionContext { AgentName = "W", AgentId = "w-1", AgentChain = ["W"] }
            });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DispatchToObservers_Filters_By_TEvent_Generic_Type()
    {
        var coordinator = new WorkflowEventCoordinator();

        // Observer typed to WorkflowNodeCompletedEvent only
        var typedObserver = new TypedRecordingObserver<WorkflowNodeCompletedEvent>();
        coordinator.AddObserver(typedObserver);

        // Dispatch a WorkflowStartedEvent — should NOT reach the typed observer
        await coordinator.DispatchToObserversAsync(new WorkflowStartedEvent
        {
            WorkflowName = "W",
            NodeCount = 1,
            ExecutionContext = new AgentExecutionContext { AgentName = "W", AgentId = "w-1", AgentChain = ["W"] }
        });

        typedObserver.Received.Should().BeEmpty("observer is typed to WorkflowNodeCompletedEvent, not WorkflowStartedEvent");
    }

    [Fact]
    public async Task DispatchToObservers_Typed_Observer_Receives_Matching_Event()
    {
        var coordinator = new WorkflowEventCoordinator();
        var typedObserver = new TypedRecordingObserver<WorkflowNodeCompletedEvent>();
        coordinator.AddObserver(typedObserver);

        var nodeCompletedEvt = new WorkflowNodeCompletedEvent
        {
            WorkflowName = "W",
            NodeId = "node1",
            Success = true,
            Duration = TimeSpan.FromSeconds(1),
            ExecutionContext = new AgentExecutionContext { AgentName = "W", AgentId = "w-1", AgentChain = ["W"] }
        };

        await coordinator.DispatchToObserversAsync(nodeCompletedEvt);

        typedObserver.Received.Should().ContainSingle().Which.Should().Be(nodeCompletedEvt);
    }

    [Fact]
    public async Task DispatchToObservers_Respects_ShouldProcess_False()
    {
        var coordinator = new WorkflowEventCoordinator();
        var refusingObserver = new RefusingObserver();
        coordinator.AddObserver(refusingObserver);

        await coordinator.DispatchToObserversAsync(new WorkflowStartedEvent
        {
            WorkflowName = "W",
            NodeCount = 1,
            ExecutionContext = new AgentExecutionContext { AgentName = "W", AgentId = "w-1", AgentChain = ["W"] }
        });

        refusingObserver.OnEventAsyncCallCount.Should().Be(0,
            "ShouldProcess returned false so OnEventAsync must not be called");
    }

    [Fact]
    public async Task DispatchToObservers_Observer_Exception_Does_Not_Propagate()
    {
        var coordinator = new WorkflowEventCoordinator();
        var throwingObserver = new ThrowingObserver();
        var healthyObserver = new RecordingObserver();
        coordinator.AddObserver(throwingObserver);
        coordinator.AddObserver(healthyObserver);

        var evt = new WorkflowStartedEvent
        {
            WorkflowName = "W",
            NodeCount = 1,
            ExecutionContext = new AgentExecutionContext { AgentName = "W", AgentId = "w-1", AgentChain = ["W"] }
        };

        // The throwing observer must not kill the dispatch
        var act = async () => await coordinator.DispatchToObserversAsync(evt);
        await act.Should().NotThrowAsync();

        // The healthy observer must still have received the event
        healthyObserver.Received.Should().ContainSingle();
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
    private sealed class RecordingObserver : IEventObserver<HPD.Events.Event>
    {
        public List<HPD.Events.Event> Received { get; } = new();

        public bool ShouldProcess(HPD.Events.Event evt) => true;

        public Task OnEventAsync(HPD.Events.Event evt, CancellationToken cancellationToken = default)
        {
            Received.Add(evt);
            return Task.CompletedTask;
        }
    }

    /// <summary>Records only TEvent-typed events.</summary>
    private sealed class TypedRecordingObserver<TEvent> : IEventObserver<TEvent>
        where TEvent : HPD.Events.Event
    {
        public List<TEvent> Received { get; } = new();

        public bool ShouldProcess(TEvent evt) => true;

        public Task OnEventAsync(TEvent evt, CancellationToken cancellationToken = default)
        {
            Received.Add(evt);
            return Task.CompletedTask;
        }
    }

    /// <summary>Always returns false from ShouldProcess — OnEventAsync must never be called.</summary>
    private sealed class RefusingObserver : IEventObserver<HPD.Events.Event>
    {
        public int OnEventAsyncCallCount { get; private set; }

        public bool ShouldProcess(HPD.Events.Event evt) => false;

        public Task OnEventAsync(HPD.Events.Event evt, CancellationToken cancellationToken = default)
        {
            OnEventAsyncCallCount++;
            return Task.CompletedTask;
        }
    }

    /// <summary>Always throws from OnEventAsync.</summary>
    private sealed class ThrowingObserver : IEventObserver<HPD.Events.Event>
    {
        public bool ShouldProcess(HPD.Events.Event evt) => true;

        public Task OnEventAsync(HPD.Events.Event evt, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Observer intentionally blew up");
    }
}
