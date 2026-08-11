using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class SessionLifecycleSnapshotReaderV1Tests
{
    [Fact]
    public async Task Reader_PinsOneSnapshotAcrossBoundedPages()
    {
        var fixture = new Fixture();
        var body = new SessionLifecycleCommandBodyV1.ReserveStarting(fixture.Operation, Hash256.Compute("request"u8));
        var commandOuter = fixture.Command(body);
        var command = await fixture.AppendAsync(commandOuter, body.OperationId, 0);
        var snapshot = Assert.IsType<SessionLifecycleReductionV1.Applied>(SessionLifecycleReducerV1.Apply(null, body)).Snapshot;
        var factBody = new SessionLifecycleFactBodyV1(body.OperationId, command.Position, null, null,
            SessionLifecycleOutcomeV1.Applied, snapshot, null);
        var fact = await fixture.AppendAsync(fixture.Fact(commandOuter, factBody), body.OperationId, 1, command.Position);

        var read = Assert.IsType<SessionLifecycleSnapshotReadResultV1.Verified>(await SessionLifecycleSnapshotReaderV1.ReadAsync(
            fixture.Journal, fixture.Session, maximumFacts: 1));
        var current = Assert.IsType<SessionLifecycleJournalFoldResultV1.Current>(read.Fold);
        Assert.Equal(2, read.SnapshotThrough);
        Assert.Equal(fact.Position, current.PreviousLifecycleFact);
        Assert.Equal(snapshot, current.Snapshot);
        Assert.Empty(current.PendingCommands);
    }

    [Fact]
    public async Task Reader_ReportsStoreExceptionsWithoutInventingAbsence()
    {
        var result = Assert.IsType<SessionLifecycleSnapshotReadResultV1.OutcomeUnknown>(
            await SessionLifecycleSnapshotReaderV1.ReadAsync(new ThrowingJournal(),
                new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create())));
        Assert.Equal("store-exception", result.SafeCode.ToString());
        Assert.Equal(0, result.LastVerifiedPosition);
    }

    [Fact]
    public void ReadResults_RejectInvalidCoverageAndDiagnostics()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var authority = new CurrentAuthorityVectorSnapshotV1(session, [], 1);
        var fold = new SessionLifecycleJournalFoldResultV1.Current(1, authority, null, null, []);

        Assert.Throws<ArgumentException>(() => new SessionLifecycleSnapshotReadResultV1.Verified(fold, 2));
        Assert.Throws<ArgumentException>(() => new SessionLifecycleSnapshotReadResultV1.Verified(
            new SessionLifecycleJournalFoldResultV1.InvalidHistory(new BoundedAscii("fixture"), -1), 0));
        Assert.Throws<ArgumentException>(() => new SessionLifecycleSnapshotReadResultV1.OutcomeUnknown(default, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionLifecycleSnapshotReadResultV1.OutcomeUnknown(
            new BoundedAscii("fixture"), -1));
    }

    [Fact]
    public async Task Reader_RejectsHostileOverCountAndObservesCancellationAfterIgnoringPortReturns()
    {
        var fixture = new Fixture();
        var body = new SessionLifecycleCommandBodyV1.ReserveStarting(fixture.Operation, Hash256.Compute("request"u8));
        var commandOuter = fixture.Command(body);
        var command = await fixture.AppendAsync(commandOuter, body.OperationId, 0);
        var snapshot = Assert.IsType<SessionLifecycleReductionV1.Applied>(SessionLifecycleReducerV1.Apply(null, body)).Snapshot;
        var factBody = new SessionLifecycleFactBodyV1(body.OperationId, command.Position, null, null,
            SessionLifecycleOutcomeV1.Applied, snapshot, null);
        await fixture.AppendAsync(fixture.Fact(commandOuter, factBody), body.OperationId, 1, command.Position);

        var overCount = Assert.IsType<SessionLifecycleSnapshotReadResultV1.OutcomeUnknown>(
            await SessionLifecycleSnapshotReaderV1.ReadAsync(new OverCountJournal(fixture.Journal), fixture.Session, maximumFacts: 1));
        Assert.Equal("count-bound-violated", overCount.SafeCode.ToString());

        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await SessionLifecycleSnapshotReaderV1.ReadAsync(
            new CancelThenReturnJournal(fixture.Session, cancellation.Cancel), fixture.Session,
            cancellationToken: cancellation.Token));
    }

    private sealed class Fixture
    {
        private readonly CorrelationEnvelopeV1 _correlation = new(TenantId.Create());
        internal Fixture() => Journal = new InMemoryAuthorityJournalV1(
            new AuthorityPayloadAdmissionRegistryV1([
                new SessionLifecycleCommandPayloadRegistrationV1(), new SessionLifecycleFactPayloadRegistrationV1(),
            ]), () => new UtcInstant(100), new AuthorityJournalCapacityV1(2, 16, 1_000_000));
        internal SessionAuthorityStampV1 Session { get; } = new(RuntimeGenerationId.Create(), LiveSessionId.Create());
        internal OperationId Operation { get; } = OperationId.Create();
        internal InMemoryAuthorityJournalV1 Journal { get; }

        internal SessionLifecycleCommandV1 Command(SessionLifecycleCommandBodyV1 body) => new(
            Session, ExpectedAuthorityVectorV1.Create(Session, []), SessionLifecycleBodyCodecsV1.Encode(body));

        internal SessionLifecycleFactV1 Fact(SessionLifecycleCommandV1 command, SessionLifecycleFactBodyV1 body) => new(
            Session, command.ExpectedAuthority, SessionLifecycleBodyCodecsV1.Encode(body));

        internal async Task<AuthorityFactEnvelopeV1> AppendAsync(
            object value,
            OperationId operation,
            long head,
            JournalPositionV1? commandPosition = null)
        {
            byte[] payload;
            Hash256 hash;
            SchemaReferenceV1 schema;
            JournalFactId factId;
            if (value is SessionLifecycleCommandV1 command)
            {
                payload = SessionLifecyclePayloadV1Codec.Encode(command);
                hash = SessionLifecyclePayloadV1Codec.ComputeIntegrityHash(command);
                schema = new SessionLifecycleCommandPayloadRegistrationV1().Schema;
                factId = SessionLifecycleCommandFactIdV1.Derive(Session, operation);
            }
            else
            {
                var fact = Assert.IsType<SessionLifecycleFactV1>(value);
                payload = SessionLifecyclePayloadV1Codec.Encode(fact);
                hash = SessionLifecyclePayloadV1Codec.ComputeIntegrityHash(fact);
                schema = new SessionLifecycleFactPayloadRegistrationV1().Schema;
                factId = SessionLifecycleResultFactIdV1.Derive(commandPosition!.Value);
            }
            var proposal = new ProposedAuthorityFactV1(factId, null, OwnerSliceId.S1, schema, payload, hash,
                _correlation, new UtcInstant(head + 1));
            var committed = Assert.IsType<AppendAuthorityResultV1.Committed>(await Journal.AppendAsync(
                new AppendAuthorityBatchV1(Session, head, [], [proposal], 100_000)));
            return Assert.Single(committed.Envelopes);
        }
    }

    private sealed class ThrowingJournal : IAuthorityJournalV1
    {
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request, CancellationToken cancellationToken = default) =>
            throw new IOException("fixture");
    }

    private sealed class OverCountJournal(IAuthorityJournalV1 inner) : IAuthorityJournalV1
    {
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default) =>
            inner.AppendAsync(request, cancellationToken);

        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(new ReadAuthorityRangeV1(request.Session, request.AfterExclusive, request.ThroughInclusive,
                2, request.MaximumEncodedBytes), cancellationToken);
    }

    private sealed class CancelThenReturnJournal(SessionAuthorityStampV1 session, Action cancel) : IAuthorityJournalV1
    {
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request, CancellationToken cancellationToken = default)
        {
            cancel();
            return ValueTask.FromResult<ReadAuthorityRangeResultV1>(
                new ReadAuthorityRangeResultV1.Batch(session, 0, 0, 0, [], false));
        }
    }
}
