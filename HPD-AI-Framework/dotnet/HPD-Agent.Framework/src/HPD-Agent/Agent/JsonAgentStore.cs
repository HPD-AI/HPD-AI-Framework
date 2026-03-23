using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent;

/// <summary>
/// File-based <see cref="IAgentStore"/> using JSON files.
/// </summary>
/// <remarks>
/// Storage structure:
/// <code>
/// {basePath}/
///   {agentId}/
///     agent.json   ← StoredAgent (id, name, config, createdAt, updatedAt, metadata)
/// </code>
/// </remarks>
public class JsonAgentStore : IAgentStore
{
    private readonly string _basePath;

    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JsonAgentStore(string basePath)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
        Directory.CreateDirectory(_basePath);
    }

    public async Task<StoredAgent?> LoadAsync(string agentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        var path = GetAgentFilePath(agentId);
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<StoredAgent>(json, _options);
    }

    public async Task SaveAsync(StoredAgent agent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(agent.Id);

        var dir = GetAgentDirectory(agent.Id);
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(agent, _options);
        await File.WriteAllTextAsync(GetAgentFilePath(agent.Id), json, ct);
    }

    public Task DeleteAsync(string agentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        var dir = GetAgentDirectory(agentId);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        return Task.CompletedTask;
    }

    public Task<List<string>> ListIdsAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_basePath))
            return Task.FromResult(new List<string>());

        var ids = Directory.GetDirectories(_basePath)
            .Select(Path.GetFileName)
            .Where(name => name != null)
            .Cast<string>()
            .ToList();

        return Task.FromResult(ids);
    }

    private string GetAgentDirectory(string agentId) =>
        Path.Combine(_basePath, agentId);

    private string GetAgentFilePath(string agentId) =>
        Path.Combine(_basePath, agentId, "agent.json");
}
