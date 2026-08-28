using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Auth.Infrastructure.Stores;

namespace HPD.Auth.Infrastructure.Serialization;

/// <summary>
/// Source-generated JSON metadata for infrastructure-level persistence helpers.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(Dictionary<string, string?>))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(AuthExternalIdentityProfilePatch))]
internal partial class HPDAuthInfrastructureJsonSerializerContext : JsonSerializerContext
{
}
