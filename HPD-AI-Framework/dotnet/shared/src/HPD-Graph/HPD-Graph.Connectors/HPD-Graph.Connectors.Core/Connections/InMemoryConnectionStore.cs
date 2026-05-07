using System.Collections.Concurrent;
using HPDAgent.Graph.Connectors.Abstractions.Connections;

namespace HPDAgent.Graph.Connectors.Core.Connections;

public sealed class InMemoryConnectionStore : IConnectionStore
{
    private readonly ConcurrentDictionary<string, ConnectionDefinition> _connections =
        new(StringComparer.Ordinal);

    public Task SaveAsync(ConnectionDefinition connection, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(connection.ConnectionId);

        ct.ThrowIfCancellationRequested();
        _connections[connection.ConnectionId] = connection;
        return Task.CompletedTask;
    }

    public Task<ConnectionDefinition?> LoadAsync(string connectionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        ct.ThrowIfCancellationRequested();
        _connections.TryGetValue(connectionId, out var connection);
        return Task.FromResult(connection);
    }

    public Task<IReadOnlyList<ConnectionDefinition>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ConnectionDefinition>>(
            _connections.Values
                .OrderBy(static connection => connection.ConnectionId, StringComparer.Ordinal)
                .ToArray());
    }

    public Task DeleteAsync(string connectionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        ct.ThrowIfCancellationRequested();
        _connections.TryRemove(connectionId, out _);
        return Task.CompletedTask;
    }
}
