using System.Text.Json;

namespace HPD.Graph.Connectors.Abstractions.Connections;

public sealed record ConnectionDescriptor
{
    public required string ConnectionType { get; init; }
    public required string AppId { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public ConnectionAuthKind AuthKind { get; init; }
    public JsonElement? ConfigSchema { get; init; }
    public IReadOnlyList<string> Scopes { get; init; } = [];
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}

public enum ConnectionAuthKind
{
    ApiKey,
    OAuth2,
    Basic,
    BearerToken,
    ConnectionString,
    Custom
}

public sealed record ConnectionDefinition
{
    public required string ConnectionId { get; init; }
    public required string ConnectionType { get; init; }
    public required string AppId { get; init; }
    public required string DisplayName { get; init; }

    public JsonElement? Config { get; init; }
    public string? SecretRef { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}

public sealed record ResolvedConnection
{
    public required string ConnectionId { get; init; }
    public required string ConnectionType { get; init; }
    public required string AppId { get; init; }
    public JsonElement? Config { get; init; }
    public IReadOnlyDictionary<string, string> Secrets { get; init; }
        = new Dictionary<string, string>();
}

public interface IConnectionProvider
{
    Task<ResolvedConnection?> ResolveAsync(
        string connectionId,
        CancellationToken ct = default);
}

public interface IConnectionStore
{
    Task SaveAsync(ConnectionDefinition connection, CancellationToken ct = default);
    Task<ConnectionDefinition?> LoadAsync(string connectionId, CancellationToken ct = default);
    Task<IReadOnlyList<ConnectionDefinition>> ListAsync(CancellationToken ct = default);
    Task DeleteAsync(string connectionId, CancellationToken ct = default);
}

public interface IConnectorClientFactory<TClient>
{
    ValueTask<TClient> CreateAsync(
        ResolvedConnection connection,
        CancellationToken ct = default);
}
