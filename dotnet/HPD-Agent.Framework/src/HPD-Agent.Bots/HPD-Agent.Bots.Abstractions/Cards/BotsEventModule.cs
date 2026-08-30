using System.Text.Json.Serialization;
using HPD.Agent.Serialization;

[assembly: HpdAgentEventModule("hpd.agent.bots", typeof(HPD.Agent.Bots.BotsEventJsonContext))]

namespace HPD.Agent.Bots;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CardContentEvent))]
internal sealed partial class BotsEventJsonContext : JsonSerializerContext;
