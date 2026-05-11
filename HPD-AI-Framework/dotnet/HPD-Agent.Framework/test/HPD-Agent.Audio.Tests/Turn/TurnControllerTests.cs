// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio.Eot;
using HPD.Agent.Audio.Recognition;
using HPD.Agent.Audio.Turn;
using Xunit;

namespace HPD.Agent.Audio.Tests.Turn;

public sealed class TurnControllerTests
{
    [Fact]
    public void Process_StartsUserTurn_WhenRecognitionStarts()
    {
        var controller = new TurnController();

        var events = controller.Process(new SpeechRecognitionStartedEvent
        {
            Context = CreateContext()
        });

        var started = Assert.Single(events);
        Assert.IsType<UserTurnStartedEvent>(started);
        Assert.Equal(TurnControllerState.UserSpeaking, controller.State);
    }

    [Fact]
    public void Process_FinalThenEnded_EmitsReadyAndCommitsAfterEndpointDelay()
    {
        var observedAt = DateTimeOffset.UnixEpoch;
        var controller = new TurnController(new TurnControllerOptions
        {
            MinEndpointingDelay = TimeSpan.FromMilliseconds(300),
            EotDetector = new HeuristicEotDetector()
        });

        controller.Process(new SpeechRecognitionStartedEvent { Context = CreateContext(observedAt) });
        var finalEvents = controller.Process(new SpeechRecognitionFinalEvent
        {
            Context = CreateContext(observedAt.AddMilliseconds(10)),
            Transcript = new SpeechRecognitionTranscript("Hello there.", TranscriptRevisionId: "rev-1")
        });
        var endedEvents = controller.Process(new SpeechRecognitionEndedEvent
        {
            Context = CreateContext(observedAt.AddMilliseconds(20))
        });

        Assert.Single(finalEvents);
        var ready = Assert.IsType<UserTurnReadyEvent>(Assert.Single(endedEvents));
        Assert.Equal(EndpointingReason.EotHighConfidence, ready.Decision.Reason);
        Assert.Equal(TimeSpan.FromMilliseconds(300), ready.Decision.Delay);
        Assert.Equal(TurnControllerState.AwaitingEndpointDelay, controller.State);

        Assert.Empty(controller.AdvanceEndpointing(observedAt.AddMilliseconds(319)));
        var committedEvents = controller.AdvanceEndpointing(observedAt.AddMilliseconds(320));
        var committed = Assert.IsType<UserTurnCommittedEvent>(Assert.Single(committedEvents));
        Assert.Equal("Hello there.", committed.Transcript.Text);
        Assert.Equal(EndpointingReason.EotHighConfidence, committed.Reason);
        Assert.Equal(TurnControllerState.Committed, controller.State);
    }

    [Fact]
    public void Process_EndedBeforeFinal_WaitsForTranscriptThenReadies()
    {
        var observedAt = DateTimeOffset.UnixEpoch;
        var controller = new TurnController();

        controller.Process(new SpeechRecognitionStartedEvent { Context = CreateContext(observedAt) });
        var endedEvents = controller.Process(new SpeechRecognitionEndedEvent
        {
            Context = CreateContext(observedAt.AddMilliseconds(100))
        });
        Assert.Empty(endedEvents);
        Assert.Equal(TurnControllerState.AwaitingTranscript, controller.State);

        var finalEvents = controller.Process(new SpeechRecognitionFinalEvent
        {
            Context = CreateContext(observedAt.AddMilliseconds(120)),
            Transcript = new SpeechRecognitionTranscript("Delayed transcript", TranscriptRevisionId: "rev-2")
        });

        Assert.Collection(
            finalEvents,
            evt => Assert.IsType<UserTurnUpdatedEvent>(evt),
            evt => Assert.IsType<UserTurnReadyEvent>(evt));
        Assert.Equal(TurnControllerState.AwaitingEndpointDelay, controller.State);
    }

    [Fact]
    public void Process_ManualMode_DoesNotAutoReady()
    {
        var controller = new TurnController(new TurnControllerOptions
        {
            Mode = EndpointingMode.Manual
        });

        controller.Process(new SpeechRecognitionStartedEvent { Context = CreateContext() });
        controller.Process(new SpeechRecognitionFinalEvent
        {
            Context = CreateContext(),
            Transcript = new SpeechRecognitionTranscript("Manual only.", TranscriptRevisionId: "rev-1")
        });
        var endedEvents = controller.Process(new SpeechRecognitionEndedEvent { Context = CreateContext() });

        Assert.Empty(endedEvents);
        var committed = Assert.IsType<UserTurnCommittedEvent>(
            Assert.Single(controller.ManualCommit(DateTimeOffset.UnixEpoch.AddSeconds(1))));
        Assert.Equal(EndpointingReason.ManualCommit, committed.Reason);
    }

    [Fact]
    public void Process_SttMode_WaitsForFinalTranscriptAfterSpeechEnds()
    {
        var observedAt = DateTimeOffset.UnixEpoch;
        var controller = new TurnController(new TurnControllerOptions
        {
            Mode = EndpointingMode.Stt,
            MinEndpointingDelay = TimeSpan.FromMilliseconds(100)
        });

        controller.Process(new SpeechRecognitionStartedEvent { Context = CreateContext(observedAt) });
        controller.Process(new SpeechRecognitionInterimEvent
        {
            Context = CreateContext(observedAt.AddMilliseconds(10)),
            Transcript = new SpeechRecognitionTranscript("Still changing", TranscriptRevisionId: "rev-interim")
        });
        var endedEvents = controller.Process(new SpeechRecognitionEndedEvent
        {
            Context = CreateContext(observedAt.AddMilliseconds(20))
        });

        Assert.Empty(endedEvents);
        Assert.Equal(TurnControllerState.AwaitingTranscript, controller.State);

        var finalEvents = controller.Process(new SpeechRecognitionFinalEvent
        {
            Context = CreateContext(observedAt.AddMilliseconds(30)),
            Transcript = new SpeechRecognitionTranscript("Stable final.", TranscriptRevisionId: "rev-final")
        });

        Assert.Collection(
            finalEvents,
            evt => Assert.IsType<UserTurnUpdatedEvent>(evt),
            evt =>
            {
                var ready = Assert.IsType<UserTurnReadyEvent>(evt);
                Assert.Equal(EndpointingReason.SttFinalMinDelay, ready.Decision.Reason);
            });
        Assert.Equal(TurnControllerState.AwaitingEndpointDelay, controller.State);
    }

    [Fact]
    public void Process_RealtimeModelMode_DoesNotAutoReadyFromRecognition()
    {
        var controller = new TurnController(new TurnControllerOptions
        {
            Mode = EndpointingMode.RealtimeModel
        });

        controller.Process(new SpeechRecognitionStartedEvent { Context = CreateContext() });
        controller.Process(new SpeechRecognitionFinalEvent
        {
            Context = CreateContext(),
            Transcript = new SpeechRecognitionTranscript("Model will decide.", TranscriptRevisionId: "rev-1")
        });
        var endedEvents = controller.Process(new SpeechRecognitionEndedEvent { Context = CreateContext() });

        Assert.Empty(endedEvents);
        Assert.Equal(TurnControllerState.UserMaybeDone, controller.State);
    }

    [Fact]
    public void ManualCancel_EmitsCancelledEvent()
    {
        var controller = new TurnController();
        controller.Process(new SpeechRecognitionStartedEvent { Context = CreateContext() });

        var cancelled = Assert.IsType<UserTurnCancelledEvent>(
            Assert.Single(controller.ManualCancel(DateTimeOffset.UnixEpoch, "test_cancel")));

        Assert.Equal("test_cancel", cancelled.Reason);
        Assert.Equal(TurnControllerState.Cancelled, controller.State);
    }

    private static SpeechRecognitionContext CreateContext(DateTimeOffset? observedAt = null) =>
        new(
            RuntimeId: "runtime-1",
            SessionId: "session-1",
            BranchId: "main",
            UtteranceId: "utt-1",
            RecognitionId: "rec-1",
            SegmentId: "seg-1",
            ProviderRequestId: "provider-request-1",
            Provider: "test",
            Model: "test-model",
            SequenceNumber: 7,
            TimestampNs: 123,
            ObservedAt: observedAt ?? DateTimeOffset.UnixEpoch);
}
