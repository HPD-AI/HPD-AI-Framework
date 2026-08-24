using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.AI.Platform.Studio;

namespace HPD.Base.Studio;

internal sealed partial class BaseStudioRuntimeContributionFactory
{
    internal static readonly ImmutableArray<string> SearchPageIds =
    [
        "base.search", "base.textIndex.detail", "base.vectorIndex.detail", "base.search.query", "base.rebuild.detail",
    ];

    private void AddSearchRuntime(BaseStudioModuleRegistration module, List<BaseStudioNamedTypeContract> types,
        List<BaseStudioEndpointContract> endpoints, List<BaseStudioMethodBinding> methods,
        List<BaseStudioProducerBinding> producers, BaseStudioNamedTypeContract error,
        BaseStudioNamedTypeContract text, BaseStudioNamedTypeContract checksum, BaseStudioNamedTypeContract decimalLong,
        BaseStudioNamedTypeContract currentKind, BaseStudioNamedTypeContract graphAuthority,
        BaseStudioNamedTypeContract accounting, BaseStudioNamedTypeContract emptyItems,
        BaseStudioNamedTypeContract tokenRequest, BaseStudioNamedTypeContract resourceParameters,
        BaseStudioNamedTypeContract resourceRoute, BaseStudioNamedTypeContract resolvedKind)
    {
        if (!module.Pages.Any(static page => SearchPageIds.Contains(page.PageId, StringComparer.Ordinal))) return;
        BaseStudioNamedTypeContract positive = types.Single(static value => value.TypeId == "base.studio.positive-number");
        var resourceTypes = new Dictionary<BaseStudioResourceKind, BaseStudioNamedTypeContract>
        {
            [BaseStudioResourceKind.Application] = types.Single(static value => value.TypeId == "base.studio.resource.application"),
            [BaseStudioResourceKind.TextIndex] = Resource("textIndex", P("applicationId", text), P("authorityChecksum", checksum), P("collectionId", text), P("indexId", text), P("indexVersion", positive), K("textIndex")),
            [BaseStudioResourceKind.VectorIndex] = Resource("vectorIndex", P("applicationId", text), P("authorityChecksum", checksum), P("collectionId", text), P("indexId", text), P("indexVersion", positive), K("vectorIndex")),
            [BaseStudioResourceKind.SearchRebuild] = Resource("searchRebuild", P("applicationId", text), P("authorityChecksum", checksum), P("collectionId", text), P("indexId", text), P("indexVersion", positive), K("searchRebuild"), P("rebuildIdentity", text), P("searchKind", text)),
        };
        types.AddRange(resourceTypes.Values.Where(static value => value.TypeId != "base.studio.resource.application"));
        types.AddRange([
            Type("base.studio.search-query.terms", "{\"kind\":\"array\",\"elementTypeId\":\"base.studio.text\",\"minItems\":1,\"maxItems\":64}"),
            Type("base.studio.search-query.children", "{\"kind\":\"array\",\"elementTypeId\":\"base.studio.search-query.atom\",\"minItems\":1,\"maxItems\":64}"),
            Type("base.studio.search-query.term", SearchNode("term", P("value", text))),
            Type("base.studio.search-query.prefix", SearchNode("prefix", P("value", text))),
            Type("base.studio.search-query.phrase", SearchNode("phrase", P("terms", TypeRef("base.studio.search-query.terms")))),
            Type("base.studio.search-query.field", SearchNode("field", P("child", TypeRef("base.studio.search-query.atom")), P("field", text))),
            Type("base.studio.search-query.and", SearchNode("and", P("children", TypeRef("base.studio.search-query.children")))),
            Type("base.studio.search-query.or", SearchNode("or", P("children", TypeRef("base.studio.search-query.children")))),
            Type("base.studio.search-query.not", SearchNode("not", P("child", TypeRef("base.studio.search-query.atom")))),
            Type("base.studio.search-vector-dimensions", "{\"kind\":\"integer\",\"wire\":\"number\",\"minimum\":\"1\",\"maximum\":\"32768\"}"),
            Type("base.studio.search-vector-component", "{\"kind\":\"floating\",\"precision\":\"binary32\",\"finiteOnly\":true}"),
            Type("base.studio.search-vector-components", "{\"kind\":\"array\",\"elementTypeId\":\"base.studio.search-vector-component\",\"minItems\":1,\"maxItems\":32768}"),
            Type("base.studio.search-query.vector", SearchNode("vector", P("components", TypeRef("base.studio.search-vector-components")), P("dimensions", TypeRef("base.studio.search-vector-dimensions")))),
            Type("base.studio.search-query.atom", "{\"kind\":\"union\",\"discriminator\":\"kind\",\"variants\":[{\"tag\":\"phrase\",\"typeId\":\"base.studio.search-query.phrase\"},{\"tag\":\"prefix\",\"typeId\":\"base.studio.search-query.prefix\"},{\"tag\":\"term\",\"typeId\":\"base.studio.search-query.term\"},{\"tag\":\"vector\",\"typeId\":\"base.studio.search-query.vector\"}]}"),
            Type("base.studio.search-query", "{\"kind\":\"union\",\"discriminator\":\"kind\",\"variants\":[{\"tag\":\"and\",\"typeId\":\"base.studio.search-query.and\"},{\"tag\":\"field\",\"typeId\":\"base.studio.search-query.field\"},{\"tag\":\"not\",\"typeId\":\"base.studio.search-query.not\"},{\"tag\":\"or\",\"typeId\":\"base.studio.search-query.or\"},{\"tag\":\"phrase\",\"typeId\":\"base.studio.search-query.phrase\"},{\"tag\":\"prefix\",\"typeId\":\"base.studio.search-query.prefix\"},{\"tag\":\"term\",\"typeId\":\"base.studio.search-query.term\"},{\"tag\":\"vector\",\"typeId\":\"base.studio.search-query.vector\"}]}"),
            Type("base.studio.search-filter-value.string", FilterValue("string", P("text", text))),
            Type("base.studio.search-filter-value.id", FilterValue("id", P("text", text))),
            Type("base.studio.search-filter-value.boolean", FilterValue("boolean", P("boolean", TypeRef("base.studio.boolean")))),
            Type("base.studio.search-filter-integer", "{\"kind\":\"integer\",\"wire\":\"decimal-string\",\"minimum\":\"-9223372036854775808\",\"maximum\":\"9223372036854775807\"}"),
            Type("base.studio.search-filter-value.integer", FilterValue("integer", P("integer", TypeRef("base.studio.search-filter-integer")))),
            Type("base.studio.search-filter-value", "{\"kind\":\"union\",\"discriminator\":\"kind\",\"variants\":[{\"tag\":\"boolean\",\"typeId\":\"base.studio.search-filter-value.boolean\"},{\"tag\":\"id\",\"typeId\":\"base.studio.search-filter-value.id\"},{\"tag\":\"integer\",\"typeId\":\"base.studio.search-filter-value.integer\"},{\"tag\":\"string\",\"typeId\":\"base.studio.search-filter-value.string\"}]}"),
            Type("base.studio.search-filter-values", "{\"kind\":\"array\",\"elementTypeId\":\"base.studio.search-filter-value\",\"minItems\":1,\"maxItems\":64}"),
            Type("base.studio.search-filter.children", "{\"kind\":\"array\",\"elementTypeId\":\"base.studio.search-filter.leaf\",\"minItems\":1,\"maxItems\":64}"),
            Type("base.studio.search-filter.and", FilterNode("and", P("children", TypeRef("base.studio.search-filter.children")))),
            Type("base.studio.search-filter.or", FilterNode("or", P("children", TypeRef("base.studio.search-filter.children")))),
            Type("base.studio.search-filter.missing", FilterNode("missing", P("field", text))),
            Type("base.studio.search-filter.null", FilterNode("null", P("field", text))),
            Type("base.studio.search-filter.equal", FilterNode("equal", P("field", text), P("value", TypeRef("base.studio.search-filter-value")))),
            Type("base.studio.search-filter.in", FilterNode("in", P("field", text), P("values", TypeRef("base.studio.search-filter-values")))),
            Type("base.studio.search-filter.leaf", "{\"kind\":\"union\",\"discriminator\":\"kind\",\"variants\":[{\"tag\":\"equal\",\"typeId\":\"base.studio.search-filter.equal\"},{\"tag\":\"in\",\"typeId\":\"base.studio.search-filter.in\"},{\"tag\":\"missing\",\"typeId\":\"base.studio.search-filter.missing\"},{\"tag\":\"null\",\"typeId\":\"base.studio.search-filter.null\"}]}"),
            Type("base.studio.search-filter", "{\"kind\":\"union\",\"discriminator\":\"kind\",\"variants\":[{\"tag\":\"and\",\"typeId\":\"base.studio.search-filter.and\"},{\"tag\":\"equal\",\"typeId\":\"base.studio.search-filter.equal\"},{\"tag\":\"in\",\"typeId\":\"base.studio.search-filter.in\"},{\"tag\":\"missing\",\"typeId\":\"base.studio.search-filter.missing\"},{\"tag\":\"null\",\"typeId\":\"base.studio.search-filter.null\"},{\"tag\":\"or\",\"typeId\":\"base.studio.search-filter.or\"}]}"),
            Type("base.studio.search-order-direction", "{\"kind\":\"enum\",\"values\":[\"asc\",\"desc\"]}"),
            Type("base.studio.search-null-order", "{\"kind\":\"enum\",\"values\":[\"first\",\"last\",\"unspecified\"]}"),
            Type("base.studio.search-order-member", "{\"kind\":\"object\",\"properties\":[{\"name\":\"direction\",\"wireName\":\"direction\",\"typeId\":\"base.studio.search-order-direction\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"field\",\"wireName\":\"field\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"nullOrder\",\"wireName\":\"nullOrder\",\"typeId\":\"base.studio.search-null-order\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}"),
            Type("base.studio.search-order", "{\"kind\":\"array\",\"elementTypeId\":\"base.studio.search-order-member\",\"minItems\":0,\"maxItems\":16}"),
            Type("base.studio.optional-search-cursor", "{\"kind\":\"string\",\"minLength\":0,\"maxLength\":4096,\"format\":\"opaque-search-cursor\"}"),
            Type("base.studio.search-page-size", "{\"kind\":\"integer\",\"wire\":\"number\",\"minimum\":\"1\",\"maximum\":\"500\"}"),
            Type("base.studio.search-score.text", TaggedNode("search-score", "text", [P("units", TypeRef("base.studio.search-filter-integer"))])),
            Type("base.studio.search-vector-measure", "{\"kind\":\"floating\",\"precision\":\"binary64\",\"finiteOnly\":true}"),
            Type("base.studio.search-vector-direction", "{\"kind\":\"enum\",\"values\":[\"HigherIsNearer\",\"LowerIsNearer\"]}"),
            Type("base.studio.search-score.vector", TaggedNode("search-score", "vector", [P("direction", TypeRef("base.studio.search-vector-direction")), P("function", text), P("value", TypeRef("base.studio.search-vector-measure"))])),
            Type("base.studio.search-score", "{\"kind\":\"union\",\"discriminator\":\"kind\",\"variants\":[{\"tag\":\"text\",\"typeId\":\"base.studio.search-score.text\"},{\"tag\":\"vector\",\"typeId\":\"base.studio.search-score.vector\"}]}"),
            Type("base.studio.search-explanation-kind", "{\"kind\":\"enum\",\"values\":[\"unsupported\"]}"),
            Type("base.studio.resource.searchindex", "{\"kind\":\"union\",\"discriminator\":\"kind\",\"variants\":[{\"tag\":\"textIndex\",\"typeId\":\"base.studio.resource.textindex\"},{\"tag\":\"vectorIndex\",\"typeId\":\"base.studio.resource.vectorindex\"}]}"),
            Type("base.studio.rebuild-mode", "{\"kind\":\"enum\",\"values\":[\"execute\",\"preview\"]}"),
            Type("base.studio.command-acknowledgement", "{\"kind\":\"object\",\"properties\":[{\"name\":\"impactId\",\"wireName\":\"impactId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"previewChecksum\",\"wireName\":\"previewChecksum\",\"typeId\":\"base.studio.sha256\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"purposeId\",\"wireName\":\"purposeId\",\"typeId\":\"base.studio.text\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}"),
            Type("base.studio.command-acknowledgements.one", "{\"kind\":\"array\",\"elementTypeId\":\"base.studio.command-acknowledgement\",\"minItems\":1,\"maxItems\":1}"),
            Type("base.studio.optional-sha256", "{\"kind\":\"string\",\"minLength\":0,\"maxLength\":64,\"format\":\"optional-sha256\"}"),
        ]);
        AddCommandEnvelope("textindex.rebuild", resourceTypes[BaseStudioResourceKind.TextIndex]);
        AddCommandEnvelope("vectorindex.rebuild", resourceTypes[BaseStudioResourceKind.VectorIndex]);

        foreach (BaseStudioViewRegistration view in module.Views.Where(static view => SearchPageIds.Any(page => view.ViewId.StartsWith(page + ".", StringComparison.Ordinal))))
        {
            BaseStudioPageRegistration page = module.Pages.Single(candidate => candidate.Presentation.Sections.Any(section => section.ViewIds.Contains(view.ViewId)));
            BaseStudioResourceKind requestKind = page.Presentation.NavigationRole == BaseStudioNavigationRole.AreaLanding
                ? BaseStudioResourceKind.Application : page.AcceptedResources[0];
            if (page.PageId == "base.search.query" && page.AcceptedResources.Length > 1)
                requestKind = page.AcceptedResources[0];
            bool query = page.PageId == "base.search.query";
            BaseStudioNamedTypeContract request = Type(view.RequestNodeId, query
                ? "{\"kind\":\"object\",\"properties\":[{\"name\":\"after\",\"wireName\":\"after\",\"typeId\":\"base.studio.optional-search-cursor\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"},{\"name\":\"filter\",\"wireName\":\"filter\",\"typeId\":\"base.studio.search-filter\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"},{\"name\":\"order\",\"wireName\":\"order\",\"typeId\":\"base.studio.search-order\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"query\",\"wireName\":\"query\",\"typeId\":\"base.studio.search-query\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"resource\",\"wireName\":\"resource\",\"typeId\":\"base.studio.resource.searchindex\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"},{\"name\":\"take\",\"wireName\":\"take\",\"typeId\":\"base.studio.search-page-size\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}],\"additionalProperties\":false}"
                : Obj(P("resource", resourceTypes[requestKind])));
            BaseStudioNamedTypeContract item = Type(view.ItemNodeId, query
                ? BaseStudioModuleRegistry.SearchQueryItemDescriptor(view.ViewId)
                : "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":256,\"format\":\"studio-resource-summary\"}");
            if (!BaseStudioSha256.FixedTimeEquals(request.NodeChecksum, view.RequestNodeChecksum) ||
                !BaseStudioSha256.FixedTimeEquals(item.NodeChecksum, view.ItemNodeChecksum))
                throw new InvalidOperationException("A BASE Search view differs from its graph-owned L41 nodes.");
            bool list = view.ViewId.EndsWith(".list", StringComparison.Ordinal);
            string viewTypePrefix = view.ViewId.ToLowerInvariant();
            BaseStudioNamedTypeContract value = list ? Type(viewTypePrefix + ".items",
                $"{{\"kind\":\"array\",\"elementTypeId\":\"{item.TypeId}\",\"minItems\":0,\"maxItems\":{view.MaximumItems.ToString(CultureInfo.InvariantCulture)}}}") : item;
            BaseStudioNamedTypeContract result = Type(viewTypePrefix + ".current", Obj(P("accounting", accounting),
                P("evidence", emptyItems), P("kind", currentKind), P("links", emptyItems),
                P("observationAuthority", graphAuthority), P("resource", resourceTypes[requestKind]), P("value", value)));
            types.Add(request); types.Add(item); if (list) types.Add(value); types.Add(result);
            string methodId = "base.studio.view." + view.ViewId; string endpointId = "base.studio.view.page." + view.ViewId;
            endpoints.Add(Endpoint(endpointId, "/base/studio/views/" + view.ViewId, request, result));
            methods.Add(BaseStudioMethodBinding.Create(methodId, BaseStudioMethodKind.Page, "base", page.PageId,
                endpointId, request.TypeId, result.TypeId));
            BaseStudioResourceRegistration registration = module.Resources.Single(value => value.Kind == requestKind);
            IBaseStudioViewProducer producer = query
                ? new SearchQueryProducer(_principals, _authorization, registration.Grants, _schema, _baseAuthority,
                    _textRuntime, _vectorRuntime, view.ViewId)
                : new SearchViewProducer(_principals, _authorization, registration.Grants, _textAdministration,
                    _vectorAdministration, requestKind, view.ViewId, list);
            producers.Add(new BaseStudioViewProducerBinding(methodId, producer));
        }

        foreach ((BaseStudioResourceKind kind, string pageId) in new[]
        {
            (BaseStudioResourceKind.TextIndex, "base.textIndex.detail"),
            (BaseStudioResourceKind.VectorIndex, "base.vectorIndex.detail"),
        })
        {
            BaseStudioResourceRegistration registration = module.Resources.Single(value => value.Kind == kind);
            BaseStudioNamedTypeContract resolved = Type("base.studio." + Name(kind) + "-resolved", Obj(P("kind", resolvedKind),
                P("links", emptyItems), P("resource", resourceTypes[kind]), P("route", resourceRoute)));
            types.Add(resolved); string endpointId = "base.studio.resource.resolve." + Name(kind);
            string methodId = "base.studio.resolve." + Name(kind);
            endpoints.Add(Endpoint(endpointId, "/base/studio/resources/" + Name(kind), tokenRequest, resolved));
            methods.Add(BaseStudioMethodBinding.Create(methodId, BaseStudioMethodKind.Resolve, "base", registration.ResolverId,
                endpointId, tokenRequest.TypeId, resolved.TypeId));
            producers.Add(new BaseStudioResourceProducerBinding(methodId, new SearchResolver(_principals, _authorization,
                registration.Grants, _textAdministration, _vectorAdministration, kind, pageId)));
        }

        foreach (BaseStudioCommandRegistration command in module.Commands.Where(static value => value.CommandId is "textIndex.rebuild" or "vectorIndex.rebuild"))
        {
            bool vector = command.CommandId == "vectorIndex.rebuild";
            if (vector ? _administration is null || _vectorAdministration is null : _textAdministration is null) continue;
            string prefix = command.CommandId.ToLowerInvariant();
            BaseStudioNamedTypeContract previewRequest = types.Single(value => value.TypeId == prefix + ".preview.request");
            BaseStudioNamedTypeContract executeRequest = types.Single(value => value.TypeId == prefix + ".execute.request");
            BaseStudioNamedTypeContract result = types.Single(value => value.TypeId == prefix + ".result");
            string previewEndpoint = "base.studio.command." + prefix + ".preview";
            string executeEndpoint = "base.studio.command." + prefix + ".execute";
            endpoints.Add(CommandEndpoint(previewEndpoint, "/base/studio/commands/" + command.CommandId + "/preview", previewRequest, result));
            endpoints.Add(CommandEndpoint(executeEndpoint, "/base/studio/commands/" + command.CommandId + "/execute", executeRequest, result));
            string previewMethod = "base.studio.command." + prefix + ".preview";
            string executeMethod = "base.studio.command." + prefix + ".execute";
            methods.Add(BaseStudioMethodBinding.Create(previewMethod, BaseStudioMethodKind.Preview, "base", command.CommandId,
                previewEndpoint, previewRequest.TypeId, result.TypeId));
            methods.Add(BaseStudioMethodBinding.Create(executeMethod, BaseStudioMethodKind.Execute, "base", command.CommandId,
                executeEndpoint, executeRequest.TypeId, result.TypeId));
            var producer = new SearchRebuildProducer(_principals, _authorization, command.Grants, command.CommandId,
                _stores, _textAdministration, _vectorAdministration, _administration);
            producers.Add(new BaseStudioCommandPreviewProducerBinding(previewMethod, producer));
            producers.Add(new BaseStudioCommandExecuteProducerBinding(executeMethod, producer));
        }

        BaseStudioNamedTypeContract Resource(string name, params string[] properties)
        { BaseStudioNamedTypeContract literal = Type("base.studio.resource-kind." + name.ToLowerInvariant(), $"{{\"kind\":\"literal\",\"value\":\"{name}\"}}");
          types.Add(literal); return Type("base.studio.resource." + name.ToLowerInvariant(), Obj(properties)); }
        string K(string name) => P("kind", Type("base.studio.resource-kind." + name.ToLowerInvariant(), $"{{\"kind\":\"literal\",\"value\":\"{name}\"}}"));
        void AddCommandEnvelope(string command, BaseStudioNamedTypeContract target)
        {
            string inputId = command + ".input", resultId = command + ".result";
            BaseStudioNamedTypeContract input = Type(inputId, command.StartsWith("textindex", StringComparison.Ordinal)
                ? BaseStudioModuleRegistry.RebuildInputDescriptorForRuntime("textIndex.rebuild") : BaseStudioModuleRegistry.RebuildInputDescriptorForRuntime("vectorIndex.rebuild"));
            types.Add(input);
            types.Add(Type(command + ".preview.request", Obj(P("commandId", text), P("input", input),
                P("pageId", text), P("responseAuthorityChecksum", checksum), P("target", target))));
            BaseStudioNamedTypeContract result = Type(resultId, BaseStudioModuleRegistry.RebuildResultDescriptorForRuntime);
            types.Add(Type(command + ".execute.request", Obj(P("acknowledgements", Type("base.studio.command-acknowledgements.one", "{\"kind\":\"array\",\"elementTypeId\":\"base.studio.command-acknowledgement\",\"minItems\":1,\"maxItems\":1}")),
                P("commandId", text), PN("freshAuthentication", types.Single(static value => value.TypeId == "base.studio.optional-text")), P("pageId", text),
                P("preview", result), P("requestIdentity", text), P("responseAuthorityChecksum", checksum), P("target", target))));
            types.Add(result);
        }
        BaseStudioEndpointContract Endpoint(string id, string route, BaseStudioNamedTypeContract request, BaseStudioNamedTypeContract result)
            => BaseStudioEndpointContract.Create(id, 1, BaseStudioTransportMethod.Post, route,
                BaseStudioEndpointAudience.ControlPlane, BaseStudioTransportKind.SameOriginHttp,
                request.TypeId, request.NodeChecksum, result.TypeId, result.NodeChecksum,
                error.TypeId, error.NodeChecksum, 16_384, 1_048_576, TimeSpan.FromSeconds(10));
        BaseStudioEndpointContract CommandEndpoint(string id, string route, BaseStudioNamedTypeContract request, BaseStudioNamedTypeContract result)
            => BaseStudioEndpointContract.Create(id, 1, BaseStudioTransportMethod.Post, route,
                BaseStudioEndpointAudience.ControlPlane, BaseStudioTransportKind.SameOriginHttp,
                request.TypeId, request.NodeChecksum, result.TypeId, result.NodeChecksum,
                error.TypeId, error.NodeChecksum, 1_048_576, 1_048_576, TimeSpan.FromSeconds(30));
        static string P(string name, BaseStudioNamedTypeContract type) => $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type.TypeId}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}";
        static string PN(string name, BaseStudioNamedTypeContract type) => $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type.TypeId}\",\"required\":true,\"nullable\":true,\"disclosureShape\":\"none\"}}";
        static string Obj(params string[] properties) => $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', properties)}],\"additionalProperties\":false}}";
        BaseStudioNamedTypeContract TypeRef(string id) => types.SingleOrDefault(value => value.TypeId == id) ?? Type(id, "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":1,\"format\":\"forward-reference\"}");
        string SearchNode(string kind, params string[] properties) => TaggedNode("search-query", kind, properties);
        string FilterNode(string kind, params string[] properties) => TaggedNode("search-filter", kind, properties);
        string FilterValue(string kind, params string[] properties) => TaggedNode("search-filter-value", kind, properties);
        string TaggedNode(string family, string kind, string[] properties)
        {
            BaseStudioNamedTypeContract literal = Type($"base.studio.{family}-kind.{kind}", $"{{\"kind\":\"literal\",\"value\":\"{kind}\"}}");
            types.Add(literal);
            return Obj(properties.Append(P("kind", literal)).Order(StringComparer.Ordinal).ToArray());
        }
    }

    private sealed class SearchRebuildProducer(IBaseStudioPrincipalContextResolver principals, BaseStudioAuthorization authorization,
        ImmutableArray<BaseStudioGrantRequirement> grants, string commandId, IRecordStoreRegistry stores,
        IBaseTextAdministration? text, IBaseVectorAdministration? vector, IHPDBaseAdministration? administration)
        : ProducerBase(principals, authorization, grants), IBaseStudioCommandProducer
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Preview> _previews = new(StringComparer.Ordinal);

        public async ValueTask<BaseStudioCanonicalJson?> PreviewAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) ||
                !TryPreview(invocation.Request, commandId, out BaseStudioResourceIdentity? target, out JsonElement input) || target is null) return null;
            (PrincipalContext Principal, OperationContext Operation)? context = await ContextAsync(invocation, cancellationToken).ConfigureAwait(false);
            if (context is null || target.ApplicationId != invocation.Bootstrap.ApplicationGraph.ApplicationId) return null;
            long generation; long purge = 0; long storeGeneration = 0;
            if (target is BaseStudioTextIndexResource lexical && text is not null)
            {
                OperationResult<BaseTextIndexStatus> status = await text.GetAsync(lexical.CollectionId, lexical.IndexId, cancellationToken).ConfigureAwait(false);
                if (!status.IsSuccess() || status.Value is not { } value || input.GetProperty("expectedGeneration").GetInt64() != value.Generation) return null;
                generation = value.Generation;
            }
            else if (target is BaseStudioVectorIndexResource nearest && vector is not null)
            {
                OperationResult<BaseVectorIndexStatus> status = await vector.GetAsync(nearest.CollectionId, nearest.IndexId, cancellationToken).ConfigureAwait(false);
                if (!status.IsSuccess() || status.Value is not { } value || input.GetProperty("expectedGeneration").GetInt64() != value.Generation ||
                    input.GetProperty("expectedPurgeGeneration").GetInt64() != value.PurgeGeneration ||
                    input.GetProperty("confirmation").GetString() != "REBUILD VECTOR INDEX") return null;
                generation = value.Generation; purge = value.PurgeGeneration; storeGeneration = input.GetProperty("expectedStoreGeneration").GetInt64();
                RecordStoreRegistration[] registrations = stores.GetRegistrations();
                BaseStudioStoreAuthority? storeAuthority = registrations.Length == 1 && invocation.Authority.Stores.Length == 1
                    ? invocation.Authority.Stores[0] : null;
                if (storeGeneration < 1 || storeAuthority is null || storeAuthority.SchemaGeneration != storeGeneration) return null;
            }
            else return null;
            DateTimeOffset expiry = DateTimeOffset.UtcNow.AddMinutes(5);
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(commandId + "\n" + Convert.ToHexString(target.AuthorityChecksum.ToArray()) + "\n" + input.GetRawText()));
            string checksum = Convert.ToHexStringLower(digest);
            foreach (var stale in _previews.Where(static item => item.Value.ExpiresAtUtc <= DateTimeOffset.UtcNow)) _previews.TryRemove(stale.Key, out _);
            if (_previews.Count >= 1024 || !_previews.TryAdd(checksum, new Preview(target.AuthorityChecksum,
                    invocation.Authority.Checksum, generation, purge, storeGeneration, expiry))) return null;
            return Result("preview", checksum, null, generation, expiry);
        }

        public async ValueTask<BaseStudioCanonicalJson?> ExecuteAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) || !TryExecute(invocation.Request, commandId,
                    out BaseStudioResourceIdentity? target, out string? requestIdentity, out string? checksum) || target is null || requestIdentity is null || checksum is null ||
                !_previews.TryRemove(checksum, out Preview? preview) || preview.ExpiresAtUtc <= DateTimeOffset.UtcNow ||
                !BaseStudioSha256.FixedTimeEquals(preview.Target, target.AuthorityChecksum) ||
                !BaseStudioSha256.FixedTimeEquals(preview.ResponseAuthority, invocation.Authority.Checksum)) throw new BaseStudioCommandFailedBeforeInfluenceException();
            (PrincipalContext Principal, OperationContext Operation)? context = await ContextAsync(invocation, cancellationToken).ConfigureAwait(false);
            if (context is null) throw new BaseStudioCommandFailedBeforeInfluenceException();
            long resulting; byte[] receipt;
            if (target is BaseStudioTextIndexResource lexical && text is not null)
            {
                OperationResult<BaseTextRebuildResult> rebuilt = await text.RebuildAsync(new BaseTextRebuildRequest
                { CollectionId = lexical.CollectionId, TextIndexId = lexical.IndexId, ExpectedGeneration = preview.Generation,
                  Identity = new BaseMutationRequestIdentity
                  {
                      Scope = "base.studio",
                      Operation = commandId,
                      IdempotencyKey = requestIdentity,
                      Fingerprint = BaseMutationRequestFingerprint.Create(
                          SHA256.HashData(Encoding.UTF8.GetBytes(commandId + "\n" + checksum)))
                  } }, cancellationToken).ConfigureAwait(false);
                if (!rebuilt.IsSuccess() || rebuilt.Value is not { } value)
                {
                    if (rebuilt.Status is OperationStatus.ValidationFailed or OperationStatus.Conflict or OperationStatus.NotFound or
                        OperationStatus.Unauthorized or OperationStatus.PolicyDenied or OperationStatus.Unsupported or OperationStatus.CapabilityUnavailable)
                        throw new BaseStudioCommandFailedBeforeInfluenceException();
                    throw new BaseStudioCommandIndeterminateException();
                }
                resulting = value.PublishedGeneration; receipt = value.PublicationChecksum.ToArray();
            }
            else if (target is BaseStudioVectorIndexResource nearest && administration is not null)
            {
                RecordStoreRegistration[] registrations = stores.GetRegistrations(); if (registrations.Length != 1) throw new BaseStudioCommandFailedBeforeInfluenceException();
                BaseResult<BaseVectorRebuildResult> rebuilt = await administration.RebuildVectorIndexAsync(new BaseVectorRebuildRequest
                { StoreId = registrations[0].StoreId, Principal = context.Value.Principal, CollectionId = nearest.CollectionId, VectorIndexId = nearest.IndexId,
                  ExpectedGeneration = preview.Generation, ExpectedPurgeGeneration = preview.PurgeGeneration,
                  Confirmation = "REBUILD VECTOR INDEX" }, cancellationToken).ConfigureAwait(false);
                if (!rebuilt.TryGetValue(out BaseVectorRebuildResult? value) || value is null)
                {
                    if (rebuilt is BaseFailure<BaseVectorRebuildResult> failure && failure.Error.Category is ErrorCategory.Validation or
                        ErrorCategory.Conflict or ErrorCategory.NotFound or ErrorCategory.Authentication or ErrorCategory.Authorization or ErrorCategory.Unsupported or ErrorCategory.Capability)
                        throw new BaseStudioCommandFailedBeforeInfluenceException();
                    throw new BaseStudioCommandIndeterminateException();
                }
                resulting = value.PublishedGeneration;
                receipt = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', commandId, requestIdentity, checksum,
                    value.StoreId, value.CollectionId, value.VectorIndexId, value.PreviousGeneration,
                    value.PublishedGeneration, value.CompletedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))));
            }
            else throw new BaseStudioCommandFailedBeforeInfluenceException();
            return Result("execute", checksum, Convert.ToHexStringLower(receipt), resulting, null);
        }

        private static bool TryPreview(BaseStudioCanonicalJson request, string expectedCommand, out BaseStudioResourceIdentity? target, out JsonElement input)
        {
            target = null; input = default;
            try { using JsonDocument document = JsonDocument.Parse(request.ToArray()); JsonElement root = document.RootElement;
                if (root.GetProperty("commandId").GetString() != expectedCommand || root.GetProperty("input").GetProperty("mode").GetString() != "preview" ||
                    !BaseStudioResourceRouteToken.TryDecode(root.GetProperty("input").GetProperty("resourceToken").GetString(), out BaseStudioResourceIdentity? token) ||
                    !DecodeResource(root.GetProperty("target"), out target) || token is null || target is null || !BaseStudioSha256.FixedTimeEquals(token.AuthorityChecksum, target.AuthorityChecksum)) return false;
                input = root.GetProperty("input").Clone(); return true; } catch { return false; }
        }
        private static bool TryExecute(BaseStudioCanonicalJson request, string expectedCommand, out BaseStudioResourceIdentity? target, out string? identity, out string? checksum)
        { target = null; identity = checksum = null; try { using JsonDocument document = JsonDocument.Parse(request.ToArray()); JsonElement root = document.RootElement;
            if (root.GetProperty("commandId").GetString() != expectedCommand || !DecodeResource(root.GetProperty("target"), out target)) return false;
            identity = root.GetProperty("requestIdentity").GetString(); checksum = root.GetProperty("preview").GetProperty("previewChecksum").GetString(); return true; } catch { return false; } }
        public static bool DecodeResource(JsonElement element, out BaseStudioResourceIdentity? resource)
        { string token = Convert.ToBase64String(Encoding.UTF8.GetBytes(element.GetRawText())).TrimEnd('=').Replace('+', '-').Replace('/', '_'); return BaseStudioResourceRouteToken.TryDecode(token, out resource); }
        private static BaseStudioCanonicalJson Result(string mode, string preview, string? receipt, long generation, DateTimeOffset? expiry)
        { var buffer = new ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer); writer.WriteStartObject(); writer.WritePropertyName("expiresAtUtc"); if (expiry is { } time) writer.WriteStringValue(time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture)); else writer.WriteNullValue(); writer.WriteString("mode", mode); writer.WriteString("previewChecksum", preview); writer.WritePropertyName("receiptChecksum"); if (receipt is null) writer.WriteNullValue(); else writer.WriteStringValue(receipt); writer.WriteString("resultingGeneration", generation.ToString(CultureInfo.InvariantCulture)); writer.WriteEndObject(); writer.Flush(); return BaseStudioCanonicalJson.Create(buffer.WrittenSpan, 1_048_576); }
        private sealed record Preview(BaseStudioSha256 Target, BaseStudioSha256 ResponseAuthority, long Generation,
            long PurgeGeneration, long StoreGeneration, DateTimeOffset ExpiresAtUtc);
    }

    private sealed class SearchResolver(IBaseStudioPrincipalContextResolver principals, BaseStudioAuthorization authorization,
        ImmutableArray<BaseStudioGrantRequirement> grants, IBaseTextAdministration? text, IBaseVectorAdministration? vector,
        BaseStudioResourceKind kind, string pageId) : ProducerBase(principals, authorization, grants), IBaseStudioResourceProducer
    {
        public async ValueTask<BaseStudioCanonicalJson?> ResolveAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) || !Token(invocation.Request, out BaseStudioResourceIdentity? decoded) ||
                decoded is null || decoded.Kind != kind || decoded.ApplicationId != invocation.Bootstrap.ApplicationGraph.ApplicationId) return null;
            bool exists = decoded switch
            {
                BaseStudioTextIndexResource value when text is not null => (await text.GetAsync(value.CollectionId, value.IndexId, cancellationToken).ConfigureAwait(false)).Value is { },
                BaseStudioVectorIndexResource value when vector is not null => (await vector.GetAsync(value.CollectionId, value.IndexId, cancellationToken).ConfigureAwait(false)).Value is { },
                _ => false,
            };
            if (!exists) return null;
            return BaseStudioResolvedResourceJson.Encode(decoded, BaseStudioResolvedRoute.Create(pageId,
                [new KeyValuePair<string, string>("resource", BaseStudioResourceRouteToken.Encode(decoded))]), [], 1_048_576);
        }
    }

    private sealed class SearchViewProducer(IBaseStudioPrincipalContextResolver principals, BaseStudioAuthorization authorization,
        ImmutableArray<BaseStudioGrantRequirement> grants, IBaseTextAdministration? text, IBaseVectorAdministration? vector,
        BaseStudioResourceKind requestKind, string viewId, bool list)
        : ProducerBase(principals, authorization, grants), IBaseStudioViewProducer
    {
        public async ValueTask<BaseStudioCanonicalJson?> ReadAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) ||
                !RequestResource(invocation.Request, requestKind, out BaseStudioResourceIdentity? resource) || resource is null ||
                resource.ApplicationId != invocation.Bootstrap.ApplicationGraph.ApplicationId) return null;
            string[]? values = await Values(resource, cancellationToken).ConfigureAwait(false); if (values is null) return null;
            BaseStudioCanonicalJson projected = Encode(values, list);
            return BaseStudioObservationJson.Current(resource, BaseStudioGraphObservationAuthority.Create(invocation.Authority),
                projected, [], [], Accounting(projected.ToArray().Length), 1_048_576);
        }

        private async ValueTask<string[]?> Values(BaseStudioResourceIdentity resource, CancellationToken cancellationToken)
        {
            if (resource is BaseStudioApplicationResource)
            {
                if (viewId.Contains("textIndexes", StringComparison.Ordinal) && text is not null)
                    return (await text.ListAsync(cancellationToken).ConfigureAwait(false)).Value?.Select(TextSummary).Take(500).ToArray();
                if (viewId.Contains("vectorIndexes", StringComparison.Ordinal) && vector is not null)
                    return (await vector.ListAsync(cancellationToken).ConfigureAwait(false)).Value?.Select(VectorSummary).Take(500).ToArray();
                return ["No currently disclosed search rebuild or attention item."];
            }
            if (resource is BaseStudioTextIndexResource lexical && text is not null)
                return (await text.GetAsync(lexical.CollectionId, lexical.IndexId, cancellationToken).ConfigureAwait(false)).Value is { } status
                    ? [TextSummary(status)] : null;
            if (resource is BaseStudioVectorIndexResource vectorIndex && vector is not null)
                return (await vector.GetAsync(vectorIndex.CollectionId, vectorIndex.IndexId, cancellationToken).ConfigureAwait(false)).Value is { } status
                    ? [VectorSummary(status)] : null;
            return null;
        }

        private static string TextSummary(BaseTextIndexStatus value) =>
            $"{value.CollectionId}/{value.TextIndexId}@{value.Version} {value.State} generation={value.Generation} visible={value.SearchVisibleThrough.Value}";
        private static string VectorSummary(BaseVectorIndexStatus value) =>
            $"{value.CollectionId}/{value.VectorIndexId} {value.State} generation={value.Generation} visible={value.AppliedThrough.Value}";
        private static BaseStudioCanonicalJson Encode(string[] values, bool list)
        {
            var buffer = new ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer);
            if (list) { writer.WriteStartArray(); foreach (string value in values) writer.WriteStringValue(value); writer.WriteEndArray(); }
            else writer.WriteStringValue(values.FirstOrDefault() ?? "No currently disclosed search state.");
            writer.Flush(); return BaseStudioCanonicalJson.Create(buffer.WrittenSpan, 1_048_576);
        }
    }

    private sealed class SearchQueryProducer(IBaseStudioPrincipalContextResolver principals, BaseStudioAuthorization authorization,
        ImmutableArray<BaseStudioGrantRequirement> grants, IBaseSchemaProvider schema, HPDBaseStudioAuthoritySnapshot baseAuthority,
        IBaseTextRuntime? textRuntime, IBaseVectorRuntime? vectorRuntime, string viewId)
        : ProducerBase(principals, authorization, grants), IBaseStudioViewProducer
    {
        public async ValueTask<BaseStudioCanonicalJson?> ReadAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) ||
                !TryRequest(invocation.Request, out BaseStudioResourceIdentity? resource, out JsonElement query,
                    out JsonElement? filter, out JsonElement order, out int take, out string? after) || resource is null ||
                resource.ApplicationId != invocation.Bootstrap.ApplicationGraph.ApplicationId) return null;
            (PrincipalContext Principal, OperationContext Operation)? context = await ContextAsync(invocation, cancellationToken).ConfigureAwait(false);
            if (context is null) return null;
            string queryChecksum = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(query.GetRawText())));
            if (viewId.EndsWith(".query.detail", StringComparison.Ordinal))
                return Observation(invocation, resource, QuerySummary(resource, queryChecksum, take));
            if (viewId.EndsWith(".explanation.detail", StringComparison.Ordinal))
                return Observation(invocation, resource, BaseStudioCanonicalJson.Create("{\"kind\":\"unsupported\",\"reasonCode\":\"base.search.explanation.selectionRequired\"}"u8, 1_048_576));
            OperationResult<CollectionDefinition> found = await schema.GetCollectionAsync(Collection(resource), context.Value.Principal,
                context.Value.Operation, VisibilityLevel.Internal, cancellationToken).ConfigureAwait(false);
            if (!found.IsSuccess() || found.Value is not { } collection ||
                baseAuthority.GetInstalledCollectionChecksum(collection.Id) is not { Length: 32 } installed) return null;
            QueryPage? page = resource switch
            {
                BaseStudioTextIndexResource text when textRuntime is not null => await Text(text, collection, installed, query, filter, order, take, after, context.Value, cancellationToken).ConfigureAwait(false),
                BaseStudioVectorIndexResource vector when vectorRuntime is not null => await Vector(vector, collection, installed, query, filter, order, take, after, context.Value, cancellationToken).ConfigureAwait(false),
                _ => null,
            };
            if (page is null) return null;
            BaseStudioCanonicalJson projected = viewId.EndsWith(".evidence.detail", StringComparison.Ordinal)
                ? Evidence(queryChecksum, page.ConsistencyToken) : Encode(page.Items);
            return Observation(invocation, resource, projected);
        }

        private async ValueTask<QueryPage?> Text(BaseStudioTextIndexResource resource, CollectionDefinition collection, byte[] installed,
            JsonElement query, JsonElement? filter, JsonElement order, int take, string? after,
            (PrincipalContext Principal, OperationContext Operation) context, CancellationToken cancellationToken)
        {
            BaseTextIndexDefinition? index = collection.TextIndexes?.SingleOrDefault(value => value.Id == resource.IndexId && value.Version == resource.IndexVersion);
            if (index is null || query.GetProperty("kind").GetString() == "vector") return null;
            try
            {
                OperationResult<BaseTextRuntimeResult> result = await textRuntime!.ExecuteAsync(new BaseTextRuntimeRequest
                {
                    Collection = collection, Index = index, Query = TextQuery(query),
                    Constraint = filter is { } value ? TextFilter(value, index) : new BaseTextCandidateConstraint.True(),
                    Order = [.. order.EnumerateArray().Select(value => TextOrder(value, index))], Take = take,
                    After = after is null ? null : BaseTextCursor.Parse(after), Consistency = new BaseTextConsistencyRequirement.Current(),
                    Principal = context.Principal, Operation = context.Operation,
                }, cancellationToken).ConfigureAwait(false);
                if (!result.IsSuccess() || result.Value is not { } page) return null;
                return new(page.Matches.Select((match, position) => Item(resource.ApplicationId, collection.Id, installed, match.Record.Id.Value,
                    position + 1, "text", match.Score.Units.ToString(CultureInfo.InvariantCulture), null, null, match.Revision.Value)).ToArray(),
                    page.Consistency.Encode());
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException or OverflowException) { return null; }
        }

        private async ValueTask<QueryPage?> Vector(BaseStudioVectorIndexResource resource, CollectionDefinition collection, byte[] installed,
            JsonElement query, JsonElement? filter, JsonElement order, int take, string? after,
            (PrincipalContext Principal, OperationContext Operation) context, CancellationToken cancellationToken)
        {
            VectorIndexDefinition? index = collection.VectorIndexes?.SingleOrDefault(value => value.Id == resource.IndexId);
            if (resource.IndexVersion != 1) return null;
            if (index is null || query.GetProperty("kind").GetString() != "vector" || after is not null || order.GetArrayLength() != 0) return null;
            try
            {
                JsonElement components = query.GetProperty("components"); int dimensions = query.GetProperty("dimensions").GetInt32();
                if (components.GetArrayLength() != dimensions || dimensions != index.Dimensions) return null;
                BaseVector vector = BaseVector.Create(components.EnumerateArray().Select(static value => value.GetSingle()).ToArray());
                OperationResult<BaseVectorRuntimeResult> result = await vectorRuntime!.ExecuteAsync(new BaseVectorRuntimeRequest
                {
                    Collection = collection, Index = index, Vector = vector,
                    Constraint = filter is { } value ? VectorFilter(value, index, collection) : new BaseVectorCandidateConstraint.True(),
                    Take = take, Consistency = null, Principal = context.Principal, Operation = context.Operation,
                }, cancellationToken).ConfigureAwait(false);
                if (!result.IsSuccess() || result.Value is not { } page) return null;
                return new(page.Matches.Select(match => Item(resource.ApplicationId, collection.Id, installed, match.Record.Id.Value,
                    match.Rank, "vector", match.Measure.Value.ToString("R", CultureInfo.InvariantCulture), match.Measure.Function.ToString(), match.Measure.Direction.ToString(),
                    $"{match.Measure.Function}:{match.Measure.Direction}:{match.Measure.Value:R}" )).ToArray(), page.ConsistencyToken.Encode());
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException or OverflowException) { return null; }
        }

        private static QueryItem Item(string application, string collection, byte[] installed, string recordId, int rank,
            string scoreKind, string score, string? function, string? direction, string explanation)
        {
            var identity = new BaseStudioRecordResource(application, collection, BaseStudioSha256.FromDigest(installed), recordId);
            return new(BaseStudioResourceRouteToken.Encode(identity), recordId, rank, scoreKind, score, function, direction,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(explanation))));
        }

        private static BaseTextQuery TextQuery(JsonElement value) => value.GetProperty("kind").GetString() switch
        {
            "term" => BaseTextQuery.Token(value.GetProperty("value").GetString()!),
            "prefix" => BaseTextQuery.StartsWith(value.GetProperty("value").GetString()!),
            "phrase" => BaseTextQuery.ExactPhrase(value.GetProperty("terms").EnumerateArray().Select(static item => item.GetString()!).ToArray()),
            "field" => BaseTextQuery.InField(value.GetProperty("field").GetString()!, TextQuery(value.GetProperty("child"))),
            "and" => BaseTextQuery.All(value.GetProperty("children").EnumerateArray().Select(TextQuery).ToArray()),
            "or" => BaseTextQuery.Any(value.GetProperty("children").EnumerateArray().Select(TextQuery).ToArray()),
            "not" => BaseTextQuery.Exclude(TextQuery(value.GetProperty("child"))),
            _ => throw new ArgumentException(),
        };

        private static BaseTextCandidateConstraint TextFilter(JsonElement value, BaseTextIndexDefinition index)
        {
            string kind = value.GetProperty("kind").GetString()!;
            if (kind is "and" or "or")
            { ImmutableArray<BaseTextCandidateConstraint> children = [.. value.GetProperty("children").EnumerateArray().Select(child => TextFilter(child, index))]; return kind == "and" ? new BaseTextCandidateConstraint.And(children) : new BaseTextCandidateConstraint.Or(children); }
            BaseTextIndexFilterFieldDefinition definition = index.FilterFields.Single(item => item.StableFieldId == value.GetProperty("field").GetString());
            var field = new BaseTextFilterField(definition.StableFieldId, definition.ValueKind);
            return kind switch { "missing" => new BaseTextCandidateConstraint.IsMissing(field), "null" => new BaseTextCandidateConstraint.IsNull(field),
                "equal" => new BaseTextCandidateConstraint.Equal(field, TextValue(value.GetProperty("value"), definition.ValueKind)),
                "in" => new BaseTextCandidateConstraint.In(field, [.. value.GetProperty("values").EnumerateArray().Select(item => TextValue(item, definition.ValueKind))]), _ => throw new ArgumentException() };
        }

        private static BaseTextFilterValue TextValue(JsonElement value, BaseTextFilterValueKind expected) => (value.GetProperty("kind").GetString(), expected) switch
        { ("string", BaseTextFilterValueKind.String) => BaseTextFilterValue.FromString(value.GetProperty("text").GetString()!), ("id", BaseTextFilterValueKind.Id) => BaseTextFilterValue.FromId(value.GetProperty("text").GetString()!),
          ("boolean", BaseTextFilterValueKind.Boolean) => BaseTextFilterValue.FromBoolean(value.GetProperty("boolean").GetBoolean()), ("integer", BaseTextFilterValueKind.Integer) => BaseTextFilterValue.FromInteger(long.Parse(value.GetProperty("integer").GetString()!, CultureInfo.InvariantCulture)), _ => throw new ArgumentException() };

        private static BaseTextOrder TextOrder(JsonElement value, BaseTextIndexDefinition index)
        { BaseTextIndexFilterFieldDefinition field = index.FilterFields.Single(item => item.StableFieldId == value.GetProperty("field").GetString()); return new(field.StableFieldId,
            value.GetProperty("direction").GetString() == "asc" ? QuerySortDirection.Asc : value.GetProperty("direction").GetString() == "desc" ? QuerySortDirection.Desc : throw new ArgumentException(),
            value.GetProperty("nullOrder").GetString() switch { "first" => QueryNullOrder.First, "last" => QueryNullOrder.Last, "unspecified" => QueryNullOrder.Unspecified, _ => throw new ArgumentException() }); }

        private static BaseVectorCandidateConstraint VectorFilter(JsonElement value, VectorIndexDefinition index, CollectionDefinition collection)
        {
            string kind = value.GetProperty("kind").GetString()!;
            if (kind is "and" or "or") { BaseVectorCandidateConstraint[] children = value.GetProperty("children").EnumerateArray().Select(child => VectorFilter(child, index, collection)).ToArray(); return kind == "and" ? new BaseVectorCandidateConstraint.And(children) : new BaseVectorCandidateConstraint.Or(children); }
            string id = value.GetProperty("field").GetString()!;
            if (!index.FilterFieldIds.Contains(id, StringComparer.Ordinal)) throw new ArgumentException();
            FieldDefinition definition = collection.Fields!.Single(item => item.Id == id);
            BaseVectorFilterValueKind valueKind = definition.Type switch { "string" => BaseVectorFilterValueKind.String, "boolean" => BaseVectorFilterValueKind.Boolean, "int64" => BaseVectorFilterValueKind.Integer, "id" => BaseVectorFilterValueKind.Id, _ => throw new ArgumentException() };
            var field = new BaseVectorFilterField(id, valueKind);
            return kind switch { "equal" => new BaseVectorCandidateConstraint.Equal(field, VectorValue(value.GetProperty("value"), valueKind)),
                "in" => new BaseVectorCandidateConstraint.In(field, value.GetProperty("values").EnumerateArray().Select(item => VectorValue(item, valueKind))), _ => throw new ArgumentException() };
        }

        private static BaseVectorFilterValue VectorValue(JsonElement value, BaseVectorFilterValueKind expected) => (value.GetProperty("kind").GetString(), expected) switch
        { ("string", BaseVectorFilterValueKind.String) => BaseVectorFilterValue.FromString(value.GetProperty("text").GetString()!), ("id", BaseVectorFilterValueKind.Id) => BaseVectorFilterValue.FromId(value.GetProperty("text").GetString()!),
          ("boolean", BaseVectorFilterValueKind.Boolean) => BaseVectorFilterValue.FromBoolean(value.GetProperty("boolean").GetBoolean()), ("integer", BaseVectorFilterValueKind.Integer) => BaseVectorFilterValue.FromInteger(long.Parse(value.GetProperty("integer").GetString()!, CultureInfo.InvariantCulture)), _ => throw new ArgumentException() };

        private static bool TryRequest(BaseStudioCanonicalJson request, out BaseStudioResourceIdentity? resource, out JsonElement query,
            out JsonElement? filter, out JsonElement order, out int take, out string? after)
        {
            resource = null; query = order = default; filter = null; take = 0; after = null;
            try { using JsonDocument document = JsonDocument.Parse(request.ToArray()); JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 6 || !SearchRebuildProducer.DecodeResource(root.GetProperty("resource"), out resource) || resource is not (BaseStudioTextIndexResource or BaseStudioVectorIndexResource)) return false;
                query = root.GetProperty("query").Clone(); order = root.GetProperty("order").Clone(); take = root.GetProperty("take").GetInt32();
                if (take is < 1 or > 500 || order.ValueKind != JsonValueKind.Array) return false;
                JsonElement filterValue = root.GetProperty("filter"); if (filterValue.ValueKind != JsonValueKind.Null) filter = filterValue.Clone();
                JsonElement afterValue = root.GetProperty("after"); if (afterValue.ValueKind != JsonValueKind.Null) after = afterValue.GetString(); return true;
            } catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or OverflowException) { return false; }
        }

        private static string Collection(BaseStudioResourceIdentity resource) => resource switch
        { BaseStudioTextIndexResource value => value.CollectionId, BaseStudioVectorIndexResource value => value.CollectionId, _ => throw new ArgumentOutOfRangeException(nameof(resource)) };

        private static BaseStudioCanonicalJson Encode(QueryItem[] items)
        { var buffer = new ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer); writer.WriteStartArray();
          foreach (QueryItem item in items) { writer.WriteStartObject(); writer.WriteString("explanationChecksum", item.ExplanationChecksum); writer.WriteNumber("rank", item.Rank); writer.WriteString("resourceToken", item.ResourceToken); writer.WriteString("safeLabel", item.SafeLabel); writer.WritePropertyName("score"); writer.WriteStartObject(); if (item.ScoreKind == "text") { writer.WriteString("kind", "text"); writer.WriteString("units", item.Score); } else { writer.WriteString("direction", item.Direction); writer.WriteString("function", item.Function); writer.WriteString("kind", "vector"); writer.WriteNumber("value", double.Parse(item.Score, CultureInfo.InvariantCulture)); } writer.WriteEndObject(); writer.WriteEndObject(); }
          writer.WriteEndArray(); writer.Flush(); return BaseStudioCanonicalJson.Create(buffer.WrittenSpan, 1_048_576); }
        private static BaseStudioCanonicalJson QuerySummary(BaseStudioResourceIdentity resource, string checksum, int take)
        { var buffer = new ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer); writer.WriteStartObject(); writer.WriteString("queryChecksum", checksum); writer.WriteString("resourceToken", BaseStudioResourceRouteToken.Encode(resource)); writer.WriteNumber("take", take); writer.WriteEndObject(); writer.Flush(); return BaseStudioCanonicalJson.Create(buffer.WrittenSpan, 1_048_576); }
        private static BaseStudioCanonicalJson Evidence(string checksum, string consistency)
        { var buffer = new ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer); writer.WriteStartObject(); writer.WriteString("consistencyToken", consistency); writer.WriteString("queryChecksum", checksum); writer.WriteEndObject(); writer.Flush(); return BaseStudioCanonicalJson.Create(buffer.WrittenSpan, 1_048_576); }
        private BaseStudioCanonicalJson Observation(BaseStudioProducerInvocation invocation, BaseStudioResourceIdentity resource, BaseStudioCanonicalJson projected) =>
            BaseStudioObservationJson.Current(resource, BaseStudioGraphObservationAuthority.Create(invocation.Authority), projected, [], [], Accounting(projected.ToArray().Length), 1_048_576);
        private sealed record QueryItem(string ResourceToken, string SafeLabel, int Rank, string ScoreKind, string Score,
            string? Function, string? Direction, string ExplanationChecksum);
        private sealed record QueryPage(QueryItem[] Items, string ConsistencyToken);
    }

    private static string Name(BaseStudioResourceKind kind) => kind switch
    { BaseStudioResourceKind.TextIndex => "textindex", BaseStudioResourceKind.VectorIndex => "vectorindex", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
}
