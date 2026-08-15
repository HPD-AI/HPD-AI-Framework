using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GenerationInitializationRecordCodecsV1Tests
{
    [Fact]
    public void All_ten_typed_initializations_round_trip_and_bind_distinct_schema_domains()
    {
        var session = Session();
        var graph = new GraphGenerationInitializedV1(session, GraphGenerationId.FromValue(Id(1)), OwnerSliceId.S2);
        var activity = new ActivityGenerationInitializedV1(session, ActivityGenerationId.FromValue(Id(2)), OwnerSliceId.S3);
        var turn = new TurnGenerationInitializedV1(session, TurnGenerationId.FromValue(Id(3)), OwnerSliceId.S4);
        var provider = new ProviderGenerationInitializedV1(session, ProviderGenerationId.FromValue(Id(4)), OwnerSliceId.S5);
        var output = new OutputGenerationInitializedV1(session, OutputGenerationId.FromValue(Id(5)), OwnerSliceId.S6);
        var sink = new SinkGenerationInitializedV1(session, SinkGenerationId.FromValue(Id(6)), OwnerSliceId.S6);
        var tool = new ToolGenerationInitializedV1(session, ToolGenerationId.FromValue(Id(7)), OwnerSliceId.S7);
        var route = new RouteGenerationInitializedV1(session, RouteGenerationId.FromValue(Id(8)), OwnerSliceId.S8);
        var privacy = new PrivacyGenerationInitializedV1(session, PrivacyGenerationId.FromValue(Id(9)), OwnerSliceId.S9);
        var transport = new TransportGenerationInitializedV1(session, TransportGenerationId.FromValue(Id(10)), OwnerSliceId.S11);

        AssertRoundTrip(graph, GenerationInitializationRecordCodecsV1.Encode, GenerationInitializationRecordCodecsV1.TryDecodeGraph);
        AssertRoundTrip(activity, GenerationInitializationRecordCodecsV1.Encode, GenerationInitializationRecordCodecsV1.TryDecodeActivity);
        AssertRoundTrip(turn, GenerationInitializationRecordCodecsV1.Encode, GenerationInitializationRecordCodecsV1.TryDecodeTurn);
        AssertRoundTrip(provider, GenerationInitializationRecordCodecsV1.Encode, GenerationInitializationRecordCodecsV1.TryDecodeProvider);
        AssertRoundTrip(output, GenerationInitializationRecordCodecsV1.Encode, GenerationInitializationRecordCodecsV1.TryDecodeOutput);
        AssertRoundTrip(sink, GenerationInitializationRecordCodecsV1.Encode, GenerationInitializationRecordCodecsV1.TryDecodeSink);
        AssertRoundTrip(tool, GenerationInitializationRecordCodecsV1.Encode, GenerationInitializationRecordCodecsV1.TryDecodeTool);
        AssertRoundTrip(route, GenerationInitializationRecordCodecsV1.Encode, GenerationInitializationRecordCodecsV1.TryDecodeRoute);
        AssertRoundTrip(privacy, GenerationInitializationRecordCodecsV1.Encode, GenerationInitializationRecordCodecsV1.TryDecodePrivacy);
        AssertRoundTrip(transport, GenerationInitializationRecordCodecsV1.Encode, GenerationInitializationRecordCodecsV1.TryDecodeTransport);

        var hashes = new[] { GenerationInitializationRecordCodecsV1.ComputeHash(graph), GenerationInitializationRecordCodecsV1.ComputeHash(activity), GenerationInitializationRecordCodecsV1.ComputeHash(turn), GenerationInitializationRecordCodecsV1.ComputeHash(provider), GenerationInitializationRecordCodecsV1.ComputeHash(output), GenerationInitializationRecordCodecsV1.ComputeHash(sink), GenerationInitializationRecordCodecsV1.ComputeHash(tool), GenerationInitializationRecordCodecsV1.ComputeHash(route), GenerationInitializationRecordCodecsV1.ComputeHash(privacy), GenerationInitializationRecordCodecsV1.ComputeHash(transport) };
        Assert.Equal(10, hashes.Distinct().Count());
    }

    [Fact]
    public void Decoder_rejects_noncanonical_trailing_and_wrong_projection()
    {
        var value = new GraphGenerationInitializedV1(Session(), GraphGenerationId.FromValue(Id(1)), OwnerSliceId.S2);
        var canonical = GenerationInitializationRecordCodecsV1.Encode(value);
        Assert.False(GenerationInitializationRecordCodecsV1.TryDecodeActivity(canonical, out _));
        Assert.False(GenerationInitializationRecordCodecsV1.TryDecodeGraph(canonical.Concat(new byte[] { 0 }).ToArray(), out _));
        Assert.False(GenerationInitializationRecordCodecsV1.TryDecodeGraph(new byte[] { 0xff }, out _));
    }

    private static void AssertRoundTrip<T>(T value, Func<T, byte[]> encode, TryDecode<T> decode) where T : class
    { var bytes = encode(value); Assert.True(decode(bytes, out var decoded)); Assert.Equal(value, decoded); }
    private delegate bool TryDecode<T>(ReadOnlyMemory<byte> bytes, out T? value) where T : class;
    private static SessionAuthorityStampV1 Session() => new(RuntimeGenerationId.FromValue(Id(20)), LiveSessionId.FromValue(Id(21)));
    private static StableId128 Id(byte seed) { Span<byte> bytes = stackalloc byte[16]; bytes.Fill(seed); return StableId128.FromBytes(bytes); }
}
