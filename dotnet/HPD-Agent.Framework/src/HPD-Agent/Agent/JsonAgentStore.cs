using System.Security.Cryptography;
using System.Text;
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
///   agent_{sha256}.json
/// </code>
/// Each filename is derived from the logical ID, which is preserved in the JSON record.
/// </remarks>
public class JsonAgentStore : IAgentStore
{
    /// <inheritdoc />
    public StorePersistenceCapability PersistenceCapability =>
        StorePersistenceCapability.RestartDurable;

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
        var json = await ReadIfPresentAsync(path, ct);
        if (json is null)
            return null;


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
        catch (ArgumentNullException ex)
        {
            throw CreateInvalidDefinitionException(
                agentId,
                path,
                "The required 'config' property cannot be null.",
                ex);
        }

        if (!string.Equals(stored.Id, agentId, StringComparison.Ordinal))
            throw CreateInvalidDefinitionException(agentId, path, "The stored ID does not match the requested ID.");

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
        var path = GetAgentFilePath(agent.Id);
        var json = JsonSerializer.Serialize(agent, HPDJsonContext.Default.StoredAgent);
        Directory.CreateDirectory(_basePath);
        var temporary = Path.Combine(_basePath, $"{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, json, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public Task DeleteAsync(string agentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ct.ThrowIfCancellationRequested();
        if (Directory.Exists(_basePath))
            File.Delete(GetAgentFilePath(agentId));
        return Task.CompletedTask;
    }

    public async Task<List<string>> ListIdsAsync(CancellationToken ct = default)
    {
        var ids = new List<string>();
        if (!Directory.Exists(_basePath))
            return ids;

        foreach (var path in Directory.EnumerateFiles(_basePath, "agent_*.json"))
        {
            var json = await ReadIfPresentAsync(path, ct);
            if (json is null)
                continue;

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("id", out var id) &&
                    id.ValueKind == JsonValueKind.String &&
                    id.GetString() is { } agentId &&
                    !string.IsNullOrWhiteSpace(agentId) &&
                    string.Equals(Path.GetFileName(path), GetAgentFileName(agentId), StringComparison.Ordinal))
                {
                    ids.Add(agentId);
                }
            }
            catch (JsonException ex)
            {
                throw CreateInvalidDefinitionException(Path.GetFileName(path), path, ex.Message, ex);
            }
        }

        return ids;
    }

    private static async Task<string?> ReadIfPresentAsync(string path, CancellationToken ct)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, bufferSize: 4096, useAsync: true);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(ct);
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private string GetAgentFilePath(string agentId) =>
        Path.Combine(_basePath, GetAgentFileName(agentId));

    private static string GetAgentFileName(string agentId) =>
        $"agent_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(agentId))).ToLowerInvariant()}.json";

    private static InvalidDataException CreateInvalidDefinitionException(
        string agentId,
        string path,
        string reason,
        Exception? innerException = null) =>
        new(
            $"Invalid stored agent definition '{agentId}' at '{path}': {reason}",
            innerException);
}
