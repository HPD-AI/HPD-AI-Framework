namespace HPD.Agent.Authority;

/// <summary>Represents a verified current S9 capture-grant query without converting absence into authorization.</summary>
public abstract record CaptureGrantReadResultV1
{
    private CaptureGrantReadResultV1() { }
    /// <summary>Contains a currently active grant projection.</summary>
    public sealed record Active : CaptureGrantReadResultV1
    {
        internal Active(CaptureGrantProofV1 proof) => Proof = proof ?? throw new ArgumentNullException(nameof(proof));
        /// <summary>Gets the current fact-derived grant proof.</summary>
        public CaptureGrantProofV1 Proof { get; }
    }
    /// <summary>Reports that no matching grant was observed through a verified snapshot.</summary>
    public sealed record NotObserved : CaptureGrantReadResultV1
    {
        internal NotObserved(long snapshotThrough) { if (snapshotThrough < 0) throw new ArgumentOutOfRangeException(nameof(snapshotThrough)); SnapshotThrough = snapshotThrough; }
        /// <summary>Gets the verified session head; this does not prove global nonexistence.</summary>
        public long SnapshotThrough { get; }
    }
    /// <summary>Reports an admitted rejection, expiry, or later authority fence.</summary>
    public sealed record Inactive : CaptureGrantReadResultV1
    {
        internal Inactive(CaptureGrantStateV1 state, JournalPositionV1 position)
        { if (state == CaptureGrantStateV1.Active || !Enum.IsDefined(state) || !position.IsValid) throw new ArgumentException("An inactive result needs a nonactive state and fact position."); State = state; Position = position; }
        /// <summary>Gets the exact nonactive disposition.</summary>
        public CaptureGrantStateV1 State { get; }
        /// <summary>Gets the source or result fact position establishing the disposition.</summary>
        public JournalPositionV1 Position { get; }
    }
    /// <summary>Reports that the bounded fold could not establish a trustworthy current result.</summary>
    public sealed record OutcomeUnknown : CaptureGrantReadResultV1
    {
        internal OutcomeUnknown(BoundedAscii safeCode, long lastVerifiedPosition)
        { if (!safeCode.IsValid || lastVerifiedPosition < 0) throw new ArgumentException("Unknown requires a safe code and verified frontier."); SafeCode = safeCode; LastVerifiedPosition = lastVerifiedPosition; }
        /// <summary>Gets the bounded nonsecret diagnostic code.</summary>
        public BoundedAscii SafeCode { get; }
        /// <summary>Gets the last position known to be valid.</summary>
        public long LastVerifiedPosition { get; }
    }
}

internal abstract record CaptureGrantAdmissionResultV1
{
    private CaptureGrantAdmissionResultV1() { }
    internal sealed record Granted(CaptureGrantProofV1 Proof) : CaptureGrantAdmissionResultV1;
    internal sealed record AlreadyGranted(CaptureGrantProofV1 Proof) : CaptureGrantAdmissionResultV1;
    internal sealed record Rejected(JournalPositionV1 ResultPosition) : CaptureGrantAdmissionResultV1;
    internal sealed record ContradictoryDuplicate(JournalFactId FactId) : CaptureGrantAdmissionResultV1;
    internal sealed record StaleAuthority(long SnapshotThrough) : CaptureGrantAdmissionResultV1;
    internal sealed record RetryRequired(long ObservedHead) : CaptureGrantAdmissionResultV1;
    internal sealed record OutcomeUnknown(JournalFactId FactId, BoundedAscii SafeCode) : CaptureGrantAdmissionResultV1;
}

internal static class CaptureGrantAdmissionV1
{
    private const ushort ReadItems = AppendAuthorityBatchV1.MaximumItems;
    private const uint ReadBytes = ProposedAuthorityFactV1.MaximumPayloadBytes;
    private static readonly CaptureAuthorizationPayloadRegistrationV1 CommandRegistration = new();
    private static readonly CaptureGrantCommittedPayloadRegistrationV1 FactRegistration = new();

    internal static async ValueTask<CaptureGrantAdmissionResultV1> AuthorizeAsync(IAuthorityJournalV1 journal,
        CaptureAuthorizationCommandV1 command, CorrelationEnvelopeV1 correlation, UtcInstant observedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal); ArgumentNullException.ThrowIfNull(command);
        if (!correlation.IsValid || correlation.OperationId != command.Body.OperationId)
            throw new ArgumentException("Correlation must bind the capture authorization operation.", nameof(correlation));
        var commandBytes = CaptureGrantCodecsV1.EncodeCommand(command);
        var commandId = CaptureGrantFactIdsV1.Command(command.Session, command.Body.OperationId);
        var commandProposal = Proposal(commandId, CommandRegistration, commandBytes,
            CaptureGrantCodecsV1.CommandHash(command), correlation, observedAt);
        var committedHere = false;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var snapshot = await ReadSnapshotAsync(journal, command.Session, cancellationToken).ConfigureAwait(false);
            if (snapshot is not Snapshot.Verified verified) return Unknown(commandId, "capture-snapshot-unknown");
            var existing = verified.Commands.SingleOrDefault(item => item.Command.Body.OperationId == command.Body.OperationId);
            if (existing is not null)
            {
                if (!Matches(existing.Envelope, commandProposal, command.Session)) return new CaptureGrantAdmissionResultV1.ContradictoryDuplicate(commandId);
                var result = verified.Results.SingleOrDefault(item => item.Fact.SourcePosition == existing.Envelope.Position);
                if (result is not null) return MapCompleted(existing.Command, result, committedHere);
            }
            else
            {
                if (!MatchesCurrent(command.Authority, verified.Authority)) return new CaptureGrantAdmissionResultV1.StaleAuthority(verified.Head);
                var append = await AppendAsync(journal, command.Session, verified.Head, commandProposal, cancellationToken).ConfigureAwait(false);
                switch (append)
                {
                    case AppendAuthorityResultV1.Committed: committedHere = true; continue;
                    case AppendAuthorityResultV1.AlreadyCommitted: continue;
                    case AppendAuthorityResultV1.SessionConflict: continue;
                    default: return MapAppend(append, commandId);
                }
            }
            var current = existing ?? verified.Commands.Single(item => item.Envelope.FactId == commandId);
            var disposition = command.Body.ExpiresAt.NanosecondsSinceUnixEpoch > observedAt.NanosecondsSinceUnixEpoch
                ? CaptureGrantCommitDispositionV1.Granted : CaptureGrantCommitDispositionV1.Rejected;
            var fact = new CaptureGrantCommittedV1(command.Body.OperationId, current.Envelope.Position, command.Authority, disposition);
            var factId = CaptureGrantFactIdsV1.Result(current.Envelope.Position);
            var proposal = Proposal(factId, FactRegistration, CaptureGrantCodecsV1.EncodeFact(fact),
                CaptureGrantCodecsV1.FactHash(fact), correlation, observedAt);
            var appended = await AppendAsync(journal, command.Session, verified.Head, proposal, cancellationToken).ConfigureAwait(false);
            switch (appended)
            {
                case AppendAuthorityResultV1.Committed committed when committed.Envelopes.Count == 1:
                    return MapCompleted(command, new ResultEntry(committed.Envelopes[0], fact), committedHere);
                case AppendAuthorityResultV1.AlreadyCommitted: continue;
                case AppendAuthorityResultV1.SessionConflict: continue;
                default: return MapAppend(appended, factId);
            }
        }
        return new CaptureGrantAdmissionResultV1.RetryRequired((await ReadSnapshotAsync(journal, command.Session, cancellationToken).ConfigureAwait(false) as Snapshot.Verified)?.Head ?? 0);
    }

    /// <summary>Reads one grant from a bounded verified session snapshot and revalidates its relevant authority axes and expiry.</summary>
    internal static async ValueTask<CaptureGrantReadResultV1> ReadCurrentAsync(IAuthorityJournalV1 journal,
        SessionAuthorityStampV1 session, CaptureGrantId grantId, UtcInstant observedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (!session.IsValid || !grantId.IsValid) throw new ArgumentException("A session and capture grant are required.");
        var snapshot = await ReadSnapshotAsync(journal, session, cancellationToken).ConfigureAwait(false);
        if (snapshot is not Snapshot.Verified verified) return new CaptureGrantReadResultV1.OutcomeUnknown(new BoundedAscii("capture-snapshot-unknown"), 0);
        var commands = verified.Commands.Where(item => item.Command.Body.GrantId == grantId).ToArray();
        if (commands.Length == 0) return new CaptureGrantReadResultV1.NotObserved(verified.Head);
        if (commands.Length != 1) return new CaptureGrantReadResultV1.OutcomeUnknown(new BoundedAscii("capture-grant-duplicate"), verified.Head);
        var command = commands[0]; var results = verified.Results.Where(item => item.Fact.SourcePosition == command.Envelope.Position).ToArray();
        if (results.Length != 1) return new CaptureGrantReadResultV1.OutcomeUnknown(new BoundedAscii("capture-result-open"), verified.Head);
        var result = results[0];
        if (result.Fact.OperationId != command.Command.Body.OperationId || result.Fact.Authority != command.Command.Authority)
            return new CaptureGrantReadResultV1.OutcomeUnknown(new BoundedAscii("capture-result-contradiction"), verified.Head);
        if (result.Fact.Disposition == CaptureGrantCommitDispositionV1.Rejected)
            return new CaptureGrantReadResultV1.Inactive(
                command.Command.Body.ExpiresAt.NanosecondsSinceUnixEpoch <= observedAt.NanosecondsSinceUnixEpoch
                    ? CaptureGrantStateV1.Expired : CaptureGrantStateV1.Revoked, result.Envelope.Position);
        if (!MatchesCurrent(command.Command.Authority, verified.Authority))
            return new CaptureGrantReadResultV1.Inactive(CaptureGrantStateV1.Revoked, result.Envelope.Position);
        if (command.Command.Body.ExpiresAt.NanosecondsSinceUnixEpoch <= observedAt.NanosecondsSinceUnixEpoch)
            return new CaptureGrantReadResultV1.Inactive(CaptureGrantStateV1.Expired, result.Envelope.Position);
        return new CaptureGrantReadResultV1.Active(new CaptureGrantProofV1(command.Command.Body.GrantId,
            command.Command.Body.AuthorizationId, result.Envelope.Position, command.Command.Authority,
            command.Command.Body.ScopeHash, command.Command.Body.LimitsHash, CaptureGrantStateV1.Active,
            command.Command.Body.ExpiresAt));
    }

    private static CaptureGrantAdmissionResultV1 MapCompleted(CaptureAuthorizationCommandV1 command, ResultEntry result, bool committedHere)
    {
        if (result.Fact.Disposition == CaptureGrantCommitDispositionV1.Rejected) return new CaptureGrantAdmissionResultV1.Rejected(result.Envelope.Position);
        var proof = new CaptureGrantProofV1(command.Body.GrantId, command.Body.AuthorizationId, result.Envelope.Position,
            command.Authority, command.Body.ScopeHash, command.Body.LimitsHash, CaptureGrantStateV1.Active, command.Body.ExpiresAt);
        return committedHere ? new CaptureGrantAdmissionResultV1.Granted(proof) : new CaptureGrantAdmissionResultV1.AlreadyGranted(proof);
    }

    private static ProposedAuthorityFactV1 Proposal(JournalFactId id, AuthorityPayloadRegistrationV1 registration,
        byte[] payload, Hash256 hash, CorrelationEnvelopeV1 correlation, UtcInstant observedAt) =>
        new(id, null, OwnerSliceId.S9, registration.Schema, payload, hash, correlation, observedAt);

    private static async ValueTask<AppendAuthorityResultV1> AppendAsync(IAuthorityJournalV1 journal, SessionAuthorityStampV1 session,
        long head, ProposedAuthorityFactV1 proposal, CancellationToken cancellationToken)
    {
        try { return await journal.AppendAsync(new AppendAuthorityBatchV1(session, head, [], [proposal], ReadBytes), cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return new AppendAuthorityResultV1.OutcomeUnknown(proposal.Correlation.OperationId!.Value); }
    }

    private static CaptureGrantAdmissionResultV1 MapAppend(AppendAuthorityResultV1 result, JournalFactId factId) => result switch
    {
        AppendAuthorityResultV1.ContradictoryDuplicate => new CaptureGrantAdmissionResultV1.ContradictoryDuplicate(factId),
        AppendAuthorityResultV1.SessionConflict conflict => new CaptureGrantAdmissionResultV1.RetryRequired(conflict.Actual),
        AppendAuthorityResultV1.StoreUnavailable unavailable => new CaptureGrantAdmissionResultV1.OutcomeUnknown(factId, unavailable.SafeCode),
        _ => Unknown(factId, "capture-append-unknown"),
    };
    private static CaptureGrantAdmissionResultV1.OutcomeUnknown Unknown(JournalFactId id, string code) => new(id, new BoundedAscii(code));
    private static bool MatchesCurrent(ExpectedAuthorityVectorV1 expected, CurrentAuthorityVectorSnapshotV1 current) =>
        expected.Session == current.Session && expected.Axes.All(entry => current.Axes.Any(actual => actual == entry));
    private static bool Matches(AuthorityFactEnvelopeV1 envelope, ProposedAuthorityFactV1 proposal, SessionAuthorityStampV1 session) =>
        envelope.Position.Session == session && envelope.FactId == proposal.FactId && envelope.Owner == proposal.Owner &&
        envelope.PayloadSchema == proposal.PayloadSchema && envelope.PayloadHash == proposal.PayloadHash &&
        envelope.Payload.SequenceEqual(proposal.Payload);

    private abstract record Snapshot
    {
        private Snapshot() { }
        internal sealed record Unknown : Snapshot;
        internal sealed record Verified(long Head, CurrentAuthorityVectorSnapshotV1 Authority,
            IReadOnlyList<CommandEntry> Commands, IReadOnlyList<ResultEntry> Results) : Snapshot;
    }
    private sealed record CommandEntry(AuthorityFactEnvelopeV1 Envelope, CaptureAuthorizationCommandV1 Command);
    private sealed record ResultEntry(AuthorityFactEnvelopeV1 Envelope, CaptureGrantCommittedV1 Fact);

    private static async ValueTask<Snapshot> ReadSnapshotAsync(IAuthorityJournalV1 journal, SessionAuthorityStampV1 session, CancellationToken cancellationToken)
    {
        var vector = AuthorityVectorReplayFoldV1.CreateAccumulator(session); var commands = new List<CommandEntry>(); var results = new List<ResultEntry>();
        long cursor = 0; long? through = null;
        while (true)
        {
            ReadAuthorityRangeResultV1 read;
            try { read = await journal.ReadAsync(new ReadAuthorityRangeV1(session, cursor, through ?? long.MaxValue, ReadItems, ReadBytes), cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { return new Snapshot.Unknown(); }
            if (read is not ReadAuthorityRangeResultV1.Batch batch || batch.AfterExclusive != cursor || (through is not null && batch.SnapshotThrough != through)) return new Snapshot.Unknown();
            through ??= batch.SnapshotThrough;
            foreach (var envelope in batch.Facts)
            {
                vector.Apply(envelope); cursor = envelope.Position.Sequence;
                if (envelope.PayloadSchema == CommandRegistration.Schema)
                { if (envelope.Owner != OwnerSliceId.S9 || !CaptureGrantCodecsV1.TryDecodeCommand(envelope.PayloadMemory, out var command) ||
                      envelope.FactId != CaptureGrantFactIdsV1.Command(session, command!.Body.OperationId)) return new Snapshot.Unknown(); commands.Add(new(envelope, command)); }
                else if (envelope.PayloadSchema == FactRegistration.Schema)
                { if (envelope.Owner != OwnerSliceId.S9 || !CaptureGrantCodecsV1.TryDecodeFact(envelope.PayloadMemory, out var fact) ||
                      envelope.FactId != CaptureGrantFactIdsV1.Result(fact!.SourcePosition)) return new Snapshot.Unknown(); results.Add(new(envelope, fact)); }
            }
            if (batch.HasMore) { if (batch.Facts.Count == 0) return new Snapshot.Unknown(); continue; }
            if (cursor != through) return new Snapshot.Unknown(); var folded = vector.Complete();
            return folded is AuthorityVectorReplayResultV1.Current current
                ? new Snapshot.Verified(through.Value, current.Snapshot, commands.AsReadOnly(), results.AsReadOnly()) : new Snapshot.Unknown();
        }
    }
}

internal static class CaptureGrantFactIdsV1
{
    internal static JournalFactId Command(SessionAuthorityStampV1 session, OperationId operation)
    {
        Span<byte> bytes = stackalloc byte[48]; session.RuntimeGenerationId.TryWriteBytes(bytes); session.LiveSessionId.TryWriteBytes(bytes[16..]); operation.TryWriteBytes(bytes[32..]);
        return SessionLifecycleFactIdDerivationV1.Derive("hpd-capture-command-fact-id-v1\0"u8, bytes);
    }
    internal static JournalFactId Result(JournalPositionV1 source)
    {
        Span<byte> bytes = stackalloc byte[40]; source.Session.RuntimeGenerationId.TryWriteBytes(bytes); source.Session.LiveSessionId.TryWriteBytes(bytes[16..]);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes[32..], source.Sequence);
        return SessionLifecycleFactIdDerivationV1.Derive("hpd-capture-result-fact-id-v1\0"u8, bytes);
    }
}
