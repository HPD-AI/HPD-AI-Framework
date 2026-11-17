using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using HPD.AI.Platform.Studio;

namespace HPD.Base.Studio;

internal sealed partial class HPDBaseStudioRuntimeContributionFactory
{
    private void AddAutomationsRuntime(StudioModuleRegistration module,
        List<StudioNamedTypeContract> types, List<StudioEndpointContract> endpoints,
        List<StudioMethodBinding> methods, List<StudioProducerBinding> producers,
        StudioNamedTypeContract error, StudioNamedTypeContract text, StudioNamedTypeContract checksum,
        StudioNamedTypeContract decimalLong, StudioNamedTypeContract currentKind,
        StudioNamedTypeContract graphAuthority, StudioNamedTypeContract accounting,
        StudioNamedTypeContract emptyItems, StudioNamedTypeContract tokenRequest,
        StudioNamedTypeContract resourceParameters, StudioNamedTypeContract resourceRoute,
        StudioNamedTypeContract resolvedKind)
    {
        StudioNamedTypeContract positiveNumber = Type("base.studio.positive-number", "{\"kind\":\"integer\",\"wire\":\"number\",\"minimum\":\"1\",\"maximum\":\"2147483647\"}");
        var resourceTypes = new Dictionary<StudioResourceKind, StudioNamedTypeContract>
        {
            [StudioResourceKind.Application] = types.Single(static x => x.TypeId == "base.studio.resource.application"),
            [StudioResourceKind.Activation] = Resource("activation", P("activationId", text), P("applicationId", text), P("authorityChecksum", checksum), P("definitionId", text), K("activation"), P("version", positiveNumber)),
            [StudioResourceKind.Schedule] = Resource("schedule", P("applicationId", text), P("authorityChecksum", checksum), K("schedule"), P("scheduleId", text), P("version", positiveNumber)),
            [StudioResourceKind.Occurrence] = Resource("occurrence", P("applicationId", text), P("authorityChecksum", checksum), K("occurrence"), P("occurrenceId", text), P("scheduleId", text), P("version", positiveNumber)),
            [StudioResourceKind.Effect] = Resource("effect", P("activationId", text), P("applicationId", text), P("attemptNumber", positiveNumber), P("authorityChecksum", checksum), P("effectId", text), K("effect")),
            [StudioResourceKind.Executor] = Resource("executor", P("applicationId", text), P("authorityChecksum", checksum), P("executorGeneration", decimalLong), P("hostId", text), K("executor"), P("processIncarnationId", text)),
        };
        types.AddRange(resourceTypes.Values.Where(static x => x.TypeId != "base.studio.resource.application"));
        types.Add(positiveNumber);

        foreach (StudioViewRegistration view in module.Views.Where(static x =>
                     x.ViewId.StartsWith("base.automation", StringComparison.Ordinal) ||
                     x.ViewId.StartsWith("base.activation.detail.", StringComparison.Ordinal) ||
                     x.ViewId.StartsWith("base.schedule.detail.", StringComparison.Ordinal) ||
                     x.ViewId.StartsWith("base.occurrence.detail.", StringComparison.Ordinal) ||
                     x.ViewId.StartsWith("base.effect.detail.", StringComparison.Ordinal) ||
                     x.ViewId.StartsWith("base.executor.detail.", StringComparison.Ordinal)))
        {
            StudioPageRegistration page = module.Pages.Single(x => x.Presentation.Sections.Any(s => s.ViewIds.Contains(view.ViewId)));
            StudioResourceKind requestKind = page.Presentation.NavigationRole == StudioNavigationRole.AreaLanding
                ? StudioResourceKind.Application : page.AcceptedResources[0];
            StudioNamedTypeContract fact = Type(view.ItemNodeId, HPDBaseStudioModuleRegistry.AutomationItemDescriptor(view.ItemKind));
            StudioNamedTypeContract facts = Type(view.ViewId.ToLowerInvariant() + ".items", $"{{\"kind\":\"array\",\"elementTypeId\":\"{fact.TypeId}\",\"minItems\":0,\"maxItems\":500}}");
            StudioNamedTypeContract request = Type(view.RequestNodeId, Obj(P("resource", resourceTypes[requestKind])));
            StudioNamedTypeContract current = Type(view.ViewId.ToLowerInvariant() + ".current", Obj(P("accounting", accounting), P("evidence", emptyItems),
                P("kind", currentKind), P("links", emptyItems), P("observationAuthority", graphAuthority), P("resource", resourceTypes[requestKind]), P("value", facts)));
            if (!StudioSha256.FixedTimeEquals(request.NodeChecksum, view.RequestNodeChecksum) ||
                !StudioSha256.FixedTimeEquals(fact.NodeChecksum, view.ItemNodeChecksum))
                throw new InvalidOperationException("An Automations view differs from its graph-owned L41 nodes.");
            types.AddRange([fact, facts, request, current]);
            string endpointId = "base.studio.view.page." + view.ViewId;
            string methodId = "base.studio.view." + view.ViewId;
            endpoints.Add(Endpoint(endpointId, "/base/studio/views/" + view.ViewId, request, current));
            methods.Add(StudioMethodBinding.Create(methodId, StudioMethodKind.Page, "base", page.PageId,
                endpointId, request.TypeId, current.TypeId));
            producers.Add(new StudioViewProducerBinding(methodId, new AutomationFactProducer(_principals, _authorization,
                page.Grants, _control, requestKind, view.ItemKind)));
        }

        foreach ((StudioResourceKind kind, string pageId) in new[]
        {
            (StudioResourceKind.Activation, "base.activation.detail"), (StudioResourceKind.Schedule, "base.schedule.detail"),
            (StudioResourceKind.Occurrence, "base.occurrence.detail"), (StudioResourceKind.Effect, "base.effect.detail"),
            (StudioResourceKind.Executor, "base.executor.detail"),
        })
        {
            StudioNamedTypeContract resolved = Type("base.studio." + Kind(kind) + "-resolved", Obj(P("kind", resolvedKind),
                P("links", emptyItems), P("resource", resourceTypes[kind]), P("route", resourceRoute)));
            types.Add(resolved);
            string endpointId = "base.studio.resource.resolve." + Kind(kind);
            string methodId = "base.studio.resolve." + Kind(kind);
            endpoints.Add(Endpoint(endpointId, "/base/studio/resources/" + Kind(kind), tokenRequest, resolved));
            StudioResourceRegistration registration = module.Resources.Single(x => x.Kind == kind);
            methods.Add(StudioMethodBinding.Create(methodId, StudioMethodKind.Resolve, "base", registration.ResolverId,
                endpointId, tokenRequest.TypeId, resolved.TypeId));
            producers.Add(new StudioResourceProducerBinding(methodId, new AutomationResolver(_principals, _authorization,
                registration.Grants, _control, kind, pageId)));
        }

        StudioNamedTypeContract Resource(string kind, params string[] properties)
        {
            StudioNamedTypeContract literal = Type("base.studio.resource-kind." + kind, $"{{\"kind\":\"literal\",\"value\":\"{kind}\"}}");
            types.Add(literal); return Type("base.studio.resource." + kind, Obj(properties));
        }
        string K(string kind) => P("kind", Type("base.studio.resource-kind." + kind, $"{{\"kind\":\"literal\",\"value\":\"{kind}\"}}"));
        StudioEndpointContract Endpoint(string id, string route, StudioNamedTypeContract request, StudioNamedTypeContract result)
            => StudioEndpointContract.Create(id, 1, StudioTransportMethod.Post, route, StudioEndpointAudience.ControlPlane,
                StudioTransportKind.SameOriginHttp, request.TypeId, request.NodeChecksum, result.TypeId, result.NodeChecksum,
                error.TypeId, error.NodeChecksum, 16_384, 1_048_576, TimeSpan.FromSeconds(10));
        static string P(string name, StudioNamedTypeContract type) => $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type.TypeId}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}";
        static string Obj(params string[] properties) => $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', properties)}],\"additionalProperties\":false}}";
    }

    private sealed class AutomationFactProducer(IStudioPrincipalContextResolver principals, HPDBaseStudioAuthorization authorization,
        ImmutableArray<StudioGrantRequirement> grants, IStudioControlInspectionStore control,
        StudioResourceKind requestKind, StudioResourceKind itemKind)
        : ProducerBase(principals, authorization, grants), IStudioViewProducer
    {
        public async ValueTask<StudioCanonicalJson?> ReadAsync(StudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) ||
                !RequestResource(invocation.Request, requestKind, out StudioResourceIdentity? resource) || resource is null ||
                !StringComparer.Ordinal.Equals(resource.ApplicationId, invocation.Bootstrap.ApplicationGraph.ApplicationId)) return null;
            StudioControlFactKind factKind = FactKind(itemKind);
            (string? subjectKind, string? subjectIdentity) = factKind == StudioControlFactKind.ActivationReceipt ? ReceiptOwner(resource) : (null, null);
            OperationResult<StudioControlInspectionPage> result = await control.ReadStudioControlFactsAsync(
                Request(invocation, factKind, subjectKind: subjectKind, subjectIdentity: subjectIdentity), cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess() || result.Value is null) return null;
            StudioControlFact[] selected = result.Value.Items.Where(item => Belongs(resource, item)).Take(500).ToArray();
            Dictionary<string, int>? scheduleVersions = null;
            if (selected.Any(static x => x is StudioOccurrenceFact))
            {
                OperationResult<StudioControlInspectionPage> schedules = await control.ReadStudioControlFactsAsync(Request(invocation, StudioControlFactKind.Schedule), cancellationToken).ConfigureAwait(false);
                if (!schedules.IsSuccess() || schedules.Value is null) return null;
                scheduleVersions = schedules.Value.Items.Cast<StudioScheduleFact>().ToDictionary(static x => DecodeSchedule(x.Identity), static x => x.Version, StringComparer.Ordinal);
            }
            var buffer = new ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer); writer.WriteStartArray();
            foreach (StudioControlFact item in selected)
            {
                StudioResourceIdentity? itemResource = Resource(invocation.Bootstrap.ApplicationGraph.ApplicationId, item, itemKind, scheduleVersions);
                if (itemResource is null) continue;
                WriteAutomationItem(writer, item, itemResource, itemKind);
            }
            writer.WriteEndArray(); writer.Flush(); StudioCanonicalJson value = StudioCanonicalJson.Create(buffer.WrittenSpan, 1_048_576);
            return StudioObservationJson.Current(resource, StudioGraphObservationAuthority.Create(invocation.Authority), value,
                [], [], Accounting(value.ToArray().Length), 1_048_576);
        }
    }

    private static void WriteAutomationItem(Utf8JsonWriter writer, StudioControlFact item,
        StudioResourceIdentity resource, StudioResourceKind requested)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["factChecksum"] = Hex(item.FactChecksum), ["identity"] = item.Identity,
            ["resourceToken"] = StudioResourceRouteToken.Encode(resource),
        };
        switch (item, requested)
        {
            case (StudioActivationFact value, StudioResourceKind.Activation or StudioResourceKind.ActivationAttempt):
                values["attemptNumber"] = value.AttemptNumber.ToString(CultureInfo.InvariantCulture); values["definitionId"] = value.DefinitionId;
                values["definitionVersion"] = value.DefinitionVersion.ToString(CultureInfo.InvariantCulture); values["state"] = value.State.ToString(); break;
            case (StudioScheduleFact value, _):
                values["enabled"] = value.Enabled ? "true" : "false"; values["scheduleId"] = DecodeSchedule(value.Identity);
                values["version"] = value.Version.ToString(CultureInfo.InvariantCulture); break;
            case (StudioOccurrenceFact value, _):
                values["activationId"] = value.ActivationId ?? string.Empty; values["disposition"] = value.Disposition;
                values["occurrenceId"] = value.Identity; values["scheduleId"] = value.ScheduleId; break;
            case (StudioEffectFact value, _):
                values["activationId"] = value.ActivationId; values["attemptNumber"] = value.AttemptNumber.ToString(CultureInfo.InvariantCulture);
                values["effectId"] = Hex(value.FactChecksum); break;
            case (StudioExecutorFact value, _):
                values["executorGeneration"] = value.ExecutorGeneration.ToString(CultureInfo.InvariantCulture); values["hostId"] = value.HostId;
                values["processIncarnationId"] = value.ProcessIncarnationId; values["state"] = value.Retired ? "retired" : "active"; break;
            case (StudioActivationReceiptFact value, _):
                values["activationId"] = value.ActivationId ?? string.Empty; values["transitionKind"] = value.TransitionKind; break;
            case (StudioQuarantineFact value, _):
                values["quarantineKind"] = value.Quarantine.Operation; values["subjectIdentity"] = value.Identity; break;
            default:
                values["authorityState"] = SafeState(item); break;
        }
        writer.WriteStartObject(); foreach ((string name, string value) in values) writer.WriteString(name, value); writer.WriteEndObject();
    }

    private sealed class AutomationResolver(IStudioPrincipalContextResolver principals, HPDBaseStudioAuthorization authorization,
        ImmutableArray<StudioGrantRequirement> grants, IStudioControlInspectionStore control,
        StudioResourceKind kind, string pageId) : ProducerBase(principals, authorization, grants), IStudioResourceProducer
    {
        public async ValueTask<StudioCanonicalJson?> ResolveAsync(StudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) || !Token(invocation.Request, out StudioResourceIdentity? decoded) ||
                decoded is null || decoded.Kind != kind || !StringComparer.Ordinal.Equals(decoded.ApplicationId, invocation.Bootstrap.ApplicationGraph.ApplicationId)) return null;
            string? identity = InspectionIdentity(decoded); if (identity is null) return null;
            OperationResult<StudioControlInspectionPage> result = await control.ReadStudioControlFactsAsync(Request(invocation, FactKind(kind), identity), cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess() || result.Value is null || result.Value.Items.Length != 1 || !Matches(decoded, result.Value.Items[0])) return null;
            string token = StudioResourceRouteToken.Encode(decoded);
            return StudioResolvedResourceJson.Encode(decoded, StudioResolvedRoute.Create(pageId,
                [new KeyValuePair<string, string>("resource", token)]), [], 1_048_576);
        }
    }

    private static StudioControlInspectionRequest Request(StudioProducerInvocation invocation, StudioControlFactKind kind,
        string? identity = null, string? subjectKind = null, string? subjectIdentity = null) => new()
    {
        ApplicationId = invocation.Bootstrap.ApplicationGraph.ApplicationId, Kind = kind, Identity = identity, Take = identity is null ? 500 : 1,
        SubjectKind = subjectKind, SubjectIdentity = subjectIdentity,
        ProtectedScopeChecksum = [.. invocation.Authority.ProtectedScopeChecksum.ToArray()], Limits = new()
        { MaximumItems = identity is null ? 500 : 1, MaximumRowsRead = 10_000, MaximumEvidenceBytes = 8_388_608,
          MaximumTransientBytes = 8_388_608, Deadline = TimeSpan.FromSeconds(5) }
    };

    private static (string? Kind, string? Identity) ReceiptOwner(StudioResourceIdentity resource) => resource switch
    {
        StudioActivationResource x => ("activation", x.ActivationId),
        StudioScheduleResource x => ("schedule", StudioControlInspectionContract.ScheduleIdentity(x.ScheduleId, x.Version)),
        StudioEffectResource x => ("activation", x.ActivationId),
        StudioExecutorResource x => ("executor", StudioControlInspectionContract.ExecutorIdentity(x.ApplicationId, x.HostId, x.ProcessIncarnationId)),
        _ => (null, null),
    };

    private static StudioControlFactKind FactKind(StudioResourceKind kind) => kind switch
    {
        StudioResourceKind.Activation or StudioResourceKind.ActivationAttempt => StudioControlFactKind.Activation,
        StudioResourceKind.Schedule => StudioControlFactKind.Schedule, StudioResourceKind.Occurrence => StudioControlFactKind.Occurrence,
        StudioResourceKind.Effect => StudioControlFactKind.Effect, StudioResourceKind.Executor => StudioControlFactKind.Executor,
        StudioResourceKind.Receipt => StudioControlFactKind.ActivationReceipt, StudioResourceKind.QuarantineItem => StudioControlFactKind.Quarantine,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static bool Belongs(StudioResourceIdentity resource, StudioControlFact fact) => resource switch
    {
        StudioApplicationResource => true,
        StudioActivationResource activation => fact switch { StudioActivationFact x => x.Identity == activation.ActivationId,
            StudioOccurrenceFact x => x.ActivationId == activation.ActivationId, StudioEffectFact x => x.ActivationId == activation.ActivationId,
            StudioActivationReceiptFact x => x.ActivationId == activation.ActivationId, _ => false },
        StudioScheduleResource schedule => fact switch { StudioScheduleFact x => DecodeSchedule(x.Identity) == schedule.ScheduleId,
            StudioOccurrenceFact x => x.ScheduleId == schedule.ScheduleId, _ => false },
        StudioOccurrenceResource occurrence => fact.Identity == occurrence.OccurrenceId,
        StudioEffectResource effect => fact is StudioEffectFact x && x.ActivationId == effect.ActivationId,
        StudioExecutorResource executor => fact is StudioExecutorFact x && x.HostId == executor.HostId && x.ProcessIncarnationId == executor.ProcessIncarnationId,
        _ => false,
    };

    private static StudioResourceIdentity? Resource(string application, StudioControlFact fact, StudioResourceKind requested,
        IReadOnlyDictionary<string, int>? schedules) => (fact, requested) switch
    {
        (StudioActivationFact x, StudioResourceKind.Activation) => new StudioActivationResource(application, x.DefinitionId, x.DefinitionVersion, x.Identity),
        (StudioActivationFact x, StudioResourceKind.ActivationAttempt) when x.AttemptNumber > 0 => new StudioActivationAttemptResource(application, x.Identity, x.AttemptNumber),
        (StudioScheduleFact x, _) => new StudioScheduleResource(application, DecodeSchedule(x.Identity), x.Version),
        (StudioOccurrenceFact x, _) when schedules is not null && schedules.TryGetValue(x.ScheduleId, out int version) => new StudioOccurrenceResource(application, x.ScheduleId, version, x.Identity),
        (StudioEffectFact x, _) => new StudioEffectResource(application, x.ActivationId, x.AttemptNumber, Hex(x.FactChecksum)),
        (StudioExecutorFact x, _) => new StudioExecutorResource(application, x.HostId, x.ProcessIncarnationId, x.ExecutorGeneration),
        (StudioActivationReceiptFact x, _) => new StudioReceiptResource(application, "activation", x.TransitionKind, x.Identity),
        (StudioQuarantineFact x, _) => new StudioQuarantineItemResource(application, "activation", "base.activations", x.Identity),
        _ => null,
    };

    private static string SafeState(StudioControlFact fact) => fact switch
    {
        StudioActivationFact x => x.State.ToString(), StudioScheduleFact x => x.Enabled ? "enabled" : "disabled",
        StudioOccurrenceFact x => x.Disposition, StudioExecutorFact x => x.Retired ? "retired" : "active",
        StudioEffectFact => "started", StudioActivationReceiptFact x => x.TransitionKind,
        StudioQuarantineFact x => x.Quarantine.Operation, _ => "observed",
    };

    private static bool Matches(StudioResourceIdentity resource, StudioControlFact fact) => Belongs(resource, fact) && resource switch
    {
        StudioEffectResource effect when fact is StudioEffectFact x => effect.EffectId == Hex(x.FactChecksum) && effect.AttemptNumber == x.AttemptNumber,
        StudioExecutorResource executor when fact is StudioExecutorFact x => executor.ExecutorGeneration == x.ExecutorGeneration,
        _ => true,
    };

    private static string? InspectionIdentity(StudioResourceIdentity resource) => resource switch
    {
        StudioActivationResource x => x.ActivationId, StudioScheduleResource x => StudioControlInspectionContract.ScheduleIdentity(x.ScheduleId, x.Version),
        StudioOccurrenceResource x => x.OccurrenceId, StudioEffectResource x => x.ActivationId,
        StudioExecutorResource x => StudioControlInspectionContract.ExecutorIdentity(x.ApplicationId, x.HostId, x.ProcessIncarnationId), _ => null,
    };

    private static string DecodeSchedule(string identity) => StudioControlInspectionContract.TryDecodeScheduleIdentity(identity, out string id, out _) ? id : throw new InvalidDataException();
    private static string Kind(StudioResourceKind kind) => kind.ToString().ToLowerInvariant();
    private static string Hex(ImmutableArray<byte> value) => Convert.ToHexString(value.AsSpan()).ToLowerInvariant();
}
