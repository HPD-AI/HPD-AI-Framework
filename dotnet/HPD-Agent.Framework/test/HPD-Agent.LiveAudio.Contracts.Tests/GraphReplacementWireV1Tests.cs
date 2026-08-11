using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;
using System.Formats.Cbor;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphReplacementWireV1Tests
{
    [Fact]
    public void TopologyCodec_HasCanonicalGoldenAndRoundTrips()
    {
        var plan = Plan();
        var encoded = GraphTopologyPlanCodecV1.Encode(plan);
        Assert.Equal("a6018250010101010101010101010101010101015002020202020202020202020202020202025005050505050505050505050505050505035006060606060606060606060606060606044e8265696e707574666f7574707574054f818265696e707574666f75747075740643820103", Convert.ToHexString(encoded).ToLowerInvariant());
        Assert.True(GraphTopologyPlanCodecV1.TryDecode(encoded, out var decoded));
        Assert.NotNull(decoded);
        Assert.Equal(plan.Fingerprint, decoded.Fingerprint);
        Assert.Equal(plan.Nodes.Select(value => value.Key), decoded.Nodes.Select(value => value.Key));
        Assert.Equal(plan.Edges, decoded.Edges);
        Assert.Equal(plan.CapacityDimensions, decoded.CapacityDimensions);
    }

    [Fact]
    public void TopologyCodec_RejectsTrailingAndNoncanonicalInput()
    {
        var encoded = GraphTopologyPlanCodecV1.Encode(Plan());
        Assert.False(GraphTopologyPlanCodecV1.TryDecode(encoded.Concat(new byte[] { 0 }).ToArray(), out _));
        Assert.False(GraphTopologyPlanCodecV1.TryDecode(ReadOnlyMemory<byte>.Empty, out _));
        Assert.False(GraphTopologyPlanCodecV1.TryDecode(new byte[GraphTopologyPlanCodecV1.MaximumEncodedBytes + 1], out _));
    }

    [Fact]
    public void TopologyCodec_RejectsUnsortedAndTrailingNestedValues()
    {
        var plan = Plan();
        Assert.False(GraphTopologyPlanCodecV1.TryDecode(EncodeWithNodes(plan, ["output", "input"]), out _));
        Assert.False(GraphTopologyPlanCodecV1.TryDecode(EncodeWithNodes(plan, ["input", "output"], trailing: true), out _));
    }

    [Fact]
    public void FactIdentities_HaveFrozenGoldensAndSeparateDomains()
    {
        var session = Session();
        var operation = OperationId.FromValue(Id(3));
        var position = new JournalPositionV1(session, 17);
        var fingerprint = Hash256.FromBytes(Enumerable.Repeat((byte)4, 32).ToArray());

        var prepare = GraphReplacementFactIdsV1.Command(session, operation, 1);
        var commit = GraphReplacementFactIdsV1.Command(session, operation, 2);
        var settle = GraphReplacementFactIdsV1.Command(session, operation, 3);
        var result = GraphReplacementFactIdsV1.Result(position);
        var installed = GraphReplacementFactIdsV1.Installed(session, fingerprint);
        var transition = GraphReplacementFactIdsV1.Transition(position);
        Assert.Equal("fct:1NC63NM93ET27KJ23TVH8QSKEK", prepare.ToString());
        Assert.Equal("fct:6WDCWAWJ16Q0C4RGR7Q0KSMZT0", commit.ToString());
        Assert.Equal("fct:56BTDMK17ECV72QHZZ9AEHZ5ER", settle.ToString());
        Assert.Equal("fct:6E98AT9YD5KEBWDQXJ82JMEVY0", result.ToString());
        Assert.Equal("fct:03EP75XZMN3R0919RC1TJGSMEY", installed.ToString());
        Assert.Equal("fct:43HQ6QD4KYD6C0E37A3AKM4RVE", transition.ToString());
        Assert.Equal(6, new[] { prepare, commit, settle, result, installed, transition }.Distinct().Count());
        Assert.Throws<ArgumentException>(() => GraphReplacementFactIdsV1.Command(session, operation, 0));
        Assert.Throws<ArgumentException>(() => GraphReplacementFactIdsV1.Command(session, operation, 4));
        Assert.Throws<ArgumentException>(() => GraphReplacementFactIdsV1.Transition(default));
    }

    private static GraphTopologyPlanV1 Plan() => new(Session(), GraphGenerationId.FromValue(Id(5)),
        CapacityGrantId.FromValue(Id(6)),
        [new GraphTopologyNodeV1(new BoundedAscii("input")), new GraphTopologyNodeV1(new BoundedAscii("output"))],
        [new GraphTopologyEdgeV1(new BoundedAscii("input"), new BoundedAscii("output"))],
        [new CapacityDimensionId(1), new CapacityDimensionId(3)]);

    private static SessionAuthorityStampV1 Session() => new(
        RuntimeGenerationId.FromValue(Id(1)), LiveSessionId.FromValue(Id(2)));

    private static StableId128 Id(byte value) => StableId128.FromBytes(Enumerable.Repeat(value, 16).ToArray());

    private static byte[] EncodeWithNodes(GraphTopologyPlanV1 plan, string[] nodes, bool trailing = false)
    {
        var nested = new CborWriter(CborConformanceMode.Ctap2Canonical); nested.WriteStartArray(nodes.Length);
        foreach (var node in nodes) nested.WriteTextString(node); nested.WriteEndArray();
        var nodeBytes = nested.Encode(); if (trailing) nodeBytes = nodeBytes.Concat(new byte[] { 0 }).ToArray();
        var canonical = GraphTopologyPlanCodecV1.Encode(plan); var reader = new CborReader(canonical, CborConformanceMode.Ctap2Canonical);
        Assert.Equal(6, reader.ReadStartMap()); var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartMap(6);
        for (var tag = 1; tag <= 6; tag++)
        {
            Assert.Equal((ulong)tag, reader.ReadUInt64()); writer.WriteUInt64((ulong)tag);
            if (tag == 4) { reader.ReadByteString(); writer.WriteByteString(nodeBytes); }
            else writer.WriteEncodedValue(reader.ReadEncodedValue().Span);
        }
        reader.ReadEndMap(); writer.WriteEndMap(); return writer.Encode();
    }
}
