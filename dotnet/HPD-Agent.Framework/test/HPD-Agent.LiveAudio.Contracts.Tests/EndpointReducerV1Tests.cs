using System.Text;
using HPD.Agent.Audio.Endpointing;
using HPD.Agent.Audio.Runtime.Endpointing;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class EndpointReducerV1Tests
{
    [Fact]
    public void Provider_finality_alone_never_commits()
    {
        var f = new Fixture(providerFinal: true);
        var opened = f.Open();

        Assert.Equal(EndpointCandidateStageV1.Assessing, opened.NextState.Stage);
        Assert.IsType<EndpointDispositionV1.AwaitEvidence>(opened.Disposition);
        Assert.IsType<EndpointIntentV1.RequestSemanticAssessment>(Assert.Single(opened.Intents));
    }

    [Fact]
    public void Backchannel_and_incomplete_semantics_remain_independent_noncommit_evidence()
    {
        var f = new Fixture(providerFinal: true);
        var opened = f.Open();
        var assessment = new SemanticAssessmentV1(SemanticCompletionV1.IncompleteLong,
            InteractionFunctionV1.BackchannelOpportunity, ProviderTurnTransitionV1.EagerEndCandidate);

        var transition = EndpointReducerV1.Reduce(opened.NextState,
            new SequencedEndpointFactV1.SemanticAssessed(2, f.Authority, assessment), f.Policy, 10);

        Assert.Equal(EndpointCandidateStageV1.Open, transition.NextState.Stage);
        Assert.IsType<EndpointDispositionV1.AwaitEvidence>(transition.Disposition);
        Assert.Empty(transition.Intents);
    }

    [Fact]
    public void Complete_semantics_prepare_exactly_one_handoff_intent()
    {
        var f = new Fixture();
        var opened = f.Open();
        var assessment = new SemanticAssessmentV1(SemanticCompletionV1.CompleteCandidate,
            InteractionFunctionV1.OrdinaryContent, ProviderTurnTransitionV1.NotObservable);

        var transition = EndpointReducerV1.Reduce(opened.NextState,
            new SequencedEndpointFactV1.SemanticAssessed(2, f.Authority, assessment), f.Policy, 10);

        Assert.Equal(EndpointCandidateStageV1.DecisionPrepared, transition.NextState.Stage);
        var eligible = Assert.IsType<EndpointDispositionV1.CommitEligible>(transition.Disposition);
        Assert.False(eligible.ExplicitIncomplete);
        var intent = Assert.IsType<EndpointIntentV1.PersistDecisionAndPrepareHandoff>(Assert.Single(transition.Intents));
        Assert.Equal(transition.NextState.DecisionId, intent.Decision);
        Assert.Same(f.Snapshot, intent.Snapshot);
    }

    [Fact]
    public void Manual_commit_is_explicitly_incomplete_and_policy_gated()
    {
        var allowed = new Fixture(allowManual: true);
        var opened = allowed.Open();
        var accepted = EndpointReducerV1.Reduce(opened.NextState,
            new SequencedEndpointFactV1.ManualCommitRequested(2, allowed.Authority, new BoundedAscii("push-to-talk")),
            allowed.Policy, 10);
        Assert.True(Assert.IsType<EndpointDispositionV1.CommitEligible>(accepted.Disposition).ExplicitIncomplete);

        var denied = new Fixture(allowManual: false);
        var deniedOpen = denied.Open();
        Assert.Equal("manual-incomplete-disabled", Assert.IsType<EndpointDispositionV1.Reject>(
            EndpointReducerV1.Reduce(deniedOpen.NextState,
                new SequencedEndpointFactV1.ManualCommitRequested(2, denied.Authority, new BoundedAscii("push-to-talk")),
                denied.Policy, 10).Disposition).Reason.ToString());
    }

    [Fact]
    public void Possible_handoff_escape_enters_reconciliation_and_cannot_locally_reject()
    {
        var f = new Fixture();
        var prepared = f.Prepare();
        var operation = OperationId.Create();
        var pending = EndpointReducerV1.Reduce(prepared.NextState,
            new SequencedEndpointFactV1.HandoffStarted(3, f.Authority, operation), f.Policy, 10);
        var unknown = EndpointReducerV1.Reduce(pending.NextState,
            new SequencedEndpointFactV1.HandoffOutcomeUnknown(4, f.Authority, operation), f.Policy, 10);

        Assert.Equal(EndpointCandidateStageV1.Reconciling, unknown.NextState.Stage);
        Assert.IsType<EndpointDispositionV1.AwaitEvidence>(unknown.Disposition);
        Assert.Equal(operation, Assert.IsType<EndpointIntentV1.ReconcileHandoff>(Assert.Single(unknown.Intents)).Operation);
    }

    [Fact]
    public void Authority_sequence_deadline_capacity_and_terminal_states_fail_closed()
    {
        var f = new Fixture(maximumFacts: 2);
        var opened = f.Open();
        var wrongSession = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var wrongAuthority = ExpectedAuthorityVectorV1.Create(wrongSession, []);
        Assert.Equal("authority-stale", Assert.IsType<EndpointDispositionV1.Unavailable>(
            EndpointReducerV1.Reduce(opened.NextState,
                new SequencedEndpointFactV1.EvidenceDeadlineExpired(2, wrongAuthority), f.Policy, 200).Disposition).Reason.ToString());
        Assert.Equal("session-sequence-invalid", Assert.IsType<EndpointDispositionV1.Unavailable>(
            EndpointReducerV1.Reduce(opened.NextState,
                new SequencedEndpointFactV1.EvidenceDeadlineExpired(3, f.Authority), f.Policy, 200).Disposition).Reason.ToString());
        Assert.Equal("deadline-not-due", Assert.IsType<EndpointDispositionV1.Reject>(
            EndpointReducerV1.Reduce(opened.NextState,
                new SequencedEndpointFactV1.EvidenceDeadlineExpired(2, f.Authority), f.Policy, 99).Disposition).Reason.ToString());
        var expired = EndpointReducerV1.Reduce(opened.NextState,
            new SequencedEndpointFactV1.EvidenceDeadlineExpired(2, f.Authority), f.Policy, 100);
        Assert.Equal(EndpointCandidateStageV1.Unavailable, expired.NextState.Stage);
        Assert.Equal("candidate-terminal", Assert.IsType<EndpointDispositionV1.Reject>(
            EndpointReducerV1.Reduce(expired.NextState,
                new SequencedEndpointFactV1.EvidenceDeadlineExpired(3, f.Authority), f.Policy, 101).Disposition).Reason.ToString());
    }

    [Fact]
    public void Reducer_is_deterministic_for_non_identity_creating_transitions()
    {
        var f = new Fixture(providerFinal: true);
        var first = f.Open();
        var second = EndpointReducerV1.Reduce(new EndpointStateV1(f.Authority),
            new SequencedEndpointFactV1.CandidateOpened(1, f.Authority, f.Family, f.Candidate, f.Snapshot), f.Policy, 10);

        Assert.Equal(first.NextState.Stage, second.NextState.Stage);
        Assert.Equal(first.NextState.EvaluationRevision, second.NextState.EvaluationRevision);
        Assert.Equal(first.NextState.PlanRevision, second.NextState.PlanRevision);
        Assert.Equal(first.Disposition, second.Disposition);
        Assert.Equal(first.Intents, second.Intents);

        var assessment = new SemanticAssessmentV1(SemanticCompletionV1.CompleteCandidate,
            InteractionFunctionV1.OrdinaryContent, ProviderTurnTransitionV1.NotObservable);
        var prepared1 = EndpointReducerV1.Reduce(first.NextState,
            new SequencedEndpointFactV1.SemanticAssessed(2, f.Authority, assessment), f.Policy, 10);
        var prepared2 = EndpointReducerV1.Reduce(second.NextState,
            new SequencedEndpointFactV1.SemanticAssessed(2, f.Authority, assessment), f.Policy, 10);
        Assert.Equal(prepared1.NextState.DecisionId, prepared2.NextState.DecisionId);
        Assert.Equal(prepared1.Intents, prepared2.Intents);
    }

    private sealed class Fixture
    {
        internal Fixture(bool providerFinal = false, bool allowManual = true, uint maximumFacts = 16)
        {
            var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
            Authority = ExpectedAuthorityVectorV1.Create(session, []);
            Family = CandidateFamilyIdV1.Create();
            Candidate = EndpointCandidateIdV1.Create();
            Policy = new CompiledEndpointPolicyV1(allowManual, maximumFacts, 100);
            var finality = providerFinal
                ? new TranscriptFinalityV1(TranscriptMutabilityV1.ImmutableUnderSourceGuarantee, null,
                    TranscriptFinalizedScopeV1.ProviderItem, TranscriptBoundaryEvidenceV1.ProviderEndpoint,
                    TranscriptContinuityV1.Complete, TranscriptObservabilityV1.Observed,
                    TranscriptCorrectionStateV1.CorrectionWindowClosed, TranscriptAssemblyClosureV1.ClosedForAssembly)
                : new TranscriptFinalityV1(TranscriptMutabilityV1.Volatile, null,
                    TranscriptFinalizedScopeV1.None, TranscriptBoundaryEvidenceV1.None,
                    TranscriptContinuityV1.Complete, TranscriptObservabilityV1.Observed,
                    TranscriptCorrectionStateV1.Correctable, TranscriptAssemblyClosureV1.Open);
            Snapshot = new TranscriptTrackSnapshotV1(TranscriptSourceIdV1.Create(), TranscriptHypothesisIdV1.Create(),
                2, new TranscriptRevisionV1(2), Encoding.UTF8.GetBytes("hello"), Authority, finality,
                [Hash256.Compute([1])], [], false);
        }
        internal ExpectedAuthorityVectorV1 Authority { get; }
        internal CandidateFamilyIdV1 Family { get; }
        internal EndpointCandidateIdV1 Candidate { get; }
        internal CompiledEndpointPolicyV1 Policy { get; }
        internal TranscriptTrackSnapshotV1 Snapshot { get; }
        internal EndpointTransitionV1 Open() => EndpointReducerV1.Reduce(new EndpointStateV1(Authority),
            new SequencedEndpointFactV1.CandidateOpened(1, Authority, Family, Candidate, Snapshot), Policy, 10);
        internal EndpointTransitionV1 Prepare()
        {
            var opened = Open();
            return EndpointReducerV1.Reduce(opened.NextState,
                new SequencedEndpointFactV1.SemanticAssessed(2, Authority,
                    new SemanticAssessmentV1(SemanticCompletionV1.CompleteCandidate,
                        InteractionFunctionV1.OrdinaryContent, ProviderTurnTransitionV1.NotObservable)), Policy, 10);
        }
    }
}
