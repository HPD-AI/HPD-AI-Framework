using HPD.AI.Platform.Studio;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.AI.Platform.Tests;

internal sealed class HostingTestStudioContribution : IBaseStudioModuleContribution, IBaseStudioModuleRuntimeContributionFactory
{
    public string ModuleId => "base";
    internal static BaseStudioEditionModuleAssetContribution CreateEditionAssetContribution()
    {
        BaseStudioAssetManifest asset = BaseStudioAssetManifest.Create("assets/base.js", BaseStudioModuleNecessity.Required,
            BaseStudioShellContract.Current, [BaseStudioAssetSource.Create("assets/base.js", BaseStudioAssetMediaType.JavaScriptModule,
                "export const activateStudioModule=()=>{};"u8)]);
        return BaseStudioEditionModuleAssetContribution.Create("base", 1, Digest(6), asset);
    }
    public BaseStudioModuleRegistration Create(IServiceProvider services)
    {
        BaseStudioShellContract shell = services.GetRequiredService<BaseStudioShellContract>();
        BaseStudioAssetManifest asset = BaseStudioAssetManifest.Create("assets/base.js", BaseStudioModuleNecessity.Required, shell,
            [BaseStudioAssetSource.Create("assets/base.js", BaseStudioAssetMediaType.JavaScriptModule, "export const activateStudioModule=()=>{};"u8)]);
        BaseStudioResourceRegistration resource = BaseStudioResourceRegistration.Create(
            BaseStudioResourceKind.Application, "base.application.resolve", ["base.application.read"], [], BaseStudioDisclosureClass.AuthorizedMetadata);

        var pages = new List<BaseStudioPageRegistration>();
        var views = new List<BaseStudioViewRegistration>();
        foreach (BaseStudioArea area in Enum.GetValues<BaseStudioArea>())
        {
            string suffix = area.ToString().ToLowerInvariant();
            string pageId = "base." + suffix;
            string viewId = pageId + ".view";
            BaseStudioSectionRegistration section = BaseStudioSectionRegistration.Create(
                "summary", "studio.section.summary", 0, BaseStudioSectionKind.Summary, [viewId], []);
            BaseStudioPagePresentationRegistration presentation = BaseStudioPagePresentationRegistration.Create(
                pageId, 1, BaseStudioNavigationRole.AreaLanding, BaseStudioWorkspaceKind.Landing,
                [section], null, null, BaseStudioDraftRetentionClass.None);
            BaseStudioRouteTemplate route = BaseStudioRouteTemplate.Create(pageId + ".route",
                area == BaseStudioArea.Overview ? [] : [BaseStudioRouteSegment.Literal(suffix)]);
            pages.Add(BaseStudioPageRegistration.Create(pageId, 1, area, "studio.page." + suffix, route,
                BaseStudioPageKind.Overview, presentation, [BaseStudioResourceKind.Application],
                ["base.application.read"], [], BaseStudioDisclosureClass.AuthorizedMetadata));
            BaseStudioGridColumnDefinition column = BaseStudioGridColumnDefinition.Create(
                "identity", "base.application.identity", BaseStudioGridRendererKind.IdentityLink,
                BaseStudioGridDisclosureBehavior.SafeLabelOnly, "studio.column.identity", true, 0, 240, 160, 600);
            BaseStudioGridDefinition grid = BaseStudioGridDefinition.Create(viewId + ".grid", 1,
                BaseStudioResourceKind.Application, "base.application.row", Digest(3), [column],
                BaseStudioSelectionMode.None, [], 100, 25, 100, 1_000_000);
            BaseStudioViewPresentationRegistration viewPresentation = BaseStudioViewPresentationRegistration.Create(
                viewId, grid, null, BaseStudioEmptyStateKind.NoItems,
                BaseStudioActivityPolicy.Create(BaseStudioActivityPolicyKind.ExplicitRefreshOnly, 10, 3, 32),
                BaseStudioPreferenceSchema.Create(viewId + ".preferences", 1, [], 1, TimeSpan.FromDays(1)));
            views.Add(BaseStudioViewRegistration.Create(viewId, 1, viewId + ".producer",
                viewId + ".request", Digest(4), BaseStudioResourceKind.Application, "base.application.row", Digest(3),
                viewId + ".cursor", [BaseStudioOrderMember.Create("base.application.identity", BaseStudioOrderDirection.Ascending,
                    BaseStudioNullPlacement.ValuesThenMissingThenNull)], [], [], Digest(5), 1_000_000, 100, viewPresentation));
        }

        pages.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.PageId, right.PageId));
        views.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.ViewId, right.ViewId));
        string[] pageIds = pages.Select(static page => page.PageId).ToArray();
        BaseStudioFrameworkClientLimits clientLimits = BaseStudioFrameworkClientLimits.Create(32, 1_000_000, 1_000_000, 4,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
        BaseStudioFrameworkClientRegistration client = BaseStudioFrameworkClientRegistration.Create(
            "base.control-plane", 1, BaseStudioContractNecessity.Required, BaseStudioFrameworkClientProtocol.BaseL41DynamicMap,
            Digest(1), Digest(2), Digest(3), "base.runtime", BaseStudioFrameworkClientTransportClass.SameOriginShellAuthenticated,
            pageIds, clientLimits);
        BaseStudioFrontendExport frontend = BaseStudioFrontendExport.Create("base", 1,
            [BaseStudioFrontendClientSlot.Create("base.control-plane", 1, BaseStudioFrameworkClientProtocol.BaseL41DynamicMap,
                Digest(1), Digest(2), Digest(3), "base.runtime", BaseStudioFrameworkClientTransportClass.SameOriginShellAuthenticated,
                pageIds, clientLimits)],
            pages.Select(static page => BaseStudioPageComponentBinding.Create(page.PageId, "component." + page.PageId, Digest(6))));
        return BaseStudioModuleRegistration.CreateBase("sample.application", asset, frontend, pages, views,
            [resource], [], [], [client], [], BaseStudioModuleLimits.Create(64, 256, 128, 256, 256, 32));
    }

    internal static BaseStudioSha256 Digest(byte value) => BaseStudioSha256.FromDigest(Enumerable.Repeat(value, 32).ToArray());

    BaseStudioModuleRuntimeContribution IBaseStudioModuleRuntimeContributionFactory.Create(BaseStudioModuleRegistration module)
    {
        BaseStudioNamedTypeContract request = BaseStudioNamedTypeContract.Create("test.request", "{\"kind\":\"object\",\"properties\":[],\"additionalProperties\":false}"u8);
        BaseStudioNamedTypeContract result = BaseStudioNamedTypeContract.Create("test.result", "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":64,\"format\":\"plain\"}"u8);
        BaseStudioNamedTypeContract error = BaseStudioNamedTypeContract.Create("test.error", "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":64,\"format\":\"plain\"}"u8);
        BaseStudioEndpointContract endpoint = BaseStudioEndpointContract.Create("test.page", 1, BaseStudioTransportMethod.Post,
            "/test/page", BaseStudioEndpointAudience.ControlPlane, BaseStudioTransportKind.SameOriginHttp,
            request.TypeId, request.NodeChecksum, result.TypeId, result.NodeChecksum, error.TypeId, error.NodeChecksum,
            1024, 1024, TimeSpan.FromSeconds(1));
        BaseStudioMethodBinding[] methods = module.Pages.Select(page => BaseStudioMethodBinding.Create("test.page." + page.PageId,
            BaseStudioMethodKind.Page, "base", page.PageId, endpoint.EndpointId, request.TypeId, result.TypeId)).OrderBy(static value => value.RegisteredMethodId, StringComparer.Ordinal).ToArray();
        BaseStudioProducerBinding[] producers = methods.Select(method => (BaseStudioProducerBinding)new BaseStudioViewProducerBinding(method.RegisteredMethodId, new TestViewProducer()))
            .OrderBy(static value => value.RegisteredMethodId, StringComparer.Ordinal).ToArray();
        return BaseStudioModuleRuntimeContribution.Create(module, new[] { error, request, result }.OrderBy(static value => value.TypeId, StringComparer.Ordinal), [endpoint], methods, producers);
    }

    private sealed class TestViewProducer : IBaseStudioViewProducer
    {
        public ValueTask<BaseStudioCanonicalJson?> ReadAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
            => ValueTask.FromResult<BaseStudioCanonicalJson?>(BaseStudioCanonicalJson.Create("\"ok\""u8, 16));
    }
}

internal sealed class HostingTestBootstrapRuntime : IBaseStudioBootstrapRuntime
{
    internal BaseStudioBootstrapInvocation? Invocation { get; private set; }
    public ValueTask<BaseStudioBootstrapSnapshot?> CreateAsync(BaseStudioBootstrapInvocation invocation, CancellationToken cancellationToken)
    {
        Invocation = invocation;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        BaseStudioResponseAuthority authority = BaseStudioResponseAuthority.Create(
            invocation.Authorization.Session.PrincipalGeneration, invocation.Authorization.Session.SessionChecksum,
            invocation.Authorization.Session.ProtectedScopeChecksum, invocation.ApplicationGraph.Generation,
            invocation.ApplicationGraph.Checksum, invocation.ApplicationGraph.Generation, invocation.ApplicationGraph.Checksum,
            1, HostingTestStudioContribution.Digest(22), [], now, invocation.Authorization.Session.ExpiresAtUtc,
            [invocation.Authorization.AuthorizedThroughUtc]);
        BaseStudioContractMap map = BaseStudioContractMap.Create("base.protocol", "base.json", "base.error", "base.realtime",
            invocation.Request.RuntimeClientChecksum, HostingTestStudioContribution.Digest(23), [], [], [], new HashSet<(string, string)>());
        BaseStudioShellLimits limits = BaseStudioShellLimits.Create(64, 512, 256, 128, 32, 16_777_216, 32_000_000, TimeSpan.FromSeconds(10));
        BaseStudioBootstrapSnapshot snapshot = BaseStudioBootstrapSnapshot.Create(invocation.ApplicationGraph.ApplicationId,
            BaseStudioMode.Inspect, authority, [], [], [], [], [], [], map, limits, now, authority.AuthorizedThroughUtc);
        return ValueTask.FromResult<BaseStudioBootstrapSnapshot?>(snapshot);
    }
}
