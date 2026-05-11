using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Auth.Infrastructure.Serialization;

/// <summary>
/// Source-generated JSON metadata for infrastructure-level persistence helpers.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, string?>))]
[JsonSerializable(typeof(JsonElement))]
internal partial class HPDAuthInfrastructureJsonSerializerContext : JsonSerializerContext
{
}
