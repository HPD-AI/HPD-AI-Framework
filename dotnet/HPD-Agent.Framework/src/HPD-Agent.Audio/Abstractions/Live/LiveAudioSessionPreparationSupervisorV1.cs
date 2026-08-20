using HPD.Agent.Authority;
using HPD.Agent.Audio.Runtime.Output;
using HPD.Agent.Audio.Runtime.Tools;

namespace HPD.Agent.Audio;

internal abstract record LiveAudioSessionPreparationResultV1
{
    private LiveAudioSessionPreparationResultV1() { }
    internal sealed record Prepared(LiveAudioPreparedSessionV1 Session) : LiveAudioSessionPreparationResultV1;
    internal sealed record Rejected(LiveAudioSessionStartRejectionV1 Reason) : LiveAudioSessionPreparationResultV1;
    internal sealed record Conflict(JournalPositionV1 ExistingPosition, Hash256 ExistingFingerprint) : LiveAudioSessionPreparationResultV1;
    internal sealed record JoinedExisting(JournalPositionV1 ReservationPosition, Hash256 RequestFingerprint) : LiveAudioSessionPreparationResultV1;
    internal sealed record ReservedNeedsConvergence(LiveAudioSessionPreparationSupervisorV1.CleanAbandonment Abandonment)
        : LiveAudioSessionPreparationResultV1
    {
        internal JournalPositionV1 ReservationPosition => Abandonment.ReservationPosition;
        internal BoundedAscii SafeCode => Abandonment.SafeCode;
    }
    internal sealed record OutcomeUnknown(OperationId OperationId, JournalPositionV1? ReservationPosition,
        BoundedAscii SafeCode) : LiveAudioSessionPreparationResultV1;
}

internal abstract record LiveAudioPreparedSessionUnwindResultV1
{
    private LiveAudioPreparedSessionUnwindResultV1() { }
    internal sealed record Clean : LiveAudioPreparedSessionUnwindResultV1;
    internal sealed record OutcomeUnknown(BoundedAscii SafeCode) : LiveAudioPreparedSessionUnwindResultV1;
}

internal sealed class LiveAudioPreparedSessionV1
{
    private readonly IReadOnlyList<OwnedParticipant> _participants;
    private readonly SemaphoreSlim _unwindGate = new(1, 1);
    private LiveAudioPreparedSessionUnwindResultV1? _unwindResult;

    internal LiveAudioPreparedSessionV1(LiveAudioSessionStartRequestV1 request, LiveAudioParticipantPlanV1 plan,
        JournalPositionV1 reservationPosition, LiveAudioParticipantPreparationResultV1.Prepared prepared)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        if (!reservationPosition.IsValid) throw new ArgumentException("A reservation position is required.", nameof(reservationPosition));
        ReservationPosition = reservationPosition;
        OutputV2 = LiveAudioOutputGenerationV2.TryCreate(request.ExpectedAuthority);
        ToolV1 = LiveAudioToolGenerationV1.TryCreate(request.ExpectedAuthority);
        EffectiveFingerprint = prepared.EffectiveFingerprint;
        SkippedOptionalFactories = prepared.SkippedOptionalFactories;
        var descriptors = plan.Descriptors.ToDictionary(value => value.FactoryKey.ToString(), StringComparer.Ordinal);
        _participants = Array.AsReadOnly(prepared.Participants.Select(participant =>
            new OwnedParticipant(participant, descriptors[participant.FactoryKey.ToString()])).ToArray());
    }

    internal LiveAudioSessionStartRequestV1 Request { get; }
    internal LiveAudioParticipantPlanV1 Plan { get; }
    internal JournalPositionV1 ReservationPosition { get; }
    internal LiveAudioOutputGenerationV2? OutputV2 { get; }
    internal LiveAudioToolGenerationV1? ToolV1 { get; }
    internal Hash256 EffectiveFingerprint { get; }
    internal IReadOnlyList<BoundedAscii> SkippedOptionalFactories { get; }
    internal IReadOnlyList<ILiveAudioPreparedParticipantV1> Participants =>
        Array.AsReadOnly(_participants.Select(value => value.Participant).ToArray());

    internal async ValueTask<LiveAudioPreparedSessionUnwindResultV1> UnwindAsync()
    {
        await _unwindGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_unwindResult is not null) return _unwindResult;
            var unknown = false;
            for (var index = _participants.Count - 1; index >= 0; index--)
            {
                Task? pending = null;
                try
                {
                    pending = _participants[index].Participant.DisposeAsync().AsTask();
                    await pending.WaitAsync(ToTimeSpan(_participants[index].Descriptor.MaximumTerminateDuration)).ConfigureAwait(false);
                }
                catch
                {
                    unknown = true;
                    if (pending is { IsCompleted: false }) _ = ObserveLateDisposalAsync(pending);
                }
            }
            return _unwindResult = unknown
                ? new LiveAudioPreparedSessionUnwindResultV1.OutcomeUnknown(new BoundedAscii("prepared-session-unwind-unknown"))
                : new LiveAudioPreparedSessionUnwindResultV1.Clean();
        }
        finally { _unwindGate.Release(); }
    }

    private static async Task ObserveLateDisposalAsync(Task pending)
    { try { await pending.ConfigureAwait(false); } catch { } }

    private static TimeSpan ToTimeSpan(DurationNs duration) =>
        TimeSpan.FromTicks(checked((duration.Nanoseconds + 99) / 100));

    private sealed record OwnedParticipant(ILiveAudioPreparedParticipantV1 Participant,
        LiveAudioParticipantDescriptorV1 Descriptor);
}

internal static class LiveAudioSessionPreparationSupervisorV1
{
    private static readonly object AbandonmentIssuer = new();

    internal sealed class CleanAbandonment
    {
        internal CleanAbandonment(object issuer, LiveAudioSessionStartRequestV1 request,
            JournalPositionV1 reservationPosition, BoundedAscii safeCode)
        {
            if (!ReferenceEquals(issuer, AbandonmentIssuer)) throw new ArgumentException("Only the preparation supervisor can attest clean abandonment.", nameof(issuer));
            Request = request; ReservationPosition = reservationPosition; SafeCode = safeCode;
        }
        internal LiveAudioSessionStartRequestV1 Request { get; }
        internal JournalPositionV1 ReservationPosition { get; }
        internal BoundedAscii SafeCode { get; }
    }

    internal static async ValueTask<LiveAudioSessionPreparationResultV1> PrepareAsync(
        IAuthorityJournalV1 journal, LiveAudioSessionStartRequestV1 request, LiveAudioParticipantFactoryCatalogV1 catalog,
        MonotonicStampV1 reservationMonotonicNow, UtcInstant reservationUtcNow,
        MonotonicStampV1 acquisitionMonotonicNow, UtcInstant acquisitionUtcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal); ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(catalog);
        var observationOrder = reservationMonotonicNow.CompareTo(acquisitionMonotonicNow);
        if (observationOrder is ClockComparison.Incomparable or ClockComparison.Later)
            throw new ArgumentException("The acquisition monotonic observation must be comparable and no earlier than reservation.",
                nameof(acquisitionMonotonicNow));
        if (acquisitionUtcNow.NanosecondsSinceUnixEpoch < reservationUtcNow.NanosecondsSinceUnixEpoch)
            throw new ArgumentException("The acquisition UTC observation cannot precede reservation evidence.", nameof(acquisitionUtcNow));
        LiveAudioParticipantPlanV1 plan;
        try { plan = LiveAudioParticipantPlanCompilerV1.Compile(request, catalog); }
        catch (ArgumentException)
        {
            return new LiveAudioSessionPreparationResultV1.Rejected(LiveAudioSessionStartRejectionV1.ParticipantUnavailable);
        }

        LiveAudioSessionStartResultV1 reservation;
        try
        {
            reservation = await LiveAudioSessionReservationCoordinatorV1.ReserveAsync(
                journal, request, reservationMonotonicNow, reservationUtcNow, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new LiveAudioSessionPreparationResultV1.OutcomeUnknown(
                request.OperationId, null, new BoundedAscii("reservation-cancelled-unknown"));
        }
        catch (Exception)
        {
            return new LiveAudioSessionPreparationResultV1.OutcomeUnknown(
                request.OperationId, null, new BoundedAscii("reservation-store-unknown"));
        }

        if (reservation is LiveAudioSessionStartResultV1.Rejected rejected)
            return new LiveAudioSessionPreparationResultV1.Rejected(rejected.Reason);
        if (reservation is LiveAudioSessionStartResultV1.Conflict conflict)
            return new LiveAudioSessionPreparationResultV1.Conflict(conflict.ExistingPosition, conflict.ExistingFingerprint);
        if (reservation is LiveAudioSessionStartResultV1.OutcomeUnknown unknown)
            return new LiveAudioSessionPreparationResultV1.OutcomeUnknown(unknown.OperationId, null, unknown.SafeCode);
        if (reservation is LiveAudioSessionStartResultV1.Joined joined)
            return new LiveAudioSessionPreparationResultV1.JoinedExisting(joined.Position, joined.Fingerprint);

        var reserved = (LiveAudioSessionStartResultV1.Reserved)reservation;
        try
        {
            var acquisition = await LiveAudioSessionReservationCoordinatorV1.RevalidateProofsAsync(
                journal, request, acquisitionMonotonicNow, acquisitionUtcNow, cancellationToken).ConfigureAwait(false);
            if (acquisition is LiveAudioProofValidationResultV1.Rejected)
                return new LiveAudioSessionPreparationResultV1.ReservedNeedsConvergence(
                    new CleanAbandonment(AbandonmentIssuer, request, reserved.Position, new BoundedAscii("acquisition-proof-stale")));
            if (acquisition is LiveAudioProofValidationResultV1.OutcomeUnknown acquisitionUnknown)
                return new LiveAudioSessionPreparationResultV1.OutcomeUnknown(
                    request.OperationId, reserved.Position, acquisitionUnknown.SafeCode);
        }
        catch (OperationCanceledException)
        {
            return new LiveAudioSessionPreparationResultV1.ReservedNeedsConvergence(
                new CleanAbandonment(AbandonmentIssuer, request, reserved.Position, new BoundedAscii("acquisition-cancelled")));
        }
        catch (Exception)
        {
            return new LiveAudioSessionPreparationResultV1.OutcomeUnknown(
                request.OperationId, reserved.Position, new BoundedAscii("acquisition-store-unknown"));
        }

        var preparation = await LiveAudioParticipantPreparationCoordinatorV1.PrepareAsync(
            request, catalog, cancellationToken).ConfigureAwait(false);
        return preparation switch
        {
            LiveAudioParticipantPreparationResultV1.Prepared prepared =>
                new LiveAudioSessionPreparationResultV1.Prepared(
                    new LiveAudioPreparedSessionV1(request, plan, reserved.Position, prepared)),
            LiveAudioParticipantPreparationResultV1.OutcomeUnknown unknownPreparation =>
                new LiveAudioSessionPreparationResultV1.OutcomeUnknown(
                    request.OperationId, reserved.Position, unknownPreparation.SafeCode),
            LiveAudioParticipantPreparationResultV1.Unavailable => NeedsConvergence(request, reserved.Position, "participant-unavailable"),
            LiveAudioParticipantPreparationResultV1.Failed failed => NeedsConvergence(request, reserved.Position, failed.SafeCode.ToString()),
            LiveAudioParticipantPreparationResultV1.Cancelled => NeedsConvergence(request, reserved.Position, "participant-preparation-cancelled"),
            _ => new LiveAudioSessionPreparationResultV1.OutcomeUnknown(
                request.OperationId, reserved.Position, new BoundedAscii("participant-result-unknown")),
        };
    }

    private static LiveAudioSessionPreparationResultV1 NeedsConvergence(
        LiveAudioSessionStartRequestV1 request, JournalPositionV1 position, string code) =>
        new LiveAudioSessionPreparationResultV1.ReservedNeedsConvergence(
            new CleanAbandonment(AbandonmentIssuer, request, position, new BoundedAscii(code)));
}
