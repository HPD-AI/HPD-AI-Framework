using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Base;
using HPD.Base.AotSmoke;
using Microsoft.Extensions.DependencyInjection;

var collection = AotProject.Collection;
var value = new AotProject
{
    OrganizationId = "org_aot",
    Name = "AOT",
};

if (AotProject.Fields.OrganizationId.Id != "organization-id" ||
    AotProject.Fields.OrganizationId.WireName != "organizationId" ||
    !AotProject.Fields.Name.Operators.HasFlag(BaseFieldOperator.Order))
{
    throw new InvalidOperationException(
        "Generated application contracts must survive Native AOT.");
}

var services = new ServiceCollection();
services.AddLogging();
services.AddHPDBase(hpd =>
{
    hpd.AddPolicyAuthority(new BasePolicyAuthorityDefinition
    {
        Id = "hpd.base.aot.allow", Version = 1, OwningModuleId = "hpd.base.aot",
        EvaluatorContractId = "hpd.base.aot.policy", EvaluatorContractVersion = 1, CompositionOrder = 0,
    }, new AotAllowPolicyEvaluator());
    foreach (string grantId in new[] { "hpd.base.aot.subject.private", "hpd.base.aot.subject.acquire", "hpd.base.aot.subject.validate", "hpd.base.aot.subject.rotate" })
        hpd.AddStaticGrantAuthority(GrantDefinition(grantId, "hpd.base.aot"), Grant(grantId, "aot"));
    hpd.AddModuleGenerationCell(ModuleMutationSmoke.Cell);
    hpd.AddModuleMutation(ModuleMutationSmoke.Definition, ModuleMutationSmoke.Identity);
    hpd.AddCollection(collection);
    hpd.AddCollection(AotPrivateSubjectRecord.Collection);
    hpd.AddCollection(AotSubjectConsumerRecord.Collection);
    hpd.AddExportedSubject(AotSubject.HPDBaseSubjectRegistration);
    hpd.AddRead(AotAcquireSubject.Definition);
    hpd.AddSubjectAcquisition(new BaseSubjectAcquisitionDefinition
    {
        Id = "hpd.base.aot.subject.acquire.v1",
        Version = 1,
        ContractId = "hpd.base.aot.subject",
        ContractVersion = 1,
        RegisteredReadId = "hpd.base.aot.subject.acquire",
        RequiredGrantId = "hpd.base.aot.subject.acquire",
        Audience = HPDBaseEndpointAudience.Application,
        MaximumResults = 1,
    });
});
using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateOnBuild = true });
if (!(await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess())
    throw new InvalidOperationException("InMemory application initialization failed.");
var session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
{
    AuthenticationState = PrincipalAuthenticationState.Service,
    SubjectKind = AccessSubjectKind.ServicePrincipal,
    SubjectId = "aot",
    CurrentTenantId = "tenant-a",
});
BaseCollectionSession<AotProject> projects = session.Collection(collection);
BaseMutationRequestIdentity moduleIdentity = BaseMutationRequestIdentity.Create(
    "aot", "module-increment", "module-request-1",
    BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("aot-module-request"u8)));
BaseInstalledModuleMutationHandle<ModuleMutationSmokeRequest, ModuleMutationSmokeResult> module =
    session.ModuleMutations.Get(ModuleMutationSmoke.Identity);
BaseModuleMutationExecutionResult<ModuleMutationSmokeResult> moduleCommitted =
    (await module.ExecuteAsync(new ModuleMutationSmokeRequest { Marker = "aot" }, moduleIdentity)).RequireValue();
BaseModuleMutationExecutionResult<ModuleMutationSmokeResult> moduleDuplicate =
    (await module.ExecuteAsync(new ModuleMutationSmokeRequest { Marker = "aot" }, moduleIdentity)).RequireValue();
if (moduleCommitted.Result.Generation != "1" || moduleCommitted.Disposition != BaseMutationRequestDisposition.Committed
    || moduleDuplicate.Result.Generation != "1" || moduleDuplicate.Disposition != BaseMutationRequestDisposition.Duplicate)
    throw new InvalidOperationException("InMemory L50 generation commit or receipt replay failed.");
if (provider.GetRequiredService<HPDBaseInstalledFeatures>().Provider != "inmemory"
    || provider.GetRequiredService<IRecordStore>().Capabilities.StoreKind != BaseStoreKinds.InMemory)
{
    throw new InvalidOperationException(
        "The built-in InMemory provider must be the Native AOT-safe default.");
}

BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
    "aot", "create-project", "request-1",
    BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("aot-request"u8)));
BaseBatchBuilder initial = session.Atomic(identity);
initial.Create(collection, new RecordId("project-1"), value);
BaseBatchBuilder retry = session.Atomic(identity);
retry.Create(collection, new RecordId("project-1"), value);
BaseResult<BaseBatchResult> committed = await initial.CommitAsync();
BaseResult<BaseBatchResult> duplicate = await retry.CommitAsync();
if (committed is not BaseSuccess<BaseBatchResult> committedSuccess
    || committedSuccess.Value.RequestDisposition != BaseMutationRequestDisposition.Committed
    || duplicate is not BaseSuccess<BaseBatchResult> duplicateSuccess
    || duplicateSuccess.Value.RequestDisposition != BaseMutationRequestDisposition.Duplicate)
{
    throw new InvalidOperationException("InMemory identified request replay failed.");
}
_ = (await projects.CreateAsync(new RecordId("project-2"), value with { Name = "AOT 2" })).RequireValue();
BasePage<BaseRecord<AotProject>> firstPage = (await projects.Query()
    .OrderBy(AotProject.Fields.Name)
    .Take(1)
    .PageAsync()).RequireValue();
if (firstPage.Page.NextCursor is null)
    throw new InvalidOperationException("InMemory opaque cursor continuation failed.");

var principal = new PrincipalContext
{
    AuthenticationState = PrincipalAuthenticationState.Service,
    SubjectKind = AccessSubjectKind.ServicePrincipal,
    SubjectId = "aot",
    CurrentTenantId = "tenant-a",
};
IBaseRecordRuntime runtime = provider.GetRequiredService<IBaseRecordRuntime>();
OperationResult<RecordEnvelope> subjectCreate = await runtime.CreateAsync(
    AotPrivateSubjectRecord.Collection.Id,
    new RecordCreateRequest
    {
        RequestedId = new RecordId("subject-1"),
        Payload = JsonPayload("""{"active":true,"tenant":"tenant-a"}"""),
    },
    principal,
    Operation(BaseOperationKind.Create, AotPrivateSubjectRecord.Collection.Id));
if (!subjectCreate.IsSuccess())
    throw new InvalidOperationException("InMemory exported-subject creation failed: " + subjectCreate.Error?.Code);

BaseSession subjectSession = provider.GetRequiredService<IBaseSessionFactory>().For(principal);
BaseSubjectReference<AotSubject> subjectReference = (await subjectSession.Reads.ToArrayAsync(
    AotAcquireSubject.Handle,
    new AotAcquireSubject { SubjectId = BaseRecordId<AotPrivateSubjectRecord>.Create("subject-1") }))
    .RequireValue()
    .Single()
    .Reference;
OperationResult<RecordEnvelope> referenceCreate = await runtime.CreateAsync(
    AotSubjectConsumerRecord.Collection.Id,
    new RecordCreateRequest
    {
        RequestedId = new RecordId("consumer-1"),
        Payload = new RecordPayload
        {
            Kind = RecordPayloadKind.Json,
            Json = JsonSerializer.SerializeToElement(
                new AotSubjectConsumerRecord { Subject = subjectReference },
                AotApplicationJsonContext.Default.AotSubjectConsumerRecord),
        },
    },
    principal,
    Operation(BaseOperationKind.Create, AotSubjectConsumerRecord.Collection.Id));
if (!referenceCreate.IsSuccess())
    throw new InvalidOperationException("InMemory subject-reference validation failed: " + referenceCreate.Error?.Code);

OperationResult<RecordEnvelope> deactivate = await runtime.PatchAsync(
    AotPrivateSubjectRecord.Collection.Id,
    new RecordId("subject-1"),
    new RecordPatchRequest { Patch = FieldPatch("active", false) },
    principal,
    Operation(BaseOperationKind.Patch, AotPrivateSubjectRecord.Collection.Id));
if (!deactivate.IsSuccess())
    throw new InvalidOperationException("InMemory subject deactivation failed: " + deactivate.Error?.Code);

OperationResult<RecordEnvelope> invalidReference = await runtime.CreateAsync(
    AotSubjectConsumerRecord.Collection.Id,
    new RecordCreateRequest
    {
        RequestedId = new RecordId("consumer-2"),
        Payload = new RecordPayload
        {
            Kind = RecordPayloadKind.Json,
            Json = JsonSerializer.SerializeToElement(
                new AotSubjectConsumerRecord { Subject = subjectReference },
                AotApplicationJsonContext.Default.AotSubjectConsumerRecord),
        },
    },
    principal,
    Operation(BaseOperationKind.Create, AotSubjectConsumerRecord.Collection.Id));
if (invalidReference.Error?.Code != BaseSubjectErrorCodes.ReferenceInvalid)
    throw new InvalidOperationException("InMemory stale/inactive subject reference was accepted.");

static OperationContext Operation(BaseOperationKind kind, string collectionId) => new()
{
    Operation = kind,
    CollectionId = collectionId,
    Now = DateTimeOffset.UtcNow,
};

static BaseGrantAuthorityDefinition GrantDefinition(string id, string owner) => new()
{
    Id = id, Version = 1, OwningModuleId = owner,
    SourceContractId = owner + ".static-grant", SourceContractVersion = 1,
};

static AccessGrant Grant(string id, string subjectId) => new()
{
    Id = id, Subject = new AccessSubject { Kind = AccessSubjectKind.ServicePrincipal, Id = subjectId },
    Action = "*", Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime },
};

static RecordPayload JsonPayload(string json)
{
    using JsonDocument document = JsonDocument.Parse(json);
    return new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() };
}

static RecordPayload FieldPatch(string name, bool value)
{
    using JsonDocument document = JsonDocument.Parse(value ? "true" : "false");
    return new RecordPayload
    {
        Kind = RecordPayloadKind.FieldMap,
        Fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [name] = document.RootElement.Clone(),
        },
    };
}

namespace HPD.Base.AotSmoke
{
    [BaseCollection("aot.projects", typeof(AotApplicationJsonContext))]
    internal sealed partial record AotProject
    {
        [BaseField("organization-id")]
        public required string OrganizationId { get; init; }

        [BaseField("name", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)]
        public required string Name { get; init; }
    }

    [JsonSerializable(typeof(AotProject))]
    [JsonSerializable(typeof(AotPrivateSubjectRecord))]
    [JsonSerializable(typeof(AotSubjectConsumerRecord))]
    [JsonSerializable(typeof(AotAcquireSubject))]
    [JsonSerializable(typeof(AotAcquireSubject.Row), TypeInfoPropertyName = "AotAcquireSubjectRow")]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    internal sealed partial class AotApplicationJsonContext : JsonSerializerContext;

    [BaseCollection("aot.subject.private", typeof(AotApplicationJsonContext),
        SystemOwnerModuleId = "hpd.base.aot.subjects")]
    internal sealed partial record AotPrivateSubjectRecord
    {
        [BaseField("subject.active")]
        public required bool Active { get; init; }

        [BaseField("subject.tenant")]
        public required string Tenant { get; init; }
    }

    [BaseExportedSubject("hpd.base.aot.subject",
        OwningModuleId = "hpd.base.aot.subjects",
        PrivateRecordType = typeof(AotPrivateSubjectRecord),
        AcquisitionGrantId = "hpd.base.aot.subject.acquire",
        ValidationGrantId = "hpd.base.aot.subject.validate",
        AdministrationGrantId = "hpd.base.aot.subject.rotate",
        ValidationPlanId = "hpd.base.aot.subject.validate.v1",
        Scope = BaseSubjectScopeKind.Tenant,
        ActiveFieldId = "subject.active",
        ScopeFieldId = "subject.tenant")]
    internal sealed partial class AotSubject;

    [BaseCollection("aot.subject.consumers", typeof(AotApplicationJsonContext))]
    internal sealed partial record AotSubjectConsumerRecord
    {
        [BaseField("consumer.subject")]
        [BaseSubjectReference(typeof(AotSubject), Requirement = BaseSubjectReferenceRequirement.Active)]
        public required BaseSubjectReference<AotSubject> Subject { get; init; }
    }

    [BaseRead("hpd.base.aot.subject.acquire", typeof(AotApplicationJsonContext),
        SourceAuthority = BaseRegisteredReadSourceAuthority.System,
        Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
        RequiredGrantId = "hpd.base.aot.subject.acquire",
        SystemSourceIds = ["aot.subject.private"])]
    internal sealed partial record AotAcquireSubject
    {
        [BaseReadParameter("hpd.base.aot.subject.acquire.id")]
        public required BaseRecordId<AotPrivateSubjectRecord> SubjectId { get; init; }

        public sealed partial record Row
        {
            [BaseReadField("hpd.base.aot.subject.acquire.reference")]
            public required BaseSubjectReference<AotSubject> Reference { get; init; }
        }

        public static void Configure(BaseReadDefinitionBuilder<AotAcquireSubject, Row> read)
        {
            read.From(AotPrivateSubjectRecord.Collection, "subjects", out BaseReadSource<AotPrivateSubjectRecord> subject)
                .Where(subject.RecordId.Equal(read.Parameter(Parameters.SubjectId)))
                .ProjectSubjectReference(Row.Fields.Reference, subject, AotSubject.HPDBaseSubjectRegistration);
        }
    }

    internal sealed class AotAllowPolicyEvaluator : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(
            PolicyEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = request;
            return ValueTask.FromResult(new PolicyDecision
            {
                Effect = PolicyEffect.Allow,
                Outcome = PolicyOutcome.Allowed,
                Audit = new PolicyAuditInfo
                {
                    MatchedGrantIds =
                    [
                        "hpd.base.aot.subject.private",
                        "hpd.base.aot.subject.acquire",
                        "hpd.base.aot.subject.validate",
                        "hpd.base.aot.subject.rotate",
                    ],
                },
            });
        }
    }
}
