using HPD.Agent.Authority;

namespace HPD.Agent.Audio;

internal enum LiveAudioSessionFailureConvergenceStageV1 : ushort
{
    ValidateReservation = 1,
    Begin = 2,
    Advance = 3,
    Complete = 4,
}

internal abstract record LiveAudioSessionFailureConvergenceResultV1
{
    private LiveAudioSessionFailureConvergenceResultV1() { }
    internal sealed record Completed(JournalPositionV1 BeginResult, JournalPositionV1 AdvanceResult,
        JournalPositionV1 CompleteResult) : LiveAudioSessionFailureConvergenceResultV1;
    internal sealed record AlreadyCompleted(JournalPositionV1 CompleteResult) : LiveAudioSessionFailureConvergenceResultV1;
    internal sealed record GenerationReplaced(RuntimeGenerationId Replacement) : LiveAudioSessionFailureConvergenceResultV1;
    internal sealed record Rejected(LiveAudioSessionFailureConvergenceStageV1 Stage, BoundedAscii SafeCode,
        long LastVerifiedPosition) : LiveAudioSessionFailureConvergenceResultV1;
    internal sealed record RetryRequired(LiveAudioSessionFailureConvergenceStageV1 Stage, long ObservedHead) : LiveAudioSessionFailureConvergenceResultV1;
    internal sealed record OutcomeUnknown(LiveAudioSessionFailureConvergenceStageV1 Stage, OperationId OperationId,
        JournalPositionV1? KnownPosition, BoundedAscii SafeCode) : LiveAudioSessionFailureConvergenceResultV1;
}

internal static class LiveAudioSessionFailureConvergenceV1
{
    internal static async ValueTask<LiveAudioSessionFailureConvergenceResultV1> ConvergeAsync(
        IAuthorityJournalV1 journal, LiveAudioSessionPreparationSupervisorV1.CleanAbandonment abandonment,
        UtcInstant observedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal); ArgumentNullException.ThrowIfNull(abandonment);
        var request = abandonment.Request; var reservationPosition = abandonment.ReservationPosition;
        var ids = LiveAudioSessionFailureOperationIdsV1.Derive(request, reservationPosition);
        var validation = await ValidateReservationAsync(journal, request, reservationPosition, ids.Begin, cancellationToken)
            .ConfigureAwait(false);
        if (validation is not null) return validation;

        var begin = await AdmitAsync(journal, request, ids.Begin,
            LiveAudioSessionFailureConvergenceStageV1.Begin, observedAt, cancellationToken).ConfigureAwait(false);
        if (begin.Result is not null) return begin.Result;

        var advance = await AdmitAsync(journal, request, ids.Advance,
            LiveAudioSessionFailureConvergenceStageV1.Advance, observedAt, cancellationToken).ConfigureAwait(false);
        if (advance.Result is not null) return advance.Result;

        var complete = await AdmitAsync(journal, request, ids.Complete,
            LiveAudioSessionFailureConvergenceStageV1.Complete, observedAt, cancellationToken).ConfigureAwait(false);
        if (complete.Result is not null) return complete.Result;
        return complete.AlreadyCommitted
            ? new LiveAudioSessionFailureConvergenceResultV1.AlreadyCompleted(complete.Position!.Value)
            : new LiveAudioSessionFailureConvergenceResultV1.Completed(
                begin.Position!.Value, advance.Position!.Value, complete.Position!.Value);
    }

    private static async ValueTask<LiveAudioSessionFailureConvergenceResultV1?> ValidateReservationAsync(
        IAuthorityJournalV1 journal, LiveAudioSessionStartRequestV1 request, JournalPositionV1 reservationPosition,
        OperationId operationId, CancellationToken cancellationToken)
    {
        try
        {
            var commandId = SessionLifecycleCommandFactIdV1.Derive(request.ExpectedAuthority.Session, request.OperationId);
            var read = await SessionLifecycleSnapshotReaderV1.ReadAsync(
                journal, request.ExpectedAuthority.Session, commandId, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (read is SessionLifecycleSnapshotReadResultV1.OutcomeUnknown unknown)
                return Unknown(LiveAudioSessionFailureConvergenceStageV1.ValidateReservation, operationId, null, unknown.SafeCode);
            var verified = (SessionLifecycleSnapshotReadResultV1.Verified)read;
            if (verified.Fold is SessionLifecycleJournalFoldResultV1.GenerationReplaced replaced)
                return new LiveAudioSessionFailureConvergenceResultV1.GenerationReplaced(replaced.Replacement);
            if (verified.Fold is SessionLifecycleJournalFoldResultV1.InvalidHistory invalid)
                return Reject(LiveAudioSessionFailureConvergenceStageV1.ValidateReservation, invalid.SafeCode, invalid.LastVerifiedPosition);
            var current = (SessionLifecycleJournalFoldResultV1.Current)verified.Fold;
            if (current.TargetCommandFact is not { } command || current.TargetResultFact is not { } result ||
                result.Position != reservationPosition ||
                !SessionLifecyclePayloadV1Codec.TryDecodeCommand(command.PayloadMemory, out var outer) ||
                !SessionLifecycleBodyCodecsV1.TryDecodeCommand(outer!.BodyBytes.ToArray(), out var body) ||
                body is not SessionLifecycleCommandBodyV1.ReserveStarting starting ||
                starting.OperationId != request.OperationId || starting.AdmissionFingerprint != request.Fingerprint ||
                !SessionLifecyclePayloadV1Codec.TryDecodeFact(result.PayloadMemory, out var resultOuter) ||
                !SessionLifecycleBodyCodecsV1.TryDecodeFact(resultOuter!.BodyBytes.ToArray(), out var resultBody) ||
                resultBody!.OperationId != request.OperationId || resultBody.CommandPosition != command.Position ||
                resultBody.Outcome is not (SessionLifecycleOutcomeV1.Applied or SessionLifecycleOutcomeV1.Idempotent) ||
                resultBody.Snapshot.State != SessionLifecycleStateWireV1.Starting)
                return Reject(LiveAudioSessionFailureConvergenceStageV1.ValidateReservation,
                    new BoundedAscii("starting-reservation-mismatch"), verified.SnapshotThrough);
            return null;
        }
        catch (OperationCanceledException)
        { return Unknown(LiveAudioSessionFailureConvergenceStageV1.ValidateReservation, operationId, null, "reservation-validation-cancelled"); }
        catch
        { return Unknown(LiveAudioSessionFailureConvergenceStageV1.ValidateReservation, operationId, null, "reservation-validation-unknown"); }
    }

    private static async ValueTask<StageResult> AdmitAsync(
        IAuthorityJournalV1 journal, LiveAudioSessionStartRequestV1 request, OperationId operationId,
        LiveAudioSessionFailureConvergenceStageV1 stage, UtcInstant observedAt, CancellationToken cancellationToken)
    {
        var context = await ResolveStageContextAsync(journal, request, operationId, stage, cancellationToken).ConfigureAwait(false);
        if (context.Result is not null) return StageResult.Stop(context.Result);
        var predecessor = context.Predecessor!.Value;
        SessionLifecycleCommandBodyV1 body = stage switch
        {
            LiveAudioSessionFailureConvergenceStageV1.Begin => new SessionLifecycleCommandBodyV1.BeginTermination(
                operationId, predecessor, SessionTerminalIntentWireV1.Abort, SessionTerminalCauseWireV1.StartFailed,
                SessionTerminalSeverityWireV1.Recoverable, SessionConvergencePhaseWireV1.Fencing),
            LiveAudioSessionFailureConvergenceStageV1.Advance => new SessionLifecycleCommandBodyV1.AdvanceTermination(
                operationId, predecessor, SessionConvergencePhaseWireV1.Disposing, SessionTerminalIntentWireV1.Abort,
                SessionTerminalCauseWireV1.StartFailed, SessionTerminalSeverityWireV1.Recoverable, true),
            LiveAudioSessionFailureConvergenceStageV1.Complete =>
                new SessionLifecycleCommandBodyV1.Complete(operationId, predecessor, true),
            _ => throw new InvalidOperationException("The convergence stage cannot emit a lifecycle command."),
        };
        SessionLifecycleAdmissionResultV1 admitted;
        try
        {
            var command = new SessionLifecycleCommandV1(request.ExpectedAuthority.Session, context.Authority!,
                SessionLifecycleBodyCodecsV1.Encode(body));
            admitted = await SessionLifecycleAdmissionCoordinatorV1.AdmitAsync(
                journal, command, request.Correlation, observedAt, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        { return StageResult.Stop(Unknown(stage, operationId, null, "lifecycle-stage-cancelled")); }
        catch
        { return StageResult.Stop(Unknown(stage, operationId, null, "lifecycle-stage-unknown")); }

        if (admitted is SessionLifecycleAdmissionResultV1.GenerationReplaced replaced)
            return StageResult.Stop(new LiveAudioSessionFailureConvergenceResultV1.GenerationReplaced(replaced.Replacement));
        if (admitted is SessionLifecycleAdmissionResultV1.InvalidHistory invalid)
            return StageResult.Stop(Reject(stage, invalid.SafeCode, invalid.LastVerifiedPosition));
        if (admitted is SessionLifecycleAdmissionResultV1.RetryRequired retry)
            return StageResult.Stop(new LiveAudioSessionFailureConvergenceResultV1.RetryRequired(stage, retry.ObservedHead));
        if (admitted is SessionLifecycleAdmissionResultV1.Rejected rejected)
            return StageResult.Stop(Reject(stage, rejected.SafeCode, Math.Max(0, predecessor.Sequence)));
        if (admitted is SessionLifecycleAdmissionResultV1.ContradictoryDuplicate)
            return StageResult.Stop(Reject(stage, new BoundedAscii("lifecycle-stage-contradiction"), Math.Max(0, predecessor.Sequence)));
        if (admitted is SessionLifecycleAdmissionResultV1.OutcomeUnknown unknown)
            return StageResult.Stop(Unknown(stage, operationId, null, unknown.SafeCode));
        var pair = admitted switch
        {
            SessionLifecycleAdmissionResultV1.Committed committed => (committed.Result, false),
            SessionLifecycleAdmissionResultV1.AlreadyCommitted existing => (existing.Result, true),
            _ => default,
        };
        if (pair.Item1 is null)
            return StageResult.Stop(Reject(stage, new BoundedAscii("lifecycle-result-invalid"), predecessor.Sequence));
        if (!TryValidateResult(pair.Item1, body, stage, out var safeCode))
            return StageResult.Stop(Reject(stage, safeCode, pair.Item1.Position.Sequence));
        return new StageResult(pair.Item1.Position, pair.Item2, null);
    }

    private static async ValueTask<StageContext> ResolveStageContextAsync(
        IAuthorityJournalV1 journal, LiveAudioSessionStartRequestV1 request, OperationId operationId,
        LiveAudioSessionFailureConvergenceStageV1 stage, CancellationToken cancellationToken)
    {
        try
        {
            var commandId = SessionLifecycleCommandFactIdV1.Derive(request.ExpectedAuthority.Session, operationId);
            var read = await SessionLifecycleSnapshotReaderV1.ReadAsync(
                journal, request.ExpectedAuthority.Session, commandId, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (read is SessionLifecycleSnapshotReadResultV1.OutcomeUnknown unknown)
                return StageContext.Stop(Unknown(stage, operationId, null, unknown.SafeCode));
            var verified = (SessionLifecycleSnapshotReadResultV1.Verified)read;
            if (verified.Fold is SessionLifecycleJournalFoldResultV1.GenerationReplaced replaced)
                return StageContext.Stop(new LiveAudioSessionFailureConvergenceResultV1.GenerationReplaced(replaced.Replacement));
            if (verified.Fold is SessionLifecycleJournalFoldResultV1.InvalidHistory invalid)
                return StageContext.Stop(Reject(stage, invalid.SafeCode, invalid.LastVerifiedPosition));
            var current = (SessionLifecycleJournalFoldResultV1.Current)verified.Fold;
            if (current.TargetCommandFact is { } existing)
            {
                if (!SessionLifecyclePayloadV1Codec.TryDecodeCommand(existing.PayloadMemory, out var outer) ||
                    !SessionLifecycleBodyCodecsV1.TryDecodeCommand(outer!.BodyBytes.ToArray(), out var body) ||
                    body!.OperationId != operationId || body.ExpectedLifecycleFact is not { } frozenPredecessor ||
                    !MatchesStage(body.Kind, stage))
                    return StageContext.Stop(Reject(stage, new BoundedAscii("lifecycle-stage-command-invalid"), verified.SnapshotThrough));
                return new StageContext(outer.ExpectedAuthority, frozenPredecessor, null);
            }
            if (current.PreviousLifecycleFact is not { } predecessor)
                return StageContext.Stop(Reject(stage, new BoundedAscii("lifecycle-stage-predecessor-missing"), verified.SnapshotThrough));
            var requestedAxes = request.ExpectedAuthority.Axes.Select(value => value.AxisId).ToHashSet();
            var currentAxes = current.Authority.Axes.Where(value => requestedAxes.Contains(value.AxisId)).ToArray();
            if (currentAxes.Length != requestedAxes.Count)
                return StageContext.Stop(Reject(stage, new BoundedAscii("lifecycle-stage-authority-incomplete"), verified.SnapshotThrough));
            var authority = ExpectedAuthorityVectorV1.Create(request.ExpectedAuthority.Session,
                currentAxes.Select(value => value.Value));
            return new StageContext(authority, predecessor, null);
        }
        catch (OperationCanceledException)
        { return StageContext.Stop(Unknown(stage, operationId, null, "lifecycle-stage-context-cancelled")); }
        catch
        { return StageContext.Stop(Unknown(stage, operationId, null, "lifecycle-stage-context-unknown")); }
    }

    private static bool MatchesStage(SessionLifecycleCommandKindV1 kind,
        LiveAudioSessionFailureConvergenceStageV1 stage) => (kind, stage) switch
    {
        (SessionLifecycleCommandKindV1.BeginTermination, LiveAudioSessionFailureConvergenceStageV1.Begin) => true,
        (SessionLifecycleCommandKindV1.AdvanceTermination, LiveAudioSessionFailureConvergenceStageV1.Advance) => true,
        (SessionLifecycleCommandKindV1.Complete, LiveAudioSessionFailureConvergenceStageV1.Complete) => true,
        _ => false,
    };

    private static bool TryValidateResult(AuthorityFactEnvelopeV1 envelope, SessionLifecycleCommandBodyV1 command,
        LiveAudioSessionFailureConvergenceStageV1 stage, out BoundedAscii safeCode)
    {
        safeCode = new BoundedAscii("lifecycle-result-invalid");
        if (!SessionLifecyclePayloadV1Codec.TryDecodeFact(envelope.PayloadMemory, out var outer) ||
            !SessionLifecycleBodyCodecsV1.TryDecodeFact(outer!.BodyBytes.ToArray(), out var fact) ||
            fact!.OperationId != command.OperationId || fact.CommandExpectedLifecycleFact != command.ExpectedLifecycleFact)
            return false;
        if (fact.Outcome == SessionLifecycleOutcomeV1.Rejected)
        { safeCode = fact.SafeCode ?? new BoundedAscii("lifecycle-stage-rejected"); return false; }
        var valid = stage switch
        {
            LiveAudioSessionFailureConvergenceStageV1.Begin =>
                fact.Snapshot.State == SessionLifecycleStateWireV1.Terminating &&
                fact.Snapshot.ConvergencePhase == SessionConvergencePhaseWireV1.Fencing,
            LiveAudioSessionFailureConvergenceStageV1.Advance =>
                fact.Snapshot.State == SessionLifecycleStateWireV1.Terminating &&
                fact.Snapshot.ConvergencePhase == SessionConvergencePhaseWireV1.Disposing && fact.Snapshot.ConversationStopped,
            LiveAudioSessionFailureConvergenceStageV1.Complete =>
                fact.Snapshot.State == SessionLifecycleStateWireV1.Completed && fact.Snapshot.ConversationStopped,
            _ => false,
        };
        return valid;
    }

    private static LiveAudioSessionFailureConvergenceResultV1.Rejected Reject(
        LiveAudioSessionFailureConvergenceStageV1 stage, BoundedAscii code, long position) => new(stage, code, position);
    private static LiveAudioSessionFailureConvergenceResultV1.OutcomeUnknown Unknown(
        LiveAudioSessionFailureConvergenceStageV1 stage, OperationId operation, JournalPositionV1? position, string code) =>
        new(stage, operation, position, new BoundedAscii(code));
    private static LiveAudioSessionFailureConvergenceResultV1.OutcomeUnknown Unknown(
        LiveAudioSessionFailureConvergenceStageV1 stage, OperationId operation, JournalPositionV1? position, BoundedAscii code) =>
        new(stage, operation, position, code);

    private sealed record StageResult(JournalPositionV1? Position, bool AlreadyCommitted,
        LiveAudioSessionFailureConvergenceResultV1? Result)
    { internal static StageResult Stop(LiveAudioSessionFailureConvergenceResultV1 result) => new(null, false, result); }

    private sealed record StageContext(ExpectedAuthorityVectorV1? Authority, JournalPositionV1? Predecessor,
        LiveAudioSessionFailureConvergenceResultV1? Result)
    { internal static StageContext Stop(LiveAudioSessionFailureConvergenceResultV1 result) => new(null, null, result); }
}
