using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.NvidiaNim;

[JsonSerializable(typeof(NvidiaNimProviderConfig))]
internal partial class NvidiaNimJsonContext : JsonSerializerContext
{
}
