using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using HPD.Agent.Serialization;

namespace HPD.Agent.MCP;

internal static class McpLiveUpdateEventSerialization
{
    [ModuleInitializer]
    internal static void RegisterEvents()
    {
        AgentEventSerializer.RegisterEventType(
            typeof(McpServerChangedEvent),
            "MCP_SERVER_CHANGED",
            McpLiveUpdateEventJsonContext.Default.McpServerChangedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(McpLiveUpdatesStartedEvent),
            "MCP_LIVE_UPDATES_STARTED",
            McpLiveUpdateEventJsonContext.Default.McpLiveUpdatesStartedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(McpLiveUpdatesStoppedEvent),
            "MCP_LIVE_UPDATES_STOPPED",
            McpLiveUpdateEventJsonContext.Default.McpLiveUpdatesStoppedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(McpLiveUpdatesErrorEvent),
            "MCP_LIVE_UPDATES_ERROR",
            McpLiveUpdateEventJsonContext.Default.McpLiveUpdatesErrorEvent);
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(McpLiveUpdateKind))]
[JsonSerializable(typeof(McpServerChangedEvent))]
[JsonSerializable(typeof(McpLiveUpdatesStartedEvent))]
[JsonSerializable(typeof(McpLiveUpdatesStoppedEvent))]
[JsonSerializable(typeof(McpLiveUpdatesErrorEvent))]
[JsonSerializable(typeof(IReadOnlyList<McpLiveUpdateKind>))]
[JsonSerializable(typeof(List<McpLiveUpdateKind>))]
internal sealed partial class McpLiveUpdateEventJsonContext : JsonSerializerContext;
