using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using HPD.AI.Platform.Studio;

namespace HPD.Base.Studio;

internal sealed partial class HPDBaseStudioRuntimeContributionFactory
{
    private void AddOperationsRuntime(StudioModuleRegistration module,
        List<StudioNamedTypeContract> types, List<StudioEndpointContract> endpoints,
        List<StudioMethodBinding> methods, List<StudioProducerBinding> producers,
        StudioNamedTypeContract error, StudioNamedTypeContract text, StudioNamedTypeContract checksum,
        StudioNamedTypeContract currentKind, StudioNamedTypeContract graphAuthority,
        StudioNamedTypeContract accounting, StudioNamedTypeContract emptyItems,
        StudioNamedTypeContract tokenRequest, StudioNamedTypeContract resourceParameters,
        StudioNamedTypeContract resourceRoute, StudioNamedTypeContract resolvedKind)
    {
        StudioNamedTypeContract operationKind = Type("base.studio.resource-kind.operationexecution", "{\"kind\":\"literal\",\"value\":\"operationExecution\"}");
        StudioNamedTypeContract operationResource = Type("base.studio.resource.operationexecution", Obj(
            P("applicationId", text), P("authorityChecksum", checksum), P("kind", operationKind),
            P("operationId", text), P("operationKind", text), P("requestIdentity", text)));
        StudioNamedTypeContract definitionItem = Type("base.studio.installed-definition.item",
            "{\"kind\":\"object\",\"properties\":[{\"name\":\"definitionChecksum\",\"wireName\":\"definitionChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"id\",\"wireName\":\"id\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"kind\",\"wireName\":\"kind\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"owningModuleId\",\"wireName\":\"owningModuleId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"version\",\"wireName\":\"version\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}");
        StudioNamedTypeContract semanticDefinitionItem = Type("base.studio.semantic-definition-inspection.item",
            "{\"kind\":\"object\",\"properties\":[{\"name\":\"capturedAuthorityGeneration\",\"wireName\":\"capturedAuthorityGeneration\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"},{\"name\":\"compactedCount\",\"wireName\":\"compactedCount\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"definitionChecksum\",\"wireName\":\"definitionChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"hasMore\",\"wireName\":\"hasMore\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"id\",\"wireName\":\"id\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"inspectionState\",\"wireName\":\"inspectionState\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"kind\",\"wireName\":\"kind\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"liveCount\",\"wireName\":\"liveCount\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"owningModuleId\",\"wireName\":\"owningModuleId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"pageChecksum\",\"wireName\":\"pageChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"},{\"name\":\"retiredCount\",\"wireName\":\"retiredCount\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"storeId\",\"wireName\":\"storeId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"version\",\"wireName\":\"version\",\"typeId\":\"base.studio.nonnegative-long\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}");
        StudioNamedTypeContract executionItem = Type("base.studio.atomic-execution.item",
            "{\"kind\":\"object\",\"properties\":[{\"name\":\"expiresAtUtc\",\"wireName\":\"expiresAtUtc\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"identity\",\"wireName\":\"identity\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"requestFingerprint\",\"wireName\":\"requestFingerprint\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"resultKind\",\"wireName\":\"resultKind\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"structuralDigest\",\"wireName\":\"structuralDigest\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}");
        StudioNamedTypeContract definitionItems = Type("base.studio.installed-definition.items", "{\"kind\":\"array\",\"elementTypeId\":\"base.studio.installed-definition.item\",\"minItems\":0,\"maxItems\":500}");
        StudioNamedTypeContract semanticDefinitionItems = Type("base.studio.semantic-definition-inspection.items", "{\"kind\":\"array\",\"elementTypeId\":\"base.studio.semantic-definition-inspection.item\",\"minItems\":0,\"maxItems\":500}");
        StudioNamedTypeContract executionItems = Type("base.studio.atomic-execution.items", "{\"kind\":\"array\",\"elementTypeId\":\"base.studio.atomic-execution.item\",\"minItems\":0,\"maxItems\":500}");
        StudioNamedTypeContract app = types.Single(static value => value.TypeId == "base.studio.resource.application");
        StudioNamedTypeContract definitionRequest = Type("base.operations.definitions.request", Obj(P("resource", app)));
        StudioNamedTypeContract executionRequest = Type("base.operations.executions.request", Obj(P("resource", app)));
        StudioNamedTypeContract definitionCurrent = Type("base.studio.installed-definition.current", Obj(P("accounting", accounting),
            P("evidence", emptyItems), P("kind", currentKind), P("links", emptyItems), P("observationAuthority", graphAuthority),
            P("resource", app), P("value", definitionItems)));
        StudioNamedTypeContract semanticDefinitionCurrent = Type("base.studio.semantic-definition-inspection.current", Obj(P("accounting", accounting),
            P("evidence", emptyItems), P("kind", currentKind), P("links", emptyItems), P("observationAuthority", graphAuthority),
            P("resource", app), P("value", semanticDefinitionItems)));
        StudioNamedTypeContract executionCurrent = Type("base.studio.atomic-execution.current", Obj(P("accounting", accounting),
            P("evidence", emptyItems), P("kind", currentKind), P("links", emptyItems), P("observationAuthority", graphAuthority),
            P("resource", app), P("value", executionItems)));
        StudioNamedTypeContract resolved = Type("base.studio.operationexecution-resolved", Obj(P("kind", resolvedKind),
            P("links", emptyItems), P("resource", operationResource), P("route", resourceRoute)));
        types.AddRange([operationKind, operationResource, definitionItem, semanticDefinitionItem, executionItem, definitionItems,
            semanticDefinitionItems, executionItems, definitionRequest, executionRequest, definitionCurrent,
            semanticDefinitionCurrent, executionCurrent, resolved]);

        foreach ((string viewId, HPDBaseStudioDefinitionKind kind) in new[]
        {
            ("base.operations.definitions.registeredReads.list", HPDBaseStudioDefinitionKind.RegisteredRead),
            ("base.operations.definitions.selectionOperations.list", HPDBaseStudioDefinitionKind.SelectionMutation),
            ("base.operations.definitions.moduleMutations.list", HPDBaseStudioDefinitionKind.ModuleMutation),
            ("base.operations.definitions.semanticActivations.list", HPDBaseStudioDefinitionKind.SemanticActivation),
        }) AddView(viewId, definitionRequest, definitionCurrent,
            new InstalledDefinitionProducer(_principals, _authorization,
                module.Pages.Single(static x => x.PageId == "base.operations").Grants, _baseAuthority, kind, null, null));
        AddView("base.semanticActivations.definitions.list", definitionRequest, semanticDefinitionCurrent,
            new InstalledDefinitionProducer(_principals, _authorization,
                module.Pages.Single(static x => x.PageId == "base.semanticActivations").Grants,
                _baseAuthority, HPDBaseStudioDefinitionKind.SemanticActivation, _stores, _administration));
        AddView("base.operations.executions.list", executionRequest, executionCurrent,
            new AtomicExecutionProducer(_principals, _authorization, module.Pages.Single(static x => x.PageId == "base.operations").Grants, _control));
        AddView("base.operations.receipts.list", executionRequest, executionCurrent,
            new AtomicExecutionProducer(_principals, _authorization, module.Pages.Single(static x => x.PageId == "base.operations").Grants, _control));

        const string resolverMethod = "base.studio.resolve.operationexecution";
        const string resolverEndpoint = "base.studio.resource.resolve.operationexecution";
        endpoints.Add(Endpoint(resolverEndpoint, "/base/studio/resources/operation-execution", tokenRequest, resolved));
        StudioResourceRegistration resource = module.Resources.Single(static x => x.Kind == StudioResourceKind.OperationExecution);
        methods.Add(StudioMethodBinding.Create(resolverMethod, StudioMethodKind.Resolve, "base", resource.ResolverId,
            resolverEndpoint, tokenRequest.TypeId, resolved.TypeId));
        producers.Add(new StudioResourceProducerBinding(resolverMethod,
            new OperationExecutionResolver(_principals, _authorization, resource.Grants, _control)));

        void AddView(string viewId, StudioNamedTypeContract request, StudioNamedTypeContract result, IStudioViewProducer producer)
        {
            StudioViewRegistration view = module.Views.Single(x => x.ViewId == viewId);
            if (!StudioSha256.FixedTimeEquals(view.RequestNodeChecksum, request.NodeChecksum) ||
                !StudioSha256.FixedTimeEquals(view.ItemNodeChecksum,
                    viewId == "base.semanticActivations.definitions.list" ? semanticDefinitionItem.NodeChecksum :
                    viewId.Contains("definitions", StringComparison.Ordinal) ? definitionItem.NodeChecksum : executionItem.NodeChecksum))
                throw new InvalidOperationException("The Operations view differs from its graph-owned L41 nodes.");
            string endpointId = "base.studio.view.page." + viewId;
            string methodId = "base.studio.view." + viewId;
            endpoints.Add(Endpoint(endpointId, "/base/studio/views/" + viewId, request, result));
            methods.Add(StudioMethodBinding.Create(methodId, StudioMethodKind.Page, "base",
                viewId.StartsWith("base.semanticActivations.", StringComparison.Ordinal) ? "base.semanticActivations" : "base.operations",
                endpointId, request.TypeId, result.TypeId));
            producers.Add(new StudioViewProducerBinding(methodId, producer));
        }
        StudioEndpointContract Endpoint(string id, string route, StudioNamedTypeContract request, StudioNamedTypeContract result)
            => StudioEndpointContract.Create(id, 1, StudioTransportMethod.Post, route, StudioEndpointAudience.ControlPlane,
                StudioTransportKind.SameOriginHttp, request.TypeId, request.NodeChecksum, result.TypeId, result.NodeChecksum,
                error.TypeId, error.NodeChecksum, 16_384, 1_048_576, TimeSpan.FromSeconds(10));
        static string P(string name, StudioNamedTypeContract type) => $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type.TypeId}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}";
        static string Obj(params string[] properties) => $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', properties)}],\"additionalProperties\":false}}";
    }

    private sealed class InstalledDefinitionProducer(IStudioPrincipalContextResolver principals, HPDBaseStudioAuthorization authorization,
        ImmutableArray<StudioGrantRequirement> grants, HPDBaseStudioAuthoritySnapshot authority, HPDBaseStudioDefinitionKind kind,
        IRecordStoreRegistry? stores, IHPDBaseAdministration? administration)
        : ProducerBase(principals, authorization, grants), IStudioViewProducer
    {
        public async ValueTask<StudioCanonicalJson?> ReadAsync(StudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) ||
                !RequestResource(invocation.Request, StudioResourceKind.Application, out StudioResourceIdentity? resource) || resource is null) return null;
            (PrincipalContext Principal, OperationContext Operation)? context = await ContextAsync(invocation, cancellationToken).ConfigureAwait(false);
            if (context is null) return null;
            var buffer = new ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer); writer.WriteStartArray();
            foreach (HPDBaseStudioDefinitionAuthority item in authority.Definitions.Where(x => x.Kind == kind))
            {
                if (stores is null || administration is null)
                {
                    writer.WriteStartObject(); writer.WriteString("definitionChecksum", Convert.ToHexString(item.DefinitionChecksum.AsSpan()).ToLowerInvariant());
                    writer.WriteString("id", item.Id); writer.WriteString("kind", item.Kind.ToString()); writer.WriteString("owningModuleId", item.OwningModuleId);
                    writer.WriteString("version", item.Version.ToString(CultureInfo.InvariantCulture)); writer.WriteEndObject(); continue;
                }
                RecordStoreRegistration[] registrations = stores?.GetRegistrations() ?? [];
                if (registrations.Length == 0)
                    WriteDefinition(writer, item, "unavailable", null, "unavailable");
                else foreach (RecordStoreRegistration registration in registrations.Take(64))
                {
                    BaseResult<BaseSemanticActivationInspectionPage> inspected = await administration.InspectSemanticActivationsAsync(
                        registration.StoreId, context.Value.Principal, new()
                        {
                            Definition = new() { Id = item.Id, Version = item.Version, Checksum = item.DefinitionChecksum },
                            State = null, After = null, Take = 256, Limits = SemanticInspectionLimits(),
                        }, cancellationToken).ConfigureAwait(false);
                    WriteDefinition(writer, item, registration.StoreId,
                        inspected.TryGetValue(out BaseSemanticActivationInspectionPage? page) ? page : null,
                        inspected.TryGetValue(out _) ? "current" : "unavailable");
                }
            }
            writer.WriteEndArray(); writer.Flush(); StudioCanonicalJson value = StudioCanonicalJson.Create(buffer.WrittenSpan, 1_048_576);
            return StudioObservationJson.Current(resource, StudioGraphObservationAuthority.Create(invocation.Authority), value,
                [], [], Accounting(value.ToArray().Length), 1_048_576);
        }

        private static BaseSemanticActivationExecutionLimits SemanticInspectionLimits() => new()
        {
            MaximumOperations = 1, MaximumScopeDirectoryReads = 256, MaximumSlotReads = 256,
            MaximumActivationReads = 256, MaximumReadIntervals = 4, MaximumIndexOperations = 4,
            MaximumActivationBytes = 1_048_576, MaximumScopeDirectoryBytes = 1_048_576,
            MaximumEvidenceBytes = 4_194_304, MaximumReceiptBytes = 1_048_576,
            MaximumTransientBytes = 8_388_608,
        };

        private static void WriteDefinition(Utf8JsonWriter writer, HPDBaseStudioDefinitionAuthority definition,
            string storeId, BaseSemanticActivationInspectionPage? page, string inspectionState)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("capturedAuthorityGeneration"); if (page is null) writer.WriteNullValue(); else writer.WriteStringValue(page.CapturedAuthorityGeneration.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("compactedCount", (page?.Items.Count(static value => value.State == BaseSemanticActivationSlotState.CompactedAbsent) ?? 0).ToString(CultureInfo.InvariantCulture));
            writer.WriteString("definitionChecksum", Convert.ToHexString(definition.DefinitionChecksum.AsSpan()).ToLowerInvariant());
            writer.WriteString("hasMore", page?.Next is null ? "false" : "true"); writer.WriteString("id", definition.Id);
            writer.WriteString("inspectionState", inspectionState); writer.WriteString("kind", definition.Kind.ToString());
            writer.WriteString("liveCount", (page?.Items.Count(static value => value.State == BaseSemanticActivationSlotState.Live) ?? 0).ToString(CultureInfo.InvariantCulture));
            writer.WriteString("owningModuleId", definition.OwningModuleId); writer.WritePropertyName("pageChecksum");
            if (page is null) writer.WriteNullValue(); else writer.WriteStringValue(Convert.ToHexString(page.Checksum.AsSpan()).ToLowerInvariant());
            writer.WriteString("retiredCount", (page?.Items.Count(static value => value.State == BaseSemanticActivationSlotState.Retired) ?? 0).ToString(CultureInfo.InvariantCulture));
            writer.WriteString("storeId", storeId); writer.WriteString("version", definition.Version.ToString(CultureInfo.InvariantCulture)); writer.WriteEndObject();
        }
    }

    private sealed class AtomicExecutionProducer(IStudioPrincipalContextResolver principals, HPDBaseStudioAuthorization authorization,
        ImmutableArray<StudioGrantRequirement> grants, IStudioControlInspectionStore control)
        : ProducerBase(principals, authorization, grants), IStudioViewProducer
    {
        public async ValueTask<StudioCanonicalJson?> ReadAsync(StudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) ||
                !RequestResource(invocation.Request, StudioResourceKind.Application, out StudioResourceIdentity? resource) || resource is null) return null;
            OperationResult<StudioControlInspectionPage> result = await control.ReadStudioControlFactsAsync(new()
            { ApplicationId = resource.ApplicationId, Kind = StudioControlFactKind.AtomicReceipt, Take = 500,
              ProtectedScopeChecksum = [.. invocation.Authority.ProtectedScopeChecksum.ToArray()], Limits = new()
              { MaximumItems = 500, MaximumRowsRead = 100_000, MaximumEvidenceBytes = 8_388_608,
                MaximumTransientBytes = 8_388_608, Deadline = TimeSpan.FromSeconds(5) } }, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess() || result.Value is null) return null;
            var buffer = new ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer); writer.WriteStartArray();
            foreach (StudioAtomicReceiptFact item in result.Value.Items.Cast<StudioAtomicReceiptFact>())
            { writer.WriteStartObject(); writer.WriteString("expiresAtUtc", StudioResponseAuthority.CanonicalUtc(item.ExpiresAtUtc)); writer.WriteString("identity", item.Identity);
              writer.WriteString("requestFingerprint", Convert.ToHexString(item.RequestFingerprint.AsSpan()).ToLowerInvariant()); writer.WriteString("resultKind", item.ResultKind.ToString());
              writer.WriteString("structuralDigest", Convert.ToHexString(item.StructuralDigest.AsSpan()).ToLowerInvariant()); writer.WriteEndObject(); }
            writer.WriteEndArray(); writer.Flush(); StudioCanonicalJson value = StudioCanonicalJson.Create(buffer.WrittenSpan, 1_048_576);
            return StudioObservationJson.Current(resource, StudioGraphObservationAuthority.Create(invocation.Authority), value,
                [], [], Accounting(value.ToArray().Length), 1_048_576);
        }
    }

    private sealed class OperationExecutionResolver(IStudioPrincipalContextResolver principals, HPDBaseStudioAuthorization authorization,
        ImmutableArray<StudioGrantRequirement> grants, IStudioControlInspectionStore control)
        : ProducerBase(principals, authorization, grants), IStudioResourceProducer
    {
        public async ValueTask<StudioCanonicalJson?> ResolveAsync(StudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) || !Token(invocation.Request, out StudioResourceIdentity? decoded) ||
                decoded is not StudioOperationExecutionResource resource || !StringComparer.Ordinal.Equals(resource.ApplicationId, invocation.Bootstrap.ApplicationGraph.ApplicationId)) return null;
            using JsonDocument document = JsonDocument.Parse(invocation.Request.ToArray());
            string token = document.RootElement.GetProperty("resourceToken").GetString()!;
            OperationResult<StudioControlInspectionPage> fact = await control.ReadStudioControlFactsAsync(new()
            { ApplicationId = resource.ApplicationId, Kind = StudioControlFactKind.AtomicReceipt, Identity = resource.RequestIdentity, Take = 1,
              ProtectedScopeChecksum = [.. invocation.Authority.ProtectedScopeChecksum.ToArray()], Limits = new()
              { MaximumItems = 1, MaximumRowsRead = 2, MaximumEvidenceBytes = 65_536, MaximumTransientBytes = 65_536, Deadline = TimeSpan.FromSeconds(5) } }, cancellationToken).ConfigureAwait(false);
            if (!fact.IsSuccess() || fact.Value is null || fact.Value.Items.Length != 1) return null;
            return StudioResolvedResourceJson.Encode(resource, StudioResolvedRoute.Create("base.operation.execution",
                [new KeyValuePair<string, string>("resource", token)]), [], 1_048_576);
        }

    }
}
