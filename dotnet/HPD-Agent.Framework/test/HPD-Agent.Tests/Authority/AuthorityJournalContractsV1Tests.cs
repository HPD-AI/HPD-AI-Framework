using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class AuthorityJournalContractsV1Tests
{
    [Fact]
    public void Correlation_ValidatesRequiredAndPresentIdentities()
    {
        var correlation = new CorrelationEnvelopeV1(
            TenantId.Create(), PrincipalId.Create(), SessionId.Create(), ThreadId.Create(), ParticipantId.Create(), OperationId.Create());

        Assert.True(correlation.IsValid);
        Assert.False(default(CorrelationEnvelopeV1).IsValid);
        Assert.Throws<ArgumentException>(() => new CorrelationEnvelopeV1(default));
        Assert.Throws<ArgumentException>(() => new CorrelationEnvelopeV1(TenantId.Create(), default(PrincipalId)));
    }

    [Fact]
    public void ProposedFact_OwnsPayloadAndRejectsInvalidBoundaries()
    {
        var payload = new byte[] { 1, 2, 3 };
        var fact = Fact(payload: payload);
        payload[0] = 9;

        Assert.Equal(new byte[] { 1, 2, 3 }, fact.Payload);
        Assert.Throws<ArgumentException>(() => Fact(factId: new JournalFactId()));
        Assert.Throws<ArgumentException>(() => Fact(owner: (OwnerSliceId)99));
        Assert.Throws<ArgumentException>(() => Fact(hash: new Hash256()));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fact(payload: new byte[ProposedAuthorityFactV1.MaximumPayloadBytes + 1]));
    }

    [Fact]
    public void Batch_CanonicalizesThreadHeadsAndPreservesFactOrder()
    {
        var threadA = ThreadId.Create();
        var threadB = ThreadId.Create();
        var heads = new[] { new ThreadExpectedHeadV1(threadB, 1, 0), new ThreadExpectedHeadV1(threadA, 2, 3) };
        var first = Fact(threadId: threadA);
        var second = Fact(threadId: threadB);

        var batch = new AppendAuthorityBatchV1(
            Stamp(), 0, heads, [first, second], 1024, TestBatchCodec.Instance);

        Assert.Equal([first.FactId, second.FactId], batch.Facts.Select(static fact => fact.FactId));
        Assert.Equal(2, batch.ExpectedThreadHeads.Count);
        Assert.True(Compare(batch.ExpectedThreadHeads[0].ThreadId, batch.ExpectedThreadHeads[1].ThreadId) < 0);
    }

    [Fact]
    public void Batch_RejectsDuplicateAndUnboundedInputBeforeMutation()
    {
        var fact = Fact();
        var thread = ThreadId.Create();
        Assert.Throws<ArgumentException>(() => new AppendAuthorityBatchV1(Stamp(), 0, [], [fact, fact], 1024, TestBatchCodec.Instance));
        Assert.Throws<ArgumentException>(() => new AppendAuthorityBatchV1(
            Stamp(), 0, [new(thread, 1, 0), new(thread, 1, 0)], [fact], 1024, TestBatchCodec.Instance));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppendAuthorityBatchV1(Stamp(), 0, [], [], 1024, TestBatchCodec.Instance));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppendAuthorityBatchV1(
            Stamp(), 0, [], Enumerable.Range(0, 257).Select(_ => Fact()), 1024, TestBatchCodec.Instance));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppendAuthorityBatchV1(
            Stamp(), 0, [], [Fact(payload: new byte[16])], 8, TestBatchCodec.Instance));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppendAuthorityBatchV1(
            Stamp(), 0, Enumerable.Range(0, 257).Select(_ => new ThreadExpectedHeadV1(ThreadId.Create(), 1, 0)),
            [fact], 1024, TestBatchCodec.Instance));
    }

    [Fact]
    public void OwnerRegistry_HasExactClosedValues()
    {
        Assert.Equal(Enumerable.Range(1, 12).Select(static value => (ushort)value),
            Enum.GetValues<OwnerSliceId>().Select(static value => (ushort)value));
        Assert.Equal(Enumerable.Range(1, 6).Select(static value => (ushort)value),
            Enum.GetValues<AuthorityPayloadValidationV1>().Select(static value => (ushort)value));
    }

    private static ProposedAuthorityFactV1 Fact(
        JournalFactId? factId = null,
        OwnerSliceId owner = OwnerSliceId.S1,
        byte[]? payload = null,
        Hash256? hash = null,
        ThreadId? threadId = null)
    {
        payload ??= [1];
        var schema = new SchemaReferenceV1(SchemaId.Create(), 1, 0);
        var registry = new TestSchemaRegistry(schema);
        return new ProposedAuthorityFactV1(
            factId ?? JournalFactId.Create(), threadId, owner,
            schema, payload, hash ?? registry.Hash(payload),
            new CorrelationEnvelopeV1(TenantId.Create()), new UtcInstant(0), registry);
    }

    private static SessionAuthorityStampV1 Stamp() =>
        new(RuntimeGenerationId.Create(), LiveSessionId.Create());

    private static int Compare(ThreadId left, ThreadId right)
    {
        Span<byte> leftBytes = stackalloc byte[16];
        Span<byte> rightBytes = stackalloc byte[16];
        left.TryWriteBytes(leftBytes);
        right.TryWriteBytes(rightBytes);
        return leftBytes.SequenceCompareTo(rightBytes);
    }

    private sealed class TestSchemaRegistry(SchemaReferenceV1 expected) : IAuthorityPayloadSchemaRegistryV1
    {
        public AuthorityPayloadValidationV1 Validate(
            SchemaReferenceV1 schema,
            OwnerSliceId owner,
            ReadOnlySpan<byte> canonicalPayload,
            Hash256 payloadHash)
        {
            if (schema != expected) return AuthorityPayloadValidationV1.UnknownSchema;
            if (owner != OwnerSliceId.S1) return AuthorityPayloadValidationV1.OwnerMismatch;
            return payloadHash == Hash(canonicalPayload)
                ? AuthorityPayloadValidationV1.Exact
                : AuthorityPayloadValidationV1.HashMismatch;
        }

        public Hash256 Hash(ReadOnlySpan<byte> payload)
        {
            Span<byte> schema = stackalloc byte[16];
            expected.SchemaId.TryWriteBytes(schema);
            var input = new byte[schema.Length + 4 + payload.Length];
            schema.CopyTo(input);
            input[16] = (byte)(expected.Major >> 8);
            input[17] = (byte)expected.Major;
            input[18] = (byte)(expected.Minor >> 8);
            input[19] = (byte)expected.Minor;
            payload.CopyTo(input.AsSpan(20));
            return Hash256.Compute(input);
        }
    }

    private sealed class TestBatchCodec : IAuthorityAppendBatchCodecV1
    {
        public static TestBatchCodec Instance { get; } = new();

        public ulong GetEncodedLength(
            SessionAuthorityStampV1 session,
            long expectedSessionHead,
            IReadOnlyList<ThreadExpectedHeadV1> expectedThreadHeads,
            IReadOnlyList<ProposedAuthorityFactV1> facts) =>
            checked((ulong)(100 + expectedThreadHeads.Count * 40 + facts.Sum(static fact => 100 + fact.Payload.Count)));
    }
}
