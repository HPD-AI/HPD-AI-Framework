using System.Collections.ObjectModel;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.VoiceActivity;

internal enum VoiceActivityOpenExtentDispositionV1 : byte
{
    ContinueWithContinuityProof = 1,
    CloseByValidEvidence = 2,
    MarkDiscontinuousOpen = 3,
    TransferCandidate = 4,
}

internal enum VoiceActivityLifecycleStateV1 : byte
{
    Active = 1,
    ReplacementPrepared = 2,
    SettlingPredecessor = 3,
    Quarantined = 4,
    Completed = 5,
}

internal enum VoiceActivityReleaseDispositionV1 : byte
{
    Confirmed = 1,
    Failed = 2,
    ReleaseUnconfirmed = 3,
    Abandoned = 4,
}

internal sealed record VoiceActivityReplacementProofV1
{
    internal VoiceActivityReplacementProofV1(
        OperationId operationId,
        VoiceActivityEffectivePlanV1 candidatePlan,
        MonotonicStampV1 observedAt,
        MonotonicStampV1 deadline,
        VoiceActivityOpenExtentDispositionV1 openExtentDisposition,
        bool targetReady,
        bool conditionalGrantsReady,
        Hash256? continuityProof,
        Hash256? transferReceipt,
        bool validCloseEvidence)
    {
        if (!operationId.IsValid) throw new ArgumentException("An operation is required.", nameof(operationId));
        CandidatePlan = candidatePlan ?? throw new ArgumentNullException(nameof(candidatePlan));
        if (!observedAt.IsValid || deadline.CompareTo(observedAt) != ClockComparison.Later)
            throw new ArgumentException("Replacement preparation requires a comparable future deadline.");
        if (!Enum.IsDefined(openExtentDisposition)) throw new ArgumentOutOfRangeException(nameof(openExtentDisposition));
        OperationId = operationId;
        ObservedAt = observedAt;
        Deadline = deadline;
        OpenExtentDisposition = openExtentDisposition;
        TargetReady = targetReady;
        ConditionalGrantsReady = conditionalGrantsReady;
        ContinuityProof = continuityProof;
        TransferReceipt = transferReceipt;
        ValidCloseEvidence = validCloseEvidence;
    }

    internal OperationId OperationId { get; }
    internal VoiceActivityEffectivePlanV1 CandidatePlan { get; }
    internal MonotonicStampV1 ObservedAt { get; }
    internal MonotonicStampV1 Deadline { get; }
    internal VoiceActivityOpenExtentDispositionV1 OpenExtentDisposition { get; }
    internal bool TargetReady { get; }
    internal bool ConditionalGrantsReady { get; }
    internal Hash256? ContinuityProof { get; }
    internal Hash256? TransferReceipt { get; }
    internal bool ValidCloseEvidence { get; }
}

internal sealed record VoiceActivityLifecycleSnapshotV1
{
    private readonly KeyValuePair<string, ulong>[] _sourceGenerations;

    internal VoiceActivityLifecycleSnapshotV1(
        SessionAuthorityStampV1 session,
        GraphDirectionV1 direction,
        ulong lifecycleRevision,
        VoiceActivityEffectivePlanV1 plan,
        IReadOnlyDictionary<string, ulong> sourceGenerations,
        VoiceActivityLifecycleStateV1 state,
        OperationId? pendingOperation,
        VoiceActivityOpenExtentDispositionV1? lastOpenExtentDisposition,
        VoiceActivityReleaseDispositionV1? predecessorRelease,
        string? safeCode)
    {
        if (!session.IsValid || !Enum.IsDefined(direction) || lifecycleRevision == 0)
            throw new ArgumentException("Lifecycle authority is invalid.");
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        ArgumentNullException.ThrowIfNull(sourceGenerations);
        _sourceGenerations = sourceGenerations.OrderBy(static row => row.Key, StringComparer.Ordinal).ToArray();
        if (_sourceGenerations.Length != plan.PromotionAuthority.SourceKeys.Count ||
            _sourceGenerations.Any(static row => row.Value == 0) || plan.PromotionAuthority.SourceKeys.Any(key =>
                !_sourceGenerations.Any(row => row.Key == key)))
            throw new ArgumentException("Snapshot source generations must exactly match promotion authority.");
        Session = session;
        Direction = direction;
        LifecycleRevision = lifecycleRevision;
        State = state;
        PendingOperation = pendingOperation;
        LastOpenExtentDisposition = lastOpenExtentDisposition;
        PredecessorRelease = predecessorRelease;
        SafeCode = safeCode is null ? null : ActivitySourceRequestV1.RequireAscii(safeCode, nameof(safeCode));
        SourceGenerations = new ReadOnlyDictionary<string, ulong>(
            _sourceGenerations.ToDictionary(static row => row.Key, static row => row.Value, StringComparer.Ordinal));
    }

    internal SessionAuthorityStampV1 Session { get; }
    internal GraphDirectionV1 Direction { get; }
    internal ulong LifecycleRevision { get; }
    internal VoiceActivityEffectivePlanV1 Plan { get; }
    internal IReadOnlyDictionary<string, ulong> SourceGenerations { get; }
    internal VoiceActivityLifecycleStateV1 State { get; }
    internal OperationId? PendingOperation { get; }
    internal VoiceActivityOpenExtentDispositionV1? LastOpenExtentDisposition { get; }
    internal VoiceActivityReleaseDispositionV1? PredecessorRelease { get; }
    internal string? SafeCode { get; }
}

internal abstract record VoiceActivityPrepareReplacementResultV1
{
    private VoiceActivityPrepareReplacementResultV1() { }
    internal sealed record Prepared(VoiceActivityLifecycleSnapshotV1 Snapshot) : VoiceActivityPrepareReplacementResultV1;
    internal sealed record Duplicate(VoiceActivityLifecycleSnapshotV1 Snapshot) : VoiceActivityPrepareReplacementResultV1;
    internal sealed record Stale(VoiceActivityLifecycleSnapshotV1 Snapshot) : VoiceActivityPrepareReplacementResultV1;
    internal sealed record Rejected(VoiceActivityLifecycleSnapshotV1 Snapshot, string SafeCode) : VoiceActivityPrepareReplacementResultV1;
}

internal abstract record VoiceActivityCommitReplacementResultV1
{
    private VoiceActivityCommitReplacementResultV1() { }
    internal sealed record Applied(VoiceActivityLifecycleSnapshotV1 Snapshot) : VoiceActivityCommitReplacementResultV1;
    internal sealed record Duplicate(VoiceActivityLifecycleSnapshotV1 Snapshot) : VoiceActivityCommitReplacementResultV1;
    internal sealed record Stale(VoiceActivityLifecycleSnapshotV1 Snapshot) : VoiceActivityCommitReplacementResultV1;
    internal sealed record Rejected(VoiceActivityLifecycleSnapshotV1 Snapshot, string SafeCode) : VoiceActivityCommitReplacementResultV1;
}

internal sealed class VoiceActivityLifecycleV1
{
    private readonly object _gate = new();
    private readonly SessionAuthorityStampV1 _session;
    private readonly GraphDirectionV1 _direction;
    private VoiceActivityEffectivePlanV1 _plan;
    private Dictionary<string, ulong> _sourceGenerations;
    private VoiceActivityLifecycleStateV1 _state = VoiceActivityLifecycleStateV1.Active;
    private ulong _revision = 1;
    private VoiceActivityReplacementProofV1? _prepared;
    private OperationId? _lastCommitted;
    private VoiceActivityOpenExtentDispositionV1? _lastOpenDisposition;
    private VoiceActivityReleaseDispositionV1? _predecessorRelease;
    private string? _safeCode;

    internal VoiceActivityLifecycleV1(SessionAuthorityStampV1 session, GraphDirectionV1 direction,
        VoiceActivityEffectivePlanV1 plan, IReadOnlyDictionary<string, ulong> sourceGenerations)
    {
        if (!session.IsValid) throw new ArgumentException("A live session authority is required.", nameof(session));
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        _session = session;
        _direction = direction;
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _sourceGenerations = sourceGenerations?.ToDictionary(static row => row.Key, static row => row.Value,
            StringComparer.Ordinal) ?? throw new ArgumentNullException(nameof(sourceGenerations));
        _ = Snapshot();
    }

    internal VoiceActivityLifecycleSnapshotV1 Current { get { lock (_gate) return Snapshot(); } }

    internal VoiceActivityPrepareReplacementResultV1 PrepareReplacement(
        ulong expectedLifecycleRevision,
        VoiceActivityReplacementProofV1 proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        lock (_gate)
        {
            if (_state == VoiceActivityLifecycleStateV1.Completed)
                return new VoiceActivityPrepareReplacementResultV1.Stale(Snapshot());
            if (_prepared is not null)
                return _prepared == proof
                    ? new VoiceActivityPrepareReplacementResultV1.Duplicate(Snapshot())
                    : RejectPrepare("replacement-already-prepared");
            if (expectedLifecycleRevision != _revision)
                return new VoiceActivityPrepareReplacementResultV1.Stale(Snapshot());
            if (_state != VoiceActivityLifecycleStateV1.Active)
                return RejectPrepare("lifecycle-state-invalid");
            if (proof.CandidatePlan.PlanGeneration != _plan.PlanGeneration + 1 ||
                proof.CandidatePlan.ConfigRevision <= _plan.ConfigRevision)
                return RejectPrepare("replacement-generation-invalid");
            if (!proof.TargetReady || !proof.ConditionalGrantsReady)
                return RejectPrepare("replacement-proof-incomplete");
            var policyError = ValidateOpenPolicy(proof);
            if (policyError is not null) return RejectPrepare(policyError);
            _prepared = proof;
            _state = VoiceActivityLifecycleStateV1.ReplacementPrepared;
            _revision++;
            return new VoiceActivityPrepareReplacementResultV1.Prepared(Snapshot());
        }
    }

    // This is the sole no-await authority cut. All preparation and proof validation precede it.
    internal VoiceActivityCommitReplacementResultV1 CommitReplacement(
        ulong expectedLifecycleRevision,
        OperationId operationId,
        MonotonicStampV1 committedAt)
    {
        if (!operationId.IsValid || !committedAt.IsValid) throw new ArgumentException("Commit identity is invalid.");
        lock (_gate)
        {
            if (_lastCommitted == operationId)
                return new VoiceActivityCommitReplacementResultV1.Duplicate(Snapshot());
            if (_state == VoiceActivityLifecycleStateV1.Completed || expectedLifecycleRevision != _revision)
                return new VoiceActivityCommitReplacementResultV1.Stale(Snapshot());
            if (_prepared is null || _state != VoiceActivityLifecycleStateV1.ReplacementPrepared ||
                _prepared.OperationId != operationId)
                return RejectCommit("replacement-grant-invalid");
            var deadlineRelation = committedAt.CompareTo(_prepared.Deadline);
            if (deadlineRelation is ClockComparison.Later or ClockComparison.Incomparable)
            {
                _state = VoiceActivityLifecycleStateV1.Quarantined;
                _safeCode = "replacement-deadline-expired";
                _prepared = null;
                _revision++;
                return new VoiceActivityCommitReplacementResultV1.Rejected(Snapshot(), _safeCode);
            }

            var oldPlan = _plan;
            var oldGenerations = _sourceGenerations;
            _plan = _prepared.CandidatePlan;
            _sourceGenerations = _plan.PromotionAuthority.SourceKeys.ToDictionary(static key => key, key =>
            {
                var prior = oldPlan.Sources.SingleOrDefault(source => source.Request.SourceKey == key);
                var next = _plan.Sources.Single(source => source.Request.SourceKey == key);
                return prior is not null && SourceEquivalent(prior, next) &&
                    oldGenerations.TryGetValue(key, out var generation)
                    ? generation
                    : checked(oldGenerations.GetValueOrDefault(key) + 1);
            }, StringComparer.Ordinal);
            _lastOpenDisposition = _prepared.OpenExtentDisposition;
            _lastCommitted = operationId;
            _prepared = null;
            _predecessorRelease = null;
            _safeCode = null;
            _state = VoiceActivityLifecycleStateV1.SettlingPredecessor;
            _revision++;
            return new VoiceActivityCommitReplacementResultV1.Applied(Snapshot());
        }
    }

    internal VoiceActivityLifecycleSnapshotV1 SettlePredecessor(
        OperationId operationId,
        VoiceActivityReleaseDispositionV1 disposition)
    {
        if (!operationId.IsValid || !Enum.IsDefined(disposition)) throw new ArgumentException("Settlement is invalid.");
        lock (_gate)
        {
            if (_lastCommitted != operationId || _state == VoiceActivityLifecycleStateV1.Completed)
                return Snapshot();
            if (_predecessorRelease.HasValue) return Snapshot();
            _predecessorRelease = disposition;
            _state = disposition is VoiceActivityReleaseDispositionV1.Failed or
                VoiceActivityReleaseDispositionV1.ReleaseUnconfirmed
                ? VoiceActivityLifecycleStateV1.Quarantined
                : VoiceActivityLifecycleStateV1.Active;
            _safeCode = _state == VoiceActivityLifecycleStateV1.Quarantined
                ? "predecessor-release-unconfirmed"
                : null;
            _revision++;
            return Snapshot();
        }
    }

    internal VoiceActivityLifecycleSnapshotV1 Reset(
        ulong expectedLifecycleRevision,
        string reason,
        MonotonicStampV1 observedAt)
    {
        _ = ActivitySourceRequestV1.RequireAscii(reason, nameof(reason));
        if (!observedAt.IsValid) throw new ArgumentException("Reset observation is invalid.", nameof(observedAt));
        lock (_gate)
        {
            if (_state == VoiceActivityLifecycleStateV1.Completed || expectedLifecycleRevision != _revision)
                return Snapshot();
            _sourceGenerations = _sourceGenerations.ToDictionary(static row => row.Key,
                static row => checked(row.Value + 1), StringComparer.Ordinal);
            _prepared = null;
            _lastOpenDisposition = VoiceActivityOpenExtentDispositionV1.MarkDiscontinuousOpen;
            _state = VoiceActivityLifecycleStateV1.Active;
            _safeCode = null;
            _revision++;
            return Snapshot();
        }
    }

    internal VoiceActivityLifecycleSnapshotV1 Complete(VoiceActivityReleaseDispositionV1 disposition)
    {
        if (!Enum.IsDefined(disposition)) throw new ArgumentOutOfRangeException(nameof(disposition));
        lock (_gate)
        {
            if (_state == VoiceActivityLifecycleStateV1.Completed) return Snapshot();
            _prepared = null;
            _predecessorRelease = disposition;
            _state = VoiceActivityLifecycleStateV1.Completed;
            _safeCode = disposition is VoiceActivityReleaseDispositionV1.Failed or
                VoiceActivityReleaseDispositionV1.ReleaseUnconfirmed ? "completion-release-unconfirmed" : null;
            _revision++;
            return Snapshot();
        }
    }

    internal bool ObserveLateRelease(OperationId operationId, VoiceActivityReleaseDispositionV1 disposition)
    {
        if (!operationId.IsValid || !Enum.IsDefined(disposition)) throw new ArgumentException("Release observation is invalid.");
        lock (_gate) return _state == VoiceActivityLifecycleStateV1.Completed;
    }

    private static string? ValidateOpenPolicy(VoiceActivityReplacementProofV1 proof) => proof.OpenExtentDisposition switch
    {
        VoiceActivityOpenExtentDispositionV1.ContinueWithContinuityProof when !proof.ContinuityProof.HasValue =>
            "continuity-proof-required",
        VoiceActivityOpenExtentDispositionV1.CloseByValidEvidence when !proof.ValidCloseEvidence =>
            "valid-close-evidence-required",
        VoiceActivityOpenExtentDispositionV1.TransferCandidate when !proof.ContinuityProof.HasValue ||
            !proof.TransferReceipt.HasValue => "transfer-proof-required",
        _ => null,
    };

    private static bool SourceEquivalent(VoiceActivityEffectiveSourcePlanV1 left,
        VoiceActivityEffectiveSourcePlanV1 right)
    {
        var l = left.Capabilities;
        var r = right.Capabilities;
        return left.Request == right.Request && left.EffectiveMaximumWindow == right.EffectiveMaximumWindow &&
            left.ProviderVisibility == right.ProviderVisibility && l.InputOwnership == r.InputOwnership &&
            l.Formats.SequenceEqual(r.Formats) && l.Window == r.Window && l.Measurement == r.Measurement &&
            l.StateModel == r.StateModel && l.Concurrency == r.Concurrency && l.DynamicUpdate == r.DynamicUpdate &&
            l.Reset == r.Reset && l.Transfer == r.Transfer && l.Replacement == r.Replacement &&
            l.SupportsCancellation == r.SupportsCancellation && l.SupportsWarmup == r.SupportsWarmup &&
            l.MaximumPendingOperations == r.MaximumPendingOperations;
    }

    private VoiceActivityPrepareReplacementResultV1.Rejected RejectPrepare(string code) =>
        new(Snapshot(), code);
    private VoiceActivityCommitReplacementResultV1.Rejected RejectCommit(string code) =>
        new(Snapshot(), code);
    private VoiceActivityLifecycleSnapshotV1 Snapshot() => new(_session, _direction, _revision, _plan,
        _sourceGenerations, _state, _prepared?.OperationId, _lastOpenDisposition, _predecessorRelease, _safeCode);
}
