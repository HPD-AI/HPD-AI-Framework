using System.Text.Json;
using HPD.Graph.Connectors.Abstractions.Assets;
using HPD.Graph.Connectors.Abstractions.Connections;
using HPD.Graph.Connectors.Abstractions.Descriptors;
using HPD.Graph.Connectors.Abstractions.Events;
using HPD.Graph.Connectors.Abstractions.Sources;

namespace HPD.Graph.Connectors.AspNetCore.Data;

public sealed record ConnectorListResponse
{
    public required IReadOnlyList<ConnectorPackageDescriptor> Connectors { get; init; }
}

public sealed record ConnectionListResponse
{
    public required IReadOnlyList<ConnectionDefinition> Connections { get; init; }
}

public sealed record WorkflowSourceListResponse
{
    public required IReadOnlyList<WorkflowSource> Sources { get; init; }
}

public sealed record WorkflowSourceStatusListResponse
{
    public required IReadOnlyList<WorkflowSourceStatus> Statuses { get; init; }
}

public sealed record ConnectorAssetListResponse
{
    public required IReadOnlyList<ConnectorAssetDescriptor> Assets { get; init; }
}

public sealed record ArtifactIOManagerListResponse
{
    public required IReadOnlyList<ArtifactIOManagerDto> Managers { get; init; }
}

public sealed record ArtifactIOManagerDto
{
    public required string Name { get; init; }
}

public sealed record ConnectorMaterializeRequest
{
    public required string MaterializationType { get; init; }
    public string? GraphId { get; init; }
    public JsonElement? Config { get; init; }
}

public sealed record ConnectorMaterializeResponse
{
    public required IReadOnlyList<string> EventTypes { get; init; }
}

public sealed record ConnectorBackfillRequest
{
    public required string MaterializationType { get; init; }
    public string? GraphId { get; init; }
    public IReadOnlyList<string> Partitions { get; init; } = [];
    public JsonElement? Config { get; init; }
}

public sealed record ConnectorObserveRequest
{
    public string? ConnectionId { get; init; }
    public string? ExternalRunId { get; init; }
    public DateTimeOffset? ObservedAt { get; init; }
    public JsonElement? Metadata { get; init; }
}

public sealed record ConnectorCheckRequest
{
    public required string CheckName { get; init; }
    public required bool Passed { get; init; }
    public string? Severity { get; init; }
    public JsonElement? Metadata { get; init; }
}
