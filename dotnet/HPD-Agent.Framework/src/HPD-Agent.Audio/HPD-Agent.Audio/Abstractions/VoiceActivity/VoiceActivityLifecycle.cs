using System.Collections.ObjectModel;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.VoiceActivity;

internal enum VoiceActivityOpenExtentDisposition : byte
{
    ContinueWithContinuityProof = 1,
    CloseByValidEvidence = 2,
    MarkDiscontinuousOpen = 3,
    TransferCandidate = 4,
}

internal enum VoiceActivityLifecycleState : byte
{
    Active = 1,
    ReplacementPrepared = 2,
    SettlingPredecessor = 3,
    Quarantined = 4,
    Completed = 5,
}

internal enum VoiceActivityReleaseDisposition : byte
{
    Confirmed = 1,
    Failed = 2,
    ReleaseUnconfirmed = 3,
    Abandoned = 4,
}

internal sealed record VoiceActivityReplacementProof
{
    internal VoiceActivityReplacementProof(
        OperationId operationId,
        VoiceActivityEffectivePlan candidatePlan,
        MonotonicStampV1 observedAt,
        MonotonicStampV1 deadline,
        VoiceActivityOpenExtentDisposition openExtentDisposition,
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
    internal VoiceActivityEffectivePlan CandidatePlan { get; }
    internal MonotonicStampV1 ObservedAt { get; }
    internal MonotonicStampV1 Deadline { get; }
    internal VoiceActivityOpenExtentDisposition OpenExtentDisposition { get; }
    internal bool TargetReady { get; }
    internal bool ConditionalGrantsReady { get; }
    internal Hash256? ContinuityProof { get; }
    internal Hash256? TransferReceipt { get; }
    internal bool ValidCloseEvidence { get; }
}

internal sealed record VoiceActivityLifecycleSnapshot
{
    private readonly KeyValuePair<string, ulong>[] _sourceGenerations;

    internal VoiceActivityLifecycleSnapshot(
        SessionAuthorityStampV1 session,
        GraphDirection direction,
        ulong lifecycleRevision,
        VoiceActivityEffectivePlan plan,
        IReadOnlyDictionary<string, ulong> sourceGenerations,
        VoiceActivityLifecycleState state,
        OperationId? pendingOperation,
        VoiceActivityOpenExtentDisposition? lastOpenExtentDisposition,
        VoiceActivityReleaseDisposition? predecessorRelease,
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
        SafeCode = safeCode is null ? null : ActivitySourceRequest.RequireAscii(safeCode, nameof(safeCode));
        SourceGenerations = new ReadOnlyDictionary<string, ulong>(
            _sourceGenerations.ToDictionary(static row => row.Key, static row => row.Value, StringComparer.Ordinal));
    }

    internal SessionAuthorityStampV1 Session { get; }
    internal GraphDirection Direction { get; }
    internal ulong LifecycleRevision { get; }
    internal VoiceActivityEffectivePlan Plan { get; }
    internal IReadOnlyDictionary<string, ulong> SourceGenerations { get; }
    internal VoiceActivityLifecycleState State { get; }
    internal OperationId? PendingOperation { get; }
    internal VoiceActivityOpenExtentDisposition? LastOpenExtentDisposition { get; }
    internal VoiceActivityReleaseDisposition? PredecessorRelease { get; }
    internal string? SafeCode { get; }
}

internal abstract record VoiceActivityPrepareReplacementResult
{
    private VoiceActivityPrepareReplacementResult() { }
    internal sealed record Prepared(VoiceActivityLifecycleSnapshot Snapshot) : VoiceActivityPrepareReplacementResult;
    internal sealed record Duplicate(VoiceActivityLifecycleSnapshot Snapshot) : VoiceActivityPrepareReplacementResult;
    internal sealed record Stale(VoiceActivityLifecycleSnapshot Snapshot) : VoiceActivityPrepareReplacementResult;
    internal sealed record Rejected(VoiceActivityLifecycleSnapshot Snapshot, string SafeCode) : VoiceActivityPrepareReplacementResult;
}

internal abstract record VoiceActivityCommitReplacementResult
{
    private VoiceActivityCommitReplacementResult() { }
    internal sealed record Applied(VoiceActivityLifecycleSnapshot Snapshot) : VoiceActivityCommitReplacementResult;
    internal sealed record Duplicate(VoiceActivityLifecycleSnapshot Snapshot) : VoiceActivityCommitReplacementResult;
    internal sealed record Stale(VoiceActivityLifecycleSnapshot Snapshot) : VoiceActivityCommitReplacementResult;
    internal sealed record Rejected(VoiceActivityLifecycleSnapshot Snapshot, string SafeCode) : VoiceActivityCommitReplacementResult;
}

internal sealed class VoiceActivityLifecycle
{
    private readonly object _gate = new();
    private readonly SessionAuthorityStampV1 _session;
    private readonly GraphDirection _direction;
    private VoiceActivityEffectivePlan _plan;
    private Dictionary<string, ulong> _sourceGenerations;
    private VoiceActivityLifecycleState _state = VoiceActivityLifecycleState.Active;
    private ulong _revision = 1;
    private VoiceActivityReplacementProof? _prepared;
    private OperationId? _lastCommitted;
    private VoiceActivityOpenExtentDisposition? _lastOpenDisposition;
    private VoiceActivityReleaseDisposition? _predecessorRelease;
    private string? _safeCode;

    internal VoiceActivityLifecycle(SessionAuthorityStampV1 session, GraphDirection direction,
        VoiceActivityEffectivePlan plan, IReadOnlyDictionary<string, ulong> sourceGenerations)
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

    internal VoiceActivityLifecycleSnapshot Current { get { lock (_gate) return Snapshot(); } }

    internal VoiceActivityPrepareReplacementResult PrepareReplacement(
        ulong expectedLifecycleRevision,
        VoiceActivityReplacementProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        lock (_gate)
        {
            if (_state == VoiceActivityLifecycleState.Completed)
                return new VoiceActivityPrepareReplacementResult.Stale(Snapshot());
            if (_prepared is not null)
                return _prepared == proof
                    ? new VoiceActivityPrepareReplacementResult.Duplicate(Snapshot())
                    : RejectPrepare("replacement-already-prepared");
            if (expectedLifecycleRevision != _revision)
                return new VoiceActivityPrepareReplacementResult.Stale(Snapshot());
            if (_state != VoiceActivityLifecycleState.Active)
                return RejectPrepare("lifecycle-state-invalid");
            if (proof.CandidatePlan.PlanGeneration != _plan.PlanGeneration + 1 ||
                proof.CandidatePlan.ConfigRevision <= _plan.ConfigRevision)
                return RejectPrepare("replacement-generation-invalid");
            if (!proof.TargetReady || !proof.ConditionalGrantsReady)
                return RejectPrepare("replacement-proof-incomplete");
            var policyError = ValidateOpenPolicy(proof);
            if (policyError is not null) return RejectPrepare(policyError);
            _prepared = proof;
            _state = VoiceActivityLifecycleState.ReplacementPrepared;
            _revision++;
            return new VoiceActivityPrepareReplacementResult.Prepared(Snapshot());
        }
    }

    // This is the sole no-await authority cut. All preparation and proof validation precede it.
    internal VoiceActivityCommitReplacementResult CommitReplacement(
        ulong expectedLifecycleRevision,
        OperationId operationId,
        MonotonicStampV1 committedAt)
    {
        if (!operationId.IsValid || !committedAt.IsValid) throw new ArgumentException("Commit identity is invalid.");
        lock (_gate)
        {
            if (_lastCommitted == operationId)
                return new VoiceActivityCommitReplacementResult.Duplicate(Snapshot());
            if (_state == VoiceActivityLifecycleState.Completed || expectedLifecycleRevision != _revision)
                return new VoiceActivityCommitReplacementResult.Stale(Snapshot());
            if (_prepared is null || _state != VoiceActivityLifecycleState.ReplacementPrepared ||
                _prepared.OperationId != operationId)
                return RejectCommit("replacement-grant-invalid");
            var deadlineRelation = committedAt.CompareTo(_prepared.Deadline);
            if (deadlineRelation is ClockComparison.Later or ClockComparison.Incomparable)
            {
                _state = VoiceActivityLifecycleState.Quarantined;
                _safeCode = "replacement-deadline-expired";
                _prepared = null;
                _revision++;
                return new VoiceActivityCommitReplacementResult.Rejected(Snapshot(), _safeCode);
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
            _state = VoiceActivityLifecycleState.SettlingPredecessor;
            _revision++;
            return new VoiceActivityCommitReplacementResult.Applied(Snapshot());
        }
    }

    internal VoiceActivityLifecycleSnapshot SettlePredecessor(
        OperationId operationId,
        VoiceActivityReleaseDisposition disposition)
    {
        if (!operationId.IsValid || !Enum.IsDefined(disposition)) throw new ArgumentException("Settlement is invalid.");
        lock (_gate)
        {
            if (_lastCommitted != operationId || _state == VoiceActivityLifecycleState.Completed)
                return Snapshot();
            if (_predecessorRelease.HasValue) return Snapshot();
            _predecessorRelease = disposition;
            _state = disposition is VoiceActivityReleaseDisposition.Failed or
                VoiceActivityReleaseDisposition.ReleaseUnconfirmed
                ? VoiceActivityLifecycleState.Quarantined
                : VoiceActivityLifecycleState.Active;
            _safeCode = _state == VoiceActivityLifecycleState.Quarantined
                ? "predecessor-release-unconfirmed"
                : null;
            _revision++;
            return Snapshot();
        }
    }

    internal VoiceActivityLifecycleSnapshot Reset(
        ulong expectedLifecycleRevision,
        string reason,
        MonotonicStampV1 observedAt)
    {
        _ = ActivitySourceRequest.RequireAscii(reason, nameof(reason));
        if (!observedAt.IsValid) throw new ArgumentException("Reset observation is invalid.", nameof(observedAt));
        lock (_gate)
        {
            if (_state == VoiceActivityLifecycleState.Completed || expectedLifecycleRevision != _revision)
                return Snapshot();
            _sourceGenerations = _sourceGenerations.ToDictionary(static row => row.Key,
                static row => checked(row.Value + 1), StringComparer.Ordinal);
            _prepared = null;
            _lastOpenDisposition = VoiceActivityOpenExtentDisposition.MarkDiscontinuousOpen;
            _state = VoiceActivityLifecycleState.Active;
            _safeCode = null;
            _revision++;
            return Snapshot();
        }
    }

    internal VoiceActivityLifecycleSnapshot? ApplyInPlaceUpdate(
        ulong expectedLifecycleRevision,
        VoiceActivityEffectivePlan candidatePlan)
    {
        ArgumentNullException.ThrowIfNull(candidatePlan);
        lock (_gate)
        {
            if (_state != VoiceActivityLifecycleState.Active || expectedLifecycleRevision != _revision ||
                candidatePlan.PlanGeneration != _plan.PlanGeneration + 1 ||
                candidatePlan.ConfigRevision <= _plan.ConfigRevision ||
                candidatePlan.PromotionAuthority.Mode != _plan.PromotionAuthority.Mode ||
                !candidatePlan.PromotionAuthority.SourceKeys.SequenceEqual(
                    _plan.PromotionAuthority.SourceKeys, StringComparer.Ordinal) ||
                candidatePlan.Sources.Count != _plan.Sources.Count ||
                candidatePlan.Sources.Any(candidate =>
                {
                    var prior = _plan.Sources.SingleOrDefault(source =>
                        source.Request.SourceKey == candidate.Request.SourceKey);
                    return prior is null || !SourceEquivalent(prior, candidate);
                }))
                return null;
            _plan = candidatePlan;
            _revision++;
            _safeCode = null;
            return Snapshot();
        }
    }

    internal VoiceActivityLifecycleSnapshot Complete(VoiceActivityReleaseDisposition disposition)
    {
        if (!Enum.IsDefined(disposition)) throw new ArgumentOutOfRangeException(nameof(disposition));
        lock (_gate)
        {
            if (_state == VoiceActivityLifecycleState.Completed) return Snapshot();
            _prepared = null;
            _predecessorRelease = disposition;
            _state = VoiceActivityLifecycleState.Completed;
            _safeCode = disposition is VoiceActivityReleaseDisposition.Failed or
                VoiceActivityReleaseDisposition.ReleaseUnconfirmed ? "completion-release-unconfirmed" : null;
            _revision++;
            return Snapshot();
        }
    }

    internal bool ObserveLateRelease(OperationId operationId, VoiceActivityReleaseDisposition disposition)
    {
        if (!operationId.IsValid || !Enum.IsDefined(disposition)) throw new ArgumentException("Release observation is invalid.");
        lock (_gate) return _state == VoiceActivityLifecycleState.Completed;
    }

    private static string? ValidateOpenPolicy(VoiceActivityReplacementProof proof) => proof.OpenExtentDisposition switch
    {
        VoiceActivityOpenExtentDisposition.ContinueWithContinuityProof when !proof.ContinuityProof.HasValue =>
            "continuity-proof-required",
        VoiceActivityOpenExtentDisposition.CloseByValidEvidence when !proof.ValidCloseEvidence =>
            "valid-close-evidence-required",
        VoiceActivityOpenExtentDisposition.TransferCandidate when !proof.ContinuityProof.HasValue ||
            !proof.TransferReceipt.HasValue => "transfer-proof-required",
        _ => null,
    };

    private static bool SourceEquivalent(VoiceActivityEffectiveSourcePlan left,
        VoiceActivityEffectiveSourcePlan right)
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

    private VoiceActivityPrepareReplacementResult.Rejected RejectPrepare(string code) =>
        new(Snapshot(), code);
    private VoiceActivityCommitReplacementResult.Rejected RejectCommit(string code) =>
        new(Snapshot(), code);
    private VoiceActivityLifecycleSnapshot Snapshot() => new(_session, _direction, _revision, _plan,
        _sourceGenerations, _state, _prepared?.OperationId, _lastOpenDisposition, _predecessorRelease, _safeCode);
}
