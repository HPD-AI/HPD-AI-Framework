using HPD.Agent.Runtime;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.VoiceActivity;

internal sealed record VoiceActivityConditionalReplacementGrantV1
{
    internal VoiceActivityConditionalReplacementGrantV1(
        SessionAuthorityStampV1 session,
        OperationId operationId,
        ulong expectedLifecycleRevision,
        BoundedAscii targetDescriptorId,
        ParticipantId targetParticipantId,
        MonotonicStampV1 deadline,
        Hash256 proofIdentity)
    {
        if (!session.IsValid || !operationId.IsValid || expectedLifecycleRevision == 0 ||
            !targetDescriptorId.IsValid || !targetParticipantId.IsValid || !deadline.IsValid ||
            proofIdentity.Equals(default(Hash256)))
            throw new ArgumentException("The conditional replacement grant is invalid.");
        Session = session;
        OperationId = operationId;
        ExpectedLifecycleRevision = expectedLifecycleRevision;
        TargetDescriptorId = targetDescriptorId;
        TargetParticipantId = targetParticipantId;
        Deadline = deadline;
        ProofIdentity = proofIdentity;
    }

    internal SessionAuthorityStampV1 Session { get; }
    internal OperationId OperationId { get; }
    internal ulong ExpectedLifecycleRevision { get; }
    internal BoundedAscii TargetDescriptorId { get; }
    internal ParticipantId TargetParticipantId { get; }
    internal MonotonicStampV1 Deadline { get; }
    internal Hash256 ProofIdentity { get; }
}

internal sealed record VoiceActivityParticipantReplacementRequestV1
{
    internal VoiceActivityParticipantReplacementRequestV1(
        VoiceActivityEffectivePlanV1 candidatePlan,
        VoiceActivityConditionalReplacementGrantV1 grant,
        MonotonicStampV1 observedAt,
        MonotonicStampV1 commitAt,
        VoiceActivityOpenExtentDispositionV1 openExtentDisposition,
        Hash256? continuityProof,
        Hash256? transferReceipt,
        bool validCloseEvidence)
    {
        CandidatePlan = candidatePlan ?? throw new ArgumentNullException(nameof(candidatePlan));
        Grant = grant ?? throw new ArgumentNullException(nameof(grant));
        if (!observedAt.IsValid || !commitAt.IsValid ||
            commitAt.CompareTo(observedAt) is ClockComparison.Earlier or ClockComparison.Incomparable)
            throw new ArgumentException("Replacement observation and commit time are invalid.");
        if (!Enum.IsDefined(openExtentDisposition)) throw new ArgumentOutOfRangeException(nameof(openExtentDisposition));
        ObservedAt = observedAt;
        CommitAt = commitAt;
        OpenExtentDisposition = openExtentDisposition;
        ContinuityProof = continuityProof;
        TransferReceipt = transferReceipt;
        ValidCloseEvidence = validCloseEvidence;
    }

    internal VoiceActivityEffectivePlanV1 CandidatePlan { get; }
    internal VoiceActivityConditionalReplacementGrantV1 Grant { get; }
    internal MonotonicStampV1 ObservedAt { get; }
    internal MonotonicStampV1 CommitAt { get; }
    internal VoiceActivityOpenExtentDispositionV1 OpenExtentDisposition { get; }
    internal Hash256? ContinuityProof { get; }
    internal Hash256? TransferReceipt { get; }
    internal bool ValidCloseEvidence { get; }
}

internal abstract record VoiceActivityParticipantReplacementResultV1
{
    private VoiceActivityParticipantReplacementResultV1() { }
    internal sealed record Applied(VoiceActivityLifecycleSnapshotV1 Snapshot,
        VoiceActivityReleaseDispositionV1 PredecessorRelease) : VoiceActivityParticipantReplacementResultV1;
    internal sealed record Duplicate(VoiceActivityLifecycleSnapshotV1 Snapshot) : VoiceActivityParticipantReplacementResultV1;
    internal sealed record Stale(VoiceActivityLifecycleSnapshotV1 Snapshot) : VoiceActivityParticipantReplacementResultV1;
    internal sealed record Rejected(VoiceActivityLifecycleSnapshotV1 Snapshot, string SafeCode) : VoiceActivityParticipantReplacementResultV1;
    internal sealed record Cancelled(VoiceActivityLifecycleSnapshotV1 Snapshot) : VoiceActivityParticipantReplacementResultV1;
}

internal sealed class VoiceActivityReplacementCoordinatorV1 : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly VoiceActivityLifecycleV1 _lifecycle;
    private IRuntimeParticipantV1 _active;
    private RuntimePreparedHandleV1 _activeHandle;
    private OperationId? _lastOperation;
    private VoiceActivityParticipantReplacementRequestV1? _lastRequest;
    private VoiceActivityParticipantReplacementResultV1? _lastResult;
    private bool _disposed;

    internal VoiceActivityReplacementCoordinatorV1(
        VoiceActivityLifecycleV1 lifecycle,
        IRuntimeParticipantV1 active,
        RuntimePreparedHandleV1 activeHandle)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _active = active ?? throw new ArgumentNullException(nameof(active));
        _activeHandle = activeHandle ?? throw new ArgumentNullException(nameof(activeHandle));
        if (_active.Descriptor.Id != _activeHandle.DescriptorId)
            throw new ArgumentException("The active participant handle does not match its descriptor.");
    }

    internal async ValueTask<VoiceActivityParticipantReplacementResultV1> ReplaceAsync(
        IRuntimeParticipantV1 candidate,
        RuntimeParticipantContextV1 candidateContext,
        VoiceActivityParticipantReplacementRequestV1 request,
        CancellationToken callerCancellation,
        CancellationToken convergenceCancellation)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(request);
        if (!candidateContext.IsValid) throw new ArgumentException("Candidate context is invalid.", nameof(candidateContext));
        if (callerCancellation.IsCancellationRequested)
            return new VoiceActivityParticipantReplacementResultV1.Cancelled(_lifecycle.Current);
        try { await _gate.WaitAsync(callerCancellation).ConfigureAwait(false); }
        catch (OperationCanceledException) when (callerCancellation.IsCancellationRequested)
        { return new VoiceActivityParticipantReplacementResultV1.Cancelled(_lifecycle.Current); }
        try
        {
            if (_disposed) return Reject("replacement-coordinator-closed");
            if (_lastOperation == request.Grant.OperationId)
                return _lastRequest == request && _lastResult is not null
                    ? new VoiceActivityParticipantReplacementResultV1.Duplicate(_lastResult switch
                    {
                        VoiceActivityParticipantReplacementResultV1.Applied applied => applied.Snapshot,
                        VoiceActivityParticipantReplacementResultV1.Rejected rejected => rejected.Snapshot,
                        VoiceActivityParticipantReplacementResultV1.Stale stale => stale.Snapshot,
                        VoiceActivityParticipantReplacementResultV1.Cancelled cancelled => cancelled.Snapshot,
                        VoiceActivityParticipantReplacementResultV1.Duplicate duplicate => duplicate.Snapshot,
                        _ => _lifecycle.Current,
                    })
                    : Reject("replacement-operation-contradiction");
            var grantError = ValidateGrant(candidate, candidateContext, request);
            if (grantError is not null) return Reject(grantError);
            if (request.Grant.ExpectedLifecycleRevision != _lifecycle.Current.LifecycleRevision)
                return new VoiceActivityParticipantReplacementResultV1.Stale(_lifecycle.Current);

            RuntimePreparedHandleV1? candidateHandle = null;
            var cutCommitted = false;
            try
            {
                var prepared = await candidate.PrepareAsync(candidateContext, callerCancellation).ConfigureAwait(false);
                if (prepared.Disposition != RuntimeParticipantDispositionV1.Succeeded || prepared.Handle is null)
                    return Reject($"candidate-prepare-{prepared.Disposition.ToString().ToLowerInvariant()}");
                candidateHandle = prepared.Handle;
                var started = await candidate.StartAsync(candidateHandle, callerCancellation).ConfigureAwait(false);
                if (started.Disposition != RuntimeParticipantDispositionV1.Succeeded)
                {
                    await TerminateCandidateAsync(candidate, convergenceCancellation).ConfigureAwait(false);
                    return Reject($"candidate-start-{started.Disposition.ToString().ToLowerInvariant()}");
                }
                if (callerCancellation.IsCancellationRequested)
                {
                    await TerminateCandidateAsync(candidate, convergenceCancellation).ConfigureAwait(false);
                    return new VoiceActivityParticipantReplacementResultV1.Cancelled(_lifecycle.Current);
                }

                var proof = new VoiceActivityReplacementProofV1(request.Grant.OperationId, request.CandidatePlan,
                    request.ObservedAt, request.Grant.Deadline, request.OpenExtentDisposition,
                    targetReady: true, conditionalGrantsReady: true, request.ContinuityProof,
                    request.TransferReceipt, request.ValidCloseEvidence);
                var prepareCut = _lifecycle.PrepareReplacement(request.Grant.ExpectedLifecycleRevision, proof);
                if (prepareCut is VoiceActivityPrepareReplacementResultV1.Stale)
                {
                    await TerminateCandidateAsync(candidate, convergenceCancellation).ConfigureAwait(false);
                    return new VoiceActivityParticipantReplacementResultV1.Stale(_lifecycle.Current);
                }
                if (prepareCut is VoiceActivityPrepareReplacementResultV1.Rejected prepareRejected)
                {
                    await TerminateCandidateAsync(candidate, convergenceCancellation).ConfigureAwait(false);
                    return Reject(prepareRejected.SafeCode);
                }

                // Sole synchronous authority change: no await may occur inside this call.
                var commit = _lifecycle.CommitReplacement(_lifecycle.Current.LifecycleRevision,
                    request.Grant.OperationId, request.CommitAt);
                if (commit is not VoiceActivityCommitReplacementResultV1.Applied applied)
                {
                    await TerminateCandidateAsync(candidate, convergenceCancellation).ConfigureAwait(false);
                    return commit switch
                    {
                        VoiceActivityCommitReplacementResultV1.Stale =>
                            new VoiceActivityParticipantReplacementResultV1.Stale(_lifecycle.Current),
                        VoiceActivityCommitReplacementResultV1.Rejected rejected => Reject(rejected.SafeCode),
                        _ => Reject("replacement-commit-invalid"),
                    };
                }
                cutCommitted = true;
                var predecessor = _active;
                _active = candidate;
                _activeHandle = candidateHandle;
                var release = await SettlePredecessorAsync(predecessor, convergenceCancellation).ConfigureAwait(false);
                var settled = _lifecycle.SettlePredecessor(request.Grant.OperationId, release);
                var result = new VoiceActivityParticipantReplacementResultV1.Applied(settled, release);
                _lastOperation = request.Grant.OperationId;
                _lastRequest = request;
                _lastResult = result;
                return result;
            }
            catch (OperationCanceledException) when (!cutCommitted && callerCancellation.IsCancellationRequested)
            {
                if (candidateHandle is not null)
                    await TerminateCandidateAsync(candidate, convergenceCancellation).ConfigureAwait(false);
                return new VoiceActivityParticipantReplacementResultV1.Cancelled(_lifecycle.Current);
            }
            catch when (!cutCommitted)
            {
                if (candidateHandle is not null)
                    await TerminateCandidateAsync(candidate, convergenceCancellation).ConfigureAwait(false);
                return Reject("replacement-effect-failed");
            }
            catch
            {
                var settled = _lifecycle.SettlePredecessor(request.Grant.OperationId,
                    VoiceActivityReleaseDispositionV1.ReleaseUnconfirmed);
                var result = new VoiceActivityParticipantReplacementResultV1.Applied(settled,
                    VoiceActivityReleaseDispositionV1.ReleaseUnconfirmed);
                _lastOperation = request.Grant.OperationId;
                _lastRequest = request;
                _lastResult = result;
                return result;
            }
        }
        finally { _gate.Release(); }
    }

    internal async ValueTask<VoiceActivityLifecycleSnapshotV1> CompleteAsync(
        VoiceActivityReleaseDispositionV1 requestedDisposition,
        CancellationToken convergenceCancellation)
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_lifecycle.Current.State == VoiceActivityLifecycleStateV1.Completed) return _lifecycle.Current;
            var actual = await SettlePredecessorAsync(_active, convergenceCancellation).ConfigureAwait(false);
            var disposition = actual == VoiceActivityReleaseDispositionV1.Confirmed
                ? requestedDisposition
                : actual;
            return _lifecycle.Complete(disposition);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            await _active.DisposeAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private string? ValidateGrant(IRuntimeParticipantV1 candidate, RuntimeParticipantContextV1 context,
        VoiceActivityParticipantReplacementRequestV1 request)
    {
        var grant = request.Grant;
        if (grant.Session != _lifecycle.Current.Session || context.Authority.Session != grant.Session)
            return "replacement-session-stale";
        if (candidate.Descriptor.Id != grant.TargetDescriptorId || context.ParticipantId != grant.TargetParticipantId)
            return "replacement-target-invalid";
        if (request.CommitAt.CompareTo(grant.Deadline) == ClockComparison.Incomparable)
            return "replacement-deadline-incomparable";
        return null;
    }

    private static async ValueTask<VoiceActivityReleaseDispositionV1> SettlePredecessorAsync(
        IRuntimeParticipantV1 participant, CancellationToken convergenceCancellation)
    {
        try
        {
            var drain = await participant.DrainAsync(RuntimeDrainIntentV1.Graceful, convergenceCancellation)
                .ConfigureAwait(false);
            if (drain.Disposition != RuntimeParticipantDispositionV1.Succeeded)
                return convergenceCancellation.IsCancellationRequested
                    ? VoiceActivityReleaseDispositionV1.ReleaseUnconfirmed
                    : VoiceActivityReleaseDispositionV1.Failed;
            var terminate = await participant.TerminateAsync(RuntimeTerminationCauseV1.Requested,
                convergenceCancellation).ConfigureAwait(false);
            if (terminate.Disposition != RuntimeParticipantDispositionV1.Succeeded)
                return convergenceCancellation.IsCancellationRequested
                    ? VoiceActivityReleaseDispositionV1.ReleaseUnconfirmed
                    : VoiceActivityReleaseDispositionV1.Failed;
            await participant.DisposeAsync().ConfigureAwait(false);
            return VoiceActivityReleaseDispositionV1.Confirmed;
        }
        catch (OperationCanceledException) { return VoiceActivityReleaseDispositionV1.ReleaseUnconfirmed; }
        catch { return VoiceActivityReleaseDispositionV1.Failed; }
    }

    private static async ValueTask TerminateCandidateAsync(IRuntimeParticipantV1 candidate,
        CancellationToken convergenceCancellation)
    {
        try
        {
            await candidate.TerminateAsync(RuntimeTerminationCauseV1.PrepareFailed,
                convergenceCancellation).ConfigureAwait(false);
            await candidate.DisposeAsync().ConfigureAwait(false);
        }
        catch { }
    }

    private VoiceActivityParticipantReplacementResultV1.Rejected Reject(string code) =>
        new(_lifecycle.Current, code);
}
