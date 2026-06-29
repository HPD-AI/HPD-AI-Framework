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
            typeof(McpServerToolsChangedEvent),
            "MCP_SERVER_TOOLS_CHANGED",
            McpLiveUpdateEventJsonContext.Default.McpServerToolsChangedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(McpServerPromptsChangedEvent),
            "MCP_SERVER_PROMPTS_CHANGED",
            McpLiveUpdateEventJsonContext.Default.McpServerPromptsChangedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(McpServerResourcesChangedEvent),
            "MCP_SERVER_RESOURCES_CHANGED",
            McpLiveUpdateEventJsonContext.Default.McpServerResourcesChangedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(McpResourceUpdatedEvent),
            "MCP_RESOURCE_UPDATED",
            McpLiveUpdateEventJsonContext.Default.McpResourceUpdatedEvent);
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
[JsonSerializable(typeof(McpServerToolsChangedEvent))]
[JsonSerializable(typeof(McpServerPromptsChangedEvent))]
[JsonSerializable(typeof(McpServerResourcesChangedEvent))]
[JsonSerializable(typeof(McpResourceUpdatedEvent))]
[JsonSerializable(typeof(McpLiveUpdatesStartedEvent))]
[JsonSerializable(typeof(McpLiveUpdatesStoppedEvent))]
[JsonSerializable(typeof(McpLiveUpdatesErrorEvent))]
[JsonSerializable(typeof(IReadOnlyList<McpLiveUpdateKind>))]
[JsonSerializable(typeof(List<McpLiveUpdateKind>))]
internal sealed partial class McpLiveUpdateEventJsonContext : JsonSerializerContext;
