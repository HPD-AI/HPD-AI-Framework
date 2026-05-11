// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio.Recognition;

namespace HPD.Agent.Audio.Turn;

/// <summary>
/// Converts recognition events into explicit user turn decisions.
/// </summary>
public sealed class TurnController
{
    private readonly TurnControllerOptions _options;
    private readonly IEndpointingPolicy _endpointingPolicy;
    private string? _turnId;
    private SpeechRecognitionContext? _recognitionContext;
    private SpeechRecognitionTranscript? _bestTranscript;
    private bool _bestTranscriptIsFinal;
    private bool _isSpeaking;
    private EndpointingDecision? _pendingDecision;
    private DateTimeOffset? _endpointDueAt;

    /// <summary>Creates a turn controller.</summary>
    public TurnController(
        TurnControllerOptions? options = null,
        IEndpointingPolicy? endpointingPolicy = null)
    {
        _options = options ?? new TurnControllerOptions();
        _endpointingPolicy = endpointingPolicy ?? new DefaultEndpointingPolicy(_options);
    }

    /// <summary>Current controller state.</summary>
    public TurnControllerState State { get; private set; } = TurnControllerState.Idle;

    /// <summary>Processes one recognition event.</summary>
    public IReadOnlyList<UserTurnEvent> Process(SpeechRecognitionEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        return evt switch
        {
            SpeechRecognitionStartedEvent started => OnStarted(started),
            SpeechRecognitionInterimEvent interim => OnTranscript(interim, interim.Transcript, "interim"),
            SpeechRecognitionPreflightEvent preflight => OnTranscript(preflight, preflight.Transcript, "preflight"),
            SpeechRecognitionFinalEvent final => OnTranscript(final, final.Transcript, "final"),
            SpeechRecognitionEndedEvent ended => OnEnded(ended),
            SpeechRecognitionErrorEvent error => Cancel(error.Context, error.ErrorCode ?? EndpointingReason.RecognitionError),
            _ => []
        };
    }

    /// <summary>Commits the current turn if its endpointing delay has elapsed.</summary>
    public IReadOnlyList<UserTurnEvent> AdvanceEndpointing(DateTimeOffset now)
    {
        if (State != TurnControllerState.AwaitingEndpointDelay ||
            _endpointDueAt is null ||
            now < _endpointDueAt.Value ||
            _bestTranscript is null ||
            _recognitionContext is null ||
            _pendingDecision is null)
        {
            return [];
        }

        return Commit(_recognitionContext, _bestTranscript, _pendingDecision.Reason);
    }

    /// <summary>Commits the current turn immediately.</summary>
    public IReadOnlyList<UserTurnEvent> ManualCommit(DateTimeOffset observedAt)
    {
        if (_bestTranscript is null || _recognitionContext is null)
            return [];

        var context = _recognitionContext with { ObservedAt = observedAt };
        return Commit(context, _bestTranscript, EndpointingReason.ManualCommit);
    }

    /// <summary>Cancels the current turn.</summary>
    public IReadOnlyList<UserTurnEvent> ManualCancel(
        DateTimeOffset observedAt,
        string reason = EndpointingReason.ManualCancel)
    {
        if (_recognitionContext is null)
            return [];

        var context = _recognitionContext with { ObservedAt = observedAt };
        return Cancel(context, reason);
    }

    private IReadOnlyList<UserTurnEvent> OnStarted(SpeechRecognitionStartedEvent evt)
    {
        _turnId = Guid.NewGuid().ToString("N");
        _recognitionContext = evt.Context;
        _endpointDueAt = null;
        _pendingDecision = null;
        _bestTranscriptIsFinal = false;
        _isSpeaking = true;
        State = TurnControllerState.UserSpeaking;

        return [new UserTurnStartedEvent { Context = CreateTurnContext(evt.Context) }];
    }

    private IReadOnlyList<UserTurnEvent> OnTranscript(
        SpeechRecognitionEvent evt,
        SpeechRecognitionTranscript transcript,
        string stability)
    {
        EnsureTurn(evt.Context);
        _recognitionContext = evt.Context;
        _bestTranscript = transcript;
        _bestTranscriptIsFinal = stability == "final";

        var events = new List<UserTurnEvent>
        {
            new UserTurnUpdatedEvent
            {
                Context = CreateTurnContext(evt.Context, transcript.TranscriptRevisionId),
                Transcript = transcript,
                Stability = stability
            }
        };

        if (stability == "final" &&
            State is TurnControllerState.UserMaybeDone or TurnControllerState.AwaitingTranscript or TurnControllerState.Idle)
        {
            var reason = _options.Mode == EndpointingMode.Stt
                ? EndpointingReason.SttFinalMinDelay
                : EndpointingReason.FinalTranscriptNoSpeech;
            events.AddRange(MarkReady(evt.Context, reason));
        }

        return events;
    }

    private IReadOnlyList<UserTurnEvent> OnEnded(SpeechRecognitionEndedEvent evt)
    {
        EnsureTurn(evt.Context);
        _recognitionContext = evt.Context;
        _isSpeaking = false;

        if (_bestTranscript is null || (_options.Mode == EndpointingMode.Stt && !_bestTranscriptIsFinal))
        {
            State = TurnControllerState.AwaitingTranscript;
            return [];
        }

        State = TurnControllerState.UserMaybeDone;
        var reason = _options.Mode == EndpointingMode.Stt
            ? EndpointingReason.SttFinalMinDelay
            : EndpointingReason.VadEndMinDelay;
        return MarkReady(evt.Context, reason);
    }

    private IReadOnlyList<UserTurnEvent> MarkReady(SpeechRecognitionContext context, string fallbackReason)
    {
        if (_bestTranscript is null)
            return [];

        if (_options.Mode is EndpointingMode.Manual or EndpointingMode.RealtimeModel)
            return [];

        var decision = _endpointingPolicy.Decide(new EndpointingPolicyContext
        {
            Transcript = _bestTranscript,
            State = State,
            FallbackReason = fallbackReason,
            IsSpeaking = _isSpeaking
        });
        _pendingDecision = decision;
        _endpointDueAt = context.ObservedAt + decision.Delay;

        var ready = new UserTurnReadyEvent
        {
            Context = CreateTurnContext(context, _bestTranscript.TranscriptRevisionId),
            Transcript = _bestTranscript,
            Decision = decision
        };

        if (decision.ShouldCommitNow)
            return [ready, .. Commit(context, _bestTranscript, decision.Reason)];

        State = TurnControllerState.AwaitingEndpointDelay;
        return [ready];
    }

    private IReadOnlyList<UserTurnEvent> Commit(
        SpeechRecognitionContext context,
        SpeechRecognitionTranscript transcript,
        string reason)
    {
        State = TurnControllerState.Committed;
        _endpointDueAt = null;
        _pendingDecision = null;
        _bestTranscriptIsFinal = true;
        _isSpeaking = false;

        return
        [
            new UserTurnCommittedEvent
            {
                Context = CreateTurnContext(context, transcript.TranscriptRevisionId),
                Transcript = transcript,
                Reason = reason
            }
        ];
    }

    private IReadOnlyList<UserTurnEvent> Cancel(SpeechRecognitionContext context, string reason)
    {
        State = TurnControllerState.Cancelled;
        _endpointDueAt = null;
        _pendingDecision = null;
        _bestTranscriptIsFinal = false;
        _isSpeaking = false;

        return
        [
            new UserTurnCancelledEvent
            {
                Context = CreateTurnContext(context),
                Reason = reason
            }
        ];
    }

    private void EnsureTurn(SpeechRecognitionContext context)
    {
        if (_turnId != null)
            return;

        _turnId = Guid.NewGuid().ToString("N");
        _recognitionContext = context;
    }

    private UserTurnContext CreateTurnContext(
        SpeechRecognitionContext context,
        string? transcriptRevisionId = null)
    {
        _turnId ??= Guid.NewGuid().ToString("N");
        return new UserTurnContext(
            RuntimeId: context.RuntimeId,
            SessionId: context.SessionId,
            BranchId: context.BranchId,
            TurnId: _turnId,
            UtteranceId: context.UtteranceId,
            RecognitionId: context.RecognitionId,
            TranscriptRevisionId: transcriptRevisionId ?? _bestTranscript?.TranscriptRevisionId,
            SequenceNumber: context.SequenceNumber,
            TimestampNs: context.TimestampNs,
            ObservedAt: context.ObservedAt);
    }
}
