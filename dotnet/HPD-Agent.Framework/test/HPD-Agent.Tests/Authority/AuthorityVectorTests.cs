using HPD.Agent.Authority;
using System.Formats.Cbor;

namespace HPD.Agent.Tests.Authority;

public sealed class AuthorityVectorTests
{
    private static readonly StableId128 First = StableId128.FromBytes(Convert.FromHexString("000102030405060708090a0b0c0d0e0f"));
    private static readonly StableId128 Second = StableId128.FromBytes(Convert.FromHexString("101112131415161718191a1b1c1d1e1f"));

    [Fact]
    public void Vector_SortsDistinctTypedAxesAndRoundTrips()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(First), LiveSessionId.FromValue(Second));
        var vector = ExpectedAuthorityVectorV1.Create(session,
        [
            new AuthorityAxisValueV1.Route(RouteGenerationId.FromValue(Second)),
            new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(First)),
        ]);

        Assert.Equal([AuthorityAxisId.Graph, AuthorityAxisId.Route], vector.Axes.Select(entry => entry.AxisId));
        var encoded = AuthorityVectorCodecsV1.Encode(vector);
        Assert.True(AuthorityVectorCodecsV1.TryDecodeVector(encoded, out var decoded));
        Assert.Equal(vector, decoded);
        Assert.True(vector == decoded);
        Assert.False(vector != decoded);
        Assert.True(vector == ExpectedAuthorityVectorV1.Create(session,
        [
            new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(First)),
            new AuthorityAxisValueV1.Route(RouteGenerationId.FromValue(Second)),
        ]));
        Assert.Equal("a201a20150000102030405060708090a0b0c0d0e0f0250101112131415161718191a1b1c1d1e1f0282a201020250000102030405060708090a0b0c0d0e0fa201090250101112131415161718191a1b1c1d1e1f", Convert.ToHexString(encoded).ToLowerInvariant());
    }

    [Fact]
    public void Vector_RejectsDuplicateAxisAndInvalidSession()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(First), LiveSessionId.FromValue(Second));
        var graph = new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(First));

        Assert.Throws<ArgumentException>(() => ExpectedAuthorityVectorV1.Create(session, [graph, graph]));
        Assert.Throws<ArgumentException>(() => ExpectedAuthorityVectorV1.Create(default, [graph]));
        Assert.Throws<ArgumentNullException>(() => ExpectedAuthorityVectorV1.Create(session, null!));
    }

    [Fact]
    public void SparseAxis_RejectsRuntimeAxisAndUnknownAxis()
    {
        var runtime = "a201010250000102030405060708090a0b0c0d0e0f";
        var unknown = "a2010c0250000102030405060708090a0b0c0d0e0f";

        Assert.Throws<CborContentException>(() => ReadAxis(runtime));
        Assert.Throws<CborContentException>(() => ReadAxis(unknown));
    }

    [Fact]
    public void EveryRegisteredSparseAxis_RoundTripsWithItsSemanticWrapper()
    {
        AuthorityAxisValueV1[] values =
        [
            new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(First)),
            new AuthorityAxisValueV1.Activity(ActivityGenerationId.FromValue(First)),
            new AuthorityAxisValueV1.Turn(TurnGenerationId.FromValue(First)),
            new AuthorityAxisValueV1.Provider(ProviderGenerationId.FromValue(First)),
            new AuthorityAxisValueV1.Output(OutputGenerationId.FromValue(First)),
            new AuthorityAxisValueV1.Sink(SinkGenerationId.FromValue(First)),
            new AuthorityAxisValueV1.Tool(ToolGenerationId.FromValue(First)),
            new AuthorityAxisValueV1.Route(RouteGenerationId.FromValue(First)),
            new AuthorityAxisValueV1.Privacy(PrivacyGenerationId.FromValue(First)),
            new AuthorityAxisValueV1.Transport(TransportGenerationId.FromValue(First)),
        ];

        Assert.Equal(Enumerable.Range(2, 10).Select(value => (AuthorityAxisId)value), values.Select(value => value.AxisId));
        foreach (var value in values)
        {
            var entry = new AxisEntryV1(value);
            var reader = new CborReader(AuthorityVectorCodecsV1.Encode(entry), CborConformanceMode.Ctap2Canonical);
            Assert.Equal(entry, AuthorityVectorCodecsV1.Read(reader));
            Assert.Equal(0, reader.BytesRemaining);
        }
    }

    [Fact]
    public void Decoder_RejectsSemanticallyUnsortedAxes()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(First), LiveSessionId.FromValue(Second));
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(2);
        writer.WriteUInt64(1);
        SessionAuthorityStampV1Codec.Write(writer, session);
        writer.WriteUInt64(2);
        writer.WriteStartArray(2);
        AuthorityVectorCodecsV1.Write(writer, new(new AuthorityAxisValueV1.Route(RouteGenerationId.FromValue(Second))));
        AuthorityVectorCodecsV1.Write(writer, new(new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(First))));
        writer.WriteEndArray();
        writer.WriteEndMap();

        Assert.False(AuthorityVectorCodecsV1.TryDecodeVector(writer.Encode(), out _));
    }

    private static AxisEntryV1 ReadAxis(string hex)
    {
        var reader = new System.Formats.Cbor.CborReader(Convert.FromHexString(hex), System.Formats.Cbor.CborConformanceMode.Ctap2Canonical);
        return AuthorityVectorCodecsV1.Read(reader);
    }
}
