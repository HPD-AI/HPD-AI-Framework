using FluentAssertions;
using HPD.Events;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Checkpointing;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Core.Checkpointing;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Graph.Tests.Advanced;

/// <summary>
/// Tests for Layered Suspension feature.
/// Covers all behavior matrix scenarios and edge cases.
/// </summary>
public class LayeredSuspensionTests
{
    #region Behavior Matrix Tests

    [Fact]
    public async Task HandleSuspendedAsync_WithCheckpointAndCoordinator_FullLayeredSuspension()
    {
        // Arrange: Checkpoint store + EventCoordinator + ActiveWait > 0
        var checkpointStore = new InMemoryCheckpointStore();
        var coordinator = new TestEventCoordinator();

        var graph = CreateSuspendingGraph();
        var services = CreateServicesWithSuspendingHandler(TimeSpan.FromSeconds(5));
        var context = new GraphContext("test-exec", graph, services);
        context.SetEventCoordinator(coordinator);

        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            checkpointStore: checkpointStore
        );

        // Start execution in background (will wait for response)
        var executionTask = Task.Run(async () =>
        {
            try
            {
                await orchestrator.ExecuteAsync(context);
            }
            catch (GraphSuspendedException)
            {
                // Expected if timeout
            }
        });

        // Poll for the approval request event with retry
        NodeApprovalRequestEvent? requestEvent = null;
        for (int i = 0; i < 50; i++)
        {
            await Task.Delay(100);
            requestEvent = coordinator.EmittedEvents.OfType<NodeApprovalRequestEvent>().FirstOrDefault();
            if (requestEvent != null) break;
        }
        requestEvent.Should().NotBeNull("should emit approval request");

        // Send approval response
        coordinator.SendResponse(requestEvent!.RequestId, new NodeApprovalResponseEvent
        {
            RequestId = requestEvent.RequestId,
            SourceName = "Test",
            Approved = true,
            ResumeData = new { ApprovedBy = "tester" }
        });

        // Wait for execution to complete
        await executionTask;

        // Assert
        var checkpoints = await checkpointStore.ListCheckpointsAsync("test-exec");
        checkpoints.Should().NotBeEmpty("checkpoint should be saved");

        context.Tags["suspend_outcome:suspender"].Should().Contain(SuspensionOutcome.Approved.ToString());
        context.CompletedNodes.Should().Contain("suspender");
    }

    [Fact]
    public async Task HandleSuspendedAsync_WithCheckpointNoCoordinator_CheckpointOnlyHalt()
    {
        // Arrange: Checkpoint store + No EventCoordinator
        var checkpointStore = new InMemoryCheckpointStore();

        var graph = CreateSuspendingGraph();
        var services = CreateServicesWithSuspendingHandler(TimeSpan.FromSeconds(5));
        var context = new GraphContext("test-exec", graph, services);
        // No EventCoordinator set

        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            checkpointStore: checkpointStore
        );

        // Act
        GraphSuspendedException? caughtException = null;
        try
        {
            await orchestrator.ExecuteAsync(context);
        }
        catch (GraphSuspendedException ex)
        {
            caughtException = ex;
        }

        // Assert
        caughtException.Should().NotBeNull();
        var checkpoints = await checkpointStore.ListCheckpointsAsync("test-exec");
        checkpoints.Should().NotBeEmpty("checkpoint should be saved even without coordinator");
    }

    [Fact]
    public async Task HandleSuspendedAsync_NoCheckpointWithCoordinator_EventOnlyMode()
    {
        // Arrange: No checkpoint store + EventCoordinator + ActiveWait > 0
        var coordinator = new TestEventCoordinator();

        var graph = CreateSuspendingGraph();
        var services = CreateServicesWithSuspendingHandler(TimeSpan.FromSeconds(5));
        var context = new GraphContext("test-exec", graph, services);
        context.SetEventCoordinator(coordinator);

        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            checkpointStore: null // No checkpoint store
        );

        // Start execution
        var executionTask = Task.Run(async () =>
        {
            try
            {
                await orchestrator.ExecuteAsync(context);
            }
            catch (GraphSuspendedException)
            {
                // Expected if timeout or denial
            }
        });

        // Wait for the approval request event to be emitted with polling
        NodeApprovalRequestEvent? requestEvent = null;
        for (int i = 0; i < 50; i++)
        {
            await Task.Delay(50);
            requestEvent = coordinator.EmittedEvents.OfType<NodeApprovalRequestEvent>().FirstOrDefault();
            if (requestEvent != null) break;
        }

        requestEvent.Should().NotBeNull("approval request should be emitted");

        coordinator.SendResponse(requestEvent!.RequestId, new NodeApprovalResponseEvent
        {
            RequestId = requestEvent.RequestId,
            SourceName = "Test",
            Approved = true
        });

        await executionTask;

        // Assert - event was emitted, execution continued
        coordinator.EmittedEvents.OfType<NodeApprovalRequestEvent>().Should().NotBeEmpty();
        context.CompletedNodes.Should().Contain("suspender");
    }

    [Fact]
    public async Task HandleSuspendedAsync_NoCheckpointNoCoordinator_ImmediateHalt()
    {
        // Arrange: No checkpoint store + No EventCoordinator
        var graph = CreateSuspendingGraph();
        var services = CreateServicesWithSuspendingHandler(TimeSpan.FromSeconds(5));
        var context = new GraphContext("test-exec", graph, services);
        // No coordinator, no checkpoint store

        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            checkpointStore: null
        );

        // Act & Assert
        var act = async () => await orchestrator.ExecuteAsync(context);
        await act.Should().ThrowAsync<GraphSuspendedException>();
    }

    #endregion

    #region Approval Response Tests

    [Fact]
    public async Task HandleSuspendedAsync_OnApproval_ContinuesExecution()
    {
        // Arrange
        var coordinator = new TestEventCoordinator();
        var graph = CreateSuspendingGraph();
        var services = CreateServicesWithSuspendingHandler(TimeSpan.FromSeconds(10));
        var context = new GraphContext("test-exec", graph, services);
        context.SetEventCoordinator(coordinator);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act - start execution
        var executionTask = Task.Run(async () =>
        {
            await orchestrator.ExecuteAsync(context);
        });

        // Wait for the approval request event to be emitted
        NodeApprovalRequestEvent? requestEvent = null;
        for (int i = 0; i < 50; i++)
        {
            await Task.Delay(50);
            requestEvent = coordinator.EmittedEvents.OfType<NodeApprovalRequestEvent>().FirstOrDefault();
            if (requestEvent != null) break;
        }

        requestEvent.Should().NotBeNull("approval request should be emitted");

        coordinator.SendResponse(requestEvent!.RequestId, new NodeApprovalResponseEvent
        {
            RequestId = requestEvent.RequestId,
            SourceName = "Test",
            Approved = true,
            ResumeData = new { ApprovedAt = DateTime.UtcNow }
        });

        await executionTask;

        // Assert
        context.CompletedNodes.Should().Contain("suspender");
        context.Tags["suspend_outcome:suspender"].Should().Contain(SuspensionOutcome.Approved.ToString());
    }

    [Fact]
    public async Task HandleSuspendedAsync_OnDenial_HaltsExecution()
    {
        // Arrange
        var coordinator = new TestEventCoordinator();
        var graph = CreateSuspendingGraph();
        var services = CreateServicesWithSuspendingHandler(TimeSpan.FromSeconds(10));
        var context = new GraphContext("test-exec", graph, services);
        context.SetEventCoordinator(coordinator);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        GraphSuspendedException? caughtException = null;
        var executionTask = Task.Run(async () =>
        {
            try
            {
                await orchestrator.ExecuteAsync(context);
            }
            catch (GraphSuspendedException ex)
            {
                caughtException = ex;
            }
        });

        // Wait for the approval request event to be emitted
        NodeApprovalRequestEvent? requestEvent = null;
        for (int i = 0; i < 50; i++)
        {
            await Task.Delay(50);
            requestEvent = coordinator.EmittedEvents.OfType<NodeApprovalRequestEvent>().FirstOrDefault();
            if (requestEvent != null) break;
        }

        requestEvent.Should().NotBeNull("approval request should be emitted");

        coordinator.SendResponse(requestEvent!.RequestId, new NodeApprovalResponseEvent
        {
            RequestId = requestEvent.RequestId,
            SourceName = "Test",
            Approved = false,
            Reason = "Not authorized"
        });

        await executionTask;

        // Assert
        caughtException.Should().NotBeNull();
        context.Tags["suspend_outcome:suspender"].Should().Contain(SuspensionOutcome.Denied.ToString());
        context.CompletedNodes.Should().NotContain("suspender");
    }

    [Fact]
    public async Task HandleSuspendedAsync_OnTimeout_HaltsAndEmitsTimeoutEvent()
    {
        // Arrange
        var coordinator = new TestEventCoordinator();
        var graph = CreateSuspendingGraph();
        var services = CreateServicesWithSuspendingHandler(TimeSpan.FromMilliseconds(100)); // Short timeout
        var context = new GraphContext("test-exec", graph, services);
        context.SetEventCoordinator(coordinator);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        GraphSuspendedException? caughtException = null;

        // Act - don't send response, let it timeout
        try
        {
            await orchestrator.ExecuteAsync(context);
        }
        catch (GraphSuspendedException ex)
        {
            caughtException = ex;
        }

        // Assert
        caughtException.Should().NotBeNull();
        context.Tags["suspend_outcome:suspender"].Should().Contain(SuspensionOutcome.TimedOut.ToString());

        var timeoutEvents = coordinator.EmittedEvents.OfType<NodeApprovalTimeoutEvent>().ToList();
        timeoutEvents.Should().HaveCount(1);
        timeoutEvents[0].NodeId.Should().Be("suspender");
    }

    #endregion

    #region Immediate Suspend Tests

    [Fact]
    public async Task HandleSuspendedAsync_ImmediateSuspend_NoWait()
    {
        // Arrange
        var coordinator = new TestEventCoordinator();
        var checkpointStore = new InMemoryCheckpointStore();

        var graph = CreateSuspendingGraphWithOptions(SuspensionOptions.ImmediateSuspend);
        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);
        context.SetEventCoordinator(coordinator);

        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            checkpointStore: checkpointStore
        );

        // Act
        GraphSuspendedException? caughtException = null;
        try
        {
            await orchestrator.ExecuteAsync(context);
        }
        catch (GraphSuspendedException ex)
        {
            caughtException = ex;
        }

        // Assert
        caughtException.Should().NotBeNull();

        // Event should be emitted
        coordinator.EmittedEvents.OfType<NodeApprovalRequestEvent>().Should().HaveCount(1);

        // No timeout event (didn't wait)
        coordinator.EmittedEvents.OfType<NodeApprovalTimeoutEvent>().Should().BeEmpty();

        // Checkpoint saved
        var checkpoints = await checkpointStore.ListCheckpointsAsync("test-exec");
        checkpoints.Should().NotBeEmpty();
    }

    #endregion

    #region Checkpoint Failure Tests

    [Fact]
    public async Task HandleSuspendedAsync_CheckpointSaveFailure_StillContinues()
    {
        // Arrange
        var failingStore = new FailingCheckpointStore();
        var coordinator = new TestEventCoordinator();

        var graph = CreateSuspendingGraph();
        var services = CreateServicesWithSuspendingHandler(TimeSpan.FromSeconds(5));
        var context = new GraphContext("test-exec", graph, services);
        context.SetEventCoordinator(coordinator);

        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            checkpointStore: failingStore
        );

        var executionTask = Task.Run(async () =>
        {
            try
            {
                await orchestrator.ExecuteAsync(context);
            }
            catch (GraphSuspendedException)
            {
                // Could happen on timeout
            }
        });

        // Wait for the approval request event to be emitted
        NodeApprovalRequestEvent? requestEvent = null;
        for (int i = 0; i < 50; i++)
        {
            await Task.Delay(50);
            requestEvent = coordinator.EmittedEvents.OfType<NodeApprovalRequestEvent>().FirstOrDefault();
            if (requestEvent != null) break;
        }

        requestEvent.Should().NotBeNull("approval request should be emitted");

        coordinator.SendResponse(requestEvent!.RequestId, new NodeApprovalResponseEvent
        {
            RequestId = requestEvent.RequestId,
            SourceName = "Test",
            Approved = true
        });

        await executionTask;

        // Assert - suspension still worked even though checkpoint failed
        context.CompletedNodes.Should().Contain("suspender");
    }

    #endregion

    #region Helper Methods

    private static HPDAgent.Graph.Abstractions.Graph.Graph CreateSuspendingGraph()
    {
        return new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("suspender", "ConfigurableSuspendingHandler")
            .AddEndNode()
            .AddEdge("start", "suspender")
            .AddEdge("suspender", "end")
            .Build();
    }

    private static HPDAgent.Graph.Abstractions.Graph.Graph CreateSuspendingGraphWithOptions(SuspensionOptions options)
    {
        return new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("suspender", "SuspendingHandler", suspensionOptions: options)
            .AddEndNode()
            .AddEdge("start", "suspender")
            .AddEdge("suspender", "end")
            .Build();
    }

    private static IServiceProvider CreateServicesWithSuspendingHandler(TimeSpan timeout)
    {
        return TestServiceProvider.Create(services =>
        {
            services.AddTransient<IGraphNodeHandler<GraphContext>>(sp =>
                new ConfigurableSuspendingHandler(timeout));
        });
    }

    #endregion
}

#region Test Infrastructure

/// <summary>
/// Test handler that suspends with configurable timeout.
/// </summary>
public class ConfigurableSuspendingHandler : IGraphNodeHandler<GraphContext>
{
    private readonly TimeSpan _timeout;

    public ConfigurableSuspendingHandler(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    public string HandlerName => "ConfigurableSuspendingHandler";

    public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
    {
        var token = Guid.NewGuid().ToString();
        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Suspended.ForHumanApproval(
                suspendToken: token,
                message: "Waiting for approval"
            )
        );
    }
}

/// <summary>
/// Test event coordinator for capturing and responding to events.
/// </summary>
public class TestEventCoordinator : IEventCoordinator
{
    private readonly List<Event> _emittedEvents = new();
    private readonly Dictionary<string, TaskCompletionSource<Event>> _waiters = new();
    private readonly object _lock = new();

    public IReadOnlyList<Event> EmittedEvents => _emittedEvents;

    public void Emit(Event evt)
    {
        lock (_lock)
        {
            _emittedEvents.Add(evt);
        }
    }

    public void EmitUpstream(Event evt)
    {
        Emit(evt);
    }

    public bool TryRead(out Event? evt)
    {
        evt = null;
        return false;
    }

    public async IAsyncEnumerable<Event> ReadAllAsync(CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public void SetParent(IEventCoordinator parent) { }

    public Task<TResponse> WaitForResponseAsync<TResponse>(string requestId, TimeSpan timeout, CancellationToken ct = default)
        where TResponse : Event
    {
        var tcs = new TaskCompletionSource<Event>();

        lock (_lock)
        {
            _waiters[requestId] = tcs;
        }

        // Set up timeout
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        cts.Token.Register(() =>
        {
            tcs.TrySetException(new TimeoutException($"Timeout waiting for response to {requestId}"));
        });

        return tcs.Task.ContinueWith(t =>
        {
            if (t.IsFaulted)
                throw t.Exception!.InnerException!;
            return (TResponse)t.Result;
        });
    }

    public void SendResponse(string requestId, Event response)
    {
        TaskCompletionSource<Event>? tcs;
        lock (_lock)
        {
            _waiters.TryGetValue(requestId, out tcs);
        }

        tcs?.TrySetResult(response);
    }

    public IStreamRegistry Streams => throw new NotImplementedException();
}

/// <summary>
/// Checkpoint store that always fails.
/// </summary>
public class FailingCheckpointStore : IGraphCheckpointStore
{
    public CheckpointRetentionMode RetentionMode => CheckpointRetentionMode.LatestOnly;

    public Task SaveCheckpointAsync(GraphCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Simulated checkpoint failure");
    }

    public Task<GraphCheckpoint?> LoadLatestCheckpointAsync(string executionId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<GraphCheckpoint?>(null);
    }

    public Task<GraphCheckpoint?> LoadCheckpointAsync(string checkpointId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<GraphCheckpoint?>(null);
    }

    public Task DeleteCheckpointsAsync(string executionId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GraphCheckpoint>> ListCheckpointsAsync(string executionId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<GraphCheckpoint>>(Array.Empty<GraphCheckpoint>());
    }
}

#endregion

#region Extension Methods

public static class GraphContextExtensions
{
    public static void SetEventCoordinator(this GraphContext context, IEventCoordinator coordinator)
    {
        // Use reflection to set the EventCoordinator since it's init-only
        var property = typeof(GraphContext).GetProperty(nameof(GraphContext.EventCoordinator));
        property?.SetValue(context, coordinator);
    }
}

#endregion
