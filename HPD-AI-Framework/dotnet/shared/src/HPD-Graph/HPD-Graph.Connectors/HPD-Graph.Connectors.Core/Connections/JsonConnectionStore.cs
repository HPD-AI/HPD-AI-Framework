using System.Text.Json;
using HPDAgent.Graph.Connectors.Abstractions.Connections;
using HPDAgent.Graph.Connectors.Abstractions.Serialization;

namespace HPDAgent.Graph.Connectors.Core.Connections;

public sealed class JsonConnectionStore : IConnectionStore
{
    private readonly string _connectionsDirectory;

    public JsonConnectionStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _connectionsDirectory = Path.Combine(rootDirectory, "connector-connections");
    }

    public async Task SaveAsync(ConnectionDefinition connection, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(connection.ConnectionId);
        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_connectionsDirectory);
        await WriteJsonAsync(GetConnectionPath(connection.ConnectionId), connection, ct).ConfigureAwait(false);
    }

    public async Task<ConnectionDefinition?> LoadAsync(string connectionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ct.ThrowIfCancellationRequested();

        var path = GetConnectionPath(connectionId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
            stream,
            ConnectorAbstractionsJsonSerializerContext.Default.ConnectionDefinition,
            ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ConnectionDefinition>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!Directory.Exists(_connectionsDirectory))
        {
            return Array.Empty<ConnectionDefinition>();
        }

        var connections = new List<ConnectionDefinition>();
        foreach (var path in Directory.EnumerateFiles(_connectionsDirectory, "*.connection.json").OrderBy(static path => path, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            await using var stream = File.OpenRead(path);
            var connection = await JsonSerializer.DeserializeAsync(
                stream,
                ConnectorAbstractionsJsonSerializerContext.Default.ConnectionDefinition,
                ct).ConfigureAwait(false);

            if (connection is not null)
            {
                connections.Add(connection);
            }
        }

        return connections.OrderBy(static connection => connection.ConnectionId, StringComparer.Ordinal).ToList();
    }

    public Task DeleteAsync(string connectionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ct.ThrowIfCancellationRequested();

        var path = GetConnectionPath(connectionId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetConnectionPath(string connectionId) =>
        Path.Combine(_connectionsDirectory, $"{Uri.EscapeDataString(connectionId)}.connection.json");

    private static async Task WriteJsonAsync(string path, ConnectionDefinition connection, CancellationToken ct)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                connection,
                ConnectorAbstractionsJsonSerializerContext.Default.ConnectionDefinition,
                ct).ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);
    }
}
