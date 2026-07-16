using System.Text.Json.Serialization;
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

/// <summary>
/// Process-local notification about the capabilities exposed by a connected MCP server.
/// These notifications are not thread facts and therefore never enter an agent thread journal.
/// </summary>
public abstract record McpLiveUpdateEvent : Event
{
    public override EventKind Kind { get; init; } = EventKind.Diagnostic;

    public required string ServerName { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }
}

/// <summary>
/// Emitted when an MCP server reports a live update for tools, prompts, resources, or a subscribed resource.
/// </summary>
public sealed record McpServerChangedEvent : McpLiveUpdateEvent
{
    /// <summary>
    /// Gets the kind of live update reported by the server.
    /// </summary>
    public required McpLiveUpdateKind ChangeKind { get; init; }

    /// <summary>
    /// Gets the resource URI when <see cref="ChangeKind"/> is <see cref="McpLiveUpdateKind.ResourceUpdated"/>.
    /// </summary>
    public string? Uri { get; init; }
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
