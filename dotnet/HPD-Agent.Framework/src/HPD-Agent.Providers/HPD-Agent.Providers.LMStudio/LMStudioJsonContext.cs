using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.LMStudio;

[JsonSerializable(typeof(LMStudioProviderConfig))]
internal partial class LMStudioJsonContext : JsonSerializerContext
{
}
