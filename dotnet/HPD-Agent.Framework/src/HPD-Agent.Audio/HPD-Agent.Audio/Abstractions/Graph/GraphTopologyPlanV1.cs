using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

/// <summary>Describes one inert S2-owned node in a compiled graph topology.</summary>
public sealed record GraphTopologyNodeV1
{
    /// <summary>Initializes a graph topology node.</summary>
    /// <param name="key">The printable-ASCII node key containing 1 to 64 bytes.</param>
    /// <exception cref="ArgumentException">The key is invalid or contains a non-printable character.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The key exceeds 64 bytes.</exception>
    public GraphTopologyNodeV1(BoundedAscii key)
    {
        ValidateKey(key, nameof(key));
        Key = key;
    }

    /// <summary>Gets the canonical node key.</summary>
    public BoundedAscii Key { get; }

    /// <summary>Gets the sole semantic owner of graph nodes.</summary>
    public OwnerSliceId Owner => OwnerSliceId.S2;

    /// <summary>Gets the authority axis that fences the node.</summary>
    public AuthorityAxisId AuthorityAxis => AuthorityAxisId.Graph;

    internal static void ValidateKey(BoundedAscii key, string parameterName)
    {
        if (!key.IsValid)
            throw new ArgumentException("A graph node key is required.", parameterName);
        var text = key.ToString();
        if (text.Length > 64)
            throw new ArgumentOutOfRangeException(parameterName, "A graph node key cannot exceed 64 ASCII bytes.");
        if (text.Any(character => character is < (char)0x21 or > (char)0x7e))
            throw new ArgumentException("A graph node key must contain printable ASCII without spaces.", parameterName);
    }
}

/// <summary>Describes one directed edge in an inert graph topology.</summary>
public sealed record GraphTopologyEdgeV1
{
    /// <summary>Initializes a directed graph topology edge.</summary>
    /// <param name="source">The source node key.</param>
    /// <param name="target">The target node key.</param>
    /// <exception cref="ArgumentException">A key is invalid or the edge is a self edge.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A key exceeds 64 bytes.</exception>
    public GraphTopologyEdgeV1(BoundedAscii source, BoundedAscii target)
    {
        GraphTopologyNodeV1.ValidateKey(source, nameof(source));
        GraphTopologyNodeV1.ValidateKey(target, nameof(target));
        if (source == target)
            throw new ArgumentException("A graph topology edge cannot target its source.", nameof(target));
        Source = source;
        Target = target;
    }

    /// <summary>Gets the source node key.</summary>
    public BoundedAscii Source { get; }

    /// <summary>Gets the target node key.</summary>
    public BoundedAscii Target { get; }
}

/// <summary>Contains an immutable, canonical, effect-free S2 graph topology plan.</summary>
public sealed class GraphTopologyPlanV1
{
    /// <summary>Gets the maximum node count.</summary>
    public const int MaximumNodes = 64;

    /// <summary>Gets the maximum edge count.</summary>
    public const int MaximumEdges = 256;

    private const string FingerprintDomain = "hpd.s2-graph-topology-plan.v1@1.0\0";
    private readonly GraphTopologyNodeV1[] _nodes;
    private readonly GraphTopologyEdgeV1[] _edges;
    private readonly CapacityDimensionId[] _capacityDimensions;
    private readonly IReadOnlyList<GraphTopologyNodeV1> _nodeView;
    private readonly IReadOnlyList<GraphTopologyEdgeV1> _edgeView;
    private readonly IReadOnlyList<CapacityDimensionId> _capacityDimensionView;

    /// <summary>Initializes a validated and canonical graph topology plan.</summary>
    /// <param name="session">The S1 session authority scope.</param>
    /// <param name="graphGeneration">The current S2 graph generation.</param>
    /// <param name="capacityGrantId">The S2 capacity grant that covers the plan.</param>
    /// <param name="nodes">The complete node set.</param>
    /// <param name="edges">The complete directed edge set.</param>
    /// <param name="capacityDimensions">The registered capacity dimensions required by the plan.</param>
    /// <exception cref="ArgumentNullException">A collection is null.</exception>
    /// <exception cref="ArgumentException">An identity, member, dependency, or graph invariant is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A collection exceeds its frozen bound.</exception>
    public GraphTopologyPlanV1(SessionAuthorityStampV1 session, GraphGenerationId graphGeneration,
        CapacityGrantId capacityGrantId, IEnumerable<GraphTopologyNodeV1> nodes,
        IEnumerable<GraphTopologyEdgeV1> edges, IEnumerable<CapacityDimensionId> capacityDimensions)
    {
        if (!session.IsValid)
            throw new ArgumentException("A valid session authority stamp is required.", nameof(session));
        if (!graphGeneration.IsValid)
            throw new ArgumentException("A valid graph generation is required.", nameof(graphGeneration));
        if (!capacityGrantId.IsValid)
            throw new ArgumentException("A valid capacity grant is required.", nameof(capacityGrantId));
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(capacityDimensions);

        _nodes = nodes.Take(MaximumNodes + 1).ToArray();
        _edges = edges.Take(MaximumEdges + 1).ToArray();
        _capacityDimensions = capacityDimensions.Take(15).ToArray();
        if (_nodes.Length is 0 or > MaximumNodes)
            throw new ArgumentOutOfRangeException(nameof(nodes), $"A topology must contain 1 to {MaximumNodes} nodes.");
        if (_edges.Length > MaximumEdges)
            throw new ArgumentOutOfRangeException(nameof(edges), $"A topology cannot exceed {MaximumEdges} edges.");
        if (_capacityDimensions.Length is 0 or > 14)
            throw new ArgumentOutOfRangeException(nameof(capacityDimensions), "A topology must declare 1 to 14 capacity dimensions.");
        if (_nodes.Any(node => node is null))
            throw new ArgumentException("A topology cannot contain a null node.", nameof(nodes));
        if (_edges.Any(edge => edge is null))
            throw new ArgumentException("A topology cannot contain a null edge.", nameof(edges));

        Array.Sort(_nodes, static (left, right) => left.Key.CompareTo(right.Key));
        Array.Sort(_edges, static (left, right) =>
        {
            var source = left.Source.CompareTo(right.Source);
            return source != 0 ? source : left.Target.CompareTo(right.Target);
        });
        Array.Sort(_capacityDimensions, static (left, right) => left.Value.CompareTo(right.Value));

        if (_nodes.Zip(_nodes.Skip(1), static (left, right) => left.Key == right.Key).Any(equal => equal))
            throw new ArgumentException("Graph node keys must be unique.", nameof(nodes));
        if (_capacityDimensions.Any(dimension => !dimension.IsValid) ||
            _capacityDimensions.Zip(_capacityDimensions.Skip(1), static (left, right) => left == right).Any(equal => equal))
            throw new ArgumentException("Capacity dimensions must be registered and unique.", nameof(capacityDimensions));
        if (_edges.Zip(_edges.Skip(1), static (left, right) => left == right).Any(equal => equal))
            throw new ArgumentException("Graph edges must be unique.", nameof(edges));

        var keys = _nodes.Select(node => node.Key).ToHashSet();
        if (_edges.Any(edge => !keys.Contains(edge.Source) || !keys.Contains(edge.Target)))
            throw new ArgumentException("Every graph edge endpoint must belong to the node set.", nameof(edges));
        if (HasCycle(keys, _edges))
            throw new ArgumentException("A graph topology must be acyclic.", nameof(edges));

        Session = session;
        GraphGeneration = graphGeneration;
        CapacityGrantId = capacityGrantId;
        _nodeView = Array.AsReadOnly(_nodes);
        _edgeView = Array.AsReadOnly(_edges);
        _capacityDimensionView = Array.AsReadOnly(_capacityDimensions);
        Fingerprint = ComputeFingerprint();
    }

    /// <summary>Gets the S1 session authority scope.</summary>
    public SessionAuthorityStampV1 Session { get; }

    /// <summary>Gets the current S2 graph generation.</summary>
    public GraphGenerationId GraphGeneration { get; }

    /// <summary>Gets the S2 capacity grant covering the plan.</summary>
    public CapacityGrantId CapacityGrantId { get; }

    /// <summary>Gets the canonical immutable node sequence.</summary>
    public IReadOnlyList<GraphTopologyNodeV1> Nodes => _nodeView;

    /// <summary>Gets the canonical immutable edge sequence.</summary>
    public IReadOnlyList<GraphTopologyEdgeV1> Edges => _edgeView;

    /// <summary>Gets the sorted immutable required capacity dimensions.</summary>
    public IReadOnlyList<CapacityDimensionId> CapacityDimensions => _capacityDimensionView;

    /// <summary>Gets the deterministic canonical plan fingerprint.</summary>
    public Hash256 Fingerprint { get; }

    private static bool HasCycle(HashSet<BoundedAscii> keys, GraphTopologyEdgeV1[] edges)
    {
        var indegree = keys.ToDictionary(key => key, _ => 0);
        var outgoing = keys.ToDictionary(key => key, _ => new List<BoundedAscii>());
        foreach (var edge in edges)
        {
            indegree[edge.Target]++;
            outgoing[edge.Source].Add(edge.Target);
        }
        var ready = new Queue<BoundedAscii>(indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var visited = 0;
        while (ready.TryDequeue(out var key))
        {
            visited++;
            foreach (var target in outgoing[key])
                if (--indegree[target] == 0)
                    ready.Enqueue(target);
        }
        return visited != keys.Count;
    }

    private Hash256 ComputeFingerprint()
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(6);
        writer.WriteUInt64(1); WriteSession(writer, Session);
        writer.WriteUInt64(2); WriteId(writer, GraphGeneration);
        writer.WriteUInt64(3); WriteId(writer, CapacityGrantId);
        writer.WriteUInt64(4); writer.WriteStartArray(_nodes.Length);
        foreach (var node in _nodes) writer.WriteTextString(node.Key.ToString());
        writer.WriteEndArray();
        writer.WriteUInt64(5); writer.WriteStartArray(_edges.Length);
        foreach (var edge in _edges)
        {
            writer.WriteStartArray(2); writer.WriteTextString(edge.Source.ToString());
            writer.WriteTextString(edge.Target.ToString()); writer.WriteEndArray();
        }
        writer.WriteEndArray();
        writer.WriteUInt64(6); writer.WriteStartArray(_capacityDimensions.Length);
        foreach (var dimension in _capacityDimensions) writer.WriteUInt64(dimension.Value);
        writer.WriteEndArray(); writer.WriteEndMap();
        var domain = Encoding.ASCII.GetBytes(FingerprintDomain);
        var payload = writer.Encode();
        var preimage = new byte[domain.Length + payload.Length];
        domain.CopyTo(preimage, 0); payload.CopyTo(preimage, domain.Length);
        var digest = SHA256.HashData(preimage);
        if (!Hash256.TryCreate(digest, out var result))
            throw new InvalidOperationException("SHA-256 did not produce 32 bytes.");
        return result;
    }

    private static void WriteSession(CborWriter writer, SessionAuthorityStampV1 value)
    {
        writer.WriteStartArray(2); WriteId(writer, value.RuntimeGenerationId);
        WriteId(writer, value.LiveSessionId); writer.WriteEndArray();
    }

    private static void WriteId<T>(CborWriter writer, T value) where T : struct
    {
        Span<byte> bytes = stackalloc byte[16];
        var written = value switch
        {
            GraphGenerationId id => id.TryWriteBytes(bytes),
            CapacityGrantId id => id.TryWriteBytes(bytes),
            RuntimeGenerationId id => id.TryWriteBytes(bytes),
            LiveSessionId id => id.TryWriteBytes(bytes),
            _ => false
        };
        if (!written) throw new InvalidOperationException("A topology identifier is invalid.");
        writer.WriteByteString(bytes);
    }
}
