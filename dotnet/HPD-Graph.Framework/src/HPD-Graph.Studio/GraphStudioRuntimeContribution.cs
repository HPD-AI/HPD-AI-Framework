using System.Collections.Immutable;
using System.Text.Json;
using HPD.AI.Platform.Studio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Graph.Studio;

/// <summary>Builds Graph's executable, resource-resolving Studio Runtime contribution.</summary>
public sealed class GraphStudioRuntimeContributionFactory(
    IServiceProvider services, IGraphStudioInspectionAuthority inspection,
    IEnumerable<IGraphStudioBaseWorkProjection> baseWork) : IBaseStudioModuleRuntimeContributionFactory
{
    private const int MaximumBytes = 1_048_576;
    private readonly ImmutableArray<IGraphStudioBaseWorkProjection> _baseWork = [.. baseWork];

    /// <inheritdoc />
    public string ModuleId => "graph";

    /// <inheritdoc />
    public BaseStudioModuleRuntimeContribution Create(BaseStudioModuleRegistration module)
    {
        if (!StringComparer.Ordinal.Equals(module.Identity.ModuleId, ModuleId))
            throw new ArgumentException("The Graph Runtime factory cannot author another module.", nameof(module));
        if (_baseWork.Length > 1)
            throw new InvalidOperationException("Graph Studio admits at most one authoritative BASE work projection.");

        BaseStudioNamedTypeContract text = Type("graph.studio.text", "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":512,\"format\":\"nfc-text\"}");
        BaseStudioNamedTypeContract checksum = Type("graph.studio.sha256", "{\"kind\":\"string\",\"minLength\":64,\"maxLength\":64,\"format\":\"sha256\"}");
        BaseStudioNamedTypeContract tokenRequest = Type("graph.studio.resource-token-request", Obj(P("resourceToken", text)));
        BaseStudioNamedTypeContract emptyMap = Type("graph.studio.empty-map", "{\"kind\":\"object\",\"properties\":[],\"additionalProperties\":false}");
        BaseStudioNamedTypeContract routeParameters = Type("graph.studio.route-parameters", Obj(P("resource", text)));
        BaseStudioNamedTypeContract route = Type("graph.studio.resolved-route", Obj(P("pageId", text), P("parameters", routeParameters), P("query", emptyMap)));
        BaseStudioNamedTypeContract resolvedKind = Type("graph.studio.resolved-kind", "{\"kind\":\"literal\",\"value\":\"resolved\"}");
        BaseStudioNamedTypeContract definitionKind = Type("graph.studio.definition-kind", "{\"kind\":\"literal\",\"value\":\"graphDefinition\"}");
        BaseStudioNamedTypeContract executionKind = Type("graph.studio.execution-kind", "{\"kind\":\"literal\",\"value\":\"graphExecution\"}");
        BaseStudioNamedTypeContract checkpointKind = Type("graph.studio.checkpoint-kind", "{\"kind\":\"literal\",\"value\":\"graphCheckpoint\"}");
        BaseStudioNamedTypeContract scheduleKind = Type("graph.studio.schedule-kind", "{\"kind\":\"literal\",\"value\":\"schedule\"}");
        BaseStudioNamedTypeContract activationKind = Type("graph.studio.activation-kind", "{\"kind\":\"literal\",\"value\":\"activation\"}");
        BaseStudioNamedTypeContract scheduledBy = Type("graph.studio.scheduled-by", "{\"kind\":\"literal\",\"value\":\"scheduledBy\"}");
        BaseStudioNamedTypeContract producedBy = Type("graph.studio.produced-by", "{\"kind\":\"literal\",\"value\":\"producedBy\"}");
        BaseStudioNamedTypeContract positiveInt = PositiveInt();
        BaseStudioNamedTypeContract definition = Type("graph.studio.resource.definition", Obj(P("applicationId", text), P("authorityChecksum", checksum), P("graphId", text), P("graphVersion", text), P("kind", definitionKind)));
        BaseStudioNamedTypeContract execution = Type("graph.studio.resource.execution", Obj(P("applicationId", text), P("authorityChecksum", checksum), P("executionId", text), P("graphId", text), P("graphVersion", text), P("kind", executionKind)));
        BaseStudioNamedTypeContract checkpoint = Type("graph.studio.resource.checkpoint", Obj(P("applicationId", text), P("authorityChecksum", checksum), P("checkpointId", text), P("executionId", text), P("graphId", text), P("graphVersion", text), P("kind", checkpointKind)));
        BaseStudioNamedTypeContract schedule = Type("graph.studio.resource.schedule", Obj(P("applicationId", text), P("authorityChecksum", checksum), P("kind", scheduleKind), P("scheduleId", text), P("version", positiveInt)));
        BaseStudioNamedTypeContract activation = Type("graph.studio.resource.activation", Obj(P("activationId", text), P("applicationId", text), P("authorityChecksum", checksum), P("definitionId", text), P("kind", activationKind), P("version", positiveInt)));
        BaseStudioNamedTypeContract scheduleLink = Type("graph.studio.link.schedule", Obj(P("label", text), P("relation", scheduledBy), P("target", schedule)));
        BaseStudioNamedTypeContract activationLink = Type("graph.studio.link.activation", Obj(P("label", text), P("relation", producedBy), P("target", activation)));
        BaseStudioNamedTypeContract scheduleLinks = Type("graph.studio.links.schedule", "{\"kind\":\"array\",\"elementTypeId\":\"graph.studio.link.schedule\",\"minItems\":0,\"maxItems\":1}");
        BaseStudioNamedTypeContract activationLinks = Type("graph.studio.links.activation", "{\"kind\":\"array\",\"elementTypeId\":\"graph.studio.link.activation\",\"minItems\":0,\"maxItems\":1}");
        BaseStudioNamedTypeContract emptyLinks = Type("graph.studio.links.empty", "{\"kind\":\"array\",\"elementTypeId\":\"graph.studio.text\",\"minItems\":0,\"maxItems\":0}");
        BaseStudioNamedTypeContract definitionResult = Type("graph.studio.resolved.definition", Obj(P("kind", resolvedKind), P("links", scheduleLinks), P("resource", definition), P("route", route)));
        BaseStudioNamedTypeContract executionResult = Type("graph.studio.resolved.execution", Obj(P("kind", resolvedKind), P("links", activationLinks), P("resource", execution), P("route", route)));
        BaseStudioNamedTypeContract checkpointResult = Type("graph.studio.resolved.checkpoint", Obj(P("kind", resolvedKind), P("links", emptyLinks), P("resource", checkpoint), P("route", route)));
        BaseStudioNamedTypeContract error = Type("graph.studio.safe-error", "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":256,\"format\":\"safe-error-code\"}");

        BaseStudioNamedTypeContract[] types = [activation, activationKind, activationLink, activationLinks, checkpoint, checkpointKind,
            checkpointResult, checksum, definition, definitionKind, definitionResult, emptyLinks, emptyMap, error, execution, executionKind,
            executionResult, producedBy, resolvedKind, route, routeParameters, schedule, scheduleKind, scheduleLink, scheduleLinks, scheduledBy,
            positiveInt, text, tokenRequest];
        var endpoints = new List<BaseStudioEndpointContract>();
        var methods = new List<BaseStudioMethodBinding>();
        var producers = new List<BaseStudioProducerBinding>();
        Add(BaseStudioResourceKind.GraphDefinition, "graph.studio.resolve.definition", "graph.studio.resource.definition", definitionResult,
            new Resolver(services, inspection, _baseWork.SingleOrDefault(), BaseStudioResourceKind.GraphDefinition));
        Add(BaseStudioResourceKind.GraphExecution, "graph.studio.resolve.execution", "graph.studio.resource.execution", executionResult,
            new Resolver(services, inspection, _baseWork.SingleOrDefault(), BaseStudioResourceKind.GraphExecution));
        Add(BaseStudioResourceKind.GraphCheckpoint, "graph.studio.resolve.checkpoint", "graph.studio.resource.checkpoint", checkpointResult,
            new Resolver(services, inspection, null, BaseStudioResourceKind.GraphCheckpoint));
        return BaseStudioModuleRuntimeContribution.Create(module, types.OrderBy(static value => value.TypeId, StringComparer.Ordinal),
            endpoints.OrderBy(static value => value.EndpointId, StringComparer.Ordinal), methods.OrderBy(static value => value.RegisteredMethodId, StringComparer.Ordinal),
            producers.OrderBy(static value => value.RegisteredMethodId, StringComparer.Ordinal));

        void Add(BaseStudioResourceKind kind, string methodId, string endpointId, BaseStudioNamedTypeContract result, IBaseStudioResourceProducer producer)
        {
            BaseStudioResourceRegistration registered = module.Resources.Single(value => value.Kind == kind);
            if (!StringComparer.Ordinal.Equals(registered.ResolverId, methodId) || !registered.EndpointIds.SequenceEqual([endpointId]))
                throw new InvalidOperationException("Graph resource registration differs from its Runtime resolver.");
            endpoints.Add(BaseStudioEndpointContract.Create(endpointId, 1, BaseStudioTransportMethod.Post, "/graph/studio/resources/" + Kind(kind),
                BaseStudioEndpointAudience.ControlPlane, BaseStudioTransportKind.SameOriginHttp, tokenRequest.TypeId, tokenRequest.NodeChecksum,
                result.TypeId, result.NodeChecksum, error.TypeId, error.NodeChecksum, 16_384, MaximumBytes, TimeSpan.FromSeconds(10)));
            methods.Add(BaseStudioMethodBinding.Create(methodId, BaseStudioMethodKind.Resolve, "graph", methodId, endpointId, tokenRequest.TypeId, result.TypeId));
            producers.Add(new BaseStudioResourceProducerBinding(methodId, producer));
        }
    }

    private static BaseStudioNamedTypeContract Type(string id, string descriptor) => BaseStudioNamedTypeContract.Create(id, System.Text.Encoding.UTF8.GetBytes(descriptor));
    private static BaseStudioNamedTypeContract PositiveInt() => Type("graph.studio.positive-int", "{\"kind\":\"integer\",\"wire\":\"number\",\"minimum\":\"1\",\"maximum\":\"2147483647\"}");
    private static string P(string name, BaseStudioNamedTypeContract type) => $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type.TypeId}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}";
    private static string Obj(params string[] properties) => $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', properties)}],\"additionalProperties\":false}}";
    private static string Kind(BaseStudioResourceKind kind) => kind switch { BaseStudioResourceKind.GraphDefinition => "definition", BaseStudioResourceKind.GraphExecution => "execution", BaseStudioResourceKind.GraphCheckpoint => "checkpoint", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };

    private sealed class Resolver(IServiceProvider services, IGraphStudioInspectionAuthority inspection,
        IGraphStudioBaseWorkProjection? baseWork, BaseStudioResourceKind kind) : IBaseStudioResourceProducer
    {
        public async ValueTask<BaseStudioCanonicalJson?> ResolveAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope(); IServiceProvider scoped = scope.ServiceProvider;
            if ((await scoped.GetRequiredService<IAuthorizationService>().AuthorizeAsync(invocation.Bootstrap.HttpContext.User, null, "graph.studio.inspect").ConfigureAwait(false)).Succeeded is false)
                return null;
            if (!TryToken(invocation.Request, out BaseStudioResourceIdentity? identity) || identity!.Kind != kind ||
                !StringComparer.Ordinal.Equals(identity.ApplicationId, invocation.Bootstrap.ApplicationGraph.ApplicationId)) return null;
            var links = new List<BaseStudioResolvedLink>(1); string page;
            if (!await inspection.ExistsAsync(identity, cancellationToken).ConfigureAwait(false)) return null;
            switch (identity)
            {
                case BaseStudioGraphDefinitionResource definition:
                    page = "graph.definition.detail";
                    if (baseWork is not null && await baseWork.ResolveScheduleAsync(definition, cancellationToken).ConfigureAwait(false) is { } schedule &&
                        StringComparer.Ordinal.Equals(schedule.ApplicationId, definition.ApplicationId))
                        links.Add(BaseStudioResolvedLink.Create(schedule, BaseStudioLinkRelation.ScheduledBy, "studio.link.graph.schedule"));
                    break;
                case BaseStudioGraphExecutionResource execution:
                    page = "graph.execution.detail";
                    if (baseWork is not null && await baseWork.ResolveActivationAsync(execution, cancellationToken).ConfigureAwait(false) is { } activation &&
                        StringComparer.Ordinal.Equals(activation.ApplicationId, execution.ApplicationId))
                        links.Add(BaseStudioResolvedLink.Create(activation, BaseStudioLinkRelation.ProducedBy, "studio.link.graph.activation"));
                    break;
                case BaseStudioGraphCheckpointResource checkpoint:
                    page = "graph.checkpoint.detail"; break;
                default: return null;
            }
            return BaseStudioResolvedResourceJson.Encode(identity, BaseStudioResolvedRoute.Create(page,
                [new KeyValuePair<string, string>("resource", BaseStudioResourceRouteToken.Encode(identity))]), links, MaximumBytes);
        }

        private static bool TryToken(BaseStudioCanonicalJson request, out BaseStudioResourceIdentity? resource)
        {
            resource = null;
            using JsonDocument document = JsonDocument.Parse(request.ToArray()); JsonElement root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Select(static value => value.Name).SequenceEqual(["resourceToken"]) &&
                BaseStudioResourceRouteToken.TryDecode(root.GetProperty("resourceToken").GetString(), out resource);
        }
    }
}
