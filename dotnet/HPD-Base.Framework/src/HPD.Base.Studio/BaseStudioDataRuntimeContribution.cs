using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using HPD.AI.Platform.Studio;

namespace HPD.Base.Studio;

internal sealed partial class BaseStudioRuntimeContributionFactory
{
    private static readonly string[] DataPages =
    [
        "base.data", "base.module.detail", "base.collection.records"
    ];
    private static readonly string[] EvidenceViews =
    [
        "base.collection.detail.history.list", "base.record.detail.history.list"
    ];

    private void AddDataRuntime(BaseStudioModuleRegistration module, List<BaseStudioNamedTypeContract> types,
        List<BaseStudioEndpointContract> endpoints, List<BaseStudioMethodBinding> methods,
        List<BaseStudioProducerBinding> producers, BaseStudioNamedTypeContract error, BaseStudioNamedTypeContract text,
        BaseStudioNamedTypeContract optionalText, BaseStudioNamedTypeContract checksum, BaseStudioNamedTypeContract decimalLong,
        BaseStudioNamedTypeContract currentKind, BaseStudioNamedTypeContract graphAuthority, BaseStudioNamedTypeContract accounting,
        BaseStudioNamedTypeContract emptyItems, BaseStudioNamedTypeContract emptyMap, BaseStudioNamedTypeContract tokenRequest,
        BaseStudioNamedTypeContract resourceParameters, BaseStudioNamedTypeContract resourceRoute,
        BaseStudioNamedTypeContract resolvedKind)
    {
        BaseStudioNamedTypeContract boolean = Type("base.studio.boolean", "{\"kind\":\"boolean\"}");
        BaseStudioNamedTypeContract moduleKind = Type("base.studio.resource-kind.module", "{\"kind\":\"literal\",\"value\":\"module\"}");
        BaseStudioNamedTypeContract moduleIdentity = Type("base.studio.resource.module", Obj(
            P("applicationId", text), P("authorityChecksum", checksum), P("kind", moduleKind), P("moduleId", text), P("moduleVersion", decimalLong)));
        BaseStudioNamedTypeContract moduleResolved = Type("base.studio.module-resolved", Obj(P("kind", resolvedKind),
            P("links", emptyItems), P("resource", moduleIdentity), P("route", resourceRoute)));
        types.AddRange([boolean, moduleKind, moduleIdentity, moduleResolved]);

        BaseStudioResourceRegistration moduleResource = module.Resources.Single(static value => value.Kind == BaseStudioResourceKind.Module);
        string moduleResolverMethod = "base.studio.resolve.module";
        string moduleResolverEndpoint = ResolveEndpoint + ".module";
        endpoints.Add(Endpoint(moduleResolverEndpoint, "/base/studio/resources/module", tokenRequest, moduleResolved));
        methods.Add(BaseStudioMethodBinding.Create(moduleResolverMethod, BaseStudioMethodKind.Resolve, "base", moduleResource.ResolverId,
            moduleResolverEndpoint, tokenRequest.TypeId, moduleResolved.TypeId));
        producers.Add(new BaseStudioResourceProducerBinding(moduleResolverMethod,
            new ModuleProducer(_principals, _authorization, moduleResource.Grants)));

        foreach (BaseStudioPageRegistration page in module.Pages.Where(page => DataPages.Contains(page.PageId, StringComparer.Ordinal)))
        {
            BaseStudioResourceRegistration resource = module.Resources.Single(value => value.Kind == page.AcceptedResources[0]);
            foreach (BaseStudioSectionRegistration section in page.Presentation.Sections)
            foreach (string viewId in section.ViewIds)
            {
                BaseStudioViewRegistration view = module.Views.Single(value => value.ViewId == viewId);
                BaseStudioNamedTypeContract request = Type(view.RequestNodeId, DataRequestDescriptor(page.AcceptedResources[0]));
                if (!BaseStudioSha256.FixedTimeEquals(request.NodeChecksum, view.RequestNodeChecksum))
                    throw new InvalidOperationException("A BASE data-view request differs from its graph-owned L41 node.");
                BaseStudioNamedTypeContract item = Type(view.ItemNodeId,
                    "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":256,\"format\":\"studio-resource-summary\"}");
                if (!BaseStudioSha256.FixedTimeEquals(item.NodeChecksum, view.ItemNodeChecksum))
                    throw new InvalidOperationException("A BASE data-view item differs from its graph-owned L41 node.");
                bool list = viewId.EndsWith(".list", StringComparison.Ordinal);
                BaseStudioNamedTypeContract value = list
                    ? Type(viewId + ".items", $"{{\"kind\":\"array\",\"elementTypeId\":\"{item.TypeId}\",\"minItems\":0,\"maxItems\":{view.MaximumItems.ToString(CultureInfo.InvariantCulture)}}}")
                    : item;
                BaseStudioNamedTypeContract result = Type(viewId + ".current", Obj(P("accounting", accounting), P("evidence", emptyItems),
                    P("kind", currentKind), P("links", emptyItems), P("observationAuthority", graphAuthority),
                    P("resource", ResourceType(page.AcceptedResources[0], moduleIdentity, types)), P("value", value)));
                types.Add(request); types.Add(item); if (list) types.Add(value); types.Add(result);
                string methodId = "base.studio.view." + viewId;
                string endpointId = PageEndpoint + "." + viewId;
                endpoints.Add(Endpoint(endpointId, "/base/studio/views/" + viewId, request, result));
                methods.Add(BaseStudioMethodBinding.Create(methodId, BaseStudioMethodKind.Page, "base", page.PageId,
                    endpointId, request.TypeId, result.TypeId));
                producers.Add(new BaseStudioViewProducerBinding(methodId, new DataViewProducer(_schema, _records, _files,
                    _principals, _authorization, resource.Grants, _baseAuthority, viewId, page.AcceptedResources[0], list)));
            }
        }

        foreach (BaseStudioPageRegistration page in module.Pages.Where(page => page.PageId is "base.collection.detail" or "base.record.detail"))
        foreach (BaseStudioSectionRegistration section in page.Presentation.Sections)
        foreach (string viewId in section.ViewIds.Where(EvidenceViews.Contains))
        {
            BaseStudioViewRegistration view = module.Views.Single(value => value.ViewId == viewId);
            BaseStudioNamedTypeContract request = Type(view.RequestNodeId, DataRequestDescriptor(page.AcceptedResources[0]));
            BaseStudioNamedTypeContract item = Type("base.studio.evidence.record-mutation.item",
                Obj(P("collectionId", text), P("evidenceChecksum", checksum), P("evidenceId", text), P("observedAtUtc", text),
                    P("recordId", text), P("semanticKind", text)));
            if (!BaseStudioSha256.FixedTimeEquals(request.NodeChecksum, view.RequestNodeChecksum) ||
                !BaseStudioSha256.FixedTimeEquals(item.NodeChecksum, view.ItemNodeChecksum))
                throw new InvalidOperationException("A BASE evidence view differs from its graph-owned L41 node.");
            bool list = viewId.EndsWith(".list", StringComparison.Ordinal);
            BaseStudioNamedTypeContract value = list ? Type(viewId + ".items",
                $"{{\"kind\":\"array\",\"elementTypeId\":\"{item.TypeId}\",\"minItems\":0,\"maxItems\":{view.MaximumItems.ToString(CultureInfo.InvariantCulture)}}}") : item;
            BaseStudioNamedTypeContract result = Type(viewId + ".current", Obj(P("accounting", accounting), P("evidence", emptyItems),
                P("kind", currentKind), P("links", emptyItems), P("observationAuthority", graphAuthority),
                P("resource", ResourceType(page.AcceptedResources[0], moduleIdentity, types)), P("value", value)));
            types.Add(request); if (!types.Any(value => StringComparer.Ordinal.Equals(value.TypeId, item.TypeId))) types.Add(item);
            if (list) types.Add(value); types.Add(result);
            string methodId = "base.studio.view." + viewId; string endpointId = PageEndpoint + "." + viewId;
            endpoints.Add(Endpoint(endpointId, "/base/studio/views/" + viewId, request, result));
            methods.Add(BaseStudioMethodBinding.Create(methodId, BaseStudioMethodKind.Page, "base", page.PageId,
                endpointId, request.TypeId, result.TypeId));
            BaseStudioResourceRegistration resource = module.Resources.Single(value => value.Kind == page.AcceptedResources[0]);
            producers.Add(new BaseStudioViewProducerBinding(methodId, new EvidenceViewProducer(_stores, _evidence, _principals,
                _authorization, resource.Grants, _baseAuthority, page.AcceptedResources[0])));
        }

        BaseStudioEndpointContract Endpoint(string id, string route, BaseStudioNamedTypeContract request, BaseStudioNamedTypeContract result)
            => BaseStudioEndpointContract.Create(id, 1, BaseStudioTransportMethod.Post, route,
                BaseStudioEndpointAudience.ControlPlane, BaseStudioTransportKind.SameOriginHttp,
                request.TypeId, request.NodeChecksum, result.TypeId, result.NodeChecksum, error.TypeId, error.NodeChecksum,
                16_384, 1_048_576, TimeSpan.FromSeconds(10));
        static string P(string name, BaseStudioNamedTypeContract type) => $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type.TypeId}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}";
        static string Obj(params string[] properties) => $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', properties)}],\"additionalProperties\":false}}";
    }

    private sealed class EvidenceViewProducer(IRecordStoreRegistry stores, IBaseStudioEvidenceRuntime evidence,
        IBaseStudioPrincipalContextResolver principals, BaseStudioAuthorization authorization,
        ImmutableArray<BaseStudioGrantRequirement> grants, HPDBaseStudioAuthoritySnapshot baseAuthority,
        BaseStudioResourceKind resourceKind)
        : ProducerBase(principals, authorization, grants), IBaseStudioViewProducer
    {
        public async ValueTask<BaseStudioCanonicalJson?> ReadAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) ||
                !RequestResource(invocation.Request, resourceKind, out BaseStudioResourceIdentity? resource) || resource is null ||
                !StringComparer.Ordinal.Equals(resource.ApplicationId, invocation.Bootstrap.ApplicationGraph.ApplicationId)) return null;
            string collectionId; BaseStudioEvidenceSubject parent;
            if (resource is BaseStudioCollectionResource collection)
            { collectionId = collection.CollectionId; parent = new BaseStudioCollectionEvidenceSubject { CollectionId = collectionId,
                InstalledCollectionChecksum = [.. collection.InstalledCollectionChecksum.ToArray()] }; }
            else if (resource is BaseStudioRecordResource record)
            { collectionId = record.CollectionId; parent = new BaseStudioRecordEvidenceSubject { CollectionId = collectionId,
                InstalledCollectionChecksum = [.. record.InstalledCollectionChecksum.ToArray()], RecordId = RecordId.Parse(record.RecordId) }; }
            else return null;
            byte[]? expected = baseAuthority.GetInstalledCollectionChecksum(collectionId);
            if (expected is null || parent is BaseStudioCollectionEvidenceSubject c && !c.InstalledCollectionChecksum.AsSpan().SequenceEqual(expected) ||
                parent is BaseStudioRecordEvidenceSubject r && !r.InstalledCollectionChecksum.AsSpan().SequenceEqual(expected)) return null;
            if (stores.GetStoreForCollection(collectionId) is not IBaseStudioEvidenceStore provider) return null;
            ImmutableArray<byte> scopeChecksum = [.. invocation.Bootstrap.Authorization.Session.ProtectedScopeChecksum.ToArray()];
            BaseOwnedSubjectScopeEvidence? authorizedScope = await ScopeAsync(invocation, cancellationToken).ConfigureAwait(false);
            if (authorizedScope is null || authorizedScope.Kind switch
                { BaseSubjectScopeKind.Global => authorizedScope.Value is not null,
                  BaseSubjectScopeKind.Tenant or BaseSubjectScopeKind.Project => string.IsNullOrWhiteSpace(authorizedScope.Value), _ => true }) return null;
            BaseOwnedSubjectScopeEvidence exactScope = authorizedScope;
            var requirement = new BaseStudioEvidenceRequirement { ApplicationId = resource.ApplicationId, Kind = BaseStudioEvidenceKind.RecordMutation,
                Parent = parent, Scope = exactScope, ProtectedScopeSeekChecksum = scopeChecksum, Limits = new BaseStudioEvidenceLimits { MaximumItems = 100,
                    MaximumRowsRead = 101, MaximumIntervals = 1, MaximumEvidenceBytes = 524_288, MaximumTransientBytes = 524_288,
                    AcquisitionDeadline = TimeSpan.FromSeconds(2), SessionDeadline = TimeSpan.FromSeconds(5), PageDeadline = TimeSpan.FromSeconds(3) } };
            OperationResult<BaseStudioEvidencePage> result = await evidence.ReadPageAsync(provider, requirement,
                new BaseOwnedScopeSeekAuthority { Kind = exactScope.Kind, ProtectedIndexDigest = scopeChecksum },
                new BaseStudioEvidencePageRequest { Take = 100 }, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess() || result.Value is null) return null;
            BaseStudioCanonicalJson projected = EvidenceJson(result.Value.Items);
            return BaseStudioObservationJson.Current(resource, BaseStudioGraphObservationAuthority.Create(invocation.Authority), projected,
                [], [], Accounting(projected.ToArray().Length), 1_048_576);
        }

        private static BaseStudioCanonicalJson EvidenceJson(ImmutableArray<BaseStudioEvidenceItem> items)
        {
            var buffer = new ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer);
            writer.WriteStartArray();
            foreach (BaseStudioEvidenceItem item in items)
            { if (item is not BaseStudioRecordMutationEvidenceItem mutation) throw new InvalidOperationException("The evidence item kind is invalid.");
              writer.WriteStartObject(); writer.WriteString("collectionId", mutation.CollectionId); writer.WriteString("evidenceChecksum", Convert.ToHexString(item.EvidenceChecksum.AsSpan()).ToLowerInvariant());
              writer.WriteString("evidenceId", mutation.EvidenceId); writer.WriteString("observedAtUtc", item.ObservedAtUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
              writer.WriteString("recordId", mutation.RecordId.Value); writer.WriteString("semanticKind", item.SemanticKind.ToString()); writer.WriteEndObject(); }
            writer.WriteEndArray();
            writer.Flush(); return BaseStudioCanonicalJson.Create(buffer.WrittenSpan, 1_048_576);
        }
    }

    private static BaseStudioNamedTypeContract ResourceType(BaseStudioResourceKind kind,
        BaseStudioNamedTypeContract module, List<BaseStudioNamedTypeContract> types) => kind switch
    {
        BaseStudioResourceKind.Application => types.Single(static value => value.TypeId == "base.studio.resource.application"),
        BaseStudioResourceKind.Module => module,
        BaseStudioResourceKind.Collection => types.Single(static value => value.TypeId == "base.studio.resource.collection"),
        BaseStudioResourceKind.Record => types.Single(static value => value.TypeId == "base.studio.resource.record"),
        _ => throw new InvalidOperationException("The BASE data page resource kind is invalid."),
    };

    private static string DataRequestDescriptor(BaseStudioResourceKind kind)
    {
        string type = kind switch
        {
            BaseStudioResourceKind.Application => "base.studio.resource.application",
            BaseStudioResourceKind.Module => "base.studio.resource.module",
            BaseStudioResourceKind.Collection => "base.studio.resource.collection",
            BaseStudioResourceKind.Record => "base.studio.resource.record",
            _ => throw new InvalidOperationException("The BASE data page request kind is invalid."),
        };
        return $"{{\"kind\":\"object\",\"properties\":[{{\"name\":\"resource\",\"wireName\":\"resource\",\"typeId\":\"{type}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}],\"additionalProperties\":false}}";
    }

    private sealed class ModuleProducer(IBaseStudioPrincipalContextResolver principals, BaseStudioAuthorization authorization,
        ImmutableArray<BaseStudioGrantRequirement> grants) : ProducerBase(principals, authorization, grants), IBaseStudioResourceProducer
    {
        public async ValueTask<BaseStudioCanonicalJson?> ResolveAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) ||
                !Token(invocation.Request, out BaseStudioResourceIdentity? decoded) || decoded is not BaseStudioModuleResource resource ||
                !StringComparer.Ordinal.Equals(resource.ApplicationId, invocation.Bootstrap.ApplicationGraph.ApplicationId)) return null;
            BaseStudioModuleRegistration? installed = invocation.Bootstrap.ApplicationGraph.Modules.SingleOrDefault(value =>
                StringComparer.Ordinal.Equals(value.Identity.ModuleId, resource.ModuleId) && value.Identity.Version == resource.ModuleVersion);
            if (installed is null) return null;
            return BaseStudioResolvedResourceJson.Encode(resource, BaseStudioResolvedRoute.Create("base.module.detail",
                [new KeyValuePair<string, string>("resource", BaseStudioResourceRouteToken.Encode(resource))]), [], 1_048_576);
        }
    }

    private sealed class DataViewProducer(IBaseSchemaProvider schema, IBaseRecordRuntime records, IFileBucketRegistry? files,
        IBaseStudioPrincipalContextResolver principals, BaseStudioAuthorization authorization,
        ImmutableArray<BaseStudioGrantRequirement> grants, HPDBaseStudioAuthoritySnapshot baseAuthority,
        string viewId, BaseStudioResourceKind resourceKind, bool list)
        : ProducerBase(principals, authorization, grants), IBaseStudioViewProducer
    {
        public async ValueTask<BaseStudioCanonicalJson?> ReadAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) ||
                !RequestResource(invocation.Request, resourceKind, out BaseStudioResourceIdentity? resource) || resource is null ||
                !StringComparer.Ordinal.Equals(resource.ApplicationId, invocation.Bootstrap.ApplicationGraph.ApplicationId)) return null;
            var context = await ContextAsync(invocation, cancellationToken).ConfigureAwait(false); if (context is null) return null;
            string[]? values = await ValuesAsync(invocation, resource, context.Value, cancellationToken).ConfigureAwait(false);
            if (values is null || values.Length > 500 || values.Any(static value => string.IsNullOrWhiteSpace(value) || value.Length > 256)) return null;
            BaseStudioCanonicalJson projected = EncodeValues(values, list);
            return BaseStudioObservationJson.Current(resource, BaseStudioGraphObservationAuthority.Create(invocation.Authority),
                projected, [], [], Accounting(projected.ToArray().Length), 1_048_576);
        }

        private async ValueTask<string[]?> ValuesAsync(BaseStudioProducerInvocation invocation, BaseStudioResourceIdentity resource,
            (PrincipalContext Principal, OperationContext Operation) context, CancellationToken cancellationToken)
        {
            if (resource is BaseStudioApplicationResource)
            {
                OperationResult<SchemaMetadata> schemaResult = await schema.GetSchemaAsync(context.Principal, context.Operation,
                    VisibilityLevel.Admin, cancellationToken).ConfigureAwait(false);
                if (!schemaResult.IsSuccess() || schemaResult.Value is null) return null;
                SchemaMetadata metadata = schemaResult.Value;
                if (viewId.EndsWith(".modules.list", StringComparison.Ordinal))
                    return invocation.Bootstrap.ApplicationGraph.Modules.Select(static value => $"{value.Identity.ModuleId}@{value.Identity.Version}").Take(64).ToArray();
                if (viewId.EndsWith(".collections.list", StringComparison.Ordinal))
                    return (metadata.Collections ?? []).Select(static value => value.DisplayName ?? value.Name).Take(500).ToArray();
                if (viewId.EndsWith(".files.list", StringComparison.Ordinal))
                    return files is null ? [] : (await files.ListAsync(cancellationToken).ConfigureAwait(false)).Where(static value => value.DescriptorVisibility == VisibilityLevel.Admin)
                        .Select(static value => value.DisplayName ?? value.BucketId.Value).Take(500).ToArray();
                return [$"{metadata.RuntimeId} · contract {metadata.ContractVersion}"];
            }
            if (resource is BaseStudioModuleResource module)
            {
                BaseStudioModuleRegistration? installed = invocation.Bootstrap.ApplicationGraph.Modules.SingleOrDefault(value =>
                    StringComparer.Ordinal.Equals(value.Identity.ModuleId, module.ModuleId) && value.Identity.Version == module.ModuleVersion);
                if (installed is null) return null;
                if (viewId.EndsWith(".resources.list", StringComparison.Ordinal)) return installed.Resources.Select(static value => value.Kind.ToString()).ToArray();
                if (viewId.EndsWith(".operations.list", StringComparison.Ordinal)) return installed.Commands.Select(static value => value.CommandId).ToArray();
                return [$"{installed.Identity.ModuleId}@{installed.Identity.Version}"];
            }
            if (resource is BaseStudioCollectionResource collection)
            {
                byte[]? expected = baseAuthority.GetInstalledCollectionChecksum(collection.CollectionId);
                if (expected is null || !BaseStudioSha256.FixedTimeEquals(collection.InstalledCollectionChecksum, BaseStudioSha256.FromDigest(expected))) return null;
                OperationResult<CollectionDefinition> result = await schema.GetCollectionAsync(collection.CollectionId, context.Principal,
                    context.Operation with { CollectionId = collection.CollectionId }, VisibilityLevel.Admin, cancellationToken).ConfigureAwait(false);
                if (!result.IsSuccess() || result.Value is null) return null;
                CollectionDefinition definition = result.Value;
                if (viewId.EndsWith(".records.list", StringComparison.Ordinal))
                {
                    OperationResult<RecordPage> page = await records.ListAsync(collection.CollectionId,
                        new RecordQuery { Page = new QueryPage { Mode = QueryPaginationMode.Cursor, Limit = 100 }, Count = QueryCountMode.None },
                        context.Principal, context.Operation with { CollectionId = collection.CollectionId }, cancellationToken).ConfigureAwait(false);
                    return !page.IsSuccess() || page.Value is null ? null : page.Value.Items.Select(static value => value.Id.Value).ToArray();
                }
                if (viewId.EndsWith(".relations.list", StringComparison.Ordinal)) return (definition.Fields ?? []).Where(static value => value.Relation is not null).Select(static value => value.Id).ToArray();
                if (viewId.EndsWith(".indexes.list", StringComparison.Ordinal)) return (definition.Indexes ?? []).Select(static value => value.Id).Concat((definition.VectorIndexes ?? []).Select(static value => value.Id)).ToArray();
                if (viewId.EndsWith(".operations.list", StringComparison.Ordinal)) return OperationNames(definition);
                if (viewId.EndsWith(".history.list", StringComparison.Ordinal)) return [];
                if (viewId.EndsWith(".filters.list", StringComparison.Ordinal)) return ["installed filters only"];
                if (viewId.EndsWith(".paging.list", StringComparison.Ordinal)) return ["cursor · maximum 100"];
                return [$"{definition.DisplayName ?? definition.Name} · {definition.Kind}"];
            }
            if (resource is BaseStudioRecordResource record)
            {
                byte[]? expected = baseAuthority.GetInstalledCollectionChecksum(record.CollectionId);
                if (expected is null || !BaseStudioSha256.FixedTimeEquals(record.InstalledCollectionChecksum, BaseStudioSha256.FromDigest(expected))) return null;
                OperationResult<RecordEnvelope> result = await records.GetAsync(record.CollectionId, RecordId.Parse(record.RecordId), context.Principal,
                    context.Operation with { CollectionId = record.CollectionId }, cancellationToken).ConfigureAwait(false);
                if (!result.IsSuccess() || result.Value is null) return null;
                RecordEnvelope envelope = result.Value;
                if (viewId.EndsWith(".fields.detail", StringComparison.Ordinal))
                    return [envelope.Payload.Kind == RecordPayloadKind.Json ? envelope.Payload.Json.GetRawText() :
                        string.Join(", ", envelope.Payload.Fields is null ? Array.Empty<string>() : envelope.Payload.Fields.Keys)];
                if (viewId.EndsWith(".relations.detail", StringComparison.Ordinal) || viewId.EndsWith(".references.list", StringComparison.Ordinal)) return ["none disclosed"];
                if (viewId.EndsWith(".history.list", StringComparison.Ordinal) || viewId.EndsWith(".receipts.list", StringComparison.Ordinal) ||
                    viewId.EndsWith(".evidence.detail", StringComparison.Ordinal)) return ["durable evidence unavailable"];
                return [$"{record.CollectionId}/{record.RecordId} · revision {envelope.Metadata.Revision?.Value ?? "unversioned"}"];
            }
            return null;
        }

        private static string[] OperationNames(CollectionDefinition definition)
        {
            CollectionOperationMatrix value = definition.Operations; var result = new List<string>(7);
            if (value.List) result.Add("list"); if (value.Get) result.Add("get"); if (value.Create) result.Add("create");
            if (value.Patch) result.Add("patch"); if (value.Replace) result.Add("replace"); if (value.Upsert) result.Add("upsert"); if (value.Delete) result.Add("delete");
            return result.ToArray();
        }
    }

    private static BaseStudioCanonicalJson EncodeValues(string[] values, bool list)
    {
        var buffer = new ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer);
        if (list) { writer.WriteStartArray(); foreach (string value in values) writer.WriteStringValue(value); writer.WriteEndArray(); }
        else writer.WriteStringValue(values.FirstOrDefault() ?? "No disclosed value");
        writer.Flush(); return BaseStudioCanonicalJson.Create(buffer.WrittenSpan, 1_048_576);
    }

    private static bool RequestResource(BaseStudioCanonicalJson request, BaseStudioResourceKind expected, out BaseStudioResourceIdentity? resource)
    {
        resource = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(request.ToArray()); JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 || !root.TryGetProperty("resource", out JsonElement value)) return false;
            string application = value.GetProperty("applicationId").GetString()!;
            resource = expected switch
            {
                BaseStudioResourceKind.Application => new BaseStudioApplicationResource(application),
                BaseStudioResourceKind.Module => new BaseStudioModuleResource(application, value.GetProperty("moduleId").GetString()!, value.GetProperty("moduleVersion").GetInt32()),
                BaseStudioResourceKind.Collection => new BaseStudioCollectionResource(application, value.GetProperty("collectionId").GetString()!, Hex(value, "installedCollectionChecksum")),
                BaseStudioResourceKind.Record => new BaseStudioRecordResource(application, value.GetProperty("collectionId").GetString()!, Hex(value, "installedCollectionChecksum"), value.GetProperty("recordId").GetString()!),
                BaseStudioResourceKind.Activation => new BaseStudioActivationResource(application, value.GetProperty("definitionId").GetString()!, value.GetProperty("version").GetInt32(), value.GetProperty("activationId").GetString()!),
                BaseStudioResourceKind.Schedule => new BaseStudioScheduleResource(application, value.GetProperty("scheduleId").GetString()!, value.GetProperty("version").GetInt32()),
                BaseStudioResourceKind.Occurrence => new BaseStudioOccurrenceResource(application, value.GetProperty("scheduleId").GetString()!, value.GetProperty("version").GetInt32(), value.GetProperty("occurrenceId").GetString()!),
                BaseStudioResourceKind.Effect => new BaseStudioEffectResource(application, value.GetProperty("activationId").GetString()!, value.GetProperty("attemptNumber").GetInt32(), value.GetProperty("effectId").GetString()!),
                BaseStudioResourceKind.Executor => new BaseStudioExecutorResource(application, value.GetProperty("hostId").GetString()!, value.GetProperty("processIncarnationId").GetString()!, long.Parse(value.GetProperty("executorGeneration").GetString()!, System.Globalization.CultureInfo.InvariantCulture)),
                BaseStudioResourceKind.SubjectContract => new BaseStudioSubjectContractResource(application, value.GetProperty("contractId").GetString()!, value.GetProperty("contractVersion").GetInt32()),
                BaseStudioResourceKind.Subject => new BaseStudioSubjectResource(application, value.GetProperty("contractId").GetString()!, value.GetProperty("contractVersion").GetInt32(), value.GetProperty("protectedSubjectIdentity").GetString()!),
                _ => null,
            };
            string authority = value.GetProperty("authorityChecksum").GetString()!;
            return resource is not null && value.GetProperty("kind").GetString() == Kind(expected) && authority.Length == 64 &&
                BaseStudioSha256.FixedTimeEquals(resource.AuthorityChecksum, BaseStudioSha256.FromDigest(Convert.FromHexString(authority)));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or OverflowException) { resource = null; return false; }
        static BaseStudioSha256 Hex(JsonElement value, string name) => BaseStudioSha256.FromDigest(Convert.FromHexString(value.GetProperty(name).GetString()!));
        static string Kind(BaseStudioResourceKind kind) => kind switch { BaseStudioResourceKind.Application => "application", BaseStudioResourceKind.Module => "module", BaseStudioResourceKind.Collection => "collection", BaseStudioResourceKind.Record => "record",
            BaseStudioResourceKind.Activation => "activation", BaseStudioResourceKind.Schedule => "schedule", BaseStudioResourceKind.Occurrence => "occurrence",
            BaseStudioResourceKind.Effect => "effect", BaseStudioResourceKind.Executor => "executor",
            BaseStudioResourceKind.SubjectContract => "subjectContract", BaseStudioResourceKind.Subject => "subject", _ => "" };
    }
}
