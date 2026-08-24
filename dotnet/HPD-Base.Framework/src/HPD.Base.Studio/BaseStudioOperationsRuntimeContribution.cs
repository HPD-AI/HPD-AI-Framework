using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using HPD.AI.Platform.Studio;

namespace HPD.Base.Studio;

internal sealed partial class BaseStudioRuntimeContributionFactory
{
    private void AddOperationsRuntime(BaseStudioModuleRegistration module,
        List<BaseStudioNamedTypeContract> types, List<BaseStudioEndpointContract> endpoints,
        List<BaseStudioMethodBinding> methods, List<BaseStudioProducerBinding> producers,
        BaseStudioNamedTypeContract error, BaseStudioNamedTypeContract text, BaseStudioNamedTypeContract checksum,
        BaseStudioNamedTypeContract currentKind, BaseStudioNamedTypeContract graphAuthority,
        BaseStudioNamedTypeContract accounting, BaseStudioNamedTypeContract emptyItems,
        BaseStudioNamedTypeContract tokenRequest, BaseStudioNamedTypeContract resourceParameters,
        BaseStudioNamedTypeContract resourceRoute, BaseStudioNamedTypeContract resolvedKind)
    {
        BaseStudioNamedTypeContract operationKind = Type("base.studio.resource-kind.operationexecution", "{\"kind\":\"literal\",\"value\":\"operationExecution\"}");
        BaseStudioNamedTypeContract operationResource = Type("base.studio.resource.operationexecution", Obj(
            P("applicationId", text), P("authorityChecksum", checksum), P("kind", operationKind),
            P("operationId", text), P("operationKind", text), P("requestIdentity", text)));
        BaseStudioNamedTypeContract definitionItem = Type("base.studio.installed-definition.item",
            "{\"kind\":\"object\",\"properties\":[{\"name\":\"definitionChecksum\",\"wireName\":\"definitionChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"id\",\"wireName\":\"id\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"kind\",\"wireName\":\"kind\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"owningModuleId\",\"wireName\":\"owningModuleId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"version\",\"wireName\":\"version\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}");
        BaseStudioNamedTypeContract executionItem = Type("base.studio.atomic-execution.item",
            "{\"kind\":\"object\",\"properties\":[{\"name\":\"expiresAtUtc\",\"wireName\":\"expiresAtUtc\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"identity\",\"wireName\":\"identity\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"requestFingerprint\",\"wireName\":\"requestFingerprint\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"resultKind\",\"wireName\":\"resultKind\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"structuralDigest\",\"wireName\":\"structuralDigest\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}");
        BaseStudioNamedTypeContract definitionItems = Type("base.studio.installed-definition.items", "{\"kind\":\"array\",\"elementTypeId\":\"base.studio.installed-definition.item\",\"minItems\":0,\"maxItems\":500}");
        BaseStudioNamedTypeContract executionItems = Type("base.studio.atomic-execution.items", "{\"kind\":\"array\",\"elementTypeId\":\"base.studio.atomic-execution.item\",\"minItems\":0,\"maxItems\":500}");
        BaseStudioNamedTypeContract app = types.Single(static value => value.TypeId == "base.studio.resource.application");
        BaseStudioNamedTypeContract definitionRequest = Type("base.operations.definitions.request", Obj(P("resource", app)));
        BaseStudioNamedTypeContract executionRequest = Type("base.operations.executions.request", Obj(P("resource", app)));
        BaseStudioNamedTypeContract definitionCurrent = Type("base.studio.installed-definition.current", Obj(P("accounting", accounting),
            P("evidence", emptyItems), P("kind", currentKind), P("links", emptyItems), P("observationAuthority", graphAuthority),
            P("resource", app), P("value", definitionItems)));
        BaseStudioNamedTypeContract executionCurrent = Type("base.studio.atomic-execution.current", Obj(P("accounting", accounting),
            P("evidence", emptyItems), P("kind", currentKind), P("links", emptyItems), P("observationAuthority", graphAuthority),
            P("resource", app), P("value", executionItems)));
        BaseStudioNamedTypeContract resolved = Type("base.studio.operationexecution-resolved", Obj(P("kind", resolvedKind),
            P("links", emptyItems), P("resource", operationResource), P("route", resourceRoute)));
        types.AddRange([operationKind, operationResource, definitionItem, executionItem, definitionItems, executionItems,
            definitionRequest, executionRequest, definitionCurrent, executionCurrent, resolved]);

        foreach ((string viewId, HPDBaseStudioDefinitionKind kind) in new[]
        {
            ("base.operations.definitions.registeredReads.list", HPDBaseStudioDefinitionKind.RegisteredRead),
            ("base.operations.definitions.selectionOperations.list", HPDBaseStudioDefinitionKind.SelectionMutation),
            ("base.operations.definitions.moduleMutations.list", HPDBaseStudioDefinitionKind.ModuleMutation),
            ("base.operations.definitions.semanticActivations.list", HPDBaseStudioDefinitionKind.SemanticActivation),
            ("base.semanticActivations.definitions.list", HPDBaseStudioDefinitionKind.SemanticActivation),
        }) AddView(viewId, definitionRequest, definitionCurrent,
            new InstalledDefinitionProducer(_principals, _authorization,
                module.Pages.Single(x => x.PageId == (viewId.StartsWith("base.semanticActivations.", StringComparison.Ordinal)
                    ? "base.semanticActivations" : "base.operations")).Grants, _baseAuthority, kind));
        AddView("base.operations.executions.list", executionRequest, executionCurrent,
            new AtomicExecutionProducer(_principals, _authorization, module.Pages.Single(static x => x.PageId == "base.operations").Grants, _control));
        AddView("base.operations.receipts.list", executionRequest, executionCurrent,
            new AtomicExecutionProducer(_principals, _authorization, module.Pages.Single(static x => x.PageId == "base.operations").Grants, _control));

        const string resolverMethod = "base.studio.resolve.operationexecution";
        const string resolverEndpoint = "base.studio.resource.resolve.operationexecution";
        endpoints.Add(Endpoint(resolverEndpoint, "/base/studio/resources/operation-execution", tokenRequest, resolved));
        BaseStudioResourceRegistration resource = module.Resources.Single(static x => x.Kind == BaseStudioResourceKind.OperationExecution);
        methods.Add(BaseStudioMethodBinding.Create(resolverMethod, BaseStudioMethodKind.Resolve, "base", resource.ResolverId,
            resolverEndpoint, tokenRequest.TypeId, resolved.TypeId));
        producers.Add(new BaseStudioResourceProducerBinding(resolverMethod,
            new OperationExecutionResolver(_principals, _authorization, resource.Grants, _control)));

        void AddView(string viewId, BaseStudioNamedTypeContract request, BaseStudioNamedTypeContract result, IBaseStudioViewProducer producer)
        {
            BaseStudioViewRegistration view = module.Views.Single(x => x.ViewId == viewId);
            if (!BaseStudioSha256.FixedTimeEquals(view.RequestNodeChecksum, request.NodeChecksum) ||
                !BaseStudioSha256.FixedTimeEquals(view.ItemNodeChecksum,
                    viewId.Contains("definitions", StringComparison.Ordinal) ? definitionItem.NodeChecksum : executionItem.NodeChecksum))
                throw new InvalidOperationException("The Operations view differs from its graph-owned L41 nodes.");
            string endpointId = "base.studio.view.page." + viewId;
            string methodId = "base.studio.view." + viewId;
            endpoints.Add(Endpoint(endpointId, "/base/studio/views/" + viewId, request, result));
            methods.Add(BaseStudioMethodBinding.Create(methodId, BaseStudioMethodKind.Page, "base",
                viewId.StartsWith("base.semanticActivations.", StringComparison.Ordinal) ? "base.semanticActivations" : "base.operations",
                endpointId, request.TypeId, result.TypeId));
            producers.Add(new BaseStudioViewProducerBinding(methodId, producer));
        }
        BaseStudioEndpointContract Endpoint(string id, string route, BaseStudioNamedTypeContract request, BaseStudioNamedTypeContract result)
            => BaseStudioEndpointContract.Create(id, 1, BaseStudioTransportMethod.Post, route, BaseStudioEndpointAudience.ControlPlane,
                BaseStudioTransportKind.SameOriginHttp, request.TypeId, request.NodeChecksum, result.TypeId, result.NodeChecksum,
                error.TypeId, error.NodeChecksum, 16_384, 1_048_576, TimeSpan.FromSeconds(10));
        static string P(string name, BaseStudioNamedTypeContract type) => $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type.TypeId}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}";
        static string Obj(params string[] properties) => $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', properties)}],\"additionalProperties\":false}}";
    }

    private sealed class InstalledDefinitionProducer(IBaseStudioPrincipalContextResolver principals, BaseStudioAuthorization authorization,
        ImmutableArray<BaseStudioGrantRequirement> grants, HPDBaseStudioAuthoritySnapshot authority, HPDBaseStudioDefinitionKind kind)
        : ProducerBase(principals, authorization, grants), IBaseStudioViewProducer
    {
        public async ValueTask<BaseStudioCanonicalJson?> ReadAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) ||
                !RequestResource(invocation.Request, BaseStudioResourceKind.Application, out BaseStudioResourceIdentity? resource) || resource is null) return null;
            var buffer = new ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer); writer.WriteStartArray();
            foreach (HPDBaseStudioDefinitionAuthority item in authority.Definitions.Where(x => x.Kind == kind))
            { writer.WriteStartObject(); writer.WriteString("definitionChecksum", Convert.ToHexString(item.DefinitionChecksum.AsSpan()).ToLowerInvariant());
              writer.WriteString("id", item.Id); writer.WriteString("kind", item.Kind.ToString()); writer.WriteString("owningModuleId", item.OwningModuleId);
              writer.WriteString("version", item.Version.ToString(CultureInfo.InvariantCulture)); writer.WriteEndObject(); }
            writer.WriteEndArray(); writer.Flush(); BaseStudioCanonicalJson value = BaseStudioCanonicalJson.Create(buffer.WrittenSpan, 1_048_576);
            return BaseStudioObservationJson.Current(resource, BaseStudioGraphObservationAuthority.Create(invocation.Authority), value,
                [], [], Accounting(value.ToArray().Length), 1_048_576);
        }
    }

    private sealed class AtomicExecutionProducer(IBaseStudioPrincipalContextResolver principals, BaseStudioAuthorization authorization,
        ImmutableArray<BaseStudioGrantRequirement> grants, IBaseStudioControlInspectionStore control)
        : ProducerBase(principals, authorization, grants), IBaseStudioViewProducer
    {
        public async ValueTask<BaseStudioCanonicalJson?> ReadAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) ||
                !RequestResource(invocation.Request, BaseStudioResourceKind.Application, out BaseStudioResourceIdentity? resource) || resource is null) return null;
            OperationResult<BaseStudioControlInspectionPage> result = await control.ReadStudioControlFactsAsync(new()
            { ApplicationId = resource.ApplicationId, Kind = BaseStudioControlFactKind.AtomicReceipt, Take = 500,
              ProtectedScopeChecksum = [.. invocation.Authority.ProtectedScopeChecksum.ToArray()], Limits = new()
              { MaximumItems = 500, MaximumRowsRead = 100_000, MaximumEvidenceBytes = 8_388_608,
                MaximumTransientBytes = 8_388_608, Deadline = TimeSpan.FromSeconds(5) } }, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess() || result.Value is null) return null;
            var buffer = new ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer); writer.WriteStartArray();
            foreach (BaseStudioAtomicReceiptFact item in result.Value.Items.Cast<BaseStudioAtomicReceiptFact>())
            { writer.WriteStartObject(); writer.WriteString("expiresAtUtc", BaseStudioResponseAuthority.CanonicalUtc(item.ExpiresAtUtc)); writer.WriteString("identity", item.Identity);
              writer.WriteString("requestFingerprint", Convert.ToHexString(item.RequestFingerprint.AsSpan()).ToLowerInvariant()); writer.WriteString("resultKind", item.ResultKind.ToString());
              writer.WriteString("structuralDigest", Convert.ToHexString(item.StructuralDigest.AsSpan()).ToLowerInvariant()); writer.WriteEndObject(); }
            writer.WriteEndArray(); writer.Flush(); BaseStudioCanonicalJson value = BaseStudioCanonicalJson.Create(buffer.WrittenSpan, 1_048_576);
            return BaseStudioObservationJson.Current(resource, BaseStudioGraphObservationAuthority.Create(invocation.Authority), value,
                [], [], Accounting(value.ToArray().Length), 1_048_576);
        }
    }

    private sealed class OperationExecutionResolver(IBaseStudioPrincipalContextResolver principals, BaseStudioAuthorization authorization,
        ImmutableArray<BaseStudioGrantRequirement> grants, IBaseStudioControlInspectionStore control)
        : ProducerBase(principals, authorization, grants), IBaseStudioResourceProducer
    {
        public async ValueTask<BaseStudioCanonicalJson?> ResolveAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) || !Token(invocation.Request, out BaseStudioResourceIdentity? decoded) ||
                decoded is not BaseStudioOperationExecutionResource resource || !StringComparer.Ordinal.Equals(resource.ApplicationId, invocation.Bootstrap.ApplicationGraph.ApplicationId)) return null;
            using JsonDocument document = JsonDocument.Parse(invocation.Request.ToArray());
            string token = document.RootElement.GetProperty("resourceToken").GetString()!;
            OperationResult<BaseStudioControlInspectionPage> fact = await control.ReadStudioControlFactsAsync(new()
            { ApplicationId = resource.ApplicationId, Kind = BaseStudioControlFactKind.AtomicReceipt, Identity = resource.RequestIdentity, Take = 1,
              ProtectedScopeChecksum = [.. invocation.Authority.ProtectedScopeChecksum.ToArray()], Limits = new()
              { MaximumItems = 1, MaximumRowsRead = 2, MaximumEvidenceBytes = 65_536, MaximumTransientBytes = 65_536, Deadline = TimeSpan.FromSeconds(5) } }, cancellationToken).ConfigureAwait(false);
            if (!fact.IsSuccess() || fact.Value is null || fact.Value.Items.Length != 1) return null;
            return BaseStudioResolvedResourceJson.Encode(resource, BaseStudioResolvedRoute.Create("base.operation.execution",
                [new KeyValuePair<string, string>("resource", token)]), [], 1_048_576);
        }

    }
}
