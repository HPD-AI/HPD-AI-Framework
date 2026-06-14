using System.Text.Json;
using HPD.Agent.Validation;

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

        StoredAgent stored;
        try
        {
            stored = JsonSerializer.Deserialize(json, HPDJsonContext.Default.StoredAgent)
                ?? throw new JsonException("The document contains JSON null.");
        }
        catch (JsonException ex)
        {
            throw CreateInvalidDefinitionException(agentId, path, ex.Message, ex);
        }

        if (stored.Config is null)
        {
            throw CreateInvalidDefinitionException(
                agentId,
                path,
                "The required 'config' property cannot be null.");
        }

        var validationErrors = AgentConfigValidator.Validate(stored.Config);
        if (validationErrors.Count > 0)
        {
            throw CreateInvalidDefinitionException(
                agentId,
                path,
                $"The agent configuration is invalid:{System.Environment.NewLine}" +
                string.Join(System.Environment.NewLine, validationErrors.Select(error => $"  - {error}")));
        }

        return stored;
    }

    public async Task SaveAsync(StoredAgent agent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(agent.Id);

        var dir = GetAgentDirectory(agent.Id);
        Directory.CreateDirectory(dir);

        var json = System.Text.Json.JsonSerializer.Serialize(agent, HPDJsonContext.Default.StoredAgent);
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

    private static InvalidDataException CreateInvalidDefinitionException(
        string agentId,
        string path,
        string reason,
        Exception? innerException = null) =>
        new(
            $"Invalid stored agent definition '{agentId}' at '{path}': {reason}",
            innerException);
}
