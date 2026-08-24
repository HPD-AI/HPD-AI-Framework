using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using HPD.AI.Platform.Studio;

namespace HPD.Base.Studio;

internal sealed partial class BaseStudioRuntimeContributionFactory
{
    private void AddAutomationsRuntime(BaseStudioModuleRegistration module,
        List<BaseStudioNamedTypeContract> types, List<BaseStudioEndpointContract> endpoints,
        List<BaseStudioMethodBinding> methods, List<BaseStudioProducerBinding> producers,
        BaseStudioNamedTypeContract error, BaseStudioNamedTypeContract text, BaseStudioNamedTypeContract checksum,
        BaseStudioNamedTypeContract decimalLong, BaseStudioNamedTypeContract currentKind,
        BaseStudioNamedTypeContract graphAuthority, BaseStudioNamedTypeContract accounting,
        BaseStudioNamedTypeContract emptyItems, BaseStudioNamedTypeContract tokenRequest,
        BaseStudioNamedTypeContract resourceParameters, BaseStudioNamedTypeContract resourceRoute,
        BaseStudioNamedTypeContract resolvedKind)
    {
        BaseStudioNamedTypeContract positiveNumber = Type("base.studio.positive-number", "{\"kind\":\"integer\",\"wire\":\"number\",\"minimum\":\"1\",\"maximum\":\"2147483647\"}");
        var resourceTypes = new Dictionary<BaseStudioResourceKind, BaseStudioNamedTypeContract>
        {
            [BaseStudioResourceKind.Application] = types.Single(static x => x.TypeId == "base.studio.resource.application"),
            [BaseStudioResourceKind.Activation] = Resource("activation", P("activationId", text), P("applicationId", text), P("authorityChecksum", checksum), P("definitionId", text), K("activation"), P("version", positiveNumber)),
            [BaseStudioResourceKind.Schedule] = Resource("schedule", P("applicationId", text), P("authorityChecksum", checksum), K("schedule"), P("scheduleId", text), P("version", positiveNumber)),
            [BaseStudioResourceKind.Occurrence] = Resource("occurrence", P("applicationId", text), P("authorityChecksum", checksum), K("occurrence"), P("occurrenceId", text), P("scheduleId", text), P("version", positiveNumber)),
            [BaseStudioResourceKind.Effect] = Resource("effect", P("activationId", text), P("applicationId", text), P("attemptNumber", positiveNumber), P("authorityChecksum", checksum), P("effectId", text), K("effect")),
            [BaseStudioResourceKind.Executor] = Resource("executor", P("applicationId", text), P("authorityChecksum", checksum), P("executorGeneration", decimalLong), P("hostId", text), K("executor"), P("processIncarnationId", text)),
        };
        types.AddRange(resourceTypes.Values.Where(static x => x.TypeId != "base.studio.resource.application"));
        types.Add(positiveNumber);

        foreach (BaseStudioViewRegistration view in module.Views.Where(static x =>
                     x.ViewId.StartsWith("base.automation", StringComparison.Ordinal) ||
                     x.ViewId.StartsWith("base.activation.detail.", StringComparison.Ordinal) ||
                     x.ViewId.StartsWith("base.schedule.detail.", StringComparison.Ordinal) ||
                     x.ViewId.StartsWith("base.occurrence.detail.", StringComparison.Ordinal) ||
                     x.ViewId.StartsWith("base.effect.detail.", StringComparison.Ordinal) ||
                     x.ViewId.StartsWith("base.executor.detail.", StringComparison.Ordinal)))
        {
            BaseStudioPageRegistration page = module.Pages.Single(x => x.Presentation.Sections.Any(s => s.ViewIds.Contains(view.ViewId)));
            BaseStudioResourceKind requestKind = page.Presentation.NavigationRole == BaseStudioNavigationRole.AreaLanding
                ? BaseStudioResourceKind.Application : page.AcceptedResources[0];
            BaseStudioNamedTypeContract fact = Type(view.ItemNodeId, BaseStudioModuleRegistry.AutomationItemDescriptor(view.ItemKind));
            BaseStudioNamedTypeContract facts = Type(view.ViewId.ToLowerInvariant() + ".items", $"{{\"kind\":\"array\",\"elementTypeId\":\"{fact.TypeId}\",\"minItems\":0,\"maxItems\":500}}");
            BaseStudioNamedTypeContract request = Type(view.RequestNodeId, Obj(P("resource", resourceTypes[requestKind])));
            BaseStudioNamedTypeContract current = Type(view.ViewId.ToLowerInvariant() + ".current", Obj(P("accounting", accounting), P("evidence", emptyItems),
                P("kind", currentKind), P("links", emptyItems), P("observationAuthority", graphAuthority), P("resource", resourceTypes[requestKind]), P("value", facts)));
            if (!BaseStudioSha256.FixedTimeEquals(request.NodeChecksum, view.RequestNodeChecksum) ||
                !BaseStudioSha256.FixedTimeEquals(fact.NodeChecksum, view.ItemNodeChecksum))
                throw new InvalidOperationException("An Automations view differs from its graph-owned L41 nodes.");
            types.AddRange([fact, facts, request, current]);
            string endpointId = "base.studio.view.page." + view.ViewId;
            string methodId = "base.studio.view." + view.ViewId;
            endpoints.Add(Endpoint(endpointId, "/base/studio/views/" + view.ViewId, request, current));
            methods.Add(BaseStudioMethodBinding.Create(methodId, BaseStudioMethodKind.Page, "base", page.PageId,
                endpointId, request.TypeId, current.TypeId));
            producers.Add(new BaseStudioViewProducerBinding(methodId, new AutomationFactProducer(_principals, _authorization,
                page.Grants, _control, requestKind, view.ItemKind)));
        }

        foreach ((BaseStudioResourceKind kind, string pageId) in new[]
        {
            (BaseStudioResourceKind.Activation, "base.activation.detail"), (BaseStudioResourceKind.Schedule, "base.schedule.detail"),
            (BaseStudioResourceKind.Occurrence, "base.occurrence.detail"), (BaseStudioResourceKind.Effect, "base.effect.detail"),
            (BaseStudioResourceKind.Executor, "base.executor.detail"),
        })
        {
            BaseStudioNamedTypeContract resolved = Type("base.studio." + Kind(kind) + "-resolved", Obj(P("kind", resolvedKind),
                P("links", emptyItems), P("resource", resourceTypes[kind]), P("route", resourceRoute)));
            types.Add(resolved);
            string endpointId = "base.studio.resource.resolve." + Kind(kind);
            string methodId = "base.studio.resolve." + Kind(kind);
            endpoints.Add(Endpoint(endpointId, "/base/studio/resources/" + Kind(kind), tokenRequest, resolved));
            BaseStudioResourceRegistration registration = module.Resources.Single(x => x.Kind == kind);
            methods.Add(BaseStudioMethodBinding.Create(methodId, BaseStudioMethodKind.Resolve, "base", registration.ResolverId,
                endpointId, tokenRequest.TypeId, resolved.TypeId));
            producers.Add(new BaseStudioResourceProducerBinding(methodId, new AutomationResolver(_principals, _authorization,
                registration.Grants, _control, kind, pageId)));
        }

        BaseStudioNamedTypeContract Resource(string kind, params string[] properties)
        {
            BaseStudioNamedTypeContract literal = Type("base.studio.resource-kind." + kind, $"{{\"kind\":\"literal\",\"value\":\"{kind}\"}}");
            types.Add(literal); return Type("base.studio.resource." + kind, Obj(properties));
        }
        string K(string kind) => P("kind", Type("base.studio.resource-kind." + kind, $"{{\"kind\":\"literal\",\"value\":\"{kind}\"}}"));
        BaseStudioEndpointContract Endpoint(string id, string route, BaseStudioNamedTypeContract request, BaseStudioNamedTypeContract result)
            => BaseStudioEndpointContract.Create(id, 1, BaseStudioTransportMethod.Post, route, BaseStudioEndpointAudience.ControlPlane,
                BaseStudioTransportKind.SameOriginHttp, request.TypeId, request.NodeChecksum, result.TypeId, result.NodeChecksum,
                error.TypeId, error.NodeChecksum, 16_384, 1_048_576, TimeSpan.FromSeconds(10));
        static string P(string name, BaseStudioNamedTypeContract type) => $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type.TypeId}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}";
        static string Obj(params string[] properties) => $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', properties)}],\"additionalProperties\":false}}";
    }

    private sealed class AutomationFactProducer(IBaseStudioPrincipalContextResolver principals, BaseStudioAuthorization authorization,
        ImmutableArray<BaseStudioGrantRequirement> grants, IBaseStudioControlInspectionStore control,
        BaseStudioResourceKind requestKind, BaseStudioResourceKind itemKind)
        : ProducerBase(principals, authorization, grants), IBaseStudioViewProducer
    {
        public async ValueTask<BaseStudioCanonicalJson?> ReadAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) ||
                !RequestResource(invocation.Request, requestKind, out BaseStudioResourceIdentity? resource) || resource is null ||
                !StringComparer.Ordinal.Equals(resource.ApplicationId, invocation.Bootstrap.ApplicationGraph.ApplicationId)) return null;
            BaseStudioControlFactKind factKind = FactKind(itemKind);
            (string? subjectKind, string? subjectIdentity) = factKind == BaseStudioControlFactKind.ActivationReceipt ? ReceiptOwner(resource) : (null, null);
            OperationResult<BaseStudioControlInspectionPage> result = await control.ReadStudioControlFactsAsync(
                Request(invocation, factKind, subjectKind: subjectKind, subjectIdentity: subjectIdentity), cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess() || result.Value is null) return null;
            BaseStudioControlFact[] selected = result.Value.Items.Where(item => Belongs(resource, item)).Take(500).ToArray();
            Dictionary<string, int>? scheduleVersions = null;
            if (selected.Any(static x => x is BaseStudioOccurrenceFact))
            {
                OperationResult<BaseStudioControlInspectionPage> schedules = await control.ReadStudioControlFactsAsync(Request(invocation, BaseStudioControlFactKind.Schedule), cancellationToken).ConfigureAwait(false);
                if (!schedules.IsSuccess() || schedules.Value is null) return null;
                scheduleVersions = schedules.Value.Items.Cast<BaseStudioScheduleFact>().ToDictionary(static x => DecodeSchedule(x.Identity), static x => x.Version, StringComparer.Ordinal);
            }
            var buffer = new ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer); writer.WriteStartArray();
            foreach (BaseStudioControlFact item in selected)
            {
                BaseStudioResourceIdentity? itemResource = Resource(invocation.Bootstrap.ApplicationGraph.ApplicationId, item, itemKind, scheduleVersions);
                if (itemResource is null) continue;
                WriteAutomationItem(writer, item, itemResource, itemKind);
            }
            writer.WriteEndArray(); writer.Flush(); BaseStudioCanonicalJson value = BaseStudioCanonicalJson.Create(buffer.WrittenSpan, 1_048_576);
            return BaseStudioObservationJson.Current(resource, BaseStudioGraphObservationAuthority.Create(invocation.Authority), value,
                [], [], Accounting(value.ToArray().Length), 1_048_576);
        }
    }

    private static void WriteAutomationItem(Utf8JsonWriter writer, BaseStudioControlFact item,
        BaseStudioResourceIdentity resource, BaseStudioResourceKind requested)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["factChecksum"] = Hex(item.FactChecksum), ["identity"] = item.Identity,
            ["resourceToken"] = BaseStudioResourceRouteToken.Encode(resource),
        };
        switch (item, requested)
        {
            case (BaseStudioActivationFact value, BaseStudioResourceKind.Activation or BaseStudioResourceKind.ActivationAttempt):
                values["attemptNumber"] = value.AttemptNumber.ToString(CultureInfo.InvariantCulture); values["definitionId"] = value.DefinitionId;
                values["definitionVersion"] = value.DefinitionVersion.ToString(CultureInfo.InvariantCulture); values["state"] = value.State.ToString(); break;
            case (BaseStudioScheduleFact value, _):
                values["enabled"] = value.Enabled ? "true" : "false"; values["scheduleId"] = DecodeSchedule(value.Identity);
                values["version"] = value.Version.ToString(CultureInfo.InvariantCulture); break;
            case (BaseStudioOccurrenceFact value, _):
                values["activationId"] = value.ActivationId ?? string.Empty; values["disposition"] = value.Disposition;
                values["occurrenceId"] = value.Identity; values["scheduleId"] = value.ScheduleId; break;
            case (BaseStudioEffectFact value, _):
                values["activationId"] = value.ActivationId; values["attemptNumber"] = value.AttemptNumber.ToString(CultureInfo.InvariantCulture);
                values["effectId"] = Hex(value.FactChecksum); break;
            case (BaseStudioExecutorFact value, _):
                values["executorGeneration"] = value.ExecutorGeneration.ToString(CultureInfo.InvariantCulture); values["hostId"] = value.HostId;
                values["processIncarnationId"] = value.ProcessIncarnationId; values["state"] = value.Retired ? "retired" : "active"; break;
            case (BaseStudioActivationReceiptFact value, _):
                values["activationId"] = value.ActivationId ?? string.Empty; values["transitionKind"] = value.TransitionKind; break;
            case (BaseStudioQuarantineFact value, _):
                values["quarantineKind"] = value.Quarantine.Operation; values["subjectIdentity"] = value.Identity; break;
            default:
                values["authorityState"] = SafeState(item); break;
        }
        writer.WriteStartObject(); foreach ((string name, string value) in values) writer.WriteString(name, value); writer.WriteEndObject();
    }

    private sealed class AutomationResolver(IBaseStudioPrincipalContextResolver principals, BaseStudioAuthorization authorization,
        ImmutableArray<BaseStudioGrantRequirement> grants, IBaseStudioControlInspectionStore control,
        BaseStudioResourceKind kind, string pageId) : ProducerBase(principals, authorization, grants), IBaseStudioResourceProducer
    {
        public async ValueTask<BaseStudioCanonicalJson?> ResolveAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) || !Token(invocation.Request, out BaseStudioResourceIdentity? decoded) ||
                decoded is null || decoded.Kind != kind || !StringComparer.Ordinal.Equals(decoded.ApplicationId, invocation.Bootstrap.ApplicationGraph.ApplicationId)) return null;
            string? identity = InspectionIdentity(decoded); if (identity is null) return null;
            OperationResult<BaseStudioControlInspectionPage> result = await control.ReadStudioControlFactsAsync(Request(invocation, FactKind(kind), identity), cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess() || result.Value is null || result.Value.Items.Length != 1 || !Matches(decoded, result.Value.Items[0])) return null;
            string token = BaseStudioResourceRouteToken.Encode(decoded);
            return BaseStudioResolvedResourceJson.Encode(decoded, BaseStudioResolvedRoute.Create(pageId,
                [new KeyValuePair<string, string>("resource", token)]), [], 1_048_576);
        }
    }

    private static BaseStudioControlInspectionRequest Request(BaseStudioProducerInvocation invocation, BaseStudioControlFactKind kind,
        string? identity = null, string? subjectKind = null, string? subjectIdentity = null) => new()
    {
        ApplicationId = invocation.Bootstrap.ApplicationGraph.ApplicationId, Kind = kind, Identity = identity, Take = identity is null ? 500 : 1,
        SubjectKind = subjectKind, SubjectIdentity = subjectIdentity,
        ProtectedScopeChecksum = [.. invocation.Authority.ProtectedScopeChecksum.ToArray()], Limits = new()
        { MaximumItems = identity is null ? 500 : 1, MaximumRowsRead = 10_000, MaximumEvidenceBytes = 8_388_608,
          MaximumTransientBytes = 8_388_608, Deadline = TimeSpan.FromSeconds(5) }
    };

    private static (string? Kind, string? Identity) ReceiptOwner(BaseStudioResourceIdentity resource) => resource switch
    {
        BaseStudioActivationResource x => ("activation", x.ActivationId),
        BaseStudioScheduleResource x => ("schedule", BaseStudioControlInspectionContract.ScheduleIdentity(x.ScheduleId, x.Version)),
        BaseStudioEffectResource x => ("activation", x.ActivationId),
        BaseStudioExecutorResource x => ("executor", BaseStudioControlInspectionContract.ExecutorIdentity(x.ApplicationId, x.HostId, x.ProcessIncarnationId)),
        _ => (null, null),
    };

    private static BaseStudioControlFactKind FactKind(BaseStudioResourceKind kind) => kind switch
    {
        BaseStudioResourceKind.Activation or BaseStudioResourceKind.ActivationAttempt => BaseStudioControlFactKind.Activation,
        BaseStudioResourceKind.Schedule => BaseStudioControlFactKind.Schedule, BaseStudioResourceKind.Occurrence => BaseStudioControlFactKind.Occurrence,
        BaseStudioResourceKind.Effect => BaseStudioControlFactKind.Effect, BaseStudioResourceKind.Executor => BaseStudioControlFactKind.Executor,
        BaseStudioResourceKind.Receipt => BaseStudioControlFactKind.ActivationReceipt, BaseStudioResourceKind.QuarantineItem => BaseStudioControlFactKind.Quarantine,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static bool Belongs(BaseStudioResourceIdentity resource, BaseStudioControlFact fact) => resource switch
    {
        BaseStudioApplicationResource => true,
        BaseStudioActivationResource activation => fact switch { BaseStudioActivationFact x => x.Identity == activation.ActivationId,
            BaseStudioOccurrenceFact x => x.ActivationId == activation.ActivationId, BaseStudioEffectFact x => x.ActivationId == activation.ActivationId,
            BaseStudioActivationReceiptFact x => x.ActivationId == activation.ActivationId, _ => false },
        BaseStudioScheduleResource schedule => fact switch { BaseStudioScheduleFact x => DecodeSchedule(x.Identity) == schedule.ScheduleId,
            BaseStudioOccurrenceFact x => x.ScheduleId == schedule.ScheduleId, _ => false },
        BaseStudioOccurrenceResource occurrence => fact.Identity == occurrence.OccurrenceId,
        BaseStudioEffectResource effect => fact is BaseStudioEffectFact x && x.ActivationId == effect.ActivationId,
        BaseStudioExecutorResource executor => fact is BaseStudioExecutorFact x && x.HostId == executor.HostId && x.ProcessIncarnationId == executor.ProcessIncarnationId,
        _ => false,
    };

    private static BaseStudioResourceIdentity? Resource(string application, BaseStudioControlFact fact, BaseStudioResourceKind requested,
        IReadOnlyDictionary<string, int>? schedules) => (fact, requested) switch
    {
        (BaseStudioActivationFact x, BaseStudioResourceKind.Activation) => new BaseStudioActivationResource(application, x.DefinitionId, x.DefinitionVersion, x.Identity),
        (BaseStudioActivationFact x, BaseStudioResourceKind.ActivationAttempt) when x.AttemptNumber > 0 => new BaseStudioActivationAttemptResource(application, x.Identity, x.AttemptNumber),
        (BaseStudioScheduleFact x, _) => new BaseStudioScheduleResource(application, DecodeSchedule(x.Identity), x.Version),
        (BaseStudioOccurrenceFact x, _) when schedules is not null && schedules.TryGetValue(x.ScheduleId, out int version) => new BaseStudioOccurrenceResource(application, x.ScheduleId, version, x.Identity),
        (BaseStudioEffectFact x, _) => new BaseStudioEffectResource(application, x.ActivationId, x.AttemptNumber, Hex(x.FactChecksum)),
        (BaseStudioExecutorFact x, _) => new BaseStudioExecutorResource(application, x.HostId, x.ProcessIncarnationId, x.ExecutorGeneration),
        (BaseStudioActivationReceiptFact x, _) => new BaseStudioReceiptResource(application, "activation", x.TransitionKind, x.Identity),
        (BaseStudioQuarantineFact x, _) => new BaseStudioQuarantineItemResource(application, "activation", "base.activations", x.Identity),
        _ => null,
    };

    private static string SafeState(BaseStudioControlFact fact) => fact switch
    {
        BaseStudioActivationFact x => x.State.ToString(), BaseStudioScheduleFact x => x.Enabled ? "enabled" : "disabled",
        BaseStudioOccurrenceFact x => x.Disposition, BaseStudioExecutorFact x => x.Retired ? "retired" : "active",
        BaseStudioEffectFact => "started", BaseStudioActivationReceiptFact x => x.TransitionKind,
        BaseStudioQuarantineFact x => x.Quarantine.Operation, _ => "observed",
    };

    private static bool Matches(BaseStudioResourceIdentity resource, BaseStudioControlFact fact) => Belongs(resource, fact) && resource switch
    {
        BaseStudioEffectResource effect when fact is BaseStudioEffectFact x => effect.EffectId == Hex(x.FactChecksum) && effect.AttemptNumber == x.AttemptNumber,
        BaseStudioExecutorResource executor when fact is BaseStudioExecutorFact x => executor.ExecutorGeneration == x.ExecutorGeneration,
        _ => true,
    };

    private static string? InspectionIdentity(BaseStudioResourceIdentity resource) => resource switch
    {
        BaseStudioActivationResource x => x.ActivationId, BaseStudioScheduleResource x => BaseStudioControlInspectionContract.ScheduleIdentity(x.ScheduleId, x.Version),
        BaseStudioOccurrenceResource x => x.OccurrenceId, BaseStudioEffectResource x => x.ActivationId,
        BaseStudioExecutorResource x => BaseStudioControlInspectionContract.ExecutorIdentity(x.ApplicationId, x.HostId, x.ProcessIncarnationId), _ => null,
    };

    private static string DecodeSchedule(string identity) => BaseStudioControlInspectionContract.TryDecodeScheduleIdentity(identity, out string id, out _) ? id : throw new InvalidDataException();
    private static string Kind(BaseStudioResourceKind kind) => kind.ToString().ToLowerInvariant();
    private static string Hex(ImmutableArray<byte> value) => Convert.ToHexString(value.AsSpan()).ToLowerInvariant();
}
