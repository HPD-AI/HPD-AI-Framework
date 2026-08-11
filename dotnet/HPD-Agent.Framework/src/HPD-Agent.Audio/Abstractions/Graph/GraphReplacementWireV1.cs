using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal static class GraphTopologyPlanCodecV1
{
    internal const int MaximumEncodedBytes = 65_536;

    internal static byte[] Encode(GraphTopologyPlanV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(6);
        writer.WriteUInt64(1); WriteSession(writer, value.Session);
        writer.WriteUInt64(2); WriteId(writer, value.GraphGeneration);
        writer.WriteUInt64(3); WriteId(writer, value.CapacityGrantId);
        writer.WriteUInt64(4); writer.WriteByteString(EncodeNodes(value.Nodes));
        writer.WriteUInt64(5); writer.WriteByteString(EncodeEdges(value.Edges));
        writer.WriteUInt64(6); writer.WriteByteString(EncodeDimensions(value.CapacityDimensions));
        writer.WriteEndMap();
        var encoded = writer.Encode();
        if (encoded.Length > MaximumEncodedBytes) throw new ArgumentOutOfRangeException(nameof(value));
        return encoded;
    }

    internal static bool TryDecode(ReadOnlyMemory<byte> encoded, out GraphTopologyPlanV1? value)
    {
        value = null;
        if (encoded.Length is 0 or > MaximumEncodedBytes) return false;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 6 || reader.ReadUInt64() != 1) return false;
            var session = ReadSession(reader);
            if (reader.ReadUInt64() != 2) return false; var generation = GraphGenerationId.FromValue(ReadId(reader));
            if (reader.ReadUInt64() != 3) return false; var grant = CapacityGrantId.FromValue(ReadId(reader));
            if (reader.ReadUInt64() != 4) return false; var nodes = ReadNodes(ReadNested(reader, 65_536));
            if (reader.ReadUInt64() != 5) return false; var edges = ReadEdges(ReadNested(reader, 65_536));
            if (reader.ReadUInt64() != 6) return false; var dimensions = ReadDimensions(ReadNested(reader, 4_096));
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0) return false;
            value = new GraphTopologyPlanV1(session, generation, grant, nodes, edges, dimensions);
            return Encode(value).AsSpan().SequenceEqual(encoded.Span);
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException)
        {
            value = null; return false;
        }
    }

    private static byte[] EncodeNodes(IReadOnlyList<GraphTopologyNodeV1> nodes)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartArray(nodes.Count);
        foreach (var node in nodes) writer.WriteTextString(node.Key.ToString());
        writer.WriteEndArray(); return writer.Encode();
    }

    private static byte[] EncodeEdges(IReadOnlyList<GraphTopologyEdgeV1> edges)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartArray(edges.Count);
        foreach (var edge in edges) { writer.WriteStartArray(2); writer.WriteTextString(edge.Source.ToString()); writer.WriteTextString(edge.Target.ToString()); writer.WriteEndArray(); }
        writer.WriteEndArray(); return writer.Encode();
    }

    private static byte[] EncodeDimensions(IReadOnlyList<CapacityDimensionId> dimensions)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartArray(dimensions.Count);
        foreach (var dimension in dimensions) writer.WriteUInt64(dimension.Value);
        writer.WriteEndArray(); return writer.Encode();
    }

    private static ReadOnlyMemory<byte> ReadNested(CborReader reader, int maximum)
    {
        var encoded = reader.ReadByteString();
        if (encoded.Length is 0 || encoded.Length > maximum) throw new CborContentException("A nested topology value exceeds its bound.");
        return encoded;
    }

    private static GraphTopologyNodeV1[] ReadNodes(ReadOnlyMemory<byte> encoded)
    {
        var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
        var count = reader.ReadStartArray();
        if (count is null or < 1 or > GraphTopologyPlanV1.MaximumNodes) throw new CborContentException("Invalid node count.");
        var values = new GraphTopologyNodeV1[count.Value];
        for (var index = 0; index < values.Length; index++) values[index] = new(new BoundedAscii(reader.ReadTextString()));
        reader.ReadEndArray(); if (reader.BytesRemaining != 0) throw new CborContentException("Trailing node bytes."); return values;
    }

    private static GraphTopologyEdgeV1[] ReadEdges(ReadOnlyMemory<byte> encoded)
    {
        var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
        var count = reader.ReadStartArray();
        if (count is null or < 0 or > GraphTopologyPlanV1.MaximumEdges) throw new CborContentException("Invalid edge count.");
        var values = new GraphTopologyEdgeV1[count.Value];
        for (var index = 0; index < values.Length; index++)
        {
            if (reader.ReadStartArray() != 2) throw new CborContentException("An edge has two keys.");
            values[index] = new(new BoundedAscii(reader.ReadTextString()), new BoundedAscii(reader.ReadTextString()));
            reader.ReadEndArray();
        }
        reader.ReadEndArray(); if (reader.BytesRemaining != 0) throw new CborContentException("Trailing edge bytes."); return values;
    }

    private static CapacityDimensionId[] ReadDimensions(ReadOnlyMemory<byte> encoded)
    {
        var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
        var count = reader.ReadStartArray();
        if (count is null or < 1 or > 14) throw new CborContentException("Invalid capacity dimension count.");
        var values = new CapacityDimensionId[count.Value];
        for (var index = 0; index < values.Length; index++)
        {
            var raw = reader.ReadUInt64();
            if (raw is 0 or > ushort.MaxValue) throw new CborContentException("Invalid capacity dimension.");
            values[index] = new CapacityDimensionId((ushort)raw);
        }
        reader.ReadEndArray(); if (reader.BytesRemaining != 0) throw new CborContentException("Trailing dimension bytes."); return values;
    }

    private static void WriteSession(CborWriter writer, SessionAuthorityStampV1 session)
    {
        writer.WriteStartArray(2); WriteId(writer, session.RuntimeGenerationId);
        WriteId(writer, session.LiveSessionId); writer.WriteEndArray();
    }

    private static SessionAuthorityStampV1 ReadSession(CborReader reader)
    {
        if (reader.ReadStartArray() != 2) throw new CborContentException("A session stamp has two identities.");
        var runtime = RuntimeGenerationId.FromValue(ReadId(reader));
        var session = LiveSessionId.FromValue(ReadId(reader)); reader.ReadEndArray();
        return new SessionAuthorityStampV1(runtime, session);
    }

    private static void WriteId<T>(CborWriter writer, T value) where T : struct
    {
        Span<byte> bytes = stackalloc byte[16];
        var valid = value switch
        {
            GraphGenerationId id => id.TryWriteBytes(bytes), CapacityGrantId id => id.TryWriteBytes(bytes),
            RuntimeGenerationId id => id.TryWriteBytes(bytes), LiveSessionId id => id.TryWriteBytes(bytes), _ => false,
        };
        if (!valid) throw new ArgumentException("A topology identity is invalid.", nameof(value));
        writer.WriteByteString(bytes);
    }

    private static StableId128 ReadId(CborReader reader)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!reader.TryReadByteString(bytes, out var written) || written != 16 || bytes.IndexOfAnyExcept((byte)0) < 0)
            throw new CborContentException("A topology identity is exactly sixteen nonzero bytes.");
        return StableId128.FromBytes(bytes);
    }
}

internal static class GraphReplacementFactIdsV1
{
    private static readonly byte[] CommandDomain = Encoding.ASCII.GetBytes("hpd-graph-replacement-command-fact-id-v1\0");
    private static readonly byte[] ResultDomain = Encoding.ASCII.GetBytes("hpd-graph-replacement-result-fact-id-v1\0");
    private static readonly byte[] InstalledDomain = Encoding.ASCII.GetBytes("hpd-graph-topology-installed-fact-id-v1\0");

    internal static JournalFactId Command(SessionAuthorityStampV1 session, OperationId operation, ushort kind)
    {
        if (!operation.IsValid || kind is < 1 or > 3) throw new ArgumentException("A registered graph replacement command is required.");
        Span<byte> identity = stackalloc byte[50]; WriteSession(session, identity);
        if (!operation.TryWriteBytes(identity[32..])) throw new ArgumentException("The operation identity is invalid.", nameof(operation));
        BinaryPrimitives.WriteUInt16BigEndian(identity[48..], kind); return Derive(CommandDomain, identity);
    }

    internal static JournalFactId Result(JournalPositionV1 command)
    {
        if (!command.IsValid) throw new ArgumentException("An admitted command position is required.", nameof(command));
        Span<byte> identity = stackalloc byte[40]; WriteSession(command.Session, identity);
        BinaryPrimitives.WriteInt64BigEndian(identity[32..], command.Sequence); return Derive(ResultDomain, identity);
    }

    internal static JournalFactId Installed(SessionAuthorityStampV1 session, Hash256 topologyFingerprint)
    {
        Span<byte> identity = stackalloc byte[64]; WriteSession(session, identity);
        if (!topologyFingerprint.TryWriteBytes(identity[32..])) throw new ArgumentException("A topology fingerprint is required.", nameof(topologyFingerprint));
        return Derive(InstalledDomain, identity);
    }

    internal static JournalFactId Transition(JournalPositionV1 commitCommand)
    {
        if (!commitCommand.IsValid) throw new ArgumentException("An admitted Commit command position is required.", nameof(commitCommand));
        ReadOnlySpan<byte> domain = "hpd-graph-replacement-generation-transition-fact-id-v1\0"u8;
        Span<byte> identity = stackalloc byte[40]; WriteSession(commitCommand.Session, identity);
        BinaryPrimitives.WriteInt64BigEndian(identity[32..], commitCommand.Sequence);
        return Derive(domain, identity);
    }

    private static void WriteSession(SessionAuthorityStampV1 session, Span<byte> destination)
    {
        if (!session.IsValid || !session.RuntimeGenerationId.TryWriteBytes(destination) || !session.LiveSessionId.TryWriteBytes(destination[16..]))
            throw new ArgumentException("A session authority stamp is required.", nameof(session));
    }

    private static JournalFactId Derive(ReadOnlySpan<byte> domain, ReadOnlySpan<byte> identity)
    {
        Span<byte> digest = stackalloc byte[32];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(domain); hash.AppendData(identity);
        if (!hash.TryGetHashAndReset(digest, out var written) || written != digest.Length) throw new CryptographicException();
        var candidate = digest[..16]; if (candidate.IndexOfAnyExcept((byte)0) < 0) candidate[^1] = 1;
        return JournalFactId.FromValue(StableId128.FromBytes(candidate));
    }
}
