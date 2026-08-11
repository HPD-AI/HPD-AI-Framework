using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class AuthorityJournalPortV1Tests
{
    [Fact]
    public void Envelope_OwnsPayloadAndKeepsIndependentPositions()
    {
        var payload = new byte[] { 1, 2, 3 };
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var thread = ThreadId.Create();
        var envelope = new AuthorityFactEnvelopeV1(
            JournalFactId.Create(), new JournalPositionV1(session, 7), new ThreadPositionV1(thread, 2, 4),
            OwnerSliceId.S4, new SchemaReferenceV1(SchemaId.Create(), 1, 0), payload, Hash256.Compute(payload),
            new CorrelationEnvelopeV1(TenantId.Create(), threadId: thread), new UtcInstant(10), new UtcInstant(11),
            new IntegrityEnvelopeV1(1, 1, Hash256.Compute([9]), []));

        payload[0] = 99;

        Assert.Equal(7, envelope.Position.Sequence);
        Assert.Equal(4, envelope.ThreadScope!.Value.Sequence);
        Assert.Equal(new byte[] { 1, 2, 3 }, envelope.Payload);
        Assert.Equal((ushort)1, AuthorityFactEnvelopeV1.SchemaVersion);
    }

    [Fact]
    public void AppendResult_IsClosedAndSeparatesDurableFromAmbiguousOutcomes()
    {
        Assert.True(typeof(AppendAuthorityResultV1).IsAbstract);
        Assert.Equal(10, typeof(AppendAuthorityResultV1).GetNestedTypes().Count(static type => !type.IsAbstract));
        Assert.IsType<AppendAuthorityResultV1.StoreUnavailable>(
            new AppendAuthorityResultV1.StoreUnavailable(new BoundedAscii("store-unavailable")));
        Assert.IsType<AppendAuthorityResultV1.OutcomeUnknown>(
            new AppendAuthorityResultV1.OutcomeUnknown(OperationId.Create()));
    }

    [Fact]
    public void Committed_RejectsNoncontiguousOrCrossSessionEnvelopes()
    {
        var firstSession = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var otherSession = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        Assert.Throws<ArgumentException>(() => new AppendAuthorityResultV1.Committed(0, 2,
            [Envelope(firstSession, 1), Envelope(otherSession, 2)]));
        Assert.Throws<ArgumentException>(() => new AppendAuthorityResultV1.Committed(0, 2,
            [Envelope(firstSession, 1), Envelope(firstSession, 3)]));
    }

    [Fact]
    public void AlreadyCommitted_StopsBeforeItem257()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppendAuthorityResultV1.AlreadyCommitted(
            Enumerable.Range(1, 257).Select(sequence => Envelope(session, sequence))));
    }

    private static AuthorityFactEnvelopeV1 Envelope(SessionAuthorityStampV1 session, int sequence)
    {
        var payload = new byte[] { 1 };
        return new AuthorityFactEnvelopeV1(
            JournalFactId.Create(), new JournalPositionV1(session, sequence), null, OwnerSliceId.S1,
            new SchemaReferenceV1(SchemaId.Create(), 1, 0), payload, Hash256.Compute(payload),
            new CorrelationEnvelopeV1(TenantId.Create()), new UtcInstant(1), new UtcInstant(2),
            new IntegrityEnvelopeV1(1, 1, Hash256.Compute([2]), []));
    }
}
