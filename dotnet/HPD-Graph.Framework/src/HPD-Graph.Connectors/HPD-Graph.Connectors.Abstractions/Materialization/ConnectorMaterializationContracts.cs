using System.Text.Json;
using HPD.Events;
using HPD.Graph.Abstractions.Artifacts;
using HPD.Graph.Connectors.Abstractions.Connections;

namespace HPD.Graph.Connectors.Abstractions.Materialization;

public sealed record ConnectorMaterializationContext
{
    public required string GraphId { get; init; }
    public required ArtifactKey ArtifactKey { get; init; }
    public PartitionKey? Partition { get; init; }
    public required IConnectionProvider Connections { get; init; }
    public required IArtifactRegistry Artifacts { get; init; }
    public required IEventCoordinator Events { get; init; }
    public JsonElement? Config { get; init; }
}

public interface IConnectorMaterializationProvider
{
    string MaterializationType { get; }

    IAsyncEnumerable<Event> MaterializeAsync(
        ConnectorMaterializationContext context,
        CancellationToken ct = default);
}

public interface IConnectorAssetObservationProvider
{
    string ObservationType { get; }

    IAsyncEnumerable<Event> ObserveAsync(
        ConnectorMaterializationContext context,
        CancellationToken ct = default);
}

public interface IConnectorAssetCheckProvider
{
    string CheckName { get; }

    IAsyncEnumerable<Event> CheckAsync(
        ConnectorMaterializationContext context,
        CancellationToken ct = default);
}
