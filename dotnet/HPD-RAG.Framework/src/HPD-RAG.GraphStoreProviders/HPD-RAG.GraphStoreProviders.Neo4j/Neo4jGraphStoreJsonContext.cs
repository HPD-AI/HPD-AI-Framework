using System.Text.Json.Serialization;

namespace HPD.RAG.GraphStoreProviders.Neo4j;

/// <summary>
/// Source-generated JSON serializer context for Neo4j graph store provider types.
/// Enables AOT-compatible serialization of <see cref="Neo4jGraphStoreConfig"/>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Neo4jGraphStoreConfig))]
public partial class Neo4jGraphStoreJsonContext : JsonSerializerContext
{
}
