using System.Collections.Immutable;
using System.Text;
using HPD.AI.Platform.Studio;
using HPD.Base;

namespace HPD.Graph.Studio;

/// <summary>Owns HPD Graph Studio's immutable version-one presentation and generated-client graph.</summary>
public static class GraphStudioModuleRegistry
{
    private const string ClientId = "graph.control-plane";
    private static readonly BaseStudioSha256 RuntimeAbi = Digest("graph.studio.generated-client-runtime-abi.v1");
    private static readonly BaseStudioSha256 ClientContract = Digest("graph.studio.generated-control-plane-contract.v1");
    private static readonly BaseStudioSha256 ComponentAbi = Digest("graph.studio.page-component-abi.v1");
    internal static readonly BaseStudioSha256 OperationInventory = BaseStudioFrameworkSurfaceOperation.ComputeInventoryChecksum(
        "graph.control-plane.v1", GraphStudioEndpointSurface.CreateOperations());

    private static readonly ImmutableArray<PageSpec> Pages =
    [
        new("graph.checkpoint.detail", BaseStudioArea.Operations, "operations/graph/checkpoints/:resource", BaseStudioPageKind.Timeline, ["summary", "state", "lineage", "evidence"], ["graph.checkpoint.get", "graph.execution.get"]),
        new("graph.definition.detail", BaseStudioArea.Operations, "operations/graph/definitions/:resource", BaseStudioPageKind.ResourceDetail, ["summary", "nodes", "channels", "executions", "baseWork"], ["graph.definition.get", "graph.execution.list"]),
        new("graph.execution.detail", BaseStudioArea.Operations, "operations/graph/executions/:resource", BaseStudioPageKind.Timeline, ["summary", "nodes", "channels", "checkpoints", "baseWork", "timeline"], ["graph.execution.get", "graph.execution.suspendedNodes"]),
        new("graph.executions", BaseStudioArea.Operations, "operations/graph/definitions/:resource/executions", BaseStudioPageKind.ResourceList, ["executions"], ["graph.execution.list"]),
        new("graph.overview", BaseStudioArea.Operations, "operations/graph", BaseStudioPageKind.Overview, ["definitions", "executions", "attention"], ["graph.definition.list"]),
        new("graph.topology.detail", BaseStudioArea.Operations, "operations/graph/topology/:resource", BaseStudioPageKind.ResourceDetail, ["nodes", "channels", "dependencies"], ["graph.definition.get"]),
    ];

    /// <summary>Creates the authorization-neutral trusted edition asset.</summary>
    public static BaseStudioEditionModuleAssetContribution CreateEditionAssetContribution()
    { BaseStudioFrontendExport frontend = Frontend(); return BaseStudioEditionModuleAssetContribution.Create("graph", 1, frontend.FrontendAbiChecksum, Asset()); }

    /// <summary>Creates Graph's semantic registration against finalized BASE application authority.</summary>
    public static BaseStudioModuleRegistration Create(HPDBaseStudioAuthoritySnapshot authority)
    {
        ArgumentNullException.ThrowIfNull(authority); BaseStudioGrantRequirement bootstrap = Grant(authority, "base.studio.bootstrap.read");
        BaseStudioGrantRequirement discover = Grant(authority, "base.studio.resource.discover");
        BaseStudioGrantRequirement inspect = Grant(authority, "base.studio.resource.inspect");
        BaseStudioPageRegistration[] pages = Pages.Select(spec => Page(spec, bootstrap, inspect)).OrderBy(static value => value.PageId, StringComparer.Ordinal).ToArray();
        BaseStudioResourceRegistration[] resources =
        [
            Resource(BaseStudioResourceKind.GraphDefinition, "graph.studio.resolve.definition", "graph.studio.resource.definition", discover, inspect),
            Resource(BaseStudioResourceKind.GraphExecution, "graph.studio.resolve.execution", "graph.studio.resource.execution", discover, inspect),
            Resource(BaseStudioResourceKind.GraphCheckpoint, "graph.studio.resolve.checkpoint", "graph.studio.resource.checkpoint", discover, inspect),
        ];
        BaseStudioLinkRegistration[] links =
        [
            BaseStudioLinkRegistration.Create(BaseStudioResourceKind.GraphDefinition, BaseStudioResourceKind.Schedule, BaseStudioLinkRelation.ScheduledBy, "graph.studio.link.definition.schedule"),
            BaseStudioLinkRegistration.Create(BaseStudioResourceKind.GraphExecution, BaseStudioResourceKind.Activation, BaseStudioLinkRelation.ProducedBy, "graph.studio.link.execution.activation"),
        ];
        return BaseStudioModuleRegistration.CreateFramework("graph", 1, authority.ApplicationId, "graph", "studio.module.graph",
            Asset(), Frontend(), pages, [], resources, [], links, [Client(pages.Select(static value => value.PageId))], [bootstrap],
            BaseStudioModuleLimits.Create(8, 1, 3, 0, 2, 1));
    }

    private static BaseStudioPageRegistration Page(PageSpec spec, BaseStudioGrantRequirement bootstrap, BaseStudioGrantRequirement inspect)
    {
        BaseStudioSectionRegistration[] sections = spec.Sections.Select((id, index) => BaseStudioSectionRegistration.Create(
            id, "studio.section." + id, index, index == 0 ? BaseStudioSectionKind.Summary : BaseStudioSectionKind.Configuration, [], [])).ToArray();
        BaseStudioResourceKind[] accepted = spec.Id switch
        {
            "graph.checkpoint.detail" => [BaseStudioResourceKind.GraphCheckpoint],
            "graph.definition.detail" or "graph.executions" or "graph.topology.detail" => [BaseStudioResourceKind.GraphDefinition],
            "graph.execution.detail" => [BaseStudioResourceKind.GraphExecution],
            _ => [],
        };
        return BaseStudioPageRegistration.Create(spec.Id, 1, spec.Area, "studio.page." + spec.Id, Route(spec), spec.Kind,
            BaseStudioPagePresentationRegistration.Create(spec.Id, 1, BaseStudioNavigationRole.Contextual,
                spec.Kind == BaseStudioPageKind.Timeline ? BaseStudioWorkspaceKind.Timeline : BaseStudioWorkspaceKind.Detail,
                sections, null, null, BaseStudioDraftRetentionClass.None), accepted, spec.Endpoints,
            accepted.Length == 0 ? [bootstrap] : [inspect], BaseStudioDisclosureClass.ProtectedValue);
    }

    private static BaseStudioResourceRegistration Resource(BaseStudioResourceKind kind, string resolver, string endpoint,
        BaseStudioGrantRequirement discover, BaseStudioGrantRequirement inspect)
        => BaseStudioResourceRegistration.Create(kind, resolver, [endpoint], [discover, inspect], BaseStudioDisclosureClass.ProtectedValue);

    private static BaseStudioFrameworkClientRegistration Client(IEnumerable<string> pages) => BaseStudioFrameworkClientRegistration.Create(
        ClientId, 1, BaseStudioContractNecessity.Required, BaseStudioFrameworkClientProtocol.FrameworkGeneratedContractV1,
        RuntimeAbi, ClientContract, OperationInventory, "graph.control-plane.v1",
        BaseStudioFrameworkClientTransportClass.SameOriginShellAuthenticated, pages, ClientLimits());

    private static BaseStudioFrontendExport Frontend() => BaseStudioFrontendExport.Create("graph", 1,
        [BaseStudioFrontendClientSlot.Create(ClientId, 1, BaseStudioFrameworkClientProtocol.FrameworkGeneratedContractV1,
            RuntimeAbi, ClientContract, OperationInventory, "graph.control-plane.v1",
            BaseStudioFrameworkClientTransportClass.SameOriginShellAuthenticated, Pages.Select(static value => value.Id).Order(StringComparer.Ordinal), ClientLimits())],
        Pages.Select(static value => BaseStudioPageComponentBinding.Create(value.Id, "component." + value.Id, ComponentAbi)).OrderBy(static value => value.PageId, StringComparer.Ordinal));

    private static BaseStudioFrameworkClientLimits ClientLimits() => BaseStudioFrameworkClientLimits.Create(
        6, 1_048_576, 8_388_608, 4, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
    private static BaseStudioRouteTemplate Route(PageSpec spec) => BaseStudioRouteTemplate.Create(spec.Id + ".route", spec.Route.Split('/').Select(static value =>
        value == ":resource" ? BaseStudioRouteSegment.Parameter("resource", BaseStudioRouteCodec.StudioResourceIdentity) : BaseStudioRouteSegment.Literal(value)));
    private static BaseStudioGrantRequirement Grant(HPDBaseStudioAuthoritySnapshot authority, string id)
    {
        HPDBaseStudioGrantAuthority registration = authority.Grants.SingleOrDefault(value => value.Id == id && value.Version == 1)
            ?? throw new InvalidOperationException("graph.studio.requiredGrantMissing");
        AccessGrant grant = registration.GetStaticGrant() ?? throw new InvalidOperationException("graph.studio.requiredGrantDynamic");
        if (grant.Audience != HPDBaseEndpointAudience.ControlPlane || grant.Effect != GrantEffect.Allow || grant.Action != id ||
            grant.ApplicationId != authority.ApplicationId || grant.ModuleId != "base") throw new InvalidOperationException("graph.studio.requiredGrantInvalid");
        return BaseStudioGrantRequirement.Create(id, 1, BaseStudioSha256.FromDigest(registration.GetChecksum()), id, "control-plane",
            grant.Subject.Kind.ToString().ToLowerInvariant(), authority.ApplicationId, "base", null, BaseStudioProtectedScopeRule.Application, true);
    }
    private static BaseStudioAssetManifest Asset()
    {
        const string path = "graph/ad53192c6b4e30b967b69c210a14edef1bd80422f4ac5b0356ba808713e8a44a.js";
        using Stream stream = typeof(GraphStudioModuleRegistry).Assembly.GetManifestResourceStream("HPD.Graph.Studio.Assets.graph.js") ?? throw new InvalidOperationException("graph.studio.assetMissing");
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        return BaseStudioAssetManifest.Create(path, BaseStudioModuleNecessity.Required, BaseStudioShellContract.Current,
            [BaseStudioAssetSource.Create(path, BaseStudioAssetMediaType.JavaScriptModule, bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length)))]);
    }
    private static BaseStudioSha256 Digest(string value) => BaseStudioSha256.FromDigest(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record PageSpec(string Id, BaseStudioArea Area, string Route, BaseStudioPageKind Kind, string[] Sections, string[] Endpoints);
}
