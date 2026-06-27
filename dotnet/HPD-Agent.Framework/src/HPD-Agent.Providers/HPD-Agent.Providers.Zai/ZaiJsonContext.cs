using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Zai;

[JsonSerializable(typeof(ZaiProviderConfig))]
internal partial class ZaiJsonContext : JsonSerializerContext
{
}
