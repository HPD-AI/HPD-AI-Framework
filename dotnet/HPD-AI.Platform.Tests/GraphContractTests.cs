using HPD.AI.Platform.Studio;
using Xunit;

namespace HPD.AI.Platform.Tests;

public sealed class StudioGraphContractTests
{
    [Fact]
    public void Grant_requirement_binds_complete_installed_authority()
    {
        StudioSha256 installed = StudioSha256.FromDigest(new byte[32]);
        StudioGrantRequirement grant = StudioGrantRequirement.Create(
            "base.studio.resource.inspect.grant", 1, installed, "base.studio.resource.inspect",
            "control-plane", "human", "sample.application", "base", StudioResourceKind.Record,
            StudioProtectedScopeRule.ResourceExact, true);

        Assert.Equal("base.studio.resource.inspect", grant.OperationId);
        Assert.Equal(StudioResourceKind.Record, grant.ResourceKind);
        Assert.True(grant.RequiresUnderlyingOperationGrant);
        Assert.Throws<ArgumentOutOfRangeException>(() => StudioGrantRequirement.Create(
            "base.studio.resource.inspect.grant", 1, installed, "base.studio.resource.inspect",
            "control-plane", "human", "sample.application", "base", (StudioResourceKind)255,
            StudioProtectedScopeRule.ResourceExact, true));
    }

    [Fact]
    public void Root_route_is_the_canonical_empty_segment_template()
    {
        StudioRouteTemplate root = StudioRouteTemplate.Create("base.overview.route", []);

        Assert.Empty(root.Segments);
        Assert.True(root.Overlaps(StudioRouteTemplate.Create("base.overview.other", [])));
        Assert.False(root.Overlaps(StudioRouteTemplate.Create("base.data.route", [StudioRouteSegment.Literal("data")])));
    }

    [Fact]
    public void Frontend_abi_matches_the_locked_browser_vector()
    {
        StudioFrontendExport value = StudioFrontendExport.Create("base", 1,
            [StudioFrontendClientSlot.Create("base.control-plane", 1, StudioFrameworkClientProtocol.BaseL41DynamicMap,
                Digest(4), Digest(5), Digest(6), "base.runtime", StudioFrameworkClientTransportClass.SameOriginShellAuthenticated,
                ["base.overview"], ClientLimits())],
            [StudioPageComponentBinding.Create("base.overview", "base.overview.component", Digest(4))]);

        Assert.Equal("c3957d780398e784d2b8e58176cbed529fd1335579c82304cf7d299aa673ecde",
            Convert.ToHexString(value.FrontendAbiChecksum.ToArray()).ToLowerInvariant());
    }

    [Fact]
    public void Complete_module_graph_is_deterministic_and_cross_validated()
    {
        StudioModuleRegistration first = CreateModule();
        StudioModuleRegistration second = CreateModule();

        Assert.True(StudioSha256.FixedTimeEquals(first.Identity.Checksum, second.Identity.Checksum));
        Assert.Equal("base.data", first.Pages.Single().PageId);
        Assert.Equal("base.records.list", first.Views.Single().ViewId);
    }

    [Fact]
    public void Dangling_section_view_fails_graph_finalization()
    {
        StudioPageRegistration page = CreatePage("base.missing.view");

        Assert.Throws<ArgumentException>(() => StudioModuleRegistration.CreateBase(
            "sample.application", Asset(), Frontend("base.data"),
            [page], [CreateView()], [Resource()], [], [], [Client()], [], Limits()));
    }

    [Fact]
    public void Disclosed_area_requires_exactly_one_landing()
    {
        StudioPageRegistration contextual = CreatePage("base.records.list", StudioNavigationRole.Contextual);

        Assert.Throws<ArgumentException>(() => StudioModuleRegistration.CreateBase(
            "sample.application", Asset(), Frontend("base.data"),
            [contextual], [CreateView()], [Resource()], [], [], [Client()], [], Limits()));
    }

    [Fact]
    public void Route_query_members_must_be_canonical()
    {
        Assert.Throws<ArgumentException>(() => StudioRouteTemplate.Create("base.records.route",
            [StudioRouteSegment.Literal("data")],
            [StudioQueryParameter.Create("z", StudioRouteCodec.Identifier, false),
             StudioQueryParameter.Create("a", StudioRouteCodec.Identifier, false)]));
    }

    [Fact]
    public void Ambiguous_route_shapes_fail_module_finalization()
    {
        StudioPageRegistration first = CreatePage("base.records.list");
        StudioPageRegistration second = CreatePageWithIdentity(
            "base.data.alternate", "base.data.alternate.route", "studio.page.data.alternate",
            StudioNavigationRole.Contextual, "base.records.second");

        Assert.Throws<ArgumentException>(() => StudioModuleRegistration.CreateBase(
            "sample.application", Asset(), Frontend("base.data", "base.data.alternate"),
            [first, second], [CreateView(), CreateView("base.records.second")], [Resource()], [], [], [Client()], [], Limits()));
    }

    [Fact]
    public void Literal_parameter_and_parameter_codec_routes_overlap()
    {
        StudioRouteTemplate literal = StudioRouteTemplate.Create("literal", [StudioRouteSegment.Literal("data")]);
        StudioRouteTemplate identifier = StudioRouteTemplate.Create("identifier", [StudioRouteSegment.Parameter("id", StudioRouteCodec.Identifier)]);
        StudioRouteTemplate digest = StudioRouteTemplate.Create("digest", [StudioRouteSegment.Parameter("digest", StudioRouteCodec.Sha256)]);

        Assert.True(literal.Overlaps(identifier));
        Assert.True(identifier.Overlaps(digest));
    }

    [Fact]
    public void Page_rejects_substituted_presentation_owner()
    {
        StudioSectionRegistration section = StudioSectionRegistration.Create(
            "summary", "studio.section.summary", 0, StudioSectionKind.Summary, [], []);
        StudioPagePresentationRegistration foreign = StudioPagePresentationRegistration.Create(
            "base.foreign", 1, StudioNavigationRole.Contextual, StudioWorkspaceKind.Detail,
            [section], null, null, StudioDraftRetentionClass.None);

        Assert.Throws<ArgumentException>(() => StudioPageRegistration.Create(
            "base.record.detail", 1, StudioArea.Data, "studio.page.record",
            StudioRouteTemplate.Create("base.record.route", [StudioRouteSegment.Literal("record")]),
            StudioPageKind.ResourceDetail, foreign, [StudioResourceKind.Record],
            ["base.records.read"], [], StudioDisclosureClass.ProtectedValue));
    }

    [Fact]
    public void Framework_module_cannot_claim_area_landing()
    {
        Assert.Throws<ArgumentException>(() => StudioModuleRegistration.CreateFramework(
            "graph", 1, "sample.application", "graph", "studio.module.graph", Asset(), Frontend("base.data", module: "graph"),
            [CreatePage("base.records.list")], [CreateView()], [Resource()], [], [], [Client()], [], Limits()));
    }

    [Fact]
    public void Public_framework_factory_cannot_impersonate_reserved_base_owner()
    {
        Assert.Throws<ArgumentException>(() => StudioModuleRegistration.CreateFramework(
            "base", 1, "sample.application", "base", "studio.module.base", Asset(), Frontend("base.data"),
            [CreatePage("base.records.list")], [CreateView()], [Resource()], [], [], [Client()], [], Limits()));
    }

    private static StudioModuleRegistration CreateModule() => StudioModuleRegistration.CreateBase(
        "sample.application", Asset(), Frontend("base.data"),
        [CreatePage("base.records.list")], [CreateView()], [Resource()], [], [], [Client()], [], Limits());

    private static StudioPageRegistration CreatePage(string viewId,
        StudioNavigationRole role = StudioNavigationRole.AreaLanding)
        => CreatePageWithIdentity("base.data", "base.data.route", "studio.page.data", role, viewId);

    private static StudioPageRegistration CreatePageWithIdentity(string pageId, string routeId, string label,
        StudioNavigationRole role, string viewId = "base.records.list")
    {
        StudioSectionRegistration section = StudioSectionRegistration.Create(
            "records", "studio.section.records", 0, StudioSectionKind.Summary, [viewId], []);
        StudioPagePresentationRegistration presentation = StudioPagePresentationRegistration.Create(
            pageId, 1, role, StudioWorkspaceKind.Landing, [section], null, null,
            StudioDraftRetentionClass.None);
        return StudioPageRegistration.Create(pageId, 1, StudioArea.Data, label,
            StudioRouteTemplate.Create(routeId, [StudioRouteSegment.Literal("data")]),
            StudioPageKind.Overview, presentation, [StudioResourceKind.Record],
            ["base.records.read"], [], StudioDisclosureClass.AuthorizedMetadata);
    }

    private static StudioViewRegistration CreateView(string viewId = "base.records.list")
    {
        StudioGridColumnDefinition column = StudioGridColumnDefinition.Create(
            "identity", "base.record.identity", StudioGridRendererKind.IdentityLink,
            StudioGridDisclosureBehavior.SafeLabelOnly, "studio.column.identity", true, 0, 240, 160, 600);
        StudioGridDefinition grid = StudioGridDefinition.Create("base.records.grid", 1,
            StudioResourceKind.Record, "base.record.row", Digest(2), [column], StudioSelectionMode.None,
            [], 100, 25, 1_000, 1_000_000);
        StudioViewPresentationRegistration presentation = StudioViewPresentationRegistration.Create(
            viewId, grid, null, StudioEmptyStateKind.NoItems,
            StudioActivityPolicy.Create(StudioActivityPolicyKind.GovernedInvalidationRefresh, 10, 3, 32),
            StudioPreferenceSchema.Create("base.records.preferences", 1, [], 1, TimeSpan.FromDays(1)));
        return StudioViewRegistration.Create(viewId, 1, "base.records.runtime",
            "base.records.request", Digest(3), StudioResourceKind.Record, "base.record.row", Digest(2), "base.records.cursor",
            [StudioOrderMember.Create("base.record.identity", StudioOrderDirection.Ascending,
                StudioNullPlacement.ValuesThenMissingThenNull)], [], [], Digest(6),
            1_000_000, 1_000, presentation);
    }

    private static StudioResourceRegistration Resource() => StudioResourceRegistration.Create(
        StudioResourceKind.Record, "base.record.resolve", ["base.records.read"], [],
        StudioDisclosureClass.ProtectedValue);

    private static StudioFrameworkClientRegistration Client() => StudioFrameworkClientRegistration.Create(
        "base.control-plane", 1, StudioContractNecessity.Required, StudioFrameworkClientProtocol.BaseL41DynamicMap,
        Digest(4), Digest(5), Digest(6), "base.runtime", StudioFrameworkClientTransportClass.SameOriginShellAuthenticated,
        ["base.data"], ClientLimits());

    private static StudioFrameworkClientLimits ClientLimits() => StudioFrameworkClientLimits.Create(
        32, 1_000_000, 1_000_000, 4, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));

    private static StudioModuleLimits Limits() => StudioModuleLimits.Create(64, 256, 128, 256, 256, 32);

    private static StudioAssetManifest Asset() => StudioAssetManifest.Create(
        "assets/base.js", StudioModuleNecessity.Required, StudioShellContract.Current,
        [StudioAssetSource.Create("assets/base.js", StudioAssetMediaType.JavaScriptModule, new byte[] { 2 })]);

    private static StudioFrontendExport Frontend(string page, string? secondPage = null, string module = "base") =>
        StudioFrontendExport.Create(module, 1,
            [StudioFrontendClientSlot.Create("base.control-plane", 1, StudioFrameworkClientProtocol.BaseL41DynamicMap,
                Digest(4), Digest(5), Digest(6), "base.runtime", StudioFrameworkClientTransportClass.SameOriginShellAuthenticated,
                secondPage is null ? [page] : [page, secondPage], ClientLimits())],
            secondPage is null
                ? [StudioPageComponentBinding.Create(page, $"component.{page}", Digest(9))]
                : [StudioPageComponentBinding.Create(page, $"component.{page}", Digest(9)),
                   StudioPageComponentBinding.Create(secondPage, $"component.{secondPage}", Digest(9))]);

    private static StudioSha256 Digest(byte value) => StudioSha256.Compute(Enumerable.Repeat(value, 32).ToArray());
}
