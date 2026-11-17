using System.Collections.Immutable;
using HPD.AI.Platform;
using HPD.AI.Platform.Studio;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Gateway.ControlPlane;

/// <summary>Contributes Gateway's immutable L52 Studio registration.</summary>
public sealed class GatewayStudioModuleContribution : IStudioModuleContribution
{
    /// <inheritdoc />
    public string ModuleId => "gateway";
    /// <inheritdoc />
    public StudioModuleRegistration Create(IServiceProvider services)
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
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStudioFrameworkEndpointSurface, GatewayStudioEndpointSurface>());
        return builder.AddStudioModule<GatewayStudioModuleContribution>();
    }
}

internal static class GatewayStudioModuleRegistry
{
    private const string ClientId = "gateway.admin";
    private static readonly StudioSha256 RuntimeAbi = Digest("hpd.gateway.client.studio-runtime-abi.v1");
    private static readonly StudioSha256 ClientContract = StudioSha256.FromDigest(
        Convert.FromHexString("02c406f8c49752d24278f14e4db91694c8e84bf8ff2ef37b2e3feed81cdb21f7"));
    internal static readonly StudioSha256 OperationInventoryChecksum =
        StudioFrameworkSurfaceOperation.ComputeInventoryChecksum("gateway.admin.v1", GatewayStudioEndpointSurface.CreateOperations());
    private static readonly StudioSha256 ComponentAbi = Digest("base.studio.page-component-abi.v1");

    internal static StudioEditionModuleAssetContribution CreateEditionAssetContribution()
    {
        StudioFrontendExport frontend = CreateFrontend();
        return StudioEditionModuleAssetContribution.Create("gateway", 1, frontend.FrontendAbiChecksum, CreateAsset());
    }

    internal static StudioModuleRegistration Create(HPDBaseStudioAuthoritySnapshot authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        StudioPageRegistration[] pages = PageDefinitions
            .OrderBy(static value => value.Id, StringComparer.Ordinal)
            .Select(CreatePage).ToArray();
        return StudioModuleRegistration.CreateFramework(
            "gateway", 1, authority.ApplicationId, "gateway", "studio.module.gateway",
            CreateAsset(), CreateFrontend(), pages, [], [], [], [],
            [CreateClientRegistration(pages.Select(static page => page.PageId))],
            [], StudioModuleLimits.Create(4, 1, 1, 0, 0, 1));
    }

    private static StudioPageRegistration CreatePage(PageDefinition definition)
    {
        var presentation = StudioPagePresentationRegistration.Create(
            definition.Id, 1, StudioNavigationRole.Contextual, StudioWorkspaceKind.Detail,
            [StudioSectionRegistration.Create("summary", "studio.section.summary", 0,
                StudioSectionKind.Summary, [], [])], null, null,
            definition.Id == "gateway.configure"
                ? StudioDraftRetentionClass.CurrentDocumentNavigation
                : StudioDraftRetentionClass.None);
        return StudioPageRegistration.Create(
            definition.Id, 1, definition.Area, definition.Label,
            StudioRouteTemplate.Create(definition.Id + ".route",
                definition.Route.Select(StudioRouteSegment.Literal)),
            definition.Kind, presentation, [], definition.Endpoints, [],
            StudioDisclosureClass.ProtectedValue);
    }

    private static StudioFrontendExport CreateFrontend()
        => StudioFrontendExport.Create("gateway", 1,
            [StudioFrontendClientSlot.Create(ClientId, 1,
                StudioFrameworkClientProtocol.FrameworkGeneratedContractV1, RuntimeAbi, ClientContract,
                OperationInventoryChecksum, "gateway.admin.v1", StudioFrameworkClientTransportClass.SameOriginShellAuthenticated,
                PageDefinitions.Select(static value => value.Id).Order(StringComparer.Ordinal), ClientLimits())],
            PageDefinitions.OrderBy(static value => value.Id, StringComparer.Ordinal)
                .Select(static value => StudioPageComponentBinding.Create(
                    value.Id, "component." + value.Id, ComponentAbi)));

    private static StudioFrameworkClientLimits ClientLimits() => StudioFrameworkClientLimits.Create(
        23, 8_388_608, 8_388_608, 8, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

    internal static StudioFrameworkClientRegistration CreateClientRegistration(IEnumerable<string> pageIds)
        => StudioFrameworkClientRegistration.Create(ClientId, 1, StudioContractNecessity.Required,
            StudioFrameworkClientProtocol.FrameworkGeneratedContractV1, RuntimeAbi, ClientContract, OperationInventoryChecksum,
            "gateway.admin.v1", StudioFrameworkClientTransportClass.SameOriginShellAuthenticated,
            pageIds, ClientLimits());

    private static StudioAssetManifest CreateAsset()
    {
        const string path = "gateway/a5c79b82643f4d311cade5cbfb3614d84e63aeef61dd555396c27a5174aaa47f.js";
        using Stream stream = typeof(GatewayStudioModuleRegistry).Assembly
            .GetManifestResourceStream("HPD.Gateway.ControlPlane.Studio.Assets.gateway.js")
            ?? throw new InvalidOperationException("The prebuilt Gateway Studio module asset is absent.");
        using var content = new MemoryStream();
        stream.CopyTo(content);
        return StudioAssetManifest.Create(path, StudioModuleNecessity.Optional,
            StudioShellContract.Current,
            [StudioAssetSource.Create(path, StudioAssetMediaType.JavaScriptModule,
                content.GetBuffer().AsSpan(0, checked((int)content.Length)))]);
    }

    private static StudioSha256 Digest(string value)
        => StudioSha256.FromDigest(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value)));

    private static readonly ImmutableArray<PageDefinition> PageDefinitions =
    [
        new("gateway.configure", StudioArea.Infrastructure, "studio.page.gateway.configure",
            ["gateway", "configure"], StudioPageKind.Action,
            ["gateway.admin.hostCapabilities", "gateway.admin.validate"]),
        new("gateway.diagnose", StudioArea.Diagnostics, "studio.page.gateway.diagnose",
            ["gateway", "diagnose"], StudioPageKind.Diagnostics,
            ["gateway.admin.audit", "gateway.admin.effective", "gateway.admin.status"]),
        new("gateway.operate", StudioArea.Infrastructure, "studio.page.gateway.operate",
            ["gateway", "operate"], StudioPageKind.Action,
            ["gateway.admin.activate", "gateway.admin.activations", "gateway.admin.operation", "gateway.admin.revisions"]),
        new("gateway.overview", StudioArea.Overview, "studio.page.gateway.overview",
            ["gateway"], StudioPageKind.Overview,
            ["gateway.admin.capabilities", "gateway.admin.desired", "gateway.admin.effective", "gateway.admin.hostCapabilities", "gateway.admin.status"]),
    ];

    private sealed record PageDefinition(string Id, StudioArea Area, string Label,
        ImmutableArray<string> Route, StudioPageKind Kind, ImmutableArray<string> Endpoints);
}
