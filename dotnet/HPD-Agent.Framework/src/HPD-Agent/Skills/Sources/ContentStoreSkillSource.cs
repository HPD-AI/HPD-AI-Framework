using System.Text;

namespace HPD.Agent;

/// <summary>Discovers selected installed packages as runtime skill definitions.</summary>
public sealed class ContentStoreSkillSource : IWatchableSkillSource
{
    private readonly ISkillStore _skillStore;
    private readonly IContentStore _contentStore;
    private readonly SkillQuery _query;

    /// <summary>Initializes a source over a selected portion of a skill store.</summary>
    public ContentStoreSkillSource(ISkillStore skillStore, IContentStore contentStore, SkillQuery? query = null)
    {
        _skillStore = skillStore ?? throw new ArgumentNullException(nameof(skillStore));
        _contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
        _query = query ?? new SkillQuery();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Skill>> GetSkillsAsync(
        SkillSourceContext context,
        CancellationToken cancellationToken)
    {
        var stored = await _skillStore.ListAsync(_query, cancellationToken).ConfigureAwait(false);
        return stored.Select(ToSkill).ToArray();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SkillSourceChange> WatchAsync(
        SkillSourceContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_skillStore is not IWatchableSkillStore watchable)
            yield break;
        await foreach (var change in watchable.WatchAsync(_query, cancellationToken).ConfigureAwait(false))
            yield return new SkillSourceChange(change.SkillId, change.Kind, change.ObservedAt);
    }

    private Skill ToSkill(StoredSkill stored)
    {
        var capabilities = new List<SkillCapability>();
        capabilities.AddRange(stored.Resources.Select(resource =>
            new ContentStoreSkillResource(
                resource.Name,
                resource.Description,
                new ContentStoreSkillContentReference(resource.Address),
                _contentStore)));
        capabilities.AddRange(stored.Scripts.Select(script =>
            new SkillScript(script.Name, script.Description)
            {
                Reference = new ContentStoreScriptReference(script.Address, script.Runtime),
                RequiresPermission = script.RequiresPermission,
                Timeout = script.Timeout,
                MaximumOutputBytes = script.MaximumOutputBytes,
                InputContract = CreateInputContract(script),
                ContentStore = _contentStore
            }));

        var provenance = stored.Manifest.Provenance is { } declaredProvenance
            ? declaredProvenance with
            {
                PackageId = stored.Manifest.Id,
                Version = stored.Manifest.Version,
                Scope = stored.Instructions.Scope.Value,
                ContentHash = stored.Instructions.Sha256
            }
            : new SkillProvenance(
                "installed-package",
                stored.Manifest.Id,
                stored.Manifest.Version,
                Scope: stored.Instructions.Scope.Value,
                ContentHash: stored.Instructions.Sha256);

        return Skill.Create(
            id: stored.Manifest.Id + "@" + stored.Manifest.Version,
            name: stored.Manifest.Name,
            description: stored.Manifest.Description,
            instructions: async (_, cancellationToken) =>
            {
                await using var result = await _contentStore.OpenReadAsync(
                    stored.Instructions,
                    cancellationToken).ConfigureAwait(false);
                if (result is null)
                    throw new InvalidOperationException(
                        $"Instructions for installed skill '{stored.Manifest.Id}' are unavailable.");
                using var reader = new StreamReader(result.Content, Encoding.UTF8, true, 1024, false);
                return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            },
            capabilities: capabilities,
            provenance: provenance);
    }

    private static SkillScriptInputContract CreateInputContract(StoredSkillScript script)
    {
        var contract = SkillScriptInput.FromCanonicalSchema(script.ParametersSchema);
        if (!string.Equals(
            contract.CanonicalSchemaFingerprint,
            script.SchemaFingerprint,
            StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Stored script '{script.Name}' has an invalid input-contract fingerprint.");
        }
        return contract;
    }
}
