using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using HPD.AI.Platform.Studio;

namespace HPD.Base.Studio;

internal sealed partial class BaseStudioRuntimeContributionFactory
{
    private void AddSecurityRuntime(BaseStudioModuleRegistration module, List<BaseStudioNamedTypeContract> types,
        List<BaseStudioEndpointContract> endpoints, List<BaseStudioMethodBinding> methods,
        List<BaseStudioProducerBinding> producers, BaseStudioNamedTypeContract error, BaseStudioNamedTypeContract text,
        BaseStudioNamedTypeContract checksum, BaseStudioNamedTypeContract decimalLong, BaseStudioNamedTypeContract currentKind,
        BaseStudioNamedTypeContract graphAuthority, BaseStudioNamedTypeContract accounting, BaseStudioNamedTypeContract emptyItems,
        BaseStudioNamedTypeContract tokenRequest, BaseStudioNamedTypeContract resourceParameters,
        BaseStudioNamedTypeContract resourceRoute, BaseStudioNamedTypeContract resolvedKind)
    {
        BaseStudioNamedTypeContract applicationKind = Type("base.studio.resource-kind.application", "{\"kind\":\"literal\",\"value\":\"application\"}");
        BaseStudioNamedTypeContract policyKind = Type("base.studio.resource-kind.policy", "{\"kind\":\"literal\",\"value\":\"policy\"}");
        BaseStudioNamedTypeContract grantKind = Type("base.studio.resource-kind.grant", "{\"kind\":\"literal\",\"value\":\"grant\"}");
        BaseStudioNamedTypeContract routeToken = Type("base.studio.resource-route-token", "{\"kind\":\"string\",\"minLength\":1,\"maxLength\":8192,\"format\":\"studio-resource-token\"}");
        BaseStudioNamedTypeContract application = Type("base.studio.resource.application", Obj(P("applicationId", text), P("authorityChecksum", checksum), P("kind", applicationKind)));
        BaseStudioNamedTypeContract policy = Type("base.studio.resource.policy", Obj(P("applicationId", text), P("authorityChecksum", checksum), P("kind", policyKind), P("policyId", text), P("version", decimalLong)));
        BaseStudioNamedTypeContract grant = Type("base.studio.resource.grant", Obj(P("applicationId", text), P("authorityChecksum", checksum), P("grantId", text), P("kind", grantKind), P("version", decimalLong)));
        AddType(applicationKind); AddType(policyKind); AddType(grantKind); AddType(routeToken); AddType(policy); AddType(grant);

        foreach (BaseStudioPageRegistration page in module.Pages.Where(static page => page.PageId is "base.security" or "base.policy.detail" or "base.grant.detail" or "base.policy.explain"))
        foreach (BaseStudioSectionRegistration section in page.Presentation.Sections)
        foreach (string viewId in section.ViewIds)
        {
            BaseStudioViewRegistration view = module.Views.Single(value => value.ViewId == viewId);
            BaseStudioNamedTypeContract resourceType = page.PageId == "base.security" ? application : page.PageId is "base.policy.detail" or "base.policy.explain" ? policy : grant;
            BaseStudioNamedTypeContract request = page.PageId == "base.policy.explain"
                ? Type(view.RequestNodeId, Obj(P("operationId", text), P("resource", resourceType), P("targetResourceKind", text), P("targetResourceToken", routeToken)))
                : Type(view.RequestNodeId, Obj(P("resource", resourceType)));
            BaseStudioNamedTypeContract item = Type(view.ItemNodeId, BaseStudioSecurityContracts.ItemDescriptor(viewId));
            if (!BaseStudioSha256.FixedTimeEquals(request.NodeChecksum, view.RequestNodeChecksum) ||
                !BaseStudioSha256.FixedTimeEquals(item.NodeChecksum, view.ItemNodeChecksum))
                throw new InvalidOperationException("A Security view differs from its graph-owned L41 node.");
            bool list = viewId.EndsWith(".list", StringComparison.Ordinal);
            BaseStudioNamedTypeContract value = list ? Type(viewId + ".items", $"{{\"kind\":\"array\",\"elementTypeId\":\"{item.TypeId}\",\"minItems\":0,\"maxItems\":{view.MaximumItems.ToString(CultureInfo.InvariantCulture)}}}") : item;
            BaseStudioNamedTypeContract result = Type(viewId + ".current", Obj(P("accounting", accounting), P("evidence", emptyItems), P("kind", currentKind),
                P("links", emptyItems), P("observationAuthority", graphAuthority), P("resource", resourceType), P("value", value)));
            AddType(request); AddType(item); if (list) AddType(value); AddType(result);
            string methodId = "base.studio.view." + viewId; string endpointId = PageEndpoint + "." + viewId;
            endpoints.Add(Endpoint(endpointId, "/base/studio/views/" + viewId, request, result));
            methods.Add(BaseStudioMethodBinding.Create(methodId, BaseStudioMethodKind.Page, "base", page.PageId, endpointId, request.TypeId, result.TypeId));
            producers.Add(new BaseStudioViewProducerBinding(methodId, page.PageId == "base.policy.explain"
                ? new PolicyExplainProducer(_principals, _authorization, page.Grants, _baseAuthority, _policy, viewId, list)
                : new SecuritySectionProducer(_principals, _authorization, page.Grants, _baseAuthority, viewId, list)));
        }

        AddResolver(BaseStudioResourceKind.Policy, policy, "base.policy.detail", static (authority, resource) =>
            resource is BaseStudioPolicyResource value && authority.Policies.Any(item => item.Id == value.PolicyId && item.Version == value.Version));
        AddResolver(BaseStudioResourceKind.Grant, grant, "base.grant.detail", static (authority, resource) =>
            resource is BaseStudioGrantResource value && authority.Grants.Any(item => item.Id == value.GrantId && item.Version == value.Version));

        void AddResolver(BaseStudioResourceKind kind, BaseStudioNamedTypeContract resourceType, string pageId,
            Func<HPDBaseStudioAuthoritySnapshot, BaseStudioResourceIdentity, bool> exists)
        {
            BaseStudioResourceRegistration registration = module.Resources.Single(value => value.Kind == kind);
            string methodId = "base.studio.resolve." + kind.ToString().ToLowerInvariant();
            string endpointId = ResolveEndpoint + "." + kind.ToString().ToLowerInvariant();
            BaseStudioNamedTypeContract result = Type("base.studio." + kind.ToString().ToLowerInvariant() + "-resolved",
                Obj(P("kind", resolvedKind), P("links", emptyItems), P("resource", resourceType), P("route", resourceRoute)));
            AddType(result); endpoints.Add(Endpoint(endpointId, "/base/studio/resources/" + kind.ToString().ToLowerInvariant(), tokenRequest, result));
            methods.Add(BaseStudioMethodBinding.Create(methodId, BaseStudioMethodKind.Resolve, "base", registration.ResolverId, endpointId, tokenRequest.TypeId, result.TypeId));
            producers.Add(new BaseStudioResourceProducerBinding(methodId, new SecurityResolver(_principals, _authorization, registration.Grants, _baseAuthority, pageId, exists)));
        }
        void AddType(BaseStudioNamedTypeContract value) { if (!types.Any(item => item.TypeId == value.TypeId)) types.Add(value); }
        BaseStudioEndpointContract Endpoint(string id, string route, BaseStudioNamedTypeContract request, BaseStudioNamedTypeContract result) =>
            BaseStudioEndpointContract.Create(id, 1, BaseStudioTransportMethod.Post, route, BaseStudioEndpointAudience.ControlPlane,
                BaseStudioTransportKind.SameOriginHttp, request.TypeId, request.NodeChecksum, result.TypeId, result.NodeChecksum,
                error.TypeId, error.NodeChecksum, 16_384, 1_048_576, TimeSpan.FromSeconds(10));
        static string P(string name, BaseStudioNamedTypeContract type) => $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type.TypeId}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}";
        static string Obj(params string[] properties) => $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', properties)}],\"additionalProperties\":false}}";
    }

    private sealed class SecuritySectionProducer(IBaseStudioPrincipalContextResolver principals, BaseStudioAuthorization authorization,
        ImmutableArray<BaseStudioGrantRequirement> grants, HPDBaseStudioAuthoritySnapshot authority, string viewId, bool list)
        : ProducerBase(principals, authorization, grants), IBaseStudioViewProducer
    {
        public async ValueTask<BaseStudioCanonicalJson?> ReadAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) || !TryResource(invocation.Request, out BaseStudioResourceIdentity? resource) ||
                resource is null || resource.ApplicationId != authority.ApplicationId) return null;
            IReadOnlyList<IReadOnlyDictionary<string, string>> rows = Rows(resource);
            if ((!list && rows.Count != 1) || rows.Count > 500) return null;
            BaseStudioCanonicalJson value = Encode(rows, list);
            return BaseStudioObservationJson.Current(resource, BaseStudioGraphObservationAuthority.Create(invocation.Authority), value, [], [], Accounting(value.ToArray().Length), 1_048_576);
        }

        private IReadOnlyList<IReadOnlyDictionary<string, string>> Rows(BaseStudioResourceIdentity resource)
        {
            string owner = authority.PolicyOwnerGeneration.ToString(CultureInfo.InvariantCulture);
            string ownerChecksum = Hex(authority.GetPolicyOwnerChecksum());
            if (viewId == "base.security.policies.list") return authority.Policies.Select(PolicySummary).ToArray();
            if (viewId == "base.security.grants.list") return authority.Grants.Select(GrantSummary).ToArray();
            if (viewId == "base.security.explanations.list") return [D(("queryKind", "operatorSupplied"), ("requiredOperation", "base.studio.resource.inspect"), ("authorityClass", "requestSpecificPolicyEvaluation"), ("maximumResultBytes", "1048576"), ("registrationChecksum", ownerChecksum))];
            if (viewId == "base.security.disclosure.detail") return [D(("policyOwnerGeneration", owner), ("policyOwnerChecksum", ownerChecksum), ("disclosureClass", "protectedValue"), ("nativeConditionsExposed", "false"), ("registrationChecksum", ownerChecksum))];
            if (resource is BaseStudioPolicyResource policyResource)
            {
                HPDBaseStudioPolicyAuthority policy = authority.Policies.Single(value => value.Id == policyResource.PolicyId && value.Version == policyResource.Version);
                string checksum = Hex(policy.RegistrationChecksum);
                return viewId switch
                {
                    "base.policy.detail.summary.detail" => [D(("policyId", policy.Id), ("version", N(policy.Version)), ("owningModuleId", policy.OwningModuleId), ("evaluatorContractId", policy.EvaluatorContractId), ("registrationChecksum", checksum))],
                    "base.policy.detail.composition.detail" => [D(("policyId", policy.Id), ("version", N(policy.Version)), ("compositionOrder", N(policy.CompositionOrder)), ("policyOwnerGeneration", owner), ("registrationChecksum", checksum))],
                    "base.policy.detail.constraints.detail" => [D(("policyId", policy.Id), ("version", N(policy.Version)), ("constraintAuthority", "requestSpecificEvaluation"), ("queryRequired", "true"), ("registrationChecksum", checksum))],
                    "base.policy.detail.masks.detail" => [D(("policyId", policy.Id), ("version", N(policy.Version)), ("maskAuthority", "requestSpecificEvaluation"), ("queryRequired", "true"), ("registrationChecksum", checksum))],
                    "base.policy.detail.obligations.detail" => [D(("policyId", policy.Id), ("version", N(policy.Version)), ("obligationAuthority", "evaluatorContract"), ("evaluatorContractId", policy.EvaluatorContractId), ("registrationChecksum", checksum))],
                    "base.policy.detail.history.list" => [D(("policyId", policy.Id), ("version", N(policy.Version)), ("policyOwnerGeneration", owner), ("policyOwnerChecksum", ownerChecksum), ("registrationChecksum", checksum))],
                    _ => [],
                };
            }
            if (resource is BaseStudioGrantResource grantResource)
            {
                HPDBaseStudioGrantAuthority grant = authority.Grants.Single(value => value.Id == grantResource.GrantId && value.Version == grantResource.Version);
                AccessGrant? semantics = grant.GetStaticGrant(); string checksum = Hex(grant.GetChecksum());
                return viewId switch
                {
                    "base.grant.detail.summary.detail" => [D(("grantId", grant.Id), ("version", N(grant.Version)), ("owningModuleId", grant.OwningModuleId), ("sourceContractId", grant.SourceContractId), ("registrationChecksum", checksum))],
                    "base.grant.detail.scope.detail" => [D(("grantId", grant.Id), ("version", N(grant.Version)), ("subjectKind", semantics?.Subject.Kind.ToString() ?? "dynamic"), ("subjectId", semantics?.Subject.Id ?? "protected"), ("audience", semantics?.Audience?.ToString() ?? "dynamic"), ("registrationChecksum", checksum))],
                    "base.grant.detail.operations.list" => [D(("grantId", grant.Id), ("version", N(grant.Version)), ("action", semantics?.Action ?? "dynamic"), ("effect", semantics?.Effect.ToString() ?? "dynamic"), ("registrationChecksum", checksum))],
                    "base.grant.detail.conditions.detail" => [D(("grantId", grant.Id), ("version", N(grant.Version)), ("staticSemantics", grant.HasStaticSemantics.ToString().ToLowerInvariant()), ("readCondition", semantics?.Condition is null ? "absent" : "presentProtected"), ("writeCondition", semantics?.WriteCondition is null ? "absent" : "presentProtected"), ("registrationChecksum", checksum))],
                    "base.grant.detail.history.list" => [D(("grantId", grant.Id), ("version", N(grant.Version)), ("policyOwnerGeneration", owner), ("policyOwnerChecksum", ownerChecksum), ("registrationChecksum", checksum))],
                    _ => [],
                };
            }
            return [];
        }
        private IReadOnlyDictionary<string, string> PolicySummary(HPDBaseStudioPolicyAuthority value) => D(("policyId", value.Id), ("version", N(value.Version)), ("owningModuleId", value.OwningModuleId), ("compositionOrder", N(value.CompositionOrder)), ("registrationChecksum", Hex(value.RegistrationChecksum)));
        private IReadOnlyDictionary<string, string> GrantSummary(HPDBaseStudioGrantAuthority value) => D(("grantId", value.Id), ("version", N(value.Version)), ("owningModuleId", value.OwningModuleId), ("sourceContractId", value.SourceContractId), ("registrationChecksum", Hex(value.GetChecksum())));
        private static Dictionary<string, string> D(params (string Key, string Value)[] values) => values.ToDictionary(static value => value.Key, static value => value.Value, StringComparer.Ordinal);
        private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Hex(IEnumerable<byte> value) => Convert.ToHexString(value.ToArray()).ToLowerInvariant();
    }

    private sealed class SecurityResolver(IBaseStudioPrincipalContextResolver principals, BaseStudioAuthorization authorization,
        ImmutableArray<BaseStudioGrantRequirement> grants, HPDBaseStudioAuthoritySnapshot authority, string pageId,
        Func<HPDBaseStudioAuthoritySnapshot, BaseStudioResourceIdentity, bool> exists)
        : ProducerBase(principals, authorization, grants), IBaseStudioResourceProducer
    {
        public async ValueTask<BaseStudioCanonicalJson?> ResolveAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false)) return null;
            try
            {
                using JsonDocument document = JsonDocument.Parse(invocation.Request.ToArray());
                if (!document.RootElement.TryGetProperty("resourceToken", out JsonElement token) ||
                    !BaseStudioResourceRouteToken.TryDecode(token.GetString(), out BaseStudioResourceIdentity? resource) || resource is null ||
                    resource.ApplicationId != authority.ApplicationId || !exists(authority, resource)) return null;
                return BaseStudioResolvedResourceJson.Encode(resource, BaseStudioResolvedRoute.Create(pageId,
                    [new KeyValuePair<string, string>("resource", BaseStudioResourceRouteToken.Encode(resource))]), [], 1_048_576);
            }
            catch (JsonException) { return null; }
        }
    }

    private sealed class PolicyExplainProducer(IBaseStudioPrincipalContextResolver principals, BaseStudioAuthorization authorization,
        ImmutableArray<BaseStudioGrantRequirement> grants, HPDBaseStudioAuthoritySnapshot authority, IBasePolicyOrchestrator policy,
        string viewId, bool list) : ProducerBase(principals, authorization, grants), IBaseStudioViewProducer
    {
        public async ValueTask<BaseStudioCanonicalJson?> ReadAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false)) return null;
            var context = await ContextAsync(invocation, cancellationToken).ConfigureAwait(false); if (context is null) return null;
            try
            {
                using JsonDocument document = JsonDocument.Parse(invocation.Request.ToArray()); JsonElement root = document.RootElement;
                JsonElement outward = root.GetProperty("resource"); var resource = new BaseStudioPolicyResource(outward.GetProperty("applicationId").GetString()!, outward.GetProperty("policyId").GetString()!, int.Parse(outward.GetProperty("version").GetString()!, CultureInfo.InvariantCulture));
                if (resource.ApplicationId != authority.ApplicationId || !authority.Policies.Any(value => value.Id == resource.PolicyId && value.Version == resource.Version) || outward.GetProperty("authorityChecksum").GetString() != HexBytes(resource.AuthorityChecksum.ToArray())) return null;
                string operationId = root.GetProperty("operationId").GetString()!; string targetKind = root.GetProperty("targetResourceKind").GetString()!; string targetToken = root.GetProperty("targetResourceToken").GetString()!;
                if (string.IsNullOrWhiteSpace(operationId) || operationId.Length > 512 || string.IsNullOrWhiteSpace(targetKind) || targetKind.Length > 128 || !BaseStudioResourceRouteToken.TryDecode(targetToken, out BaseStudioResourceIdentity? target) || target is null || target.ApplicationId != authority.ApplicationId || !string.Equals(target.Kind.ToString(), targetKind, StringComparison.OrdinalIgnoreCase)) return null;
                OperationResult<BasePolicyEvaluation> evaluated = await policy.EvaluateStudioAsync(new BaseStudioPolicyRequest { Principal = context.Value.Principal, Operation = context.Value.Operation, StudioOperationId = operationId, StudioModuleId = "base", StudioResourceKind = targetKind, StudioResourceIdentity = targetToken }, cancellationToken).ConfigureAwait(false);
                if (!evaluated.IsSuccess() || evaluated.Value is null || evaluated.Value.Authority is null || evaluated.Value.Authority.PolicyGraphGeneration != authority.PolicyOwnerGeneration || !CryptographicOperations.FixedTimeEquals(evaluated.Value.Authority.PolicyOwnerChecksum.AsSpan(), authority.GetPolicyOwnerChecksum())) return null;
                BasePolicyEvaluation value = evaluated.Value; BasePolicyConstraintAuthority constraints = value.Authority.Constraints; string evaluationChecksum = HexBytes(value.Authority.Checksum.ToArray()); string tokenChecksum = Hash(targetToken);
                IReadOnlyList<IReadOnlyDictionary<string, string>> rows = viewId switch
                {
                    "base.policy.explain.operation.detail" => [D(("operationId", operationId), ("effect", value.Decision.Effect.ToString()), ("outcome", value.Decision.Outcome.ToString()), ("evaluationChecksum", evaluationChecksum))],
                    "base.policy.explain.resource.detail" => [D(("targetResourceKind", targetKind), ("targetResourceTokenChecksum", tokenChecksum), ("effect", value.Decision.Effect.ToString()), ("evaluationChecksum", evaluationChecksum))],
                    "base.policy.explain.filters.list" => [D(("filterKind", "recordFilter"), ("filterPresent", (constraints.EffectiveRecordFilter is not null).ToString().ToLowerInvariant()), ("authorityClass", "normalizedConstraint"), ("evaluationChecksum", evaluationChecksum)), D(("filterKind", "writeCheck"), ("filterPresent", (constraints.EffectiveWriteCheck is not null).ToString().ToLowerInvariant()), ("authorityClass", "normalizedConstraint"), ("evaluationChecksum", evaluationChecksum))],
                    "base.policy.explain.constraints.detail" => [D(("effect", value.Decision.Effect.ToString()), ("outcome", value.Decision.Outcome.ToString()), ("constraintCount", new[] { constraints.EffectiveRecordFilter, constraints.EffectiveWriteCheck }.Count(static x => x is not null).ToString(CultureInfo.InvariantCulture)), ("evaluationChecksum", evaluationChecksum))],
                    "base.policy.explain.masks.detail" => [D(("readMaskPresent", (constraints.EffectiveReadMask is not null).ToString().ToLowerInvariant()), ("writeMaskPresent", (constraints.EffectiveWriteMask is not null).ToString().ToLowerInvariant()), ("authorityClass", "normalizedConstraint"), ("evaluationChecksum", evaluationChecksum))],
                    "base.policy.explain.disclosure.detail" => [D(("reasonCode", value.Decision.ReasonCode ?? "none"), ("safeMessageAvailable", (value.Decision.SafeMessage is not null).ToString().ToLowerInvariant()), ("nativeAuditExposed", "false"), ("evaluationChecksum", evaluationChecksum))],
                    "base.policy.explain.decision.detail" => [D(("effect", value.Decision.Effect.ToString()), ("outcome", value.Decision.Outcome.ToString()), ("policyOwnerGeneration", authority.PolicyOwnerGeneration.ToString(CultureInfo.InvariantCulture)), ("evaluationChecksum", evaluationChecksum))], _ => []
                };
                if (!list && rows.Count != 1) return null; BaseStudioCanonicalJson encoded = Encode(rows, list);
                return BaseStudioObservationJson.Current(resource, BaseStudioGraphObservationAuthority.Create(invocation.Authority), encoded, [], [], Accounting(encoded.ToArray().Length), 1_048_576);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or OverflowException) { return null; }
        }
        private static Dictionary<string, string> D(params (string Key, string Value)[] values) => values.ToDictionary(static value => value.Key, static value => value.Value, StringComparer.Ordinal);
        private static string Hash(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        private static string HexBytes(IEnumerable<byte> value) => Convert.ToHexString(value.ToArray()).ToLowerInvariant();
    }

    private static bool TryResource(BaseStudioCanonicalJson request, out BaseStudioResourceIdentity? resource)
    {
        resource = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(request.ToArray()); JsonElement value = document.RootElement.GetProperty("resource");
            string applicationId = value.GetProperty("applicationId").GetString()!; string kind = value.GetProperty("kind").GetString()!;
            resource = kind switch
            {
                "application" => new BaseStudioApplicationResource(applicationId),
                "policy" => new BaseStudioPolicyResource(applicationId, value.GetProperty("policyId").GetString()!, int.Parse(value.GetProperty("version").GetString()!, CultureInfo.InvariantCulture)),
                "grant" => new BaseStudioGrantResource(applicationId, value.GetProperty("grantId").GetString()!, int.Parse(value.GetProperty("version").GetString()!, CultureInfo.InvariantCulture)),
                _ => null,
            };
            return resource is not null && value.GetProperty("authorityChecksum").GetString() == Convert.ToHexString(resource.AuthorityChecksum.ToArray()).ToLowerInvariant();
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or OverflowException) { resource = null; return false; }
    }

    private static BaseStudioCanonicalJson Encode(IReadOnlyList<IReadOnlyDictionary<string, string>> rows, bool list)
    {
        var buffer = new ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer);
        if (list) writer.WriteStartArray();
        foreach (IReadOnlyDictionary<string, string> row in rows)
        {
            writer.WriteStartObject(); foreach ((string key, string value) in row.OrderBy(static value => value.Key, StringComparer.Ordinal)) writer.WriteString(key, value); writer.WriteEndObject();
        }
        if (list) writer.WriteEndArray(); writer.Flush(); return BaseStudioCanonicalJson.Create(buffer.WrittenSpan, 1_048_576);
    }
}
