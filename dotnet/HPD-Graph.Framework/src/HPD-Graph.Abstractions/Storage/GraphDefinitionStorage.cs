using HPD.Graph.Abstractions.Config;

namespace HPD.Graph.Abstractions.Storage;

public interface IGraphDefinitionStore
{
    Task<StoredGraph?> LoadAsync(string graphId, CancellationToken ct = default);
    Task SaveAsync(StoredGraph graph, CancellationToken ct = default);
    Task DeleteAsync(string graphId, CancellationToken ct = default);
    Task<IReadOnlyList<StoredGraphSummary>> ListAsync(CancellationToken ct = default);
}

public sealed record StoredGraph
{
    public required string GraphId { get; init; }
    public required string Name { get; init; }
    public required string GraphVersion { get; init; }
    public required GraphConfig Config { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? Description { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record StoredGraphSummary
{
    public required string GraphId { get; init; }
    public required string Name { get; init; }
    public required string GraphVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? Description { get; init; }
}
