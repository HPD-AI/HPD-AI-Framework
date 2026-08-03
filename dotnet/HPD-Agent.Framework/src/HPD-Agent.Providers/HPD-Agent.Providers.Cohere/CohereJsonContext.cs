using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace HPD.Agent.Providers.Cohere;

/// <summary>
/// JSON serialization context for Cohere provider types.
/// Enables AOT-compatible serialization for FFI scenarios.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(CohereChatRequestOptions))]
[JsonSerializable(typeof(CohereChatDocument))]
[JsonSerializable(typeof(List<CohereChatDocument>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
internal partial class CohereJsonContext : JsonSerializerContext
{
}
