using System.Formats.Cbor;

namespace HPD.Agent.Authority;

internal enum AuthorityGenerationTransitionDecodeV1
{
    NotTransition = 0,
    Valid = 1,
    Invalid = 2,
}

internal readonly record struct DecodedAuthorityGenerationTransitionV1(
    SessionAuthorityStampV1 Session,
    AuthorityAxisId Axis,
    StableId128 ExpectedPrevious,
    StableId128 ProposedNext,
    OwnerSliceId Owner);

internal static class AuthorityGenerationTransitionCodecV1
{
    private sealed record Descriptor(
        string SchemaToken,
        SchemaReferenceV1 Schema,
        AuthorityAxisId Axis,
        OwnerSliceId Owner);

    private static readonly Descriptor[] Descriptors =
    [
        Create("hpd.runtime-generation-changed.v1", AuthorityAxisId.Runtime, OwnerSliceId.S1),
        Create("hpd.graph-generation-changed.v1", AuthorityAxisId.Graph, OwnerSliceId.S2),
        Create("hpd.activity-generation-changed.v1", AuthorityAxisId.Activity, OwnerSliceId.S3),
        Create("hpd.turn-generation-changed.v1", AuthorityAxisId.Turn, OwnerSliceId.S4),
        Create("hpd.provider-generation-changed.v1", AuthorityAxisId.Provider, OwnerSliceId.S5),
        Create("hpd.output-generation-changed.v1", AuthorityAxisId.Output, OwnerSliceId.S6),
        Create("hpd.sink-generation-changed.v1", AuthorityAxisId.Sink, OwnerSliceId.S6),
        Create("hpd.tool-generation-changed.v1", AuthorityAxisId.Tool, OwnerSliceId.S7),
        Create("hpd.route-generation-changed.v1", AuthorityAxisId.Route, OwnerSliceId.S8),
        Create("hpd.privacy-generation-changed.v1", AuthorityAxisId.Privacy, OwnerSliceId.S9),
        Create("hpd.transport-generation-changed.v1", AuthorityAxisId.Transport, OwnerSliceId.S11),
    ];

    internal static AuthorityGenerationTransitionDecodeV1 Decode(
        SchemaReferenceV1 schema,
        OwnerSliceId envelopeOwner,
        SessionAuthorityStampV1 positionSession,
        ReadOnlyMemory<byte> payload,
        out DecodedAuthorityGenerationTransitionV1 transition)
    {
        transition = default;
        var descriptor = Descriptors.SingleOrDefault(row => row.Schema == schema);
        if (descriptor is null) return AuthorityGenerationTransitionDecodeV1.NotTransition;
        if (envelopeOwner != descriptor.Owner || !positionSession.IsValid)
            return AuthorityGenerationTransitionDecodeV1.Invalid;
        try
        {
            var reader = new CborReader(payload, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 4 || reader.ReadUInt64() != 1)
                return AuthorityGenerationTransitionDecodeV1.Invalid;
            var session = SessionAuthorityStampV1Codec.Read(reader);
            if (reader.ReadUInt64() != 2) return AuthorityGenerationTransitionDecodeV1.Invalid;
            var expected = ReadStableId(reader);
            if (reader.ReadUInt64() != 3) return AuthorityGenerationTransitionDecodeV1.Invalid;
            var proposed = ReadStableId(reader);
            if (reader.ReadUInt64() != 4) return AuthorityGenerationTransitionDecodeV1.Invalid;
            var rawOwner = reader.ReadUInt64();
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0 || session != positionSession || rawOwner != (ushort)descriptor.Owner ||
                expected.Equals(proposed))
                return AuthorityGenerationTransitionDecodeV1.Invalid;
            transition = new(session, descriptor.Axis, expected, proposed, descriptor.Owner);
            return AuthorityGenerationTransitionDecodeV1.Valid;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException)
        {
            transition = default;
            return AuthorityGenerationTransitionDecodeV1.Invalid;
        }
    }

    internal static SchemaReferenceV1 SchemaFor(AuthorityAxisId axis) =>
        Descriptors.Single(row => row.Axis == axis).Schema;

    internal static OwnerSliceId OwnerFor(AuthorityAxisId axis) =>
        Descriptors.Single(row => row.Axis == axis).Owner;

    internal static BoundedAscii SchemaTokenFor(AuthorityAxisId axis) =>
        new(Descriptors.Single(row => row.Axis == axis).SchemaToken);

    private static Descriptor Create(string token, AuthorityAxisId axis, OwnerSliceId owner)
    {
        var expectedLedgerRow = $"{(ushort)axis}|{token}|{AxisType(axis).Replace("Id", "ChangedV1", StringComparison.Ordinal)}|{AxisType(axis)}|{owner}";
        if (!AuthoritySchemaLedgerV1.GenerationTransitionSchemas.Contains(expectedLedgerRow, StringComparer.Ordinal))
            throw new InvalidOperationException($"The generation transition registry does not contain the exact {axis} schema tuple.");
        var bounded = new BoundedAscii(token);
        return new(token, new SchemaReferenceV1(AuthoritySchemaIdentityV1.Derive(bounded), 1, 0), axis, owner);
    }

    private static string AxisType(AuthorityAxisId axis) => axis switch
    {
        AuthorityAxisId.Runtime => nameof(RuntimeGenerationId),
        AuthorityAxisId.Graph => nameof(GraphGenerationId),
        AuthorityAxisId.Activity => nameof(ActivityGenerationId),
        AuthorityAxisId.Turn => nameof(TurnGenerationId),
        AuthorityAxisId.Provider => nameof(ProviderGenerationId),
        AuthorityAxisId.Output => nameof(OutputGenerationId),
        AuthorityAxisId.Sink => nameof(SinkGenerationId),
        AuthorityAxisId.Tool => nameof(ToolGenerationId),
        AuthorityAxisId.Route => nameof(RouteGenerationId),
        AuthorityAxisId.Privacy => nameof(PrivacyGenerationId),
        AuthorityAxisId.Transport => nameof(TransportGenerationId),
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };

    private static StableId128 ReadStableId(CborReader reader)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!reader.TryReadByteString(bytes, out var written) || written != 16)
            throw new CborContentException("A generation identifier is exactly 16 bytes.");
        return StableId128.FromBytes(bytes);
    }
}
