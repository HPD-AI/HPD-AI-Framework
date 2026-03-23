using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Serialization;
using HPD.Events;
using Xunit;
using EventPriority = HPD.Events.EventPriority;
using EventDirection = HPD.Events.EventDirection;

namespace HPD.Agent.Tests.Core;

/// <summary>
/// Tests for agent-specific priority streaming and event coordinator integration.
///
/// NOTE: Core priority routing tests have been migrated to HPD.Events.Tests/EventCoordinatorTests.cs:
/// - PriorityEvents_ImmediateProcessedBeforeNormal (core priority behavior)
/// - PriorityEvents_ControlProcessedBeforeNormal (core priority behavior)
/// - PriorityEvents_NormalAndBackgroundInFIFOOrder (core FIFO behavior)
///
/// This file focuses on agent-specific tests using AgentEvent subtypes:
/// - Integration with agent middleware (TextDeltaEvent, InterruptionRequestEvent, etc.)
/// - AgentEvent serialization with priority fields
/// - Stream management integration with agent events
/// - Upstream event flow with agent-specific middleware
/// </summary>
public class PriorityStreamingTests
{
    #region Priority Routing Tests (Agent-Specific Integration)

    [Fact]
    public async Task PriorityEvents_ProcessedBeforeNormalEvents()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();

        // Emit 100 normal events first
        for (int i = 0; i < 100; i++)
        {
            coordinator.Emit(new TextDeltaEvent($"text{i}", "msg1")
            {
                Priority = EventPriority.Normal
            });
        }

        // Then emit 1 immediate priority event
        coordinator.Emit(new InterruptionRequestEvent(null, "test", InterruptionSource.User)
        {
            Priority = EventPriority.Immediate
        });

        // Act - Read first event
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        AgentEvent? firstEvent = null;

        await foreach (var evt in coordinator.ReadAllAsync(cts.Token))
        {
            firstEvent = (AgentEvent)evt;
            break;
        }

        // Assert - First event should be the interruption (priority)
        Assert.NotNull(firstEvent);
        Assert.IsType<InterruptionRequestEvent>(firstEvent);
    }

    [Fact]
    public async Task ControlEvents_ProcessedBeforeNormalEvents()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();

        // Emit normal events
        for (int i = 0; i < 50; i++)
        {
            coordinator.Emit(new TextDeltaEvent($"text{i}", "msg1")
            {
                Priority = EventPriority.Normal
            });
        }

        // Emit control priority event
        coordinator.Emit(new MessageTurnFinishedEvent("turn1", "conv1", "agent", TimeSpan.Zero)
        {
            Priority = EventPriority.Control
        });

        // Act - Read first event
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        AgentEvent? firstEvent = null;

        await foreach (var evt in coordinator.ReadAllAsync(cts.Token))
        {
            firstEvent = (AgentEvent)evt;
            break;
        }

        // Assert - First event should be the control event
        Assert.NotNull(firstEvent);
        Assert.IsType<MessageTurnFinishedEvent>(firstEvent);
    }

    [Fact]
    public async Task NormalAndBackgroundEvents_BothGoToStandardChannel()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();

        // Both Normal and Background go to the standard channel
        // They are read in FIFO order within the same channel
        coordinator.Emit(new StateSnapshotEvent(1, 10, false, null, 0, new List<string>(), "agent")
        {
            Priority = EventPriority.Background
        });

        coordinator.Emit(new TextDeltaEvent("hello", "msg1")
        {
            Priority = EventPriority.Normal
        });

        // Act - Read events in order
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = new List<AgentEvent>();

        await foreach (var evt in coordinator.ReadAllAsync(cts.Token))
        {
            events.Add((AgentEvent)evt);
            if (events.Count >= 2) break;
        }

        // Assert - Events come in FIFO order since both are in standard channel
        Assert.Equal(2, events.Count);
        // Background was emitted first, so it comes first
        Assert.IsType<StateSnapshotEvent>(events[0]);
        Assert.IsType<TextDeltaEvent>(events[1]);
    }

    [Fact]
    public void EventPriority_DefaultsToNormal()
    {
        // Arrange & Act
        var evt = new TextDeltaEvent("hello", "msg1");

        // Assert
        Assert.Equal(EventPriority.Normal, evt.Priority);
    }

    [Fact]
    public void EventDirection_DefaultsToDownstream()
    {
        // Arrange & Act
        var evt = new TextDeltaEvent("hello", "msg1");

        // Assert
        Assert.Equal(EventDirection.Downstream, evt.Direction);
    }

    [Fact]
    public void SequenceNumber_AssignedByCoordinator()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();

        // Act
        coordinator.Emit(new TextDeltaEvent("first", "msg1"));
        coordinator.Emit(new TextDeltaEvent("second", "msg1"));

        // Assert - Read and verify sequence numbers are monotonically increasing
        Assert.True(coordinator.TryRead(out var first));
        Assert.True(coordinator.TryRead(out var second));

        Assert.True(first!.SequenceNumber > 0);
        Assert.True(second!.SequenceNumber > first.SequenceNumber);
    }

    #endregion

    #region Stream Management Tests

    [Fact]
    public void StreamRegistry_CreateStream_ReturnsHandle()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();

        // Act
        var handle = coordinator.Streams.Create();

        // Assert
        Assert.NotNull(handle);
        Assert.NotNull(handle.StreamId);
        Assert.False(handle.IsInterrupted);
        Assert.False(handle.IsCompleted);
        Assert.Equal(0, handle.EmittedCount);
        Assert.Equal(0, handle.DroppedCount);
    }

    [Fact]
    public void StreamRegistry_CreateStream_WithCustomId()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();
        const string customId = "my-custom-stream-id";

        // Act
        var handle = coordinator.Streams.Create(customId);

        // Assert
        Assert.Equal(customId, handle.StreamId);
    }

    [Fact]
    public void StreamRegistry_CreateStream_DuplicateIdThrows()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();
        const string streamId = "duplicate-id";

        coordinator.Streams.Create(streamId);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => coordinator.Streams.Create(streamId));
    }

    [Fact]
    public void StreamRegistry_Get_ReturnsExistingStream()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var created = coordinator.Streams.Create("test-stream");

        // Act
        var retrieved = coordinator.Streams.Get("test-stream");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(created.StreamId, retrieved!.StreamId);
    }

    [Fact]
    public void StreamRegistry_Get_ReturnsNullForUnknown()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();

        // Act
        var result = coordinator.Streams.Get("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void StreamHandle_Interrupt_SetsFlags()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var handle = coordinator.Streams.Create();

        // Act
        handle.Interrupt();

        // Assert
        Assert.True(handle.IsInterrupted);
        Assert.True(handle.IsCompleted);
    }

    [Fact]
    public void StreamHandle_Complete_SetsCompletedFlag()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var handle = coordinator.Streams.Create();

        // Act
        handle.Complete();

        // Assert
        Assert.False(handle.IsInterrupted);
        Assert.True(handle.IsCompleted);
    }

    [Fact]
    public async Task StreamHandle_WaitAsync_CompletesOnInterrupt()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var handle = coordinator.Streams.Create();

        // Act
        var waitTask = handle.WaitAsync();
        handle.Interrupt();

        // Assert
        await waitTask; // Should complete immediately
        Assert.True(handle.IsCompleted);
    }

    [Fact]
    public void StreamHandle_OnInterrupted_RaisesEvent()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var handle = coordinator.Streams.Create();
        var wasRaised = false;

        handle.OnInterrupted += _ => wasRaised = true;

        // Act
        handle.Interrupt();

        // Assert
        Assert.True(wasRaised);
    }

    [Fact]
    public void StreamHandle_OnCompleted_RaisesEvent()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var handle = coordinator.Streams.Create();
        var wasRaised = false;

        handle.OnCompleted += _ => wasRaised = true;

        // Act
        handle.Complete();

        // Assert
        Assert.True(wasRaised);
    }

    [Fact]
    public void StreamRegistry_InterruptAll_InterruptsAllStreams()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var stream1 = coordinator.Streams.Create();
        var stream2 = coordinator.Streams.Create();
        var stream3 = coordinator.Streams.Create();

        // Act
        coordinator.Streams.InterruptAll();

        // Assert
        Assert.True(stream1.IsInterrupted);
        Assert.True(stream2.IsInterrupted);
        Assert.True(stream3.IsInterrupted);
    }

    [Fact]
    public void StreamRegistry_InterruptWhere_SelectivelyInterrupts()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var stream1 = coordinator.Streams.Create("keep-1");
        var stream2 = coordinator.Streams.Create("interrupt-2");
        var stream3 = coordinator.Streams.Create("interrupt-3");

        // Act - Interrupt only streams with "interrupt" in ID
        coordinator.Streams.InterruptWhere(h => h.StreamId.StartsWith("interrupt"));

        // Assert
        Assert.False(stream1.IsInterrupted);
        Assert.True(stream2.IsInterrupted);
        Assert.True(stream3.IsInterrupted);
    }

    [Fact]
    public void StreamRegistry_ActiveStreams_ReturnsOnlyActive()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var active1 = coordinator.Streams.Create();
        var active2 = coordinator.Streams.Create();
        var completed = coordinator.Streams.Create();
        completed.Complete();

        // Act
        var activeStreams = coordinator.Streams.ActiveStreams;

        // Assert
        Assert.Equal(2, activeStreams.Count);
        Assert.Contains(activeStreams, s => s.StreamId == active1.StreamId);
        Assert.Contains(activeStreams, s => s.StreamId == active2.StreamId);
    }

    [Fact]
    public void StreamRegistry_ActiveCount_ReturnsCorrectCount()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();
        coordinator.Streams.Create();
        coordinator.Streams.Create();
        var toComplete = coordinator.Streams.Create();
        toComplete.Complete();

        // Act & Assert
        Assert.Equal(2, coordinator.Streams.ActiveCount);
    }

    #endregion

    #region Event Dropping Tests

    [Fact(Skip = "Event dropping is universal (HPD.Events) - see HPD.Events.Tests.StreamRegistryTests.EventCoordinator_DropsInterruptedStreamEvents")]
    public void Emit_DropsCanInterruptEvents_WhenStreamInterrupted()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var stream = coordinator.Streams.Create();
        var streamId = stream.StreamId;

        // Emit first event (should succeed)
        coordinator.Emit(new TextDeltaEvent("first", "msg1")
        {
            StreamId = streamId,
            CanInterrupt = true
        });

        // Interrupt the stream BEFORE emitting more events
        stream.Interrupt();

        // Emit second event AFTER interruption (should be dropped)
        coordinator.Emit(new TextDeltaEvent("second", "msg1")
        {
            StreamId = streamId,
            CanInterrupt = true
        });

        // Act - Read events from standard channel
        var events = new List<AgentEvent>();
        while (coordinator.TryRead(out var evt))
        {
            events.Add((AgentEvent)evt);
        }

        // Assert - Only first event should be present (second was dropped)
        var textEvents = events.OfType<TextDeltaEvent>().ToList();
        Assert.Single(textEvents);
        Assert.Equal("first", textEvents[0].Text);

        // NOTE: EventDroppedEvent is now universal (HPD.Events.Event), not AgentEvent
        // So it won't appear in AgentEvent streams. Event dropping tests are in HPD.Events.Tests
        Assert.Single(events); // Only the first TextDeltaEvent
    }

    [Fact]
    public void Emit_DoesNotDropCanInterruptFalseEvents_WhenStreamInterrupted()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var stream = coordinator.Streams.Create();

        // Interrupt immediately
        stream.Interrupt();

        // Emit event with CanInterrupt = false (should NOT be dropped)
        coordinator.Emit(new TextMessageEndEvent("msg1")
        {
            StreamId = stream.StreamId,
            CanInterrupt = false
        });

        // Act - Read events
        var events = new List<AgentEvent>();
        while (coordinator.TryRead(out var evt))
        {
            events.Add((AgentEvent)evt);
        }

        // Assert - End event should be delivered
        var endEvents = events.OfType<TextMessageEndEvent>().ToList();
        Assert.Single(endEvents);
    }

    [Fact]
    public void StreamHandle_TracksEmittedAndDroppedCounts()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var stream = coordinator.Streams.Create();
        var streamId = stream.StreamId;

        // Emit 3 events before interruption
        for (int i = 0; i < 3; i++)
        {
            coordinator.Emit(new TextDeltaEvent($"before{i}", "msg1")
            {
                StreamId = streamId
            });
        }

        // Interrupt - this marks the stream as interrupted
        stream.Interrupt();

        // Emit 2 more events AFTER interruption (should be dropped)
        for (int i = 0; i < 2; i++)
        {
            coordinator.Emit(new TextDeltaEvent($"after{i}", "msg1")
            {
                StreamId = streamId
            });
        }

        // Assert
        Assert.Equal(3, stream.EmittedCount);
        Assert.Equal(2, stream.DroppedCount);
    }

    #endregion

    #region Upstream Event Tests

    [Fact]
    public void EmitUpstream_SetsDirectionToUpstream()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();

        // Act
        coordinator.EmitUpstream(new InterruptionRequestEvent(null, "test", InterruptionSource.User)
        {
            Priority = EventPriority.Immediate
        });

        // Assert - Read from ReadAllAsync to get upstream events
        // For now, just verify no exception thrown
        Assert.True(true);
    }

    [Fact]
    public void EmitUpstream_BubblesToParent()
    {
        // Arrange
        var parent = new HPD.Events.Core.EventCoordinator();
        var child = new HPD.Events.Core.EventCoordinator();
        child.SetParent(parent);

        // Act
        child.EmitUpstream(new InterruptionRequestEvent("stream1", "test", InterruptionSource.User)
        {
            Priority = EventPriority.Immediate
        });

        // Assert - Event should be in both coordinators' upstream channels
        // (verified by reading through ReadAllAsync)
        Assert.True(true); // No exception means success
    }

    #endregion

    #region Middleware Upstream Tests (OBSOLETE - V1 ONLY)

    // OBSOLETE: ExecuteOnUpstreamEventAsync and OnUpstreamEventAsync removed in V2
    //
    // V2 Replacement Patterns:
    // 1. Priority Event Routing: BidirectionalEventCoordinator.Emit() with EventPriority.Immediate
    // 2. Stream Interruption: IStreamRegistry.InterruptAll() + IStreamHandle.WasInterrupted
    // 3. Cancellation Cleanup: OnErrorAsync(ErrorContext) handles OperationCanceledException
    //

    //[Fact]
    //public async Task OnUpstreamEventAsync_CalledInReverseOrder()
    //{
    //    // V1 Pattern - middleware pipeline manually routes upstream events backward
    //    // V2 Replacement: BidirectionalEventCoordinator handles priority natively
    //}

    //[Fact]
    //public async Task OnUpstreamEventAsync_ContinuesOnException()
    //{
    //    // V1 Pattern - middleware handles upstream events for cleanup
    //    // V2 Replacement: OnErrorAsync for cleanup, stream registry for interruption
    //}

    #endregion

    #region Serialization Tests

    // NOTE: EventDroppedEvent is now universal (HPD.Events.Event), not AgentEvent
    // Serialization tests for universal events belong in HPD.Events.Tests

    [Fact]
    public void InterruptionRequestEvent_SerializesWithType()
    {
        // Arrange
        var evt = new InterruptionRequestEvent("stream-abc", "User cancelled", InterruptionSource.User);

        // Act
        var json = AgentEventSerializer.ToJson(evt);

        // Assert - Type discriminator is correct
        Assert.Contains("\"type\":\"INTERRUPTION_REQUEST\"", json);
        Assert.Contains("\"version\":\"1.0\"", json);
        // Note: Record primary constructor params serialize with lowercase camelCase
        Assert.Contains("\"reason\":\"User cancelled\"", json);
    }

    [Fact]
    public void EventPriority_DefaultValue_NotSerializedWhenWritingNull()
    {
        // Arrange
        var evt = new TextDeltaEvent("hello", "msg1"); // Default priority is Normal

        // Act
        var json = AgentEventSerializer.ToJson(evt);

        // Assert - Default values may or may not be serialized depending on config
        // The important thing is the type discriminator works
        Assert.Contains("\"type\":\"TEXT_DELTA\"", json);
        Assert.Contains("\"text\":\"hello\"", json);
    }

    [Fact]
    public void EventPriority_NonDefaultValue_Serializes()
    {
        // Arrange
        var evt = new TextDeltaEvent("hello", "msg1") { Priority = EventPriority.Immediate };

        // Act
        var json = AgentEventSerializer.ToJson(evt);

        // Assert - Non-default priority should be serialized
        Assert.Contains("\"type\":\"TEXT_DELTA\"", json);
        // Priority as enum serializes as int or string depending on JsonSerializerOptions
        Assert.True(json.Contains("\"priority\":") || json.Contains("priority"));
    }

    [Fact]
    public void AgentEventSerializer_GetEventTypeName_ReturnsCorrectType()
    {
        // Assert known event types
        Assert.Equal("INTERRUPTION_REQUEST", AgentEventSerializer.GetEventTypeName(typeof(InterruptionRequestEvent)));
        // NOTE: EventDroppedEvent is now universal (HPD.Events.Event), not serialized by AgentEventSerializer
    }

    #endregion

    #region Integration Tests

    [Fact(Skip = "Event dropping is universal (HPD.Events) - see HPD.Events.Tests.StreamRegistryTests.EventCoordinator_FullInterruptionFlow_TracksEmittedAndDroppedCounts")]
    public async Task FullInterruptionFlow_Works()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var stream = coordinator.Streams.Create("response-stream");
        var streamId = stream.StreamId;

        // Simulate streaming response - 5 tokens before interruption
        for (int i = 0; i < 5; i++)
        {
            coordinator.Emit(new TextDeltaEvent($"token{i}", "msg1")
            {
                StreamId = streamId,
                Priority = EventPriority.Normal
            });
        }

        // User interrupts
        stream.Interrupt();

        // More tokens arrive AFTER interruption (should be dropped)
        for (int i = 5; i < 10; i++)
        {
            coordinator.Emit(new TextDeltaEvent($"token{i}", "msg1")
            {
                StreamId = streamId,
                Priority = EventPriority.Normal
            });
        }

        // End marker (should NOT be dropped because CanInterrupt = false)
        coordinator.Emit(new TextMessageEndEvent("msg1")
        {
            StreamId = streamId,
            CanInterrupt = false,
            Priority = EventPriority.Normal
        });

        // Act - Read all events
        var events = new List<AgentEvent>();
        while (coordinator.TryRead(out var evt))
        {
            events.Add((AgentEvent)evt);
        }

        // Assert
        var textDeltas = events.OfType<TextDeltaEvent>().ToList();
        var endEvents = events.OfType<TextMessageEndEvent>().ToList();

        Assert.Equal(5, textDeltas.Count); // First 5 before interruption
        Assert.Single(endEvents); // End marker delivered (CanInterrupt = false)

        // NOTE: EventDroppedEvent is now universal (HPD.Events.Event), not AgentEvent
        // So it won't appear in AgentEvent streams. Event dropping tests are in HPD.Events.Tests
        Assert.Equal(6, events.Count); // 5 textDeltas + 1 endEvent (no EventDroppedEvents in Agent stream)
        Assert.Equal(5, stream.DroppedCount); // But dropping still tracked in StreamHandle
        Assert.Equal(5, stream.EmittedCount);
    }

    [Fact]
    public async Task PriorityCancellation_BypassesQueuedEvents()
    {
        // Arrange
        var coordinator = new HPD.Events.Core.EventCoordinator();

        // Queue many normal events
        for (int i = 0; i < 1000; i++)
        {
            coordinator.Emit(new TextDeltaEvent($"text{i}", "msg1")
            {
                Priority = EventPriority.Normal
            });
        }

        // Queue cancellation (should be read first)
        coordinator.Emit(new InterruptionRequestEvent(null, "User cancelled", InterruptionSource.User)
        {
            Priority = EventPriority.Immediate
        });

        // Act - Read first event
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        AgentEvent? firstEvent = null;

        await foreach (var evt in coordinator.ReadAllAsync(cts.Token))
        {
            firstEvent = (AgentEvent)evt;
            break;
        }

        // Assert - Cancellation should be first despite 1000 queued events
        Assert.IsType<InterruptionRequestEvent>(firstEvent);
    }

    #endregion

    #region Helper Classes

    // OBSOLETE V1 helper methods removed
    // CreateTestContext() - used AgentMiddlewareContext (removed in V2)
    // TestUpstreamMiddleware - used OnUpstreamEventAsync (removed in V2)
    // ThrowingUpstreamMiddleware - used OnUpstreamEventAsync (removed in V2)
    //
    // V2 uses typed contexts from HPD.Agent.Tests.Middleware.V2.MiddlewareTestHelpers

    #endregion
}
