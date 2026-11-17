using System.Buffers;
using System.Collections.Immutable;
using System.Text.Json;
using HPD.AI.Platform.Studio;

namespace HPD.Base.Studio;

/// <summary>Contributes BASE's exact executable Studio Runtime contracts.</summary>
internal sealed partial class HPDBaseStudioRuntimeContributionFactory : IStudioModuleRuntimeContributionFactory
{
    private const string PageEndpoint = "base.studio.view.page";
    private const string ResolveEndpoint = "base.studio.resource.resolve";
    private readonly IBaseSchemaProvider _schema;
    private readonly IBaseRecordRuntime _records;
    private readonly IRecordStoreRegistry _stores;
    private readonly IStudioEvidenceRuntime _evidence;
    private readonly IStudioControlInspectionStore _control;
    private readonly IStudioDynamicStoreAuthoritySource _dynamicStore;
    private readonly IStudioInfrastructureInventoryStore _infrastructure;
    private readonly IBaseHealthProvider _health;
    private readonly IBaseDiagnosticProvider _diagnostics;
    private readonly ImmutableArray<IBaseHealthContributor> _healthContributors;
    private readonly ImmutableArray<IBaseDiagnosticContributor> _diagnosticContributors;
    private readonly IFileBucketRegistry? _files;
    private readonly HPDBaseStudioAuthoritySnapshot _baseAuthority;
    private readonly IStudioPrincipalContextResolver _principals;
    private readonly HPDBaseStudioAuthorization _authorization;
    private readonly IBasePolicyOrchestrator _policy;
    private readonly IBaseTextAdministration? _textAdministration;
    private readonly IBaseVectorAdministration? _vectorAdministration;
    private readonly IHPDBaseAdministration? _administration;
    private readonly IBaseTextRuntime? _textRuntime;
    private readonly IBaseVectorRuntime? _vectorRuntime;
    private readonly IBaseSessionFactory? _sessions;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the BASE Runtime contribution from public Runtime-owned services.</summary>
    public HPDBaseStudioRuntimeContributionFactory(IBaseSchemaProvider schema, IBaseRecordRuntime records, IRecordStoreRegistry stores,
        IStudioPrincipalContextResolver principals, HPDBaseStudioAuthorization authorization, HPDBaseStudioAuthoritySnapshot baseAuthority,
        IAtomicRecordStore atomicStore, IBasePolicyOrchestrator policy, IBaseHealthProvider health, IBaseDiagnosticProvider diagnostics,
        IEnumerable<IBaseHealthContributor> healthContributors, IEnumerable<IBaseDiagnosticContributor> diagnosticContributors,
        IFileBucketRegistry? files = null, IBaseTextAdministration? textAdministration = null,
        IBaseVectorAdministration? vectorAdministration = null, IHPDBaseAdministration? administration = null,
        IServiceProvider? services = null)
    { _schema = schema; _records = records; _stores = stores; _evidence = new DefaultStudioEvidenceRuntime();
      _control = atomicStore as IStudioControlInspectionStore ?? throw new InvalidOperationException("base.studio.controlInspectionUnavailable");
      _dynamicStore = atomicStore as IStudioDynamicStoreAuthoritySource ?? throw new InvalidOperationException("base.studio.dynamicStoreUnavailable");
      _infrastructure = atomicStore as IStudioInfrastructureInventoryStore ?? throw new InvalidOperationException("base.studio.infrastructureInventoryUnavailable");
      _health = health; _diagnostics = diagnostics; _healthContributors = [.. healthContributors.OrderBy(static value => value.Id, StringComparer.Ordinal)];
      _diagnosticContributors = [.. diagnosticContributors.OrderBy(static value => value.Id, StringComparer.Ordinal)];
      _files = files; _principals = principals; _authorization = authorization; _policy = policy; _baseAuthority = baseAuthority;
      _textAdministration = textAdministration; _vectorAdministration = vectorAdministration; _administration = administration;
      _textRuntime = services?.GetService(typeof(IBaseTextRuntime)) as IBaseTextRuntime;
      _vectorRuntime = services?.GetService(typeof(IBaseVectorRuntime)) as IBaseVectorRuntime;
      _sessions = services?.GetService(typeof(IBaseSessionFactory)) as IBaseSessionFactory;
      _timeProvider = services?.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System; }

    /// <inheritdoc />
    public string ModuleId => "base";

    /// <inheritdoc />
    public StudioModuleRuntimeContribution Create(StudioModuleRegistration module)
    {
        if (!StringComparer.Ordinal.Equals(module.Identity.ModuleId, ModuleId))
            throw new ArgumentException("The BASE Runtime factory cannot author another module.", nameof(module));
        StudioNamedTypeContract empty = Type("base.studio.empty-request", "{\"kind\":\"object\",\"properties\":[],\"additionalProperties\":false}");
        StudioNamedTypeContract error = Type("base.studio.safe-error", "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":256,\"format\":\"safe-error-code\"}");
        StudioNamedTypeContract text = Type("base.studio.text", "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":512,\"format\":\"nfc-text\"}");
        StudioNamedTypeContract optionalText = Type("base.studio.optional-text", "{\"kind\":\"string\",\"minLength\":0,\"maxLength\":512,\"format\":\"nfc-text\"}");
        StudioNamedTypeContract checksum = Type("base.studio.sha256", "{\"kind\":\"string\",\"minLength\":64,\"maxLength\":64,\"format\":\"sha256\"}");
        StudioNamedTypeContract decimalLong = Type("base.studio.nonnegative-long", "{\"kind\":\"integer\",\"wire\":\"decimal-string\",\"minimum\":\"0\",\"maximum\":\"9223372036854775807\"}");
        StudioNamedTypeContract applicationKind = Type("base.studio.resource-kind.application", "{\"kind\":\"literal\",\"value\":\"application\"}");
        StudioNamedTypeContract currentKind = Type("base.studio.result-kind.current", "{\"kind\":\"literal\",\"value\":\"current\"}");
        StudioNamedTypeContract resolvedKind = Type("base.studio.result-kind.resolved", "{\"kind\":\"literal\",\"value\":\"resolved\"}");
        StudioNamedTypeContract collectionKind = Type("base.studio.resource-kind.collection", "{\"kind\":\"literal\",\"value\":\"collection\"}");
        StudioNamedTypeContract recordKind = Type("base.studio.resource-kind.record", "{\"kind\":\"literal\",\"value\":\"record\"}");
        StudioNamedTypeContract graphKind = Type("base.studio.authority-kind.graph", "{\"kind\":\"literal\",\"value\":\"graph\"}");
        StudioNamedTypeContract applicationIdentity = Type("base.studio.resource.application", Obj(
            P("applicationId", text), P("authorityChecksum", checksum), P("kind", applicationKind)));
        StudioNamedTypeContract graphAuthority = Type("base.studio.observation-authority.graph", Obj(
            P("applicationGraphChecksum", checksum), P("applicationGraphGeneration", decimalLong), P("authorityChecksum", checksum), P("kind", graphKind),
            P("policyOwnerChecksum", checksum), P("policyOwnerGeneration", decimalLong), P("studioOwnerChecksum", checksum), P("studioOwnerGeneration", decimalLong)));
        StudioNamedTypeContract overviewValue = Type("base.studio.overview.value", Obj(
            P("applicationId", text), P("contractVersion", text), P("diagnosticCount", decimalLong), PN("refreshedAtUtc", optionalText),
            P("runtimeId", text), P("viewId", text)));
        StudioNamedTypeContract emptyItems = Type("base.studio.empty-items", "{\"kind\":\"array\",\"elementTypeId\":\"base.studio.text\",\"minItems\":0,\"maxItems\":0}");
        StudioNamedTypeContract accounting = Type("base.studio.graph-accounting", Obj(
            P("definitionReads", decimalLong), P("policyEvaluations", decimalLong), P("projectedBytes", decimalLong), P("transientBytes", decimalLong)));
        StudioNamedTypeContract current = Type("base.studio.overview.current", Obj(P("accounting", accounting), P("evidence", emptyItems),
            P("kind", currentKind), P("links", emptyItems), P("observationAuthority", graphAuthority), P("resource", applicationIdentity), P("value", overviewValue)));
        StudioNamedTypeContract emptyMap = Type("base.studio.empty-map", "{\"kind\":\"object\",\"properties\":[],\"additionalProperties\":false}");
        StudioNamedTypeContract route = Type("base.studio.application-route", Obj(P("pageId", text), P("parameters", emptyMap), P("query", emptyMap)));
        StudioNamedTypeContract resolved = Type("base.studio.application-resolved", Obj(P("kind", resolvedKind),
            P("links", emptyItems), P("resource", applicationIdentity), P("route", route)));
        StudioNamedTypeContract tokenRequest = Type("base.studio.resource-token-request", Obj(P("resourceToken", text)));
        StudioNamedTypeContract resourceParameters = Type("base.studio.resource-route-parameters", Obj(P("resource", text)));
        StudioNamedTypeContract resourceRoute = Type("base.studio.resource-route", Obj(P("pageId", text), P("parameters", resourceParameters), P("query", emptyMap)));
        StudioNamedTypeContract collectionIdentity = Type("base.studio.resource.collection", Obj(P("applicationId", text), P("authorityChecksum", checksum),
            P("collectionId", text), P("installedCollectionChecksum", checksum), P("kind", collectionKind)));
        StudioNamedTypeContract recordIdentity = Type("base.studio.resource.record", Obj(P("applicationId", text), P("authorityChecksum", checksum),
            P("collectionId", text), P("installedCollectionChecksum", checksum), P("kind", recordKind), P("recordId", text)));
        StudioNamedTypeContract collectionResolved = Type("base.studio.collection-resolved", Obj(P("kind", resolvedKind), P("links", emptyItems),
            P("resource", collectionIdentity), P("route", resourceRoute)));
        StudioNamedTypeContract recordResolved = Type("base.studio.record-resolved", Obj(P("kind", resolvedKind), P("links", emptyItems),
            P("resource", recordIdentity), P("route", resourceRoute)));

        var types = new List<StudioNamedTypeContract> { accounting, applicationIdentity, applicationKind, checksum, current,
            collectionIdentity, collectionKind, collectionResolved, currentKind, decimalLong, empty, emptyItems, emptyMap, error,
            graphAuthority, graphKind, optionalText, overviewValue, recordIdentity, recordKind, recordResolved, resolved, resolvedKind,
            resourceParameters, resourceRoute, route, text, tokenRequest };
        var endpoints = new List<StudioEndpointContract>
        {
            Endpoint(PageEndpoint + ".application", "/base/studio/resources/application", empty, resolved),
            Endpoint(ResolveEndpoint + ".collection", "/base/studio/resources/collection", tokenRequest, collectionResolved),
            Endpoint(ResolveEndpoint + ".record", "/base/studio/resources/record", tokenRequest, recordResolved),
        };
        var methods = new List<StudioMethodBinding>();
        var producers = new List<StudioProducerBinding>();
        StudioResourceRegistration applicationResource = module.Resources.Single(static value => value.Kind == StudioResourceKind.Application);
        foreach (string viewId in new[] { "base.overview.activity.list", "base.overview.attention.list", "base.overview.summary.detail" })
        {
            string methodId = "base.studio.view." + viewId;
            StudioViewRegistration registeredView = module.Views.Single(value => value.ViewId == viewId);
            StudioNamedTypeContract request = Type(registeredView.RequestNodeId,
                Obj(P("resource", applicationIdentity)));
            if (!StudioSha256.FixedTimeEquals(request.NodeChecksum, registeredView.RequestNodeChecksum))
                throw new InvalidOperationException("A BASE view request differs from its graph-owned L41 node.");
            if (!StringComparer.Ordinal.Equals(registeredView.ItemNodeId, overviewValue.TypeId) ||
                !StudioSha256.FixedTimeEquals(registeredView.ItemNodeChecksum, overviewValue.NodeChecksum))
                throw new InvalidOperationException("A BASE view result differs from its graph-owned L41 node.");
            types.Add(request);
            string endpointId = PageEndpoint + "." + viewId;
            endpoints.Add(Endpoint(endpointId, "/base/studio/views/" + viewId, request, current));
            methods.Add(StudioMethodBinding.Create(methodId, StudioMethodKind.Page, "base", "base.overview",
                endpointId, request.TypeId, current.TypeId));
            producers.Add(new StudioViewProducerBinding(methodId, new OverviewProducer(_schema, _principals, _authorization, applicationResource.Grants, viewId)));
        }
        AddDataRuntime(module, types, endpoints, methods, producers, error, text, optionalText, checksum,
            decimalLong, currentKind, graphAuthority, accounting, emptyItems, emptyMap, tokenRequest,
            resourceParameters, resourceRoute, resolvedKind);
        AddOperationsRuntime(module, types, endpoints, methods, producers, error, text, checksum,
            currentKind, graphAuthority, accounting, emptyItems, tokenRequest, resourceParameters,
            resourceRoute, resolvedKind);
        AddSecurityRuntime(module, types, endpoints, methods, producers, error, text, checksum,
            decimalLong, currentKind, graphAuthority, accounting, emptyItems, tokenRequest,
            resourceParameters, resourceRoute, resolvedKind);
        AddInfrastructureRuntime(module, types, endpoints, methods, producers, error, text, checksum,
            decimalLong, currentKind, graphAuthority, accounting, emptyItems, tokenRequest,
            resourceParameters, resourceRoute, resolvedKind);
        AddAutomationsRuntime(module, types, endpoints, methods, producers, error, text, checksum,
            decimalLong, currentKind, graphAuthority, accounting, emptyItems, tokenRequest,
            resourceParameters, resourceRoute, resolvedKind);
        AddSubjectsRuntime(module, types, endpoints, methods, producers, error, text, checksum,
            currentKind, graphAuthority, accounting, emptyItems, tokenRequest, resourceRoute,
            resolvedKind);
        AddSearchRuntime(module, types, endpoints, methods, producers, error, text, checksum,
            decimalLong, currentKind, graphAuthority, accounting, emptyItems, tokenRequest,
            resourceParameters, resourceRoute, resolvedKind);
        AddDiagnosticsRuntime(module, types, endpoints, methods, producers, error, text, checksum,
            decimalLong, currentKind, graphAuthority, accounting, emptyItems, tokenRequest,
            resourceParameters, resourceRoute, resolvedKind);
        AddResolver(StudioResourceKind.Application, "base.studio.resolve.application", PageEndpoint + ".application", empty, resolved,
            new ApplicationProducer(_schema, _principals, _authorization, applicationResource.Grants));
        AddResolver(StudioResourceKind.Collection, "base.studio.resolve.collection", ResolveEndpoint + ".collection", tokenRequest, collectionResolved,
            new CollectionProducer(_schema, _principals, _authorization, module.Resources.Single(static value => value.Kind == StudioResourceKind.Collection).Grants, _baseAuthority));
        AddResolver(StudioResourceKind.Record, "base.studio.resolve.record", ResolveEndpoint + ".record", tokenRequest, recordResolved,
            new RecordProducer(_records, _principals, _authorization, module.Resources.Single(static value => value.Kind == StudioResourceKind.Record).Grants, _baseAuthority));
        methods.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.RegisteredMethodId, right.RegisteredMethodId));
        producers.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.RegisteredMethodId, right.RegisteredMethodId));
        StudioNamedTypeContract[] canonicalTypes = types
            .GroupBy(static value => value.TypeId, StringComparer.Ordinal)
            .Select(static group => group.All(value => StudioSha256.FixedTimeEquals(value.NodeChecksum, group.First().NodeChecksum))
                ? group.First()
                : throw new InvalidOperationException($"Runtime type '{group.Key}' has competing descriptors."))
            .OrderBy(static value => value.TypeId, StringComparer.Ordinal).ToArray();
        return StudioModuleRuntimeContribution.Create(module, canonicalTypes,
            endpoints.OrderBy(static value => value.EndpointId, StringComparer.Ordinal), methods, producers);

        void AddResolver(StudioResourceKind kind, string methodId, string endpointId, StudioNamedTypeContract request,
            StudioNamedTypeContract result, IStudioResourceProducer producer)
        {
            string owner = module.Resources.Single(value => value.Kind == kind).ResolverId;
            methods.Add(StudioMethodBinding.Create(methodId, StudioMethodKind.Resolve, "base", owner, endpointId, request.TypeId, result.TypeId));
            producers.Add(new StudioResourceProducerBinding(methodId, producer));
        }
        StudioEndpointContract Endpoint(string id, string route, StudioNamedTypeContract request, StudioNamedTypeContract result)
            => StudioEndpointContract.Create(id, 1, StudioTransportMethod.Post, route,
                StudioEndpointAudience.ControlPlane, StudioTransportKind.SameOriginHttp,
                request.TypeId, request.NodeChecksum, result.TypeId, result.NodeChecksum, error.TypeId, error.NodeChecksum,
                16_384, 1_048_576, TimeSpan.FromSeconds(10));
        static string P(string name, StudioNamedTypeContract type) => $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type.TypeId}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}";
        static string PN(string name, StudioNamedTypeContract type) => $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type.TypeId}\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"}}";
        static string Obj(params string[] properties) => $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', properties)}],\"additionalProperties\":false}}";
    }

    private static StudioNamedTypeContract Type(string id, string descriptor)
        => StudioNamedTypeContract.Create(id, System.Text.Encoding.UTF8.GetBytes(descriptor));

    private abstract class ProducerBase(IStudioPrincipalContextResolver principals, HPDBaseStudioAuthorization authorization,
        ImmutableArray<StudioGrantRequirement> grants)
    {
        protected async ValueTask<(PrincipalContext Principal, OperationContext Operation)?> ContextAsync(
            StudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            PrincipalContext? principal = await principals.ResolveAsync(invocation.Bootstrap.HttpContext,
                invocation.Bootstrap.Authorization.Session, cancellationToken).ConfigureAwait(false);
            return principal is null ? null : (principal, new OperationContext
            {
                ApplicationId = invocation.Bootstrap.ApplicationGraph.ApplicationId,
                Audience = HPDBaseEndpointAudience.ControlPlane, Operation = BaseOperationKind.AdminInspect,
                CollectionId = "base.studio", Mode = OperationMode.User,
                Now = invocation.Bootstrap.Authorization.Session.IssuedAtUtc,
            });
        }
        protected async ValueTask<bool> AuthorizedAsync(StudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            foreach (StudioGrantRequirement grant in grants)
                if (await authorization.AdmitAsync(invocation.Bootstrap, grant, cancellationToken).ConfigureAwait(false) is null) return false;
            return true;
        }
        protected ValueTask<BaseOwnedSubjectScopeEvidence?> ScopeAsync(StudioProducerInvocation invocation, CancellationToken cancellationToken) =>
            principals.ResolveScopeAsync(invocation.Bootstrap.HttpContext, invocation.Bootstrap.Authorization.Session, cancellationToken);
        protected int PolicyEvaluationCount => grants.Length;
        protected static StudioCanonicalJson Value(string viewId, string applicationId, SchemaMetadata metadata)
        {
            var buffer = new ArrayBufferWriter<byte>(); using var json = new Utf8JsonWriter(buffer);
            json.WriteStartObject(); json.WriteString("applicationId", applicationId); json.WriteString("contractVersion", metadata.ContractVersion);
            json.WriteString("diagnosticCount", (metadata.Diagnostics?.Length ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture));
            json.WritePropertyName("refreshedAtUtc"); if (metadata.RefreshedAt is { } refreshed)
                json.WriteStringValue(StudioResponseAuthority.CanonicalUtc(refreshed.ToUniversalTime())); else json.WriteNullValue();
            json.WriteString("runtimeId", metadata.RuntimeId); json.WriteString("viewId", viewId);
            json.WriteEndObject(); json.Flush();
            return StudioCanonicalJson.Create(buffer.WrittenSpan, 1_048_576);
        }
        protected StudioCanonicalJson Accounting(int projectedBytes)
        {
            var buffer = new ArrayBufferWriter<byte>(); using var json = new Utf8JsonWriter(buffer); json.WriteStartObject();
            json.WriteString("definitionReads", "1"); json.WriteString("policyEvaluations", PolicyEvaluationCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            json.WriteString("projectedBytes", projectedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            json.WriteString("transientBytes", projectedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            json.WriteEndObject(); json.Flush(); return StudioCanonicalJson.Create(buffer.WrittenSpan, 4_096);
        }
    }

    private sealed class OverviewProducer(IBaseSchemaProvider schema, IStudioPrincipalContextResolver principals,
        HPDBaseStudioAuthorization authorization, ImmutableArray<StudioGrantRequirement> grants, string viewId)
        : ProducerBase(principals, authorization, grants), IStudioViewProducer
    {
        public async ValueTask<StudioCanonicalJson?> ReadAsync(StudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false)) return null;
            var context = await ContextAsync(invocation, cancellationToken).ConfigureAwait(false); if (context is null) return null;
            OperationResult<SchemaMetadata> result = await schema.GetSchemaAsync(context.Value.Principal, context.Value.Operation,
                VisibilityLevel.Admin, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess() || result.Value is null) return null;
            StudioCanonicalJson value = Value(viewId, invocation.Bootstrap.ApplicationGraph.ApplicationId, result.Value);
            return StudioObservationJson.Current(new StudioApplicationResource(invocation.Bootstrap.ApplicationGraph.ApplicationId),
                StudioGraphObservationAuthority.Create(invocation.Authority), value, [], [], Accounting(value.ToArray().Length), 1_048_576);
        }
    }

    private sealed class ApplicationProducer(IBaseSchemaProvider schema, IStudioPrincipalContextResolver principals,
        HPDBaseStudioAuthorization authorization, ImmutableArray<StudioGrantRequirement> grants)
        : ProducerBase(principals, authorization, grants), IStudioResourceProducer
    {
        public async ValueTask<StudioCanonicalJson?> ResolveAsync(StudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false)) return null;
            var context = await ContextAsync(invocation, cancellationToken).ConfigureAwait(false); if (context is null) return null;
            OperationResult<SchemaMetadata> result = await schema.GetSchemaAsync(context.Value.Principal, context.Value.Operation,
                VisibilityLevel.Admin, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess() || result.Value is null) return null;
            var identity = new StudioApplicationResource(invocation.Bootstrap.ApplicationGraph.ApplicationId);
            return StudioResolvedResourceJson.Encode(identity, StudioResolvedRoute.Create("base.overview", []), [], 1_048_576);
        }
    }

    private sealed class CollectionProducer(IBaseSchemaProvider schema, IStudioPrincipalContextResolver principals,
        HPDBaseStudioAuthorization authorization, ImmutableArray<StudioGrantRequirement> grants, HPDBaseStudioAuthoritySnapshot baseAuthority)
        : ProducerBase(principals, authorization, grants), IStudioResourceProducer
    {
        public async ValueTask<StudioCanonicalJson?> ResolveAsync(StudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false)) return null;
            if (!Token(invocation.Request, out StudioResourceIdentity? decoded) || decoded is not StudioCollectionResource resource ||
                !StringComparer.Ordinal.Equals(resource.ApplicationId, invocation.Bootstrap.ApplicationGraph.ApplicationId) ||
                baseAuthority.GetInstalledCollectionChecksum(resource.CollectionId) is not { } expected ||
                !StudioSha256.FixedTimeEquals(resource.InstalledCollectionChecksum, StudioSha256.FromDigest(expected))) return null;
            var context = await ContextAsync(invocation, cancellationToken).ConfigureAwait(false); if (context is null) return null;
            OperationResult<CollectionDefinition> result = await schema.GetCollectionAsync(resource.CollectionId, context.Value.Principal,
                context.Value.Operation with { CollectionId = resource.CollectionId }, VisibilityLevel.Admin, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess() || result.Value is null) return null;
            return StudioResolvedResourceJson.Encode(resource, StudioResolvedRoute.Create("base.collection.detail",
                [new KeyValuePair<string, string>("resource", StudioResourceRouteToken.Encode(resource))]), [], 1_048_576);
        }
    }

    private sealed class RecordProducer(IBaseRecordRuntime records, IStudioPrincipalContextResolver principals,
        HPDBaseStudioAuthorization authorization, ImmutableArray<StudioGrantRequirement> grants, HPDBaseStudioAuthoritySnapshot baseAuthority)
        : ProducerBase(principals, authorization, grants), IStudioResourceProducer
    {
        public async ValueTask<StudioCanonicalJson?> ResolveAsync(StudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false)) return null;
            if (!Token(invocation.Request, out StudioResourceIdentity? decoded) || decoded is not StudioRecordResource resource ||
                !StringComparer.Ordinal.Equals(resource.ApplicationId, invocation.Bootstrap.ApplicationGraph.ApplicationId) ||
                baseAuthority.GetInstalledCollectionChecksum(resource.CollectionId) is not { } expected ||
                !StudioSha256.FixedTimeEquals(resource.InstalledCollectionChecksum, StudioSha256.FromDigest(expected))) return null;
            var context = await ContextAsync(invocation, cancellationToken).ConfigureAwait(false); if (context is null) return null;
            OperationResult<RecordEnvelope> result = await records.GetAsync(resource.CollectionId, RecordId.Parse(resource.RecordId), context.Value.Principal,
                context.Value.Operation with { CollectionId = resource.CollectionId }, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess() || result.Value is null) return null;
            return StudioResolvedResourceJson.Encode(resource, StudioResolvedRoute.Create("base.record.detail",
                [new KeyValuePair<string, string>("resource", StudioResourceRouteToken.Encode(resource))]), [], 1_048_576);
        }
    }

    private static bool Token(StudioCanonicalJson request, out StudioResourceIdentity? resource)
    {
        using JsonDocument document = JsonDocument.Parse(request.ToArray()); JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.EnumerateObject().Select(static value => value.Name)
                .SequenceEqual(["resourceToken"], StringComparer.Ordinal) || root.GetProperty("resourceToken").ValueKind != JsonValueKind.String)
        { resource = null; return false; }
        return StudioResourceRouteToken.TryDecode(root.GetProperty("resourceToken").GetString(), out resource);
    }
}
