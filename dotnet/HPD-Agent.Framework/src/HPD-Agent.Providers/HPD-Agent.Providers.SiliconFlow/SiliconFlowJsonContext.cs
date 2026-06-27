using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.SiliconFlow;

[JsonSerializable(typeof(SiliconFlowProviderConfig))]
internal partial class SiliconFlowJsonContext : JsonSerializerContext
{
}
