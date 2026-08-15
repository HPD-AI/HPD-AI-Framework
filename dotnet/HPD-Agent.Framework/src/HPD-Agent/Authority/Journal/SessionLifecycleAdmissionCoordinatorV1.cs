namespace HPD.Agent.Authority;

internal abstract record SessionLifecycleAdmissionResultV1
{
    private static readonly SessionLifecycleCommandPayloadRegistrationV1 CommandRegistration = new();
    private static readonly SessionLifecycleFactPayloadRegistrationV1 ResultRegistration = new();
    private static readonly SchemaReferenceV1 CommandSchema = CommandRegistration.Schema;
    private static readonly SchemaReferenceV1 ResultSchema = ResultRegistration.Schema;
    private SessionLifecycleAdmissionResultV1() { }
    internal sealed record Committed : SessionLifecycleAdmissionResultV1
    { internal Committed(AuthorityFactEnvelopeV1 command, AuthorityFactEnvelopeV1 result) { ValidatePair(command, result); Command = command; Result = result; } internal AuthorityFactEnvelopeV1 Command { get; } internal AuthorityFactEnvelopeV1 Result { get; } }
    internal sealed record AlreadyCommitted : SessionLifecycleAdmissionResultV1
    { internal AlreadyCommitted(AuthorityFactEnvelopeV1 command, AuthorityFactEnvelopeV1 result) { ValidatePair(command, result); Command = command; Result = result; } internal AuthorityFactEnvelopeV1 Command { get; } internal AuthorityFactEnvelopeV1 Result { get; } }
    internal sealed record ContradictoryDuplicate : SessionLifecycleAdmissionResultV1
    { internal ContradictoryDuplicate(JournalFactId factId) { if (!factId.IsValid) throw new ArgumentException("A fact identity is required.", nameof(factId)); FactId = factId; } internal JournalFactId FactId { get; } }
    internal sealed record GenerationReplaced : SessionLifecycleAdmissionResultV1
    { internal GenerationReplaced(RuntimeGenerationId replacement) { if (!replacement.IsValid) throw new ArgumentException("A replacement generation is required.", nameof(replacement)); Replacement = replacement; } internal RuntimeGenerationId Replacement { get; } }
    internal sealed record InvalidHistory : SessionLifecycleAdmissionResultV1
    { internal InvalidHistory(BoundedAscii safeCode, long lastVerifiedPosition) { if (!safeCode.IsValid) throw new ArgumentException("A safe code is required.", nameof(safeCode)); if (lastVerifiedPosition < 0) throw new ArgumentOutOfRangeException(nameof(lastVerifiedPosition)); SafeCode = safeCode; LastVerifiedPosition = lastVerifiedPosition; } internal BoundedAscii SafeCode { get; } internal long LastVerifiedPosition { get; } }
    internal sealed record RetryRequired : SessionLifecycleAdmissionResultV1
    { internal RetryRequired(long observedHead) { if (observedHead < 0) throw new ArgumentOutOfRangeException(nameof(observedHead)); ObservedHead = observedHead; } internal long ObservedHead { get; } }
    internal sealed record Rejected : SessionLifecycleAdmissionResultV1
    { internal Rejected(BoundedAscii safeCode) { if (!safeCode.IsValid) throw new ArgumentException("A safe code is required.", nameof(safeCode)); SafeCode = safeCode; } internal BoundedAscii SafeCode { get; } }
    internal sealed record OutcomeUnknown : SessionLifecycleAdmissionResultV1
    { internal OutcomeUnknown(JournalFactId factId, BoundedAscii safeCode) { if (!factId.IsValid || !safeCode.IsValid) throw new ArgumentException("A fact identity and safe code are required."); FactId = factId; SafeCode = safeCode; } internal JournalFactId FactId { get; } internal BoundedAscii SafeCode { get; } }

    private static void ValidatePair(AuthorityFactEnvelopeV1 command, AuthorityFactEnvelopeV1 result)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);
        if (command.Owner != OwnerSliceId.S1 || result.Owner != OwnerSliceId.S1 ||
            command.PayloadSchema != CommandSchema || result.PayloadSchema != ResultSchema ||
            command.ThreadScope is not null || result.ThreadScope is not null ||
            command.Position.Session != result.Position.Session || command.Position.Sequence >= result.Position.Sequence ||
            command.PayloadHash != AuthorityPayloadHashV1.Compute(
                CommandRegistration.SchemaToken, CommandRegistration.Schema, command.PayloadBytes) ||
            result.PayloadHash != AuthorityPayloadHashV1.Compute(
                ResultRegistration.SchemaToken, ResultRegistration.Schema, result.PayloadBytes) ||
            !SessionLifecyclePayloadV1Codec.TryDecodeCommand(command.PayloadMemory, out var commandOuter) ||
            !SessionLifecycleBodyCodecsV1.TryDecodeCommand(commandOuter!.BodyBytes.ToArray(), out var commandBody) ||
            !SessionLifecyclePayloadV1Codec.TryDecodeFact(result.PayloadMemory, out var resultOuter) ||
            !SessionLifecycleBodyCodecsV1.TryDecodeFact(resultOuter!.BodyBytes.ToArray(), out var resultBody) ||
            commandOuter.Session != command.Position.Session || resultOuter.Session != result.Position.Session ||
            commandOuter.ExpectedAuthority != resultOuter.ExpectedAuthority ||
            resultBody!.CommandPosition != command.Position || resultBody.OperationId != commandBody!.OperationId ||
            resultBody.CommandExpectedLifecycleFact != commandBody.ExpectedLifecycleFact ||
            command.FactId != SessionLifecycleCommandFactIdV1.Derive(command.Position.Session, commandBody.OperationId) ||
            result.FactId != SessionLifecycleResultFactIdV1.Derive(command.Position))
            throw new ArgumentException("Lifecycle success requires one exact ordered command/result envelope pair.");
    }
}

internal static class SessionLifecycleAdmissionCoordinatorV1
{
    private const int MaximumAttempts = 8;
    private const uint MaximumAppendBytes = 100_000;
    private static readonly SessionLifecycleCommandPayloadRegistrationV1 CommandRegistration = new();
    private static readonly SessionLifecycleFactPayloadRegistrationV1 FactRegistration = new();

    internal static async ValueTask<SessionLifecycleAdmissionResultV1> AdmitAsync(
        IAuthorityJournalV1 journal,
        SessionLifecycleCommandV1 command,
        CorrelationEnvelopeV1 correlation,
        UtcInstant observedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(command);
        if (!correlation.IsValid) throw new ArgumentException("A valid correlation envelope is required.", nameof(correlation));
        if (!SessionLifecycleBodyCodecsV1.TryDecodeCommand(command.BodyBytes.ToArray(), out var body))
            return Rejected("invalid-lifecycle-command");
        var commandId = SessionLifecycleCommandFactIdV1.Derive(command.Session, body!.OperationId);
        var exactCorrelation = new CorrelationEnvelopeV1(
            correlation.TenantId, correlation.PrincipalId, correlation.SessionId, correlation.ThreadId,
            correlation.ParticipantId, body.OperationId);
        var commandProposal = Proposal(commandId, CommandRegistration, SessionLifecyclePayloadV1Codec.Encode(command),
            SessionLifecyclePayloadV1Codec.ComputeIntegrityHash(command), exactCorrelation, observedAt);
        var commandWasCommittedHere = false;

        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            var read = await SessionLifecycleSnapshotReaderV1.ReadAsync(
                journal, command.Session, commandId, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (read is SessionLifecycleSnapshotReadResultV1.OutcomeUnknown unknown)
                return Unknown(commandId, unknown.SafeCode);
            var verified = (SessionLifecycleSnapshotReadResultV1.Verified)read;
            if (verified.Fold is SessionLifecycleJournalFoldResultV1.GenerationReplaced replaced)
                return new SessionLifecycleAdmissionResultV1.GenerationReplaced(replaced.Replacement);
            if (verified.Fold is SessionLifecycleJournalFoldResultV1.InvalidHistory invalid)
                return new SessionLifecycleAdmissionResultV1.InvalidHistory(invalid.SafeCode, invalid.LastVerifiedPosition);
            var current = (SessionLifecycleJournalFoldResultV1.Current)verified.Fold;
            if (current.TargetResultFact is { } existingResult)
            {
                var existingCommand = current.TargetCommandFact;
                if (existingCommand is null || !Matches(existingCommand, commandProposal, command.Session))
                    return new SessionLifecycleAdmissionResultV1.ContradictoryDuplicate(commandId);
                return commandWasCommittedHere
                    ? new SessionLifecycleAdmissionResultV1.Committed(existingCommand, existingResult)
                    : new SessionLifecycleAdmissionResultV1.AlreadyCommitted(existingCommand, existingResult);
            }

            var pending = current.PendingCommands.SingleOrDefault(item => item.Envelope.FactId == commandId);
            if (pending is null)
            {
                var appendCommand = await AppendAsync(journal, command.Session, current.SnapshotThrough, commandProposal, cancellationToken)
                    .ConfigureAwait(false);
                switch (appendCommand)
                {
                    case AppendAuthorityResultV1.Committed committed when committed.Envelopes.Count == 1 &&
                        Matches(committed.Envelopes[0], commandProposal, command.Session):
                        commandWasCommittedHere = true;
                        continue;
                    case AppendAuthorityResultV1.AlreadyCommitted existing when existing.Envelopes.Count == 1 &&
                        Matches(existing.Envelopes[0], commandProposal, command.Session):
                        continue;
                    case AppendAuthorityResultV1.SessionConflict:
                        continue;
                    default:
                        return MapAppend(appendCommand, commandId);
                }
            }
            if (!Matches(pending.Envelope, commandProposal, command.Session))
                return new SessionLifecycleAdmissionResultV1.ContradictoryDuplicate(commandId);

            var factBody = Reduce(current, pending);
            if (factBody is null)
                return new SessionLifecycleAdmissionResultV1.InvalidHistory(
                    new BoundedAscii("invalid-lifecycle-predecessor"), current.SnapshotThrough);
            var fact = new SessionLifecycleFactV1(command.Session, pending.Command.ExpectedAuthority,
                SessionLifecycleBodyCodecsV1.Encode(factBody));
            var resultId = SessionLifecycleResultFactIdV1.Derive(pending.Envelope.Position);
            var resultProposal = Proposal(resultId, FactRegistration, SessionLifecyclePayloadV1Codec.Encode(fact),
                SessionLifecyclePayloadV1Codec.ComputeIntegrityHash(fact), pending.Envelope.Correlation, pending.Envelope.ObservedAt);
            var appendResult = await AppendAsync(journal, command.Session, current.SnapshotThrough, resultProposal, cancellationToken)
                .ConfigureAwait(false);
            switch (appendResult)
            {
                case AppendAuthorityResultV1.Committed committed when committed.Envelopes.Count == 1 &&
                    Matches(committed.Envelopes[0], resultProposal, command.Session):
                    return new SessionLifecycleAdmissionResultV1.Committed(pending.Envelope, committed.Envelopes[0]);
                case AppendAuthorityResultV1.AlreadyCommitted existing when existing.Envelopes.Count == 1 &&
                    Matches(existing.Envelopes[0], resultProposal, command.Session):
                    return new SessionLifecycleAdmissionResultV1.AlreadyCommitted(pending.Envelope, existing.Envelopes[0]);
                case AppendAuthorityResultV1.SessionConflict:
                    continue;
                default:
                    return MapAppend(appendResult, resultId);
            }
        }
        var finalRead = await SessionLifecycleSnapshotReaderV1.ReadAsync(
            journal, command.Session, commandId, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (finalRead is SessionLifecycleSnapshotReadResultV1.OutcomeUnknown finalUnknown)
            return Unknown(commandId, finalUnknown.SafeCode);
        var finalVerified = (SessionLifecycleSnapshotReadResultV1.Verified)finalRead;
        if (finalVerified.Fold is SessionLifecycleJournalFoldResultV1.GenerationReplaced finalReplaced)
            return new SessionLifecycleAdmissionResultV1.GenerationReplaced(finalReplaced.Replacement);
        if (finalVerified.Fold is SessionLifecycleJournalFoldResultV1.InvalidHistory finalInvalid)
            return new SessionLifecycleAdmissionResultV1.InvalidHistory(finalInvalid.SafeCode, finalInvalid.LastVerifiedPosition);
        var finalCurrent = (SessionLifecycleJournalFoldResultV1.Current)finalVerified.Fold;
        if (finalCurrent.TargetResultFact is { } finalResult && finalCurrent.TargetCommandFact is { } finalCommand)
        {
            if (!Matches(finalCommand, commandProposal, command.Session))
                return new SessionLifecycleAdmissionResultV1.ContradictoryDuplicate(commandId);
            return commandWasCommittedHere
                ? new SessionLifecycleAdmissionResultV1.Committed(finalCommand, finalResult)
                : new SessionLifecycleAdmissionResultV1.AlreadyCommitted(finalCommand, finalResult);
        }
        return new SessionLifecycleAdmissionResultV1.RetryRequired(finalVerified.SnapshotThrough);
    }

    private static SessionLifecycleFactBodyV1? Reduce(
        SessionLifecycleJournalFoldResultV1.Current current,
        PendingSessionLifecycleCommandV1 pending)
    {
        SessionLifecycleOutcomeV1 outcome;
        SessionLifecycleSnapshotBodyV1 snapshot;
        BoundedAscii? safeCode = null;
        if (pending.Body.ExpectedLifecycleFact != current.PreviousLifecycleFact)
        {
            if (current.Snapshot is null) return null;
            outcome = SessionLifecycleOutcomeV1.Rejected;
            snapshot = current.Snapshot;
            safeCode = new BoundedAscii("lifecycle-predecessor-conflict");
        }
        else if (!SessionLifecycleJournalFoldV1.Matches(pending.Command.ExpectedAuthority, current.Authority))
        {
            if (current.Snapshot is null) return null;
            outcome = SessionLifecycleOutcomeV1.Rejected;
            snapshot = current.Snapshot;
            safeCode = new BoundedAscii("authority-vector-stale");
        }
        else
        {
            switch (SessionLifecycleReducerV1.Apply(current.Snapshot, pending.Body))
            {
                case SessionLifecycleReductionV1.Applied applied:
                    outcome = SessionLifecycleOutcomeV1.Applied; snapshot = applied.Snapshot; break;
                case SessionLifecycleReductionV1.Idempotent idempotent:
                    outcome = SessionLifecycleOutcomeV1.Idempotent; snapshot = idempotent.Snapshot; break;
                case SessionLifecycleReductionV1.Rejected rejected:
                    outcome = SessionLifecycleOutcomeV1.Rejected; snapshot = rejected.Snapshot; safeCode = rejected.SafeCode; break;
                default:
                    return null;
            }
        }
        return new SessionLifecycleFactBodyV1(pending.Body.OperationId, pending.Envelope.Position,
            pending.Body.ExpectedLifecycleFact, current.PreviousLifecycleFact, outcome, snapshot, safeCode);
    }

    private static ProposedAuthorityFactV1 Proposal(
        JournalFactId factId,
        AuthorityPayloadRegistrationV1 registration,
        byte[] payload,
        Hash256 hash,
        CorrelationEnvelopeV1 correlation,
        UtcInstant observedAt) =>
        new(factId, null, OwnerSliceId.S1, registration.Schema, payload, hash, correlation, observedAt);

    private static async ValueTask<AppendAuthorityResultV1> AppendAsync(
        IAuthorityJournalV1 journal,
        SessionAuthorityStampV1 session,
        long expectedHead,
        ProposedAuthorityFactV1 proposal,
        CancellationToken cancellationToken)
    {
        try
        {
            return await journal.AppendAsync(new AppendAuthorityBatchV1(
                session, expectedHead, [], [proposal], MaximumAppendBytes), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new AppendAuthorityResultV1.OutcomeUnknown(
                proposal.Correlation.OperationId ?? throw new InvalidOperationException("Lifecycle proposals require an operation correlation."));
        }
    }

    private static bool Matches(AuthorityFactEnvelopeV1 envelope, ProposedAuthorityFactV1 proposal, SessionAuthorityStampV1 session) =>
        envelope.Position.Session == session && envelope.FactId == proposal.FactId && envelope.ThreadScope is null &&
        envelope.Owner == proposal.Owner && envelope.PayloadSchema == proposal.PayloadSchema &&
        envelope.PayloadHash == proposal.PayloadHash && envelope.Payload.SequenceEqual(proposal.Payload);

    private static SessionLifecycleAdmissionResultV1 MapAppend(AppendAuthorityResultV1 result, JournalFactId factId) => result switch
    {
        AppendAuthorityResultV1.ContradictoryDuplicate => new SessionLifecycleAdmissionResultV1.ContradictoryDuplicate(factId),
        AppendAuthorityResultV1.InvalidPayload invalid => new SessionLifecycleAdmissionResultV1.Rejected(invalid.SafeCode),
        AppendAuthorityResultV1.UnknownSchema => Rejected("unknown-schema"),
        AppendAuthorityResultV1.CapacityRefused => Rejected("capacity-refused"),
        AppendAuthorityResultV1.StoreUnavailable unavailable => Unknown(factId, unavailable.SafeCode),
        AppendAuthorityResultV1.OutcomeUnknown => Unknown(factId, new BoundedAscii("append-outcome-unknown")),
        _ => Unknown(factId, new BoundedAscii("unexpected-append-result")),
    };

    private static SessionLifecycleAdmissionResultV1.Rejected Rejected(string code) => new(new BoundedAscii(code));
    private static SessionLifecycleAdmissionResultV1.OutcomeUnknown Unknown(JournalFactId factId, string code) =>
        new(factId, new BoundedAscii(code));
    private static SessionLifecycleAdmissionResultV1.OutcomeUnknown Unknown(JournalFactId factId, BoundedAscii code) =>
        new(factId, code);
}
