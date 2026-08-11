using System.Formats.Cbor;
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
        var changed = fixture.Fact(factId: original.FactId, payloadValue: 7);

        Assert.IsType<AppendAuthorityResultV1.ContradictoryDuplicate>(await fixture.Journal.AppendAsync(fixture.Batch(1, [], [changed])));
        Assert.IsType<AppendAuthorityResultV1.ContradictoryDuplicate>(await fixture.Journal.AppendAsync(
            fixture.Batch(1, [], [original, fixture.Fact()])));
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
        var schema = new SchemaReferenceV1(SchemaId.Create(), 1, 0);
        var token = new BoundedAscii("hpd.authority-payload-session-lifecycle-command.v1");
        Assert.Throws<ArgumentException>(() => new AuthorityPayloadRegistrationV1(
            schema, token, OwnerSliceId.S2, 1024, AuthorityCanonicalCborV1.IsSingleCanonicalValue));
        var first = new AuthorityPayloadRegistrationV1(
            schema, token, OwnerSliceId.S1, 1024, AuthorityCanonicalCborV1.IsSingleCanonicalValue);
        var alias = new AuthorityPayloadRegistrationV1(
            new SchemaReferenceV1(SchemaId.Create(), 1, 0), token, OwnerSliceId.S1, 1024,
            AuthorityCanonicalCborV1.IsSingleCanonicalValue);
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

    private sealed class Fixture
    {
        private readonly BoundedAscii _schemaToken = new("hpd.authority-payload-session-lifecycle-command.v1");
        internal Fixture()
        {
            Schema = new SchemaReferenceV1(SchemaId.Create(), 1, 0);
            var registry = new AuthorityPayloadAdmissionRegistryV1([
                new AuthorityPayloadRegistrationV1(Schema, _schemaToken, OwnerSliceId.S1, 1024,
                    AuthorityCanonicalCborV1.IsSingleCanonicalValue),
            ]);
            Journal = new InMemoryAuthorityJournalV1(registry, () => new UtcInstant(123));
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
            int payloadValue = 1,
            byte[]? rawPayload = null,
            Hash256? hash = null)
        {
            var payload = rawPayload ?? CanonicalInteger(payloadValue);
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
            uint maximumEncodedBytes = 4096) =>
            new(Session, expectedSessionHead, threadHeads, facts, maximumEncodedBytes);

        private static byte[] CanonicalInteger(int value)
        {
            var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
            writer.WriteInt32(value);
            return writer.Encode();
        }
    }
}
