using System.Text.Json.Serialization;
using HPD.Agent;
using HPD.Events;

namespace HPD.Agent.MCP;

[JsonConverter(typeof(JsonStringEnumConverter<McpLiveUpdateKind>))]
public enum McpLiveUpdateKind
{
    ToolsChanged,
    PromptsChanged,
    ResourcesChanged,
    ResourceUpdated
}

public abstract record McpLiveUpdateEvent : AgentEvent
{
    public override EventKind Kind { get; init; } = EventKind.Diagnostic;

    public required string ServerName { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }
}

public sealed record McpServerToolsChangedEvent : McpLiveUpdateEvent;

public sealed record McpServerPromptsChangedEvent : McpLiveUpdateEvent;

public sealed record McpServerResourcesChangedEvent : McpLiveUpdateEvent;

public sealed record McpResourceUpdatedEvent : McpLiveUpdateEvent
{
    public required string Uri { get; init; }
}

public sealed record McpLiveUpdatesStartedEvent : McpLiveUpdateEvent
{
    public override EventKind Kind { get; init; } = EventKind.Lifecycle;

    public required IReadOnlyList<McpLiveUpdateKind> Subscriptions { get; init; }
}

public sealed record McpLiveUpdatesStoppedEvent : McpLiveUpdateEvent
{
    public override EventKind Kind { get; init; } = EventKind.Lifecycle;
}

public sealed record McpLiveUpdatesErrorEvent : McpLiveUpdateEvent, IErrorEvent
{
    public required string ErrorMessage { get; init; }

    [JsonIgnore]
    public Exception? Exception { get; init; }
}
