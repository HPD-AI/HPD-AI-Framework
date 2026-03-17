using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Pipeline;

namespace HPD.RAG.Core.Serialization;

/// <summary>
/// Source-generated JSON serializer context for all MRAG core types.
/// Extends GraphJsonSerializerContext so checkpoint serialization, socket value serialization,
/// and node fingerprinting all use the same source-generated, AOT-safe type metadata.
///
/// ChatMessage[] and ChatResponse are NOT re-registered here — ME.AI's own JsonSerializerContext
/// is chained via TypeInfoResolverChain to avoid re-registering the full AIContent discriminated union.
///
/// Handler Config nested classes must also be registered (see per-handler partial contexts).
/// Applications with custom handler configs register via their own JsonSerializerContext.
/// </summary>
[JsonSerializable(typeof(MragDocumentDto))]
[JsonSerializable(typeof(MragDocumentElementDto))]
[JsonSerializable(typeof(MragDocumentDto[]))]
[JsonSerializable(typeof(MragDocumentElementDto[]))]
[JsonSerializable(typeof(MragChunkDto))]
[JsonSerializable(typeof(MragChunkDto[]))]
[JsonSerializable(typeof(MragChunkDto[][]))]
[JsonSerializable(typeof(MragSearchResultDto))]
[JsonSerializable(typeof(MragSearchResultDto[]))]
[JsonSerializable(typeof(MragSearchResultDto[][]))]
[JsonSerializable(typeof(MragGraphNodeDto))]
[JsonSerializable(typeof(MragGraphEdgeDto))]
[JsonSerializable(typeof(MragGraphResultDto))]
[JsonSerializable(typeof(MragMetricsDto))]
[JsonSerializable(typeof(MragFilterNode))]
[JsonSerializable(typeof(MragFilterNode[]))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(MragFormat))]
[JsonSerializable(typeof(MragMapErrorMode))]
// Handler socket primitive types
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(float[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public partial class MragJsonSerializerContext : JsonSerializerContext
{
    // Singleton for convenient access
    public static MragJsonSerializerContext Shared { get; } = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    });
}
