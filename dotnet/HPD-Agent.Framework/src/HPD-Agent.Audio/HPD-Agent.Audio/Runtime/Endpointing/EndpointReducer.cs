using HPD.Agent.Authority;
using HPD.Agent.Audio.Endpointing;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HPD.Agent.Audio.Runtime.Endpointing;

internal readonly record struct CandidateFamilyId
{
    private CandidateFamilyId(StableId128 value) => Value = value;
    internal StableId128 Value { get; }
    internal bool IsValid => !Value.Equals(default);
    internal static CandidateFamilyId Create() => new(StableId128.CreateRandom());
}

internal readonly record struct EndpointCandidateId
{
    private EndpointCandidateId(StableId128 value) => Value = value;
    internal StableId128 Value { get; }
    internal bool IsValid => !Value.Equals(default);
    internal static EndpointCandidateId Create() => new(StableId128.CreateRandom());
}

internal readonly record struct EndpointDecisionId
{
    private EndpointDecisionId(StableId128 value) => Value = value;
    internal StableId128 Value { get; }
    internal bool IsValid => !Value.Equals(default);
    internal static EndpointDecisionId Derive(EndpointCandidateId candidate, uint planRevision)
    {
        if (!candidate.IsValid || planRevision == 0) throw new ArgumentException("Candidate and plan revision are required.");
        Span<byte> preimage = stackalloc byte[16 + 4 + 24];
        "hpd.endpoint-decision.v1"u8.CopyTo(preimage);
        if (!candidate.Value.TryWriteBytes(preimage[24..40])) throw new ArgumentException("Candidate is invalid.", nameof(candidate));
        BinaryPrimitives.WriteUInt32BigEndian(preimage[40..], planRevision);
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(preimage, digest);
        return new(StableId128.FromBytes(digest[..16]));
    }
}

internal enum EndpointCandidateStage : ushort
{
    None = 1,
    Open = 2,
    Assessing = 3,
    DecisionPrepared = 4,
    HandoffPending = 5,
    WithdrawPending = 6,
    Reconciling = 7,
    Accepted = 8,
    Rejected = 9,
    Cancelled = 10,
    Unavailable = 11,
    Withdrawn = 12,
    RejectedKnown = 13,
}

internal sealed record CompiledEndpointPolicy
{
    internal CompiledEndpointPolicy(bool allowExplicitIncompleteManualCommit,
        uint maximumSequencedFacts, ulong evidenceDeadlineMonotonicNanoseconds)
    {
        if (maximumSequencedFacts == 0 || evidenceDeadlineMonotonicNanoseconds == 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSequencedFacts));
        AllowExplicitIncompleteManualCommit = allowExplicitIncompleteManualCommit;
        MaximumSequencedFacts = maximumSequencedFacts;
        EvidenceDeadlineMonotonicNanoseconds = evidenceDeadlineMonotonicNanoseconds;
    }
    internal bool AllowExplicitIncompleteManualCommit { get; }
    internal uint MaximumSequencedFacts { get; }
    internal ulong EvidenceDeadlineMonotonicNanoseconds { get; }
}

internal sealed record EndpointState
{
    internal EndpointState(ExpectedAuthorityVectorV1 authority)
    {
        Authority = authority ?? throw new ArgumentNullException(nameof(authority));
        Stage = EndpointCandidateStage.None;
    }

    private EndpointState(ExpectedAuthorityVectorV1 authority, ulong lastSequence,
        EndpointCandidateStage stage, CandidateFamilyId familyId, EndpointCandidateId candidateId,
        uint evaluationRevision, uint planRevision, TranscriptTrackSnapshot? snapshot,
        SemanticAssessment? assessment, EndpointDecisionId decisionId, OperationId handoffOperation,
        uint appliedFactCount)
    {
        Authority = authority;
        LastSequence = lastSequence;
        Stage = stage;
        FamilyId = familyId;
        CandidateId = candidateId;
        EvaluationRevision = evaluationRevision;
        PlanRevision = planRevision;
        Snapshot = snapshot;
        Assessment = assessment;
        DecisionId = decisionId;
        HandoffOperation = handoffOperation;
        AppliedFactCount = appliedFactCount;
    }

    internal ExpectedAuthorityVectorV1 Authority { get; }
    internal ulong LastSequence { get; }
    internal EndpointCandidateStage Stage { get; }
    internal CandidateFamilyId FamilyId { get; }
    internal EndpointCandidateId CandidateId { get; }
    internal uint EvaluationRevision { get; }
    internal uint PlanRevision { get; }
    internal TranscriptTrackSnapshot? Snapshot { get; }
    internal SemanticAssessment? Assessment { get; }
    internal EndpointDecisionId DecisionId { get; }
    internal OperationId HandoffOperation { get; }
    internal uint AppliedFactCount { get; }

    internal EndpointState Next(ulong sequence, EndpointCandidateStage stage,
        CandidateFamilyId familyId, EndpointCandidateId candidateId, uint evaluationRevision,
        uint planRevision, TranscriptTrackSnapshot? snapshot, SemanticAssessment? assessment,
        EndpointDecisionId decisionId, OperationId handoffOperation) =>
        new(Authority, sequence, stage, familyId, candidateId, evaluationRevision, planRevision,
            snapshot, assessment, decisionId, handoffOperation, checked(AppliedFactCount + 1));
}

internal abstract record SequencedEndpointFact
{
    private protected SequencedEndpointFact(ulong sequence, ExpectedAuthorityVectorV1 authority)
    {
        if (sequence == 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        Sequence = sequence;
        Authority = authority ?? throw new ArgumentNullException(nameof(authority));
    }
    internal ulong Sequence { get; }
    internal ExpectedAuthorityVectorV1 Authority { get; }

    internal sealed record CandidateOpened : SequencedEndpointFact
    {
        internal CandidateOpened(ulong q, ExpectedAuthorityVectorV1 a, CandidateFamilyId family,
            EndpointCandidateId candidate, TranscriptTrackSnapshot snapshot) : base(q, a)
        {
            if (!family.IsValid || !candidate.IsValid) throw new ArgumentException("Candidate identities are required.");
            Family = family; Candidate = candidate; Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }
        internal CandidateFamilyId Family { get; }
        internal EndpointCandidateId Candidate { get; }
        internal TranscriptTrackSnapshot Snapshot { get; }
    }
    internal sealed record TranscriptAdvanced(ulong Q, ExpectedAuthorityVectorV1 A, TranscriptTrackSnapshot Snapshot) : SequencedEndpointFact(Q, A);
    internal sealed record SemanticAssessed(ulong Q, ExpectedAuthorityVectorV1 A, SemanticAssessment Assessment) : SequencedEndpointFact(Q, A);
    internal sealed record ManualCommitRequested(ulong Q, ExpectedAuthorityVectorV1 A, BoundedAscii Reason) : SequencedEndpointFact(Q, A);
    internal sealed record EvidenceDeadlineExpired(ulong Q, ExpectedAuthorityVectorV1 A) : SequencedEndpointFact(Q, A);
    internal sealed record ContinuityLost(ulong Q, ExpectedAuthorityVectorV1 A, BoundedAscii Reason) : SequencedEndpointFact(Q, A);
    internal sealed record HandoffStarted : SequencedEndpointFact
    {
        internal HandoffStarted(ulong q, ExpectedAuthorityVectorV1 a, OperationId operation) : base(q, a)
        { if (!operation.IsValid) throw new ArgumentException("A handoff operation is required.", nameof(operation)); Operation = operation; }
        internal OperationId Operation { get; }
    }
    internal sealed record HandoffAccepted(ulong Q, ExpectedAuthorityVectorV1 A, OperationId Operation) : SequencedEndpointFact(Q, A);
    internal sealed record HandoffRejectedKnown(ulong Q, ExpectedAuthorityVectorV1 A, OperationId Operation, BoundedAscii Reason) : SequencedEndpointFact(Q, A);
    internal sealed record HandoffOutcomeUnknown(ulong Q, ExpectedAuthorityVectorV1 A, OperationId Operation) : SequencedEndpointFact(Q, A);
    internal sealed record WithdrawRequested(ulong Q, ExpectedAuthorityVectorV1 A, OperationId Operation) : SequencedEndpointFact(Q, A);
    internal sealed record Withdrawn(ulong Q, ExpectedAuthorityVectorV1 A, OperationId Operation) : SequencedEndpointFact(Q, A);
}

internal abstract record EndpointDisposition
{
    private EndpointDisposition() { }
    internal sealed record ContinueListening : EndpointDisposition;
    internal sealed record AwaitEvidence(BoundedAscii Required, ulong DeadlineMonotonicNanoseconds) : EndpointDisposition;
    internal sealed record Reject(BoundedAscii Reason) : EndpointDisposition;
    internal sealed record CommitEligible(TranscriptTrackSnapshot Snapshot, bool ExplicitIncomplete, BoundedAscii Reason) : EndpointDisposition;
    internal sealed record Cancel(BoundedAscii Reason) : EndpointDisposition;
    internal sealed record Unavailable(BoundedAscii Reason) : EndpointDisposition;
}

internal abstract record EndpointIntent
{
    private EndpointIntent() { }
    internal sealed record RequestSemanticAssessment(EndpointCandidateId Candidate, uint EvaluationRevision) : EndpointIntent;
    internal sealed record PersistDecisionAndPrepareHandoff(EndpointDecisionId Decision,
        EndpointCandidateId Candidate, TranscriptTrackSnapshot Snapshot, bool ExplicitIncomplete) : EndpointIntent;
    internal sealed record ReconcileHandoff(OperationId Operation) : EndpointIntent;
    internal sealed record WithdrawHandoff(OperationId Operation) : EndpointIntent;
}

internal sealed record EndpointTransition(EndpointState NextState,
    EndpointDisposition Disposition, IReadOnlyList<EndpointIntent> Intents);

internal static class EndpointReducer
{
    internal static EndpointTransition Reduce(EndpointState state, SequencedEndpointFact fact,
        CompiledEndpointPolicy policy, ulong monotonicNowNanoseconds)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentNullException.ThrowIfNull(policy);
        if (fact.Authority != state.Authority)
            return Same(state, new EndpointDisposition.Unavailable(new BoundedAscii("authority-stale")));
        if (fact.Sequence != checked(state.LastSequence + 1))
            return Same(state, new EndpointDisposition.Unavailable(new BoundedAscii("session-sequence-invalid")));
        if (Terminal(state.Stage))
            return Same(state, new EndpointDisposition.Reject(new BoundedAscii("candidate-terminal")));
        if (state.AppliedFactCount >= policy.MaximumSequencedFacts)
            return Same(state, new EndpointDisposition.Unavailable(new BoundedAscii("fact-capacity-refused")));

        return fact switch
        {
            SequencedEndpointFact.CandidateOpened opened when state.Stage == EndpointCandidateStage.None =>
                Open(state, opened, policy),
            SequencedEndpointFact.TranscriptAdvanced advanced when Active(state.Stage) =>
                Transcript(state, advanced, policy),
            SequencedEndpointFact.SemanticAssessed assessed when state.Stage is EndpointCandidateStage.Open or EndpointCandidateStage.Assessing =>
                Assess(state, assessed, policy),
            SequencedEndpointFact.ManualCommitRequested manual when state.Stage is EndpointCandidateStage.Open or EndpointCandidateStage.Assessing =>
                Manual(state, manual, policy),
            SequencedEndpointFact.EvidenceDeadlineExpired when state.Stage is EndpointCandidateStage.Open or EndpointCandidateStage.Assessing =>
                Deadline(state, fact, monotonicNowNanoseconds, policy),
            SequencedEndpointFact.ContinuityLost lost when Active(state.Stage) =>
                Unavailable(state, lost.Sequence, lost.Reason),
            SequencedEndpointFact.HandoffStarted started when state.Stage == EndpointCandidateStage.DecisionPrepared =>
                Handoff(state, started),
            SequencedEndpointFact.HandoffAccepted accepted when state.Stage is EndpointCandidateStage.HandoffPending or EndpointCandidateStage.Reconciling =>
                HandoffTerminal(state, accepted.Sequence, accepted.Operation, EndpointCandidateStage.Accepted, "accepted"),
            SequencedEndpointFact.HandoffRejectedKnown rejected when state.Stage is EndpointCandidateStage.HandoffPending or EndpointCandidateStage.Reconciling =>
                HandoffTerminal(state, rejected.Sequence, rejected.Operation, EndpointCandidateStage.RejectedKnown, rejected.Reason.ToString()),
            SequencedEndpointFact.HandoffOutcomeUnknown unknown when state.Stage == EndpointCandidateStage.HandoffPending =>
                Reconcile(state, unknown),
            SequencedEndpointFact.WithdrawRequested withdraw when state.Stage == EndpointCandidateStage.HandoffPending =>
                Withdraw(state, withdraw),
            SequencedEndpointFact.Withdrawn withdrawn when state.Stage == EndpointCandidateStage.WithdrawPending =>
                HandoffTerminal(state, withdrawn.Sequence, withdrawn.Operation, EndpointCandidateStage.Withdrawn, "withdrawn"),
            _ => Same(state, new EndpointDisposition.Reject(new BoundedAscii("transition-invalid"))),
        };
    }

    private static EndpointTransition Open(EndpointState state, SequencedEndpointFact.CandidateOpened fact,
        CompiledEndpointPolicy policy)
    {
        if (fact.Snapshot.Authority != state.Authority || fact.Snapshot.ContinuityLost)
            return Same(state, new EndpointDisposition.Unavailable(new BoundedAscii("snapshot-ineligible")));
        var next = state.Next(fact.Sequence, EndpointCandidateStage.Assessing, fact.Family, fact.Candidate,
            1, 1, fact.Snapshot, null, default, default);
        return new(next, new EndpointDisposition.AwaitEvidence(new BoundedAscii("semantic-assessment"),
            policy.EvidenceDeadlineMonotonicNanoseconds), [new EndpointIntent.RequestSemanticAssessment(fact.Candidate, 1)]);
    }

    private static EndpointTransition Transcript(EndpointState state, SequencedEndpointFact.TranscriptAdvanced fact,
        CompiledEndpointPolicy policy)
    {
        if (fact.Snapshot.SourceId != state.Snapshot!.SourceId || fact.Snapshot.HypothesisId != state.Snapshot.HypothesisId ||
            fact.Snapshot.Revision.Value <= state.Snapshot.Revision.Value || fact.Snapshot.Authority != state.Authority || fact.Snapshot.ContinuityLost)
            return Same(state, new EndpointDisposition.Unavailable(new BoundedAscii("snapshot-advance-invalid")));
        var evaluation = checked(state.EvaluationRevision + 1);
        var next = state.Next(fact.Sequence, EndpointCandidateStage.Assessing, state.FamilyId, state.CandidateId,
            evaluation, checked(state.PlanRevision + 1), fact.Snapshot, null, default, default);
        return new(next, new EndpointDisposition.AwaitEvidence(new BoundedAscii("semantic-assessment"),
            policy.EvidenceDeadlineMonotonicNanoseconds), [new EndpointIntent.RequestSemanticAssessment(state.CandidateId, evaluation)]);
    }

    private static EndpointTransition Assess(EndpointState state, SequencedEndpointFact.SemanticAssessed fact,
        CompiledEndpointPolicy policy)
    {
        if (state.Snapshot is null || state.Snapshot.ContinuityLost || string.IsNullOrEmpty(state.Snapshot.Text))
            return Unavailable(state, fact.Sequence, new BoundedAscii("snapshot-ineligible"));
        if (fact.Assessment.Completion != SemanticCompletion.CompleteCandidate)
        {
            var next = state.Next(fact.Sequence, EndpointCandidateStage.Open, state.FamilyId, state.CandidateId,
                state.EvaluationRevision, state.PlanRevision, state.Snapshot, fact.Assessment, default, default);
            return new(next, new EndpointDisposition.AwaitEvidence(new BoundedAscii("completion-evidence"),
                policy.EvidenceDeadlineMonotonicNanoseconds), []);
        }
        return Prepare(state, fact.Sequence, fact.Assessment, false, "semantic-complete");
    }

    private static EndpointTransition Manual(EndpointState state, SequencedEndpointFact.ManualCommitRequested fact,
        CompiledEndpointPolicy policy)
    {
        if (!fact.Reason.IsValid) return Same(state, new EndpointDisposition.Reject(new BoundedAscii("manual-reason-invalid")));
        if (!policy.AllowExplicitIncompleteManualCommit)
            return Same(state, new EndpointDisposition.Reject(new BoundedAscii("manual-incomplete-disabled")));
        if (state.Snapshot is null || state.Snapshot.ContinuityLost || string.IsNullOrEmpty(state.Snapshot.Text))
            return Same(state, new EndpointDisposition.Reject(new BoundedAscii("manual-snapshot-ineligible")));
        return Prepare(state, fact.Sequence, state.Assessment, true, fact.Reason.ToString());
    }

    private static EndpointTransition Deadline(EndpointState state, SequencedEndpointFact fact,
        ulong now, CompiledEndpointPolicy policy) => now < policy.EvidenceDeadlineMonotonicNanoseconds
        ? Same(state, new EndpointDisposition.Reject(new BoundedAscii("deadline-not-due")))
        : Unavailable(state, fact.Sequence, new BoundedAscii("evidence-deadline-expired"));

    private static EndpointTransition Prepare(EndpointState state, ulong sequence,
        SemanticAssessment? assessment, bool incomplete, string reason)
    {
        var nextPlanRevision = checked(state.PlanRevision + 1);
        var decision = EndpointDecisionId.Derive(state.CandidateId, nextPlanRevision);
        var next = state.Next(sequence, EndpointCandidateStage.DecisionPrepared, state.FamilyId, state.CandidateId,
            state.EvaluationRevision, nextPlanRevision, state.Snapshot, assessment, decision, default);
        return new(next, new EndpointDisposition.CommitEligible(state.Snapshot!, incomplete, new BoundedAscii(reason)),
            [new EndpointIntent.PersistDecisionAndPrepareHandoff(decision, state.CandidateId, state.Snapshot!, incomplete)]);
    }

    private static EndpointTransition Handoff(EndpointState state, SequencedEndpointFact.HandoffStarted fact)
    {
        var next = state.Next(fact.Sequence, EndpointCandidateStage.HandoffPending, state.FamilyId, state.CandidateId,
            state.EvaluationRevision, state.PlanRevision, state.Snapshot, state.Assessment, state.DecisionId, fact.Operation);
        return new(next, new EndpointDisposition.ContinueListening(), []);
    }

    private static EndpointTransition Reconcile(EndpointState state, SequencedEndpointFact.HandoffOutcomeUnknown fact)
    {
        if (fact.Operation != state.HandoffOperation)
            return Same(state, new EndpointDisposition.Reject(new BoundedAscii("handoff-operation-conflict")));
        var next = state.Next(fact.Sequence, EndpointCandidateStage.Reconciling, state.FamilyId, state.CandidateId,
            state.EvaluationRevision, state.PlanRevision, state.Snapshot, state.Assessment, state.DecisionId, state.HandoffOperation);
        return new(next, new EndpointDisposition.AwaitEvidence(new BoundedAscii("handoff-reconciliation"), ulong.MaxValue),
            [new EndpointIntent.ReconcileHandoff(fact.Operation)]);
    }

    private static EndpointTransition Withdraw(EndpointState state, SequencedEndpointFact.WithdrawRequested fact)
    {
        if (fact.Operation != state.HandoffOperation)
            return Same(state, new EndpointDisposition.Reject(new BoundedAscii("handoff-operation-conflict")));
        var next = state.Next(fact.Sequence, EndpointCandidateStage.WithdrawPending, state.FamilyId, state.CandidateId,
            state.EvaluationRevision, state.PlanRevision, state.Snapshot, state.Assessment, state.DecisionId, state.HandoffOperation);
        return new(next, new EndpointDisposition.AwaitEvidence(new BoundedAscii("withdrawal-result"), ulong.MaxValue),
            [new EndpointIntent.WithdrawHandoff(fact.Operation)]);
    }

    private static EndpointTransition HandoffTerminal(EndpointState state, ulong sequence,
        OperationId operation, EndpointCandidateStage stage, string reason)
    {
        if (operation != state.HandoffOperation)
            return Same(state, new EndpointDisposition.Reject(new BoundedAscii("handoff-operation-conflict")));
        var next = state.Next(sequence, stage, state.FamilyId, state.CandidateId, state.EvaluationRevision,
            state.PlanRevision, state.Snapshot, state.Assessment, state.DecisionId, state.HandoffOperation);
        EndpointDisposition disposition = stage == EndpointCandidateStage.Accepted
            ? new EndpointDisposition.ContinueListening()
            : stage == EndpointCandidateStage.Withdrawn
                ? new EndpointDisposition.Cancel(new BoundedAscii(reason))
                : new EndpointDisposition.Reject(new BoundedAscii(reason));
        return new(next, disposition, []);
    }

    private static EndpointTransition Unavailable(EndpointState state, ulong sequence, BoundedAscii reason)
    {
        var next = state.Next(sequence, EndpointCandidateStage.Unavailable, state.FamilyId, state.CandidateId,
            state.EvaluationRevision, state.PlanRevision, state.Snapshot, state.Assessment, state.DecisionId, state.HandoffOperation);
        return new(next, new EndpointDisposition.Unavailable(reason), []);
    }

    private static EndpointTransition Same(EndpointState state, EndpointDisposition disposition) => new(state, disposition, []);
    private static bool Active(EndpointCandidateStage stage) => stage is EndpointCandidateStage.Open or EndpointCandidateStage.Assessing;
    private static bool Terminal(EndpointCandidateStage stage) => stage is EndpointCandidateStage.Accepted or EndpointCandidateStage.Rejected or
        EndpointCandidateStage.Cancelled or EndpointCandidateStage.Unavailable or EndpointCandidateStage.Withdrawn or EndpointCandidateStage.RejectedKnown;
}
