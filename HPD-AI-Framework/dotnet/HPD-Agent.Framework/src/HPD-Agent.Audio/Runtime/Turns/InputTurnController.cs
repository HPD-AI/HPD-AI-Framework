using HPD.Agent.Audio.Turns;

namespace HPD.Agent.Audio.Runtime.Turns;

public sealed class InputTurnController : ITurnController
{
    private readonly List<TurnEvidence> _evidence = [];
    private readonly RuntimeClock _clock;
    private readonly RuntimeIdFactory _ids;

    public InputTurnController(
        AudioSessionId sessionId,
        RuntimeIdFactory? ids = null,
        RuntimeClock? clock = null)
    {
        SessionId = sessionId;
        _ids = ids ?? new RuntimeIdFactory();
        _clock = clock ?? new RuntimeClock();
    }

    public AudioSessionId SessionId { get; }

    public TurnSnapshot Snapshot => new()
    {
        SessionId = SessionId,
        CurrentTurnId = _evidence.LastOrDefault()?.TurnId,
        Evidence = _evidence.ToArray()
    };

    public ValueTask<TurnDecision> ObserveAsync(
        TurnEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _evidence.Add(evidence);

        if (evidence.Detail is TranscriptEvidenceDetail { IsFinal: true } transcript &&
            evidence.Kind is TurnEvidenceKind.FinalTranscript or TurnEvidenceKind.InputMediaTranscribed &&
            !string.IsNullOrWhiteSpace(transcript.Text))
        {
            var turnId = evidence.TurnId ?? _ids.NextTurnId();
            var commit = new TurnCommit
            {
                TurnId = turnId,
                Text = transcript.Text,
                Reason = TurnCommitReason.InputMediaTranscript,
                EvidenceIds = _evidence.Select(item => item.Id).ToArray()
            };

            return ValueTask.FromResult(new TurnDecision
            {
                Kind = TurnDecisionKind.CommitUserTurn,
                DecidedAt = _clock.Tick(),
                TurnId = turnId,
                Reason = "input-media-final-transcript",
                Commit = commit
            });
        }

        return ValueTask.FromResult(new TurnDecision
        {
            Kind = TurnDecisionKind.ContinueListening,
            DecidedAt = _clock.Tick(),
            TurnId = evidence.TurnId,
            Reason = "waiting-for-input-media-transcript"
        });
    }
}
