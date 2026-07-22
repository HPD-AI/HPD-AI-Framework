namespace HPD.Agent;

/// <summary>Mutates versioned skill packages independently from agent registration.</summary>
public interface ISkillStore
{
    /// <summary>Installs a new logical skill package.</summary>
    ValueTask<StoredSkill> InstallAsync(SkillPackage package, CancellationToken cancellationToken = default);
    /// <summary>Publishes a replacement package after an optional version check.</summary>
    ValueTask<StoredSkill> UpdateAsync(string skillId, SkillPackage package, string? expectedVersion, CancellationToken cancellationToken = default);
    /// <summary>Removes a logical package publication after an optional version check.</summary>
    ValueTask DeleteAsync(string skillId, string? expectedVersion, CancellationToken cancellationToken = default);
    /// <summary>Gets the currently published package.</summary>
    ValueTask<StoredSkill?> GetAsync(string skillId, CancellationToken cancellationToken = default);
    /// <summary>Lists currently published packages matching a query.</summary>
    ValueTask<IReadOnlyList<StoredSkill>> ListAsync(SkillQuery query, CancellationToken cancellationToken = default);
}

/// <summary>A skill store whose published content can be resolved through one content store.</summary>
public interface IContentBackedSkillStore : ISkillStore
{
    /// <summary>Gets the content store containing published package bytes.</summary>
    IContentStore ContentStore { get; }
}

/// <summary>An optional change feed for a mutable skill store.</summary>
public interface IWatchableSkillStore : ISkillStore
{
    /// <summary>Watches publication changes matching a query.</summary>
    IAsyncEnumerable<SkillStoreChange> WatchAsync(SkillQuery query, CancellationToken cancellationToken = default);
}

/// <summary>An installable, fully described skill package.</summary>
public sealed record SkillPackage
{
    /// <summary>Gets the package manifest.</summary>
    public required SkillPackageManifest Manifest { get; init; }
    /// <summary>Gets the authoritative instruction bytes.</summary>
    public required Stream Instructions { get; init; }
    /// <summary>Gets packaged resources.</summary>
    public IReadOnlyList<SkillPackageResource> Resources { get; init; } = [];
    /// <summary>Gets packaged scripts.</summary>
    public IReadOnlyList<SkillPackageScript> Scripts { get; init; } = [];
}

/// <summary>Identifies and describes one immutable package version.</summary>
public sealed record SkillPackageManifest
{
    /// <summary>Gets the logical skill identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the model-visible activation name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the discovery description.</summary>
    public required string Description { get; init; }
    /// <summary>Gets the opaque package version.</summary>
    public required string Version { get; init; }
    /// <summary>Gets optional provenance.</summary>
    public SkillProvenance? Provenance { get; init; }
    /// <summary>Gets host-defined labels used to select this package for a harness.</summary>
    public IReadOnlyDictionary<string, string>? Tags { get; init; }
}

/// <summary>A resource included in a package.</summary>
public sealed record SkillPackageResource
{
    /// <summary>Gets the model-visible resource name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the resource description.</summary>
    public required string Description { get; init; }
    /// <summary>Gets the resource bytes.</summary>
    public required Stream Content { get; init; }
    /// <summary>Gets the media type.</summary>
    public string ContentType { get; init; } = "text/plain";
}

/// <summary>An external script included in a package.</summary>
public sealed record SkillPackageScript
{
    /// <summary>Gets the model-visible script name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the selection and side-effect description.</summary>
    public required string Description { get; init; }
    /// <summary>Gets the script bytes.</summary>
    public required Stream Content { get; init; }
    /// <summary>Gets the runner runtime identifier.</summary>
    public required string Runtime { get; init; }
    /// <summary>Gets whether execution requires permission.</summary>
    public bool RequiresPermission { get; init; } = true;
}

/// <summary>A package version whose bytes have immutable content addresses.</summary>
public sealed record StoredSkill
{
    /// <summary>Gets the stored manifest.</summary>
    public required SkillPackageManifest Manifest { get; init; }
    /// <summary>Gets the exact instruction content address.</summary>
    public required ContentAddress Instructions { get; init; }
    /// <summary>Gets stored resources.</summary>
    public IReadOnlyList<StoredSkillResource> Resources { get; init; } = [];
    /// <summary>Gets stored scripts.</summary>
    public IReadOnlyList<StoredSkillScript> Scripts { get; init; } = [];
}

/// <summary>A stored package resource.</summary>
public sealed record StoredSkillResource(string Name, string Description, string ContentType, ContentAddress Address);

/// <summary>A stored package script.</summary>
public sealed record StoredSkillScript(string Name, string Description, string Runtime, bool RequiresPermission, ContentAddress Address);

/// <summary>Filters stored skill publications for one harness attachment.</summary>
public sealed record SkillQuery(
    string? IdPrefix = null,
    IReadOnlySet<string>? Ids = null,
    IReadOnlyDictionary<string, string>? Tags = null)
{
    /// <summary>Creates a selector for explicit logical skill IDs.</summary>
    public static SkillQuery ByIds(params string[] ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Length == 0 || ids.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one nonblank skill ID is required.", nameof(ids));
        return new SkillQuery(Ids: ids.ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>Creates a selector requiring one exact manifest tag.</summary>
    public static SkillQuery WithTag(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new SkillQuery(Tags: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [name] = value
        });
    }

    internal bool IsUnfiltered =>
        IdPrefix is null && (Ids is null || Ids.Count == 0) && (Tags is null || Tags.Count == 0);

    internal bool Matches(SkillPackageManifest manifest) =>
        (IdPrefix is null || manifest.Id.StartsWith(IdPrefix, StringComparison.Ordinal)) &&
        (Ids is null || Ids.Count == 0 || Ids.Contains(manifest.Id)) &&
        (Tags is null || Tags.Count == 0 ||
            manifest.Tags is not null && Tags.All(tag =>
                manifest.Tags.TryGetValue(tag.Key, out var value) &&
                string.Equals(value, tag.Value, StringComparison.Ordinal)));

    internal bool MayMatchId(string skillId) =>
        (IdPrefix is null || skillId.StartsWith(IdPrefix, StringComparison.Ordinal)) &&
        (Ids is null || Ids.Count == 0 || Ids.Contains(skillId));
}

/// <summary>Describes a store publication change.</summary>
public sealed record SkillStoreChange(string SkillId, string? Version, SkillSourceChangeKind Kind, DateTimeOffset ObservedAt);

/// <summary>Reports a bounded non-sensitive skill-store reconstruction problem.</summary>
public sealed record SkillStoreDiagnostic(string Category, string Message, DateTimeOffset ObservedAt);

/// <summary>Persisted immutable package-version state used by content-backed stores.</summary>
internal sealed record SkillPackageVersionManifest
{
    /// <summary>Gets the package's discovery manifest.</summary>
    public required SkillPackageManifest Manifest { get; init; }
    /// <summary>Gets the exact instruction snapshot.</summary>
    public required ContentAddress Instructions { get; init; }
    /// <summary>Gets the exact packaged resource snapshots.</summary>
    public IReadOnlyList<StoredSkillResource> Resources { get; init; } = [];
    /// <summary>Gets the exact packaged script snapshots.</summary>
    public IReadOnlyList<StoredSkillScript> Scripts { get; init; } = [];
    /// <summary>Gets when this immutable manifest was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Persisted current-version pointer used by content-backed stores.</summary>
internal sealed record SkillPackagePublicationRecord
{
    /// <summary>Gets the logical skill identifier.</summary>
    public required string SkillId { get; init; }
    /// <summary>Gets the published package version.</summary>
    public required string Version { get; init; }
    /// <summary>Gets the exact immutable version-manifest address.</summary>
    public required ContentAddress VersionManifest { get; init; }
    /// <summary>Gets the monotonically increasing publication generation.</summary>
    public required long Generation { get; init; }
    /// <summary>Gets when this version became current.</summary>
    public required DateTimeOffset PublishedAt { get; init; }
}
