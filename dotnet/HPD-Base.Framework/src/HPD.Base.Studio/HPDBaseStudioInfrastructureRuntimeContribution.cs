using System.Collections.Immutable;
using System.Globalization;
using HPD.AI.Platform.Studio;

namespace HPD.Base.Studio;

internal sealed partial class HPDBaseStudioRuntimeContributionFactory
{
    private static readonly string[] InfrastructurePages = ["base.infrastructure", "base.store.detail", "base.provider.detail", "base.schema.detail", "base.migration.detail", "base.backup.detail", "base.restore.detail", "base.maintenance.detail"];

    private void AddInfrastructureRuntime(StudioModuleRegistration module, List<StudioNamedTypeContract> types,
        List<StudioEndpointContract> endpoints, List<StudioMethodBinding> methods, List<StudioProducerBinding> producers,
        StudioNamedTypeContract error, StudioNamedTypeContract text, StudioNamedTypeContract checksum,
        StudioNamedTypeContract decimalLong, StudioNamedTypeContract currentKind, StudioNamedTypeContract graphAuthority,
        StudioNamedTypeContract accounting, StudioNamedTypeContract emptyItems, StudioNamedTypeContract tokenRequest,
        StudioNamedTypeContract resourceParameters, StudioNamedTypeContract resourceRoute, StudioNamedTypeContract resolvedKind)
    {
        var identities = new Dictionary<StudioResourceKind, StudioNamedTypeContract>
        {
            [StudioResourceKind.Application] = Type("base.studio.resource.application", Identity("application")),
            [StudioResourceKind.Store] = Type("base.studio.resource.store", Identity("store", ("storeIdentity", text))),
            [StudioResourceKind.Provider] = Type("base.studio.resource.provider", Identity("provider", ("providerId", text), ("providerVersion", decimalLong), ("storeIdentity", text))),
            [StudioResourceKind.CertificationReceipt] = Type("base.studio.resource.certificationreceipt", Identity("certificationReceipt", ("certificationKind", text), ("contractChecksum", checksum), ("providerId", text), ("providerVersion", decimalLong))),
            [StudioResourceKind.Schema] = Type("base.studio.resource.schema", Identity("schema", ("schemaGeneration", decimalLong), ("storeIdentity", text))),
            [StudioResourceKind.Migration] = Type("base.studio.resource.migration", Identity("migration", ("migrationId", text), ("storeIdentity", text))),
            [StudioResourceKind.Backup] = Type("base.studio.resource.backup", Identity("backup", ("artifactId", text), ("storeIdentity", text))),
            [StudioResourceKind.Restore] = Type("base.studio.resource.restore", Identity("restore", ("restoreRequestIdentity", text), ("storeIdentity", text))),
            [StudioResourceKind.Maintenance] = Type("base.studio.resource.maintenance", Identity("maintenance", ("maintenanceKind", text), ("operationIdentity", text), ("storeIdentity", text))),
            [StudioResourceKind.QuarantineItem] = Type("base.studio.resource.quarantineitem", Identity("quarantineItem", ("owningSubsystemId", text), ("quarantineIdentity", text), ("quarantineKind", text))),
        };
        foreach (StudioNamedTypeContract identity in identities.Values) AddType(identity);
        foreach (StudioPageRegistration page in module.Pages.Where(page => InfrastructurePages.Contains(page.PageId, StringComparer.Ordinal)))
        foreach (StudioSectionRegistration section in page.Presentation.Sections)
        foreach (string viewId in section.ViewIds)
        {
            StudioViewRegistration view = module.Views.Single(value => value.ViewId == viewId);
            StudioResourceKind requestKind = page.Presentation.NavigationRole == StudioNavigationRole.AreaLanding ? StudioResourceKind.Application : page.AcceptedResources[0];
            StudioNamedTypeContract requestResource = identities[requestKind];
            StudioNamedTypeContract request = Type(view.RequestNodeId, Obj(P("resource", requestResource)));
            StudioNamedTypeContract item = Type(view.ItemNodeId, HPDBaseStudioInfrastructureContracts.ItemDescriptor(viewId));
            if (!StudioSha256.FixedTimeEquals(request.NodeChecksum, view.RequestNodeChecksum) || !StudioSha256.FixedTimeEquals(item.NodeChecksum, view.ItemNodeChecksum))
                throw new InvalidOperationException("An Infrastructure view differs from its graph-owned L41 node.");
            bool list = viewId.EndsWith(".list", StringComparison.Ordinal);
            string typePrefix = viewId.ToLowerInvariant();
            StudioNamedTypeContract value = list ? Type(typePrefix + ".items", $"{{\"kind\":\"array\",\"elementTypeId\":\"{item.TypeId}\",\"minItems\":0,\"maxItems\":{view.MaximumItems}}}") : item;
            StudioNamedTypeContract result = Type(typePrefix + ".current", Obj(P("accounting", accounting), P("evidence", emptyItems), P("kind", currentKind), P("links", emptyItems), P("observationAuthority", graphAuthority), P("resource", requestResource), P("value", value)));
            AddType(request); AddType(item); if (list) AddType(value); AddType(result);
            string methodId = "base.studio.view." + viewId; string endpointId = PageEndpoint + "." + viewId;
            endpoints.Add(Endpoint(endpointId, "/base/studio/views/" + viewId, request, result));
            methods.Add(StudioMethodBinding.Create(methodId, StudioMethodKind.Page, "base", page.PageId, endpointId, request.TypeId, result.TypeId));
            producers.Add(new StudioViewProducerBinding(methodId, new InfrastructureSectionProducer(_principals, _authorization, page.Grants,
                _baseAuthority, _dynamicStore, _infrastructure, _control, _health, _diagnostics, viewId, list, requestKind)));
        }
        foreach ((StudioResourceKind kind, string pageId) in new[]
        {
            (StudioResourceKind.Store, "base.store.detail"),
            (StudioResourceKind.Provider, "base.provider.detail"),
            (StudioResourceKind.CertificationReceipt, "base.provider.detail"),
            (StudioResourceKind.Schema, "base.schema.detail"), (StudioResourceKind.Migration, "base.migration.detail"),
            (StudioResourceKind.Backup, "base.backup.detail"), (StudioResourceKind.Restore, "base.restore.detail"),
            (StudioResourceKind.Maintenance, "base.maintenance.detail")
            ,(StudioResourceKind.QuarantineItem, "base.maintenance.detail")
        })
        {
            StudioResourceRegistration registration = module.Resources.Single(value => value.Kind == kind);
            string suffix = kind.ToString().ToLowerInvariant(); string methodId = "base.studio.resolve." + suffix; string endpointId = ResolveEndpoint + "." + suffix;
            StudioNamedTypeContract result = Type("base.studio." + suffix + "-resolved", Obj(P("kind", resolvedKind), P("links", emptyItems), P("resource", identities[kind]), P("route", resourceRoute)));
            AddType(result); endpoints.Add(Endpoint(endpointId, "/base/studio/resources/" + suffix, tokenRequest, result));
            methods.Add(StudioMethodBinding.Create(methodId, StudioMethodKind.Resolve, "base", registration.ResolverId, endpointId, tokenRequest.TypeId, result.TypeId));
            producers.Add(new StudioResourceProducerBinding(methodId, new InfrastructureResolver(_principals, _authorization, registration.Grants,
                _baseAuthority, _dynamicStore, _infrastructure, _control, kind, pageId)));
        }
        string Identity(string literal, params (string Name, StudioNamedTypeContract Type)[] extra)
        {
            StudioNamedTypeContract kind = Type("base.studio.resource-kind." + literal.ToLowerInvariant(), $"{{\"kind\":\"literal\",\"value\":\"{literal}\"}}"); AddType(kind);
            return Obj([P("applicationId", text), P("authorityChecksum", checksum), .. extra.Select(value => P(value.Name, value.Type)), P("kind", kind)]);
        }
        void AddType(StudioNamedTypeContract value) { if (!types.Any(item => item.TypeId == value.TypeId)) types.Add(value); }
        StudioEndpointContract Endpoint(string id, string route, StudioNamedTypeContract request, StudioNamedTypeContract result) => StudioEndpointContract.Create(id, 1, StudioTransportMethod.Post, route, StudioEndpointAudience.ControlPlane, StudioTransportKind.SameOriginHttp, request.TypeId, request.NodeChecksum, result.TypeId, result.NodeChecksum, error.TypeId, error.NodeChecksum, 16_384, 1_048_576, TimeSpan.FromSeconds(10));
        static string P(string name, StudioNamedTypeContract type) => $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type.TypeId}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}";
        static string Obj(params string[] properties) => $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', properties.Order(StringComparer.Ordinal))}],\"additionalProperties\":false}}";
    }

    private sealed class InfrastructureSectionProducer(IStudioPrincipalContextResolver principals, HPDBaseStudioAuthorization authorization,
        ImmutableArray<StudioGrantRequirement> grants, HPDBaseStudioAuthoritySnapshot authority, IStudioDynamicStoreAuthoritySource dynamicStore,
        IStudioInfrastructureInventoryStore inventory, IStudioControlInspectionStore control, IBaseHealthProvider health, IBaseDiagnosticProvider diagnostics,
        string viewId, bool list, StudioResourceKind requestKind)
        : ProducerBase(principals, authorization, grants), IStudioViewProducer
    {
        public async ValueTask<StudioCanonicalJson?> ReadAsync(StudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) || !TryInfrastructureResource(invocation.Request, requestKind, out StudioResourceIdentity? resource) || resource is null) return null;
            StudioDynamicStoreAuthority? store = await CaptureStore(dynamicStore, authority.ApplicationId, cancellationToken).ConfigureAwait(false); if (store is null) return null;
            if (viewId.StartsWith("base.store.detail.", StringComparison.Ordinal) || viewId.StartsWith("base.provider.detail.", StringComparison.Ordinal))
            {
                IReadOnlyList<IReadOnlyDictionary<string, string>>? exact = await StoreProviderRows(invocation, resource, store, cancellationToken).ConfigureAwait(false);
                if (exact is null || !list && exact.Count != 1) return null; StudioCanonicalJson projected = Encode(exact, list);
                return StudioObservationJson.Current(resource, StudioGraphObservationAuthority.Create(invocation.Authority), projected, [], [], Accounting(projected.ToArray().Length), 1_048_576);
            }
            StudioInfrastructureInventoryKind? kind = ViewKind(viewId); IReadOnlyList<StudioInfrastructureItem>? items = kind is null ? [] : await Read(inventory, authority.ApplicationId, store, kind.Value, cancellationToken).ConfigureAwait(false);
            if (items is null) return null;
            IEnumerable<StudioInfrastructureItem> selected = Select(items, resource, requestKind);
            if (viewId == "base.infrastructure.attention.list") selected = selected.Where(static item => item.State is StudioInfrastructureState.Failed or StudioInfrastructureState.Indeterminate);
            IReadOnlyList<IReadOnlyDictionary<string, string>> rows = viewId == "base.infrastructure.stores.list" ? [StoreRow(store)] : selected.Select(item => Row(viewId, item)).ToArray();
            if (!list && rows.Count != 1) return null; StudioCanonicalJson value = Encode(rows, list);
            return StudioObservationJson.Current(resource, StudioGraphObservationAuthority.Create(invocation.Authority), value, [], [], Accounting(value.ToArray().Length), 1_048_576);
        }

        private async ValueTask<IReadOnlyList<IReadOnlyDictionary<string, string>>?> StoreProviderRows(StudioProducerInvocation invocation, StudioResourceIdentity resource, StudioDynamicStoreAuthority store, CancellationToken token)
        {
            if (resource is StudioStoreResource storeResource && storeResource.StoreIdentity != store.StoreInstanceId || resource is StudioProviderResource providerResource && (providerResource.StoreIdentity != store.StoreInstanceId || providerResource.ProviderId != authority.ProviderId || providerResource.ProviderVersion != authority.ProviderVersion)) return null;
            string cap = H(authority.GetProviderCapabilityChecksum()); string dynamic = H(store.EvidenceChecksum); StudioInfrastructureInventoryCapability inventoryCapability = inventory.InfrastructureInventoryCapability;
            if (viewId.EndsWith(".summary.detail", StringComparison.Ordinal)) return [viewId.StartsWith("base.store", StringComparison.Ordinal) ? new Dictionary<string,string>(StringComparer.Ordinal) { ["storeIdentity"] = store.StoreInstanceId, ["storeInstanceId"] = store.StoreInstanceId, ["restoreEpoch"] = L(store.RestoreEpoch), ["schemaGeneration"] = L(store.SchemaGeneration), ["authorityChecksum"] = dynamic } : new Dictionary<string,string>(StringComparer.Ordinal) { ["providerId"] = authority.ProviderId, ["providerVersion"] = L(authority.ProviderVersion), ["providerGeneration"] = L(authority.ProviderGeneration), ["storeIdentity"] = store.StoreInstanceId, ["authorityChecksum"] = cap }];
            if (viewId.EndsWith(".capabilities.detail", StringComparison.Ordinal)) return [new Dictionary<string,string>(StringComparer.Ordinal) { ["providerId"] = authority.ProviderId, ["providerVersion"] = L(authority.ProviderVersion), ["providerGeneration"] = L(authority.ProviderGeneration), ["capabilityChecksum"] = cap, ["authorityChecksum"] = dynamic }];
            if (viewId.EndsWith(".capability.detail", StringComparison.Ordinal)) return [new Dictionary<string,string>(StringComparer.Ordinal) { ["providerId"] = authority.ProviderId, ["providerVersion"] = L(authority.ProviderVersion), ["capabilityChecksum"] = cap, ["supportedInventoryKinds"] = string.Join(',', inventoryCapability.SupportedKinds), ["authorityChecksum"] = dynamic }];
            if (viewId.EndsWith(".certification.detail", StringComparison.Ordinal)) return [new Dictionary<string,string>(StringComparer.Ordinal) { ["providerId"] = authority.ProviderId, ["providerVersion"] = L(authority.ProviderVersion), ["capabilityChecksum"] = cap, ["inventoryCertificationChecksum"] = H(inventoryCapability.CertificationChecksum), ["durableThroughBackupRestore"] = inventoryCapability.DurableThroughBackupRestore.ToString().ToLowerInvariant() }];
            if (viewId.EndsWith(".assets.detail", StringComparison.Ordinal)) return [new Dictionary<string,string>(StringComparer.Ordinal) { ["recordStoreRegistrationId"] = authority.RecordStoreRegistrationId, ["schemaDigest"] = authority.SchemaDigest, ["storeInstanceId"] = store.StoreInstanceId, ["schemaGeneration"] = L(store.SchemaGeneration), ["authorityChecksum"] = dynamic }];
            var context = await ContextAsync(invocation, token).ConfigureAwait(false); if (context is null) return null;
            if (viewId.EndsWith(".health.list", StringComparison.Ordinal)) { OperationResult<HealthDescriptor[]> result = await health.GetHealthAsync(context.Value.Principal, context.Value.Operation, VisibilityLevel.Admin, token).ConfigureAwait(false); if (!result.IsSuccess() || result.Value is null) return null; return result.Value.Where(value => value.TargetRef is null || value.TargetRef == store.StoreInstanceId || value.TargetRef == authority.ProviderId).Select(value => (IReadOnlyDictionary<string,string>)new Dictionary<string,string>(StringComparer.Ordinal) { ["entryId"] = value.Id, ["status"] = value.Status.ToString(), ["checkedAtUtc"] = StudioResponseAuthority.CanonicalUtc(value.CheckedAt.ToUniversalTime()), ["entryChecksum"] = Hash(value.Id, value.Status.ToString(), value.CheckedAt.ToString("O")) }).ToArray(); }
            if (viewId.EndsWith(".diagnostics.detail", StringComparison.Ordinal)) { OperationResult<DiagnosticDescriptor[]> result = await diagnostics.GetDiagnosticsAsync(context.Value.Principal, context.Value.Operation, VisibilityLevel.Admin, token).ConfigureAwait(false); if (!result.IsSuccess() || result.Value is null) return null; DiagnosticDescriptor[] visible = result.Value.Where(value => value.TargetRef is null || value.TargetRef == store.StoreInstanceId || value.TargetRef == authority.ProviderId).ToArray(); return [new Dictionary<string,string>(StringComparer.Ordinal) { ["diagnosticCount"] = L(visible.Length), ["highestSeverity"] = visible.Length == 0 ? "none" : visible.Max(static value => value.Severity).ToString(), ["capturedAtUtc"] = visible.Length == 0 ? StudioResponseAuthority.CanonicalUtc(invocation.Bootstrap.Authorization.Session.IssuedAtUtc) : StudioResponseAuthority.CanonicalUtc(visible.Max(static value => value.EmittedAt).ToUniversalTime()), ["nativeMessagesExposed"] = "false", ["authorityChecksum"] = dynamic }]; }
            OperationResult<StudioControlInspectionPage> quarantine = await control.ReadStudioControlFactsAsync(new() { ApplicationId = authority.ApplicationId, Kind = StudioControlFactKind.Quarantine, Take = 500, ProtectedScopeChecksum = [.. invocation.Authority.ProtectedScopeChecksum.ToArray()], Limits = new() { MaximumItems = 500, MaximumRowsRead = 100_000, MaximumEvidenceBytes = 8_388_608, MaximumTransientBytes = 8_388_608, Deadline = TimeSpan.FromSeconds(5) } }, token).ConfigureAwait(false); if (!quarantine.IsSuccess() || quarantine.Value is null) return null;
            StudioQuarantineFact[] facts = quarantine.Value.Items.OfType<StudioQuarantineFact>().ToArray();
            if (viewId.EndsWith(".retainedWork.detail", StringComparison.Ordinal)) return [new Dictionary<string,string>(StringComparer.Ordinal) { ["retainedQuarantineCount"] = L(facts.Length), ["retentionClass"] = "providerQuarantine", ["capturedAtUtc"] = StudioResponseAuthority.CanonicalUtc(invocation.Bootstrap.Authorization.Session.IssuedAtUtc), ["authorityChecksum"] = H(quarantine.Value.PageChecksum) }];
            if (viewId.EndsWith(".quarantine.list", StringComparison.Ordinal)) return facts.Select(value => (IReadOnlyDictionary<string,string>)new Dictionary<string,string>(StringComparer.Ordinal) { ["quarantineIdentity"] = value.Identity, ["quarantineKind"] = value.Quarantine.Operation, ["operationId"] = value.Quarantine.Operation, ["quarantinedAt"] = StudioResponseAuthority.CanonicalUtc(value.Quarantine.RetainedAt.ToUniversalTime()), ["itemChecksum"] = H(value.FactChecksum) }).ToArray();
            if (viewId.EndsWith(".maintenance.list", StringComparison.Ordinal)) { IReadOnlyList<StudioInfrastructureItem>? values = await Read(inventory, authority.ApplicationId, store, StudioInfrastructureInventoryKind.Maintenance, token).ConfigureAwait(false); return values?.Select(value => Row("base.infrastructure.maintenance.list", value)).ToArray(); }
            if (viewId.EndsWith(".recovery.detail", StringComparison.Ordinal)) { IReadOnlyList<StudioInfrastructureItem>? values = await Read(inventory, authority.ApplicationId, store, StudioInfrastructureInventoryKind.Restore, token).ConfigureAwait(false); if (values is null) return null; StudioRestoreItem? latest = values.OfType<StudioRestoreItem>().OrderByDescending(static value => value.Sequence).FirstOrDefault(); return [new Dictionary<string,string>(StringComparer.Ordinal) { ["restoreEpoch"] = L(store.RestoreEpoch), ["latestRestoreIdentity"] = latest?.RestoreRequestIdentity ?? "none", ["latestRestoreState"] = latest?.State.ToString() ?? "none", ["recoveryAuthorityClass"] = "restoreInventory", ["authorityChecksum"] = latest is null ? dynamic : H(latest.Checksum) }]; }
            return null;
        }
    }

    private sealed class InfrastructureResolver(IStudioPrincipalContextResolver principals, HPDBaseStudioAuthorization authorization,
        ImmutableArray<StudioGrantRequirement> grants, HPDBaseStudioAuthoritySnapshot authority, IStudioDynamicStoreAuthoritySource dynamicStore,
        IStudioInfrastructureInventoryStore inventory, IStudioControlInspectionStore control, StudioResourceKind kind, string pageId)
        : ProducerBase(principals, authorization, grants), IStudioResourceProducer
    {
        public async ValueTask<StudioCanonicalJson?> ResolveAsync(StudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false)) return null;
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(invocation.Request.ToArray());
                if (!StudioResourceRouteToken.TryDecode(document.RootElement.GetProperty("resourceToken").GetString(), out StudioResourceIdentity? resource) || resource is null || resource.Kind != kind || resource.ApplicationId != authority.ApplicationId) return null;
                StudioDynamicStoreAuthority? store = await CaptureStore(dynamicStore, authority.ApplicationId, cancellationToken).ConfigureAwait(false); if (store is null) return null;
                if (resource is StudioQuarantineItemResource)
                {
                    string quarantineIdentity = document.RootElement.GetProperty("resourceToken").GetString()!;
                    string tokenValue = quarantineIdentity.Replace('-', '+').Replace('_', '/'); tokenValue += new string('=', (4 - tokenValue.Length % 4) % 4);
                    using var decoded = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(tokenValue)); string identity = decoded.RootElement.GetProperty("quarantineIdentity").GetString()!;
                    OperationResult<StudioControlInspectionPage> page = await control.ReadStudioControlFactsAsync(new() { ApplicationId = authority.ApplicationId, Kind = StudioControlFactKind.Quarantine, Take = 500, ProtectedScopeChecksum = [.. invocation.Authority.ProtectedScopeChecksum.ToArray()], Limits = new() { MaximumItems = 500, MaximumRowsRead = 100_000, MaximumEvidenceBytes = 8_388_608, MaximumTransientBytes = 8_388_608, Deadline = TimeSpan.FromSeconds(5) } }, cancellationToken).ConfigureAwait(false);
                    if (!page.IsSuccess() || page.Value is null || !page.Value.Items.OfType<StudioQuarantineFact>().Any(value => value.Identity == identity)) return null;
                }
                else if (resource is StudioProviderResource providerResource)
                { if (providerResource.StoreIdentity != store.StoreInstanceId || providerResource.ProviderId != authority.ProviderId || providerResource.ProviderVersion != authority.ProviderVersion) return null; }
                else if (resource is StudioCertificationReceiptResource)
                {
                    string encoded = document.RootElement.GetProperty("resourceToken").GetString()!; string tokenValue = encoded.Replace('-', '+').Replace('_', '/'); tokenValue += new string('=', (4 - tokenValue.Length % 4) % 4);
                    using var decoded = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(tokenValue)); string providerId = decoded.RootElement.GetProperty("providerId").GetString()!; int providerVersion = decoded.RootElement.GetProperty("providerVersion").GetInt32(); string contractChecksum = decoded.RootElement.GetProperty("contractChecksum").GetString()!;
                    if (providerId != authority.ProviderId || providerVersion != authority.ProviderVersion || contractChecksum != H(inventory.InfrastructureInventoryCapability.CertificationChecksum)) return null;
                }
                else if (resource is StudioStoreResource storeResource)
                { if (storeResource.StoreIdentity != store.StoreInstanceId) return null; }
                else
                { IReadOnlyList<StudioInfrastructureItem>? items = await Read(inventory, authority.ApplicationId, store, ResourceKind(kind), cancellationToken).ConfigureAwait(false); if (items is null || !Select(items, resource, kind).Any()) return null; }
                return StudioResolvedResourceJson.Encode(resource, StudioResolvedRoute.Create(pageId, [new KeyValuePair<string, string>("resource", StudioResourceRouteToken.Encode(resource))]), [], 1_048_576);
            }
            catch (System.Text.Json.JsonException) { return null; }
        }
    }

    private static async ValueTask<StudioDynamicStoreAuthority?> CaptureStore(IStudioDynamicStoreAuthoritySource source, string applicationId, CancellationToken token)
    {
        StudioDynamicStoreAuthorityRequest request = new() { ApplicationId = applicationId, MaximumEvidenceBytes = 65_536, MaximumTransientBytes = 262_144, Deadline = TimeSpan.FromSeconds(5) };
        OperationResult<StudioDynamicStoreAuthority> result = await source.CaptureStudioDynamicStoreAuthorityAsync(request, token).ConfigureAwait(false);
        return result.IsSuccess() && StudioDynamicStoreAuthorityContract.IsValidResult(request, result.Value) ? result.Value : null;
    }
    private static async ValueTask<IReadOnlyList<StudioInfrastructureItem>?> Read(IStudioInfrastructureInventoryStore store, string applicationId, StudioDynamicStoreAuthority authority, StudioInfrastructureInventoryKind kind, CancellationToken token)
    {
        StudioInfrastructureInventoryCapability capability = store.InfrastructureInventoryCapability;
        var limits = new StudioInfrastructureInventoryLimits { MaximumItems = Math.Min(500, capability.MaximumItems), MaximumRowsRead = capability.MaximumRowsRead, MaximumEvidenceBytes = capability.MaximumEvidenceBytes, MaximumTransientBytes = capability.MaximumTransientBytes, AcquisitionDeadline = capability.AcquisitionDeadline, SessionDeadline = capability.SessionDeadline, PageDeadline = capability.PageDeadline };
        var requirement = new StudioInfrastructureInventoryRequirement { ApplicationId = applicationId, StoreId = authority.StoreInstanceId, Kind = kind, StoreInstanceId = authority.StoreInstanceId, RestoreEpoch = authority.RestoreEpoch, SchemaGeneration = authority.SchemaGeneration, Limits = limits };
        OperationResult<BaseCapturedStudioInfrastructureAuthority> capture = await store.CaptureInfrastructureAuthorityAsync(requirement, token).ConfigureAwait(false); if (!capture.IsSuccess() || capture.Value is null) return null;
        OperationResult<IStudioInfrastructureInventorySession> opened = await store.OpenInfrastructureSessionAsync(capture.Value, token).ConfigureAwait(false); if (!opened.IsSuccess() || opened.Value is null) return null;
        await using IStudioInfrastructureInventorySession session = opened.Value;
        OperationResult<StudioInfrastructurePage> page = await session.ReadPageAsync(new() { Take = limits.MaximumItems }, token).ConfigureAwait(false);
        return page.IsSuccess() && page.Value is not null && page.Value.Next is null ? page.Value.Items : null;
    }
    private static StudioInfrastructureInventoryKind? ViewKind(string id) => id switch
    {
        "base.infrastructure.stores.list" => null,
        "base.infrastructure.schemas.list" => StudioInfrastructureInventoryKind.SchemaGeneration,
        "base.infrastructure.backups.list" => StudioInfrastructureInventoryKind.Backup,
        "base.infrastructure.maintenance.list" or "base.infrastructure.attention.list" => StudioInfrastructureInventoryKind.Maintenance,
        _ when id.StartsWith("base.schema.detail.", StringComparison.Ordinal) => StudioInfrastructureInventoryKind.SchemaGeneration,
        _ when id.StartsWith("base.migration.detail.", StringComparison.Ordinal) => StudioInfrastructureInventoryKind.Migration,
        _ when id.StartsWith("base.backup.detail.", StringComparison.Ordinal) => StudioInfrastructureInventoryKind.Backup,
        _ when id.StartsWith("base.restore.detail.", StringComparison.Ordinal) => StudioInfrastructureInventoryKind.Restore,
        _ when id.StartsWith("base.maintenance.detail.", StringComparison.Ordinal) => StudioInfrastructureInventoryKind.Maintenance,
        _ => throw new InvalidOperationException("base.studio.infrastructureViewUnknown"),
    };
    private static StudioInfrastructureInventoryKind ResourceKind(StudioResourceKind kind) => kind switch { StudioResourceKind.Schema => StudioInfrastructureInventoryKind.SchemaGeneration, StudioResourceKind.Migration => StudioInfrastructureInventoryKind.Migration, StudioResourceKind.Backup => StudioInfrastructureInventoryKind.Backup, StudioResourceKind.Restore => StudioInfrastructureInventoryKind.Restore, StudioResourceKind.Maintenance => StudioInfrastructureInventoryKind.Maintenance, _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
    private static IEnumerable<StudioInfrastructureItem> Select(IEnumerable<StudioInfrastructureItem> items, StudioResourceIdentity resource, StudioResourceKind kind) => resource switch
    {
        StudioApplicationResource => items,
        StudioSchemaResource value => items.OfType<StudioSchemaGenerationItem>().Where(item => item.StoreId == value.StoreIdentity && item.SchemaGeneration == value.SchemaGeneration),
        StudioMigrationResource value => items.OfType<StudioMigrationItem>().Where(item => item.StoreId == value.StoreIdentity && item.MigrationId == value.MigrationId),
        StudioBackupResource value => items.OfType<StudioBackupItem>().Where(item => item.StoreId == value.StoreIdentity && item.ArtifactId == value.ArtifactId),
        StudioRestoreResource value => items.OfType<StudioRestoreItem>().Where(item => item.StoreId == value.StoreIdentity && item.RestoreRequestIdentity == value.RestoreRequestIdentity),
        StudioMaintenanceResource value => items.OfType<StudioMaintenanceItem>().Where(item => item.StoreId == value.StoreIdentity && item.MaintenanceKind == value.MaintenanceKind && item.OperationIdentity == value.OperationIdentity),
        _ => [],
    };

    private static IReadOnlyDictionary<string, string> StoreRow(StudioDynamicStoreAuthority value) => new Dictionary<string, string>(StringComparer.Ordinal) { ["storeIdentity"] = value.StoreInstanceId, ["storeInstanceId"] = value.StoreInstanceId, ["restoreEpoch"] = L(value.RestoreEpoch), ["schemaGeneration"] = L(value.SchemaGeneration), ["authorityChecksum"] = H(value.EvidenceChecksum) };
    private static IReadOnlyDictionary<string, string> Row(string viewId, StudioInfrastructureItem item)
    {
        var all = new Dictionary<string, string>(StringComparer.Ordinal) { ["storeId"] = item.StoreId, ["restoreEpoch"] = L(item.RestoreEpoch), ["sourceRestoreEpoch"] = L(item.RestoreEpoch), ["schemaGeneration"] = L(item.SchemaGeneration), ["sequence"] = L(item.Sequence), ["state"] = item.State.ToString(), ["observedAtUtc"] = StudioResponseAuthority.CanonicalUtc(item.ObservedAtUtc.ToUniversalTime()), ["itemChecksum"] = H(item.Checksum) };
        switch (item)
        {
            case StudioSchemaGenerationItem value: all["baselineId"] = value.BaselineId; all["schemaChecksum"] = H(value.SchemaChecksum); all["driftDetected"] = value.DriftDetected.ToString().ToLowerInvariant(); all["planAuthority"] = "migrationInventory"; break;
            case StudioMigrationItem value: all["migrationId"] = value.MigrationId; all["fromSchemaGeneration"] = L(value.FromSchemaGeneration); all["toSchemaGeneration"] = L(value.ToSchemaGeneration); all["planChecksum"] = H(value.PlanChecksum); all["compatibilityClass"] = value.ToSchemaGeneration >= value.FromSchemaGeneration ? "forwardGeneration" : "rollbackGeneration"; break;
            case StudioBackupItem value: bool available = value.State == StudioInfrastructureState.Completed && value.ArtifactBytes > 0 && value.ArtifactDigest.Any(static x => x != 0); all["artifactId"] = value.ArtifactId; all["artifactDigest"] = H(value.ArtifactDigest); all["artifactBytes"] = L(value.ArtifactBytes); all["artifactAvailable"] = available.ToString().ToLowerInvariant(); break;
            case StudioRestoreItem value: bool restoreAvailable = value.ArtifactDigest.Any(static x => x != 0); all["restoreRequestIdentity"] = value.RestoreRequestIdentity; all["artifactDigest"] = H(value.ArtifactDigest); all["artifactAvailable"] = restoreAvailable.ToString().ToLowerInvariant(); all["resultRestoreEpoch"] = L(value.ResultRestoreEpoch); all["authorityChanged"] = (value.ResultRestoreEpoch > 0 && value.ResultRestoreEpoch != value.RestoreEpoch).ToString().ToLowerInvariant(); all["reconciliationRequired"] = (value.State == StudioInfrastructureState.Indeterminate).ToString().ToLowerInvariant(); break;
            case StudioMaintenanceItem value: all["maintenanceKind"] = value.MaintenanceKind; all["operationIdentity"] = value.OperationIdentity; all["progressBasisPoints"] = L(value.ProgressBasisPoints); all["retentionAuthority"] = "inventoryRetention"; break;
        }
        return HPDBaseStudioInfrastructureContracts.Fields(viewId).ToDictionary(name => name, name => all[name], StringComparer.Ordinal);
    }
    private static string L(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string H(IEnumerable<byte> value) => Convert.ToHexString(value.ToArray()).ToLowerInvariant();

    private static bool TryInfrastructureResource(StudioCanonicalJson request, StudioResourceKind expected, out StudioResourceIdentity? resource)
    {
        resource = null;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(request.ToArray()); var value = document.RootElement.GetProperty("resource");
            string application = value.GetProperty("applicationId").GetString()!;
            resource = expected switch
            {
                StudioResourceKind.Application => new StudioApplicationResource(application),
                StudioResourceKind.Store => new StudioStoreResource(application, value.GetProperty("storeIdentity").GetString()!),
                StudioResourceKind.Provider => new StudioProviderResource(application, value.GetProperty("storeIdentity").GetString()!, value.GetProperty("providerId").GetString()!, int.Parse(value.GetProperty("providerVersion").GetString()!, CultureInfo.InvariantCulture)),
                StudioResourceKind.Schema => new StudioSchemaResource(application, value.GetProperty("storeIdentity").GetString()!, long.Parse(value.GetProperty("schemaGeneration").GetString()!, CultureInfo.InvariantCulture)),
                StudioResourceKind.Migration => new StudioMigrationResource(application, value.GetProperty("storeIdentity").GetString()!, value.GetProperty("migrationId").GetString()!),
                StudioResourceKind.Backup => new StudioBackupResource(application, value.GetProperty("storeIdentity").GetString()!, value.GetProperty("artifactId").GetString()!),
                StudioResourceKind.Restore => new StudioRestoreResource(application, value.GetProperty("storeIdentity").GetString()!, value.GetProperty("restoreRequestIdentity").GetString()!),
                StudioResourceKind.Maintenance => new StudioMaintenanceResource(application, value.GetProperty("storeIdentity").GetString()!, value.GetProperty("maintenanceKind").GetString()!, value.GetProperty("operationIdentity").GetString()!),
                _ => null,
            };
            return resource is not null && value.GetProperty("kind").GetString() == expected.ToString().ToLowerInvariant() && value.GetProperty("authorityChecksum").GetString() == H(resource.AuthorityChecksum.ToArray());
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or ArgumentException or FormatException or OverflowException) { resource = null; return false; }
    }
}
