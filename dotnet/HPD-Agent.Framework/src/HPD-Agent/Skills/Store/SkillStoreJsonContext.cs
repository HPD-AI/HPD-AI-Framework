using System.Text.Json.Serialization;

namespace HPD.Agent;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SkillPackageVersionManifest))]
[JsonSerializable(typeof(SkillPackagePublicationRecord))]
internal partial class SkillStoreJsonContext : JsonSerializerContext;
