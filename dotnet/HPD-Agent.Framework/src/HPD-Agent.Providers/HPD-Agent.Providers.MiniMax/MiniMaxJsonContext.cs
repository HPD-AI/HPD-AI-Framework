using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.MiniMax;

[JsonSerializable(typeof(MiniMaxProviderConfig))]
internal partial class MiniMaxJsonContext : JsonSerializerContext
{
}
