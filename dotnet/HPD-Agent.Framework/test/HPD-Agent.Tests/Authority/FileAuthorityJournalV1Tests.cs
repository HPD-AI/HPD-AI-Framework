using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class FileAuthorityJournalV1Tests
{
    [Fact]
    public async Task FileAndMemoryBackends_ProduceEquivalentCommittedEnvelope()
    {
        await using var fixture = await Fixture.CreateAsync();
        var thread = ThreadId.Create();
        var request = fixture.Batch(0, [new(thread, 1, 0)], [fixture.Fact(thread)]);

        var memoryResult = Assert.IsType<AppendAuthorityResultV1.Committed>(await fixture.Memory.AppendAsync(request));
        var fileResult = Assert.IsType<AppendAuthorityResultV1.Committed>(await fixture.File.AppendAsync(request));

        AssertEnvelopeEqual(Assert.Single(memoryResult.Envelopes), Assert.Single(fileResult.Envelopes));
    }

    [Fact]
    public async Task Reopen_ReplaysExactPrefixAndPreservesIdempotency()
    {
        await using var fixture = await Fixture.CreateAsync();
        var fact = fixture.Fact();
        var request = fixture.Batch(0, [], [fact]);
        var committed = Assert.IsType<AppendAuthorityResultV1.Committed>(await fixture.File.AppendAsync(request));
        await fixture.ReopenAsync();

        var duplicate = Assert.IsType<AppendAuthorityResultV1.AlreadyCommitted>(await fixture.File.AppendAsync(request));
        var read = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await fixture.File.ReadAsync(
            new ReadAuthorityRangeV1(fixture.Session, 0, long.MaxValue, 256, 1_048_576)));

        AssertEnvelopeEqual(Assert.Single(committed.Envelopes), Assert.Single(duplicate.Envelopes));
        AssertEnvelopeEqual(Assert.Single(committed.Envelopes), Assert.Single(read.Facts));
        Assert.Equal((1L, 1L, false), (read.SnapshotHead, read.SnapshotThrough, read.HasMore));
    }

    [Fact]
    public async Task Recovery_DropsIncompleteSuffixButRejectsCommittedRecordCorruption()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.IsType<AppendAuthorityResultV1.Committed>(await fixture.File.AppendAsync(
            fixture.Batch(0, [], [fixture.Fact()])));
        await fixture.CloseAsync();
        await using (var suffix = new FileStream(fixture.Path, FileMode.Append, FileAccess.Write, FileShare.None))
            await suffix.WriteAsync(new byte[] { 0x48, 0x50, 0x44 });

        await fixture.ReopenAsync();
        var recovered = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await fixture.File.ReadAsync(
            new ReadAuthorityRangeV1(fixture.Session, 0, long.MaxValue, 256, 1_048_576)));
        Assert.Single(recovered.Facts);
        await fixture.CloseAsync();

        var bytes = await System.IO.File.ReadAllBytesAsync(fixture.Path);
        bytes[20] ^= 0x01;
        await System.IO.File.WriteAllBytesAsync(fixture.Path, bytes);
        Assert.Throws<InvalidDataException>(() => fixture.Open());
    }

    [Fact]
    public async Task ConcurrentExpectedHead_HasOneDurableWinnerAcrossRestart()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = fixture.Batch(0, [], [fixture.Fact()]);
        var second = fixture.Batch(0, [], [fixture.Fact()]);

        var results = await Task.WhenAll(
            fixture.File.AppendAsync(first).AsTask(), fixture.File.AppendAsync(second).AsTask());
        Assert.Single(results.OfType<AppendAuthorityResultV1.Committed>());
        Assert.Single(results.OfType<AppendAuthorityResultV1.SessionConflict>());

        await fixture.ReopenAsync();
        var read = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await fixture.File.ReadAsync(
            new ReadAuthorityRangeV1(fixture.Session, 0, long.MaxValue, 256, 1_048_576)));
        Assert.Single(read.Facts);
    }

    [Fact]
    public async Task Reopen_PreservesGenerationFenceAndRejectsStaleWritesLikeMemory()
    {
        await using var fixture = await Fixture.CreateAsync();
        var replacementBytes = Enumerable.Range(1, 16).Select(static value => (byte)value).ToArray();
        var replacement = StableId128.FromBytes(replacementBytes);
        var transition = fixture.Batch(0, [], [fixture.RuntimeTransition(replacement)]);

        Assert.IsType<AppendAuthorityResultV1.Committed>(await fixture.Memory.AppendAsync(transition));
        Assert.IsType<AppendAuthorityResultV1.Committed>(await fixture.File.AppendAsync(transition));
        await fixture.ReopenAsync();

        var stale = fixture.Batch(1, [], [fixture.Fact()]);
        var memoryRejected = Assert.IsType<AppendAuthorityResultV1.InvalidPayload>(
            await fixture.Memory.AppendAsync(stale));
        var fileRejected = Assert.IsType<AppendAuthorityResultV1.InvalidPayload>(
            await fixture.File.AppendAsync(stale));
        Assert.Equal("generation-state-conflict", memoryRejected.SafeCode.ToString());
        Assert.Equal(memoryRejected.SafeCode, fileRejected.SafeCode);
    }

    [Theory]
    [InlineData("before-record-write", 0)]
    [InlineData("after-record-write-before-flush", 1)]
    [InlineData("after-record-flush", 1)]
    public async Task FaultBoundaries_ReconcileFromDurablePrefix(string faultStage, int expectedFacts)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ReopenWithFaultAsync(stage =>
            stage.ToString() == faultStage
                ? ValueTask.FromException(new IOException("injected-stage-fault"))
                : ValueTask.CompletedTask);

        await Assert.ThrowsAsync<IOException>(async () => await fixture.File.AppendAsync(
            fixture.Batch(0, [], [fixture.Fact()])));
        await fixture.ReopenAsync();
        var read = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await fixture.File.ReadAsync(
            new ReadAuthorityRangeV1(fixture.Session, 0, long.MaxValue, 256, 1_048_576)));
        Assert.Equal(expectedFacts, read.Facts.Count);
    }

    private static void AssertEnvelopeEqual(AuthorityFactEnvelopeV1 expected, AuthorityFactEnvelopeV1 actual)
    {
        Assert.Equal(expected.FactId, actual.FactId);
        Assert.Equal(expected.Position, actual.Position);
        Assert.Equal(expected.ThreadScope, actual.ThreadScope);
        Assert.Equal(expected.Owner, actual.Owner);
        Assert.Equal(expected.PayloadSchema, actual.PayloadSchema);
        Assert.Equal(expected.Payload, actual.Payload);
        Assert.Equal(expected.PayloadHash, actual.PayloadHash);
        Assert.Equal(expected.Correlation, actual.Correlation);
        Assert.Equal(expected.ObservedAt, actual.ObservedAt);
        Assert.Equal(expected.AdmittedAt, actual.AdmittedAt);
        Assert.Equal(expected.Integrity.Profile, actual.Integrity.Profile);
        Assert.Equal(expected.Integrity.KeyVersion, actual.Integrity.KeyVersion);
        Assert.Equal(expected.Integrity.Digest, actual.Integrity.Digest);
        Assert.Equal(expected.Integrity.Signature, actual.Integrity.Signature);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly BoundedAscii _schemaToken = new(SessionAuthorityStampV1Codec.SchemaId);
        private readonly string _directory;
        private readonly AuthorityPayloadAdmissionRegistryV1 _registry;
        private readonly AuthorityJournalCapacityV1 _capacity = new(32, 1024, 16 * 1024 * 1024);

        private Fixture(string directory)
        {
            _directory = directory;
            Path = System.IO.Path.Combine(directory, "authority.log");
            var registration = new SessionAuthorityStampPayloadRegistrationV1();
            Schema = registration.Schema;
            _registry = new AuthorityPayloadAdmissionRegistryV1(
                [registration, new AuthorityGenerationTransitionPayloadRegistrationV1(AuthorityAxisId.Runtime)]);
            Session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
            Memory = new InMemoryAuthorityJournalV1(_registry, Clock, _capacity);
            File = Open();
        }

        internal string Path { get; }
        internal SchemaReferenceV1 Schema { get; }
        internal SessionAuthorityStampV1 Session { get; }
        internal InMemoryAuthorityJournalV1 Memory { get; }
        internal FileAuthorityJournalV1 File { get; private set; }

        internal static ValueTask<Fixture> CreateAsync()
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hpd-authority-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return ValueTask.FromResult(new Fixture(directory));
        }

        internal FileAuthorityJournalV1 Open() =>
            FileAuthorityJournalV1.OpenAsync(Path, _registry, Clock, _capacity).AsTask().GetAwaiter().GetResult();

        internal async ValueTask CloseAsync()
        {
            if (File is not null) await File.DisposeAsync();
        }

        internal async ValueTask ReopenAsync()
        {
            await CloseAsync();
            File = Open();
        }

        internal async ValueTask ReopenWithFaultAsync(Func<BoundedAscii, ValueTask> stageFault)
        {
            await CloseAsync();
            File = FileAuthorityJournalV1.OpenForTestingAsync(Path, _registry, Clock, _capacity, stageFault)
                .AsTask().GetAwaiter().GetResult();
        }

        internal ProposedAuthorityFactV1 Fact(ThreadId? threadId = null)
        {
            var payload = SessionAuthorityStampV1Codec.Encode(new SessionAuthorityStampV1(
                RuntimeGenerationId.Create(), LiveSessionId.Create()));
            return new ProposedAuthorityFactV1(JournalFactId.Create(), threadId, OwnerSliceId.S1, Schema,
                payload, AuthorityPayloadHashV1.Compute(_schemaToken, Schema, payload),
                new CorrelationEnvelopeV1(TenantId.Create(), threadId: threadId), new UtcInstant(100));
        }

        internal ProposedAuthorityFactV1 RuntimeTransition(StableId128 replacement)
        {
            Span<byte> currentBytes = stackalloc byte[16];
            if (!Session.RuntimeGenerationId.TryWriteBytes(currentBytes))
                throw new InvalidOperationException("The fixture runtime generation is invalid.");
            var registration = new AuthorityGenerationTransitionPayloadRegistrationV1(AuthorityAxisId.Runtime);
            var payload = AuthorityGenerationTransitionCodecV1.Encode(
                Session, AuthorityAxisId.Runtime, StableId128.FromBytes(currentBytes), replacement);
            return new ProposedAuthorityFactV1(JournalFactId.Create(), null, registration.Owner,
                registration.Schema, payload,
                AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload),
                new CorrelationEnvelopeV1(TenantId.Create(), operationId: OperationId.Create()),
                new UtcInstant(100));
        }

        internal AppendAuthorityBatchV1 Batch(long expectedSessionHead,
            IEnumerable<ThreadExpectedHeadV1> threadHeads, IEnumerable<ProposedAuthorityFactV1> facts) =>
            new(Session, expectedSessionHead, threadHeads, facts, 1_048_576);

        public async ValueTask DisposeAsync()
        {
            await CloseAsync();
            try { Directory.Delete(_directory, recursive: true); } catch (DirectoryNotFoundException) { }
        }

        private static UtcInstant Clock() => new(123);
    }
}
