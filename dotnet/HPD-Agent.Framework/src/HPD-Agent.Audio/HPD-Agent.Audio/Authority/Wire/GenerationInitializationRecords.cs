using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal sealed record GraphGenerationInitialized { internal GraphGenerationInitialized(SessionAuthorityStampV1 session, GraphGenerationId initial, OwnerSliceId owner) { Validate(session, initial.IsValid, owner, OwnerSliceId.S2); Session=session; Initial=initial; Owner=owner; } internal SessionAuthorityStampV1 Session { get; } internal GraphGenerationId Initial { get; } internal OwnerSliceId Owner { get; } private static void Validate(SessionAuthorityStampV1 s, bool valid, OwnerSliceId o, OwnerSliceId expected) { if (!s.IsValid || !valid || o != expected) throw new ArgumentException("Invalid generation initialization."); } }
internal sealed record ActivityGenerationInitialized { internal ActivityGenerationInitialized(SessionAuthorityStampV1 s, ActivityGenerationId i, OwnerSliceId o) { if (!s.IsValid||!i.IsValid||o!=OwnerSliceId.S3) throw new ArgumentException("Invalid generation initialization."); Session=s; Initial=i; Owner=o; } internal SessionAuthorityStampV1 Session {get;} internal ActivityGenerationId Initial {get;} internal OwnerSliceId Owner {get;} }
internal sealed record TurnGenerationInitialized { internal TurnGenerationInitialized(SessionAuthorityStampV1 s, TurnGenerationId i, OwnerSliceId o) { if (!s.IsValid||!i.IsValid||o!=OwnerSliceId.S4) throw new ArgumentException("Invalid generation initialization."); Session=s; Initial=i; Owner=o; } internal SessionAuthorityStampV1 Session {get;} internal TurnGenerationId Initial {get;} internal OwnerSliceId Owner {get;} }
internal sealed record ProviderGenerationInitialized { internal ProviderGenerationInitialized(SessionAuthorityStampV1 s, ProviderGenerationId i, OwnerSliceId o) { if (!s.IsValid||!i.IsValid||o!=OwnerSliceId.S5) throw new ArgumentException("Invalid generation initialization."); Session=s; Initial=i; Owner=o; } internal SessionAuthorityStampV1 Session {get;} internal ProviderGenerationId Initial {get;} internal OwnerSliceId Owner {get;} }
internal sealed record OutputGenerationInitialized { internal OutputGenerationInitialized(SessionAuthorityStampV1 s, OutputGenerationId i, OwnerSliceId o) { if (!s.IsValid||!i.IsValid||o!=OwnerSliceId.S6) throw new ArgumentException("Invalid generation initialization."); Session=s; Initial=i; Owner=o; } internal SessionAuthorityStampV1 Session {get;} internal OutputGenerationId Initial {get;} internal OwnerSliceId Owner {get;} }
internal sealed record SinkGenerationInitialized { internal SinkGenerationInitialized(SessionAuthorityStampV1 s, SinkGenerationId i, OwnerSliceId o) { if (!s.IsValid||!i.IsValid||o!=OwnerSliceId.S6) throw new ArgumentException("Invalid generation initialization."); Session=s; Initial=i; Owner=o; } internal SessionAuthorityStampV1 Session {get;} internal SinkGenerationId Initial {get;} internal OwnerSliceId Owner {get;} }
internal sealed record ToolGenerationInitialized { internal ToolGenerationInitialized(SessionAuthorityStampV1 s, ToolGenerationId i, OwnerSliceId o) { if (!s.IsValid||!i.IsValid||o!=OwnerSliceId.S7) throw new ArgumentException("Invalid generation initialization."); Session=s; Initial=i; Owner=o; } internal SessionAuthorityStampV1 Session {get;} internal ToolGenerationId Initial {get;} internal OwnerSliceId Owner {get;} }
internal sealed record RouteGenerationInitialized { internal RouteGenerationInitialized(SessionAuthorityStampV1 s, RouteGenerationId i, OwnerSliceId o) { if (!s.IsValid||!i.IsValid||o!=OwnerSliceId.S8) throw new ArgumentException("Invalid generation initialization."); Session=s; Initial=i; Owner=o; } internal SessionAuthorityStampV1 Session {get;} internal RouteGenerationId Initial {get;} internal OwnerSliceId Owner {get;} }
internal sealed record PrivacyGenerationInitialized { internal PrivacyGenerationInitialized(SessionAuthorityStampV1 s, PrivacyGenerationId i, OwnerSliceId o) { if (!s.IsValid||!i.IsValid||o!=OwnerSliceId.S9) throw new ArgumentException("Invalid generation initialization."); Session=s; Initial=i; Owner=o; } internal SessionAuthorityStampV1 Session {get;} internal PrivacyGenerationId Initial {get;} internal OwnerSliceId Owner {get;} }
internal sealed record TransportGenerationInitialized { internal TransportGenerationInitialized(SessionAuthorityStampV1 s, TransportGenerationId i, OwnerSliceId o) { if (!s.IsValid||!i.IsValid||o!=OwnerSliceId.S11) throw new ArgumentException("Invalid generation initialization."); Session=s; Initial=i; Owner=o; } internal SessionAuthorityStampV1 Session {get;} internal TransportGenerationId Initial {get;} internal OwnerSliceId Owner {get;} }

internal static class GenerationInitializationRecordCodecs
{
    internal static byte[] Encode(GraphGenerationInitialized value) => Encode(value.Session, AuthorityAxisId.Graph, value.Initial, value.Owner, OwnerSliceId.S2);
    internal static byte[] Encode(ActivityGenerationInitialized value) => Encode(value.Session, AuthorityAxisId.Activity, value.Initial, value.Owner, OwnerSliceId.S3);
    internal static byte[] Encode(TurnGenerationInitialized value) => Encode(value.Session, AuthorityAxisId.Turn, value.Initial, value.Owner, OwnerSliceId.S4);
    internal static byte[] Encode(ProviderGenerationInitialized value) => Encode(value.Session, AuthorityAxisId.Provider, value.Initial, value.Owner, OwnerSliceId.S5);
    internal static byte[] Encode(OutputGenerationInitialized value) => Encode(value.Session, AuthorityAxisId.Output, value.Initial, value.Owner, OwnerSliceId.S6);
    internal static byte[] Encode(SinkGenerationInitialized value) => Encode(value.Session, AuthorityAxisId.Sink, value.Initial, value.Owner, OwnerSliceId.S6);
    internal static byte[] Encode(ToolGenerationInitialized value) => Encode(value.Session, AuthorityAxisId.Tool, value.Initial, value.Owner, OwnerSliceId.S7);
    internal static byte[] Encode(RouteGenerationInitialized value) => Encode(value.Session, AuthorityAxisId.Route, value.Initial, value.Owner, OwnerSliceId.S8);
    internal static byte[] Encode(PrivacyGenerationInitialized value) => Encode(value.Session, AuthorityAxisId.Privacy, value.Initial, value.Owner, OwnerSliceId.S9);
    internal static byte[] Encode(TransportGenerationInitialized value) => Encode(value.Session, AuthorityAxisId.Transport, value.Initial, value.Owner, OwnerSliceId.S11);

    internal static bool TryDecodeGraph(ReadOnlyMemory<byte> encoded, out GraphGenerationInitialized? value) => Decode(encoded, AuthorityAxisId.Graph, OwnerSliceId.S2, static (s, i, o) => new(s, GraphGenerationId.FromValue(i), o), Encode, out value);
    internal static bool TryDecodeActivity(ReadOnlyMemory<byte> encoded, out ActivityGenerationInitialized? value) => Decode(encoded, AuthorityAxisId.Activity, OwnerSliceId.S3, static (s, i, o) => new(s, ActivityGenerationId.FromValue(i), o), Encode, out value);
    internal static bool TryDecodeTurn(ReadOnlyMemory<byte> encoded, out TurnGenerationInitialized? value) => Decode(encoded, AuthorityAxisId.Turn, OwnerSliceId.S4, static (s, i, o) => new(s, TurnGenerationId.FromValue(i), o), Encode, out value);
    internal static bool TryDecodeProvider(ReadOnlyMemory<byte> encoded, out ProviderGenerationInitialized? value) => Decode(encoded, AuthorityAxisId.Provider, OwnerSliceId.S5, static (s, i, o) => new(s, ProviderGenerationId.FromValue(i), o), Encode, out value);
    internal static bool TryDecodeOutput(ReadOnlyMemory<byte> encoded, out OutputGenerationInitialized? value) => Decode(encoded, AuthorityAxisId.Output, OwnerSliceId.S6, static (s, i, o) => new(s, OutputGenerationId.FromValue(i), o), Encode, out value);
    internal static bool TryDecodeSink(ReadOnlyMemory<byte> encoded, out SinkGenerationInitialized? value) => Decode(encoded, AuthorityAxisId.Sink, OwnerSliceId.S6, static (s, i, o) => new(s, SinkGenerationId.FromValue(i), o), Encode, out value);
    internal static bool TryDecodeTool(ReadOnlyMemory<byte> encoded, out ToolGenerationInitialized? value) => Decode(encoded, AuthorityAxisId.Tool, OwnerSliceId.S7, static (s, i, o) => new(s, ToolGenerationId.FromValue(i), o), Encode, out value);
    internal static bool TryDecodeRoute(ReadOnlyMemory<byte> encoded, out RouteGenerationInitialized? value) => Decode(encoded, AuthorityAxisId.Route, OwnerSliceId.S8, static (s, i, o) => new(s, RouteGenerationId.FromValue(i), o), Encode, out value);
    internal static bool TryDecodePrivacy(ReadOnlyMemory<byte> encoded, out PrivacyGenerationInitialized? value) => Decode(encoded, AuthorityAxisId.Privacy, OwnerSliceId.S9, static (s, i, o) => new(s, PrivacyGenerationId.FromValue(i), o), Encode, out value);
    internal static bool TryDecodeTransport(ReadOnlyMemory<byte> encoded, out TransportGenerationInitialized? value) => Decode(encoded, AuthorityAxisId.Transport, OwnerSliceId.S11, static (s, i, o) => new(s, TransportGenerationId.FromValue(i), o), Encode, out value);

    internal static Hash256 ComputeHash(GraphGenerationInitialized value) => Hash(AuthorityAxisId.Graph, Encode(value));
    internal static Hash256 ComputeHash(ActivityGenerationInitialized value) => Hash(AuthorityAxisId.Activity, Encode(value));
    internal static Hash256 ComputeHash(TurnGenerationInitialized value) => Hash(AuthorityAxisId.Turn, Encode(value));
    internal static Hash256 ComputeHash(ProviderGenerationInitialized value) => Hash(AuthorityAxisId.Provider, Encode(value));
    internal static Hash256 ComputeHash(OutputGenerationInitialized value) => Hash(AuthorityAxisId.Output, Encode(value));
    internal static Hash256 ComputeHash(SinkGenerationInitialized value) => Hash(AuthorityAxisId.Sink, Encode(value));
    internal static Hash256 ComputeHash(ToolGenerationInitialized value) => Hash(AuthorityAxisId.Tool, Encode(value));
    internal static Hash256 ComputeHash(RouteGenerationInitialized value) => Hash(AuthorityAxisId.Route, Encode(value));
    internal static Hash256 ComputeHash(PrivacyGenerationInitialized value) => Hash(AuthorityAxisId.Privacy, Encode(value));
    internal static Hash256 ComputeHash(TransportGenerationInitialized value) => Hash(AuthorityAxisId.Transport, Encode(value));

    private static byte[] Encode<T>(SessionAuthorityStampV1 session, AuthorityAxisId axis, T initial, OwnerSliceId owner, OwnerSliceId expectedOwner)
    {
        if (!session.IsValid || owner != expectedOwner) throw new ArgumentException("Invalid generation initialization.");
        Span<byte> bytes = stackalloc byte[16];
        var valid = initial switch
        {
            GraphGenerationId x => x.TryWriteBytes(bytes), ActivityGenerationId x => x.TryWriteBytes(bytes),
            TurnGenerationId x => x.TryWriteBytes(bytes), ProviderGenerationId x => x.TryWriteBytes(bytes),
            OutputGenerationId x => x.TryWriteBytes(bytes), SinkGenerationId x => x.TryWriteBytes(bytes),
            ToolGenerationId x => x.TryWriteBytes(bytes), RouteGenerationId x => x.TryWriteBytes(bytes),
            PrivacyGenerationId x => x.TryWriteBytes(bytes), TransportGenerationId x => x.TryWriteBytes(bytes), _ => false,
        };
        if (!valid) throw new ArgumentException("An initial generation is required.", nameof(initial));
        return AuthorityGenerationInitializationCodecV1.Encode(session, axis, StableId128.FromBytes(bytes));
    }

    private static bool Decode<T>(ReadOnlyMemory<byte> encoded, AuthorityAxisId axis, OwnerSliceId owner,
        Func<SessionAuthorityStampV1, StableId128, OwnerSliceId, T> create, Func<T, byte[]> encode, out T? value) where T : class
    {
        value = null;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 3 || reader.ReadUInt64() != 1) return false;
            var session = SessionAuthorityStampV1Codec.Read(reader);
            if (AuthorityGenerationInitializationCodecV1.Decode(AuthorityGenerationInitializationCodecV1.SchemaFor(axis), owner, session, encoded, out var decoded) != AuthorityGenerationInitializationDecodeV1.Valid) return false;
            var candidate = create(decoded.Session, decoded.Initial, decoded.Owner);
            if (!encode(candidate).AsSpan().SequenceEqual(encoded.Span)) return false;
            value = candidate;
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException) { return false; }
    }

    private static Hash256 Hash(AuthorityAxisId axis, byte[] bytes) => AuthorityIntegrityHashV1.Compute(AuthorityGenerationInitializationCodecV1.SchemaTokenFor(axis).ToString(), 1, 0, bytes);
}
