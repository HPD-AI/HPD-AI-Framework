using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using HPD.AI.Platform.Studio;
using HPD.Base;

namespace HPD.Agent.Studio;

/// <summary>Owns Agent Studio's immutable page, resource, and generated-client graph.</summary>
public static class AgentStudioModuleRegistry
{
    private const string ClientId = "agent.control-plane";
    private const string CreateAgentCommand = AgentStudioCreationContracts.CreateAgent;
    private const string ConfigurationUpdateCommand = "agent.configuration.update";
    private const string ConfigurationInputDescriptor =
        "{\"kind\":\"object\",\"properties\":[{\"name\":\"configJson\",\"wireName\":\"configJson\",\"typeId\":\"agent.studio.configuration-json\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}";
    private const string ConfigurationResultDescriptor =
        "{\"kind\":\"object\",\"properties\":[{\"name\":\"acknowledgements\",\"wireName\":\"acknowledgements\",\"typeId\":\"agent.studio.command-acknowledgements.one\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"changeCount\",\"wireName\":\"changeCount\",\"typeId\":\"agent.studio.configuration-change-count\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"diff\",\"wireName\":\"diff\",\"typeId\":\"agent.studio.configuration-diffs\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"expiresAtUtc\",\"wireName\":\"expiresAtUtc\",\"typeId\":\"agent.studio.optional-text\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"},{\"name\":\"mode\",\"wireName\":\"mode\",\"typeId\":\"agent.studio.configuration-command-mode\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"previewChecksum\",\"wireName\":\"previewChecksum\",\"typeId\":\"agent.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"receiptChecksum\",\"wireName\":\"receiptChecksum\",\"typeId\":\"agent.studio.optional-sha256\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"},{\"name\":\"updatedAtUtc\",\"wireName\":\"updatedAtUtc\",\"typeId\":\"agent.studio.optional-text\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}";
    private static readonly StudioNamedTypeContract ConfigurationInput =
        StudioNamedTypeContract.Create("agent.configuration.update.input", Encoding.UTF8.GetBytes(ConfigurationInputDescriptor));
    private static readonly StudioNamedTypeContract ConfigurationResult =
        StudioNamedTypeContract.Create("agent.configuration.update.result", Encoding.UTF8.GetBytes(ConfigurationResultDescriptor));
    private static readonly StudioSha256 RuntimeAbi = Digest("agent.studio.generated-client-runtime-abi.v1");
    private static readonly StudioSha256 ClientContract = Digest("agent.studio.generated-control-plane-contract.v1");
    private static readonly StudioSha256 ComponentAbi = StudioSha256.FromDigest(
        Convert.FromHexString("9b256fa00bf2b97c3d4ee8c50a89cb476c6ff3de30c8181eb001df867c1b09dd"));
    internal static readonly StudioSha256 OperationInventory =
        StudioFrameworkSurfaceOperation.ComputeInventoryChecksum("agent.control-plane.v1", AgentStudioEndpointSurface.CreateOperations());

    private static readonly ImmutableArray<PageSpec> Pages =
    [
        new("agent.catalog", "operations/agents", StudioPageKind.Overview, ["catalog", "attention"], ["agent.list"]),
        new("agent.creation", "operations/agent-creation", StudioPageKind.Action,
            ["identity", "model", "runtime", "capabilities", "review"], []),
        new("agent.overview", "operations/agents/:resource", StudioPageKind.ResourceDetail, ["summary", "capabilities", "activity"], ["agent.get"]),
        new("agent.workspace", "operations/agents/:resource/chat", StudioPageKind.Action, ["threads", "chat", "execution"], ["agent.get"]),
        new("agent.configuration", "operations/agents/:resource/configuration", StudioPageKind.ResourceDetail, ["definition", "layers", "validation"], ["agent.get"]),
        new("harness.catalog", "operations/harnesses", StudioPageKind.ResourceList, ["harnesses"], ["harness.list"]),
        new("harness.detail", "operations/harnesses/:resource", StudioPageKind.ResourceDetail, ["contents", "configuration", "dependencies"], ["harness.get"]),
        new("aiFunction.detail", "operations/agent-functions/:resource", StudioPageKind.ResourceDetail, ["summary", "schema", "policy", "activity"], ["function.get"]),
        new("aiFunction.action.detail", "operations/agent-actions/:resource", StudioPageKind.ResourceDetail, ["summary", "schema", "policy", "usage"], ["function.action.get"]),
        new("session.catalog", "operations/sessions", StudioPageKind.Collection, ["sessions"], ["session.list"]),
        new("session.detail", "operations/sessions/:resource", StudioPageKind.ResourceDetail, ["summary", "threads", "executions"], ["session.get", "thread.list"]),
        new("thread.detail", "operations/agent-threads/:resource", StudioPageKind.ResourceDetail, ["summary", "messages", "executions"], ["thread.get", "execution.list"]),
        new("execution.detail", "operations/agent-executions/:resource", StudioPageKind.Timeline, ["summary", "timeline", "evidence"], ["execution.get"]),
        new("observability.overview", "operations/observability", StudioPageKind.QueryTool, ["overview", "attention"], ["observability.summary"]),
        new("observability.metrics", "operations/observability/metrics", StudioPageKind.QueryTool, ["measures", "breakdowns", "capabilities"], ["metrics.query"]),
        new("observability.events", "operations/observability/events", StudioPageKind.QueryTool, ["events", "filters", "capabilities"], ["event.get", "events.list"]),
        new("trace.catalog", "operations/observability/traces", StudioPageKind.Collection, ["traces", "attention"], ["trace.list"]),
        new("trace.detail", "operations/observability/traces/:resource", StudioPageKind.Timeline, ["summary", "messages", "spans", "payload"], ["trace.get"]),
    ];

    /// <summary>Creates the authorization-neutral trusted edition asset.</summary>
    public static StudioEditionModuleAssetContribution CreateEditionAssetContribution()
    {
        StudioFrontendExport frontend = Frontend();
        return StudioEditionModuleAssetContribution.Create("agents", 1, frontend.FrontendAbiChecksum, Asset());
    }

    /// <summary>Creates Agent Studio against finalized BASE application authority.</summary>
    public static StudioModuleRegistration Create(HPDBaseStudioAuthoritySnapshot authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        StudioGrantRequirement bootstrap = Grant(authority, "base.studio.bootstrap.read");
        StudioGrantRequirement discover = Grant(authority, "base.studio.resource.discover");
        StudioGrantRequirement inspect = Grant(authority, "base.studio.resource.inspect");
        StudioGrantRequirement actionDiscover = Grant(authority, "base.studio.action.discover", false);
        StudioGrantRequirement actionPreview = Grant(authority, "base.studio.action.preview");
        StudioGrantRequirement actionExecute = Grant(authority, "base.studio.action.execute");
        StudioPageRegistration[] pages = Pages.Select(spec => Page(spec, bootstrap, inspect))
            .OrderBy(static value => value.PageId, StringComparer.Ordinal).ToArray();
        StudioCommandRegistration configurationUpdate = StudioCommandRegistration.Create(
            ConfigurationUpdateCommand, 1, ConfigurationUpdateCommand,
            ConfigurationInput.TypeId, ConfigurationInput.NodeChecksum,
            ConfigurationResult.TypeId, ConfigurationResult.NodeChecksum,
            StudioActionClass.OperationalTransition,
            [actionDiscover, actionExecute, actionPreview, inspect],
            1_048_576, 1_048_576, null,
            [
                StudioCommandAcknowledgementRequirement.Create(
                    "confirm.agent.configuration.update", "impact.agent.configuration.update"),
            ]);
        StudioCommandRegistration createAgent = StudioCommandRegistration.Create(
            CreateAgentCommand, 1, CreateAgentCommand,
            AgentStudioCreationContracts.Input.TypeId, AgentStudioCreationContracts.Input.NodeChecksum,
            AgentStudioCreationContracts.Result.TypeId, AgentStudioCreationContracts.Result.NodeChecksum,
            StudioActionClass.OperationalTransition,
            [actionDiscover, actionExecute, actionPreview, inspect],
            1_048_576, 1_048_576, null,
            [
                StudioCommandAcknowledgementRequirement.Create(
                    AgentStudioCreationContracts.ConfirmationId, AgentStudioCreationContracts.ImpactId),
            ]);
        StudioCommandRegistration[] experimentCommands =
        [
            ExperimentCommand(AgentStudioExperimentContracts.CreateSession, StudioActionClass.Routine,
                actionDiscover, actionExecute, actionPreview, inspect),
            ExperimentCommand(AgentStudioExperimentContracts.CreateThread, StudioActionClass.OperationalTransition,
                actionDiscover, actionExecute, actionPreview, inspect),
            ExperimentCommand(AgentStudioExperimentContracts.ForkThread, StudioActionClass.OperationalTransition,
                actionDiscover, actionExecute, actionPreview, inspect),
            ExperimentCommand(AgentStudioExperimentContracts.SteerInput, StudioActionClass.OperationalTransition,
                actionDiscover, actionExecute, actionPreview, inspect),
            ExperimentCommand(AgentStudioExperimentContracts.PermissionRespond, StudioActionClass.Routine,
                actionDiscover, actionExecute, actionPreview, inspect),
            ExperimentCommand(AgentStudioExperimentContracts.QuestionRespond, StudioActionClass.Routine,
                actionDiscover, actionExecute, actionPreview, inspect),
            ExperimentCommand(AgentStudioExperimentContracts.ContinuationRespond, StudioActionClass.Routine,
                actionDiscover, actionExecute, actionPreview, inspect),
            ExperimentCommand(AgentStudioExperimentContracts.ClientToolRespond, StudioActionClass.Routine,
                actionDiscover, actionExecute, actionPreview, inspect),
            ExperimentCommand(AgentStudioExperimentContracts.SubmitInput, StudioActionClass.Routine,
                actionDiscover, actionExecute, actionPreview, inspect),
            ExperimentCommand(AgentStudioExperimentContracts.CancelExecution, StudioActionClass.OperationalTransition,
                actionDiscover, actionExecute, actionPreview, inspect),
        ];
        StudioCommandRegistration[] lifecycleCommands =
        [
            LifecycleCommand(AgentStudioExperimentContracts.DeleteSession, StudioActionClass.Destructive,
                actionDiscover, actionExecute, actionPreview, inspect, "confirm.session.delete", "impact.session.delete"),
            LifecycleCommand(AgentStudioExperimentContracts.DeleteThread, StudioActionClass.Destructive,
                actionDiscover, actionExecute, actionPreview, inspect, "confirm.thread.delete", "impact.thread.delete"),
        ];
        StudioCommandRegistration[] commands = new[] { configurationUpdate, createAgent }.Concat(experimentCommands)
            .Concat(lifecycleCommands)
            .OrderBy(static value => value.CommandId, StringComparer.Ordinal)
            .ToArray();
        StudioResourceRegistration[] resources =
        [
            Resource(StudioResourceKind.Application, "agent.studio.resolve.application",
                "agent.studio.resource.application", discover, inspect),
            Resource(StudioResourceKind.Agent, "agent.studio.resolve.agent", "agent.studio.resource.agent", discover, inspect),
            Resource(StudioResourceKind.AgentSession, "agent.studio.resolve.session", "agent.studio.resource.session", discover, inspect),
            Resource(StudioResourceKind.AgentThread, "agent.studio.resolve.thread", "agent.studio.resource.thread", discover, inspect),
            Resource(StudioResourceKind.AgentExecution, "agent.studio.resolve.execution", "agent.studio.resource.execution", discover, inspect),
            Resource(StudioResourceKind.ToolHarness, "agent.studio.resolve.harness", "agent.studio.resource.harness", discover, inspect),
            Resource(StudioResourceKind.AgentTrace, "agent.studio.resolve.trace", "agent.studio.resource.trace", discover, inspect),
            Resource(StudioResourceKind.AiFunction, "agent.studio.resolve.function", "agent.studio.resource.function", discover, inspect),
            Resource(StudioResourceKind.AiFunctionAction, "agent.studio.resolve.action", "agent.studio.resource.action", discover, inspect),
        ];
        StudioLinkRegistration[] links =
        [
            StudioLinkRegistration.Create(StudioResourceKind.Agent, StudioResourceKind.AgentSession, StudioLinkRelation.Owns, "agent.studio.link.agent.sessions"),
            StudioLinkRegistration.Create(StudioResourceKind.Agent, StudioResourceKind.ToolHarness, StudioLinkRelation.Owns, "agent.studio.link.agent.harnesses"),
            StudioLinkRegistration.Create(StudioResourceKind.ToolHarness, StudioResourceKind.AiFunction, StudioLinkRelation.Owns, "agent.studio.link.harness.functions"),
            StudioLinkRegistration.Create(StudioResourceKind.AiFunction, StudioResourceKind.AiFunctionAction, StudioLinkRelation.Owns, "agent.studio.link.function.actions"),
            StudioLinkRegistration.Create(StudioResourceKind.AgentSession, StudioResourceKind.AgentThread, StudioLinkRelation.Owns, "agent.studio.link.session.threads"),
            StudioLinkRegistration.Create(StudioResourceKind.AgentThread, StudioResourceKind.AgentExecution, StudioLinkRelation.Owns, "agent.studio.link.thread.executions"),
            StudioLinkRegistration.Create(StudioResourceKind.AgentTrace, StudioResourceKind.AgentThread, StudioLinkRelation.ProducedBy, "agent.studio.link.trace.thread"),
        ];
        links = [.. links
            .OrderBy(static value => (byte)value.SourceKind)
            .ThenBy(static value => (byte)value.Relation)
            .ThenBy(static value => (byte)value.TargetKind)
            .ThenBy(static value => value.ResolverId, StringComparer.Ordinal)];
        return StudioModuleRegistration.CreateFramework("agents", 1, authority.ApplicationId, "agent", "studio.module.agents",
            Asset(), Frontend(), pages, [], resources, commands, links,
            [Client(pages.Select(static value => value.PageId))],
            [bootstrap], StudioModuleLimits.Create(19, 1, 32, 14, 16, 2));
    }

    private static StudioPageRegistration Page(PageSpec spec, StudioGrantRequirement bootstrap, StudioGrantRequirement inspect)
    {
        StudioSectionRegistration[] sections = spec.Sections.Select((id, index) =>
        {
            string[] commandIds = spec.Id switch
            {
                "agent.creation" when index == spec.Sections.Length - 1 => [CreateAgentCommand],
                "agent.configuration" when index == spec.Sections.Length - 1 => [ConfigurationUpdateCommand],
                "agent.workspace" when index == 1 =>
                [
                    AgentStudioExperimentContracts.ClientToolRespond,
                    AgentStudioExperimentContracts.ContinuationRespond,
                    AgentStudioExperimentContracts.CancelExecution,
                    AgentStudioExperimentContracts.SteerInput,
                    AgentStudioExperimentContracts.SubmitInput,
                    AgentStudioExperimentContracts.PermissionRespond,
                    AgentStudioExperimentContracts.QuestionRespond,
                    AgentStudioExperimentContracts.CreateSession,
                    AgentStudioExperimentContracts.CreateThread,
                    AgentStudioExperimentContracts.ForkThread,
                ],
                "session.detail" when index == spec.Sections.Length - 1 =>
                    [AgentStudioExperimentContracts.DeleteSession],
                "thread.detail" when index == spec.Sections.Length - 1 =>
                    [AgentStudioExperimentContracts.DeleteThread],
                _ => [],
            };
            return StudioSectionRegistration.Create(
                id, "studio.section." + id, index,
                index == 0
                    ? StudioSectionKind.Summary
                    : spec.Kind is StudioPageKind.Timeline
                        ? StudioSectionKind.History
                        : StudioSectionKind.Configuration,
                [], commandIds);
        }).ToArray();
        StudioResourceKind[] accepted = spec.Id switch
        {
            "agent.creation" => [StudioResourceKind.Application],
            "agent.overview" or "agent.workspace" or "agent.configuration" => [StudioResourceKind.Agent],
            "harness.detail" => [StudioResourceKind.ToolHarness],
            "aiFunction.detail" => [StudioResourceKind.AiFunction],
            "aiFunction.action.detail" => [StudioResourceKind.AiFunctionAction],
            "session.detail" => [StudioResourceKind.AgentSession],
            "thread.detail" => [StudioResourceKind.AgentThread],
            "execution.detail" => [StudioResourceKind.AgentExecution],
            "trace.detail" => [StudioResourceKind.AgentTrace],
            _ => [],
        };
        StudioPagePresentationRegistration presentation = StudioPagePresentationRegistration.Create(
            spec.Id, 1, StudioNavigationRole.Contextual,
            spec.Kind switch
            {
                StudioPageKind.Timeline => StudioWorkspaceKind.Timeline,
                StudioPageKind.QueryTool => StudioWorkspaceKind.QueryTool,
                _ => StudioWorkspaceKind.Detail,
            }, sections, null, null,
            spec.Id == "agent.configuration" ? StudioDraftRetentionClass.CurrentDocumentNavigation : StudioDraftRetentionClass.None);
        return StudioPageRegistration.Create(spec.Id, 1, StudioArea.Operations, "studio.page." + spec.Id, Route(spec), spec.Kind,
            presentation, accepted, spec.Endpoints.Order(StringComparer.Ordinal),
            accepted.Length == 0 ? [bootstrap] : [inspect], StudioDisclosureClass.ProtectedValue);
    }

    private static StudioResourceRegistration Resource(StudioResourceKind kind, string resolver, string endpoint,
        StudioGrantRequirement discover, StudioGrantRequirement inspect) =>
        StudioResourceRegistration.Create(kind, resolver, [endpoint], [discover, inspect], StudioDisclosureClass.ProtectedValue);

    private static StudioCommandRegistration ExperimentCommand(string commandId, StudioActionClass actionClass,
        StudioGrantRequirement discover, StudioGrantRequirement execute, StudioGrantRequirement preview,
        StudioGrantRequirement inspect)
    {
        StudioNamedTypeContract input = AgentStudioExperimentContracts.Input(commandId);
        StudioNamedTypeContract result = AgentStudioExperimentContracts.Result(commandId);
        return StudioCommandRegistration.Create(commandId, 1, commandId,
            input.TypeId, input.NodeChecksum, result.TypeId, result.NodeChecksum,
            actionClass, [discover, execute, preview, inspect],
            65_536, 1_048_576, null, []);
    }

    private static StudioCommandRegistration LifecycleCommand(string commandId, StudioActionClass actionClass,
        StudioGrantRequirement discover, StudioGrantRequirement execute, StudioGrantRequirement preview,
        StudioGrantRequirement inspect, string purposeId, string impactId)
    {
        StudioNamedTypeContract input = AgentStudioExperimentContracts.Input(commandId);
        StudioNamedTypeContract result = AgentStudioExperimentContracts.Result(commandId);
        return StudioCommandRegistration.Create(commandId, 1, commandId,
            input.TypeId, input.NodeChecksum, result.TypeId, result.NodeChecksum,
            actionClass, [discover, execute, preview, inspect],
            65_536, 1_048_576, StudioFreshAuthenticationClass.MultiFactor,
            [StudioCommandAcknowledgementRequirement.Create(purposeId, impactId)]);
    }

    private static StudioFrameworkClientRegistration Client(IEnumerable<string> pages) =>
        StudioFrameworkClientRegistration.Create(ClientId, 1, StudioContractNecessity.Required,
            StudioFrameworkClientProtocol.FrameworkGeneratedContractV1, RuntimeAbi, ClientContract, OperationInventory,
            "agent.control-plane.v1", StudioFrameworkClientTransportClass.SameOriginShellAuthenticated,
            pages, StudioFrameworkClientLimits.Create(32, 1_048_576, 8_388_608, 8,
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));

    private static StudioFrontendExport Frontend() => StudioFrontendExport.Create("agents", 1,
        [StudioFrontendClientSlot.Create(ClientId, 1, StudioFrameworkClientProtocol.FrameworkGeneratedContractV1,
            RuntimeAbi, ClientContract, OperationInventory, "agent.control-plane.v1",
            StudioFrameworkClientTransportClass.SameOriginShellAuthenticated,
            Pages.Select(static value => value.Id).Order(StringComparer.Ordinal), ClientLimits())],
        Pages.Select(static value => StudioPageComponentBinding.Create(value.Id, "component." + value.Id, ComponentAbi))
            .OrderBy(static value => value.PageId, StringComparer.Ordinal));

    private static StudioFrameworkClientLimits ClientLimits() => StudioFrameworkClientLimits.Create(
        32, 1_048_576, 8_388_608, 8, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

    private static StudioRouteTemplate Route(PageSpec spec) => StudioRouteTemplate.Create(
        spec.Id + ".route", spec.Route.Split('/').Select(static value => value.StartsWith(':')
            ? StudioRouteSegment.Parameter(value[1..], value == ":resource"
                ? StudioRouteCodec.StudioResourceIdentity
                : StudioRouteCodec.Identifier)
            : StudioRouteSegment.Literal(value)),
        RouteQuery(spec.Id));

    private static IEnumerable<StudioQueryParameter> RouteQuery(string pageId) => pageId switch
    {
        "trace.catalog" =>
            [
                StudioQueryParameter.Create("after", StudioRouteCodec.PositiveLong, false),
                StudioQueryParameter.Create("agentId", StudioRouteCodec.Identifier, false),
                StudioQueryParameter.Create("before", StudioRouteCodec.PositiveLong, false),
                StudioQueryParameter.Create("limit", StudioRouteCodec.PositiveLong, false),
                StudioQueryParameter.Create("offset", StudioRouteCodec.NonnegativeLong, false),
                StudioQueryParameter.Create("sessionId", StudioRouteCodec.Identifier, false),
                StudioQueryParameter.Create("state", StudioRouteCodec.RegisteredEnum, false,
                    ["cancelled", "completed", "failed", "running"]),
                StudioQueryParameter.Create("threadId", StudioRouteCodec.Identifier, false),
            ],
        "observability.events" =>
            [
                StudioQueryParameter.Create("after", StudioRouteCodec.PositiveLong, false),
                StudioQueryParameter.Create("agentId", StudioRouteCodec.Identifier, false),
                StudioQueryParameter.Create("before", StudioRouteCodec.PositiveLong, false),
                StudioQueryParameter.Create("errors", StudioRouteCodec.RegisteredEnum, false, ["true"]),
                StudioQueryParameter.Create("limit", StudioRouteCodec.PositiveLong, false),
                StudioQueryParameter.Create("offset", StudioRouteCodec.NonnegativeLong, false),
                StudioQueryParameter.Create("sessionId", StudioRouteCodec.Identifier, false),
                StudioQueryParameter.Create("spanId", StudioRouteCodec.Identifier, false),
                StudioQueryParameter.Create("threadId", StudioRouteCodec.Identifier, false),
                StudioQueryParameter.Create("traceId", StudioRouteCodec.Identifier, false),
                StudioQueryParameter.Create("type", StudioRouteCodec.Identifier, false),
            ],
        "observability.metrics" =>
            [
                StudioQueryParameter.Create("after", StudioRouteCodec.PositiveLong, false),
                StudioQueryParameter.Create("agentId", StudioRouteCodec.Identifier, false),
                StudioQueryParameter.Create("before", StudioRouteCodec.PositiveLong, false),
                StudioQueryParameter.Create("sessionId", StudioRouteCodec.Identifier, false),
                StudioQueryParameter.Create("state", StudioRouteCodec.RegisteredEnum, false,
                    ["cancelled", "completed", "failed", "running"]),
                StudioQueryParameter.Create("threadId", StudioRouteCodec.Identifier, false),
            ],
        _ => []
    };

    private static StudioGrantRequirement Grant(
        HPDBaseStudioAuthoritySnapshot authority, string id, bool requiresUnderlying = true)
    {
        HPDBaseStudioGrantAuthority registration = authority.Grants.SingleOrDefault(value => value.Id == id && value.Version == 1)
            ?? throw new InvalidOperationException("agent.studio.requiredGrantMissing");
        AccessGrant grant = registration.GetStaticGrant() ?? throw new InvalidOperationException("agent.studio.requiredGrantDynamic");
        if (grant.Audience != HPDBaseEndpointAudience.ControlPlane || grant.Effect != GrantEffect.Allow || grant.Action != id ||
            grant.ApplicationId != authority.ApplicationId || grant.ModuleId != "base")
            throw new InvalidOperationException("agent.studio.requiredGrantInvalid");
        return StudioGrantRequirement.Create(id, 1, StudioSha256.FromDigest(registration.GetChecksum()), id,
            "control-plane", grant.Subject.Kind.ToString().ToLowerInvariant(), authority.ApplicationId, "base", null,
            StudioProtectedScopeRule.Application, requiresUnderlying);
    }

    internal static StudioNamedTypeContract ConfigurationUpdateInputType =>
        StudioNamedTypeContract.Create(ConfigurationInput.TypeId,
            ConfigurationInput.GetCanonicalDescriptor());

    internal static StudioNamedTypeContract ConfigurationUpdateResultType =>
        StudioNamedTypeContract.Create(ConfigurationResult.TypeId,
            ConfigurationResult.GetCanonicalDescriptor());

    private static StudioAssetManifest Asset()
    {
        const string path = "agent/db403d8fb72955ac2f5b58787ad4c85ae238708c7cfafc95200a610e110741cc.js";
        using Stream stream = typeof(AgentStudioModuleRegistry).Assembly
            .GetManifestResourceStream("HPD.Agent.Studio.Assets.agent.js")
            ?? throw new InvalidOperationException("agent.studio.assetMissing");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return StudioAssetManifest.Create(path, StudioModuleNecessity.Required, StudioShellContract.Current,
            [StudioAssetSource.Create(path, StudioAssetMediaType.JavaScriptModule,
                bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length)))]);
    }

    private static StudioSha256 Digest(string value) =>
        StudioSha256.FromDigest(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record PageSpec(string Id, string Route, StudioPageKind Kind, string[] Sections, string[] Endpoints);
}
