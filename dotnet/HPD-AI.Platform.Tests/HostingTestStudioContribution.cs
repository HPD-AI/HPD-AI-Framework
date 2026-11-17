using HPD.AI.Platform.Studio;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.AI.Platform.Tests;

internal sealed class HostingTestStudioContribution : IStudioModuleContribution, IStudioModuleRuntimeContributionFactory
{
    public string ModuleId => "base";
    internal static StudioEditionModuleAssetContribution CreateEditionAssetContribution()
    {
        StudioAssetManifest asset = StudioAssetManifest.Create("assets/base.js", StudioModuleNecessity.Required,
            StudioShellContract.Current, [StudioAssetSource.Create("assets/base.js", StudioAssetMediaType.JavaScriptModule,
                "export const activateStudioModule=()=>{};"u8)]);
        return StudioEditionModuleAssetContribution.Create("base", 1, Digest(6), asset);
    }
    public StudioModuleRegistration Create(IServiceProvider services)
    {
        StudioShellContract shell = services.GetRequiredService<StudioShellContract>();
        StudioAssetManifest asset = StudioAssetManifest.Create("assets/base.js", StudioModuleNecessity.Required, shell,
            [StudioAssetSource.Create("assets/base.js", StudioAssetMediaType.JavaScriptModule, "export const activateStudioModule=()=>{};"u8)]);
        StudioResourceRegistration resource = StudioResourceRegistration.Create(
            StudioResourceKind.Application, "base.application.resolve", ["base.application.read"], [], StudioDisclosureClass.AuthorizedMetadata);

        var pages = new List<StudioPageRegistration>();
        var views = new List<StudioViewRegistration>();
        foreach (StudioArea area in Enum.GetValues<StudioArea>())
        {
            string suffix = area.ToString().ToLowerInvariant();
            string pageId = "base." + suffix;
            string viewId = pageId + ".view";
            StudioSectionRegistration section = StudioSectionRegistration.Create(
                "summary", "studio.section.summary", 0, StudioSectionKind.Summary, [viewId], []);
            StudioPagePresentationRegistration presentation = StudioPagePresentationRegistration.Create(
                pageId, 1, StudioNavigationRole.AreaLanding, StudioWorkspaceKind.Landing,
                [section], null, null, StudioDraftRetentionClass.None);
            StudioRouteTemplate route = StudioRouteTemplate.Create(pageId + ".route",
                area == StudioArea.Overview ? [] : [StudioRouteSegment.Literal(suffix)]);
            pages.Add(StudioPageRegistration.Create(pageId, 1, area, "studio.page." + suffix, route,
                StudioPageKind.Overview, presentation, [StudioResourceKind.Application],
                ["base.application.read"], [], StudioDisclosureClass.AuthorizedMetadata));
            StudioGridColumnDefinition column = StudioGridColumnDefinition.Create(
                "identity", "base.application.identity", StudioGridRendererKind.IdentityLink,
                StudioGridDisclosureBehavior.SafeLabelOnly, "studio.column.identity", true, 0, 240, 160, 600);
            StudioGridDefinition grid = StudioGridDefinition.Create(viewId + ".grid", 1,
                StudioResourceKind.Application, "base.application.row", Digest(3), [column],
                StudioSelectionMode.None, [], 100, 25, 100, 1_000_000);
            StudioViewPresentationRegistration viewPresentation = StudioViewPresentationRegistration.Create(
                viewId, grid, null, StudioEmptyStateKind.NoItems,
                StudioActivityPolicy.Create(StudioActivityPolicyKind.ExplicitRefreshOnly, 10, 3, 32),
                StudioPreferenceSchema.Create(viewId + ".preferences", 1, [], 1, TimeSpan.FromDays(1)));
            views.Add(StudioViewRegistration.Create(viewId, 1, viewId + ".producer",
                viewId + ".request", Digest(4), StudioResourceKind.Application, "base.application.row", Digest(3),
                viewId + ".cursor", [StudioOrderMember.Create("base.application.identity", StudioOrderDirection.Ascending,
                    StudioNullPlacement.ValuesThenMissingThenNull)], [], [], Digest(5), 1_000_000, 100, viewPresentation));
        }

        pages.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.PageId, right.PageId));
        views.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.ViewId, right.ViewId));
        string[] pageIds = pages.Select(static page => page.PageId).ToArray();
        StudioFrameworkClientLimits clientLimits = StudioFrameworkClientLimits.Create(32, 1_000_000, 1_000_000, 4,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
        StudioFrameworkClientRegistration client = StudioFrameworkClientRegistration.Create(
            "base.control-plane", 1, StudioContractNecessity.Required, StudioFrameworkClientProtocol.BaseL41DynamicMap,
            Digest(1), Digest(2), Digest(3), "base.runtime", StudioFrameworkClientTransportClass.SameOriginShellAuthenticated,
            pageIds, clientLimits);
        StudioFrontendExport frontend = StudioFrontendExport.Create("base", 1,
            [StudioFrontendClientSlot.Create("base.control-plane", 1, StudioFrameworkClientProtocol.BaseL41DynamicMap,
                Digest(1), Digest(2), Digest(3), "base.runtime", StudioFrameworkClientTransportClass.SameOriginShellAuthenticated,
                pageIds, clientLimits)],
            pages.Select(static page => StudioPageComponentBinding.Create(page.PageId, "component." + page.PageId, Digest(6))));
        return StudioModuleRegistration.CreateBase("sample.application", asset, frontend, pages, views,
            [resource], [], [], [client], [], StudioModuleLimits.Create(64, 256, 128, 256, 256, 32));
    }

    internal static StudioSha256 Digest(byte value) => StudioSha256.FromDigest(Enumerable.Repeat(value, 32).ToArray());

    StudioModuleRuntimeContribution IStudioModuleRuntimeContributionFactory.Create(StudioModuleRegistration module)
    {
        StudioNamedTypeContract request = StudioNamedTypeContract.Create("test.request", "{\"kind\":\"object\",\"properties\":[],\"additionalProperties\":false}"u8);
        StudioNamedTypeContract result = StudioNamedTypeContract.Create("test.result", "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":64,\"format\":\"plain\"}"u8);
        StudioNamedTypeContract error = StudioNamedTypeContract.Create("test.error", "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":64,\"format\":\"plain\"}"u8);
        StudioEndpointContract endpoint = StudioEndpointContract.Create("test.page", 1, StudioTransportMethod.Post,
            "/test/page", StudioEndpointAudience.ControlPlane, StudioTransportKind.SameOriginHttp,
            request.TypeId, request.NodeChecksum, result.TypeId, result.NodeChecksum, error.TypeId, error.NodeChecksum,
            1024, 1024, TimeSpan.FromSeconds(1));
        StudioMethodBinding[] methods = module.Pages.Select(page => StudioMethodBinding.Create("test.page." + page.PageId,
            StudioMethodKind.Page, "base", page.PageId, endpoint.EndpointId, request.TypeId, result.TypeId)).OrderBy(static value => value.RegisteredMethodId, StringComparer.Ordinal).ToArray();
        StudioProducerBinding[] producers = methods.Select(method => (StudioProducerBinding)new StudioViewProducerBinding(method.RegisteredMethodId, new TestViewProducer()))
            .OrderBy(static value => value.RegisteredMethodId, StringComparer.Ordinal).ToArray();
        return StudioModuleRuntimeContribution.Create(module, new[] { error, request, result }.OrderBy(static value => value.TypeId, StringComparer.Ordinal), [endpoint], methods, producers);
    }

    private sealed class TestViewProducer : IStudioViewProducer
    {
        public ValueTask<StudioCanonicalJson?> ReadAsync(StudioProducerInvocation invocation, CancellationToken cancellationToken)
            => ValueTask.FromResult<StudioCanonicalJson?>(StudioCanonicalJson.Create("\"ok\""u8, 16));
    }
}

internal sealed class HostingTestBootstrapRuntime : IStudioBootstrapRuntime
{
    internal StudioBootstrapInvocation? Invocation { get; private set; }
    public ValueTask<StudioBootstrapSnapshot?> CreateAsync(StudioBootstrapInvocation invocation, CancellationToken cancellationToken)
    {
        Invocation = invocation;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        StudioResponseAuthority authority = StudioResponseAuthority.Create(
            invocation.Authorization.Session.PrincipalGeneration, invocation.Authorization.Session.SessionChecksum,
            invocation.Authorization.Session.ProtectedScopeChecksum, invocation.ApplicationGraph.Generation,
            invocation.ApplicationGraph.Checksum, invocation.ApplicationGraph.Generation, invocation.ApplicationGraph.Checksum,
            1, HostingTestStudioContribution.Digest(22), [], now, invocation.Authorization.Session.ExpiresAtUtc,
            [invocation.Authorization.AuthorizedThroughUtc]);
        StudioContractMap map = StudioContractMap.Create("base.protocol", "base.json", "base.error", "base.realtime",
            invocation.Request.RuntimeClientChecksum, HostingTestStudioContribution.Digest(23), [], [], [], new HashSet<(string, string)>());
        StudioShellLimits limits = StudioShellLimits.Create(64, 512, 256, 128, 32, 16_777_216, 32_000_000, TimeSpan.FromSeconds(10));
        StudioBootstrapSnapshot snapshot = StudioBootstrapSnapshot.Create(invocation.ApplicationGraph.ApplicationId,
            StudioMode.Inspect, authority, [], [], [], [], [], [], map, limits, now, authority.AuthorizedThroughUtc);
        return ValueTask.FromResult<StudioBootstrapSnapshot?>(snapshot);
    }
}
