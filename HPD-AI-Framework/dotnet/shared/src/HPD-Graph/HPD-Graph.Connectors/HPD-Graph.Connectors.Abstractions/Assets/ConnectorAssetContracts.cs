using System.Text.Json;
using HPDAgent.Graph.Abstractions.Artifacts;

namespace HPDAgent.Graph.Connectors.Abstractions.Assets;

public sealed record ConnectorAssetDescriptor
{
    public required string AssetType { get; init; }
    public required string AppId { get; init; }
    public required ArtifactKey ArtifactKey { get; init; }

    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string? GroupName { get; init; }
    public string? ComputeKind { get; init; }

    public IReadOnlyList<ArtifactKey> Dependencies { get; init; } = [];
    public PartitionDefinition? Partitions { get; init; }
    public PartitionDependencyMapping? PartitionDependencies { get; init; }

    public JsonElement? ConfigSchema { get; init; }
    public JsonElement? MetadataSchema { get; init; }
    public ConnectorFreshnessPolicy? FreshnessPolicy { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}

public sealed record ConnectorExternalAssetDescriptor
{
    public required string AssetType { get; init; }
    public required string AppId { get; init; }
    public required ArtifactKey ArtifactKey { get; init; }
    public string? ConnectionId { get; init; }
    public string? ExternalUri { get; init; }
    public JsonElement? Metadata { get; init; }
}

public sealed record ConnectorAssetMaterializationDescriptor
{
    public required string MaterializationType { get; init; }
    public required string AppId { get; init; }
    public required ArtifactKey ArtifactKey { get; init; }
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public JsonElement? ConfigSchema { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}

public sealed record ConnectorAssetObservationDescriptor
{
    public required string ObservationType { get; init; }
    public required string AppId { get; init; }
    public required ArtifactKey ArtifactKey { get; init; }
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public JsonElement? MetadataSchema { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}

public sealed record ConnectorAssetCheckDescriptor
{
    public required string CheckName { get; init; }
    public required string AppId { get; init; }
    public required ArtifactKey ArtifactKey { get; init; }
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string? Severity { get; init; }
    public JsonElement? ConfigSchema { get; init; }
    public JsonElement? MetadataSchema { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}

public sealed record ConnectorFreshnessPolicy
{
    public TimeSpan? MaximumLag { get; init; }
    public string? CronSchedule { get; init; }
    public string? TimeZone { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}

public sealed record ConnectorAssetCatalogRequest
{
    public string? ConnectionId { get; init; }
    public JsonElement? Config { get; init; }
    public string? Selector { get; init; }
    public string? Cursor { get; init; }
    public int? Limit { get; init; }
}

public interface IConnectorAssetCatalogProvider
{
    string CatalogProviderName { get; }

    Task<IReadOnlyList<ConnectorAssetDescriptor>> LoadAssetsAsync(
        ConnectorAssetCatalogRequest request,
        CancellationToken ct = default);
}
