using System.Collections.Immutable;
using HPD.AI.Platform;
using HPD.AI.Platform.Studio;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Gateway.ControlPlane;

/// <summary>Contributes Gateway's immutable L52 Studio registration.</summary>
public sealed class GatewayStudioModuleContribution : IBaseStudioModuleContribution
{
    /// <inheritdoc />
    public string ModuleId => "gateway";
    /// <inheritdoc />
    public BaseStudioModuleRegistration Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return GatewayStudioModuleRegistry.Create(
            services.GetRequiredService<HPDBaseStudioAuthoritySnapshot>());
    }
}

internal static class GatewayStudioComposition
{
    internal static HPDAIPlatformBuilder AddGatewayStudioCore(this HPDAIPlatformBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddStudioEditionModuleAsset(GatewayStudioModuleRegistry.CreateEditionAssetContribution());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseStudioFrameworkEndpointSurface, GatewayStudioEndpointSurface>());
        return builder.AddStudioModule<GatewayStudioModuleContribution>();
    }
}

internal static class GatewayStudioModuleRegistry
{
    private const string ClientId = "gateway.admin";
    private static readonly BaseStudioSha256 RuntimeAbi = Digest("hpd.gateway.client.studio-runtime-abi.v1");
    private static readonly BaseStudioSha256 ClientContract = BaseStudioSha256.FromDigest(
        Convert.FromHexString("02c406f8c49752d24278f14e4db91694c8e84bf8ff2ef37b2e3feed81cdb21f7"));
    internal static readonly BaseStudioSha256 OperationInventoryChecksum =
        BaseStudioFrameworkSurfaceOperation.ComputeInventoryChecksum("gateway.admin.v1", GatewayStudioEndpointSurface.CreateOperations());
    private static readonly BaseStudioSha256 ComponentAbi = Digest("base.studio.page-component-abi.v1");

    internal static BaseStudioEditionModuleAssetContribution CreateEditionAssetContribution()
    {
        BaseStudioFrontendExport frontend = CreateFrontend();
        return BaseStudioEditionModuleAssetContribution.Create("gateway", 1, frontend.FrontendAbiChecksum, CreateAsset());
    }

    internal static BaseStudioModuleRegistration Create(HPDBaseStudioAuthoritySnapshot authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        BaseStudioPageRegistration[] pages = PageDefinitions
            .OrderBy(static value => value.Id, StringComparer.Ordinal)
            .Select(CreatePage).ToArray();
        return BaseStudioModuleRegistration.CreateFramework(
            "gateway", 1, authority.ApplicationId, "gateway", "studio.module.gateway",
            CreateAsset(), CreateFrontend(), pages, [], [], [], [],
            [CreateClientRegistration(pages.Select(static page => page.PageId))],
            [], BaseStudioModuleLimits.Create(4, 1, 1, 0, 0, 1));
    }

    private static BaseStudioPageRegistration CreatePage(PageDefinition definition)
    {
        var presentation = BaseStudioPagePresentationRegistration.Create(
            definition.Id, 1, BaseStudioNavigationRole.Contextual, BaseStudioWorkspaceKind.Detail,
            [BaseStudioSectionRegistration.Create("summary", "studio.section.summary", 0,
                BaseStudioSectionKind.Summary, [], [])], null, null,
            definition.Id == "gateway.configure"
                ? BaseStudioDraftRetentionClass.CurrentDocumentNavigation
                : BaseStudioDraftRetentionClass.None);
        return BaseStudioPageRegistration.Create(
            definition.Id, 1, definition.Area, definition.Label,
            BaseStudioRouteTemplate.Create(definition.Id + ".route",
                definition.Route.Select(BaseStudioRouteSegment.Literal)),
            definition.Kind, presentation, [], definition.Endpoints, [],
            BaseStudioDisclosureClass.ProtectedValue);
    }

    private static BaseStudioFrontendExport CreateFrontend()
        => BaseStudioFrontendExport.Create("gateway", 1,
            [BaseStudioFrontendClientSlot.Create(ClientId, 1,
                BaseStudioFrameworkClientProtocol.FrameworkGeneratedContractV1, RuntimeAbi, ClientContract,
                OperationInventoryChecksum, "gateway.admin.v1", BaseStudioFrameworkClientTransportClass.SameOriginShellAuthenticated,
                PageDefinitions.Select(static value => value.Id).Order(StringComparer.Ordinal), ClientLimits())],
            PageDefinitions.OrderBy(static value => value.Id, StringComparer.Ordinal)
                .Select(static value => BaseStudioPageComponentBinding.Create(
                    value.Id, "component." + value.Id, ComponentAbi)));

    private static BaseStudioFrameworkClientLimits ClientLimits() => BaseStudioFrameworkClientLimits.Create(
        23, 8_388_608, 8_388_608, 8, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

    internal static BaseStudioFrameworkClientRegistration CreateClientRegistration(IEnumerable<string> pageIds)
        => BaseStudioFrameworkClientRegistration.Create(ClientId, 1, BaseStudioContractNecessity.Required,
            BaseStudioFrameworkClientProtocol.FrameworkGeneratedContractV1, RuntimeAbi, ClientContract, OperationInventoryChecksum,
            "gateway.admin.v1", BaseStudioFrameworkClientTransportClass.SameOriginShellAuthenticated,
            pageIds, ClientLimits());

    private static BaseStudioAssetManifest CreateAsset()
    {
        const string path = "gateway/a13ef264b30b9e72b2f77eb000dee656edf8ae8452047bc0b6d409fefb410a85.js";
        using Stream stream = typeof(GatewayStudioModuleRegistry).Assembly
            .GetManifestResourceStream("HPD.Gateway.ControlPlane.Studio.Assets.gateway.js")
            ?? throw new InvalidOperationException("The prebuilt Gateway Studio module asset is absent.");
        using var content = new MemoryStream();
        stream.CopyTo(content);
        return BaseStudioAssetManifest.Create(path, BaseStudioModuleNecessity.Optional,
            BaseStudioShellContract.Current,
            [BaseStudioAssetSource.Create(path, BaseStudioAssetMediaType.JavaScriptModule,
                content.GetBuffer().AsSpan(0, checked((int)content.Length)))]);
    }

    private static BaseStudioSha256 Digest(string value)
        => BaseStudioSha256.FromDigest(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value)));

    private static readonly ImmutableArray<PageDefinition> PageDefinitions =
    [
        new("gateway.configure", BaseStudioArea.Infrastructure, "studio.page.gateway.configure",
            ["gateway", "configure"], BaseStudioPageKind.Action,
            ["gateway.admin.hostCapabilities", "gateway.admin.validate"]),
        new("gateway.diagnose", BaseStudioArea.Diagnostics, "studio.page.gateway.diagnose",
            ["gateway", "diagnose"], BaseStudioPageKind.Diagnostics,
            ["gateway.admin.audit", "gateway.admin.effective", "gateway.admin.status"]),
        new("gateway.operate", BaseStudioArea.Infrastructure, "studio.page.gateway.operate",
            ["gateway", "operate"], BaseStudioPageKind.Action,
            ["gateway.admin.activate", "gateway.admin.activations", "gateway.admin.operation", "gateway.admin.revisions"]),
        new("gateway.overview", BaseStudioArea.Overview, "studio.page.gateway.overview",
            ["gateway"], BaseStudioPageKind.Overview,
            ["gateway.admin.capabilities", "gateway.admin.desired", "gateway.admin.effective", "gateway.admin.hostCapabilities", "gateway.admin.status"]),
    ];

    private sealed record PageDefinition(string Id, BaseStudioArea Area, string Label,
        ImmutableArray<string> Route, BaseStudioPageKind Kind, ImmutableArray<string> Endpoints);
}
