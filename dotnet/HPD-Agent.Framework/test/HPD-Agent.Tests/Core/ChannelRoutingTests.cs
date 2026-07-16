using HPD.Agent;
using HPD.Agent.Serialization;
using HPD.Events;
using Xunit;
using EventDirection = HPD.Events.EventDirection;

namespace HPD.Agent.Tests.Core;

/// <summary>
/// Agent-specific coverage for channel-routed events.
/// Core channel behavior lives in HPD.Events.Tests.
/// </summary>
public class ChannelRoutingTests
{
    [Fact]
    public async Task ControlEvents_AreReadFromControlChannel()
    {
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var interruption = new InterruptionHandledEvent(null, "test", InterruptionSource.User);
        await using var subscription = coordinator.CreateChannelInbox(EventChannel.Control);

        coordinator.Emit(interruption);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var evt = await ReadFirstAsync(subscription.Reader, cts.Token);

        Assert.Same(interruption, evt);
        Assert.Equal(EventChannel.Control, evt.Channel);
    }

    [Fact]
    public async Task SynchronousAgentEvents_AreReadFromSynchronousChannel()
    {
        var coordinator = new HPD.Events.Core.EventCoordinator();
        await using var subscription = coordinator.CreateChannelInbox(EventChannel.Synchronous);

        var snapshot = new StateSnapshotEvent(1, 10, false, null, 0, [], "agent");

        coordinator.Emit(snapshot);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var evt = await ReadFirstAsync(subscription.Reader, cts.Token);

        Assert.IsType<StateSnapshotEvent>(evt);
    }

    [Fact]
    public void TextDeltas_DefaultToStreaming()
    {
        var evt = new TextDeltaEvent("hello", "msg1");

        Assert.Equal(EventChannel.Streaming, evt.Channel);
    }

    [Fact]
    public void EventDirection_DefaultsToDownstream()
    {
        var evt = new TextDeltaEvent("hello", "msg1");

        Assert.Equal(EventDirection.Downstream, evt.Direction);
    }

    [Fact]
    public async Task Coordinator_DoesNotMutateDomainOrderingState()
    {
        var coordinator = new HPD.Events.Core.EventCoordinator();
        await using var subscription = coordinator.CreateChannelInbox(EventChannel.Streaming);

        coordinator.Emit(new TextDeltaEvent("first", "msg1"));
        coordinator.Emit(new TextDeltaEvent("second", "msg1"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = await ReadFirstAsync(subscription.Reader, cts.Token);
        var second = await ReadFirstAsync(subscription.Reader, cts.Token);

        Assert.Equal(0, ((AgentEvent)first).ThreadSequenceNumber);
        Assert.Equal(0, ((AgentEvent)second).ThreadSequenceNumber);
    }

    [Fact]
    public void EventFlowRegistry_BeginFlow_ReturnsHandle()
    {
        var coordinator = new HPD.Events.Core.EventCoordinator();

        var handle = coordinator.EventFlows.Create();

        Assert.NotNull(handle.EventFlowId);
        Assert.False(handle.IsInterrupted);
        Assert.False(handle.IsCompleted);
        Assert.Equal(0, handle.EmittedCount);
        Assert.Equal(0, handle.DroppedCount);
    }

    [Fact]
    public void EventFlowRegistry_BeginFlow_WithCustomId()
    {
        var coordinator = new HPD.Events.Core.EventCoordinator();

        var handle = coordinator.EventFlows.Create("my-custom-stream-id");

        Assert.Equal("my-custom-stream-id", handle.EventFlowId);
    }

    [Fact]
    public void EventFlowRegistry_BeginFlow_DuplicateIdThrows()
    {
        var coordinator = new HPD.Events.Core.EventCoordinator();
        coordinator.EventFlows.Create("duplicate-id");

        Assert.Throws<InvalidOperationException>(() => coordinator.EventFlows.Create("duplicate-id"));
    }

    [Fact]
    public void EventFlowRegistry_Get_ReturnsExistingFlow()
    {
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var created = coordinator.EventFlows.Create("test-stream");

        var retrieved = coordinator.EventFlows.Get("test-stream");

        Assert.NotNull(retrieved);
        Assert.Equal(created.EventFlowId, retrieved!.EventFlowId);
    }

    [Fact]
    public void EventFlowHandle_Interrupt_SetsFlags()
    {
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var handle = coordinator.EventFlows.Create();

        handle.Interrupt();

        Assert.True(handle.IsInterrupted);
        Assert.True(handle.IsCompleted);
    }

    [Fact]
    public void EventFlowHandle_Complete_SetsCompletedFlag()
    {
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var handle = coordinator.EventFlows.Create();

        handle.Complete();

        Assert.False(handle.IsInterrupted);
        Assert.True(handle.IsCompleted);
    }

    [Fact]
    public async Task EventFlowHandle_WaitAsync_CompletesOnInterrupt()
    {
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var handle = coordinator.EventFlows.Create();

        var waitTask = handle.WaitAsync();
        handle.Interrupt();

        await waitTask;
        Assert.True(handle.IsCompleted);
    }

    [Fact]
    public void EventFlowRegistry_InterruptAll_InterruptsAllFlows()
    {
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var stream1 = coordinator.EventFlows.Create();
        var stream2 = coordinator.EventFlows.Create();
        var stream3 = coordinator.EventFlows.Create();

        coordinator.EventFlows.InterruptAll();

        Assert.True(stream1.IsInterrupted);
        Assert.True(stream2.IsInterrupted);
        Assert.True(stream3.IsInterrupted);
    }

    [Fact]
    public void EventFlowRegistry_InterruptWhere_SelectivelyInterrupts()
    {
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var stream1 = coordinator.EventFlows.Create("keep-1");
        var stream2 = coordinator.EventFlows.Create("interrupt-2");
        var stream3 = coordinator.EventFlows.Create("interrupt-3");

        coordinator.EventFlows.InterruptWhere(h => h.EventFlowId.StartsWith("interrupt"));

        Assert.False(stream1.IsInterrupted);
        Assert.True(stream2.IsInterrupted);
        Assert.True(stream3.IsInterrupted);
    }

    [Fact]
    public async Task Emit_DoesNotDropCanInterruptFalseEvents_WhenStreamInterrupted()
    {
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var stream = coordinator.EventFlows.Create();
        await using var subscription = coordinator.CreateChannelInbox(EventChannel.Streaming);
        stream.Interrupt();

        coordinator.Emit(new TextMessageEndEvent("msg1")
        {
            EventFlowId = stream.EventFlowId,
            CanInterrupt = false
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var evt = await ReadFirstAsync(subscription.Reader, cts.Token);

        Assert.IsType<TextMessageEndEvent>(evt);
    }

    [Fact]
    public void EventFlowHandle_TracksEmittedAndDroppedCounts()
    {
        var coordinator = new HPD.Events.Core.EventCoordinator();
        var stream = coordinator.EventFlows.Create();

        for (var i = 0; i < 3; i++)
            coordinator.Emit(new TextDeltaEvent($"before{i}", "msg1") { EventFlowId = stream.EventFlowId });

        stream.Interrupt();

        for (var i = 0; i < 2; i++)
            coordinator.Emit(new TextDeltaEvent($"after{i}", "msg1") { EventFlowId = stream.EventFlowId });

        Assert.Equal(3, stream.EmittedCount);
        Assert.Equal(2, stream.DroppedCount);
    }

    [Fact]
    public void InterruptionHandledEvent_IsControlDownstream()
    {
        var evt = new InterruptionHandledEvent(null, "test", InterruptionSource.User);

        Assert.Equal(EventChannel.Control, evt.Channel);
        Assert.Equal(EventDirection.Downstream, evt.Direction);
        Assert.Equal(EventKind.Control, evt.Kind);
    }

    [Theory]
    [MemberData(nameof(ChannelSamples))]
    public void AgentEvents_HaveExpectedDefaultChannels(AgentEvent evt, EventChannel expectedChannel, EventKind expectedKind)
    {
        Assert.Equal(expectedChannel, evt.Channel);
        Assert.Equal(expectedKind, evt.Kind);
    }

    public static TheoryData<AgentEvent, EventChannel, EventKind> ChannelSamples() => new()
    {
        { new TextDeltaEvent("hello", "msg1"), EventChannel.Streaming, EventKind.Content },
        { new ReasoningDeltaEvent("thinking", "msg1"), EventChannel.Streaming, EventKind.Content },
        { new ToolCallStartEvent("call1", "tool", "msg1"), EventChannel.Synchronous, EventKind.Lifecycle },
        { new PermissionRequestEvent("perm1", "source", "tool", null, "call1", null), EventChannel.Interactive, EventKind.Control },
        { new PermissionResponseEvent("perm1", "source", true), EventChannel.Interactive, EventKind.Control },
        { new ClarificationRequestEvent("req1", "source", "question"), EventChannel.Interactive, EventKind.Control },
        { new HPD.Agent.ClientTools.ClientToolInvokeOutcomeEvent
            {
                RequestId = "req1",
                Outcome = HPD.Agent.ClientTools.ClientToolInvokeOutcomeKind.Completed,
                Content = [new HPD.Agent.ClientTools.TextContent("ok")]
            }, EventChannel.Interactive, EventKind.Control },
        { new StateSnapshotEvent(1, 10, false, null, 0, [], "agent"), EventChannel.Synchronous, EventKind.Diagnostic },
        { new EventDroppedEvent("stream1", "TextDeltaEvent", 1), EventChannel.Synchronous, EventKind.Diagnostic },
        { new InterruptionHandledEvent(null, "stop", InterruptionSource.User), EventChannel.Control, EventKind.Control },
    };

    [Fact]
    public async Task ControlEvent_BubblesToParentThroughEmit()
    {
        var parent = new HPD.Events.Core.EventCoordinator();
        var child = new HPD.Events.Core.EventCoordinator();
        child.SetParent(parent);
        await using var subscription = parent.CreateChannelInbox(EventChannel.Control);

        var interruption = new InterruptionHandledEvent("stream1", "test", InterruptionSource.User);

        child.Emit(interruption);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var evt = await ReadFirstAsync(subscription.Reader, cts.Token);

        Assert.Same(interruption, evt);
        Assert.Equal(EventDirection.Downstream, evt.Direction);
    }

    [Fact]
    public void InterruptionRequestEvent_SerializesAsInputEnvelope()
    {
        var evt = new InterruptionRequestEvent("stream-abc", "User cancelled", InterruptionSource.User);

        var json = AgentEventSerializer.ToJson(evt);

        Assert.Contains("\"type\":\"INTERRUPTION_REQUEST\"", json);
        Assert.Contains("\"version\":\"1.0\"", json);
        Assert.Contains("\"reason\":\"User cancelled\"", json);
    }

    [Fact]
    public void InterruptionHandledEvent_SerializesWithType()
    {
        var evt = new InterruptionHandledEvent("stream-abc", "User cancelled", InterruptionSource.User);

        var json = AgentEventSerializer.ToJson(evt);

        Assert.Contains("\"type\":\"INTERRUPTION_HANDLED\"", json);
        Assert.Contains("\"version\":\"1.0\"", json);
        Assert.Contains("\"reason\":\"User cancelled\"", json);
    }

    [Fact]
    public void EventChannel_DefaultValue_SerializesAgentEventPayload()
    {
        var evt = new TextDeltaEvent("hello", "msg1");

        var json = AgentEventSerializer.ToJson(evt);

        Assert.Contains("\"type\":\"TEXT_DELTA\"", json);
        Assert.Contains("\"text\":\"hello\"", json);
    }

    [Fact]
    public void EventChannel_NonDefaultValue_Serializes()
    {
        var evt = new MessageTurnStartedEvent("turn1", "conv1", "agent") { Channel = EventChannel.Control };

        var json = AgentEventSerializer.ToJson(evt);

        Assert.Contains("\"type\":\"MESSAGE_TURN_STARTED\"", json);
        Assert.Contains("channel", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AgentEventSerializer_GetEventTypeName_ReturnsCorrectType()
    {
        Assert.Equal("INTERRUPTION_REQUEST", AgentEventSerializer.GetEventTypeName(typeof(InterruptionRequestEvent)));
    }

    private static async Task<Event> ReadFirstAsync(System.Threading.Channels.ChannelReader<Event> reader, CancellationToken ct)
    {
        await foreach (var evt in reader.ReadAllAsync(ct))
            return evt;

        throw new InvalidOperationException("No event was produced.");
    }
}
