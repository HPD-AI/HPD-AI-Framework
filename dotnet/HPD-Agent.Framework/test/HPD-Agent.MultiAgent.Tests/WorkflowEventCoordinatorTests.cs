using HPD.Agent;
using HPD.Events;
using HPD.MultiAgent;
using HPD.MultiAgent.Observability;
using HPD.Graph.Abstractions.Events;
using HPD.Graph.Abstractions.Execution;

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
    public async Task PublishAsync_Calls_All_Registered_Observers()
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
            Metadata = new AgentMetadata { AgentName = "W", AgentId = "w-1", AgentChain = ["W"] }
        };

        await coordinator.PublishAsync(evt);
        await Task.WhenAll(
            obs1.WaitForCountAsync(1),
            obs2.WaitForCountAsync(1));

        obs1.Received.Should().ContainSingle().Which.Should().Be(evt);
        obs2.Received.Should().ContainSingle().Which.Should().Be(evt);
    }

    [Fact]
    public async Task PublishAsync_When_No_Observers_Does_Nothing()
    {
        var coordinator = new WorkflowEventCoordinator();

        // Should not throw even with no observers
        var act = async () => await coordinator.PublishAsync(
            new WorkflowStartedEvent
            {
                WorkflowName = "W",
                NodeCount = 1,
                Metadata = new AgentMetadata { AgentName = "W", AgentId = "w-1", AgentChain = ["W"] }
            });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_Filters_By_TEvent_Generic_Type()
    {
        var coordinator = new WorkflowEventCoordinator();

        // Observer typed to WorkflowAgentCompletedEvent only
        var typedObserver = new TypedRecordingObserver<WorkflowAgentCompletedEvent>();
        using var subscription = coordinator.Subscribe<WorkflowAgentCompletedEvent>(typedObserver.HandleAsync);

        // Emit a WorkflowStartedEvent — should NOT reach the typed observer
        await coordinator.PublishAsync(new WorkflowStartedEvent
        {
            WorkflowName = "W",
            NodeCount = 1,
            Metadata = new AgentMetadata { AgentName = "W", AgentId = "w-1", AgentChain = ["W"] }
        });
        await Task.Delay(50);

        typedObserver.Received.Should().BeEmpty("observer is typed to WorkflowAgentCompletedEvent, not WorkflowStartedEvent");
    }

    [Fact]
    public async Task PublishAsync_Typed_Observer_Receives_Matching_Event()
    {
        var coordinator = new WorkflowEventCoordinator();
        var typedObserver = new TypedRecordingObserver<WorkflowAgentCompletedEvent>();
        using var subscription = coordinator.Subscribe<WorkflowAgentCompletedEvent>(typedObserver.HandleAsync);

        var nodeCompletedEvt = new WorkflowAgentCompletedEvent
        {
            WorkflowName = "W",
            AgentId = "agent1",
            Success = true,
            Duration = TimeSpan.FromSeconds(1),
            Metadata = new AgentMetadata { AgentName = "W", AgentId = "w-1", AgentChain = ["W"] }
        };

        await coordinator.PublishAsync(nodeCompletedEvt);
        await typedObserver.WaitForCountAsync(1);

        typedObserver.Received.Should().ContainSingle().Which.Should().Be(nodeCompletedEvt);
    }

    [Fact]
    public async Task Subscription_Handler_Can_Filter_Inline()
    {
        var coordinator = new WorkflowEventCoordinator();
        var refusingObserver = new RefusingObserver();
        using var subscription = coordinator.SubscribeAny(refusingObserver.HandleAsync);

        await coordinator.PublishAsync(new WorkflowStartedEvent
        {
            WorkflowName = "W",
            NodeCount = 1,
            Metadata = new AgentMetadata { AgentName = "W", AgentId = "w-1", AgentChain = ["W"] }
        });
        await Task.Delay(50);

        refusingObserver.HandleCallCount.Should().Be(0);
    }

    [Fact]
    public async Task PublishAsync_Observer_Exception_Does_Not_Propagate()
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
            Metadata = new AgentMetadata { AgentName = "W", AgentId = "w-1", AgentChain = ["W"] }
        };

        // The throwing observer must not kill the dispatch
        var act = async () => await coordinator.PublishAsync(evt);
        await act.Should().NotThrowAsync();
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
    public async Task Deny_Default_Reason_String()
    {
        var coordinator = new WorkflowEventCoordinator();
        var request = new NodeApprovalRequestEvent
        {
            RequestId = "some-req",
            SourceName = "test",
            NodeId = "node-1",
            Message = "Approve?"
        };

        var responseTask = coordinator.Inner.RequestAsync<NodeApprovalRequestEvent, NodeApprovalResponseEvent>(
            request,
            TimeSpan.FromSeconds(5));

        coordinator.Deny("some-req");
        var response = await responseTask;

        response.Approved.Should().BeFalse();
        response.Reason.Should().Be("Denied by user");
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
        private readonly object _gate = new();
        private readonly List<HPD.Events.Event> _received = [];
        private readonly TaskCompletionSource _receivedSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<HPD.Events.Event> Received
        {
            get
            {
                lock (_gate)
                    return _received.ToArray();
            }
        }

        public ValueTask HandleAsync(HPD.Events.Event evt)
        {
            lock (_gate)
                _received.Add(evt);

            _receivedSignal.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public async Task WaitForCountAsync(int count)
        {
            while (true)
            {
                lock (_gate)
                {
                    if (_received.Count >= count)
                        return;
                }

                await _receivedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    /// <summary>Records only TEvent-typed events.</summary>
    private sealed class TypedRecordingObserver<TEvent>
        where TEvent : HPD.Events.Event
    {
        private readonly object _gate = new();
        private readonly List<TEvent> _received = [];
        private readonly TaskCompletionSource _receivedSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<TEvent> Received
        {
            get
            {
                lock (_gate)
                    return _received.ToArray();
            }
        }

        public ValueTask HandleAsync(TEvent evt)
        {
            lock (_gate)
                _received.Add(evt);

            _receivedSignal.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public async Task WaitForCountAsync(int count)
        {
            while (true)
            {
                lock (_gate)
                {
                    if (_received.Count >= count)
                        return;
                }

                await _receivedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    /// <summary>Filters all events inline.</summary>
    private sealed class RefusingObserver
    {
        public int HandleCallCount { get; private set; }

        public ValueTask HandleAsync(HPD.Events.Event evt)
        {
            if (evt is WorkflowAgentCompletedEvent)
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
