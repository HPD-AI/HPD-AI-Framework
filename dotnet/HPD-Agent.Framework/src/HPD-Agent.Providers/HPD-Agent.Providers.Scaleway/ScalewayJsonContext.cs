using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Scaleway;

[JsonSerializable(typeof(ScalewayProviderConfig))]
internal partial class ScalewayJsonContext : JsonSerializerContext
{
}
