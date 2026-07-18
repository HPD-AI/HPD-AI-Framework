using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Agent;

/// <summary>
/// Materializes self-contained ToolHarness subagent declarations into an agent store.
/// </summary>
internal sealed class AgentDefinitionMaterializer(IAgentStore store)
{
    private const string OwnerMetadataKey = "hpd.subagent.owner";
    private const string FingerprintMetadataKey = "hpd.subagent.fingerprint";
    private const string ConfigurationSourceMetadataKey = "hpd.subagent.configurationSource";

    public async Task MaterializeAsync(SubAgent definition, AgentConfig parentConfig, string owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(parentConfig);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        if (definition.Configuration is StoredAgentConfiguration)
        {
            _ = await store.LoadAsync(definition.AgentId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Stored subagent definition '{definition.AgentId}' was not found.");
            return;
        }

        var config = definition.Configuration switch
        {
            SuppliedAgentConfiguration supplied => AgentConfigSnapshot.Create(supplied.Config),
            ParentAgentConfiguration => AgentConfigSnapshot.Create(parentConfig),
            _ => throw new ArgumentOutOfRangeException(nameof(definition.Configuration))
        };
        config.AgentId = definition.AgentId;
        config.AgentStore = null;
        config.AgentStoreOptions = null;

        var fingerprint = Fingerprint(config);
        var existing = await store.LoadAsync(definition.AgentId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            var existingOwner = ReadMetadata(existing.Metadata, OwnerMetadataKey);
            if (!string.IsNullOrWhiteSpace(existingOwner) && !string.Equals(existingOwner, owner, StringComparison.Ordinal))
                throw new InvalidOperationException($"Subagent AgentId '{definition.AgentId}' is already owned by '{existingOwner}', not '{owner}'.");

            if (string.Equals(ReadMetadata(existing.Metadata, FingerprintMetadataKey), fingerprint, StringComparison.Ordinal))
                return;
        }

        var metadata = existing?.Metadata is null
            ? new Dictionary<string, object>(StringComparer.Ordinal)
            : new Dictionary<string, object>(existing.Metadata, StringComparer.Ordinal);
        metadata[OwnerMetadataKey] = owner;
        metadata[FingerprintMetadataKey] = fingerprint;
        metadata[ConfigurationSourceMetadataKey] = definition.Configuration.GetType().Name;

        await store.SaveAsync(new StoredAgent
        {
            Id = definition.AgentId,
            Name = config.Name,
            Config = config,
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Metadata = metadata
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string Fingerprint(AgentConfig config)
    {
        var json = JsonSerializer.Serialize(config, HPDJsonContext.Default.AgentConfig);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static string? ReadMetadata(Dictionary<string, object>? metadata, string key)
    {
        if (metadata is null || !metadata.TryGetValue(key, out var value))
            return null;
        return value is JsonElement element && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : value?.ToString();
    }
}
