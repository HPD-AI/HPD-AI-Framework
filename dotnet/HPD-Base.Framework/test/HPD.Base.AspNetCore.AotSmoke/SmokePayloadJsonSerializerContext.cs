using System.Text.Json.Serialization;

namespace HPD.Base.AspNetCore.AotSmoke;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SmokePayload))]
internal partial class SmokePayloadJsonSerializerContext : JsonSerializerContext
{
}
