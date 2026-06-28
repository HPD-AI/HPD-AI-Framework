using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace HPD.Agent.Providers.HuggingFace;

/// <summary>
/// JSON source generation context for AOT compatibility.
/// Enables Native AOT compilation by generating serialization code at build time.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(HuggingFaceProviderConfig))]
[JsonSerializable(typeof(HuggingFaceChatRequestOptions))]
[JsonSerializable(typeof(List<float>))]
internal partial class HuggingFaceJsonContext : JsonSerializerContext
{
}
