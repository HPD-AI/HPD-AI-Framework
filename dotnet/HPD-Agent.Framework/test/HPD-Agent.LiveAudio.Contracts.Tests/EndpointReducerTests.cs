using System.Text;
using HPD.Agent.Audio.Endpointing;
using HPD.Agent.Audio.Runtime.Endpointing;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class EndpointReducerTests
{
    [Fact]
    public void Provider_finality_alone_never_commits()
    {
        var f = new Fixture(providerFinal: true);
        var opened = f.Open();

        Assert.Equal(EndpointCandidateStage.Assessing, opened.NextState.Stage);
        Assert.IsType<EndpointDisposition.AwaitEvidence>(opened.Disposition);
        Assert.IsType<EndpointIntent.RequestSemanticAssessment>(Assert.Single(opened.Intents));
    }

    [Fact]
    public void Backchannel_and_incomplete_semantics_remain_independent_noncommit_evidence()
    {
        var f = new Fixture(providerFinal: true);
        var opened = f.Open();
        var assessment = new SemanticAssessment(SemanticCompletion.IncompleteLong,
            InteractionFunction.BackchannelOpportunity, ProviderTurnTransition.EagerEndCandidate);

        var transition = EndpointReducer.Reduce(opened.NextState,
            new SequencedEndpointFact.SemanticAssessed(2, f.Authority, assessment), f.Policy, 10);

        Assert.Equal(EndpointCandidateStage.Open, transition.NextState.Stage);
        Assert.IsType<EndpointDisposition.AwaitEvidence>(transition.Disposition);
        Assert.Empty(transition.Intents);
    }

    [Fact]
    public void Complete_semantics_prepare_exactly_one_handoff_intent()
    {
        var f = new Fixture();
        var opened = f.Open();
        var assessment = new SemanticAssessment(SemanticCompletion.CompleteCandidate,
            InteractionFunction.OrdinaryContent, ProviderTurnTransition.NotObservable);

        var transition = EndpointReducer.Reduce(opened.NextState,
            new SequencedEndpointFact.SemanticAssessed(2, f.Authority, assessment), f.Policy, 10);

        Assert.Equal(EndpointCandidateStage.DecisionPrepared, transition.NextState.Stage);
        var eligible = Assert.IsType<EndpointDisposition.CommitEligible>(transition.Disposition);
        Assert.False(eligible.ExplicitIncomplete);
        var intent = Assert.IsType<EndpointIntent.PersistDecisionAndPrepareHandoff>(Assert.Single(transition.Intents));
        Assert.Equal(transition.NextState.DecisionId, intent.Decision);
        Assert.Same(f.Snapshot, intent.Snapshot);
    }

    [Fact]
    public void Manual_commit_is_explicitly_incomplete_and_policy_gated()
    {
        var allowed = new Fixture(allowManual: true);
        var opened = allowed.Open();
        var accepted = EndpointReducer.Reduce(opened.NextState,
            new SequencedEndpointFact.ManualCommitRequested(2, allowed.Authority, new BoundedAscii("push-to-talk")),
            allowed.Policy, 10);
        Assert.True(Assert.IsType<EndpointDisposition.CommitEligible>(accepted.Disposition).ExplicitIncomplete);

        var denied = new Fixture(allowManual: false);
        var deniedOpen = denied.Open();
        Assert.Equal("manual-incomplete-disabled", Assert.IsType<EndpointDisposition.Reject>(
            EndpointReducer.Reduce(deniedOpen.NextState,
                new SequencedEndpointFact.ManualCommitRequested(2, denied.Authority, new BoundedAscii("push-to-talk")),
                denied.Policy, 10).Disposition).Reason.ToString());
    }

    [Fact]
    public void Possible_handoff_escape_enters_reconciliation_and_cannot_locally_reject()
    {
        var f = new Fixture();
        var prepared = f.Prepare();
        var operation = OperationId.Create();
        var pending = EndpointReducer.Reduce(prepared.NextState,
            new SequencedEndpointFact.HandoffStarted(3, f.Authority, operation), f.Policy, 10);
        var unknown = EndpointReducer.Reduce(pending.NextState,
            new SequencedEndpointFact.HandoffOutcomeUnknown(4, f.Authority, operation), f.Policy, 10);

        Assert.Equal(EndpointCandidateStage.Reconciling, unknown.NextState.Stage);
        Assert.IsType<EndpointDisposition.AwaitEvidence>(unknown.Disposition);
        Assert.Equal(operation, Assert.IsType<EndpointIntent.ReconcileHandoff>(Assert.Single(unknown.Intents)).Operation);
    }

    [Fact]
    public void Authority_sequence_deadline_capacity_and_terminal_states_fail_closed()
    {
        var f = new Fixture(maximumFacts: 2);
        var opened = f.Open();
        var wrongSession = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var wrongAuthority = ExpectedAuthorityVectorV1.Create(wrongSession, []);
        Assert.Equal("authority-stale", Assert.IsType<EndpointDisposition.Unavailable>(
            EndpointReducer.Reduce(opened.NextState,
                new SequencedEndpointFact.EvidenceDeadlineExpired(2, wrongAuthority), f.Policy, 200).Disposition).Reason.ToString());
        Assert.Equal("session-sequence-invalid", Assert.IsType<EndpointDisposition.Unavailable>(
            EndpointReducer.Reduce(opened.NextState,
                new SequencedEndpointFact.EvidenceDeadlineExpired(3, f.Authority), f.Policy, 200).Disposition).Reason.ToString());
        Assert.Equal("deadline-not-due", Assert.IsType<EndpointDisposition.Reject>(
            EndpointReducer.Reduce(opened.NextState,
                new SequencedEndpointFact.EvidenceDeadlineExpired(2, f.Authority), f.Policy, 99).Disposition).Reason.ToString());
        var expired = EndpointReducer.Reduce(opened.NextState,
            new SequencedEndpointFact.EvidenceDeadlineExpired(2, f.Authority), f.Policy, 100);
        Assert.Equal(EndpointCandidateStage.Unavailable, expired.NextState.Stage);
        Assert.Equal("candidate-terminal", Assert.IsType<EndpointDisposition.Reject>(
            EndpointReducer.Reduce(expired.NextState,
                new SequencedEndpointFact.EvidenceDeadlineExpired(3, f.Authority), f.Policy, 101).Disposition).Reason.ToString());
    }

    [Fact]
    public void Reducer_is_deterministic_for_non_identity_creating_transitions()
    {
        var f = new Fixture(providerFinal: true);
        var first = f.Open();
        var second = EndpointReducer.Reduce(new EndpointState(f.Authority),
            new SequencedEndpointFact.CandidateOpened(1, f.Authority, f.Family, f.Candidate, f.Snapshot), f.Policy, 10);

        Assert.Equal(first.NextState.Stage, second.NextState.Stage);
        Assert.Equal(first.NextState.EvaluationRevision, second.NextState.EvaluationRevision);
        Assert.Equal(first.NextState.PlanRevision, second.NextState.PlanRevision);
        Assert.Equal(first.Disposition, second.Disposition);
        Assert.Equal(first.Intents, second.Intents);

        var assessment = new SemanticAssessment(SemanticCompletion.CompleteCandidate,
            InteractionFunction.OrdinaryContent, ProviderTurnTransition.NotObservable);
        var prepared1 = EndpointReducer.Reduce(first.NextState,
            new SequencedEndpointFact.SemanticAssessed(2, f.Authority, assessment), f.Policy, 10);
        var prepared2 = EndpointReducer.Reduce(second.NextState,
            new SequencedEndpointFact.SemanticAssessed(2, f.Authority, assessment), f.Policy, 10);
        Assert.Equal(prepared1.NextState.DecisionId, prepared2.NextState.DecisionId);
        Assert.Equal(prepared1.Intents, prepared2.Intents);
    }

    private sealed class Fixture
    {
        internal Fixture(bool providerFinal = false, bool allowManual = true, uint maximumFacts = 16)
        {
            var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
            Authority = ExpectedAuthorityVectorV1.Create(session, []);
            Family = CandidateFamilyId.Create();
            Candidate = EndpointCandidateId.Create();
            Policy = new CompiledEndpointPolicy(allowManual, maximumFacts, 100);
            var finality = providerFinal
                ? new TranscriptFinality(TranscriptMutability.ImmutableUnderSourceGuarantee, null,
                    TranscriptFinalizedScope.ProviderItem, TranscriptBoundaryEvidence.ProviderEndpoint,
                    TranscriptContinuity.Complete, TranscriptObservability.Observed,
                    TranscriptCorrectionState.CorrectionWindowClosed, TranscriptAssemblyClosure.ClosedForAssembly)
                : new TranscriptFinality(TranscriptMutability.Volatile, null,
                    TranscriptFinalizedScope.None, TranscriptBoundaryEvidence.None,
                    TranscriptContinuity.Complete, TranscriptObservability.Observed,
                    TranscriptCorrectionState.Correctable, TranscriptAssemblyClosure.Open);
            Snapshot = new TranscriptTrackSnapshot(TranscriptSourceId.Create(), TranscriptHypothesisId.Create(),
                2, new TranscriptRevision(2), Encoding.UTF8.GetBytes("hello"), Authority, finality,
                [Hash256.Compute([1])], [], false);
        }
        internal ExpectedAuthorityVectorV1 Authority { get; }
        internal CandidateFamilyId Family { get; }
        internal EndpointCandidateId Candidate { get; }
        internal CompiledEndpointPolicy Policy { get; }
        internal TranscriptTrackSnapshot Snapshot { get; }
        internal EndpointTransition Open() => EndpointReducer.Reduce(new EndpointState(Authority),
            new SequencedEndpointFact.CandidateOpened(1, Authority, Family, Candidate, Snapshot), Policy, 10);
        internal EndpointTransition Prepare()
        {
            var opened = Open();
            return EndpointReducer.Reduce(opened.NextState,
                new SequencedEndpointFact.SemanticAssessed(2, Authority,
                    new SemanticAssessment(SemanticCompletion.CompleteCandidate,
                        InteractionFunction.OrdinaryContent, ProviderTurnTransition.NotObservable)), Policy, 10);
        }
    }
}
