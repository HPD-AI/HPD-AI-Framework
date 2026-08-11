using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class InMemoryAuthorityJournalV1Tests
{
    [Fact]
    public async Task Append_AssignsAtomicSessionAndThreadPositions()
    {
        var fixture = new Fixture();
        var thread = ThreadId.Create();
        var first = fixture.Fact(threadId: thread);
        var second = fixture.Fact(threadId: thread);

        var result = Assert.IsType<AppendAuthorityResultV1.Committed>(await fixture.Journal.AppendAsync(
            fixture.Batch(0, [new(thread, 1, 0)], [first, second])));

        Assert.Equal((0, 2), (result.PreviousHead, result.CurrentHead));
        Assert.Equal([1L, 2L], result.Envelopes.Select(static value => value.Position.Sequence));
        Assert.Equal([1L, 2L], result.Envelopes.Select(static value => value.ThreadScope!.Value.Sequence));
        Assert.All(result.Envelopes, static envelope => Assert.Equal((ushort)1, envelope.Integrity.Profile));
    }

    [Fact]
    public async Task Append_ExactRetryReturnsOriginalPositions()
    {
        var fixture = new Fixture();
        var fact = fixture.Fact();
        var request = fixture.Batch(0, [], [fact]);
        var committed = Assert.IsType<AppendAuthorityResultV1.Committed>(await fixture.Journal.AppendAsync(request));

        var duplicate = Assert.IsType<AppendAuthorityResultV1.AlreadyCommitted>(await fixture.Journal.AppendAsync(request));

        Assert.Same(committed.Envelopes[0], duplicate.Envelopes[0]);
        Assert.Equal(1, duplicate.Envelopes[0].Position.Sequence);
    }

    [Fact]
    public async Task Append_RejectsContradictoryAndMixedDuplicatesWithoutMutation()
    {
        var fixture = new Fixture();
        var original = fixture.Fact();
        Assert.IsType<AppendAuthorityResultV1.Committed>(await fixture.Journal.AppendAsync(fixture.Batch(0, [], [original])));
        var changed = fixture.Fact(factId: original.FactId);

        Assert.IsType<AppendAuthorityResultV1.ContradictoryDuplicate>(await fixture.Journal.AppendAsync(fixture.Batch(1, [], [changed])));
        var mixed = Assert.IsType<AppendAuthorityResultV1.InvalidPayload>(await fixture.Journal.AppendAsync(
            fixture.Batch(1, [], [original, fixture.Fact()])));
        Assert.Equal("mixed-idempotency-batch", mixed.SafeCode.ToString());
        var next = Assert.IsType<AppendAuthorityResultV1.Committed>(await fixture.Journal.AppendAsync(
            fixture.Batch(1, [], [fixture.Fact()])));
        Assert.Equal(2, next.CurrentHead);
    }

    [Fact]
    public async Task Append_RejectsSessionAndThreadCasWithoutMutation()
    {
        var fixture = new Fixture();
        var thread = ThreadId.Create();
        Assert.IsType<AppendAuthorityResultV1.Committed>(await fixture.Journal.AppendAsync(
            fixture.Batch(0, [new(thread, 1, 0)], [fixture.Fact(threadId: thread)])));

        Assert.IsType<AppendAuthorityResultV1.SessionConflict>(await fixture.Journal.AppendAsync(
            fixture.Batch(0, [new(thread, 1, 1)], [fixture.Fact(threadId: thread)])));
        Assert.IsType<AppendAuthorityResultV1.ThreadConflict>(await fixture.Journal.AppendAsync(
            fixture.Batch(1, [new(thread, 1, 0)], [fixture.Fact(threadId: thread)])));
        var committed = Assert.IsType<AppendAuthorityResultV1.Committed>(await fixture.Journal.AppendAsync(
            fixture.Batch(1, [new(thread, 1, 1)], [fixture.Fact(threadId: thread)])));
        Assert.Equal(2, committed.CurrentHead);
    }

    [Fact]
    public async Task Append_TrustedAdmissionRejectsUnknownOwnerHashCanonicalAndSize()
    {
        var fixture = new Fixture();
        Assert.IsType<AppendAuthorityResultV1.UnknownSchema>(await fixture.Journal.AppendAsync(
            fixture.Batch(0, [], [fixture.Fact(schema: new(SchemaId.Create(), 1, 0))])));
        Assert.Equal("owner-mismatch", Assert.IsType<AppendAuthorityResultV1.InvalidPayload>(await fixture.Journal.AppendAsync(
            fixture.Batch(0, [], [fixture.Fact(owner: OwnerSliceId.S2)]))).SafeCode.ToString());
        Assert.Equal("hash-mismatch", Assert.IsType<AppendAuthorityResultV1.InvalidPayload>(await fixture.Journal.AppendAsync(
            fixture.Batch(0, [], [fixture.Fact(hash: Hash256.Compute([9]))]))).SafeCode.ToString());
        Assert.Equal("invalid-canonical-payload", Assert.IsType<AppendAuthorityResultV1.InvalidPayload>(await fixture.Journal.AppendAsync(
            fixture.Batch(0, [], [fixture.Fact(rawPayload: [0xff])]))).SafeCode.ToString());
        Assert.IsType<AppendAuthorityResultV1.CapacityRefused>(await fixture.Journal.AppendAsync(
            fixture.Batch(0, [], [fixture.Fact()], maximumEncodedBytes: 64)));
    }

    [Fact]
    public async Task Append_ConcurrentExpectedHeadHasOneWinner()
    {
        var fixture = new Fixture();
        var first = fixture.Batch(0, [], [fixture.Fact()]);
        var second = fixture.Batch(0, [], [fixture.Fact()]);

        var results = await Task.WhenAll(
            Task.Run(async () => await fixture.Journal.AppendAsync(first)),
            Task.Run(async () => await fixture.Journal.AppendAsync(second)));

        Assert.Single(results.OfType<AppendAuthorityResultV1.Committed>());
        Assert.Single(results.OfType<AppendAuthorityResultV1.SessionConflict>());
    }

    [Fact]
    public async Task Append_PreCanceledRequestDoesNotMutate()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fixture.Journal.AppendAsync(fixture.Batch(0, [], [fixture.Fact()]), cancellation.Token));
        var committed = Assert.IsType<AppendAuthorityResultV1.Committed>(await fixture.Journal.AppendAsync(
            fixture.Batch(0, [], [fixture.Fact()])));
        Assert.Equal(1, committed.CurrentHead);
    }

    [Fact]
    public async Task Append_FailedFirstSessionCasLeavesNoVisibleState()
    {
        var fixture = new Fixture();
        Assert.IsType<AppendAuthorityResultV1.SessionConflict>(await fixture.Journal.AppendAsync(
            fixture.Batch(1, [], [fixture.Fact()])));
        var committed = Assert.IsType<AppendAuthorityResultV1.Committed>(await fixture.Journal.AppendAsync(
            fixture.Batch(0, [], [fixture.Fact()])));
        Assert.Equal((0, 1), (committed.PreviousHead, committed.CurrentHead));
    }

    [Fact]
    public void Registry_RejectsOwnerAndTokenAliasContradictions()
    {
        var first = new SessionAuthorityStampPayloadRegistrationV1();
        var alias = new SessionAuthorityStampPayloadRegistrationV1();
        Assert.Equal(AuthoritySchemaIdentityV1.Derive(new BoundedAscii(SessionAuthorityStampV1Codec.SchemaId)), first.Schema.SchemaId);
        Assert.Throws<ArgumentException>(() => new AuthorityPayloadAdmissionRegistryV1([first, alias]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(256)]
    public void CanonicalBatchLength_EqualsRegisteredEncoder(int factCount)
    {
        var fixture = new Fixture();
        var request = fixture.Batch(0, [], Enumerable.Range(0, factCount).Select(_ => fixture.Fact()), 1_048_576);
        Assert.Equal((ulong)AuthorityCanonicalCborV1.EncodeAppendBatch(request).Length,
            AuthorityCanonicalCborV1.GetAppendBatchEncodedLength(request));
    }

    [Fact]
    public async Task Append_ResidentFactByteAndSessionCapacitiesAreAtomic()
    {
        var factLimited = new Fixture(new AuthorityJournalCapacityV1(1, 1, 1_048_576));
        Assert.IsType<AppendAuthorityResultV1.Committed>(await factLimited.Journal.AppendAsync(
            factLimited.Batch(0, [], [factLimited.Fact()])));
        Assert.IsType<AppendAuthorityResultV1.CapacityRefused>(await factLimited.Journal.AppendAsync(
            factLimited.Batch(1, [], [factLimited.Fact()])));

        var sessionLimited = new Fixture(new AuthorityJournalCapacityV1(1, 2, 1_048_576));
        Assert.IsType<AppendAuthorityResultV1.Committed>(await sessionLimited.Journal.AppendAsync(
            sessionLimited.Batch(0, [], [sessionLimited.Fact()])));
        var otherSession = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        Assert.IsType<AppendAuthorityResultV1.CapacityRefused>(await sessionLimited.Journal.AppendAsync(
            sessionLimited.Batch(0, [], [sessionLimited.Fact()], session: otherSession)));

        var byteLimited = new Fixture(new AuthorityJournalCapacityV1(1, 1, 1));
        Assert.IsType<AppendAuthorityResultV1.CapacityRefused>(await byteLimited.Journal.AppendAsync(
            byteLimited.Batch(0, [], [byteLimited.Fact()])));
        Assert.IsType<AppendAuthorityResultV1.CapacityRefused>(await byteLimited.Journal.AppendAsync(
            byteLimited.Batch(0, [], [byteLimited.Fact()])));
    }

    [Fact]
    public void AdmissionSchemaHashBatchAndEnvelope_MatchIndependentGoldens()
    {
        var token = new BoundedAscii(SessionAuthorityStampV1Codec.SchemaId);
        var schema = new SchemaReferenceV1(AuthoritySchemaIdentityV1.Derive(token), 1, 0);
        Assert.Equal("sch:637NZQGC7QNR96H0V57WVXKY1V", schema.SchemaId.ToString());
        var payload = Convert.FromHexString("a20150000102030405060708090a0b0c0d0e0f0250101112131415161718191a1b1c1d1e1f");
        Assert.True(Hash256.TryParse("429698042c849d1302b7b44b7e16ee44d3a25d25ad58a548f30c7c472f1f304e", out var payloadHash));
        Assert.Equal(payloadHash, AuthorityPayloadHashV1.Compute(token, schema, payload));
        var session = new SessionAuthorityStampV1(
            RuntimeGenerationId.FromValue(StableId128.FromBytes(Convert.FromHexString("202122232425262728292a2b2c2d2e2f"))),
            LiveSessionId.FromValue(StableId128.FromBytes(Convert.FromHexString("303132333435363738393a3b3c3d3e3f"))));
        var fact = new ProposedAuthorityFactV1(
            JournalFactId.FromValue(StableId128.FromBytes(Convert.FromHexString("404142434445464748494a4b4c4d4e4f"))),
            null, OwnerSliceId.S1, schema, payload, payloadHash,
            new CorrelationEnvelopeV1(TenantId.FromValue(StableId128.FromBytes(Convert.FromHexString("505152535455565758595a5b5c5d5e5f")))),
            new UtcInstant(100));
        var request = new AppendAuthorityBatchV1(session, 0, [], [fact], 4096);
        const string expectedBatch = "a501a20150202122232425262728292a2b2c2d2e2f0250303132333435363738393a3b3c3d3e3f020003800481a80150404142434445464748494a4b4c4d4e4f02a10100030104a30150c33d7f7830f7ae126883653f37d9f83b02010300055825a20150000102030405060708090a0b0c0d0e0f0250101112131415161718191a1b1c1d1e1f065820429698042c849d1302b7b44b7e16ee44d3a25d25ad58a548f30c7c472f1f304e07a60150505152535455565758595a5b5c5d5e5f02a1010003a1010004a1010005a1010006a1010008186405191000";
        Assert.Equal(expectedBatch, Convert.ToHexString(AuthorityCanonicalCborV1.EncodeAppendBatch(request)).ToLowerInvariant());
        Assert.Equal(216UL, AuthorityCanonicalCborV1.GetAppendBatchEncodedLength(request));
        var preimage = AuthorityCanonicalCborV1.EncodeEnvelopeWithoutIntegrity(fact, new JournalPositionV1(session, 1), null, new UtcInstant(123));
        const string expectedPreimage = "ab01010250404142434445464748494a4b4c4d4e4f03a201a20150202122232425262728292a2b2c2d2e2f0250303132333435363738393a3b3c3d3e3f020104a10100050106a30150c33d7f7830f7ae126883653f37d9f83b02010300075825a20150000102030405060708090a0b0c0d0e0f0250101112131415161718191a1b1c1d1e1f085820429698042c849d1302b7b44b7e16ee44d3a25d25ad58a548f30c7c472f1f304e09a60150505152535455565758595a5b5c5d5e5f02a1010003a1010004a1010005a1010006a101000a18640b187b";
        Assert.Equal(expectedPreimage, Convert.ToHexString(preimage).ToLowerInvariant());
        Assert.Equal("cbb16017f597b7e8f36566073de08f75ab143a8085865e6a2c6d717becdce410",
            AuthorityIntegrityHashV1.Compute("hpd.authority-fact-envelope.v1", 1, 0, preimage).ToString());
    }

    private sealed class Fixture
    {
        private readonly BoundedAscii _schemaToken = new("hpd.session-authority-stamp.v1");
        internal Fixture(AuthorityJournalCapacityV1? capacity = null)
        {
            var registration = new SessionAuthorityStampPayloadRegistrationV1();
            Schema = registration.Schema;
            var registry = new AuthorityPayloadAdmissionRegistryV1([registration]);
            Journal = new InMemoryAuthorityJournalV1(registry, () => new UtcInstant(123),
                capacity ?? new AuthorityJournalCapacityV1(1024, 4096, 16 * 1024 * 1024));
            Session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        }

        internal SchemaReferenceV1 Schema { get; }
        internal SessionAuthorityStampV1 Session { get; }
        internal InMemoryAuthorityJournalV1 Journal { get; }

        internal ProposedAuthorityFactV1 Fact(
            JournalFactId? factId = null,
            ThreadId? threadId = null,
            OwnerSliceId owner = OwnerSliceId.S1,
            SchemaReferenceV1? schema = null,
            byte[]? rawPayload = null,
            Hash256? hash = null)
        {
            var payload = rawPayload ?? SessionAuthorityStampV1Codec.Encode(new SessionAuthorityStampV1(
                RuntimeGenerationId.Create(), LiveSessionId.Create()));
            var reference = schema ?? Schema;
            return new ProposedAuthorityFactV1(
                factId ?? JournalFactId.Create(), threadId, owner, reference, payload,
                hash ?? AuthorityPayloadHashV1.Compute(_schemaToken, reference, payload),
                new CorrelationEnvelopeV1(TenantId.Create(), threadId: threadId), new UtcInstant(100));
        }

        internal AppendAuthorityBatchV1 Batch(
            long expectedSessionHead,
            IEnumerable<ThreadExpectedHeadV1> threadHeads,
            IEnumerable<ProposedAuthorityFactV1> facts,
            uint maximumEncodedBytes = 4096,
            SessionAuthorityStampV1? session = null) =>
            new(session ?? Session, expectedSessionHead, threadHeads, facts, maximumEncodedBytes);

    }
}
