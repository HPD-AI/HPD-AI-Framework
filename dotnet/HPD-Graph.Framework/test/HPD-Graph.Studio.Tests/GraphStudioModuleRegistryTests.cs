using HPD.AI.Platform;
using HPD.AI.Platform.Studio;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Graph.Studio.Tests;

/// <summary>Verifies Graph Studio's hard-broken immutable contribution.</summary>
public sealed class GraphStudioModuleRegistryTests
{
    /// <summary>Proves Graph owns only its semantic resources and links to BASE work.</summary>
    [Fact]
    public void Registry_is_semantic_and_links_to_base_authority()
    {
        BaseStudioModuleRegistration module = GraphStudioModuleRegistry.Create(Snapshot());
        Assert.Equal("graph", module.Identity.ModuleId);
        Assert.Equal(6, module.Pages.Length);
        Assert.Equal([BaseStudioResourceKind.GraphDefinition, BaseStudioResourceKind.GraphExecution, BaseStudioResourceKind.GraphCheckpoint],
            module.Resources.Select(static value => value.Kind));
        Assert.Empty(module.Commands);
        Assert.Collection(module.Links,
            link => { Assert.Equal(BaseStudioResourceKind.GraphDefinition, link.SourceKind); Assert.Equal(BaseStudioResourceKind.Schedule, link.TargetKind); },
            link => { Assert.Equal(BaseStudioResourceKind.GraphExecution, link.SourceKind); Assert.Equal(BaseStudioResourceKind.Activation, link.TargetKind); });
        Assert.Equal(BaseStudioFrameworkClientProtocol.FrameworkGeneratedContractV1, module.Clients.Single().Protocol);
        Assert.Equal(6, module.Clients.Single().Limits.MaximumOperations);
        Assert.Equal("acb04f05700c7af25c0ea6cc76c536334f89998249298a169abf743f8d6fc992",
            Convert.ToHexString(module.Clients.Single().OperationInventoryChecksum.ToArray()).ToLowerInvariant());
        Assert.Equal("34e2556104951a2ac458ba0e4c553f210fab9a0b0158227fb432a2bcd448d1a8",
            Convert.ToHexString(module.Clients.Single().Limits.Checksum.ToArray()).ToLowerInvariant());
        Assert.Equal(module.Pages.Select(static x => x.PageId), module.Frontend.Components.Select(static x => x.PageId));
        using ServiceProvider runtimeServices = new ServiceCollection().BuildServiceProvider();
        BaseStudioModuleRuntimeContribution runtime = new GraphStudioRuntimeContributionFactory(
            runtimeServices, new Inspection(), []).Create(module);
        Assert.Equal(3, runtime.Producers.Length);
    }

    /// <summary>Proves the obsolete loose module catalog is replaced by the graph contribution.</summary>
    [Fact]
    public void Extension_registers_the_framework_module_and_edition_asset()
    {
        var services = new ServiceCollection();
        services.AddHPDBase(ConfigureBase);
        services.AddSingleton<IGraphStudioInspectionAuthority, Inspection>();
        services.AddHPDAIPlatform().AddGraphStudio();
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Equal("graph", provider.GetRequiredService<BaseStudioEditionAssetCatalogProvider>().GetRequiredCatalog().Single().ModuleId);
    }

    /// <summary>Proves required fixed disclosure grants fail closed.</summary>
    [Fact]
    public void Missing_fixed_grant_rejects_registration()
    {
        var services = new ServiceCollection();
        services.AddHPDBase(builder => ConfigureBase(builder, "base.studio.bootstrap.read"));
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => GraphStudioModuleRegistry.Create(provider.GetRequiredService<HPDBaseStudioAuthoritySnapshot>()));
    }

    private static HPDBaseStudioAuthoritySnapshot Snapshot()
    { var services = new ServiceCollection(); services.AddHPDBase(ConfigureBase); using ServiceProvider provider = services.BuildServiceProvider(); return provider.GetRequiredService<HPDBaseStudioAuthoritySnapshot>(); }
    private static void ConfigureBase(HPDBaseBuilder builder) => ConfigureBase(builder, null);
    private static void ConfigureBase(HPDBaseBuilder builder, string? omit)
    {
        builder.ConfigureSchema(static options => options.ApplicationId = "sample.application");
        foreach (string id in new[] { "base.studio.bootstrap.read", "base.studio.resource.discover", "base.studio.resource.inspect" }.Where(x => x != omit))
            builder.AddStaticGrantAuthority(new BaseGrantAuthorityDefinition { Id = id, Version = 1, OwningModuleId = "base", SourceContractId = "base.studio.fixed-grant", SourceContractVersion = 1 },
                new AccessGrant { Id = id, ApplicationId = "sample.application", ModuleId = "base", Audience = HPDBaseEndpointAudience.ControlPlane,
                    Subject = new AccessSubject { Kind = AccessSubjectKind.User, Id = "operator" }, Action = id, Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime } });
    }

    private sealed class Inspection : IGraphStudioInspectionAuthority
    {
        public ValueTask<BaseStudioFrameworkSurfaceResponse?> ObserveAsync(string operationId, string relativePath,
            string applicationId, CancellationToken cancellationToken) => new((BaseStudioFrameworkSurfaceResponse?)null);
        public ValueTask<bool> ExistsAsync(BaseStudioResourceIdentity resource, CancellationToken cancellationToken) => new(false);
    }
}
