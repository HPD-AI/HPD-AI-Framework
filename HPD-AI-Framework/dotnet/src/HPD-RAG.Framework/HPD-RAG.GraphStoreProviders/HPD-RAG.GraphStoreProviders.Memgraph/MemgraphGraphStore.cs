using System.Text.Json;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Providers.GraphStore;
using Neo4j.Driver;

namespace HPD.RAG.GraphStoreProviders.Memgraph;

/// <summary>
/// IGraphStore implementation backed by a Memgraph property graph database.
/// Memgraph is Bolt-compatible — the Neo4j .NET driver is used as the transport.
/// All Cypher is run against the configured database (default "memgraph").
/// JsonElement property values are converted to Neo4j-native types before every driver call.
/// </summary>
internal sealed class MemgraphGraphStore : IGraphStore, IAsyncDisposable
{
    private readonly IDriver _driver;
    private readonly string _database;

    internal MemgraphGraphStore(IDriver driver, string database)
    {
        ArgumentNullException.ThrowIfNull(driver);
        if (string.IsNullOrWhiteSpace(database))
            throw new ArgumentException("Database name must not be empty.", nameof(database));

        _driver = driver;
        _database = database;
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task UpsertNodesAsync(IReadOnlyList<MragGraphNodeDto> nodes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (nodes.Count == 0) return;

        await using var session = _driver.AsyncSession(o => o.WithDatabase(_database));

        foreach (var node in nodes)
        {
            var nodeId = node.Id;
            var props = BuildNativeProps(node.Properties);
            var label = EscapeLabel(node.Label);
            var cypher = $"MERGE (n:`{label}` {{id: $id}}) SET n += $props";
            var parameters = new Dictionary<string, object?>
            {
                ["id"] = nodeId,
                ["props"] = props
            };

            await session.ExecuteWriteAsync(
                async tx =>
                {
                    await tx.RunAsync(cypher, parameters).ConfigureAwait(false);
                }).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();
        }
    }

    /// <inheritdoc />
    public async Task UpsertEdgesAsync(IReadOnlyList<MragGraphEdgeDto> edges, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edges);
        if (edges.Count == 0) return;

        await using var session = _driver.AsyncSession(o => o.WithDatabase(_database));

        foreach (var edge in edges)
        {
            var props = BuildNativeProps(edge.Properties);
            var relType = EscapeLabel(edge.Type);
            var cypher =
                $"MATCH (a {{id: $src}}), (b {{id: $tgt}}) " +
                $"MERGE (a)-[r:`{relType}`]->(b) SET r += $props";
            var parameters = new Dictionary<string, object?>
            {
                ["src"] = edge.SourceId,
                ["tgt"] = edge.TargetId,
                ["props"] = props
            };

            await session.ExecuteWriteAsync(
                async tx =>
                {
                    await tx.RunAsync(cypher, parameters).ConfigureAwait(false);
                }).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        IReadOnlyList<string>? nodeIds = null,
        IReadOnlyList<string>? edgeTypes = null,
        CancellationToken ct = default)
    {
        bool hasNodes = nodeIds is { Count: > 0 };
        bool hasEdgeTypes = edgeTypes is { Count: > 0 };
        if (!hasNodes && !hasEdgeTypes) return;

        await using var session = _driver.AsyncSession(o => o.WithDatabase(_database));

        if (hasNodes)
        {
            var ids = nodeIds!;
            const string cypher = "MATCH (n) WHERE n.id IN $ids DETACH DELETE n";
            await session.ExecuteWriteAsync(
                async tx =>
                {
                    await tx.RunAsync(cypher, new Dictionary<string, object?> { ["ids"] = ids }).ConfigureAwait(false);
                }).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();
        }

        if (hasEdgeTypes)
        {
            foreach (var relType in edgeTypes!)
            {
                var escaped = EscapeLabel(relType);
                var cypher = $"MATCH ()-[r:`{escaped}`]->() DELETE r";

                await session.ExecuteWriteAsync(
                    async tx =>
                    {
                        await tx.RunAsync(cypher).ConfigureAwait(false);
                    }).ConfigureAwait(false);

                ct.ThrowIfCancellationRequested();
            }
        }
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<MragGraphResultDto> GetRelationshipsAsync(
        IReadOnlyList<string> seedEntityIds,
        int maxDepth = 2,
        int limit = 30,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(seedEntityIds);

        if (seedEntityIds.Count == 0 || maxDepth <= 0)
        {
            return await FetchSeedNodesOnlyAsync(seedEntityIds, limit, ct).ConfigureAwait(false);
        }

        await using var session = _driver.AsyncSession(o => o.WithDatabase(_database));

        var ids = seedEntityIds;
        var cypher =
            $"MATCH path = (seed)-[*1..{maxDepth}]-(neighbor) " +
            "WHERE seed.id IN $ids " +
            "WITH nodes(path) AS ns, relationships(path) AS rs " +
            $"LIMIT {limit} " +
            "UNWIND ns AS n " +
            "WITH collect(DISTINCT n) AS allNodes, rs " +
            "UNWIND rs AS r " +
            "RETURN allNodes, collect(DISTINCT r) AS allRels";

        var parameters = new Dictionary<string, object?> { ["ids"] = ids };

        var result = await session.ExecuteReadAsync(
            async tx =>
            {
                var cursor = await tx.RunAsync(cypher, parameters).ConfigureAwait(false);
                return await MapPathResultAsync(cursor, limit).ConfigureAwait(false);
            }).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        return new MragGraphResultDto
        {
            Nodes = result.Nodes,
            Edges = result.Edges,
            IsTruncated = result.Truncated
        };
    }

    /// <inheritdoc />
    public async Task<MragGraphResultDto> StructuredQueryAsync(
        string query,
        IReadOnlyDictionary<string, JsonElement>? parameters = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var session = _driver.AsyncSession(o => o.WithDatabase(_database));

        Dictionary<string, object?>? nativeParams = null;
        if (parameters is { Count: > 0 })
        {
            nativeParams = new Dictionary<string, object?>(parameters.Count);
            foreach (var (key, value) in parameters)
                nativeParams[key] = JsonElementToNative(value);
        }

        var capturedQuery = query;
        var capturedParams = nativeParams;

        var result = await session.ExecuteReadAsync(
            async tx =>
            {
                var cursor = capturedParams is null
                    ? await tx.RunAsync(capturedQuery).ConfigureAwait(false)
                    : await tx.RunAsync(capturedQuery, capturedParams).ConfigureAwait(false);
                return await MapPathResultAsync(cursor, limit: int.MaxValue).ConfigureAwait(false);
            }).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        return new MragGraphResultDto
        {
            Nodes = result.Nodes,
            Edges = result.Edges,
            IsTruncated = result.Truncated
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<MragGraphResultDto> FetchSeedNodesOnlyAsync(
        IReadOnlyList<string> seedIds,
        int limit,
        CancellationToken ct)
    {
        if (seedIds.Count == 0)
            return new MragGraphResultDto { Nodes = [], Edges = [], IsTruncated = false };

        await using var session = _driver.AsyncSession(o => o.WithDatabase(_database));

        var ids = seedIds;
        const string cypher = "MATCH (n) WHERE n.id IN $ids RETURN n";
        var parameters = new Dictionary<string, object?> { ["ids"] = ids };

        var nodeList = await session.ExecuteReadAsync(
            async tx =>
            {
                var cursor = await tx.RunAsync(cypher, parameters).ConfigureAwait(false);
                var records = await cursor.ToListAsync().ConfigureAwait(false);
                var results = new List<MragGraphNodeDto>(records.Count);
                foreach (var record in records)
                {
                    if (record["n"].As<INode>() is { } inode)
                        results.Add(MapNode(inode));
                }
                return results;
            }).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        bool truncated = nodeList.Count >= limit;
        return new MragGraphResultDto
        {
            Nodes = nodeList.Take(limit).ToArray(),
            Edges = [],
            IsTruncated = truncated
        };
    }

    private sealed record PathResult(MragGraphNodeDto[] Nodes, MragGraphEdgeDto[] Edges, bool Truncated);

    private static async Task<PathResult> MapPathResultAsync(IResultCursor cursor, int limit)
    {
        var records = await cursor.ToListAsync().ConfigureAwait(false);

        var nodeMap = new Dictionary<long, MragGraphNodeDto>();
        var edgeMap = new Dictionary<long, MragGraphEdgeDto>();

        foreach (var record in records)
        {
            foreach (var key in record.Keys)
            {
                var val = record[key];

                // Handle list columns (allNodes / allRels)
                if (val?.As<List<object>>() is { } list)
                {
                    foreach (var item in list)
                    {
                        if (item is INode ln && !nodeMap.ContainsKey(ln.Id))
                            nodeMap[ln.Id] = MapNode(ln);
                        if (item is IRelationship lr && !edgeMap.ContainsKey(lr.Id))
                            edgeMap[lr.Id] = MapRelationship(lr);
                    }
                }

                // Handle scalar INode / IRelationship columns
                if (val?.As<INode>() is { } n && !nodeMap.ContainsKey(n.Id))
                    nodeMap[n.Id] = MapNode(n);
                if (val?.As<IRelationship>() is { } r && !edgeMap.ContainsKey(r.Id))
                    edgeMap[r.Id] = MapRelationship(r);
            }
        }

        bool truncated = nodeMap.Count >= limit;
        return new PathResult(nodeMap.Values.ToArray(), edgeMap.Values.ToArray(), truncated);
    }

    private static MragGraphNodeDto MapNode(INode node)
    {
        var label = node.Labels.FirstOrDefault() ?? "Node";
        var props = MapDriverProps(node.Properties);
        return new MragGraphNodeDto
        {
            Id = node.Properties.TryGetValue("id", out var id)
                ? id?.ToString() ?? node.Id.ToString()
                : node.Id.ToString(),
            Label = label,
            Properties = props
        };
    }

    private static MragGraphEdgeDto MapRelationship(IRelationship rel)
    {
        var props = MapDriverProps(rel.Properties);
        return new MragGraphEdgeDto
        {
            SourceId = rel.StartNodeId.ToString(),
            TargetId = rel.EndNodeId.ToString(),
            Type = rel.Type,
            Properties = props
        };
    }

    private static Dictionary<string, JsonElement>? MapDriverProps(IReadOnlyDictionary<string, object> props)
    {
        if (props is null || props.Count == 0) return null;
        var result = new Dictionary<string, JsonElement>(props.Count);
        foreach (var (k, v) in props)
            result[k] = NativeToJsonElement(v);
        return result;
    }

    private static Dictionary<string, object?> BuildNativeProps(
        Dictionary<string, JsonElement>? props)
    {
        if (props is null || props.Count == 0) return new Dictionary<string, object?>();
        var result = new Dictionary<string, object?>(props.Count);
        foreach (var (k, v) in props)
            result[k] = JsonElementToNative(v);
        return result;
    }

    /// <summary>
    /// Converts a <see cref="JsonElement"/> to a Neo4j/Bolt driver-native scalar type.
    /// </summary>
    internal static object? JsonElementToNative(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out long l) => l,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            JsonValueKind.Array => MapJsonArray(element),
            JsonValueKind.Object => MapJsonObject(element),
            _ => element.GetRawText()
        };
    }

    private static List<object?> MapJsonArray(JsonElement array)
    {
        var list = new List<object?>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
            list.Add(JsonElementToNative(item));
        return list;
    }

    private static Dictionary<string, object?> MapJsonObject(JsonElement obj)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in obj.EnumerateObject())
            dict[prop.Name] = JsonElementToNative(prop.Value);
        return dict;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "Neo4j/Bolt driver only returns primitive/well-known types; trimmer-safe in practice.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "AOT",
        "IL3050:RequiresDynamicCode",
        Justification = "Neo4j/Bolt driver only returns primitive/well-known types; no dynamic code generation needed.")]
    private static JsonElement NativeToJsonElement(object? value)
    {
        if (value is null) return default;
        var json = JsonSerializer.Serialize(value);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string EscapeLabel(string label) => label.Replace("`", "``");

    public async ValueTask DisposeAsync()
    {
        await _driver.DisposeAsync().ConfigureAwait(false);
    }
}
