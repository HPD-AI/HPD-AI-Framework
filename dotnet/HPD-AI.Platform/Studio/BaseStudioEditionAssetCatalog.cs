using System.Collections.Immutable;

namespace HPD.AI.Platform.Studio;

/// <summary>Describes one authorization-neutral, build-selected framework module asset contribution.</summary>
public sealed class BaseStudioEditionModuleAssetContribution
{
    private BaseStudioEditionModuleAssetContribution(string moduleId, int version, BaseStudioSha256 frontendAbi,
        BaseStudioAssetManifest asset)
    { ModuleId = moduleId; ModuleVersion = version; FrontendAbiChecksum = frontendAbi; Asset = asset; }
    /// <summary>Gets the static module identity.</summary>
    public string ModuleId { get; }
    /// <summary>Gets the positive static module version.</summary>
    public int ModuleVersion { get; }
    /// <summary>Gets the static frontend ABI checksum.</summary>
    public BaseStudioSha256 FrontendAbiChecksum { get; }
    /// <summary>Gets the complete content-addressed module manifest.</summary>
    public BaseStudioAssetManifest Asset { get; }

    /// <summary>Creates a deeply owned public-edition contribution independent of application installation.</summary>
    public static BaseStudioEditionModuleAssetContribution Create(string moduleId, int version,
        BaseStudioSha256 frontendAbiChecksum, BaseStudioAssetManifest asset)
    {
        StudioContractValidation.Id(moduleId);
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        ArgumentNullException.ThrowIfNull(frontendAbiChecksum); ArgumentNullException.ThrowIfNull(asset);
        return new(moduleId, version, BaseStudioSha256.FromDigest(frontendAbiChecksum.ToArray()), asset);
    }
}

/// <summary>Collects the host's explicit authorization-neutral edition module set before it is frozen.</summary>
public sealed class BaseStudioEditionAssetCatalog
{
    private readonly List<BaseStudioEditionModuleAssetContribution> _items = [];
    private bool _frozen;
    /// <summary>Adds one explicitly build-selected first-party module asset contribution.</summary>
    public void Add(BaseStudioEditionModuleAssetContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        if (_frozen) throw new InvalidOperationException("The Studio edition asset catalog is frozen.");
        if (_items.Any(item => StringComparer.Ordinal.Equals(item.ModuleId, contribution.ModuleId) && item.ModuleVersion == contribution.ModuleVersion))
            throw new InvalidOperationException("The Studio edition contains a duplicate module asset identity.");
        if (_items.Count >= 64) throw new InvalidOperationException("The Studio edition module limit was exceeded.");
        _items.Add(contribution);
    }
    internal ImmutableArray<BaseStudioEditionModuleAssetContribution> Freeze()
    {
        _frozen = true;
        return [.. _items.OrderBy(static value => value.ModuleId, StringComparer.Ordinal).ThenBy(static value => value.ModuleVersion)];
    }
}

/// <summary>Exposes the independently frozen public edition module catalog.</summary>
public sealed class BaseStudioEditionAssetCatalogProvider
{
    private readonly BaseStudioEditionAssetCatalog _catalog;
    private ImmutableArray<BaseStudioEditionModuleAssetContribution>? _frozen;
    /// <summary>Initializes the provider over the explicit build-owned catalog.</summary>
    public BaseStudioEditionAssetCatalogProvider(BaseStudioEditionAssetCatalog catalog)
        => _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    /// <summary>Returns the one canonical frozen public edition catalog.</summary>
    public ImmutableArray<BaseStudioEditionModuleAssetContribution> GetRequiredCatalog()
        => _frozen ??= _catalog.Freeze();

    /// <summary>Returns the canonical edition asset-graph checksum for the supplied shell contract.</summary>
    public BaseStudioSha256 GetRequiredChecksum(BaseStudioShellContract shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        return BaseStudioEditionAssetGraph.Create(GetRequiredCatalog(), shell).Checksum;
    }
}
