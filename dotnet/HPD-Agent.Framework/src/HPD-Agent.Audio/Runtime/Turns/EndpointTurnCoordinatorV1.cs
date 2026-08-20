using System.Text;
using HPD.Agent.Audio.Endpointing;
using HPD.Agent.Audio.Runtime.Endpointing;
using HPD.Agent.Audio.Turns;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Turns;

internal sealed class EndpointTurnCoordinatorV1
{
    private readonly List<EndpointEvidenceProjectionV1> _evidence = [];
    private readonly RuntimeClock _clock;
    private readonly RuntimeIdFactory _ids;
    private readonly ExpectedAuthorityVectorV1 _authority;

    public EndpointTurnCoordinatorV1(
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

    public EndpointSnapshotProjectionV1 Snapshot => new()
    {
        SessionId = SessionId,
        CurrentTurnId = _evidence.LastOrDefault()?.TurnId,
        Evidence = _evidence.ToArray()
    };

    public ValueTask<EndpointDecisionProjectionV1> ObserveAsync(
        EndpointEvidenceProjectionV1 evidence,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _evidence.Add(evidence);

        if (evidence.Detail is TranscriptEvidenceProjectionDetailV1 { IsFinal: true } transcript &&
            evidence.Kind is EndpointEvidenceProjectionKindV1.FinalTranscript or EndpointEvidenceProjectionKindV1.InputMediaTranscribed &&
            !string.IsNullOrWhiteSpace(transcript.Text))
        {
            var finality = new TranscriptFinalityV1(
                TranscriptMutabilityV1.ImmutableUnderSourceGuarantee,
                null,
                TranscriptFinalizedScopeV1.ProviderTurn,
                TranscriptBoundaryEvidenceV1.ProviderTurnEnd,
                TranscriptContinuityV1.Complete,
                TranscriptObservabilityV1.Observed,
                TranscriptCorrectionStateV1.CorrectionWindowClosed,
                TranscriptAssemblyClosureV1.ClosedForAssembly);
            var track = new TranscriptTrackSnapshotV1(
                TranscriptSourceIdV1.Create(), TranscriptHypothesisIdV1.Create(), 1,
                new TranscriptRevisionV1(1), Encoding.UTF8.GetBytes(transcript.Text),
                _authority, finality, [Hash256.Compute(Encoding.UTF8.GetBytes(transcript.Text))], [], false);
            var policy = new CompiledEndpointPolicyV1(false, 8, ulong.MaxValue - 1);
            var state = new EndpointStateV1(_authority);
            var opened = EndpointReducerV1.Reduce(state,
                new SequencedEndpointFactV1.CandidateOpened(1, _authority,
                    CandidateFamilyIdV1.Create(), EndpointCandidateIdV1.Create(), track),
                policy, 1);
            var assessed = EndpointReducerV1.Reduce(opened.NextState,
                new SequencedEndpointFactV1.SemanticAssessed(2, _authority,
                    new SemanticAssessmentV1(SemanticCompletionV1.CompleteCandidate,
                        InteractionFunctionV1.OrdinaryContent, ProviderTurnTransitionV1.ProviderTurnEnded)),
                policy, 2);
            if (assessed.Disposition is not EndpointDispositionV1.CommitEligible)
                throw new InvalidOperationException("Accepted endpoint evidence did not prepare a commit.");
            var handoff = OperationId.Create();
            var pending = EndpointReducerV1.Reduce(assessed.NextState,
                new SequencedEndpointFactV1.HandoffStarted(3, _authority, handoff), policy, 3);
            var accepted = EndpointReducerV1.Reduce(pending.NextState,
                new SequencedEndpointFactV1.HandoffAccepted(4, _authority, handoff), policy, 4);
            if (accepted.NextState.Stage != EndpointCandidateStageV1.Accepted)
                throw new InvalidOperationException("Endpoint handoff was not accepted.");
            var turnId = evidence.TurnId ?? _ids.NextTurnId();
            var commit = new EndpointCommitProjectionV1
            {
                TurnId = turnId,
                Text = transcript.Text,
                Reason = EndpointCommitProjectionReasonV1.InputMediaTranscript,
                EvidenceIds = _evidence.Select(item => item.Id).ToArray()
            };

            return ValueTask.FromResult(new EndpointDecisionProjectionV1
            {
                Kind = EndpointDecisionProjectionKindV1.CommitUserTurn,
                DecidedAt = _clock.Tick(),
                TurnId = turnId,
                Reason = "input-media-final-transcript",
                Commit = commit
            });
        }

        return ValueTask.FromResult(new EndpointDecisionProjectionV1
        {
            Kind = EndpointDecisionProjectionKindV1.ContinueListening,
            DecidedAt = _clock.Tick(),
            TurnId = evidence.TurnId,
            Reason = "waiting-for-input-media-transcript"
        });
    }
}
