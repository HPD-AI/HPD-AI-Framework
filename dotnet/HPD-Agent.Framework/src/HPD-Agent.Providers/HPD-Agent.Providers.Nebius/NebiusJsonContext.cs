using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Nebius;

[JsonSerializable(typeof(NebiusProviderConfig))]
internal partial class NebiusJsonContext : JsonSerializerContext
{
}
