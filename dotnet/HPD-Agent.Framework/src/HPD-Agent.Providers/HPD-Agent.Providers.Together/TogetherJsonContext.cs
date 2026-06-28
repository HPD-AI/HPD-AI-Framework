using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace HPD.Agent.Providers.Together;

/// <summary>
/// JSON serialization context for Together provider types.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(TogetherProviderConfig))]
[JsonSerializable(typeof(TogetherChatRequestOptions))]
[JsonSerializable(typeof(Dictionary<string, float>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
internal partial class TogetherJsonContext : JsonSerializerContext
{
}
