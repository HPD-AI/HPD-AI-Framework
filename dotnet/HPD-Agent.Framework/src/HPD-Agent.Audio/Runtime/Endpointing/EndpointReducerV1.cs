using HPD.Agent.Authority;
using HPD.Agent.Audio.Endpointing;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HPD.Agent.Audio.Runtime.Endpointing;

internal readonly record struct CandidateFamilyIdV1
{
    private CandidateFamilyIdV1(StableId128 value) => Value = value;
    internal StableId128 Value { get; }
    internal bool IsValid => !Value.Equals(default);
    internal static CandidateFamilyIdV1 Create() => new(StableId128.CreateRandom());
}

internal readonly record struct EndpointCandidateIdV1
{
    private EndpointCandidateIdV1(StableId128 value) => Value = value;
    internal StableId128 Value { get; }
    internal bool IsValid => !Value.Equals(default);
    internal static EndpointCandidateIdV1 Create() => new(StableId128.CreateRandom());
}

internal readonly record struct EndpointDecisionIdV1
{
    private EndpointDecisionIdV1(StableId128 value) => Value = value;
    internal StableId128 Value { get; }
    internal bool IsValid => !Value.Equals(default);
    internal static EndpointDecisionIdV1 Derive(EndpointCandidateIdV1 candidate, uint planRevision)
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

internal enum EndpointCandidateStageV1 : ushort
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

internal sealed record CompiledEndpointPolicyV1
{
    internal CompiledEndpointPolicyV1(bool allowExplicitIncompleteManualCommit,
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

internal sealed record EndpointStateV1
{
    internal EndpointStateV1(ExpectedAuthorityVectorV1 authority)
    {
        Authority = authority ?? throw new ArgumentNullException(nameof(authority));
        Stage = EndpointCandidateStageV1.None;
    }

    private EndpointStateV1(ExpectedAuthorityVectorV1 authority, ulong lastSequence,
        EndpointCandidateStageV1 stage, CandidateFamilyIdV1 familyId, EndpointCandidateIdV1 candidateId,
        uint evaluationRevision, uint planRevision, TranscriptTrackSnapshotV1? snapshot,
        SemanticAssessmentV1? assessment, EndpointDecisionIdV1 decisionId, OperationId handoffOperation,
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
    internal EndpointCandidateStageV1 Stage { get; }
    internal CandidateFamilyIdV1 FamilyId { get; }
    internal EndpointCandidateIdV1 CandidateId { get; }
    internal uint EvaluationRevision { get; }
    internal uint PlanRevision { get; }
    internal TranscriptTrackSnapshotV1? Snapshot { get; }
    internal SemanticAssessmentV1? Assessment { get; }
    internal EndpointDecisionIdV1 DecisionId { get; }
    internal OperationId HandoffOperation { get; }
    internal uint AppliedFactCount { get; }

    internal EndpointStateV1 Next(ulong sequence, EndpointCandidateStageV1 stage,
        CandidateFamilyIdV1 familyId, EndpointCandidateIdV1 candidateId, uint evaluationRevision,
        uint planRevision, TranscriptTrackSnapshotV1? snapshot, SemanticAssessmentV1? assessment,
        EndpointDecisionIdV1 decisionId, OperationId handoffOperation) =>
        new(Authority, sequence, stage, familyId, candidateId, evaluationRevision, planRevision,
            snapshot, assessment, decisionId, handoffOperation, checked(AppliedFactCount + 1));
}

internal abstract record SequencedEndpointFactV1
{
    private protected SequencedEndpointFactV1(ulong sequence, ExpectedAuthorityVectorV1 authority)
    {
        if (sequence == 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        Sequence = sequence;
        Authority = authority ?? throw new ArgumentNullException(nameof(authority));
    }
    internal ulong Sequence { get; }
    internal ExpectedAuthorityVectorV1 Authority { get; }

    internal sealed record CandidateOpened : SequencedEndpointFactV1
    {
        internal CandidateOpened(ulong q, ExpectedAuthorityVectorV1 a, CandidateFamilyIdV1 family,
            EndpointCandidateIdV1 candidate, TranscriptTrackSnapshotV1 snapshot) : base(q, a)
        {
            if (!family.IsValid || !candidate.IsValid) throw new ArgumentException("Candidate identities are required.");
            Family = family; Candidate = candidate; Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }
        internal CandidateFamilyIdV1 Family { get; }
        internal EndpointCandidateIdV1 Candidate { get; }
        internal TranscriptTrackSnapshotV1 Snapshot { get; }
    }
    internal sealed record TranscriptAdvanced(ulong Q, ExpectedAuthorityVectorV1 A, TranscriptTrackSnapshotV1 Snapshot) : SequencedEndpointFactV1(Q, A);
    internal sealed record SemanticAssessed(ulong Q, ExpectedAuthorityVectorV1 A, SemanticAssessmentV1 Assessment) : SequencedEndpointFactV1(Q, A);
    internal sealed record ManualCommitRequested(ulong Q, ExpectedAuthorityVectorV1 A, BoundedAscii Reason) : SequencedEndpointFactV1(Q, A);
    internal sealed record EvidenceDeadlineExpired(ulong Q, ExpectedAuthorityVectorV1 A) : SequencedEndpointFactV1(Q, A);
    internal sealed record ContinuityLost(ulong Q, ExpectedAuthorityVectorV1 A, BoundedAscii Reason) : SequencedEndpointFactV1(Q, A);
    internal sealed record HandoffStarted : SequencedEndpointFactV1
    {
        internal HandoffStarted(ulong q, ExpectedAuthorityVectorV1 a, OperationId operation) : base(q, a)
        { if (!operation.IsValid) throw new ArgumentException("A handoff operation is required.", nameof(operation)); Operation = operation; }
        internal OperationId Operation { get; }
    }
    internal sealed record HandoffAccepted(ulong Q, ExpectedAuthorityVectorV1 A, OperationId Operation) : SequencedEndpointFactV1(Q, A);
    internal sealed record HandoffRejectedKnown(ulong Q, ExpectedAuthorityVectorV1 A, OperationId Operation, BoundedAscii Reason) : SequencedEndpointFactV1(Q, A);
    internal sealed record HandoffOutcomeUnknown(ulong Q, ExpectedAuthorityVectorV1 A, OperationId Operation) : SequencedEndpointFactV1(Q, A);
    internal sealed record WithdrawRequested(ulong Q, ExpectedAuthorityVectorV1 A, OperationId Operation) : SequencedEndpointFactV1(Q, A);
    internal sealed record Withdrawn(ulong Q, ExpectedAuthorityVectorV1 A, OperationId Operation) : SequencedEndpointFactV1(Q, A);
}

internal abstract record EndpointDispositionV1
{
    private EndpointDispositionV1() { }
    internal sealed record ContinueListening : EndpointDispositionV1;
    internal sealed record AwaitEvidence(BoundedAscii Required, ulong DeadlineMonotonicNanoseconds) : EndpointDispositionV1;
    internal sealed record Reject(BoundedAscii Reason) : EndpointDispositionV1;
    internal sealed record CommitEligible(TranscriptTrackSnapshotV1 Snapshot, bool ExplicitIncomplete, BoundedAscii Reason) : EndpointDispositionV1;
    internal sealed record Cancel(BoundedAscii Reason) : EndpointDispositionV1;
    internal sealed record Unavailable(BoundedAscii Reason) : EndpointDispositionV1;
}

internal abstract record EndpointIntentV1
{
    private EndpointIntentV1() { }
    internal sealed record RequestSemanticAssessment(EndpointCandidateIdV1 Candidate, uint EvaluationRevision) : EndpointIntentV1;
    internal sealed record PersistDecisionAndPrepareHandoff(EndpointDecisionIdV1 Decision,
        EndpointCandidateIdV1 Candidate, TranscriptTrackSnapshotV1 Snapshot, bool ExplicitIncomplete) : EndpointIntentV1;
    internal sealed record ReconcileHandoff(OperationId Operation) : EndpointIntentV1;
    internal sealed record WithdrawHandoff(OperationId Operation) : EndpointIntentV1;
}

internal sealed record EndpointTransitionV1(EndpointStateV1 NextState,
    EndpointDispositionV1 Disposition, IReadOnlyList<EndpointIntentV1> Intents);

internal static class EndpointReducerV1
{
    internal static EndpointTransitionV1 Reduce(EndpointStateV1 state, SequencedEndpointFactV1 fact,
        CompiledEndpointPolicyV1 policy, ulong monotonicNowNanoseconds)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentNullException.ThrowIfNull(policy);
        if (fact.Authority != state.Authority)
            return Same(state, new EndpointDispositionV1.Unavailable(new BoundedAscii("authority-stale")));
        if (fact.Sequence != checked(state.LastSequence + 1))
            return Same(state, new EndpointDispositionV1.Unavailable(new BoundedAscii("session-sequence-invalid")));
        if (Terminal(state.Stage))
            return Same(state, new EndpointDispositionV1.Reject(new BoundedAscii("candidate-terminal")));
        if (state.AppliedFactCount >= policy.MaximumSequencedFacts)
            return Same(state, new EndpointDispositionV1.Unavailable(new BoundedAscii("fact-capacity-refused")));

        return fact switch
        {
            SequencedEndpointFactV1.CandidateOpened opened when state.Stage == EndpointCandidateStageV1.None =>
                Open(state, opened, policy),
            SequencedEndpointFactV1.TranscriptAdvanced advanced when Active(state.Stage) =>
                Transcript(state, advanced, policy),
            SequencedEndpointFactV1.SemanticAssessed assessed when state.Stage is EndpointCandidateStageV1.Open or EndpointCandidateStageV1.Assessing =>
                Assess(state, assessed, policy),
            SequencedEndpointFactV1.ManualCommitRequested manual when state.Stage is EndpointCandidateStageV1.Open or EndpointCandidateStageV1.Assessing =>
                Manual(state, manual, policy),
            SequencedEndpointFactV1.EvidenceDeadlineExpired when state.Stage is EndpointCandidateStageV1.Open or EndpointCandidateStageV1.Assessing =>
                Deadline(state, fact, monotonicNowNanoseconds, policy),
            SequencedEndpointFactV1.ContinuityLost lost when Active(state.Stage) =>
                Unavailable(state, lost.Sequence, lost.Reason),
            SequencedEndpointFactV1.HandoffStarted started when state.Stage == EndpointCandidateStageV1.DecisionPrepared =>
                Handoff(state, started),
            SequencedEndpointFactV1.HandoffAccepted accepted when state.Stage is EndpointCandidateStageV1.HandoffPending or EndpointCandidateStageV1.Reconciling =>
                HandoffTerminal(state, accepted.Sequence, accepted.Operation, EndpointCandidateStageV1.Accepted, "accepted"),
            SequencedEndpointFactV1.HandoffRejectedKnown rejected when state.Stage is EndpointCandidateStageV1.HandoffPending or EndpointCandidateStageV1.Reconciling =>
                HandoffTerminal(state, rejected.Sequence, rejected.Operation, EndpointCandidateStageV1.RejectedKnown, rejected.Reason.ToString()),
            SequencedEndpointFactV1.HandoffOutcomeUnknown unknown when state.Stage == EndpointCandidateStageV1.HandoffPending =>
                Reconcile(state, unknown),
            SequencedEndpointFactV1.WithdrawRequested withdraw when state.Stage == EndpointCandidateStageV1.HandoffPending =>
                Withdraw(state, withdraw),
            SequencedEndpointFactV1.Withdrawn withdrawn when state.Stage == EndpointCandidateStageV1.WithdrawPending =>
                HandoffTerminal(state, withdrawn.Sequence, withdrawn.Operation, EndpointCandidateStageV1.Withdrawn, "withdrawn"),
            _ => Same(state, new EndpointDispositionV1.Reject(new BoundedAscii("transition-invalid"))),
        };
    }

    private static EndpointTransitionV1 Open(EndpointStateV1 state, SequencedEndpointFactV1.CandidateOpened fact,
        CompiledEndpointPolicyV1 policy)
    {
        if (fact.Snapshot.Authority != state.Authority || fact.Snapshot.ContinuityLost)
            return Same(state, new EndpointDispositionV1.Unavailable(new BoundedAscii("snapshot-ineligible")));
        var next = state.Next(fact.Sequence, EndpointCandidateStageV1.Assessing, fact.Family, fact.Candidate,
            1, 1, fact.Snapshot, null, default, default);
        return new(next, new EndpointDispositionV1.AwaitEvidence(new BoundedAscii("semantic-assessment"),
            policy.EvidenceDeadlineMonotonicNanoseconds), [new EndpointIntentV1.RequestSemanticAssessment(fact.Candidate, 1)]);
    }

    private static EndpointTransitionV1 Transcript(EndpointStateV1 state, SequencedEndpointFactV1.TranscriptAdvanced fact,
        CompiledEndpointPolicyV1 policy)
    {
        if (fact.Snapshot.SourceId != state.Snapshot!.SourceId || fact.Snapshot.HypothesisId != state.Snapshot.HypothesisId ||
            fact.Snapshot.Revision.Value <= state.Snapshot.Revision.Value || fact.Snapshot.Authority != state.Authority || fact.Snapshot.ContinuityLost)
            return Same(state, new EndpointDispositionV1.Unavailable(new BoundedAscii("snapshot-advance-invalid")));
        var evaluation = checked(state.EvaluationRevision + 1);
        var next = state.Next(fact.Sequence, EndpointCandidateStageV1.Assessing, state.FamilyId, state.CandidateId,
            evaluation, checked(state.PlanRevision + 1), fact.Snapshot, null, default, default);
        return new(next, new EndpointDispositionV1.AwaitEvidence(new BoundedAscii("semantic-assessment"),
            policy.EvidenceDeadlineMonotonicNanoseconds), [new EndpointIntentV1.RequestSemanticAssessment(state.CandidateId, evaluation)]);
    }

    private static EndpointTransitionV1 Assess(EndpointStateV1 state, SequencedEndpointFactV1.SemanticAssessed fact,
        CompiledEndpointPolicyV1 policy)
    {
        if (state.Snapshot is null || state.Snapshot.ContinuityLost || string.IsNullOrEmpty(state.Snapshot.Text))
            return Unavailable(state, fact.Sequence, new BoundedAscii("snapshot-ineligible"));
        if (fact.Assessment.Completion != SemanticCompletionV1.CompleteCandidate)
        {
            var next = state.Next(fact.Sequence, EndpointCandidateStageV1.Open, state.FamilyId, state.CandidateId,
                state.EvaluationRevision, state.PlanRevision, state.Snapshot, fact.Assessment, default, default);
            return new(next, new EndpointDispositionV1.AwaitEvidence(new BoundedAscii("completion-evidence"),
                policy.EvidenceDeadlineMonotonicNanoseconds), []);
        }
        return Prepare(state, fact.Sequence, fact.Assessment, false, "semantic-complete");
    }

    private static EndpointTransitionV1 Manual(EndpointStateV1 state, SequencedEndpointFactV1.ManualCommitRequested fact,
        CompiledEndpointPolicyV1 policy)
    {
        if (!fact.Reason.IsValid) return Same(state, new EndpointDispositionV1.Reject(new BoundedAscii("manual-reason-invalid")));
        if (!policy.AllowExplicitIncompleteManualCommit)
            return Same(state, new EndpointDispositionV1.Reject(new BoundedAscii("manual-incomplete-disabled")));
        if (state.Snapshot is null || state.Snapshot.ContinuityLost || string.IsNullOrEmpty(state.Snapshot.Text))
            return Same(state, new EndpointDispositionV1.Reject(new BoundedAscii("manual-snapshot-ineligible")));
        return Prepare(state, fact.Sequence, state.Assessment, true, fact.Reason.ToString());
    }

    private static EndpointTransitionV1 Deadline(EndpointStateV1 state, SequencedEndpointFactV1 fact,
        ulong now, CompiledEndpointPolicyV1 policy) => now < policy.EvidenceDeadlineMonotonicNanoseconds
        ? Same(state, new EndpointDispositionV1.Reject(new BoundedAscii("deadline-not-due")))
        : Unavailable(state, fact.Sequence, new BoundedAscii("evidence-deadline-expired"));

    private static EndpointTransitionV1 Prepare(EndpointStateV1 state, ulong sequence,
        SemanticAssessmentV1? assessment, bool incomplete, string reason)
    {
        var nextPlanRevision = checked(state.PlanRevision + 1);
        var decision = EndpointDecisionIdV1.Derive(state.CandidateId, nextPlanRevision);
        var next = state.Next(sequence, EndpointCandidateStageV1.DecisionPrepared, state.FamilyId, state.CandidateId,
            state.EvaluationRevision, nextPlanRevision, state.Snapshot, assessment, decision, default);
        return new(next, new EndpointDispositionV1.CommitEligible(state.Snapshot!, incomplete, new BoundedAscii(reason)),
            [new EndpointIntentV1.PersistDecisionAndPrepareHandoff(decision, state.CandidateId, state.Snapshot!, incomplete)]);
    }

    private static EndpointTransitionV1 Handoff(EndpointStateV1 state, SequencedEndpointFactV1.HandoffStarted fact)
    {
        var next = state.Next(fact.Sequence, EndpointCandidateStageV1.HandoffPending, state.FamilyId, state.CandidateId,
            state.EvaluationRevision, state.PlanRevision, state.Snapshot, state.Assessment, state.DecisionId, fact.Operation);
        return new(next, new EndpointDispositionV1.ContinueListening(), []);
    }

    private static EndpointTransitionV1 Reconcile(EndpointStateV1 state, SequencedEndpointFactV1.HandoffOutcomeUnknown fact)
    {
        if (fact.Operation != state.HandoffOperation)
            return Same(state, new EndpointDispositionV1.Reject(new BoundedAscii("handoff-operation-conflict")));
        var next = state.Next(fact.Sequence, EndpointCandidateStageV1.Reconciling, state.FamilyId, state.CandidateId,
            state.EvaluationRevision, state.PlanRevision, state.Snapshot, state.Assessment, state.DecisionId, state.HandoffOperation);
        return new(next, new EndpointDispositionV1.AwaitEvidence(new BoundedAscii("handoff-reconciliation"), ulong.MaxValue),
            [new EndpointIntentV1.ReconcileHandoff(fact.Operation)]);
    }

    private static EndpointTransitionV1 Withdraw(EndpointStateV1 state, SequencedEndpointFactV1.WithdrawRequested fact)
    {
        if (fact.Operation != state.HandoffOperation)
            return Same(state, new EndpointDispositionV1.Reject(new BoundedAscii("handoff-operation-conflict")));
        var next = state.Next(fact.Sequence, EndpointCandidateStageV1.WithdrawPending, state.FamilyId, state.CandidateId,
            state.EvaluationRevision, state.PlanRevision, state.Snapshot, state.Assessment, state.DecisionId, state.HandoffOperation);
        return new(next, new EndpointDispositionV1.AwaitEvidence(new BoundedAscii("withdrawal-result"), ulong.MaxValue),
            [new EndpointIntentV1.WithdrawHandoff(fact.Operation)]);
    }

    private static EndpointTransitionV1 HandoffTerminal(EndpointStateV1 state, ulong sequence,
        OperationId operation, EndpointCandidateStageV1 stage, string reason)
    {
        if (operation != state.HandoffOperation)
            return Same(state, new EndpointDispositionV1.Reject(new BoundedAscii("handoff-operation-conflict")));
        var next = state.Next(sequence, stage, state.FamilyId, state.CandidateId, state.EvaluationRevision,
            state.PlanRevision, state.Snapshot, state.Assessment, state.DecisionId, state.HandoffOperation);
        EndpointDispositionV1 disposition = stage == EndpointCandidateStageV1.Accepted
            ? new EndpointDispositionV1.ContinueListening()
            : stage == EndpointCandidateStageV1.Withdrawn
                ? new EndpointDispositionV1.Cancel(new BoundedAscii(reason))
                : new EndpointDispositionV1.Reject(new BoundedAscii(reason));
        return new(next, disposition, []);
    }

    private static EndpointTransitionV1 Unavailable(EndpointStateV1 state, ulong sequence, BoundedAscii reason)
    {
        var next = state.Next(sequence, EndpointCandidateStageV1.Unavailable, state.FamilyId, state.CandidateId,
            state.EvaluationRevision, state.PlanRevision, state.Snapshot, state.Assessment, state.DecisionId, state.HandoffOperation);
        return new(next, new EndpointDispositionV1.Unavailable(reason), []);
    }

    private static EndpointTransitionV1 Same(EndpointStateV1 state, EndpointDispositionV1 disposition) => new(state, disposition, []);
    private static bool Active(EndpointCandidateStageV1 stage) => stage is EndpointCandidateStageV1.Open or EndpointCandidateStageV1.Assessing;
    private static bool Terminal(EndpointCandidateStageV1 stage) => stage is EndpointCandidateStageV1.Accepted or EndpointCandidateStageV1.Rejected or
        EndpointCandidateStageV1.Cancelled or EndpointCandidateStageV1.Unavailable or EndpointCandidateStageV1.Withdrawn or EndpointCandidateStageV1.RejectedKnown;
}
