using System.Collections.Immutable;

namespace HPD.AI.Platform.Studio;

/// <summary>Represents the finalized application-wide Studio registration graph.</summary>
public sealed class StudioApplicationGraph
{
    private StudioApplicationGraph(string application, long generation,
        ImmutableArray<StudioModuleRegistration> modules, StudioSha256 checksum)
    { ApplicationId = application; Generation = generation; Modules = modules; Checksum = checksum; }
    /// <summary>Gets the owning application identity.</summary>
    public string ApplicationId { get; }
    /// <summary>Gets the positive graph generation.</summary>
    public long Generation { get; }
    /// <summary>Gets modules in canonical identity/version order.</summary>
    public ImmutableArray<StudioModuleRegistration> Modules { get; }
    /// <summary>Gets the application-specific graph checksum.</summary>
    public StudioSha256 Checksum { get; }

    /// <summary>Finalizes and checksums an application-wide Studio graph.</summary>
    public static StudioApplicationGraph Create(string applicationId, long generation,
        IEnumerable<StudioModuleRegistration> modules)
    {
        StudioContractValidation.Id(applicationId);
        if (generation < 1) throw new ArgumentOutOfRangeException(nameof(generation));
        ImmutableArray<StudioModuleRegistration> owned = StudioGraphValidation.Ordered(
            modules, 64, static value => (value.Identity.ModuleId, value.Identity.Version), nameof(modules));
        if (owned.Any(module => !StringComparer.Ordinal.Equals(module.OwningApplicationId, applicationId)))
            throw new ArgumentException("A Studio module belongs to another application graph.", nameof(modules));
        StudioModuleRegistration[] bases = owned.Where(static value => value.ModuleClass == StudioModuleClass.Base).ToArray();
        if (bases.Length != 1 || !StringComparer.Ordinal.Equals(bases[0].Identity.ModuleId, "base"))
            throw new ArgumentException("A Studio application requires exactly one BASE navigation owner.", nameof(modules));
        StudioPageRegistration[] landings = bases[0].Pages
            .Where(static page => page.Presentation.NavigationRole == StudioNavigationRole.AreaLanding).ToArray();
        if (landings.Length == 0 || landings.Length > 9 ||
            Enum.GetValues<StudioArea>().Any(area => landings.Count(page => page.Area == area) > 1) ||
            landings.Count(static page => page.Area == StudioArea.Overview) != 1)
            throw new ArgumentException("The HPD Base Studio module must own Overview and at most one landing for each disclosed task area.", nameof(modules));
        StudioPageRegistration[] pages = owned.SelectMany(static value => value.Pages).ToArray();
        if (pages.Select(static value => value.PageId).Distinct(StringComparer.Ordinal).Count() != pages.Length ||
            pages.Select(static value => value.Route.TemplateId).Distinct(StringComparer.Ordinal).Count() != pages.Length ||
            StudioGraphValidation.HasRouteOverlap(pages))
            throw new ArgumentException("The application Studio graph has duplicate or ambiguous page routes.", nameof(modules));
        StudioResourceRegistration[] resources = owned.SelectMany(static value => value.Resources).ToArray();
        if (resources.GroupBy(static value => value.Kind).Any(group => group.Key != StudioResourceKind.Application && group.Count() > 1 &&
            group.Select(static value => value.ResolverId).Distinct(StringComparer.Ordinal).Count() != 1))
            throw new ArgumentException("Multiple Studio modules claim incompatible resource resolvers.", nameof(modules));
        int assetCount = owned.Sum(static value => value.Asset.Assets.Length);
        long assetBytes = owned.SelectMany(static value => value.Asset.Assets).Aggregate(0L, static (total, value) => checked(total + value.Length));
        if (assetCount > 2_048 || assetBytes > 128L * 1024 * 1024)
            throw new ArgumentException("The application Studio asset graph exceeds platform limits.", nameof(modules));
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.application-graph.v1", writer =>
        {
            writer.String(applicationId); writer.Int64(generation); writer.Count(owned.Length);
            foreach (StudioModuleRegistration module in owned) writer.Checksum(module.Identity.Checksum);
        });
        return new(applicationId, generation, owned, checksum);
    }
}
