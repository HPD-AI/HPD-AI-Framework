using System.Buffers;
using System.Collections.Immutable;
using System.Text.Json;
using HPD.AI.Platform.Studio;

namespace HPD.Base.Studio;

internal sealed partial class BaseStudioRuntimeContributionFactory
{
    private void AddSubjectsRuntime(BaseStudioModuleRegistration module,
        List<BaseStudioNamedTypeContract> types, List<BaseStudioEndpointContract> endpoints,
        List<BaseStudioMethodBinding> methods, List<BaseStudioProducerBinding> producers,
        BaseStudioNamedTypeContract error, BaseStudioNamedTypeContract text, BaseStudioNamedTypeContract checksum,
        BaseStudioNamedTypeContract currentKind, BaseStudioNamedTypeContract graphAuthority,
        BaseStudioNamedTypeContract accounting, BaseStudioNamedTypeContract emptyItems,
        BaseStudioNamedTypeContract tokenRequest, BaseStudioNamedTypeContract resourceRoute,
        BaseStudioNamedTypeContract resolvedKind)
    {
        BaseStudioNamedTypeContract number = types.Single(static x => x.TypeId == "base.studio.positive-number");
        BaseStudioNamedTypeContract contractKind = Type("base.studio.resource-kind.subjectcontract", "{\"kind\":\"literal\",\"value\":\"subjectContract\"}");
        BaseStudioNamedTypeContract subjectKind = Type("base.studio.resource-kind.subject", "{\"kind\":\"literal\",\"value\":\"subject\"}");
        BaseStudioNamedTypeContract consumerKind = Type("base.studio.resource-kind.lifecycleconsumer", "{\"kind\":\"literal\",\"value\":\"lifecycleConsumer\"}");
        BaseStudioNamedTypeContract checkpointKind = Type("base.studio.resource-kind.lifecyclecheckpoint", "{\"kind\":\"literal\",\"value\":\"lifecycleCheckpoint\"}");
        BaseStudioNamedTypeContract barrierKind = Type("base.studio.resource-kind.retirementbarrier", "{\"kind\":\"literal\",\"value\":\"retirementBarrier\"}");
        BaseStudioNamedTypeContract contractResource = Type("base.studio.resource.subjectcontract", Obj(P("applicationId", text), P("authorityChecksum", checksum),
            P("contractId", text), P("contractVersion", number), P("kind", contractKind)));
        BaseStudioNamedTypeContract subjectResource = Type("base.studio.resource.subject", Obj(P("applicationId", text), P("authorityChecksum", checksum),
            P("contractId", text), P("contractVersion", number), P("kind", subjectKind), P("protectedSubjectIdentity", text)));
        BaseStudioNamedTypeContract consumerResource = Type("base.studio.resource.lifecycleconsumer", Obj(P("applicationId", text), P("authorityChecksum", checksum), P("consumerId", text), P("contractId", text), P("contractVersion", number), P("kind", consumerKind), P("version", number)));
        BaseStudioNamedTypeContract checkpointResource = Type("base.studio.resource.lifecyclecheckpoint", Obj(P("applicationId", text), P("authorityChecksum", checksum), P("consumerId", text), P("consumerVersion", number), P("contractId", text), P("contractVersion", number), P("kind", checkpointKind), P("protectedScopeIdentity", text)));
        BaseStudioNamedTypeContract barrierResource = Type("base.studio.resource.retirementbarrier", Obj(P("applicationId", text), P("authorityChecksum", checksum), P("authorityEpoch", text), P("contractId", text), P("contractVersion", number), P("incarnation", text), P("kind", barrierKind), P("protectedSubjectIdentity", text)));
        types.AddRange([barrierKind, barrierResource, checkpointKind, checkpointResource, consumerKind, consumerResource, contractKind, subjectKind, contractResource, subjectResource]);

        foreach (BaseStudioViewRegistration view in module.Views.Where(static x => x.ViewId.StartsWith("base.subjects.", StringComparison.Ordinal) ||
                     x.ViewId.StartsWith("base.subjectContract.detail.", StringComparison.Ordinal) || x.ViewId.StartsWith("base.subject.detail.", StringComparison.Ordinal) ||
                     x.ViewId.StartsWith("base.lifecycleConsumer.detail.", StringComparison.Ordinal) || x.ViewId.StartsWith("base.retirementBarrier.detail.", StringComparison.Ordinal)))
        {
            BaseStudioPageRegistration page = module.Pages.Single(x => x.Presentation.Sections.Any(section => section.ViewIds.Contains(view.ViewId)));
            BaseStudioResourceKind requestKind = page.Presentation.NavigationRole == BaseStudioNavigationRole.AreaLanding
                ? BaseStudioResourceKind.Application : page.AcceptedResources[0];
            BaseStudioNamedTypeContract requestResource = requestKind switch { BaseStudioResourceKind.Application => types.Single(static x => x.TypeId == "base.studio.resource.application"),
                BaseStudioResourceKind.SubjectContract => contractResource, BaseStudioResourceKind.Subject => subjectResource,
                BaseStudioResourceKind.LifecycleConsumer => consumerResource, BaseStudioResourceKind.LifecycleCheckpoint => checkpointResource,
                BaseStudioResourceKind.RetirementBarrier => barrierResource, _ => throw new InvalidOperationException() };
            BaseStudioNamedTypeContract request = Type(view.RequestNodeId, Obj(P("resource", requestResource)));
            BaseStudioNamedTypeContract fact = Type(view.ItemNodeId, BaseStudioModuleRegistry.SubjectItemDescriptor(view.ItemKind));
            BaseStudioNamedTypeContract facts = Type(view.ViewId.ToLowerInvariant() + ".items", $"{{\"kind\":\"array\",\"elementTypeId\":\"{fact.TypeId}\",\"minItems\":0,\"maxItems\":500}}");
            BaseStudioNamedTypeContract current = Type(view.ViewId.ToLowerInvariant() + ".current", Obj(P("accounting", accounting), P("evidence", emptyItems),
                P("kind", currentKind), P("links", emptyItems), P("observationAuthority", graphAuthority), P("resource", requestResource), P("value", facts)));
            if (!BaseStudioSha256.FixedTimeEquals(request.NodeChecksum, view.RequestNodeChecksum) || !BaseStudioSha256.FixedTimeEquals(fact.NodeChecksum, view.ItemNodeChecksum))
                throw new InvalidOperationException("A Subjects view differs from its graph-owned L41 nodes.");
            types.AddRange([fact, facts, request, current]); string endpointId = "base.studio.view.page." + view.ViewId; string methodId = "base.studio.view." + view.ViewId;
            endpoints.Add(Endpoint(endpointId, "/base/studio/views/" + view.ViewId, request, current));
            methods.Add(BaseStudioMethodBinding.Create(methodId, BaseStudioMethodKind.Page, "base", page.PageId, endpointId, request.TypeId, current.TypeId));
            producers.Add(new BaseStudioViewProducerBinding(methodId, new SubjectFactProducer(_principals, _authorization, page.Grants, _control, requestKind, view.ItemKind)));
        }

        AddResolver(BaseStudioResourceKind.SubjectContract, contractResource, "base.subjectContract.detail");
        AddResolver(BaseStudioResourceKind.Subject, subjectResource, "base.subject.detail");
        AddResolver(BaseStudioResourceKind.LifecycleConsumer, consumerResource, "base.lifecycleConsumer.detail");
        AddResolver(BaseStudioResourceKind.RetirementBarrier, barrierResource, "base.retirementBarrier.detail");
        AddRetirementCommands(module, types, endpoints, methods, producers, error);

        void AddResolver(BaseStudioResourceKind kind, BaseStudioNamedTypeContract resourceType, string pageId)
        {
            string wireKind = kind.ToString().ToLowerInvariant();
            BaseStudioNamedTypeContract resolved = Type("base.studio." + wireKind + "-resolved", Obj(P("kind", resolvedKind), P("links", emptyItems), P("resource", resourceType), P("route", resourceRoute)));
            types.Add(resolved); string endpointId = "base.studio.resource.resolve." + wireKind; string methodId = "base.studio.resolve." + wireKind;
            endpoints.Add(Endpoint(endpointId, "/base/studio/resources/" + wireKind, tokenRequest, resolved));
            BaseStudioResourceRegistration registration = module.Resources.Single(x => x.Kind == kind);
            methods.Add(BaseStudioMethodBinding.Create(methodId, BaseStudioMethodKind.Resolve, "base", registration.ResolverId, endpointId, tokenRequest.TypeId, resolved.TypeId));
            producers.Add(new BaseStudioResourceProducerBinding(methodId, new SubjectResolver(_principals, _authorization, registration.Grants, _control, kind, pageId)));
        }

        BaseStudioEndpointContract Endpoint(string id, string route, BaseStudioNamedTypeContract request, BaseStudioNamedTypeContract result)
            => BaseStudioEndpointContract.Create(id, 1, BaseStudioTransportMethod.Post, route, BaseStudioEndpointAudience.ControlPlane,
                BaseStudioTransportKind.SameOriginHttp, request.TypeId, request.NodeChecksum, result.TypeId, result.NodeChecksum,
                error.TypeId, error.NodeChecksum, 16_384, 1_048_576, TimeSpan.FromSeconds(10));
        static string P(string name, BaseStudioNamedTypeContract type) => $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type.TypeId}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}";
        static string Obj(params string[] properties) => $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', properties)}],\"additionalProperties\":false}}";
    }

    private sealed class SubjectFactProducer(IBaseStudioPrincipalContextResolver principals, BaseStudioAuthorization authorization,
        ImmutableArray<BaseStudioGrantRequirement> grants, IBaseStudioControlInspectionStore control,
        BaseStudioResourceKind requestKind, BaseStudioResourceKind itemKind) : ProducerBase(principals, authorization, grants), IBaseStudioViewProducer
    {
        public async ValueTask<BaseStudioCanonicalJson?> ReadAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) || !RequestResource(invocation.Request, requestKind, out BaseStudioResourceIdentity? resource) ||
                resource is null || !StringComparer.Ordinal.Equals(resource.ApplicationId, invocation.Bootstrap.ApplicationGraph.ApplicationId)) return null;
            BaseStudioControlFactKind kind = SubjectFactKind(itemKind);
            OperationResult<BaseStudioControlInspectionPage> result = await control.ReadStudioControlFactsAsync(SubjectRequest(invocation, kind), cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess() || result.Value is null) return null;
            BaseStudioControlFact[] selected = result.Value.Items.Where(item => SubjectBelongs(resource, item)).ToArray();
            var buffer = new ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer); writer.WriteStartArray();
            foreach (BaseStudioControlFact item in selected)
            {
                BaseStudioResourceIdentity itemResource = SubjectResource(resource.ApplicationId, item);
                var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
                { ["factChecksum"] = Convert.ToHexString(item.FactChecksum.AsSpan()).ToLowerInvariant(), ["identity"] = item.Identity,
                  ["resourceToken"] = BaseStudioResourceRouteToken.Encode(itemResource) };
                if (item is BaseStudioSubjectContractFact published)
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
            writer.WriteEndArray(); writer.Flush(); BaseStudioCanonicalJson value = BaseStudioCanonicalJson.Create(buffer.WrittenSpan, 1_048_576);
            return BaseStudioObservationJson.Current(resource, BaseStudioGraphObservationAuthority.Create(invocation.Authority), value, [], [], Accounting(value.ToArray().Length), 1_048_576);
        }
    }

    private sealed class SubjectResolver(IBaseStudioPrincipalContextResolver principals, BaseStudioAuthorization authorization,
        ImmutableArray<BaseStudioGrantRequirement> grants, IBaseStudioControlInspectionStore control,
        BaseStudioResourceKind kind, string pageId) : ProducerBase(principals, authorization, grants), IBaseStudioResourceProducer
    {
        public async ValueTask<BaseStudioCanonicalJson?> ResolveAsync(BaseStudioProducerInvocation invocation, CancellationToken cancellationToken)
        {
            if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) || !Token(invocation.Request, out BaseStudioResourceIdentity? resource) ||
                resource is null || resource.Kind != kind || !StringComparer.Ordinal.Equals(resource.ApplicationId, invocation.Bootstrap.ApplicationGraph.ApplicationId)) return null;
            BaseStudioControlFactKind factKind = SubjectFactKind(kind);
            OperationResult<BaseStudioControlInspectionPage> result = await control.ReadStudioControlFactsAsync(SubjectRequest(invocation, factKind), cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess() || result.Value is null || !result.Value.Items.Any(item => SubjectBelongs(resource, item))) return null;
            string token = BaseStudioResourceRouteToken.Encode(resource); return BaseStudioResolvedResourceJson.Encode(resource,
                BaseStudioResolvedRoute.Create(pageId, [new KeyValuePair<string, string>("resource", token)]), [], 1_048_576);
        }
    }

    private static BaseStudioControlInspectionRequest SubjectRequest(BaseStudioProducerInvocation invocation, BaseStudioControlFactKind kind) => new()
    { ApplicationId = invocation.Bootstrap.ApplicationGraph.ApplicationId, Kind = kind, Take = 500,
      ProtectedScopeChecksum = [.. invocation.Authority.ProtectedScopeChecksum.ToArray()], Limits = new()
      { MaximumItems = 500, MaximumRowsRead = 10_000, MaximumEvidenceBytes = 8_388_608, MaximumTransientBytes = 8_388_608, Deadline = TimeSpan.FromSeconds(5) } };

    private static bool SubjectBelongs(BaseStudioResourceIdentity resource, BaseStudioControlFact fact) => resource switch
    {
        BaseStudioApplicationResource => true,
        BaseStudioSubjectContractResource x => fact switch { BaseStudioSubjectContractFact y => y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion,
            BaseStudioSubjectFact y => y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion,
            BaseStudioLifecycleConsumerFact y => y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion,
            BaseStudioLifecycleCheckpointFact y => y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion,
            BaseStudioRetirementBarrierFact y => y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion, _ => false },
        BaseStudioSubjectResource x => fact switch { BaseStudioSubjectFact y => y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion && y.SubjectId == x.ProtectedSubjectIdentity,
            BaseStudioRetirementBarrierFact y => y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion && y.ProtectedSubjectIdentity == x.ProtectedSubjectIdentity, _ => false },
        BaseStudioLifecycleConsumerResource x => fact is BaseStudioLifecycleConsumerFact y && y.ConsumerId == x.ConsumerId && y.ConsumerVersion == x.Version && y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion,
        BaseStudioLifecycleCheckpointResource x => fact is BaseStudioLifecycleCheckpointFact y && y.ConsumerId == x.ConsumerId && y.ConsumerVersion == x.ConsumerVersion && y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion && y.ProtectedScopeIdentity == x.ProtectedScopeIdentity,
        BaseStudioRetirementBarrierResource x => fact switch { BaseStudioRetirementBarrierFact y => y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion && y.ProtectedSubjectIdentity == x.ProtectedSubjectIdentity && y.AuthorityEpoch == x.AuthorityEpoch && y.Incarnation == x.Incarnation,
            BaseStudioLifecycleConsumerFact y => y.ContractId == x.ContractId && y.ContractVersion == x.ContractVersion, _ => false },
        _ => false,
    };

    private static BaseStudioControlFactKind SubjectFactKind(BaseStudioResourceKind kind) => kind switch
    { BaseStudioResourceKind.SubjectContract => BaseStudioControlFactKind.SubjectContract, BaseStudioResourceKind.Subject => BaseStudioControlFactKind.Subject,
      BaseStudioResourceKind.LifecycleConsumer => BaseStudioControlFactKind.LifecycleConsumer, BaseStudioResourceKind.LifecycleCheckpoint => BaseStudioControlFactKind.LifecycleCheckpoint,
      BaseStudioResourceKind.RetirementBarrier => BaseStudioControlFactKind.RetirementBarrier, _ => throw new ArgumentOutOfRangeException(nameof(kind)) };

    private static BaseStudioResourceIdentity SubjectResource(string applicationId, BaseStudioControlFact fact) => fact switch
    {
        BaseStudioSubjectContractFact x => new BaseStudioSubjectContractResource(applicationId,x.ContractId,x.ContractVersion),
        BaseStudioSubjectFact x => new BaseStudioSubjectResource(applicationId,x.ContractId,x.ContractVersion,x.SubjectId),
        BaseStudioLifecycleConsumerFact x => new BaseStudioLifecycleConsumerResource(applicationId,x.ConsumerId,x.ConsumerVersion,x.ContractId,x.ContractVersion),
        BaseStudioLifecycleCheckpointFact x => new BaseStudioLifecycleCheckpointResource(applicationId,x.ConsumerId,x.ConsumerVersion,x.ContractId,x.ContractVersion,x.ProtectedScopeIdentity),
        BaseStudioRetirementBarrierFact x => new BaseStudioRetirementBarrierResource(applicationId,x.ContractId,x.ContractVersion,x.ProtectedSubjectIdentity,x.AuthorityEpoch,x.Incarnation),
        _ => throw new InvalidDataException(),
    };

    private static void AddSubjectSemanticValues(SortedDictionary<string,string> values, BaseStudioControlFact item)
    {
        switch(item)
        {
            case BaseStudioSubjectFact x: values["contractId"]=x.ContractId;values["contractVersion"]=x.ContractVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);values["createdJournalPosition"]=x.CreatedJournalPosition.ToString(System.Globalization.CultureInfo.InvariantCulture);values["incarnation"]=Convert.ToHexString(x.Incarnation.AsSpan()).ToLowerInvariant();values["protectedSubjectIdentity"]=x.SubjectId;break;
            case BaseStudioLifecycleConsumerFact x: values["consumerChecksum"]=x.ConsumerChecksum;values["consumerId"]=x.ConsumerId;values["consumerVersion"]=x.ConsumerVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);values["contractId"]=x.ContractId;values["contractVersion"]=x.ContractVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);values["deliveryEpoch"]=x.DeliveryEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture);values["projectionGeneration"]=x.ProjectionGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture);values["publishedGraphGeneration"]=x.PublishedGraphGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture);break;
            case BaseStudioLifecycleCheckpointFact x: values["checkpointGeneration"]=x.CheckpointGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture);values["consumerId"]=x.ConsumerId;values["consumerVersion"]=x.ConsumerVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);values["throughBoundary"]=x.ThroughBoundary;break;
            case BaseStudioRetirementBarrierFact x: values["barrierGeneration"]=x.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture);values["barrierState"]=x.State.ToString();values["deadlineUtc"]=x.DeadlineUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",System.Globalization.CultureInfo.InvariantCulture);values["requiredConsumerSetChecksum"]=x.RequiredConsumerSetChecksum;values["tombstoneSequence"]=x.TombstoneSequence.ToString(System.Globalization.CultureInfo.InvariantCulture);break;
            default: throw new InvalidDataException();
        }
    }
}
