using System.Collections.Immutable;
using System.Text;
using HPD.AI.Platform.Studio;
using HPD.Base;

namespace HPD.Graph.Studio;

/// <summary>Owns HPD Graph Studio's immutable version-one presentation and generated-client graph.</summary>
public static class GraphStudioModuleRegistry
{
    private const string ClientId = "graph.control-plane";
    private static readonly StudioSha256 RuntimeAbi =
        StudioSha256.FromDigest(Convert.FromHexString("75e580b84bd3267ad7177ace62def3be7c611a9df94f6bab2f95d69e729d8304"));
    private static readonly StudioSha256 ClientContract =
        StudioSha256.FromDigest(Convert.FromHexString("20dcd5dd905fb5f1f00d9ff04e9d18406903badcfcb43fcea24ab090cfb2a792"));
    private static readonly StudioSha256 ComponentAbi =
        StudioSha256.FromDigest(Convert.FromHexString("0e356515536da0406864966c232dd2c25c3e48fd5eb779cb0f969eb78432919d"));
    internal static readonly StudioSha256 OperationInventory = StudioFrameworkSurfaceOperation.ComputeInventoryChecksum(
        "graph.control-plane.v1", GraphStudioEndpointSurface.CreateOperations());

    private static readonly ImmutableArray<PageSpec> Pages =
    [
        new("graph.checkpoint.detail", StudioArea.Operations, "operations/graph/checkpoints/:resource", StudioPageKind.Timeline, ["summary", "state", "lineage", "evidence"], ["graph.checkpoint.get", "graph.execution.get"]),
        new("graph.definition.detail", StudioArea.Operations, "operations/graph/definitions/:resource", StudioPageKind.ResourceDetail, ["summary", "nodes", "channels", "executions", "baseWork"], ["graph.definition.get", "graph.execution.list"]),
        new("graph.execution.detail", StudioArea.Operations, "operations/graph/executions/:resource", StudioPageKind.Timeline, ["summary", "nodes", "channels", "checkpoints", "baseWork", "timeline"], ["graph.execution.get", "graph.execution.suspendedNodes"]),
        new("graph.executions", StudioArea.Operations, "operations/graph/definitions/:resource/executions", StudioPageKind.ResourceList, ["executions"], ["graph.execution.list"]),
        new("graph.overview", StudioArea.Operations, "operations/graph", StudioPageKind.Overview, ["definitions", "executions", "attention"], ["graph.definition.list"]),
        new("graph.topology.detail", StudioArea.Operations, "operations/graph/topology/:resource", StudioPageKind.ResourceDetail, ["nodes", "channels", "dependencies"], ["graph.definition.get"]),
    ];

    /// <summary>Creates the authorization-neutral trusted edition asset.</summary>
    public static StudioEditionModuleAssetContribution CreateEditionAssetContribution()
    { StudioFrontendExport frontend = Frontend(); return StudioEditionModuleAssetContribution.Create("graph", 1, frontend.FrontendAbiChecksum, Asset()); }

    /// <summary>Creates Graph's semantic registration against finalized BASE application authority.</summary>
    public static StudioModuleRegistration Create(HPDBaseStudioAuthoritySnapshot authority)
    {
        ArgumentNullException.ThrowIfNull(authority); StudioGrantRequirement bootstrap = Grant(authority, "base.studio.bootstrap.read");
        StudioGrantRequirement discover = Grant(authority, "base.studio.resource.discover");
        StudioGrantRequirement inspect = Grant(authority, "base.studio.resource.inspect");
        StudioPageRegistration[] pages = Pages.Select(spec => Page(spec, bootstrap, inspect)).OrderBy(static value => value.PageId, StringComparer.Ordinal).ToArray();
        StudioResourceRegistration[] resources =
        [
            Resource(StudioResourceKind.GraphDefinition, "graph.studio.resolve.definition", "graph.studio.resource.definition", discover, inspect),
            Resource(StudioResourceKind.GraphExecution, "graph.studio.resolve.execution", "graph.studio.resource.execution", discover, inspect),
            Resource(StudioResourceKind.GraphCheckpoint, "graph.studio.resolve.checkpoint", "graph.studio.resource.checkpoint", discover, inspect),
        ];
        StudioLinkRegistration[] links =
        [
            StudioLinkRegistration.Create(StudioResourceKind.GraphDefinition, StudioResourceKind.Schedule, StudioLinkRelation.ScheduledBy, "graph.studio.link.definition.schedule"),
            StudioLinkRegistration.Create(StudioResourceKind.GraphExecution, StudioResourceKind.Activation, StudioLinkRelation.ProducedBy, "graph.studio.link.execution.activation"),
        ];
        return StudioModuleRegistration.CreateFramework("graph", 1, authority.ApplicationId, "graph", "studio.module.graph",
            Asset(), Frontend(), pages, [], resources, [], links, [Client(pages.Select(static value => value.PageId))], [bootstrap],
            StudioModuleLimits.Create(8, 1, 3, 0, 2, 1));
    }

    private static StudioPageRegistration Page(PageSpec spec, StudioGrantRequirement bootstrap, StudioGrantRequirement inspect)
    {
        StudioSectionRegistration[] sections = spec.Sections.Select((id, index) => StudioSectionRegistration.Create(
            id, "studio.section." + id, index, index == 0 ? StudioSectionKind.Summary : StudioSectionKind.Configuration, [], [])).ToArray();
        StudioResourceKind[] accepted = spec.Id switch
        {
            "graph.checkpoint.detail" => [StudioResourceKind.GraphCheckpoint],
            "graph.definition.detail" or "graph.executions" or "graph.topology.detail" => [StudioResourceKind.GraphDefinition],
            "graph.execution.detail" => [StudioResourceKind.GraphExecution],
            _ => [],
        };
        return StudioPageRegistration.Create(spec.Id, 1, spec.Area, "studio.page." + spec.Id, Route(spec), spec.Kind,
            StudioPagePresentationRegistration.Create(spec.Id, 1, StudioNavigationRole.Contextual,
                spec.Kind == StudioPageKind.Timeline ? StudioWorkspaceKind.Timeline : StudioWorkspaceKind.Detail,
                sections, null, null, StudioDraftRetentionClass.None), accepted, spec.Endpoints,
            accepted.Length == 0 ? [bootstrap] : [inspect], StudioDisclosureClass.ProtectedValue);
    }

    private static StudioResourceRegistration Resource(StudioResourceKind kind, string resolver, string endpoint,
        StudioGrantRequirement discover, StudioGrantRequirement inspect)
        => StudioResourceRegistration.Create(kind, resolver, [endpoint], [discover, inspect], StudioDisclosureClass.ProtectedValue);

    private static StudioFrameworkClientRegistration Client(IEnumerable<string> pages) => StudioFrameworkClientRegistration.Create(
        ClientId, 1, StudioContractNecessity.Required, StudioFrameworkClientProtocol.FrameworkGeneratedContractV1,
        RuntimeAbi, ClientContract, OperationInventory, "graph.control-plane.v1",
        StudioFrameworkClientTransportClass.SameOriginShellAuthenticated, pages, ClientLimits());

    private static StudioFrontendExport Frontend() => StudioFrontendExport.Create("graph", 1,
        [StudioFrontendClientSlot.Create(ClientId, 1, StudioFrameworkClientProtocol.FrameworkGeneratedContractV1,
            RuntimeAbi, ClientContract, OperationInventory, "graph.control-plane.v1",
            StudioFrameworkClientTransportClass.SameOriginShellAuthenticated, Pages.Select(static value => value.Id).Order(StringComparer.Ordinal), ClientLimits())],
        Pages.Select(static value => StudioPageComponentBinding.Create(value.Id, "component." + value.Id, ComponentAbi)).OrderBy(static value => value.PageId, StringComparer.Ordinal));

    private static StudioFrameworkClientLimits ClientLimits() => StudioFrameworkClientLimits.Create(
        6, 1_048_576, 8_388_608, 4, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
    private static StudioRouteTemplate Route(PageSpec spec) => StudioRouteTemplate.Create(spec.Id + ".route", spec.Route.Split('/').Select(static value =>
        value == ":resource" ? StudioRouteSegment.Parameter("resource", StudioRouteCodec.StudioResourceIdentity) : StudioRouteSegment.Literal(value)));
    private static StudioGrantRequirement Grant(HPDBaseStudioAuthoritySnapshot authority, string id)
    {
        HPDBaseStudioGrantAuthority registration = authority.Grants.SingleOrDefault(value => value.Id == id && value.Version == 1)
            ?? throw new InvalidOperationException("graph.studio.requiredGrantMissing");
        AccessGrant grant = registration.GetStaticGrant() ?? throw new InvalidOperationException("graph.studio.requiredGrantDynamic");
        if (grant.Audience != HPDBaseEndpointAudience.ControlPlane || grant.Effect != GrantEffect.Allow || grant.Action != id ||
            grant.ApplicationId != authority.ApplicationId || grant.ModuleId != "base") throw new InvalidOperationException("graph.studio.requiredGrantInvalid");
        return StudioGrantRequirement.Create(id, 1, StudioSha256.FromDigest(registration.GetChecksum()), id, "control-plane",
            grant.Subject.Kind.ToString().ToLowerInvariant(), authority.ApplicationId, "base", null, StudioProtectedScopeRule.Application, true);
    }
    private static StudioAssetManifest Asset()
    {
        const string path = "graph/f2dc7b4b4b2f3d924cdaeee058d779df309ebc0f78668d07aa6994c229e81e3e.js";
        using Stream stream = typeof(GraphStudioModuleRegistry).Assembly.GetManifestResourceStream("HPD.Graph.Studio.Assets.graph.js") ?? throw new InvalidOperationException("graph.studio.assetMissing");
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        return StudioAssetManifest.Create(path, StudioModuleNecessity.Required, StudioShellContract.Current,
            [StudioAssetSource.Create(path, StudioAssetMediaType.JavaScriptModule, bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length)))]);
    }
    private static StudioSha256 Digest(string value) => StudioSha256.FromDigest(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record PageSpec(string Id, StudioArea Area, string Route, StudioPageKind Kind, string[] Sections, string[] Endpoints);
}
