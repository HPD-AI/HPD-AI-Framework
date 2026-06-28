using System.Reflection;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Packages;

namespace HPD.Agent.Packages.DynamicDotNet;

public static class HpdDynamicDotNetPackageExtensions
{
    public static HpdLoadedPackage EnableFromDotNetManifest(
        this HpdPackageManager manager,
        HpdPackageManifest manifest,
        string scope = HpdPackageScopes.App,
        string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(manifest);

        try
        {
            var loaded = new HpdDotNetPackageLoader().Load(manifest, baseDirectory);
            return manager.Enable(
                new HpdDotNetLoadedPackage(
                    loaded.Package,
                    loaded.Assembly),
                scope);
        }
        catch (Exception ex)
        {
            return CreateFailedPackage(
                manifest,
                scope,
                $"Package load failed: {ex.Message}",
                ex.GetType().FullName);
        }
    }

    public static HpdLoadedPackage EnableFromDotNetManifestFile(
        this HpdPackageManager manager,
        string manifestPath,
        string scope = HpdPackageScopes.App)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        var fullManifestPath = Path.GetFullPath(manifestPath);
        using var stream = File.OpenRead(fullManifestPath);
        var manifest = JsonSerializer.Deserialize(
                stream,
                HpdPackageManifestJsonContext.Default.HpdPackageManifest)
            ?? throw new HpdDotNetPackageLoadException(
                $"Package manifest '{fullManifestPath}' did not contain a manifest.");
        return manager.EnableFromDotNetManifest(
            manifest,
            scope,
            Path.GetDirectoryName(fullManifestPath));
    }

    private static HpdLoadedPackage CreateFailedPackage(
        HpdPackageManifest manifest,
        string scope,
        string message,
        string? code)
    {
        var owner = new HpdContributionOwner(
            manifest.Id,
            scope,
            manifest.Version.ToString(),
            manifest.DisplayName);
        return new HpdLoadedPackage(
            manifest.Id,
            manifest.DisplayName,
            manifest.Version,
            scope,
            manifest,
            owner,
            HpdPackageLoadState.Failed,
            HpdPackageContributionSummary.Empty,
            [],
            [
                new HpdPackageDiagnostic(
                    HpdPackageDiagnosticSeverity.Error,
                    message,
                    code)
            ]);
    }
}

internal sealed class HpdDotNetLoadedPackage : IHpdPackage
{
    private readonly IHpdPackage _inner;
    private readonly Assembly _assembly;

    public HpdDotNetLoadedPackage(
        IHpdPackage inner,
        Assembly assembly)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
    }

    public HpdPackageManifest Manifest => _inner.Manifest;

    public string Id => _inner.Id;

    public string DisplayName => _inner.DisplayName;

    public Version Version => _inner.Version;

    public void Configure(IHpdPackageBuilder builder)
    {
        builder.AddAgentContributor(
            $"{Id}.generated-agent-catalog",
            new HpdPackageAgentCatalogContributor(_assembly),
            HpdDynamicDotNetPackageContributorOrders.GeneratedAgentCatalog);
        _inner.Configure(builder);
    }
}

internal static class HpdDynamicDotNetPackageContributorOrders
{
    public const int GeneratedAgentCatalog = -10_000;
}

internal sealed class HpdPackageAgentCatalogContributor : IAgentBuilderContributor
{
    private readonly Assembly _assembly;

    public HpdPackageAgentCatalogContributor(Assembly assembly)
    {
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
    }

    public void ConfigureAgent(
        AgentBuilder builder,
        HpdAgentContributionContext context)
    {
        builder.WithToolHarnessCatalogFrom(_assembly);
    }
}
