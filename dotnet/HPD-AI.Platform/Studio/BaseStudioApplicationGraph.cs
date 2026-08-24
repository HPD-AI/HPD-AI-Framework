using System.Collections.Immutable;

namespace HPD.AI.Platform.Studio;

/// <summary>Represents the finalized application-wide Studio registration graph.</summary>
public sealed class BaseStudioApplicationGraph
{
    private BaseStudioApplicationGraph(string application, long generation,
        ImmutableArray<BaseStudioModuleRegistration> modules, BaseStudioSha256 checksum)
    { ApplicationId = application; Generation = generation; Modules = modules; Checksum = checksum; }
    /// <summary>Gets the owning application identity.</summary>
    public string ApplicationId { get; }
    /// <summary>Gets the positive graph generation.</summary>
    public long Generation { get; }
    /// <summary>Gets modules in canonical identity/version order.</summary>
    public ImmutableArray<BaseStudioModuleRegistration> Modules { get; }
    /// <summary>Gets the application-specific graph checksum.</summary>
    public BaseStudioSha256 Checksum { get; }

    /// <summary>Finalizes and checksums an application-wide Studio graph.</summary>
    public static BaseStudioApplicationGraph Create(string applicationId, long generation,
        IEnumerable<BaseStudioModuleRegistration> modules)
    {
        StudioContractValidation.Id(applicationId);
        if (generation < 1) throw new ArgumentOutOfRangeException(nameof(generation));
        ImmutableArray<BaseStudioModuleRegistration> owned = StudioGraphValidation.Ordered(
            modules, 64, static value => (value.Identity.ModuleId, value.Identity.Version), nameof(modules));
        if (owned.Any(module => !StringComparer.Ordinal.Equals(module.OwningApplicationId, applicationId)))
            throw new ArgumentException("A Studio module belongs to another application graph.", nameof(modules));
        BaseStudioModuleRegistration[] bases = owned.Where(static value => value.ModuleClass == BaseStudioModuleClass.Base).ToArray();
        if (bases.Length != 1 || !StringComparer.Ordinal.Equals(bases[0].Identity.ModuleId, "base"))
            throw new ArgumentException("A Studio application requires exactly one BASE navigation owner.", nameof(modules));
        BaseStudioPageRegistration[] landings = bases[0].Pages
            .Where(static page => page.Presentation.NavigationRole == BaseStudioNavigationRole.AreaLanding).ToArray();
        if (landings.Length == 0 || landings.Length > 9 ||
            Enum.GetValues<BaseStudioArea>().Any(area => landings.Count(page => page.Area == area) > 1) ||
            landings.Count(static page => page.Area == BaseStudioArea.Overview) != 1)
            throw new ArgumentException("The BASE Studio module must own Overview and at most one landing for each disclosed task area.", nameof(modules));
        BaseStudioPageRegistration[] pages = owned.SelectMany(static value => value.Pages).ToArray();
        if (pages.Select(static value => value.PageId).Distinct(StringComparer.Ordinal).Count() != pages.Length ||
            pages.Select(static value => value.Route.TemplateId).Distinct(StringComparer.Ordinal).Count() != pages.Length ||
            StudioGraphValidation.HasRouteOverlap(pages))
            throw new ArgumentException("The application Studio graph has duplicate or ambiguous page routes.", nameof(modules));
        BaseStudioResourceRegistration[] resources = owned.SelectMany(static value => value.Resources).ToArray();
        if (resources.GroupBy(static value => value.Kind).Any(group => group.Count() > 1 &&
            group.Select(static value => value.ResolverId).Distinct(StringComparer.Ordinal).Count() != 1))
            throw new ArgumentException("Multiple Studio modules claim incompatible resource resolvers.", nameof(modules));
        int assetCount = owned.Sum(static value => value.Asset.Assets.Length);
        long assetBytes = owned.SelectMany(static value => value.Asset.Assets).Aggregate(0L, static (total, value) => checked(total + value.Length));
        if (assetCount > 2_048 || assetBytes > 128L * 1024 * 1024)
            throw new ArgumentException("The application Studio asset graph exceeds platform limits.", nameof(modules));
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.application-graph.v1", writer =>
        {
            writer.String(applicationId); writer.Int64(generation); writer.Count(owned.Length);
            foreach (BaseStudioModuleRegistration module in owned) writer.Checksum(module.Identity.Checksum);
        });
        return new(applicationId, generation, owned, checksum);
    }
}
