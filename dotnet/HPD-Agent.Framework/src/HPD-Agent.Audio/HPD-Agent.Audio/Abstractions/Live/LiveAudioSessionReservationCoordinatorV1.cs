using HPD.Agent.Authority;

namespace HPD.Agent.Audio;

internal abstract record LiveAudioProofValidationResultV1
{
    private LiveAudioProofValidationResultV1() { }
    internal sealed record Valid : LiveAudioProofValidationResultV1;
    internal sealed record Rejected(LiveAudioSessionStartRejectionV1 Reason) : LiveAudioProofValidationResultV1;
    internal sealed record OutcomeUnknown(BoundedAscii SafeCode) : LiveAudioProofValidationResultV1;
}

/// <summary>Admits only the inert S1 Starting reservation after re-reading current S2 and S9 proofs.</summary>
/// <remarks>This coordinator never constructs participants or invokes provider, device, network, media, output, or transport effects.</remarks>
public static class LiveAudioSessionReservationCoordinatorV1
{
    /// <summary>Revalidates current authority proofs and reserves or joins one exact Starting lifecycle request.</summary>
    /// <param name="journal">The sole S1 authority journal.</param>
    /// <param name="request">The deeply owned inert start request.</param>
    /// <param name="monotonicNow">The current comparable monotonic time used for deadlines and capacity expiry.</param>
    /// <param name="utcNow">The current UTC evidence time used only for capture expiry and fact evidence.</param>
    /// <param name="cancellationToken">Detaches the caller wait without proving commit or noncommit.</param>
    /// <returns>One closed pre-effect reservation result.</returns>
    public static async ValueTask<LiveAudioSessionStartResultV1> ReserveAsync(IAuthorityJournalV1 journal,
        LiveAudioSessionStartRequestV1 request, MonotonicStampV1 monotonicNow, UtcInstant utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal); ArgumentNullException.ThrowIfNull(request);
        var validation = await RevalidateProofsAsync(journal, request, monotonicNow, utcNow, cancellationToken).ConfigureAwait(false);
        if (validation is LiveAudioProofValidationResultV1.Rejected validationRejected)
            return new LiveAudioSessionStartResultV1.Rejected(validationRejected.Reason);
        if (validation is LiveAudioProofValidationResultV1.OutcomeUnknown validationUnknown)
            return new LiveAudioSessionStartResultV1.OutcomeUnknown(request.OperationId, validationUnknown.SafeCode);

        var body = new SessionLifecycleCommandBodyV1.ReserveStarting(request.OperationId, request.Fingerprint);
        var command = new SessionLifecycleCommandV1(request.ExpectedAuthority.Session, request.ExpectedAuthority,
            SessionLifecycleBodyCodecsV1.Encode(body));
        var admitted = await SessionLifecycleAdmissionCoordinatorV1.AdmitAsync(
            journal, command, request.Correlation, utcNow, cancellationToken).ConfigureAwait(false);
        return admitted switch
        {
            SessionLifecycleAdmissionResultV1.Committed committed =>
                new LiveAudioSessionStartResultV1.Reserved(committed.Result.Position, request.Fingerprint),
            SessionLifecycleAdmissionResultV1.AlreadyCommitted existing =>
                new LiveAudioSessionStartResultV1.Joined(existing.Result.Position, request.Fingerprint),
            SessionLifecycleAdmissionResultV1.GenerationReplaced =>
                new LiveAudioSessionStartResultV1.Rejected(LiveAudioSessionStartRejectionV1.StaleAuthority),
            SessionLifecycleAdmissionResultV1.Rejected rejected =>
                new LiveAudioSessionStartResultV1.Rejected(MapRejection(rejected.SafeCode)),
            SessionLifecycleAdmissionResultV1.ContradictoryDuplicate =>
                await ResolveConflictAsync(journal, request, cancellationToken).ConfigureAwait(false),
            SessionLifecycleAdmissionResultV1.OutcomeUnknown unknown =>
                new LiveAudioSessionStartResultV1.OutcomeUnknown(request.OperationId, unknown.SafeCode),
            SessionLifecycleAdmissionResultV1.InvalidHistory invalid =>
                new LiveAudioSessionStartResultV1.OutcomeUnknown(request.OperationId, invalid.SafeCode),
            SessionLifecycleAdmissionResultV1.RetryRequired =>
                new LiveAudioSessionStartResultV1.OutcomeUnknown(request.OperationId, new BoundedAscii("lifecycle-retry-required")),
            _ => new LiveAudioSessionStartResultV1.OutcomeUnknown(request.OperationId, new BoundedAscii("lifecycle-result-unknown")),
        };
    }

    internal static async ValueTask<LiveAudioProofValidationResultV1> RevalidateProofsAsync(
        IAuthorityJournalV1 journal, LiveAudioSessionStartRequestV1 request, MonotonicStampV1 monotonicNow,
        UtcInstant utcNow, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(journal); ArgumentNullException.ThrowIfNull(request);
        if (!monotonicNow.IsValid) throw new ArgumentException("A monotonic observation is required.", nameof(monotonicNow));
        var deadline = monotonicNow.CompareTo(request.TerminalDeadline);
        if (deadline == ClockComparison.Incomparable)
            throw new ArgumentException("The observation must use the request clock and boot.", nameof(monotonicNow));
        if (deadline is ClockComparison.Equal or ClockComparison.Later)
            return new LiveAudioProofValidationResultV1.Rejected(LiveAudioSessionStartRejectionV1.DeadlineReached);

        var capacity = await CapacityAdmissionCoordinatorV1.ReadCurrentAsync(journal, request.ExpectedAuthority.Session,
            request.CapacityGrant.GrantId, monotonicNow, cancellationToken).ConfigureAwait(false);
        if (capacity is CapacityGrantReadResultV1.OutcomeUnknown capacityUnknown)
            return new LiveAudioProofValidationResultV1.OutcomeUnknown(capacityUnknown.SafeCode);
        if (capacity is not CapacityGrantReadResultV1.Current currentCapacity ||
            currentCapacity.Grant.OperationId != request.OperationId || currentCapacity.Grant.Authority != request.ExpectedAuthority)
            return new LiveAudioProofValidationResultV1.Rejected(capacity is CapacityGrantReadResultV1.StaleAuthority
                ? LiveAudioSessionStartRejectionV1.StaleAuthority : LiveAudioSessionStartRejectionV1.CapacityUnavailable);

        var capture = await CaptureGrantAdmissionV1.ReadCurrentAsync(journal, request.ExpectedAuthority.Session,
            request.CaptureGrant.GrantId, utcNow, cancellationToken).ConfigureAwait(false);
        if (capture is CaptureGrantReadResultV1.OutcomeUnknown captureUnknown)
            return new LiveAudioProofValidationResultV1.OutcomeUnknown(captureUnknown.SafeCode);
        if (capture is not CaptureGrantReadResultV1.Active activeCapture || !Matches(request.CaptureGrant, activeCapture.Proof))
            return new LiveAudioProofValidationResultV1.Rejected(
                capture is CaptureGrantReadResultV1.Inactive inactive && inactive.State == CaptureGrantStateV1.Revoked
                    ? LiveAudioSessionStartRejectionV1.StaleAuthority : LiveAudioSessionStartRejectionV1.CaptureUnauthorized);
        return new LiveAudioProofValidationResultV1.Valid();
    }

    private static bool Matches(CaptureGrantProofV1 requested, CaptureGrantProofV1 current) =>
        requested.GrantId == current.GrantId && requested.AuthorizationId == current.AuthorizationId &&
        requested.Authority == current.Authority && requested.ScopeHash == current.ScopeHash &&
        requested.LimitsHash == current.LimitsHash && requested.ExpiresAt == current.ExpiresAt &&
        current.State == CaptureGrantStateV1.Active;

    private static LiveAudioSessionStartRejectionV1 MapRejection(BoundedAscii safeCode) => safeCode.ToString() switch
    {
        "authority-vector-stale" => LiveAudioSessionStartRejectionV1.StaleAuthority,
        _ => LiveAudioSessionStartRejectionV1.ConcurrencyConflict,
    };

    private static async ValueTask<LiveAudioSessionStartResultV1> ResolveConflictAsync(IAuthorityJournalV1 journal,
        LiveAudioSessionStartRequestV1 request, CancellationToken cancellationToken)
    {
        var commandId = SessionLifecycleCommandFactIdV1.Derive(request.ExpectedAuthority.Session, request.OperationId);
        var read = await SessionLifecycleSnapshotReaderV1.ReadAsync(journal, request.ExpectedAuthority.Session, commandId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (read is SessionLifecycleSnapshotReadResultV1.Verified verified &&
            verified.Fold is SessionLifecycleJournalFoldResultV1.Current current &&
            current.TargetCommandFact is { } envelope &&
            SessionLifecyclePayloadV1Codec.TryDecodeCommand(envelope.PayloadMemory, out var outer) &&
            SessionLifecycleBodyCodecsV1.TryDecodeCommand(outer!.BodyBytes.ToArray(), out var body) &&
            body is SessionLifecycleCommandBodyV1.ReserveStarting reserve)
            return new LiveAudioSessionStartResultV1.Conflict(envelope.Position, reserve.AdmissionFingerprint);
        return new LiveAudioSessionStartResultV1.OutcomeUnknown(request.OperationId, new BoundedAscii("lifecycle-conflict-unresolved"));
    }
}
