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
        IReadOnlyList<PendingSessionLifecycleCommandV1> PendingCommands,
        AuthorityFactEnvelopeV1? TargetCommandFact = null,
        AuthorityFactEnvelopeV1? TargetResultFact = null) : SessionLifecycleJournalFoldResultV1;

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
        ArgumentNullException.ThrowIfNull(facts);
        var accumulator = new Accumulator(session);
        foreach (var envelope in facts)
            accumulator.Apply(envelope);
        return accumulator.Complete();
    }

    internal static Accumulator CreateAccumulator(SessionAuthorityStampV1 session, JournalFactId? targetCommandFactId = null) =>
        new(session, targetCommandFactId);

    internal sealed class Accumulator
    {
        private readonly SessionAuthorityStampV1 _session;
        private readonly JournalFactId? _targetCommandFactId;
        private readonly AuthorityVectorReplayFoldV1.AuthorityVectorReplayAccumulatorV1 _vector;
        private readonly Dictionary<long, PendingSessionLifecycleCommandV1> _commands = [];
        private SessionLifecycleSnapshotBodyV1? _snapshot;
        private JournalPositionV1? _previous;
        private long _expectedPosition = 1;
        private SessionLifecycleJournalFoldResultV1.InvalidHistory? _invalid;
        private AuthorityFactEnvelopeV1? _targetResultFact;
        private AuthorityFactEnvelopeV1? _targetCommandFact;
        private bool _targetCommandSeen;

        internal Accumulator(SessionAuthorityStampV1 session, JournalFactId? targetCommandFactId = null)
        {
            if (!session.IsValid) throw new ArgumentException("A valid session authority stamp is required.", nameof(session));
            if (targetCommandFactId is { IsValid: false })
                throw new ArgumentException("A present target command identity must be valid.", nameof(targetCommandFactId));
            _session = session;
            _targetCommandFactId = targetCommandFactId;
            _vector = AuthorityVectorReplayFoldV1.CreateAccumulator(session);
        }

        internal void Apply(AuthorityFactEnvelopeV1? envelope)
        {
            if (_invalid is not null) return;
            if (envelope is null || envelope.Position.Session != _session || envelope.Position.Sequence != _expectedPosition)
            {
                _invalid = Invalid("noncontiguous-history", _expectedPosition - 1);
                return;
            }
            _vector.Apply(envelope);
            var vectorResult = _vector.Complete();
            if (vectorResult is AuthorityVectorReplayResultV1.InvalidHistory invalid)
            {
                _invalid = Invalid("invalid-authority-history", invalid.LastPosition);
                return;
            }
            if (envelope.PayloadSchema == CommandSchema)
            {
                if (envelope.Owner != OwnerSliceId.S1 ||
                    !HasExactPayloadHash(envelope, CommandRegistration) ||
                    !SessionLifecyclePayloadV1Codec.TryDecodeCommand(envelope.PayloadMemory, out var command) ||
                    !SessionLifecycleBodyCodecsV1.TryDecodeCommand(command!.BodyBytes.ToArray(), out var body) ||
                    envelope.FactId != SessionLifecycleCommandFactIdV1.Derive(_session, body!.OperationId) ||
                    command.Session != _session || command.ExpectedAuthority.Session != _session ||
                    body.ExpectedLifecycleFact is { } expected && expected.Session != _session)
                    _invalid = Invalid("invalid-lifecycle-command", _expectedPosition - 1);
                else if (_commands.Values.Any(item => item.Envelope.FactId == envelope.FactId) ||
                         _targetCommandFactId == envelope.FactId && _targetCommandSeen)
                    _invalid = Invalid("duplicate-lifecycle-command", _expectedPosition - 1);
                else if (_commands.Count == MaximumPendingCommands)
                    _invalid = Invalid("pending-command-bound", _expectedPosition - 1);
                else
                {
                    _commands.Add(_expectedPosition, new(envelope, command, body));
                    if (_targetCommandFactId == envelope.FactId)
                    {
                        _targetCommandSeen = true;
                        _targetCommandFact = envelope;
                    }
                }
            }
            else if (envelope.PayloadSchema == FactSchema)
            {
                var failure = ApplyFact(envelope, vectorResult, _commands, ref _snapshot, ref _previous, out var resolvedCommandId);
                if (failure is not null) _invalid = Invalid(failure, _expectedPosition - 1);
                else if (_targetCommandFactId == resolvedCommandId) _targetResultFact = envelope;
            }
            else if (envelope.PayloadSchema.SchemaId == CommandSchema.SchemaId || envelope.PayloadSchema.SchemaId == FactSchema.SchemaId)
                _invalid = Invalid("unknown-lifecycle-version", _expectedPosition - 1);
            if (_invalid is null) _expectedPosition++;
        }

        internal SessionLifecycleJournalFoldResultV1 Complete()
        {
            if (_invalid is not null) return _invalid;
            var completedVector = _vector.Complete();
            if (completedVector is AuthorityVectorReplayResultV1.GenerationReplaced replaced)
                return new SessionLifecycleJournalFoldResultV1.GenerationReplaced(replaced.ReplacedBy, replaced.LastPosition);
            if (completedVector is not AuthorityVectorReplayResultV1.Current current)
                return Invalid("invalid-authority-history", _vector.LastVerifiedPosition);
            var pending = _commands.OrderBy(static pair => pair.Key).Select(static pair => pair.Value).ToArray();
            return new SessionLifecycleJournalFoldResultV1.Current(
                _expectedPosition - 1, current.Snapshot, _previous, _snapshot, Array.AsReadOnly(pending),
                _targetCommandFact, _targetResultFact);
        }

        internal long LastVerifiedPosition => _expectedPosition - 1;
    }

    private static string? ApplyFact(
        AuthorityFactEnvelopeV1 envelope,
        AuthorityVectorReplayResultV1 vectorResult,
        IDictionary<long, PendingSessionLifecycleCommandV1> commands,
        ref SessionLifecycleSnapshotBodyV1? snapshot,
        ref JournalPositionV1? previous,
        out JournalFactId resolvedCommandId)
    {
        resolvedCommandId = default;
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
        resolvedCommandId = pending.Envelope.FactId;
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
