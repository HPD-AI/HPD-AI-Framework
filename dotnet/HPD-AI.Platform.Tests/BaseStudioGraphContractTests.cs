using HPD.AI.Platform.Studio;
using Xunit;

namespace HPD.AI.Platform.Tests;

public sealed class BaseStudioGraphContractTests
{
    [Fact]
    public void Grant_requirement_binds_complete_installed_authority()
    {
        BaseStudioSha256 installed = BaseStudioSha256.FromDigest(new byte[32]);
        BaseStudioGrantRequirement grant = BaseStudioGrantRequirement.Create(
            "base.studio.resource.inspect.grant", 1, installed, "base.studio.resource.inspect",
            "control-plane", "human", "sample.application", "base", BaseStudioResourceKind.Record,
            BaseStudioProtectedScopeRule.ResourceExact, true);

        Assert.Equal("base.studio.resource.inspect", grant.OperationId);
        Assert.Equal(BaseStudioResourceKind.Record, grant.ResourceKind);
        Assert.True(grant.RequiresUnderlyingOperationGrant);
        Assert.Throws<ArgumentOutOfRangeException>(() => BaseStudioGrantRequirement.Create(
            "base.studio.resource.inspect.grant", 1, installed, "base.studio.resource.inspect",
            "control-plane", "human", "sample.application", "base", (BaseStudioResourceKind)255,
            BaseStudioProtectedScopeRule.ResourceExact, true));
    }

    [Fact]
    public void Root_route_is_the_canonical_empty_segment_template()
    {
        BaseStudioRouteTemplate root = BaseStudioRouteTemplate.Create("base.overview.route", []);

        Assert.Empty(root.Segments);
        Assert.True(root.Overlaps(BaseStudioRouteTemplate.Create("base.overview.other", [])));
        Assert.False(root.Overlaps(BaseStudioRouteTemplate.Create("base.data.route", [BaseStudioRouteSegment.Literal("data")])));
    }

    [Fact]
    public void Frontend_abi_matches_the_locked_browser_vector()
    {
        BaseStudioFrontendExport value = BaseStudioFrontendExport.Create("base", 1,
            [BaseStudioFrontendClientSlot.Create("base.control-plane", 1, BaseStudioFrameworkClientProtocol.BaseL41DynamicMap,
                Digest(4), Digest(5), Digest(6), "base.runtime", BaseStudioFrameworkClientTransportClass.SameOriginShellAuthenticated,
                ["base.overview"], ClientLimits())],
            [BaseStudioPageComponentBinding.Create("base.overview", "base.overview.component", Digest(4))]);

        Assert.Equal("c3957d780398e784d2b8e58176cbed529fd1335579c82304cf7d299aa673ecde",
            Convert.ToHexString(value.FrontendAbiChecksum.ToArray()).ToLowerInvariant());
    }

    [Fact]
    public void Complete_module_graph_is_deterministic_and_cross_validated()
    {
        BaseStudioModuleRegistration first = CreateModule();
        BaseStudioModuleRegistration second = CreateModule();

        Assert.True(BaseStudioSha256.FixedTimeEquals(first.Identity.Checksum, second.Identity.Checksum));
        Assert.Equal("base.data", first.Pages.Single().PageId);
        Assert.Equal("base.records.list", first.Views.Single().ViewId);
    }

    [Fact]
    public void Dangling_section_view_fails_graph_finalization()
    {
        BaseStudioPageRegistration page = CreatePage("base.missing.view");

        Assert.Throws<ArgumentException>(() => BaseStudioModuleRegistration.CreateBase(
            "sample.application", Asset(), Frontend("base.data"),
            [page], [CreateView()], [Resource()], [], [], [Client()], [], Limits()));
    }

    [Fact]
    public void Disclosed_area_requires_exactly_one_landing()
    {
        BaseStudioPageRegistration contextual = CreatePage("base.records.list", BaseStudioNavigationRole.Contextual);

        Assert.Throws<ArgumentException>(() => BaseStudioModuleRegistration.CreateBase(
            "sample.application", Asset(), Frontend("base.data"),
            [contextual], [CreateView()], [Resource()], [], [], [Client()], [], Limits()));
    }

    [Fact]
    public void Route_query_members_must_be_canonical()
    {
        Assert.Throws<ArgumentException>(() => BaseStudioRouteTemplate.Create("base.records.route",
            [BaseStudioRouteSegment.Literal("data")],
            [BaseStudioQueryParameter.Create("z", BaseStudioRouteCodec.Identifier, false),
             BaseStudioQueryParameter.Create("a", BaseStudioRouteCodec.Identifier, false)]));
    }

    [Fact]
    public void Ambiguous_route_shapes_fail_module_finalization()
    {
        BaseStudioPageRegistration first = CreatePage("base.records.list");
        BaseStudioPageRegistration second = CreatePageWithIdentity(
            "base.data.alternate", "base.data.alternate.route", "studio.page.data.alternate",
            BaseStudioNavigationRole.Contextual, "base.records.second");

        Assert.Throws<ArgumentException>(() => BaseStudioModuleRegistration.CreateBase(
            "sample.application", Asset(), Frontend("base.data", "base.data.alternate"),
            [first, second], [CreateView(), CreateView("base.records.second")], [Resource()], [], [], [Client()], [], Limits()));
    }

    [Fact]
    public void Literal_parameter_and_parameter_codec_routes_overlap()
    {
        BaseStudioRouteTemplate literal = BaseStudioRouteTemplate.Create("literal", [BaseStudioRouteSegment.Literal("data")]);
        BaseStudioRouteTemplate identifier = BaseStudioRouteTemplate.Create("identifier", [BaseStudioRouteSegment.Parameter("id", BaseStudioRouteCodec.Identifier)]);
        BaseStudioRouteTemplate digest = BaseStudioRouteTemplate.Create("digest", [BaseStudioRouteSegment.Parameter("digest", BaseStudioRouteCodec.Sha256)]);

        Assert.True(literal.Overlaps(identifier));
        Assert.True(identifier.Overlaps(digest));
    }

    [Fact]
    public void Page_rejects_substituted_presentation_owner()
    {
        BaseStudioSectionRegistration section = BaseStudioSectionRegistration.Create(
            "summary", "studio.section.summary", 0, BaseStudioSectionKind.Summary, [], []);
        BaseStudioPagePresentationRegistration foreign = BaseStudioPagePresentationRegistration.Create(
            "base.foreign", 1, BaseStudioNavigationRole.Contextual, BaseStudioWorkspaceKind.Detail,
            [section], null, null, BaseStudioDraftRetentionClass.None);

        Assert.Throws<ArgumentException>(() => BaseStudioPageRegistration.Create(
            "base.record.detail", 1, BaseStudioArea.Data, "studio.page.record",
            BaseStudioRouteTemplate.Create("base.record.route", [BaseStudioRouteSegment.Literal("record")]),
            BaseStudioPageKind.ResourceDetail, foreign, [BaseStudioResourceKind.Record],
            ["base.records.read"], [], BaseStudioDisclosureClass.ProtectedValue));
    }

    [Fact]
    public void Framework_module_cannot_claim_area_landing()
    {
        Assert.Throws<ArgumentException>(() => BaseStudioModuleRegistration.CreateFramework(
            "graph", 1, "sample.application", "graph", "studio.module.graph", Asset(), Frontend("base.data", module: "graph"),
            [CreatePage("base.records.list")], [CreateView()], [Resource()], [], [], [Client()], [], Limits()));
    }

    [Fact]
    public void Public_framework_factory_cannot_impersonate_reserved_base_owner()
    {
        Assert.Throws<ArgumentException>(() => BaseStudioModuleRegistration.CreateFramework(
            "base", 1, "sample.application", "base", "studio.module.base", Asset(), Frontend("base.data"),
            [CreatePage("base.records.list")], [CreateView()], [Resource()], [], [], [Client()], [], Limits()));
    }

    private static BaseStudioModuleRegistration CreateModule() => BaseStudioModuleRegistration.CreateBase(
        "sample.application", Asset(), Frontend("base.data"),
        [CreatePage("base.records.list")], [CreateView()], [Resource()], [], [], [Client()], [], Limits());

    private static BaseStudioPageRegistration CreatePage(string viewId,
        BaseStudioNavigationRole role = BaseStudioNavigationRole.AreaLanding)
        => CreatePageWithIdentity("base.data", "base.data.route", "studio.page.data", role, viewId);

    private static BaseStudioPageRegistration CreatePageWithIdentity(string pageId, string routeId, string label,
        BaseStudioNavigationRole role, string viewId = "base.records.list")
    {
        BaseStudioSectionRegistration section = BaseStudioSectionRegistration.Create(
            "records", "studio.section.records", 0, BaseStudioSectionKind.Summary, [viewId], []);
        BaseStudioPagePresentationRegistration presentation = BaseStudioPagePresentationRegistration.Create(
            pageId, 1, role, BaseStudioWorkspaceKind.Landing, [section], null, null,
            BaseStudioDraftRetentionClass.None);
        return BaseStudioPageRegistration.Create(pageId, 1, BaseStudioArea.Data, label,
            BaseStudioRouteTemplate.Create(routeId, [BaseStudioRouteSegment.Literal("data")]),
            BaseStudioPageKind.Overview, presentation, [BaseStudioResourceKind.Record],
            ["base.records.read"], [], BaseStudioDisclosureClass.AuthorizedMetadata);
    }

    private static BaseStudioViewRegistration CreateView(string viewId = "base.records.list")
    {
        BaseStudioGridColumnDefinition column = BaseStudioGridColumnDefinition.Create(
            "identity", "base.record.identity", BaseStudioGridRendererKind.IdentityLink,
            BaseStudioGridDisclosureBehavior.SafeLabelOnly, "studio.column.identity", true, 0, 240, 160, 600);
        BaseStudioGridDefinition grid = BaseStudioGridDefinition.Create("base.records.grid", 1,
            BaseStudioResourceKind.Record, "base.record.row", Digest(2), [column], BaseStudioSelectionMode.None,
            [], 100, 25, 1_000, 1_000_000);
        BaseStudioViewPresentationRegistration presentation = BaseStudioViewPresentationRegistration.Create(
            viewId, grid, null, BaseStudioEmptyStateKind.NoItems,
            BaseStudioActivityPolicy.Create(BaseStudioActivityPolicyKind.GovernedInvalidationRefresh, 10, 3, 32),
            BaseStudioPreferenceSchema.Create("base.records.preferences", 1, [], 1, TimeSpan.FromDays(1)));
        return BaseStudioViewRegistration.Create(viewId, 1, "base.records.runtime",
            "base.records.request", Digest(3), BaseStudioResourceKind.Record, "base.record.row", Digest(2), "base.records.cursor",
            [BaseStudioOrderMember.Create("base.record.identity", BaseStudioOrderDirection.Ascending,
                BaseStudioNullPlacement.ValuesThenMissingThenNull)], [], [], Digest(6),
            1_000_000, 1_000, presentation);
    }

    private static BaseStudioResourceRegistration Resource() => BaseStudioResourceRegistration.Create(
        BaseStudioResourceKind.Record, "base.record.resolve", ["base.records.read"], [],
        BaseStudioDisclosureClass.ProtectedValue);

    private static BaseStudioFrameworkClientRegistration Client() => BaseStudioFrameworkClientRegistration.Create(
        "base.control-plane", 1, BaseStudioContractNecessity.Required, BaseStudioFrameworkClientProtocol.BaseL41DynamicMap,
        Digest(4), Digest(5), Digest(6), "base.runtime", BaseStudioFrameworkClientTransportClass.SameOriginShellAuthenticated,
        ["base.data"], ClientLimits());

    private static BaseStudioFrameworkClientLimits ClientLimits() => BaseStudioFrameworkClientLimits.Create(
        32, 1_000_000, 1_000_000, 4, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));

    private static BaseStudioModuleLimits Limits() => BaseStudioModuleLimits.Create(64, 256, 128, 256, 256, 32);

    private static BaseStudioAssetManifest Asset() => BaseStudioAssetManifest.Create(
        "assets/base.js", BaseStudioModuleNecessity.Required, BaseStudioShellContract.Current,
        [BaseStudioAssetSource.Create("assets/base.js", BaseStudioAssetMediaType.JavaScriptModule, new byte[] { 2 })]);

    private static BaseStudioFrontendExport Frontend(string page, string? secondPage = null, string module = "base") =>
        BaseStudioFrontendExport.Create(module, 1,
            [BaseStudioFrontendClientSlot.Create("base.control-plane", 1, BaseStudioFrameworkClientProtocol.BaseL41DynamicMap,
                Digest(4), Digest(5), Digest(6), "base.runtime", BaseStudioFrameworkClientTransportClass.SameOriginShellAuthenticated,
                secondPage is null ? [page] : [page, secondPage], ClientLimits())],
            secondPage is null
                ? [BaseStudioPageComponentBinding.Create(page, $"component.{page}", Digest(9))]
                : [BaseStudioPageComponentBinding.Create(page, $"component.{page}", Digest(9)),
                   BaseStudioPageComponentBinding.Create(secondPage, $"component.{secondPage}", Digest(9))]);

    private static BaseStudioSha256 Digest(byte value) => BaseStudioSha256.Compute(Enumerable.Repeat(value, 32).ToArray());
}
