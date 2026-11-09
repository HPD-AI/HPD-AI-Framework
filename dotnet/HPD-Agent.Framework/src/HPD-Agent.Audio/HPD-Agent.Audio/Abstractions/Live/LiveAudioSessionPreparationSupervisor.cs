using HPD.Agent.Authority;
using HPD.Agent.Audio.Runtime.Output;
using HPD.Agent.Audio.Runtime.Tools;
using HPD.Agent.Audio.Runtime.Routing;

namespace HPD.Agent.Audio;

internal abstract record LiveAudioSessionPreparationResult
{
    private LiveAudioSessionPreparationResult() { }
    internal sealed record Prepared(LiveAudioPreparedSession Session) : LiveAudioSessionPreparationResult;
    internal sealed record Rejected(LiveAudioSessionStartRejection Reason) : LiveAudioSessionPreparationResult;
    internal sealed record Conflict(JournalPositionV1 ExistingPosition, Hash256 ExistingFingerprint) : LiveAudioSessionPreparationResult;
    internal sealed record JoinedExisting(JournalPositionV1 ReservationPosition, Hash256 RequestFingerprint) : LiveAudioSessionPreparationResult;
    internal sealed record ReservedNeedsConvergence(LiveAudioSessionPreparationSupervisor.CleanAbandonment Abandonment)
        : LiveAudioSessionPreparationResult
    {
        internal JournalPositionV1 ReservationPosition => Abandonment.ReservationPosition;
        internal BoundedAscii SafeCode => Abandonment.SafeCode;
    }
    internal sealed record OutcomeUnknown(OperationId OperationId, JournalPositionV1? ReservationPosition,
        BoundedAscii SafeCode) : LiveAudioSessionPreparationResult;
}

internal abstract record LiveAudioPreparedSessionUnwindResult
{
    private LiveAudioPreparedSessionUnwindResult() { }
    internal sealed record Clean : LiveAudioPreparedSessionUnwindResult;
    internal sealed record OutcomeUnknown(BoundedAscii SafeCode) : LiveAudioPreparedSessionUnwindResult;
}

internal sealed class LiveAudioPreparedSession
{
    private readonly IReadOnlyList<OwnedParticipant> _participants;
    private readonly SemaphoreSlim _unwindGate = new(1, 1);
    private LiveAudioPreparedSessionUnwindResult? _unwindResult;

    internal LiveAudioPreparedSession(LiveAudioSessionStartRequest request, LiveAudioParticipantPlan plan,
        JournalPositionV1 reservationPosition, LiveAudioParticipantPreparationResult.Prepared prepared)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        if (!reservationPosition.IsValid) throw new ArgumentException("A reservation position is required.", nameof(reservationPosition));
        ReservationPosition = reservationPosition;
        Output = LiveAudioOutputGeneration.TryCreate(request.ExpectedAuthority);
        Tool = LiveAudioToolGeneration.TryCreate(request.ExpectedAuthority);
        Route = LiveAudioRouteGeneration.TryCreate(request.ExpectedAuthority);
        EffectiveFingerprint = prepared.EffectiveFingerprint;
        SkippedOptionalFactories = prepared.SkippedOptionalFactories;
        var descriptors = plan.Descriptors.ToDictionary(value => value.FactoryKey.ToString(), StringComparer.Ordinal);
        _participants = Array.AsReadOnly(prepared.Participants.Select(participant =>
            new OwnedParticipant(participant, descriptors[participant.FactoryKey.ToString()])).ToArray());
    }

    internal LiveAudioSessionStartRequest Request { get; }
    internal LiveAudioParticipantPlan Plan { get; }
    internal JournalPositionV1 ReservationPosition { get; }
    internal LiveAudioOutputGeneration? Output { get; }
    internal LiveAudioToolGeneration? Tool { get; }
    internal LiveAudioRouteGeneration? Route { get; }
    internal Hash256 EffectiveFingerprint { get; }
    internal IReadOnlyList<BoundedAscii> SkippedOptionalFactories { get; }
    internal IReadOnlyList<ILiveAudioPreparedParticipant> Participants =>
        Array.AsReadOnly(_participants.Select(value => value.Participant).ToArray());

    internal LiveAudioOutputActivationResult ActivateOutput(OutputOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        return Output is null
            ? new LiveAudioOutputActivationResult.Rejected(new BoundedAscii("output-generation-unavailable"))
            : Output.Activate(offer);
    }

    internal PreparedOutputExecution BindOutputOrigin(OutputOriginEvidence origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        return Output is null
            ? throw new InvalidOperationException("The prepared session has no admitted output generation.")
            : new PreparedOutputExecution(Output, origin);
    }

    internal async ValueTask<LiveAudioPreparedSessionUnwindResult> UnwindAsync()
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
                ? new LiveAudioPreparedSessionUnwindResult.OutcomeUnknown(new BoundedAscii("prepared-session-unwind-unknown"))
                : new LiveAudioPreparedSessionUnwindResult.Clean();
        }
        finally { _unwindGate.Release(); }
    }

    private static async Task ObserveLateDisposalAsync(Task pending)
    { try { await pending.ConfigureAwait(false); } catch { } }

    private static TimeSpan ToTimeSpan(DurationNs duration) =>
        TimeSpan.FromTicks(checked((duration.Nanoseconds + 99) / 100));

    private sealed record OwnedParticipant(ILiveAudioPreparedParticipant Participant,
        LiveAudioParticipantDescriptor Descriptor);
}

internal static class LiveAudioSessionPreparationSupervisor
{
    private static readonly object AbandonmentIssuer = new();

    internal sealed class CleanAbandonment
    {
        internal CleanAbandonment(object issuer, LiveAudioSessionStartRequest request,
            JournalPositionV1 reservationPosition, BoundedAscii safeCode)
        {
            if (!ReferenceEquals(issuer, AbandonmentIssuer)) throw new ArgumentException("Only the preparation supervisor can attest clean abandonment.", nameof(issuer));
            Request = request; ReservationPosition = reservationPosition; SafeCode = safeCode;
        }
        internal LiveAudioSessionStartRequest Request { get; }
        internal JournalPositionV1 ReservationPosition { get; }
        internal BoundedAscii SafeCode { get; }
    }

    internal static async ValueTask<LiveAudioSessionPreparationResult> PrepareAsync(
        IAuthorityJournalV1 journal, LiveAudioSessionStartRequest request, LiveAudioParticipantFactoryCatalog catalog,
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
        LiveAudioParticipantPlan plan;
        try { plan = LiveAudioParticipantPlanCompiler.Compile(request, catalog); }
        catch (ArgumentException)
        {
            return new LiveAudioSessionPreparationResult.Rejected(LiveAudioSessionStartRejection.ParticipantUnavailable);
        }

        LiveAudioSessionStartResult reservation;
        try
        {
            reservation = await LiveAudioSessionReservationCoordinator.ReserveAsync(
                journal, request, reservationMonotonicNow, reservationUtcNow, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new LiveAudioSessionPreparationResult.OutcomeUnknown(
                request.OperationId, null, new BoundedAscii("reservation-cancelled-unknown"));
        }
        catch (Exception)
        {
            return new LiveAudioSessionPreparationResult.OutcomeUnknown(
                request.OperationId, null, new BoundedAscii("reservation-store-unknown"));
        }

        if (reservation is LiveAudioSessionStartResult.Rejected rejected)
            return new LiveAudioSessionPreparationResult.Rejected(rejected.Reason);
        if (reservation is LiveAudioSessionStartResult.Conflict conflict)
            return new LiveAudioSessionPreparationResult.Conflict(conflict.ExistingPosition, conflict.ExistingFingerprint);
        if (reservation is LiveAudioSessionStartResult.OutcomeUnknown unknown)
            return new LiveAudioSessionPreparationResult.OutcomeUnknown(unknown.OperationId, null, unknown.SafeCode);
        if (reservation is LiveAudioSessionStartResult.Joined joined)
            return new LiveAudioSessionPreparationResult.JoinedExisting(joined.Position, joined.Fingerprint);

        var reserved = (LiveAudioSessionStartResult.Reserved)reservation;
        try
        {
            var acquisition = await LiveAudioSessionReservationCoordinator.RevalidateProofsAsync(
                journal, request, acquisitionMonotonicNow, acquisitionUtcNow, cancellationToken).ConfigureAwait(false);
            if (acquisition is LiveAudioProofValidationResult.Rejected)
                return new LiveAudioSessionPreparationResult.ReservedNeedsConvergence(
                    new CleanAbandonment(AbandonmentIssuer, request, reserved.Position, new BoundedAscii("acquisition-proof-stale")));
            if (acquisition is LiveAudioProofValidationResult.OutcomeUnknown acquisitionUnknown)
                return new LiveAudioSessionPreparationResult.OutcomeUnknown(
                    request.OperationId, reserved.Position, acquisitionUnknown.SafeCode);
        }
        catch (OperationCanceledException)
        {
            return new LiveAudioSessionPreparationResult.ReservedNeedsConvergence(
                new CleanAbandonment(AbandonmentIssuer, request, reserved.Position, new BoundedAscii("acquisition-cancelled")));
        }
        catch (Exception)
        {
            return new LiveAudioSessionPreparationResult.OutcomeUnknown(
                request.OperationId, reserved.Position, new BoundedAscii("acquisition-store-unknown"));
        }

        var preparation = await LiveAudioParticipantPreparationCoordinator.PrepareAsync(
            request, catalog, cancellationToken).ConfigureAwait(false);
        return preparation switch
        {
            LiveAudioParticipantPreparationResult.Prepared prepared =>
                new LiveAudioSessionPreparationResult.Prepared(
                    new LiveAudioPreparedSession(request, plan, reserved.Position, prepared)),
            LiveAudioParticipantPreparationResult.OutcomeUnknown unknownPreparation =>
                new LiveAudioSessionPreparationResult.OutcomeUnknown(
                    request.OperationId, reserved.Position, unknownPreparation.SafeCode),
            LiveAudioParticipantPreparationResult.Unavailable => NeedsConvergence(request, reserved.Position, "participant-unavailable"),
            LiveAudioParticipantPreparationResult.Failed failed => NeedsConvergence(request, reserved.Position, failed.SafeCode.ToString()),
            LiveAudioParticipantPreparationResult.Cancelled => NeedsConvergence(request, reserved.Position, "participant-preparation-cancelled"),
            _ => new LiveAudioSessionPreparationResult.OutcomeUnknown(
                request.OperationId, reserved.Position, new BoundedAscii("participant-result-unknown")),
        };
    }

    private static LiveAudioSessionPreparationResult NeedsConvergence(
        LiveAudioSessionStartRequest request, JournalPositionV1 position, string code) =>
        new LiveAudioSessionPreparationResult.ReservedNeedsConvergence(
            new CleanAbandonment(AbandonmentIssuer, request, position, new BoundedAscii(code)));
}
