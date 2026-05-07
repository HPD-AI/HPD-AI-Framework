using System.Text.Json;
using HPD.Events;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Connectors.Abstractions.Sources;

namespace HPDAgent.Graph.Connectors.Abstractions.Events;

public sealed record WorkflowSourceEmittedEvent : Event
{
    public required string SourceId { get; init; }
    public required string GraphId { get; init; }
    public required string SourceType { get; init; }
    public required JsonElement Payload { get; init; }

    public string? EventId { get; init; }
    public string? Summary { get; init; }
    public DateTimeOffset OccurredAt { get; init; }

    public DedupeStrategy DedupeStrategy { get; init; } = DedupeStrategy.Unique;
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    public override EventKind Kind => EventKind.Content;
    public override EventChannel Channel => EventChannel.Synchronous;
}

public sealed record WorkflowExecutionDispatchedEvent : GraphEvent
{
    public required string SourceId { get; init; }
    public required string SourceType { get; init; }
    public required string GraphId { get; init; }
    public required string ExecutionId { get; init; }
    public string? EventId { get; init; }

    public override EventKind Kind => EventKind.Lifecycle;
}

public sealed record ArtifactObservedEvent : Event
{
    public required ArtifactKey ArtifactKey { get; init; }
    public string? ConnectionId { get; init; }
    public string? ExternalRunId { get; init; }
    public DateTimeOffset ObservedAt { get; init; }
    public JsonElement? Metadata { get; init; }
}

public sealed record ExternalArtifactMaterializedEvent : Event
{
    public required ArtifactKey ArtifactKey { get; init; }
    public string? Version { get; init; }
    public string? ConnectionId { get; init; }
    public string? ExternalRunId { get; init; }
    public DateTimeOffset MaterializedAt { get; init; }
    public IReadOnlyList<ArtifactInputVersion> InputVersions { get; init; } = [];
    public JsonElement? Metadata { get; init; }
}

public sealed record ArtifactInputVersion
{
    public required ArtifactKey ArtifactKey { get; init; }
    public required string Version { get; init; }
}

public sealed record ArtifactCheckCompletedEvent : Event
{
    public required ArtifactKey ArtifactKey { get; init; }
    public required string CheckName { get; init; }
    public required bool Passed { get; init; }
    public string? Severity { get; init; }
    public JsonElement? Metadata { get; init; }
}
