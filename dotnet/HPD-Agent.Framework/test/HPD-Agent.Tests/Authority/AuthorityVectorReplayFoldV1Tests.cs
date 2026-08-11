using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class AuthorityVectorReplayFoldV1Tests
{
    [Fact]
    public void Fold_InitializesThenAdvancesOnlyTheNamedSparseAxis()
    {
        var session = Session();
        var facts = new[]
        {
            Fact(1, session, AuthorityAxisId.Graph, OwnerSliceId.S2, EncodeInitialization(session, First, OwnerSliceId.S2), true),
            Fact(2, session, AuthorityAxisId.Graph, OwnerSliceId.S2, EncodeTransition(session, First, Second, OwnerSliceId.S2), false),
            UnknownFact(3, session),
        };

        var result = AuthorityVectorReplayFoldV1.Fold(session, facts);

        var current = Assert.IsType<AuthorityVectorReplayResultV1.Current>(result).Snapshot;
        Assert.Equal(3, current.ThroughPosition);
        var axis = Assert.Single(current.Axes);
        Assert.Equal(AuthorityAxisId.Graph, axis.AxisId);
        Assert.Equal(GraphGenerationId.FromValue(Second), Assert.IsType<AuthorityAxisValueV1.Graph>(axis.Value).Value);
    }

    [Fact]
    public void Fold_RejectsDuplicateInitializationTransitionBeforeInitializationAndGaps()
    {
        var session = Session();
        var initial = Fact(1, session, AuthorityAxisId.Graph, OwnerSliceId.S2, EncodeInitialization(session, First, OwnerSliceId.S2), true);
        var duplicate = Fact(2, session, AuthorityAxisId.Graph, OwnerSliceId.S2, EncodeInitialization(session, Second, OwnerSliceId.S2), true);
        var transition = Fact(1, session, AuthorityAxisId.Graph, OwnerSliceId.S2, EncodeTransition(session, First, Second, OwnerSliceId.S2), false);

        var duplicateResult = AuthorityVectorReplayFoldV1.Fold(session, [initial, duplicate]);
        Assert.IsType<AuthorityVectorReplayResultV1.InvalidHistory>(duplicateResult);
        Assert.IsType<AuthorityVectorReplayResultV1.InvalidHistory>(
            AuthorityVectorReplayFoldV1.Fold(session, [transition]));
        Assert.IsType<AuthorityVectorReplayResultV1.InvalidHistory>(
            AuthorityVectorReplayFoldV1.Fold(session, [UnknownFact(2, session)]));
    }

    [Fact]
    public void Fold_RejectsMalformedKnownFactAndWrongSessionOrOwner()
    {
        var session = Session();
        Assert.IsType<AuthorityVectorReplayResultV1.InvalidHistory>(AuthorityVectorReplayFoldV1.Fold(session,
            [Fact(1, session, AuthorityAxisId.Graph, OwnerSliceId.S2, [0xff], true)]));
        Assert.IsType<AuthorityVectorReplayResultV1.InvalidHistory>(AuthorityVectorReplayFoldV1.Fold(session,
            [Fact(1, session, AuthorityAxisId.Graph, OwnerSliceId.S3, EncodeInitialization(session, First, OwnerSliceId.S2), true)]));
        Assert.IsType<AuthorityVectorReplayResultV1.InvalidHistory>(AuthorityVectorReplayFoldV1.Fold(session,
            [Fact(1, OtherSession(), AuthorityAxisId.Graph, OwnerSliceId.S2, EncodeInitialization(OtherSession(), First, OwnerSliceId.S2), true)]));
    }

    [Fact]
    public void Fold_RejectsStaleAndNonadvancingTransitions()
    {
        var session = Session();
        var initial = Fact(1, session, AuthorityAxisId.Graph, OwnerSliceId.S2,
            EncodeInitialization(session, First, OwnerSliceId.S2), true);
        var stale = Fact(2, session, AuthorityAxisId.Graph, OwnerSliceId.S2,
            EncodeTransition(session, Second, First, OwnerSliceId.S2), false);
        var equal = Fact(2, session, AuthorityAxisId.Graph, OwnerSliceId.S2,
            EncodeTransition(session, First, First, OwnerSliceId.S2), false);

        Assert.IsType<AuthorityVectorReplayResultV1.InvalidHistory>(
            AuthorityVectorReplayFoldV1.Fold(session, [initial, stale]));
        Assert.IsType<AuthorityVectorReplayResultV1.InvalidHistory>(
            AuthorityVectorReplayFoldV1.Fold(session, [initial, equal]));
    }

    [Fact]
    public void Fold_RuntimeTransitionReportsReplacementAndOldStreamCannotContinue()
    {
        var session = Session();
        var replacement = StableId128.FromBytes(Convert.FromHexString("303132333435363738393a3b3c3d3e3f"));
        var transition = Fact(1, session, AuthorityAxisId.Runtime, OwnerSliceId.S1,
            EncodeTransition(session, Stable(session.RuntimeGenerationId), replacement, OwnerSliceId.S1), false);

        var replaced = AuthorityVectorReplayFoldV1.Fold(session, [transition]);
        var mapped = Assert.IsType<AuthorityVectorReplayResultV1.GenerationReplaced>(replaced);
        Assert.Equal(RuntimeGenerationId.FromValue(replacement), mapped.ReplacedBy);
        Assert.IsType<AuthorityVectorReplayResultV1.InvalidHistory>(
            AuthorityVectorReplayFoldV1.Fold(session, [transition, UnknownFact(2, session)]));
    }

    [Fact]
    public void Fold_EmptyHistoryIsKnownWithAllSparseAxesUninitialized()
    {
        var session = Session();
        var result = AuthorityVectorReplayFoldV1.Fold(session, []);
        var current = Assert.IsType<AuthorityVectorReplayResultV1.Current>(result).Snapshot;
        Assert.Empty(current.Axes);
        Assert.Equal(0, current.ThroughPosition);
    }

    private static readonly StableId128 First = StableId128.FromBytes(Convert.FromHexString("0102030405060708090a0b0c0d0e0f10"));
    private static readonly StableId128 Second = StableId128.FromBytes(Convert.FromHexString("1112131415161718191a1b1c1d1e1f20"));

    private static SessionAuthorityStampV1 Session() => new(
        RuntimeGenerationId.FromValue(StableId128.FromBytes(Convert.FromHexString("000102030405060708090a0b0c0d0e0f"))),
        LiveSessionId.FromValue(StableId128.FromBytes(Convert.FromHexString("202122232425262728292a2b2c2d2e2f"))));

    private static SessionAuthorityStampV1 OtherSession() => new(
        RuntimeGenerationId.FromValue(StableId128.FromBytes(Convert.FromHexString("404142434445464748494a4b4c4d4e4f"))),
        LiveSessionId.FromValue(StableId128.FromBytes(Convert.FromHexString("505152535455565758595a5b5c5d5e5f"))));

    private static StableId128 Stable(RuntimeGenerationId value)
    {
        Span<byte> bytes = stackalloc byte[16];
        Assert.True(value.TryWriteBytes(bytes));
        return StableId128.FromBytes(bytes);
    }

    private static AuthorityFactEnvelopeV1 Fact(
        long sequence,
        SessionAuthorityStampV1 session,
        AuthorityAxisId axis,
        OwnerSliceId owner,
        byte[] payload,
        bool initialization)
    {
        var schema = initialization
            ? AuthorityGenerationInitializationCodecV1.SchemaFor(axis)
            : AuthorityGenerationTransitionCodecV1.SchemaFor(axis);
        return Envelope(sequence, session, owner, schema, payload);
    }

    private static AuthorityFactEnvelopeV1 UnknownFact(long sequence, SessionAuthorityStampV1 session) =>
        Envelope(sequence, session, OwnerSliceId.S4, new SchemaReferenceV1(SchemaId.Create(), 1, 0), [0x80]);

    private static AuthorityFactEnvelopeV1 Envelope(
        long sequence,
        SessionAuthorityStampV1 session,
        OwnerSliceId owner,
        SchemaReferenceV1 schema,
        byte[] payload) => new(
            JournalFactId.Create(), new JournalPositionV1(session, sequence), null, owner, schema, payload,
            Hash256.Compute(payload), new CorrelationEnvelopeV1(TenantId.Create()), new UtcInstant(sequence),
            new UtcInstant(sequence), new IntegrityEnvelopeV1(1, 1, Hash256.Compute([1]), []));

    private static byte[] EncodeInitialization(SessionAuthorityStampV1 session, StableId128 initial, OwnerSliceId owner)
    {
        Span<byte> bytes = stackalloc byte[16];
        Assert.True(initial.TryWriteBytes(bytes));
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(3);
        writer.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(writer, session);
        writer.WriteUInt64(2); writer.WriteByteString(bytes);
        writer.WriteUInt64(3); writer.WriteUInt64((ushort)owner);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static byte[] EncodeTransition(
        SessionAuthorityStampV1 session,
        StableId128 expected,
        StableId128 proposed,
        OwnerSliceId owner)
    {
        Span<byte> expectedBytes = stackalloc byte[16];
        Span<byte> proposedBytes = stackalloc byte[16];
        Assert.True(expected.TryWriteBytes(expectedBytes));
        Assert.True(proposed.TryWriteBytes(proposedBytes));
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(4);
        writer.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(writer, session);
        writer.WriteUInt64(2); writer.WriteByteString(expectedBytes);
        writer.WriteUInt64(3); writer.WriteByteString(proposedBytes);
        writer.WriteUInt64(4); writer.WriteUInt64((ushort)owner);
        writer.WriteEndMap();
        return writer.Encode();
    }
}
