using System.Collections.Immutable;
using System.Text.Json;
using HPD.AI.Platform.Studio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Graph.Studio;

/// <summary>Builds Graph's executable, resource-resolving Studio Runtime contribution.</summary>
public sealed class GraphStudioRuntimeContributionFactory(
    IServiceProvider services, IGraphStudioInspectionAuthority inspection,
    IEnumerable<IGraphStudioBaseWorkProjection> baseWork) : IStudioModuleRuntimeContributionFactory
{
    private const int MaximumBytes = 1_048_576;
    private readonly ImmutableArray<IGraphStudioBaseWorkProjection> _baseWork = [.. baseWork];

    /// <inheritdoc />
    public string ModuleId => "graph";

    /// <inheritdoc />
    public StudioModuleRuntimeContribution Create(StudioModuleRegistration module)
    {
        if (!StringComparer.Ordinal.Equals(module.Identity.ModuleId, ModuleId))
            throw new ArgumentException("The Graph Runtime factory cannot author another module.", nameof(module));
        if (_baseWork.Length > 1)
            throw new InvalidOperationException("Graph Studio admits at most one authoritative BASE work projection.");

        StudioNamedTypeContract text = Type("graph.studio.text", "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":512,\"format\":\"nfc-text\"}");
        StudioNamedTypeContract checksum = Type("graph.studio.sha256", "{\"kind\":\"string\",\"minLength\":64,\"maxLength\":64,\"format\":\"sha256\"}");
        StudioNamedTypeContract tokenRequest = Type("graph.studio.resource-token-request", Obj(P("resourceToken", text)));
        StudioNamedTypeContract emptyMap = Type("graph.studio.empty-map", "{\"kind\":\"object\",\"properties\":[],\"additionalProperties\":false}");
        StudioNamedTypeContract routeParameters = Type("graph.studio.route-parameters", Obj(P("resource", text)));
        StudioNamedTypeContract route = Type("graph.studio.resolved-route", Obj(P("pageId", text), P("parameters", routeParameters), P("query", emptyMap)));
        StudioNamedTypeContract resolvedKind = Type("graph.studio.resolved-kind", "{\"kind\":\"literal\",\"value\":\"resolved\"}");
        StudioNamedTypeContract definitionKind = Type("graph.studio.definition-kind", "{\"kind\":\"literal\",\"value\":\"graphDefinition\"}");
        StudioNamedTypeContract executionKind = Type("graph.studio.execution-kind", "{\"kind\":\"literal\",\"value\":\"graphExecution\"}");
        StudioNamedTypeContract checkpointKind = Type("graph.studio.checkpoint-kind", "{\"kind\":\"literal\",\"value\":\"graphCheckpoint\"}");
        StudioNamedTypeContract scheduleKind = Type("graph.studio.schedule-kind", "{\"kind\":\"literal\",\"value\":\"schedule\"}");
        StudioNamedTypeContract activationKind = Type("graph.studio.activation-kind", "{\"kind\":\"literal\",\"value\":\"activation\"}");
        StudioNamedTypeContract scheduledBy = Type("graph.studio.scheduled-by", "{\"kind\":\"literal\",\"value\":\"scheduledBy\"}");
        StudioNamedTypeContract producedBy = Type("graph.studio.produced-by", "{\"kind\":\"literal\",\"value\":\"producedBy\"}");
        StudioNamedTypeContract positiveInt = PositiveInt();
        StudioNamedTypeContract definition = Type("graph.studio.resource.definition", Obj(P("applicationId", text), P("authorityChecksum", checksum), P("graphId", text), P("graphVersion", text), P("kind", definitionKind)));
        StudioNamedTypeContract execution = Type("graph.studio.resource.execution", Obj(P("applicationId", text), P("authorityChecksum", checksum), P("executionId", text), P("graphId", text), P("graphVersion", text), P("kind", executionKind)));
        StudioNamedTypeContract checkpoint = Type("graph.studio.resource.checkpoint", Obj(P("applicationId", text), P("authorityChecksum", checksum), P("checkpointId", text), P("executionId", text), P("graphId", text), P("graphVersion", text), P("kind", checkpointKind)));
        StudioNamedTypeContract schedule = Type("graph.studio.resource.schedule", Obj(P("applicationId", text), P("authorityChecksum", checksum), P("kind", scheduleKind), P("scheduleId", text), P("version", positiveInt)));
        StudioNamedTypeContract activation = Type("graph.studio.resource.activation", Obj(P("activationId", text), P("applicationId", text), P("authorityChecksum", checksum), P("definitionId", text), P("kind", activationKind), P("version", positiveInt)));
        StudioNamedTypeContract scheduleLink = Type("graph.studio.link.schedule", Obj(P("label", text), P("relation", scheduledBy), P("target", schedule)));
        StudioNamedTypeContract activationLink = Type("graph.studio.link.activation", Obj(P("label", text), P("relation", producedBy), P("target", activation)));
        StudioNamedTypeContract scheduleLinks = Type("graph.studio.links.schedule", "{\"kind\":\"array\",\"elementTypeId\":\"graph.studio.link.schedule\",\"minItems\":0,\"maxItems\":1}");
        StudioNamedTypeContract activationLinks = Type("graph.studio.links.activation", "{\"kind\":\"array\",\"elementTypeId\":\"graph.studio.link.activation\",\"minItems\":0,\"maxItems\":1}");
        StudioNamedTypeContract emptyLinks = Type("graph.studio.links.empty", "{\"kind\":\"array\",\"elementTypeId\":\"graph.studio.text\",\"minItems\":0,\"maxItems\":0}");
        StudioNamedTypeContract definitionResult = Type("graph.studio.resolved.definition", Obj(P("kind", resolvedKind), P("links", scheduleLinks), P("resource", definition), P("route", route)));
        StudioNamedTypeContract executionResult = Type("graph.studio.resolved.execution", Obj(P("kind", resolvedKind), P("links", activationLinks), P("resource", execution), P("route", route)));
        StudioNamedTypeContract checkpointResult = Type("graph.studio.resolved.checkpoint", Obj(P("kind", resolvedKind), P("links", emptyLinks), P("resource", checkpoint), P("route", route)));
        StudioNamedTypeContract error = Type("graph.studio.safe-error", "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":256,\"format\":\"safe-error-code\"}");

        StudioNamedTypeContract[] types = [activation, activationKind, activationLink, activationLinks, checkpoint, checkpointKind,
            checkpointResult, checksum, definition, definitionKind, definitionResult, emptyLinks, emptyMap, error, execution, executionKind,
            executionResult, producedBy, resolvedKind, route, routeParameters, schedule, scheduleKind, scheduleLink, scheduleLinks, scheduledBy,
            positiveInt, text, tokenRequest];
        var endpoints = new List<StudioEndpointContract>();
        var methods = new List<StudioMethodBinding>();
        var producers = new List<StudioProducerBinding>();
        Add(StudioResourceKind.GraphDefinition, "graph.studio.resolve.definition", "graph.studio.resource.definition", definitionResult,
            new Resolver(services, inspection, _baseWork.SingleOrDefault(), StudioResourceKind.GraphDefinition));
        Add(StudioResourceKind.GraphExecution, "graph.studio.resolve.execution", "graph.studio.resource.execution", executionResult,
            new Resolver(services, inspection, _baseWork.SingleOrDefault(), StudioResourceKind.GraphExecution));
        Add(StudioResourceKind.GraphCheckpoint, "graph.studio.resolve.checkpoint", "graph.studio.resource.checkpoint", checkpointResult,
            new Resolver(services, inspection, null, StudioResourceKind.GraphCheckpoint));
        return StudioModuleRuntimeContribution.Create(module, types.OrderBy(static value => value.TypeId, StringComparer.Ordinal),
            endpoints.OrderBy(static value => value.EndpointId, StringComparer.Ordinal), methods.OrderBy(static value => value.RegisteredMethodId, StringComparer.Ordinal),
            producers.OrderBy(static value => value.RegisteredMethodId, StringComparer.Ordinal));

        void Add(StudioResourceKind kind, string methodId, string endpointId, StudioNamedTypeContract result, IStudioResourceProducer producer)
        {
            StudioResourceRegistration registered = module.Resources.Single(value => value.Kind == kind);
            if (!StringComparer.Ordinal.Equals(registered.ResolverId, methodId) || !registered.EndpointIds.SequenceEqual([endpointId]))
                throw new InvalidOperationException("Graph resource registration differs from its Runtime resolver.");
            endpoints.Add(StudioEndpointContract.Create(endpointId, 1, StudioTransportMethod.Post, "/graph/studio/resources/" + Kind(kind),
                StudioEndpointAudience.ControlPlane, StudioTransportKind.SameOriginHttp, tokenRequest.TypeId, tokenRequest.NodeChecksum,
                result.TypeId, result.NodeChecksum, error.TypeId, error.NodeChecksum, 16_384, MaximumBytes, TimeSpan.FromSeconds(10)));
            methods.Add(StudioMethodBinding.Create(methodId, StudioMethodKind.Resolve, "graph", methodId, endpointId, tokenRequest.TypeId, result.TypeId));
            producers.Add(new StudioResourceProducerBinding(methodId, producer));
        }
    }

    private static StudioNamedTypeContract Type(string id, string descriptor) => StudioNamedTypeContract.Create(id, System.Text.Encoding.UTF8.GetBytes(descriptor));
    private static StudioNamedTypeContract PositiveInt() => Type("graph.studio.positive-int", "{\"kind\":\"integer\",\"wire\":\"number\",\"minimum\":\"1\",\"maximum\":\"2147483647\"}");
    private static string P(string name, StudioNamedTypeContract type) => $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type.TypeId}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}";
    private static string Obj(params string[] properties) => $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', properties)}],\"additionalProperties\":false}}";
    private static string Kind(StudioResourceKind kind) => kind switch { StudioResourceKind.GraphDefinition => "definition", StudioResourceKind.GraphExecution => "execution", StudioResourceKind.GraphCheckpoint => "checkpoint", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };

    private sealed class Resolver(IServiceProvider services, IGraphStudioInspectionAuthority inspection,
        IGraphStudioBaseWorkProjection? baseWork, StudioResourceKind kind) : IStudioResourceProducer
    {
        public async ValueTask<StudioCanonicalJson?> ResolveAsync(StudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope(); IServiceProvider scoped = scope.ServiceProvider;
            if ((await scoped.GetRequiredService<IAuthorizationService>().AuthorizeAsync(invocation.Bootstrap.HttpContext.User, null, "graph.studio.inspect").ConfigureAwait(false)).Succeeded is false)
                return null;
            if (!TryToken(invocation.Request, out StudioResourceIdentity? identity) || identity!.Kind != kind ||
                !StringComparer.Ordinal.Equals(identity.ApplicationId, invocation.Bootstrap.ApplicationGraph.ApplicationId)) return null;
            var links = new List<StudioResolvedLink>(1); string page;
            if (!await inspection.ExistsAsync(identity, cancellationToken).ConfigureAwait(false)) return null;
            switch (identity)
            {
                case StudioGraphDefinitionResource definition:
                    page = "graph.definition.detail";
                    if (baseWork is not null && await baseWork.ResolveScheduleAsync(definition, cancellationToken).ConfigureAwait(false) is { } schedule &&
                        StringComparer.Ordinal.Equals(schedule.ApplicationId, definition.ApplicationId))
                        links.Add(StudioResolvedLink.Create(schedule, StudioLinkRelation.ScheduledBy, "studio.link.graph.schedule"));
                    break;
                case StudioGraphExecutionResource execution:
                    page = "graph.execution.detail";
                    if (baseWork is not null && await baseWork.ResolveActivationAsync(execution, cancellationToken).ConfigureAwait(false) is { } activation &&
                        StringComparer.Ordinal.Equals(activation.ApplicationId, execution.ApplicationId))
                        links.Add(StudioResolvedLink.Create(activation, StudioLinkRelation.ProducedBy, "studio.link.graph.activation"));
                    break;
                case StudioGraphCheckpointResource checkpoint:
                    page = "graph.checkpoint.detail"; break;
                default: return null;
            }
            return StudioResolvedResourceJson.Encode(identity, StudioResolvedRoute.Create(page,
                [new KeyValuePair<string, string>("resource", StudioResourceRouteToken.Encode(identity))]), links, MaximumBytes);
        }

        private static bool TryToken(StudioCanonicalJson request, out StudioResourceIdentity? resource)
        {
            resource = null;
            using JsonDocument document = JsonDocument.Parse(request.ToArray()); JsonElement root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Select(static value => value.Name).SequenceEqual(["resourceToken"]) &&
                StudioResourceRouteToken.TryDecode(root.GetProperty("resourceToken").GetString(), out resource);
        }
    }
}
