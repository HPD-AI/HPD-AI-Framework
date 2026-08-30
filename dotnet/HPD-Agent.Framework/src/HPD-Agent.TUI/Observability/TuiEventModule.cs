using System.Text.Json.Serialization;
using HPD.Agent.Serialization;

[assembly: HpdAgentEventModule("hpd.agent.tui", typeof(HPD.Agent.TUI.Observability.TuiEventJsonContext))]

namespace HPD.Agent.TUI.Observability;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TranscriptViewRendered))]
[JsonSerializable(typeof(AgentTuiEventBatchApplied))]
internal sealed partial class TuiEventJsonContext : JsonSerializerContext;
