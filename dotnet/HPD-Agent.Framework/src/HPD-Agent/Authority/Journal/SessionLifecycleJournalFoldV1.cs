namespace HPD.Agent.Authority;

internal sealed record PendingSessionLifecycleCommandV1(
    AuthorityFactEnvelopeV1 Envelope,
    SessionLifecycleCommandV1 Command,
    SessionLifecycleCommandBodyV1 Body);

internal abstract record SessionLifecycleJournalFoldResultV1
{
    private SessionLifecycleJournalFoldResultV1() { }

    internal sealed record Current(
        long SnapshotThrough,
        CurrentAuthorityVectorSnapshotV1 Authority,
        JournalPositionV1? PreviousLifecycleFact,
        SessionLifecycleSnapshotBodyV1? Snapshot,
        IReadOnlyList<PendingSessionLifecycleCommandV1> PendingCommands) : SessionLifecycleJournalFoldResultV1;

    internal sealed record GenerationReplaced(RuntimeGenerationId Replacement, long LastPosition) : SessionLifecycleJournalFoldResultV1;

    internal sealed record InvalidHistory(BoundedAscii SafeCode, long LastVerifiedPosition) : SessionLifecycleJournalFoldResultV1;
}

internal static class SessionLifecycleJournalFoldV1
{
    internal const int MaximumPendingCommands = 256;
    private static readonly SessionLifecycleCommandPayloadRegistrationV1 CommandRegistration = new();
    private static readonly SessionLifecycleFactPayloadRegistrationV1 FactRegistration = new();
    private static readonly SchemaReferenceV1 CommandSchema = CommandRegistration.Schema;
    private static readonly SchemaReferenceV1 FactSchema = FactRegistration.Schema;

    internal static SessionLifecycleJournalFoldResultV1 Fold(
        SessionAuthorityStampV1 session,
        IEnumerable<AuthorityFactEnvelopeV1> facts)
    {
        if (!session.IsValid) throw new ArgumentException("A valid session authority stamp is required.", nameof(session));
        ArgumentNullException.ThrowIfNull(facts);
        var vector = AuthorityVectorReplayFoldV1.CreateAccumulator(session);
        var commands = new Dictionary<long, PendingSessionLifecycleCommandV1>();
        SessionLifecycleSnapshotBodyV1? snapshot = null;
        JournalPositionV1? previous = null;
        var expectedPosition = 1L;

        foreach (var envelope in facts)
        {
            if (envelope is null || envelope.Position.Session != session || envelope.Position.Sequence != expectedPosition)
                return Invalid("noncontiguous-history", expectedPosition - 1);
            vector.Apply(envelope);
            var vectorResult = vector.Complete();
            if (vectorResult is AuthorityVectorReplayResultV1.InvalidHistory invalid)
                return Invalid("invalid-authority-history", invalid.LastPosition);

            if (envelope.PayloadSchema == CommandSchema)
            {
                if (envelope.Owner != OwnerSliceId.S1 ||
                    !HasExactPayloadHash(envelope, CommandRegistration) ||
                    !SessionLifecyclePayloadV1Codec.TryDecodeCommand(envelope.PayloadMemory, out var command) ||
                    !SessionLifecycleBodyCodecsV1.TryDecodeCommand(command!.BodyBytes.ToArray(), out var body) ||
                    envelope.FactId != SessionLifecycleCommandFactIdV1.Derive(session, body!.OperationId) ||
                    command.Session != session || command.ExpectedAuthority.Session != session ||
                    body.ExpectedLifecycleFact is { } expected && expected.Session != session)
                    return Invalid("invalid-lifecycle-command", expectedPosition - 1);
                if (commands.Count == MaximumPendingCommands)
                    return Invalid("pending-command-bound", expectedPosition - 1);
                commands.Add(expectedPosition, new(envelope, command, body));
            }
            else if (envelope.PayloadSchema == FactSchema)
            {
                var failure = ApplyFact(envelope, vectorResult, commands, ref snapshot, ref previous);
                if (failure is not null) return Invalid(failure, expectedPosition - 1);
            }
            else if (envelope.PayloadSchema.SchemaId == CommandSchema.SchemaId || envelope.PayloadSchema.SchemaId == FactSchema.SchemaId)
                return Invalid("unknown-lifecycle-version", expectedPosition - 1);
            expectedPosition++;
        }

        var completedVector = vector.Complete();
        if (completedVector is AuthorityVectorReplayResultV1.GenerationReplaced replaced)
            return new SessionLifecycleJournalFoldResultV1.GenerationReplaced(replaced.ReplacedBy, replaced.LastPosition);
        if (completedVector is not AuthorityVectorReplayResultV1.Current current)
            return Invalid("invalid-authority-history", vector.LastVerifiedPosition);
        var pending = commands.OrderBy(static pair => pair.Key).Select(static pair => pair.Value).ToArray();
        return new SessionLifecycleJournalFoldResultV1.Current(
            expectedPosition - 1, current.Snapshot, previous, snapshot, Array.AsReadOnly(pending));
    }

    private static string? ApplyFact(
        AuthorityFactEnvelopeV1 envelope,
        AuthorityVectorReplayResultV1 vectorResult,
        IDictionary<long, PendingSessionLifecycleCommandV1> commands,
        ref SessionLifecycleSnapshotBodyV1? snapshot,
        ref JournalPositionV1? previous)
    {
        if (envelope.Owner != OwnerSliceId.S1 ||
            !HasExactPayloadHash(envelope, FactRegistration) ||
            !SessionLifecyclePayloadV1Codec.TryDecodeFact(envelope.PayloadMemory, out var fact) ||
            !SessionLifecycleBodyCodecsV1.TryDecodeFact(fact!.BodyBytes.ToArray(), out var body) ||
            envelope.FactId != SessionLifecycleResultFactIdV1.Derive(body!.CommandPosition) ||
            !commands.Remove(body.CommandPosition.Sequence, out var pending) ||
            pending.Envelope.Position != body.CommandPosition ||
            pending.Body.OperationId != body.OperationId ||
            pending.Body.ExpectedLifecycleFact != body.CommandExpectedLifecycleFact ||
            pending.Command.ExpectedAuthority != fact.ExpectedAuthority || pending.Command.Session != fact.Session ||
            body.PreviousLifecycleFact != previous)
            return "invalid-lifecycle-fact";

        SessionLifecycleOutcomeV1 outcome;
        SessionLifecycleSnapshotBodyV1 expectedSnapshot;
        BoundedAscii? safeCode = null;
        if (pending.Body.ExpectedLifecycleFact != previous)
        {
            if (snapshot is null) return "missing-lifecycle-predecessor";
            outcome = SessionLifecycleOutcomeV1.Rejected;
            expectedSnapshot = snapshot;
            safeCode = new BoundedAscii("lifecycle-predecessor-conflict");
        }
        else if (vectorResult is not AuthorityVectorReplayResultV1.Current current ||
                 !Matches(pending.Command.ExpectedAuthority, current.Snapshot))
        {
            if (snapshot is null) return "missing-lifecycle-predecessor";
            outcome = SessionLifecycleOutcomeV1.Rejected;
            expectedSnapshot = snapshot;
            safeCode = new BoundedAscii("authority-vector-stale");
        }
        else
        {
            var reduction = SessionLifecycleReducerV1.Apply(snapshot, pending.Body);
            switch (reduction)
            {
                case SessionLifecycleReductionV1.Applied applied:
                    outcome = SessionLifecycleOutcomeV1.Applied; expectedSnapshot = applied.Snapshot; break;
                case SessionLifecycleReductionV1.Idempotent idempotent:
                    outcome = SessionLifecycleOutcomeV1.Idempotent; expectedSnapshot = idempotent.Snapshot; break;
                case SessionLifecycleReductionV1.Rejected rejected:
                    outcome = SessionLifecycleOutcomeV1.Rejected; expectedSnapshot = rejected.Snapshot; safeCode = rejected.SafeCode; break;
                default:
                    return "invalid-lifecycle-predecessor";
            }
        }
        if (body.Outcome != outcome || body.Snapshot != expectedSnapshot || body.SafeCode != safeCode)
            return "lifecycle-reduction-mismatch";
        snapshot = expectedSnapshot;
        previous = envelope.Position;
        return null;
    }

    private static bool HasExactPayloadHash(AuthorityFactEnvelopeV1 envelope, AuthorityPayloadRegistrationV1 registration) =>
        envelope.PayloadHash == AuthorityPayloadHashV1.Compute(
            registration.SchemaToken, registration.Schema, envelope.PayloadBytes);

    internal static bool Matches(ExpectedAuthorityVectorV1 expected, CurrentAuthorityVectorSnapshotV1 current)
    {
        if (expected.Session != current.Session) return false;
        foreach (var required in expected.Axes)
        {
            var actual = current.Axes.FirstOrDefault(axis => axis.AxisId == required.AxisId);
            if (!actual.IsValid || actual != required) return false;
        }
        return true;
    }

    private static SessionLifecycleJournalFoldResultV1.InvalidHistory Invalid(string code, long position) =>
        new(new BoundedAscii(code), position);
}
