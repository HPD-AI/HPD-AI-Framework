using System.Text.Json.Serialization;

namespace HPD.RAG.GraphStoreProviders.Memgraph;

/// <summary>
/// Source-generated JSON serializer context for Memgraph graph store provider types.
/// Enables AOT-compatible serialization of <see cref="MemgraphGraphStoreConfig"/>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MemgraphGraphStoreConfig))]
public partial class MemgraphGraphStoreJsonContext : JsonSerializerContext
{
}
