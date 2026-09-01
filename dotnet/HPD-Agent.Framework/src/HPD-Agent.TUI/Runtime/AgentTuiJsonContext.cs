using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.TUI.Runtime;

internal sealed record AgentTuiContextUsageRequest(AgentRunConfig? RunConfig);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(AgentTuiContextUsageRequest))]
[JsonSerializable(typeof(ThreadContextUsage))]
[JsonSerializable(typeof(AgentRespondResult))]
internal partial class AgentTuiJsonContext : JsonSerializerContext;
