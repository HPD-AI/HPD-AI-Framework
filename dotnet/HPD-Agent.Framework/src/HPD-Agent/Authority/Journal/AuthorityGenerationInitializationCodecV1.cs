using System.Formats.Cbor;

namespace HPD.Agent.Authority;

internal enum AuthorityGenerationInitializationDecodeV1
{
    NotInitialization = 0,
    Valid = 1,
    Invalid = 2,
}

internal readonly record struct DecodedAuthorityGenerationInitializationV1(
    SessionAuthorityStampV1 Session,
    AuthorityAxisId Axis,
    StableId128 Initial,
    OwnerSliceId Owner);

internal static class AuthorityGenerationInitializationCodecV1
{
    private sealed record Descriptor(SchemaReferenceV1 Schema, AuthorityAxisId Axis, OwnerSliceId Owner);

    private static readonly Descriptor[] Descriptors =
    [
        Create("hpd.graph-generation-initialized.v1", AuthorityAxisId.Graph, OwnerSliceId.S2),
        Create("hpd.activity-generation-initialized.v1", AuthorityAxisId.Activity, OwnerSliceId.S3),
        Create("hpd.turn-generation-initialized.v1", AuthorityAxisId.Turn, OwnerSliceId.S4),
        Create("hpd.provider-generation-initialized.v1", AuthorityAxisId.Provider, OwnerSliceId.S5),
        Create("hpd.output-generation-initialized.v1", AuthorityAxisId.Output, OwnerSliceId.S6),
        Create("hpd.sink-generation-initialized.v1", AuthorityAxisId.Sink, OwnerSliceId.S6),
        Create("hpd.tool-generation-initialized.v1", AuthorityAxisId.Tool, OwnerSliceId.S7),
        Create("hpd.route-generation-initialized.v1", AuthorityAxisId.Route, OwnerSliceId.S8),
        Create("hpd.privacy-generation-initialized.v1", AuthorityAxisId.Privacy, OwnerSliceId.S9),
        Create("hpd.transport-generation-initialized.v1", AuthorityAxisId.Transport, OwnerSliceId.S11),
    ];

    internal static AuthorityGenerationInitializationDecodeV1 Decode(
        SchemaReferenceV1 schema,
        OwnerSliceId envelopeOwner,
        SessionAuthorityStampV1 positionSession,
        ReadOnlyMemory<byte> payload,
        out DecodedAuthorityGenerationInitializationV1 initialization)
    {
        initialization = default;
        var descriptor = Descriptors.SingleOrDefault(row => row.Schema == schema);
        if (descriptor is null) return AuthorityGenerationInitializationDecodeV1.NotInitialization;
        if (envelopeOwner != descriptor.Owner || !positionSession.IsValid)
            return AuthorityGenerationInitializationDecodeV1.Invalid;
        try
        {
            var reader = new CborReader(payload, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 3 || reader.ReadUInt64() != 1)
                return AuthorityGenerationInitializationDecodeV1.Invalid;
            var session = SessionAuthorityStampV1Codec.Read(reader);
            if (reader.ReadUInt64() != 2) return AuthorityGenerationInitializationDecodeV1.Invalid;
            var initial = ReadStableId(reader);
            if (reader.ReadUInt64() != 3) return AuthorityGenerationInitializationDecodeV1.Invalid;
            var rawOwner = reader.ReadUInt64();
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0 || session != positionSession || rawOwner != (ushort)descriptor.Owner)
                return AuthorityGenerationInitializationDecodeV1.Invalid;
            initialization = new(session, descriptor.Axis, initial, descriptor.Owner);
            return AuthorityGenerationInitializationDecodeV1.Valid;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException)
        {
            initialization = default;
            return AuthorityGenerationInitializationDecodeV1.Invalid;
        }
    }

    internal static SchemaReferenceV1 SchemaFor(AuthorityAxisId axis) =>
        Descriptors.Single(row => row.Axis == axis).Schema;

    internal static OwnerSliceId OwnerFor(AuthorityAxisId axis) =>
        Descriptors.Single(row => row.Axis == axis).Owner;

    internal static BoundedAscii SchemaTokenFor(AuthorityAxisId axis) =>
        new(AuthoritySchemaLedgerV1.GenerationInitializationSchemas
            .Single(row => row.StartsWith($"{(ushort)axis - 1}|", StringComparison.Ordinal))
            .Split('|')[1]);

    private static Descriptor Create(string token, AuthorityAxisId axis, OwnerSliceId owner)
    {
        var axisType = AxisType(axis);
        var payloadType = axisType.Replace("Id", "InitializedV1", StringComparison.Ordinal);
        var expectedLedgerRow = $"{(ushort)axis - 1}|{token}|{payloadType}|{axisType}|{owner}";
        if (!AuthoritySchemaLedgerV1.GenerationInitializationSchemas.Contains(expectedLedgerRow, StringComparer.Ordinal))
            throw new InvalidOperationException($"The generation initialization registry does not contain the exact {axis} schema tuple.");
        var bounded = new BoundedAscii(token);
        return new(new SchemaReferenceV1(AuthoritySchemaIdentityV1.Derive(bounded), 1, 0), axis, owner);
    }

    private static string AxisType(AuthorityAxisId axis) => axis switch
    {
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
