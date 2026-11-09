using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GenerationInitializationRecordCodecsTests
{
    [Fact]
    public void All_ten_typed_initializations_round_trip_and_bind_distinct_schema_domains()
    {
        var session = Session();
        var graph = new GraphGenerationInitialized(session, GraphGenerationId.FromValue(Id(1)), OwnerSliceId.S2);
        var activity = new ActivityGenerationInitialized(session, ActivityGenerationId.FromValue(Id(2)), OwnerSliceId.S3);
        var turn = new TurnGenerationInitialized(session, TurnGenerationId.FromValue(Id(3)), OwnerSliceId.S4);
        var provider = new ProviderGenerationInitialized(session, ProviderGenerationId.FromValue(Id(4)), OwnerSliceId.S5);
        var output = new OutputGenerationInitialized(session, OutputGenerationId.FromValue(Id(5)), OwnerSliceId.S6);
        var sink = new SinkGenerationInitialized(session, SinkGenerationId.FromValue(Id(6)), OwnerSliceId.S6);
        var tool = new ToolGenerationInitialized(session, ToolGenerationId.FromValue(Id(7)), OwnerSliceId.S7);
        var route = new RouteGenerationInitialized(session, RouteGenerationId.FromValue(Id(8)), OwnerSliceId.S8);
        var privacy = new PrivacyGenerationInitialized(session, PrivacyGenerationId.FromValue(Id(9)), OwnerSliceId.S9);
        var transport = new TransportGenerationInitialized(session, TransportGenerationId.FromValue(Id(10)), OwnerSliceId.S11);

        AssertRoundTrip(graph, GenerationInitializationRecordCodecs.Encode, GenerationInitializationRecordCodecs.TryDecodeGraph);
        AssertRoundTrip(activity, GenerationInitializationRecordCodecs.Encode, GenerationInitializationRecordCodecs.TryDecodeActivity);
        AssertRoundTrip(turn, GenerationInitializationRecordCodecs.Encode, GenerationInitializationRecordCodecs.TryDecodeTurn);
        AssertRoundTrip(provider, GenerationInitializationRecordCodecs.Encode, GenerationInitializationRecordCodecs.TryDecodeProvider);
        AssertRoundTrip(output, GenerationInitializationRecordCodecs.Encode, GenerationInitializationRecordCodecs.TryDecodeOutput);
        AssertRoundTrip(sink, GenerationInitializationRecordCodecs.Encode, GenerationInitializationRecordCodecs.TryDecodeSink);
        AssertRoundTrip(tool, GenerationInitializationRecordCodecs.Encode, GenerationInitializationRecordCodecs.TryDecodeTool);
        AssertRoundTrip(route, GenerationInitializationRecordCodecs.Encode, GenerationInitializationRecordCodecs.TryDecodeRoute);
        AssertRoundTrip(privacy, GenerationInitializationRecordCodecs.Encode, GenerationInitializationRecordCodecs.TryDecodePrivacy);
        AssertRoundTrip(transport, GenerationInitializationRecordCodecs.Encode, GenerationInitializationRecordCodecs.TryDecodeTransport);

        var hashes = new[] { GenerationInitializationRecordCodecs.ComputeHash(graph), GenerationInitializationRecordCodecs.ComputeHash(activity), GenerationInitializationRecordCodecs.ComputeHash(turn), GenerationInitializationRecordCodecs.ComputeHash(provider), GenerationInitializationRecordCodecs.ComputeHash(output), GenerationInitializationRecordCodecs.ComputeHash(sink), GenerationInitializationRecordCodecs.ComputeHash(tool), GenerationInitializationRecordCodecs.ComputeHash(route), GenerationInitializationRecordCodecs.ComputeHash(privacy), GenerationInitializationRecordCodecs.ComputeHash(transport) };
        Assert.Equal(10, hashes.Distinct().Count());
    }

    [Fact]
    public void Decoder_rejects_noncanonical_trailing_and_wrong_projection()
    {
        var value = new GraphGenerationInitialized(Session(), GraphGenerationId.FromValue(Id(1)), OwnerSliceId.S2);
        var canonical = GenerationInitializationRecordCodecs.Encode(value);
        Assert.False(GenerationInitializationRecordCodecs.TryDecodeActivity(canonical, out _));
        Assert.False(GenerationInitializationRecordCodecs.TryDecodeGraph(canonical.Concat(new byte[] { 0 }).ToArray(), out _));
        Assert.False(GenerationInitializationRecordCodecs.TryDecodeGraph(new byte[] { 0xff }, out _));
    }

    private static void AssertRoundTrip<T>(T value, Func<T, byte[]> encode, TryDecode<T> decode) where T : class
    { var bytes = encode(value); Assert.True(decode(bytes, out var decoded)); Assert.Equal(value, decoded); }
    private delegate bool TryDecode<T>(ReadOnlyMemory<byte> bytes, out T? value) where T : class;
    private static SessionAuthorityStampV1 Session() => new(RuntimeGenerationId.FromValue(Id(20)), LiveSessionId.FromValue(Id(21)));
    private static StableId128 Id(byte seed) { Span<byte> bytes = stackalloc byte[16]; bytes.Fill(seed); return StableId128.FromBytes(bytes); }
}
