using System.Text.Json;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Connectors.Abstractions.Connections;

namespace HPDAgent.Graph.Connectors.Abstractions.IO;

public sealed record ArtifactWriteContext
{
    public required ArtifactKey ArtifactKey { get; init; }
    public string? Version { get; init; }
    public PartitionKey? Partition { get; init; }
    public JsonElement? Config { get; init; }
    public required ResolvedConnection Connection { get; init; }
}

public sealed record ArtifactReadContext
{
    public required ArtifactKey ArtifactKey { get; init; }
    public string? Version { get; init; }
    public PartitionKey? Partition { get; init; }
    public JsonElement? Config { get; init; }
    public required ResolvedConnection Connection { get; init; }
}

public interface IArtifactIOManager
{
    string Name { get; }

    ValueTask StoreAsync(
        ArtifactWriteContext context,
        object? value,
        CancellationToken ct = default);

    ValueTask<object?> LoadAsync(
        ArtifactReadContext context,
        CancellationToken ct = default);
}

public interface IArtifactValueStore
{
    ValueTask StoreAsync(
        ArtifactWriteContext context,
        object? value,
        CancellationToken ct = default);

    ValueTask<object?> LoadAsync(
        ArtifactReadContext context,
        CancellationToken ct = default);
}
