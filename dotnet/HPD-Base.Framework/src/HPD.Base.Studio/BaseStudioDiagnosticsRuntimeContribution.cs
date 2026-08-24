using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HPD.AI.Platform.Studio;

namespace HPD.Base.Studio;

internal sealed partial class BaseStudioRuntimeContributionFactory
{
    private void AddDiagnosticsRuntime(BaseStudioModuleRegistration module, List<BaseStudioNamedTypeContract> types,
        List<BaseStudioEndpointContract> endpoints, List<BaseStudioMethodBinding> methods, List<BaseStudioProducerBinding> producers,
        BaseStudioNamedTypeContract error, BaseStudioNamedTypeContract text, BaseStudioNamedTypeContract checksum, BaseStudioNamedTypeContract decimalLong,
        BaseStudioNamedTypeContract currentKind, BaseStudioNamedTypeContract graphAuthority, BaseStudioNamedTypeContract accounting,
        BaseStudioNamedTypeContract emptyItems, BaseStudioNamedTypeContract tokenRequest, BaseStudioNamedTypeContract resourceParameters,
        BaseStudioNamedTypeContract resourceRoute, BaseStudioNamedTypeContract resolvedKind)
    {
        BaseStudioNamedTypeContract application = Identity(BaseStudioResourceKind.Application, []);
        BaseStudioNamedTypeContract health = Identity(BaseStudioResourceKind.Health, [("contributorId", text), ("entryId", text)]);
        BaseStudioNamedTypeContract diagnostic = Identity(BaseStudioResourceKind.Diagnostic, [("contributorId", text), ("entryId", text)]);
        Add(application); Add(health); Add(diagnostic);
        foreach (BaseStudioPageRegistration page in module.Pages.Where(static page => page.PageId is "base.diagnostics" or "base.health.detail" or "base.diagnostic.detail"))
        foreach (BaseStudioSectionRegistration section in page.Presentation.Sections)
        foreach (string viewId in section.ViewIds)
        {
            BaseStudioResourceKind requestKind = page.PageId == "base.diagnostics" ? BaseStudioResourceKind.Application : page.AcceptedResources[0];
            BaseStudioNamedTypeContract resource = requestKind == BaseStudioResourceKind.Application ? application : requestKind == BaseStudioResourceKind.Health ? health : diagnostic;
            BaseStudioViewRegistration view = module.Views.Single(value => value.ViewId == viewId);
            BaseStudioNamedTypeContract request = Type(view.RequestNodeId, Obj(P("resource", resource)));
            BaseStudioNamedTypeContract item = Type(view.ItemNodeId, BaseStudioDiagnosticsContracts.ItemDescriptor(viewId));
            if (!BaseStudioSha256.FixedTimeEquals(request.NodeChecksum, view.RequestNodeChecksum) || !BaseStudioSha256.FixedTimeEquals(item.NodeChecksum, view.ItemNodeChecksum)) throw new InvalidOperationException("A Diagnostics view differs from its graph-owned L41 node.");
            bool list = viewId.EndsWith(".list", StringComparison.Ordinal); string prefix = viewId.ToLowerInvariant();
            BaseStudioNamedTypeContract value = list ? Type(prefix + ".items", $"{{\"kind\":\"array\",\"elementTypeId\":\"{item.TypeId}\",\"minItems\":0,\"maxItems\":{view.MaximumItems}}}") : item;
            BaseStudioNamedTypeContract result = Type(prefix + ".current", Obj(P("accounting", accounting), P("evidence", emptyItems), P("kind", currentKind), P("links", emptyItems), P("observationAuthority", graphAuthority), P("resource", resource), P("value", value)));
            Add(request); Add(item); if (list) Add(value); Add(result);
            string methodId = "base.studio.view." + viewId; string endpointId = PageEndpoint + "." + viewId;
            endpoints.Add(Endpoint(endpointId, "/base/studio/views/" + viewId, request, result)); methods.Add(BaseStudioMethodBinding.Create(methodId, BaseStudioMethodKind.Page, "base", page.PageId, endpointId, request.TypeId, result.TypeId));
            producers.Add(new BaseStudioViewProducerBinding(methodId, new DiagnosticsSectionProducer(_principals, _authorization, page.Grants, _health, _diagnostics, _healthContributors, _diagnosticContributors, viewId, list, requestKind)));
        }
        AddResolver(BaseStudioResourceKind.Health, health, "base.health.detail"); AddResolver(BaseStudioResourceKind.Diagnostic, diagnostic, "base.diagnostic.detail");
        void AddResolver(BaseStudioResourceKind kind, BaseStudioNamedTypeContract resource, string pageId)
        {
            BaseStudioResourceRegistration registration = module.Resources.Single(value => value.Kind == kind); string suffix = kind.ToString().ToLowerInvariant();
            string methodId = "base.studio.resolve." + suffix; string endpointId = ResolveEndpoint + "." + suffix;
            BaseStudioNamedTypeContract result = Type("base.studio." + suffix + "-resolved", Obj(P("kind", resolvedKind), P("links", emptyItems), P("resource", resource), P("route", resourceRoute))); Add(result);
            endpoints.Add(Endpoint(endpointId, "/base/studio/resources/" + suffix, tokenRequest, result)); methods.Add(BaseStudioMethodBinding.Create(methodId, BaseStudioMethodKind.Resolve, "base", registration.ResolverId, endpointId, tokenRequest.TypeId, result.TypeId));
            producers.Add(new BaseStudioResourceProducerBinding(methodId, new DiagnosticsResolver(_principals, _authorization, registration.Grants, _health, _diagnostics, _healthContributors, _diagnosticContributors, kind, pageId)));
        }
        BaseStudioNamedTypeContract Identity(BaseStudioResourceKind resourceKind, (string Name, BaseStudioNamedTypeContract Type)[] extra)
        {
            string literal = resourceKind.ToString(); literal = char.ToLowerInvariant(literal[0]) + literal[1..]; BaseStudioNamedTypeContract kind = Type("base.studio.resource-kind." + literal.ToLowerInvariant(), $"{{\"kind\":\"literal\",\"value\":\"{literal}\"}}"); Add(kind);
            return Type("base.studio.resource." + literal.ToLowerInvariant(), Obj([P("applicationId", text), P("authorityChecksum", checksum), .. extra.Select(value => P(value.Name, value.Type)), P("kind", kind)]));
        }
        void Add(BaseStudioNamedTypeContract value) { if (!types.Any(item => item.TypeId == value.TypeId)) types.Add(value); }
        BaseStudioEndpointContract Endpoint(string id, string route, BaseStudioNamedTypeContract request, BaseStudioNamedTypeContract result) => BaseStudioEndpointContract.Create(id, 1, BaseStudioTransportMethod.Post, route, BaseStudioEndpointAudience.ControlPlane, BaseStudioTransportKind.SameOriginHttp, request.TypeId, request.NodeChecksum, result.TypeId, result.NodeChecksum, error.TypeId, error.NodeChecksum, 16_384, 1_048_576, TimeSpan.FromSeconds(10));
        static string P(string name, BaseStudioNamedTypeContract type) => $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type.TypeId}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}";
        static string Obj(params string[] properties) => $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', properties.Order(StringComparer.Ordinal))}],\"additionalProperties\":false}}";
    }

    private sealed class DiagnosticsSectionProducer(IBaseStudioPrincipalContextResolver principals, BaseStudioAuthorization authorization,
        ImmutableArray<BaseStudioGrantRequirement> grants, IBaseHealthProvider health, IBaseDiagnosticProvider diagnostics,
        ImmutableArray<IBaseHealthContributor> healthContributors, ImmutableArray<IBaseDiagnosticContributor> diagnosticContributors,
        string viewId, bool list, BaseStudioResourceKind requestKind) : ProducerBase(principals, authorization, grants), IBaseStudioViewProducer
    {
        public async ValueTask<BaseStudioCanonicalJson?> ReadAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) || !TryDiagnosticsResource(invocation.Request, requestKind, out BaseStudioResourceIdentity? resource) || resource is null) return null;
            var context = await ContextAsync(invocation, cancellationToken).ConfigureAwait(false); if (context is null) return null;
            OperationResult<HealthDescriptor[]> healthResult = await health.GetHealthAsync(context.Value.Principal, context.Value.Operation, VisibilityLevel.Admin, cancellationToken).ConfigureAwait(false);
            OperationResult<DiagnosticDescriptor[]> diagnosticResult = await diagnostics.GetDiagnosticsAsync(context.Value.Principal, context.Value.Operation, VisibilityLevel.Admin, cancellationToken).ConfigureAwait(false);
            if (!healthResult.IsSuccess() || healthResult.Value is null || !diagnosticResult.IsSuccess() || diagnosticResult.Value is null) return null;
            (string Contributor, HealthDescriptor Value)[] healthRows = await VisibleHealth(healthContributors, healthResult.Value, cancellationToken).ConfigureAwait(false);
            (string Contributor, DiagnosticDescriptor Value)[] diagnosticRows = await VisibleDiagnostics(diagnosticContributors, diagnosticResult.Value, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<IReadOnlyDictionary<string, string>> rows = Rows(resource, healthRows, diagnosticRows);
            if (!list && rows.Count != 1) return null; BaseStudioCanonicalJson value = Encode(rows, list);
            return BaseStudioObservationJson.Current(resource, BaseStudioGraphObservationAuthority.Create(invocation.Authority), value, [], [], Accounting(value.ToArray().Length), 1_048_576);
        }
        private IReadOnlyList<IReadOnlyDictionary<string, string>> Rows(BaseStudioResourceIdentity resource, (string Contributor, HealthDescriptor Value)[] healthRows, (string Contributor, DiagnosticDescriptor Value)[] diagnosticRows)
        {
            if (viewId == "base.diagnostics.incidents.list") return diagnosticRows.Select(DiagnosticSummary).ToArray();
            if (viewId == "base.diagnostics.health.list") return healthRows.Select(HealthSummary).ToArray();
            if (viewId == "base.diagnostics.accounting.detail") return [D(("healthContributorCount", L(healthContributors.Length)), ("diagnosticContributorCount", L(diagnosticContributors.Length)), ("healthEntryCount", L(healthRows.Length)), ("diagnosticEntryCount", L(diagnosticRows.Length)), ("capturedAtUtc", healthRows.Select(static value => value.Value.CheckedAt).Concat(diagnosticRows.Select(static value => value.Value.EmittedAt)).DefaultIfEmpty(DateTimeOffset.UnixEpoch).Max().ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture)), ("entryChecksum", Hash("accounting", healthRows.Length.ToString(), diagnosticRows.Length.ToString())))];
            if (resource is BaseStudioHealthResource healthResource)
            {
                var entry = healthRows.Single(value => value.Contributor == healthResource.ContributorId && value.Value.Id == healthResource.EntryId); string checksum = HealthChecksum(entry);
                if (viewId == "base.health.detail.dependencies.list") return (entry.Value.Dependencies ?? []).Select(value => D(("dependencyId", value.Id), ("dependencyKind", value.Kind), ("dependencyStatus", value.Status.ToString()), ("entryChecksum", checksum))).ToArray();
                return viewId switch
                {
                    "base.health.detail.summary.detail" => [D(("contributorId", entry.Contributor), ("entryId", entry.Value.Id), ("scope", entry.Value.Scope.ToString()), ("status", entry.Value.Status.ToString()), ("targetRef", entry.Value.TargetRef ?? "none"), ("checkedAtUtc", U(entry.Value.CheckedAt)), ("entryChecksum", checksum))],
                    "base.health.detail.history.list" => [D(("contributorId", entry.Contributor), ("entryId", entry.Value.Id), ("status", entry.Value.Status.ToString()), ("checkedAtUtc", U(entry.Value.CheckedAt)), ("historyClass", "currentObservationOnly"), ("entryChecksum", checksum))],
                    "base.health.detail.remediation.detail" => [D(("contributorId", entry.Contributor), ("entryId", entry.Value.Id), ("remediationClass", "noRegisteredTypedAction"), ("typedActionAvailable", "false"), ("entryChecksum", checksum))], _ => []
                };
            }
            if (resource is BaseStudioDiagnosticResource diagnosticResource)
            {
                var entry = diagnosticRows.Single(value => value.Contributor == diagnosticResource.ContributorId && value.Value.Id == diagnosticResource.EntryId); string checksum = DiagnosticChecksum(entry);
                if (viewId == "base.diagnostic.detail.affectedResources.list") return (entry.Value.RelatedFeatureIds ?? []).Select(value => D(("featureId", value), ("relation", "relatedFeature"), ("entryChecksum", checksum))).ToArray();
                return viewId switch
                {
                    "base.diagnostic.detail.summary.detail" => [DiagnosticSummary(entry)],
                    "base.diagnostic.detail.correlation.detail" => [D(("contributorId", entry.Contributor), ("entryId", entry.Value.Id), ("targetRef", entry.Value.TargetRef ?? "none"), ("targetPath", entry.Value.TargetPath ?? "none"), ("correlationClass", "contributorTarget"), ("entryChecksum", checksum))],
                    "base.diagnostic.detail.accounting.detail" => [D(("contributorReads", "1"), ("aggregateReads", "1"), ("projectedFields", "6"), ("nativeMessageFields", "0"), ("entryChecksum", checksum))],
                    "base.diagnostic.detail.evidence.detail" => [D(("contributorId", entry.Contributor), ("entryId", entry.Value.Id), ("code", entry.Value.Code), ("emittedAtUtc", U(entry.Value.EmittedAt)), ("visibility", entry.Value.Visibility.ToString()), ("entryChecksum", checksum))], _ => []
                };
            }
            return [];
        }
    }

    private sealed class DiagnosticsResolver(IBaseStudioPrincipalContextResolver principals, BaseStudioAuthorization authorization,
        ImmutableArray<BaseStudioGrantRequirement> grants, IBaseHealthProvider health, IBaseDiagnosticProvider diagnostics,
        ImmutableArray<IBaseHealthContributor> healthContributors, ImmutableArray<IBaseDiagnosticContributor> diagnosticContributors,
        BaseStudioResourceKind kind, string pageId) : ProducerBase(principals, authorization, grants), IBaseStudioResourceProducer
    {
        public async ValueTask<BaseStudioCanonicalJson?> ResolveAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false)) return null; var context = await ContextAsync(invocation, cancellationToken).ConfigureAwait(false); if (context is null) return null;
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(invocation.Request.ToArray()); if (!BaseStudioResourceRouteToken.TryDecode(document.RootElement.GetProperty("resourceToken").GetString(), out BaseStudioResourceIdentity? resource) || resource is null || resource.Kind != kind) return null;
                bool exists;
                if (resource is BaseStudioHealthResource h) { OperationResult<HealthDescriptor[]> aggregate = await health.GetHealthAsync(context.Value.Principal, context.Value.Operation, VisibilityLevel.Admin, cancellationToken).ConfigureAwait(false); exists = aggregate.IsSuccess() && aggregate.Value is not null && (await VisibleHealth(healthContributors, aggregate.Value, cancellationToken).ConfigureAwait(false)).Any(value => value.Contributor == h.ContributorId && value.Value.Id == h.EntryId); }
                else if (resource is BaseStudioDiagnosticResource d) { OperationResult<DiagnosticDescriptor[]> aggregate = await diagnostics.GetDiagnosticsAsync(context.Value.Principal, context.Value.Operation, VisibilityLevel.Admin, cancellationToken).ConfigureAwait(false); exists = aggregate.IsSuccess() && aggregate.Value is not null && (await VisibleDiagnostics(diagnosticContributors, aggregate.Value, cancellationToken).ConfigureAwait(false)).Any(value => value.Contributor == d.ContributorId && value.Value.Id == d.EntryId); }
                else return null; if (!exists) return null;
                return BaseStudioResolvedResourceJson.Encode(resource, BaseStudioResolvedRoute.Create(pageId, [new KeyValuePair<string, string>("resource", BaseStudioResourceRouteToken.Encode(resource))]), [], 1_048_576);
            }
            catch (System.Text.Json.JsonException) { return null; }
        }
    }

    private static async ValueTask<(string Contributor, HealthDescriptor Value)[]> VisibleHealth(ImmutableArray<IBaseHealthContributor> contributors, HealthDescriptor[] aggregate, CancellationToken token)
    { var rows = new List<(string, HealthDescriptor)>(); foreach (IBaseHealthContributor contributor in contributors) foreach (HealthDescriptor value in await contributor.GetHealthAsync(token).ConfigureAwait(false)) if (aggregate.Any(item => item.Id == value.Id && item.CheckedAt == value.CheckedAt && item.Status == value.Status)) rows.Add((contributor.Id, value)); return [.. rows.OrderBy(static value => value.Item1, StringComparer.Ordinal).ThenBy(static value => value.Item2.Id, StringComparer.Ordinal)]; }
    private static async ValueTask<(string Contributor, DiagnosticDescriptor Value)[]> VisibleDiagnostics(ImmutableArray<IBaseDiagnosticContributor> contributors, DiagnosticDescriptor[] aggregate, CancellationToken token)
    { var rows = new List<(string, DiagnosticDescriptor)>(); foreach (IBaseDiagnosticContributor contributor in contributors) foreach (DiagnosticDescriptor value in await contributor.GetDiagnosticsAsync(token).ConfigureAwait(false)) if (aggregate.Any(item => item.Id == value.Id && item.EmittedAt == value.EmittedAt && item.Code == value.Code && item.Severity == value.Severity)) rows.Add((contributor.Id, value)); return [.. rows.OrderBy(static value => value.Item1, StringComparer.Ordinal).ThenBy(static value => value.Item2.Id, StringComparer.Ordinal)]; }
    private static IReadOnlyDictionary<string, string> HealthSummary((string Contributor, HealthDescriptor Value) value) => D(("contributorId", value.Contributor), ("entryId", value.Value.Id), ("scope", value.Value.Scope.ToString()), ("status", value.Value.Status.ToString()), ("checkedAtUtc", U(value.Value.CheckedAt)), ("entryChecksum", HealthChecksum(value)));
    private static IReadOnlyDictionary<string, string> DiagnosticSummary((string Contributor, DiagnosticDescriptor Value) value) => D(("contributorId", value.Contributor), ("entryId", value.Value.Id), ("code", value.Value.Code), ("severity", value.Value.Severity.ToString()), ("category", value.Value.Category.ToString()), ("emittedAtUtc", U(value.Value.EmittedAt)), ("entryChecksum", DiagnosticChecksum(value)));
    private static string HealthChecksum((string Contributor, HealthDescriptor Value) value) => Hash(value.Contributor, value.Value.Id, value.Value.Status.ToString(), U(value.Value.CheckedAt));
    private static string DiagnosticChecksum((string Contributor, DiagnosticDescriptor Value) value) => Hash(value.Contributor, value.Value.Id, value.Value.Code, value.Value.Severity.ToString(), U(value.Value.EmittedAt));
    private static Dictionary<string, string> D(params (string Key, string Value)[] values) => values.ToDictionary(static value => value.Key, static value => value.Value, StringComparer.Ordinal);
    private static string L(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string U(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    private static string Hash(params string[] values) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', values)))).ToLowerInvariant();
    private static bool TryDiagnosticsResource(BaseStudioCanonicalJson request, BaseStudioResourceKind expected, out BaseStudioResourceIdentity? resource)
    { resource = null; try { using var document = System.Text.Json.JsonDocument.Parse(request.ToArray()); var value = document.RootElement.GetProperty("resource"); string app = value.GetProperty("applicationId").GetString()!; resource = expected switch { BaseStudioResourceKind.Application => new BaseStudioApplicationResource(app), BaseStudioResourceKind.Health => new BaseStudioHealthResource(app, value.GetProperty("contributorId").GetString()!, value.GetProperty("entryId").GetString()!), BaseStudioResourceKind.Diagnostic => new BaseStudioDiagnosticResource(app, value.GetProperty("contributorId").GetString()!, value.GetProperty("entryId").GetString()!), _ => null }; return resource is not null && value.GetProperty("authorityChecksum").GetString() == H(resource.AuthorityChecksum.ToArray()); } catch (Exception exception) when (exception is System.Text.Json.JsonException or ArgumentException) { resource = null; return false; } }
}
