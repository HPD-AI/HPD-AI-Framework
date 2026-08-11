using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class SessionLifecycleAdmissionCoordinatorV1Tests
{
    [Fact]
    public async Task Reserve_CommitsCommandAndFact_ThenExactRetryReturnsOriginalPair()
    {
        var fixture = new Fixture();
        var command = fixture.Command(new SessionLifecycleCommandBodyV1.ReserveStarting(
            fixture.Operation, Hash256.Compute("request"u8)));

        var committed = Assert.IsType<SessionLifecycleAdmissionResultV1.Committed>(await fixture.AdmitAsync(command));
        Assert.Equal(1, committed.Command.Position.Sequence);
        Assert.Equal(2, committed.Result.Position.Sequence);
        Assert.Equal(SessionLifecycleOutcomeV1.Applied, DecodeResult(committed.Result).Outcome);
        Assert.Throws<ArgumentException>(() => new SessionLifecycleAdmissionResultV1.Committed(
            committed.Result, committed.Command));
        Assert.Throws<ArgumentException>(() => new SessionLifecycleAdmissionResultV1.Committed(
            Copy(committed.Command, threadScope: new ThreadPositionV1(ThreadId.Create(), 1, 1)), committed.Result));
        Assert.Throws<ArgumentException>(() => new SessionLifecycleAdmissionResultV1.Committed(
            committed.Command, Copy(committed.Result, payloadHash: Hash256.Compute("wrong"u8))));
        var resultBody = DecodeResult(committed.Result);
        var mismatchedBody = new SessionLifecycleFactBodyV1(resultBody.OperationId, resultBody.CommandPosition,
            committed.Command.Position, resultBody.PreviousLifecycleFact, resultBody.Outcome, resultBody.Snapshot, resultBody.SafeCode);
        var mismatchedOuter = new SessionLifecycleFactV1(committed.Result.Position.Session,
            AssertFactOuter(committed.Result).ExpectedAuthority, SessionLifecycleBodyCodecsV1.Encode(mismatchedBody));
        Assert.Throws<ArgumentException>(() => new SessionLifecycleAdmissionResultV1.Committed(
            committed.Command, Copy(committed.Result, payload: SessionLifecyclePayloadV1Codec.Encode(mismatchedOuter),
                payloadHash: SessionLifecyclePayloadV1Codec.ComputeIntegrityHash(mismatchedOuter))));

        var duplicate = Assert.IsType<SessionLifecycleAdmissionResultV1.AlreadyCommitted>(await fixture.AdmitAsync(command));
        Assert.Equal(committed.Command.FactId, duplicate.Command.FactId);
        Assert.Equal(committed.Result.FactId, duplicate.Result.FactId);
    }

    [Fact]
    public async Task SameOperationWithDifferentBytes_IsContradictory()
    {
        var fixture = new Fixture();
        var first = fixture.Command(new SessionLifecycleCommandBodyV1.ReserveStarting(
            fixture.Operation, Hash256.Compute("first"u8)));
        Assert.IsType<SessionLifecycleAdmissionResultV1.Committed>(await fixture.AdmitAsync(first));
        var changed = fixture.Command(new SessionLifecycleCommandBodyV1.ReserveStarting(
            fixture.Operation, Hash256.Compute("changed"u8)));

        Assert.IsType<SessionLifecycleAdmissionResultV1.ContradictoryDuplicate>(await fixture.AdmitAsync(changed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AmbiguousAppend_IsReconciledByExactRetry(bool throwAfterCommit)
    {
        var fixture = new Fixture();
        var command = fixture.Command(new SessionLifecycleCommandBodyV1.ReserveStarting(
            fixture.Operation, Hash256.Compute("request"u8)));
        var faulting = new FaultingAppendJournal(fixture.Journal, throwAfterCommit ? 2 : 1, throwAfterCommit);

        Assert.IsType<SessionLifecycleAdmissionResultV1.OutcomeUnknown>(await fixture.AdmitAsync(command, faulting));
        var retried = await fixture.AdmitAsync(command);
        if (throwAfterCommit)
            Assert.IsType<SessionLifecycleAdmissionResultV1.AlreadyCommitted>(retried);
        else
            Assert.IsType<SessionLifecycleAdmissionResultV1.Committed>(retried);
    }

    [Fact]
    public async Task ConcurrentCommandsClaimingOnePredecessor_ProduceOneAppliedAndOneDurableConflict()
    {
        var fixture = new Fixture();
        var reserve = Assert.IsType<SessionLifecycleAdmissionResultV1.Committed>(await fixture.AdmitAsync(
            fixture.Command(new SessionLifecycleCommandBodyV1.ReserveStarting(
                fixture.Operation, Hash256.Compute("request"u8)))));
        var predecessor = reserve.Result.Position;
        var ready = fixture.Command(new SessionLifecycleCommandBodyV1.PublishReady(
            OperationId.Create(), predecessor, SessionAvailabilityWireV1.Available));
        var drain = fixture.Command(new SessionLifecycleCommandBodyV1.BeginDrain(OperationId.Create(), predecessor));

        var results = await Task.WhenAll(fixture.AdmitAsync(ready).AsTask(), fixture.AdmitAsync(drain).AsTask());
        var bodies = results.Select(ResultEnvelope).Select(DecodeResult).ToArray();
        Assert.Single(bodies, body => body.Outcome == SessionLifecycleOutcomeV1.Applied);
        var rejected = Assert.Single(bodies, body => body.Outcome == SessionLifecycleOutcomeV1.Rejected);
        Assert.Equal("lifecycle-predecessor-conflict", rejected.SafeCode?.ToString());

        var fold = Assert.IsType<SessionLifecycleSnapshotReadResultV1.Verified>(await SessionLifecycleSnapshotReaderV1.ReadAsync(
            fixture.Journal, fixture.Session));
        Assert.Empty(Assert.IsType<SessionLifecycleJournalFoldResultV1.Current>(fold.Fold).PendingCommands);
    }

    [Fact]
    public void ResultUnion_RejectsImpossibleValues()
    {
        Assert.Throws<ArgumentNullException>(() => new SessionLifecycleAdmissionResultV1.Committed(null!, null!));
        Assert.Throws<ArgumentException>(() => new SessionLifecycleAdmissionResultV1.ContradictoryDuplicate(default));
        Assert.Throws<ArgumentException>(() => new SessionLifecycleAdmissionResultV1.GenerationReplaced(default));
        Assert.Throws<ArgumentException>(() => new SessionLifecycleAdmissionResultV1.InvalidHistory(default, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionLifecycleAdmissionResultV1.InvalidHistory(
            new BoundedAscii("fixture"), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionLifecycleAdmissionResultV1.RetryRequired(-1));
        Assert.Throws<ArgumentException>(() => new SessionLifecycleAdmissionResultV1.Rejected(default));
        Assert.Throws<ArgumentException>(() => new SessionLifecycleAdmissionResultV1.OutcomeUnknown(default, default));
    }

    [Fact]
    public async Task FinalReconciliation_ReturnsCommittedTargetOrOutcomeUnknownInsteadOfGenericRetry()
    {
        var committedFixture = new Fixture();
        var committedCommand = committedFixture.Command(new SessionLifecycleCommandBodyV1.ReserveStarting(
            committedFixture.Operation, Hash256.Compute("commit"u8)));
        Assert.IsType<SessionLifecycleAdmissionResultV1.OutcomeUnknown>(await committedFixture.AdmitAsync(
            committedCommand, new FaultingAppendJournal(committedFixture.Journal, 2, false)));
        var reconciled = await committedFixture.AdmitAsync(
            committedCommand, new ConflictFinalJournal(committedFixture.Journal, commitOnLastConflict: true, failFinalRead: false));
        Assert.IsType<SessionLifecycleAdmissionResultV1.AlreadyCommitted>(reconciled);

        var unknownFixture = new Fixture();
        var unknownCommand = unknownFixture.Command(new SessionLifecycleCommandBodyV1.ReserveStarting(
            unknownFixture.Operation, Hash256.Compute("unknown"u8)));
        Assert.IsType<SessionLifecycleAdmissionResultV1.OutcomeUnknown>(await unknownFixture.AdmitAsync(
            unknownCommand, new FaultingAppendJournal(unknownFixture.Journal, 2, false)));
        var unknown = Assert.IsType<SessionLifecycleAdmissionResultV1.OutcomeUnknown>(await unknownFixture.AdmitAsync(
            unknownCommand, new ConflictFinalJournal(unknownFixture.Journal, commitOnLastConflict: false, failFinalRead: true)));
        Assert.Equal("final-read-unavailable", unknown.SafeCode.ToString());
    }

    private static AuthorityFactEnvelopeV1 ResultEnvelope(SessionLifecycleAdmissionResultV1 result) => result switch
    {
        SessionLifecycleAdmissionResultV1.Committed committed => committed.Result,
        SessionLifecycleAdmissionResultV1.AlreadyCommitted existing => existing.Result,
        _ => throw new Xunit.Sdk.XunitException($"Expected a durable lifecycle result, received {result.GetType().Name}."),
    };

    private static SessionLifecycleFactBodyV1 DecodeResult(AuthorityFactEnvelopeV1 envelope)
    {
        Assert.True(SessionLifecyclePayloadV1Codec.TryDecodeFact(envelope.PayloadMemory, out var fact));
        Assert.True(SessionLifecycleBodyCodecsV1.TryDecodeFact(fact!.BodyBytes.ToArray(), out var body));
        return body!;
    }

    private static SessionLifecycleFactV1 AssertFactOuter(AuthorityFactEnvelopeV1 envelope)
    {
        Assert.True(SessionLifecyclePayloadV1Codec.TryDecodeFact(envelope.PayloadMemory, out var fact));
        return fact!;
    }

    private static AuthorityFactEnvelopeV1 Copy(
        AuthorityFactEnvelopeV1 source,
        ThreadPositionV1? threadScope = null,
        byte[]? payload = null,
        Hash256? payloadHash = null) => new(
        source.FactId, source.Position, threadScope, source.Owner, source.PayloadSchema,
        payload ?? source.Payload.ToArray(), payloadHash ?? source.PayloadHash, source.Correlation,
        source.ObservedAt, source.AdmittedAt, source.Integrity);

    private sealed class Fixture
    {
        private readonly CorrelationEnvelopeV1 _correlation = new(TenantId.Create());
        internal Fixture() => Journal = new InMemoryAuthorityJournalV1(
            new AuthorityPayloadAdmissionRegistryV1([
                new SessionLifecycleCommandPayloadRegistrationV1(), new SessionLifecycleFactPayloadRegistrationV1(),
            ]), () => new UtcInstant(100), new AuthorityJournalCapacityV1(4, 64, 4_000_000));
        internal SessionAuthorityStampV1 Session { get; } = new(RuntimeGenerationId.Create(), LiveSessionId.Create());
        internal OperationId Operation { get; } = OperationId.Create();
        internal InMemoryAuthorityJournalV1 Journal { get; }
        internal SessionLifecycleCommandV1 Command(SessionLifecycleCommandBodyV1 body) => new(
            Session, ExpectedAuthorityVectorV1.Create(Session, []), SessionLifecycleBodyCodecsV1.Encode(body));
        internal ValueTask<SessionLifecycleAdmissionResultV1> AdmitAsync(
            SessionLifecycleCommandV1 command,
            IAuthorityJournalV1? journal = null) => SessionLifecycleAdmissionCoordinatorV1.AdmitAsync(
                journal ?? Journal, command, _correlation, new UtcInstant(7));
    }

    private sealed class FaultingAppendJournal(IAuthorityJournalV1 inner, int faultAppend, bool commitFirst) : IAuthorityJournalV1
    {
        private int _appendCount;
        public async ValueTask<AppendAuthorityResultV1> AppendAsync(
            AppendAuthorityBatchV1 request,
            CancellationToken cancellationToken = default)
        {
            var count = Interlocked.Increment(ref _appendCount);
            if (count != faultAppend) return await inner.AppendAsync(request, cancellationToken);
            if (commitFirst) await inner.AppendAsync(request, cancellationToken);
            throw new IOException("fixture");
        }
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(
            ReadAuthorityRangeV1 request,
            CancellationToken cancellationToken = default) => inner.ReadAsync(request, cancellationToken);
    }

    private sealed class ConflictFinalJournal(
        IAuthorityJournalV1 inner,
        bool commitOnLastConflict,
        bool failFinalRead) : IAuthorityJournalV1
    {
        private int _appendCount;
        private int _readCount;

        public async ValueTask<AppendAuthorityResultV1> AppendAsync(
            AppendAuthorityBatchV1 request,
            CancellationToken cancellationToken = default)
        {
            var count = Interlocked.Increment(ref _appendCount);
            if (count == 8 && commitOnLastConflict)
                await inner.AppendAsync(request, cancellationToken);
            return new AppendAuthorityResultV1.SessionConflict(request.ExpectedSessionHead,
                checked(request.ExpectedSessionHead + 1));
        }

        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(
            ReadAuthorityRangeV1 request,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _readCount) == 9 && failFinalRead)
                return ValueTask.FromResult<ReadAuthorityRangeResultV1>(
                    new ReadAuthorityRangeResultV1.StoreUnavailable(new BoundedAscii("final-read-unavailable")));
            return inner.ReadAsync(request, cancellationToken);
        }
    }
}
