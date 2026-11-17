using System.Buffers;
using System.Collections.Immutable;
using System.Text.Json;
using HPD.AI.Platform.Studio;

namespace HPD.Base.Studio;

internal sealed partial class HPDBaseStudioRuntimeContributionFactory
{
    private void AddSubjectsRuntime(StudioModuleRegistration module,
        List<StudioNamedTypeContract> types, List<StudioEndpointContract> endpoints,
        List<StudioMethodBinding> methods, List<StudioProducerBinding> producers,
        StudioNamedTypeContract error, StudioNamedTypeContract text, StudioNamedTypeContract checksum,
        StudioNamedTypeContract currentKind, StudioNamedTypeContract graphAuthority,
        StudioNamedTypeContract accounting, StudioNamedTypeContract emptyItems,
        StudioNamedTypeContract tokenRequest, StudioNamedTypeContract resourceRoute,
        StudioNamedTypeContract resolvedKind)
    {
        StudioNamedTypeContract number = types.Single(static x => x.TypeId == "base.studio.positive-number");
        StudioNamedTypeContract contractKind = Type("base.studio.resource-kind.subjectcontract", "{\"kind\":\"literal\",\"value\":\"subjectContract\"}");
        StudioNamedTypeContract subjectKind = Type("base.studio.resource-kind.subject", "{\"kind\":\"literal\",\"value\":\"subject\"}");
        StudioNamedTypeContract consumerKind = Type("base.studio.resource-kind.lifecycleconsumer", "{\"kind\":\"literal\",\"value\":\"lifecycleConsumer\"}");
        StudioNamedTypeContract checkpointKind = Type("base.studio.resource-kind.lifecyclecheckpoint", "{\"kind\":\"literal\",\"value\":\"lifecycleCheckpoint\"}");
        StudioNamedTypeContract barrierKind = Type("base.studio.resource-kind.retirementbarrier", "{\"kind\":\"literal\",\"value\":\"retirementBarrier\"}");
        StudioNamedTypeContract contractResource = Type("base.studio.resource.subjectcontract", Obj(P("applicationId", text), P("authorityChecksum", checksum),
            P("contractId", text), P("contractVersion", number), P("kind", contractKind)));
        StudioNamedTypeContract subjectResource = Type("base.studio.resource.subject", Obj(P("applicationId", text), P("authorityChecksum", checksum),
            P("contractId", text), P("contractVersion", number), P("kind", subjectKind), P("protectedSubjectIdentity", text)));
        StudioNamedTypeContract consumerResource = Type("base.studio.resource.lifecycleconsumer", Obj(P("applicationId", text), P("authorityChecksum", checksum), P("consumerId", text), P("contractId", text), P("contractVersion", number), P("kind", consumerKind), P("version", number)));
        StudioNamedTypeContract checkpointResource = Type("base.studio.resource.lifecyclecheckpoint", Obj(P("applicationId", text), P("authorityChecksum", checksum), P("consumerId", text), P("consumerVersion", number), P("contractId", text), P("contractVersion", number), P("kind", checkpointKind), P("protectedScopeIdentity", text)));
        StudioNamedTypeContract barrierResource = Type("base.studio.resource.retirementbarrier", Obj(P("applicationId", text), P("authorityChecksum", checksum), P("authorityEpoch", text), P("contractId", text), P("contractVersion", number), P("incarnation", text), P("kind", barrierKind), P("protectedSubjectIdentity", text)));
        types.AddRange([barrierKind, barrierResource, checkpointKind, checkpointResource, consumerKind, consumerResource, contractKind, subjectKind, contractResource, subjectResource]);

        foreach (StudioViewRegistration view in module.Views.Where(static x => x.ViewId.StartsWith("base.subjects.", StringComparison.Ordinal) ||
                     x.ViewId.StartsWith("base.subjectContract.detail.", StringComparison.Ordinal) || x.ViewId.StartsWith("base.subject.detail.", StringComparison.Ordinal) ||
                     x.ViewId.StartsWith("base.lifecycleConsumer.detail.", StringComparison.Ordinal) || x.ViewId.StartsWith("base.retirementBarrier.detail.", StringComparison.Ordinal)))
        {
            StudioPageRegistration page = module.Pages.Single(x => x.Presentation.Sections.Any(section => section.ViewIds.Contains(view.ViewId)));
            StudioResourceKind requestKind = page.Presentation.NavigationRole == StudioNavigationRole.AreaLanding
                ? StudioResourceKind.Application : page.AcceptedResources[0];
            StudioNamedTypeContract requestResource = requestKind switch { StudioResourceKind.Application => types.Single(static x => x.TypeId == "base.studio.resource.application"),
                StudioResourceKind.SubjectContract => contractResource, StudioResourceKind.Subject => subjectResource,
                StudioResourceKind.LifecycleConsumer => consumerResource, StudioResourceKind.LifecycleCheckpoint => checkpointResource,
                StudioResourceKind.RetirementBarrier => barrierResource, _ => throw new InvalidOperationException() };
            StudioNamedTypeContract request = Type(view.RequestNodeId, Obj(P("resource", requestResource)));
            StudioNamedTypeContract fact = Type(view.ItemNodeId, HPDBaseStudioModuleRegistry.SubjectItemDescriptor(view.ItemKind));
            StudioNamedTypeContract facts = Type(view.ViewId.ToLowerInvariant() + ".items", $"{{\"kind\":\"array\",\"elementTypeId\":\"{fact.TypeId}\",\"minItems\":0,\"maxItems\":500}}");
            StudioNamedTypeContract current = Type(view.ViewId.ToLowerInvariant() + ".current", Obj(P("accounting", accounting), P("evidence", emptyItems),
                P("kind", currentKind), P("links", emptyItems), P("observationAuthority", graphAuthority), P("resource", requestResource), P("value", facts)));
            if (!StudioSha256.FixedTimeEquals(request.NodeChecksum, view.RequestNodeChecksum) || !StudioSha256.FixedTimeEquals(fact.NodeChecksum, view.ItemNodeChecksum))
                throw new InvalidOperationException("A Subjects view differs from its graph-owned L41 nodes.");
            types.AddRange([fact, facts, request, current]); string endpointId = "base.studio.view.page." + view.ViewId; string methodId = "base.studio.view." + view.ViewId;
            endpoints.Add(Endpoint(endpointId, "/base/studio/views/" + view.ViewId, request, current));
            methods.Add(StudioMethodBinding.Create(methodId, StudioMethodKind.Page, "base", page.PageId, endpointId, request.TypeId, current.TypeId));
            producers.Add(new StudioViewProducerBinding(methodId, new SubjectFactProducer(_principals, _authorization, page.Grants, _control, requestKind, view.ItemKind)));
        }

        AddResolver(StudioResourceKind.SubjectContract, contractResource, "base.subjectContract.detail");
        AddResolver(StudioResourceKind.Subject, subjectResource, "base.subject.detail");
        AddResolver(StudioResourceKind.LifecycleConsumer, consumerResource, "base.lifecycleConsumer.detail");
        AddResolver(StudioResourceKind.RetirementBarrier, barrierResource, "base.retirementBarrier.detail");
        AddRetirementCommands(module, types, endpoints, methods, producers, error);

        void AddResolver(StudioResourceKind kind, StudioNamedTypeContract resourceType, string pageId)
        {
            string wireKind = kind.ToString().ToLowerInvariant();
            StudioNamedTypeContract resolved = Type("base.studio." + wireKind + "-resolved", Obj(P("kind", resolvedKind), P("links", emptyItems), P("resource", resourceType), P("route", resourceRoute)));
            types.Add(resolved); string endpointId = "base.studio.resource.resolve." + wireKind; string methodId = "base.studio.resolve." + wireKind;
            endpoints.Add(Endpoint(endpointId, "/base/studio/resources/" + wireKind, tokenRequest, resolved));
            StudioResourceRegistration registration = module.Resources.Single(x => x.Kind == kind);
            methods.Add(StudioMethodBinding.Create(methodId, StudioMethodKind.Resolve, "base", registration.ResolverId, endpointId, tokenRequest.TypeId, resolved.TypeId));
            producers.Add(new StudioResourceProducerBinding(methodId, new SubjectResolver(_principals, _authorization, registration.Grants, _control, kind, pageId)));
        }

        StudioEndpointContract Endpoint(string id, string route, StudioNamedTypeContract request, StudioNamedTypeContract result)
            => StudioEndpointContract.Create(id, 1, StudioTransportMethod.Post, route, StudioEndpointAudience.ControlPlane,
                StudioTransportKind.SameOriginHttp, request.TypeId, request.NodeChecksum, result.TypeId, result.NodeChecksum,
                error.TypeId, error.NodeChecksum, 16_384, 1_048_576, TimeSpan.FromSeconds(10));
        static string P(string name, StudioNamedTypeContract type) => $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type.TypeId}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}";
        static string Obj(params string[] properties) => $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', properties)}],\"additionalProperties\":false}}";
    }

    private sealed class SubjectFactProducer(IStudioPrincipalContextResolver principals, HPDBaseStudioAuthorization authorization,
        ImmutableArray<StudioGrantRequirement> grants, IStudioControlInspectionStore control,
        StudioResourceKind requestKind, StudioResourceKind itemKind) : ProducerBase(principals, authorization, grants), IStudioViewProducer
    {
        public async ValueTask<StudioCanonicalJson?> ReadAsync(StudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) || !RequestResource(invocation.Request, requestKind, out StudioResourceIdentity? resource) ||
                resource is null || !StringComparer.Ordinal.Equals(resource.ApplicationId, invocation.Bootstrap.ApplicationGraph.ApplicationId)) return null;
            StudioControlFactKind kind = SubjectFactKind(itemKind);
            OperationResult<StudioControlInspectionPage> result = await control.ReadStudioControlFactsAsync(SubjectRequest(invocation, kind), cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess() || result.Value is null) return null;
            StudioControlFact[] selected = result.Value.Items.Where(item => SubjectBelongs(resource, item)).ToArray();
            var buffer = new ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer); writer.WriteStartArray();
            foreach (StudioControlFact item in selected)
            {
                StudioResourceIdentity itemResource = SubjectResource(resource.ApplicationId, item);
                var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
                { ["factChecksum"] = Convert.ToHexString(item.FactChecksum.AsSpan()).ToLowerInvariant(), ["identity"] = item.Identity,
                  ["resourceToken"] = StudioResourceRouteToken.Encode(itemResource) };
                if (item is StudioSubjectContractFact published)
                {
                    values["authorityEpoch"] = Convert.ToHexString(published.AuthorityEpoch.AsSpan()).ToLowerInvariant(); values["contractId"] = published.ContractId;
                    values["contractVersion"] = published.ContractVersion.ToString(System.Globalization.CultureInfo.InvariantCulture); values["publicationKind"] = published.PublicationKind.ToString();
                    values["publicationPosition"] = published.PublicationPosition.ToString(System.Globalization.CultureInfo.InvariantCulture); values["restoreEpoch"] = published.RestoreEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    values["stateGeneration"] = published.StateGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    AddSubjectSemanticValues(values, item);
                }
                writer.WriteStartObject(); foreach ((string name, string fieldValue) in values) writer.WriteString(name, fieldValue); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.Flush(); StudioCanonicalJson value = StudioCanonicalJson.Create(buffer.WrittenSpan, 1_048_576);
            return StudioObservationJson.Current(resource, StudioGraphObservationAuthority.Create(invocation.Authority), value, [], [], Accounting(value.ToArray().Length), 1_048_576);
        }
    }

    private sealed class SubjectResolver(IStudioPrincipalContextResolver principals, HPDBaseStudioAuthorization authorization,
        ImmutableArray<StudioGrantRequirement> grants, IStudioControlInspectionStore control,
        StudioResourceKind kind, string pageId) : ProducerBase(principals, authorization, grants), IStudioResourceProducer
    {
        public async ValueTask<StudioCanonicalJson?> ResolveAsync(StudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) || !Token(invocation.Request, out StudioResourceIdentity? resource) ||
                resource is null || resource.Kind != kind || !StringComparer.Ordinal.Equals(resource.ApplicationId, invocation.Bootstrap.ApplicationGraph.ApplicationId)) return null;
            StudioControlFactKind factKind = SubjectFactKind(kind);
            OperationResult<StudioControlInspectionPage> result = await control.ReadStudioControlFactsAsync(SubjectRequest(invocation, factKind), cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess() || result.Value is null || !result.Value.Items.Any(item => SubjectBelongs(resource, item))) return null;
            string token = StudioResourceRouteToken.Encode(resource); return StudioResolvedResourceJson.Encode(resource,
                StudioResolvedRoute.Create(pageId, [new KeyValuePair<string, string>("resource", token)]), [], 1_048_576);
        }
    }

    private static StudioControlInspectionRequest SubjectRequest(StudioProducerInvocation invocation, StudioControlFactKind kind) => new()
    { ApplicationId = invocation.Bootstrap.ApplicationGraph.ApplicationId, Kind = kind, Take = 500,
      ProtectedScopeChecksum = [.. invocation.Authority.ProtectedScopeChecksum.ToArray()], Limits = new()
      { MaximumItems = 500, MaximumRowsRead = 10_000, MaximumEvidenceBytes = 8_388_608, MaximumTransientBytes = 8_388_608, Deadline = TimeSpan.FromSeconds(5) } };

    private static bool SubjectBelongs(StudioResourceIdentity resource, StudioControlFact fact) => resource switch
    {
        StudioApplicationResource => true,
        StudioSubjectContractResource x => fact switch { StudioSubjectContractFact y => y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion,
            StudioSubjectFact y => y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion,
            StudioLifecycleConsumerFact y => y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion,
            StudioLifecycleCheckpointFact y => y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion,
            StudioRetirementBarrierFact y => y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion, _ => false },
        StudioSubjectResource x => fact switch { StudioSubjectFact y => y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion && y.SubjectId == x.ProtectedSubjectIdentity,
            StudioRetirementBarrierFact y => y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion && y.ProtectedSubjectIdentity == x.ProtectedSubjectIdentity, _ => false },
        StudioLifecycleConsumerResource x => fact is StudioLifecycleConsumerFact y && y.ConsumerId == x.ConsumerId && y.ConsumerVersion == x.Version && y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion,
        StudioLifecycleCheckpointResource x => fact is StudioLifecycleCheckpointFact y && y.ConsumerId == x.ConsumerId && y.ConsumerVersion == x.ConsumerVersion && y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion && y.ProtectedScopeIdentity == x.ProtectedScopeIdentity,
        StudioRetirementBarrierResource x => fact switch { StudioRetirementBarrierFact y => y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion && y.ProtectedSubjectIdentity == x.ProtectedSubjectIdentity && y.AuthorityEpoch == x.AuthorityEpoch && y.Incarnation == x.Incarnation,
            StudioLifecycleConsumerFact y => y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion, _ => false },
        _ => false,
    };

    private static StudioControlFactKind SubjectFactKind(StudioResourceKind kind) => kind switch
    { StudioResourceKind.SubjectContract => StudioControlFactKind.SubjectContract, StudioResourceKind.Subject => StudioControlFactKind.Subject,
      StudioResourceKind.LifecycleConsumer => StudioControlFactKind.LifecycleConsumer, StudioResourceKind.LifecycleCheckpoint => StudioControlFactKind.LifecycleCheckpoint,
      StudioResourceKind.RetirementBarrier => StudioControlFactKind.RetirementBarrier, _ => throw new ArgumentOutOfRangeException(nameof(kind)) };

    private static StudioResourceIdentity SubjectResource(string applicationId, StudioControlFact fact) => fact switch
    {
        StudioSubjectContractFact x => new StudioSubjectContractResource(applicationId,x.ContractId,x.ContractVersion),
        StudioSubjectFact x => new StudioSubjectResource(applicationId,x.ContractId,x.ContractVersion,x.SubjectId),
        StudioLifecycleConsumerFact x => new StudioLifecycleConsumerResource(applicationId,x.ConsumerId,x.ConsumerVersion,x.ContractId,x.ContractVersion),
        StudioLifecycleCheckpointFact x => new StudioLifecycleCheckpointResource(applicationId,x.ConsumerId,x.ConsumerVersion,x.ContractId,x.ContractVersion,x.ProtectedScopeIdentity),
        StudioRetirementBarrierFact x => new StudioRetirementBarrierResource(applicationId,x.ContractId,x.ContractVersion,x.ProtectedSubjectIdentity,x.AuthorityEpoch,x.Incarnation),
        _ => throw new InvalidDataException(),
    };

    private static void AddSubjectSemanticValues(SortedDictionary<string,string> values, StudioControlFact item)
    {
        switch(item)
        {
            case StudioSubjectFact x: values["contractId"]=x.ContractId;values["contractVersion"]=x.ContractVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);values["createdJournalPosition"]=x.CreatedJournalPosition.ToString(System.Globalization.CultureInfo.InvariantCulture);values["incarnation"]=Convert.ToHexString(x.Incarnation.AsSpan()).ToLowerInvariant();values["protectedSubjectIdentity"]=x.SubjectId;break;
            case StudioLifecycleConsumerFact x: values["consumerChecksum"]=x.ConsumerChecksum;values["consumerId"]=x.ConsumerId;values["consumerVersion"]=x.ConsumerVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);values["contractId"]=x.ContractId;values["contractVersion"]=x.ContractVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);values["deliveryEpoch"]=x.DeliveryEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture);values["projectionGeneration"]=x.ProjectionGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture);values["publishedGraphGeneration"]=x.PublishedGraphGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture);break;
            case StudioLifecycleCheckpointFact x: values["checkpointGeneration"]=x.CheckpointGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture);values["consumerId"]=x.ConsumerId;values["consumerVersion"]=x.ConsumerVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);values["throughBoundary"]=x.ThroughBoundary;break;
            case StudioRetirementBarrierFact x: values["barrierGeneration"]=x.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture);values["barrierState"]=x.State.ToString();values["deadlineUtc"]=x.DeadlineUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",System.Globalization.CultureInfo.InvariantCulture);values["requiredConsumerSetChecksum"]=x.RequiredConsumerSetChecksum;values["tombstoneSequence"]=x.TombstoneSequence.ToString(System.Globalization.CultureInfo.InvariantCulture);break;
            default: throw new InvalidDataException();
        }
    }
}
