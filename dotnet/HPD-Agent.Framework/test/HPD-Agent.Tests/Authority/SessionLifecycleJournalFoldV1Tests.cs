using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class SessionLifecycleJournalFoldV1Tests
{
    [Fact]
    public void EmptyAndPendingHistory_AreExplicit()
    {
        var fixture = new Fixture();
        var empty = Assert.IsType<SessionLifecycleJournalFoldResultV1.Current>(
            SessionLifecycleJournalFoldV1.Fold(fixture.Session, []));
        Assert.Null(empty.Snapshot);
        Assert.Empty(empty.PendingCommands);

        var command = fixture.Command(new SessionLifecycleCommandBodyV1.ReserveStarting(
            fixture.Operation(), Hash256.Compute("request"u8)), 1);
        var pending = Assert.IsType<SessionLifecycleJournalFoldResultV1.Current>(
            SessionLifecycleJournalFoldV1.Fold(fixture.Session, [command]));
        Assert.Single(pending.PendingCommands);
        Assert.Null(pending.PreviousLifecycleFact);
    }

    [Fact]
    public void AppliedFact_ReplaysExactStartingSnapshotAndResolvesCommand()
    {
        var fixture = new Fixture();
        var body = new SessionLifecycleCommandBodyV1.ReserveStarting(fixture.Operation(), Hash256.Compute("request"u8));
        var command = fixture.Command(body, 1);
        var snapshot = Assert.IsType<SessionLifecycleReductionV1.Applied>(SessionLifecycleReducerV1.Apply(null, body)).Snapshot;
        var fact = fixture.Fact(command, body, null, SessionLifecycleOutcomeV1.Applied, snapshot, null, 2);

        var result = Assert.IsType<SessionLifecycleJournalFoldResultV1.Current>(
            SessionLifecycleJournalFoldV1.Fold(fixture.Session, [command, fact]));
        Assert.Equal(snapshot, result.Snapshot);
        Assert.Equal(fact.Position, result.PreviousLifecycleFact);
        Assert.Empty(result.PendingCommands);
    }

    [Fact]
    public void ConcurrentPredecessorLoser_MustCarryDurableConflictSnapshot()
    {
        var fixture = new Fixture();
        var reserveBody = new SessionLifecycleCommandBodyV1.ReserveStarting(fixture.Operation(), Hash256.Compute("request"u8));
        var reserve = fixture.Command(reserveBody, 1);
        var starting = Assert.IsType<SessionLifecycleReductionV1.Applied>(SessionLifecycleReducerV1.Apply(null, reserveBody)).Snapshot;
        var started = fixture.Fact(reserve, reserveBody, null, SessionLifecycleOutcomeV1.Applied, starting, null, 2);
        var readyBody = new SessionLifecycleCommandBodyV1.PublishReady(fixture.Operation(), started.Position, SessionAvailabilityWireV1.Available);
        var drainBody = new SessionLifecycleCommandBodyV1.BeginDrain(fixture.Operation(), started.Position);
        var ready = fixture.Command(readyBody, 3);
        var drain = fixture.Command(drainBody, 4);
        var active = Assert.IsType<SessionLifecycleReductionV1.Applied>(SessionLifecycleReducerV1.Apply(starting, readyBody)).Snapshot;
        var readyFact = fixture.Fact(ready, readyBody, started.Position, SessionLifecycleOutcomeV1.Applied, active, null, 5);
        var losingFact = fixture.Fact(drain, drainBody, readyFact.Position, SessionLifecycleOutcomeV1.Rejected,
            active, new BoundedAscii("lifecycle-predecessor-conflict"), 6);

        var result = Assert.IsType<SessionLifecycleJournalFoldResultV1.Current>(SessionLifecycleJournalFoldV1.Fold(
            fixture.Session, [reserve, started, ready, drain, readyFact, losingFact]));
        Assert.Equal(active, result.Snapshot);
        Assert.Equal(losingFact.Position, result.PreviousLifecycleFact);
        Assert.Empty(result.PendingCommands);
    }

    [Fact]
    public void WrongIdentityOrReducerResult_QuarantinesHistory()
    {
        var fixture = new Fixture();
        var body = new SessionLifecycleCommandBodyV1.ReserveStarting(fixture.Operation(), Hash256.Compute("request"u8));
        var command = fixture.Command(body, 1);
        var wrongIdentity = fixture.Copy(command, JournalFactId.Create());
        Assert.Equal("invalid-lifecycle-command", Assert.IsType<SessionLifecycleJournalFoldResultV1.InvalidHistory>(
            SessionLifecycleJournalFoldV1.Fold(fixture.Session, [wrongIdentity])).SafeCode.ToString());

        var starting = Assert.IsType<SessionLifecycleReductionV1.Applied>(SessionLifecycleReducerV1.Apply(null, body)).Snapshot;
        var wrongFact = fixture.Fact(command, body, null, SessionLifecycleOutcomeV1.Idempotent, starting, null, 2);
        Assert.Equal("lifecycle-reduction-mismatch", Assert.IsType<SessionLifecycleJournalFoldResultV1.InvalidHistory>(
            SessionLifecycleJournalFoldV1.Fold(fixture.Session, [command, wrongFact])).SafeCode.ToString());
    }

    [Fact]
    public void WrongVersionHashAndCrossSessionPredecessor_Quarantine()
    {
        var fixture = new Fixture();
        var reserve = fixture.Command(new SessionLifecycleCommandBodyV1.ReserveStarting(
            fixture.Operation(), Hash256.Compute("request"u8)), 1);
        var wrongVersion = fixture.Copy(reserve, payloadSchema: new SchemaReferenceV1(
            reserve.PayloadSchema.SchemaId, reserve.PayloadSchema.Major, checked((ushort)(reserve.PayloadSchema.Minor + 1))));
        Assert.Equal("unknown-lifecycle-version", Assert.IsType<SessionLifecycleJournalFoldResultV1.InvalidHistory>(
            SessionLifecycleJournalFoldV1.Fold(fixture.Session, [wrongVersion])).SafeCode.ToString());

        var wrongHash = fixture.Copy(reserve, payloadHash: Hash256.Compute("wrong"u8));
        Assert.Equal("invalid-lifecycle-command", Assert.IsType<SessionLifecycleJournalFoldResultV1.InvalidHistory>(
            SessionLifecycleJournalFoldV1.Fold(fixture.Session, [wrongHash])).SafeCode.ToString());

        var other = new JournalPositionV1(new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()), 1);
        var crossSession = fixture.Command(new SessionLifecycleCommandBodyV1.BeginDrain(fixture.Operation(), other), 1);
        Assert.Equal("invalid-lifecycle-command", Assert.IsType<SessionLifecycleJournalFoldResultV1.InvalidHistory>(
            SessionLifecycleJournalFoldV1.Fold(fixture.Session, [crossSession])).SafeCode.ToString());
    }

    [Fact]
    public void PendingRecovery_IsPositionSortedAndFailsAtExactBoundPlusOne()
    {
        var fixture = new Fixture();
        var maximum = Enumerable.Range(1, SessionLifecycleJournalFoldV1.MaximumPendingCommands)
            .Select(sequence => fixture.Command(new SessionLifecycleCommandBodyV1.ReserveStarting(
                fixture.Operation(), Hash256.Compute(BitConverter.GetBytes(sequence))), sequence))
            .ToArray();
        var accepted = Assert.IsType<SessionLifecycleJournalFoldResultV1.Current>(
            SessionLifecycleJournalFoldV1.Fold(fixture.Session, maximum));
        Assert.Equal(SessionLifecycleJournalFoldV1.MaximumPendingCommands, accepted.PendingCommands.Count);
        Assert.Equal(Enumerable.Range(1, SessionLifecycleJournalFoldV1.MaximumPendingCommands).Select(static value => (long)value),
            accepted.PendingCommands.Select(static value => value.Envelope.Position.Sequence));

        var overflow = maximum.Append(fixture.Command(new SessionLifecycleCommandBodyV1.ReserveStarting(
            fixture.Operation(), Hash256.Compute("overflow"u8)), SessionLifecycleJournalFoldV1.MaximumPendingCommands + 1));
        Assert.Equal("pending-command-bound", Assert.IsType<SessionLifecycleJournalFoldResultV1.InvalidHistory>(
            SessionLifecycleJournalFoldV1.Fold(fixture.Session, overflow)).SafeCode.ToString());
    }

    private sealed class Fixture
    {
        private readonly CorrelationEnvelopeV1 _correlation = new(TenantId.Create());
        private readonly IntegrityEnvelopeV1 _integrity = new(1, 1, Hash256.Compute("integrity"u8), []);
        internal SessionAuthorityStampV1 Session { get; } = new(RuntimeGenerationId.Create(), LiveSessionId.Create());
        internal OperationId Operation() => OperationId.Create();

        internal AuthorityFactEnvelopeV1 Command(SessionLifecycleCommandBodyV1 body, long sequence)
        {
            var outer = new SessionLifecycleCommandV1(Session, ExpectedAuthorityVectorV1.Create(Session, []),
                SessionLifecycleBodyCodecsV1.Encode(body));
            var payload = SessionLifecyclePayloadV1Codec.Encode(outer);
            return Envelope(SessionLifecycleCommandFactIdV1.Derive(Session, body.OperationId), sequence,
                new SessionLifecycleCommandPayloadRegistrationV1().Schema, payload,
                SessionLifecyclePayloadV1Codec.ComputeIntegrityHash(outer));
        }

        internal AuthorityFactEnvelopeV1 Fact(
            AuthorityFactEnvelopeV1 command,
            SessionLifecycleCommandBodyV1 commandBody,
            JournalPositionV1? previous,
            SessionLifecycleOutcomeV1 outcome,
            SessionLifecycleSnapshotBodyV1 snapshot,
            BoundedAscii? safeCode,
            long sequence)
        {
            var body = new SessionLifecycleFactBodyV1(commandBody.OperationId, command.Position,
                commandBody.ExpectedLifecycleFact, previous, outcome, snapshot, safeCode);
            var outer = new SessionLifecycleFactV1(Session, ExpectedAuthorityVectorV1.Create(Session, []),
                SessionLifecycleBodyCodecsV1.Encode(body));
            var payload = SessionLifecyclePayloadV1Codec.Encode(outer);
            return Envelope(SessionLifecycleResultFactIdV1.Derive(command.Position), sequence,
                new SessionLifecycleFactPayloadRegistrationV1().Schema, payload,
                SessionLifecyclePayloadV1Codec.ComputeIntegrityHash(outer));
        }

        internal AuthorityFactEnvelopeV1 Copy(
            AuthorityFactEnvelopeV1 value,
            JournalFactId? factId = null,
            SchemaReferenceV1? payloadSchema = null,
            Hash256? payloadHash = null) => new(
            factId ?? value.FactId, value.Position, value.ThreadScope, value.Owner,
            payloadSchema ?? value.PayloadSchema, value.Payload.ToArray(), payloadHash ?? value.PayloadHash,
            value.Correlation, value.ObservedAt, value.AdmittedAt, value.Integrity);

        private AuthorityFactEnvelopeV1 Envelope(
            JournalFactId factId,
            long sequence,
            SchemaReferenceV1 schema,
            byte[] payload,
            Hash256 hash) => new(factId, new JournalPositionV1(Session, sequence), null, OwnerSliceId.S1,
                schema, payload, hash, _correlation, new UtcInstant(sequence), new UtcInstant(sequence), _integrity);
    }
}
