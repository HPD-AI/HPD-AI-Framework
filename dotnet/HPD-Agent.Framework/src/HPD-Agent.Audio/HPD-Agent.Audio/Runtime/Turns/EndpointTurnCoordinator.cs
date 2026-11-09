using System.Text;
using HPD.Agent.Audio.Endpointing;
using HPD.Agent.Audio.Runtime.Endpointing;
using HPD.Agent.Audio.Turns;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Turns;

internal sealed class EndpointTurnCoordinator
{
    private readonly List<EndpointEvidenceProjection> _evidence = [];
    private readonly RuntimeClock _clock;
    private readonly RuntimeIdFactory _ids;
    private readonly ExpectedAuthorityVectorV1 _authority;

    public EndpointTurnCoordinator(
        AudioSessionId sessionId,
        RuntimeIdFactory? ids = null,
        RuntimeClock? clock = null)
    {
        SessionId = sessionId;
        _ids = ids ?? new RuntimeIdFactory();
        _clock = clock ?? new RuntimeClock();
        var authoritySession = new SessionAuthorityStampV1(
            RuntimeGenerationId.Create(), LiveSessionId.Create());
        _authority = ExpectedAuthorityVectorV1.Create(authoritySession,
            [new AuthorityAxisValueV1.Turn(TurnGenerationId.Create())]);
    }

    public AudioSessionId SessionId { get; }

    public EndpointSnapshotProjection Snapshot => new()
    {
        SessionId = SessionId,
        CurrentTurnId = _evidence.LastOrDefault()?.TurnId,
        Evidence = _evidence.ToArray()
    };

    public ValueTask<EndpointDecisionProjection> ObserveAsync(
        EndpointEvidenceProjection evidence,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _evidence.Add(evidence);

        if (evidence.Detail is TranscriptEvidenceProjectionDetail { IsFinal: true } transcript &&
            evidence.Kind is EndpointEvidenceProjectionKind.FinalTranscript or EndpointEvidenceProjectionKind.InputMediaTranscribed &&
            !string.IsNullOrWhiteSpace(transcript.Text))
        {
            var finality = new TranscriptFinality(
                TranscriptMutability.ImmutableUnderSourceGuarantee,
                null,
                TranscriptFinalizedScope.ProviderTurn,
                TranscriptBoundaryEvidence.ProviderTurnEnd,
                TranscriptContinuity.Complete,
                TranscriptObservability.Observed,
                TranscriptCorrectionState.CorrectionWindowClosed,
                TranscriptAssemblyClosure.ClosedForAssembly);
            var track = new TranscriptTrackSnapshot(
                TranscriptSourceId.Create(), TranscriptHypothesisId.Create(), 1,
                new TranscriptRevision(1), Encoding.UTF8.GetBytes(transcript.Text),
                _authority, finality, [Hash256.Compute(Encoding.UTF8.GetBytes(transcript.Text))], [], false);
            var policy = new CompiledEndpointPolicy(false, 8, ulong.MaxValue - 1);
            var state = new EndpointState(_authority);
            var opened = EndpointReducer.Reduce(state,
                new SequencedEndpointFact.CandidateOpened(1, _authority,
                    CandidateFamilyId.Create(), EndpointCandidateId.Create(), track),
                policy, 1);
            var assessed = EndpointReducer.Reduce(opened.NextState,
                new SequencedEndpointFact.SemanticAssessed(2, _authority,
                    new SemanticAssessment(SemanticCompletion.CompleteCandidate,
                        InteractionFunction.OrdinaryContent, ProviderTurnTransition.ProviderTurnEnded)),
                policy, 2);
            if (assessed.Disposition is not EndpointDisposition.CommitEligible)
                throw new InvalidOperationException("Accepted endpoint evidence did not prepare a commit.");
            var handoff = OperationId.Create();
            var pending = EndpointReducer.Reduce(assessed.NextState,
                new SequencedEndpointFact.HandoffStarted(3, _authority, handoff), policy, 3);
            var accepted = EndpointReducer.Reduce(pending.NextState,
                new SequencedEndpointFact.HandoffAccepted(4, _authority, handoff), policy, 4);
            if (accepted.NextState.Stage != EndpointCandidateStage.Accepted)
                throw new InvalidOperationException("Endpoint handoff was not accepted.");
            var turnId = evidence.TurnId ?? _ids.NextTurnId();
            var commit = new EndpointCommitProjection
            {
                TurnId = turnId,
                Text = transcript.Text,
                Reason = EndpointCommitProjectionReason.InputMediaTranscript,
                EvidenceIds = _evidence.Select(item => item.Id).ToArray()
            };

            return ValueTask.FromResult(new EndpointDecisionProjection
            {
                Kind = EndpointDecisionProjectionKind.CommitUserTurn,
                DecidedAt = _clock.Tick(),
                TurnId = turnId,
                Reason = "input-media-final-transcript",
                Commit = commit
            });
        }

        return ValueTask.FromResult(new EndpointDecisionProjection
        {
            Kind = EndpointDecisionProjectionKind.ContinueListening,
            DecidedAt = _clock.Tick(),
            TurnId = evidence.TurnId,
            Reason = "waiting-for-input-media-transcript"
        });
    }
}
