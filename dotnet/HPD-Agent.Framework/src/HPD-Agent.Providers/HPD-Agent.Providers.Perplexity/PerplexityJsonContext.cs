using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Perplexity;

[JsonSerializable(typeof(PerplexityProviderConfig))]
internal partial class PerplexityJsonContext : JsonSerializerContext
{
}
