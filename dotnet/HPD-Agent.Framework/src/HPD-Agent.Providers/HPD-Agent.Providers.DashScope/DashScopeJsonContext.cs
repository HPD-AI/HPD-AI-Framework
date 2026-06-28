using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.DashScope;

/// <summary>
/// JSON serialization context for DashScope provider types.
/// Enables AOT-compatible serialization for FFI scenarios.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(DashScopeProviderConfig))]
[JsonSerializable(typeof(DashScopeChatRequestOptions))]
[JsonSerializable(typeof(DashScopeSearchRequestOptions))]
[JsonSerializable(typeof(DashScopeTranslationRequestOptions))]
[JsonSerializable(typeof(DashScopeCacheControlRequestOptions))]
[JsonSerializable(typeof(Dictionary<string, int>))]
internal partial class DashScopeJsonContext : JsonSerializerContext
{
}
