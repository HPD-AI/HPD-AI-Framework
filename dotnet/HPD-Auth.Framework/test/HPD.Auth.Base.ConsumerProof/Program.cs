using System.Text.Json.Serialization;
using HPD.Auth.Base;
using HPD.Base;

_ = CustomerProfile.Collection.Definition;
_ = typeof(AuthUserSubject);
_ = typeof(AuthRoleSubject);
_ = (Func<BaseSession, BaseExportedSubjectContract<AuthUserSubject>>)AuthSubjects.Users;

_ = ConsumerStoredSubjectRead.Definition;

await HPD.Auth.Base.ConsumerProof.ProofHost.RunAsync(args.Contains("--sqlite", StringComparer.Ordinal));
if (args.Contains("--print-manifest-identities", StringComparer.Ordinal))
{
    Console.WriteLine("identityAndGeneration\t" + BaseGeneratedGraphEvidence.ModuleMutation(
        HPD.Auth.Base.ConsumerProof.IdentityAndGenerationProof.Identity));
    Console.WriteLine("requestControl\t" + BaseGeneratedGraphEvidence.ModuleMutation(
        HPD.Auth.Base.ConsumerProof.RequestControlProof.Identity));
    Console.WriteLine("staticSet\t" + BaseGeneratedGraphEvidence.ModuleMutation(
        HPD.Auth.Base.ConsumerProof.StaticSetProof.Identity));
    Console.WriteLine("presenceAndRemoval\t" + BaseGeneratedGraphEvidence.ModuleMutation(
        HPD.Auth.Base.ConsumerProof.PresenceAndRemovalProof.Identity));
    Console.WriteLine("semanticEnsure\t" + BaseGeneratedGraphEvidence.ModuleMutation(
        HPD.Auth.Base.ConsumerProof.SemanticEnsureProof.Identity));
    Console.WriteLine("semanticRetire\t" + BaseGeneratedGraphEvidence.ModuleMutation(
        HPD.Auth.Base.ConsumerProof.SemanticRetireProof.Identity));
    Console.WriteLine("selection\t" + BaseGeneratedGraphEvidence.SelectionProfile(
        HPD.Auth.Base.ConsumerProof.SelectionProof.Identity));
    Console.WriteLine("lifecycle\t" + BaseGeneratedGraphEvidence.LifecycleConsumer(
        HPD.Auth.Base.ConsumerProof.LifecycleProof.LifecycleIdentity));
    Console.WriteLine("retirement\t" + BaseGeneratedGraphEvidence.RetirementConsumer(
        HPD.Auth.Base.ConsumerProof.LifecycleProof.RetirementIdentity));
    Console.WriteLine("activation\t" + Convert.ToHexStringLower(
        HPD.Auth.Base.ConsumerProof.ProofActivation.Registration.Identity.Checksum.Span));
    Console.WriteLine("yieldActivation\t" + Convert.ToHexStringLower(
        HPD.Auth.Base.ConsumerProof.ProofYieldActivation.Registration.Identity.Checksum.Span));
    Console.WriteLine("schedule\t" + Convert.ToHexStringLower(
        HPD.Auth.Base.ConsumerProof.ProofActivation.Schedule.Definition.Checksum.AsSpan()));
    Console.WriteLine("jsonRead\t" + BaseGeneratedGraphEvidence.RegisteredRead(
        HPD.Auth.Base.ConsumerProof.ProofJsonRead.Handle));
    Console.WriteLine("countRead\t" + BaseGeneratedGraphEvidence.RegisteredRead(
        HPD.Auth.Base.ConsumerProof.ProofCountSummary.Handle));
}
HPD.Auth.Base.ConsumerProof.ProofManifest.Verify();

[BaseCollection("consumer.customerProfiles", typeof(ConsumerJsonSerializerContext))]
internal sealed partial record CustomerProfile
{
    [BaseField("id")]
    public required Guid Id { get; init; }

    [BaseField("tenantId")]
    public required Guid TenantId { get; init; }

    [BaseField("user")]
    [BaseSubjectReference(
        typeof(AuthUserSubject),
        Requirement = BaseSubjectReferenceRequirement.Active,
        Guarantee = BaseSubjectValidationGuarantee.TransactionSnapshot)]
    public required BaseSubjectReference<AuthUserSubject> User { get; init; }
}

[BaseCollection("consumer.privateSubjects", typeof(ConsumerJsonSerializerContext), SystemOwnerModuleId = "consumer.module")]
internal sealed partial record ConsumerPrivateSubject
{
    [BaseField("active")] public required bool Active { get; init; }
    [BaseField("tombstoned")] public required bool Tombstoned { get; init; }
    [BaseField("tenant")] public required string Tenant { get; init; }
}

[BaseExportedSubject("consumer.subject", OwningModuleId = "consumer.module",
    PrivateRecordType = typeof(ConsumerPrivateSubject), AcquisitionGrantId = "consumer.subject.acquire",
    ValidationGrantId = "consumer.subject.validate", AdministrationGrantId = "consumer.subject.admin",
    ValidationPlanId = "consumer.subject.validate.v1", Scope = BaseSubjectScopeKind.Tenant,
    ActiveFieldId = "active", TombstoneFieldId = "tombstoned", ScopeFieldId = "tenant",
    SupportsCoordinatedRetirement = true)]
internal sealed partial class ConsumerSubject;

[BaseCollection("consumer.otherPrivateSubjects", typeof(ConsumerJsonSerializerContext), SystemOwnerModuleId = "consumer.module")]
internal sealed partial record ConsumerOtherPrivateSubject
{
    [BaseField("other.active")] public required bool Active { get; init; }
    [BaseField("other.tombstoned")] public required bool Tombstoned { get; init; }
    [BaseField("other.tenant")] public required string Tenant { get; init; }
}

[BaseExportedSubject("consumer.other-subject", OwningModuleId = "consumer.module",
    PrivateRecordType = typeof(ConsumerOtherPrivateSubject), AcquisitionGrantId = "consumer.other-subject.acquire",
    ValidationGrantId = "consumer.other-subject.validate", AdministrationGrantId = "consumer.other-subject.admin",
    ValidationPlanId = "consumer.other-subject.validate.v1", Scope = BaseSubjectScopeKind.Tenant,
    ActiveFieldId = "other.active", TombstoneFieldId = "other.tombstoned", ScopeFieldId = "other.tenant")]
internal sealed partial class ConsumerOtherSubject;

[BaseRead("consumer.other-subject.acquire", typeof(ConsumerJsonSerializerContext),
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    RequiredGrantId = "consumer.other-subject.acquire",
    SystemSourceIds = ["consumer.otherPrivateSubjects"])]
internal sealed partial record ConsumerOtherSubjectAcquire
{
    [BaseReadParameter("consumer.other-subject.acquire.id")]
    public required BaseRecordId<ConsumerOtherPrivateSubject> SubjectId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("consumer.other-subject.acquire.reference")]
        public required BaseSubjectReference<ConsumerOtherSubject> Reference { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<ConsumerOtherSubjectAcquire, Row> read)
    {
        read.From(ConsumerOtherPrivateSubject.Collection, "subject", out BaseReadSource<ConsumerOtherPrivateSubject> subject)
            .Where(subject.RecordId.Equal(read.Parameter(Parameters.SubjectId)))
            .ProjectSubjectReference(Row.Fields.Reference, subject, ConsumerOtherSubject.HPDBaseSubjectRegistration);
    }
}

[BaseRead("consumer.subject.acquire", typeof(ConsumerJsonSerializerContext),
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    RequiredGrantId = "consumer.subject.acquire",
    SystemSourceIds = ["consumer.privateSubjects"])]
internal sealed partial record ConsumerSubjectAcquire
{
    [BaseReadParameter("consumer.subject.acquire.id")]
    public required BaseRecordId<ConsumerPrivateSubject> SubjectId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("consumer.subject.acquire.reference")]
        public required BaseSubjectReference<ConsumerSubject> Reference { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<ConsumerSubjectAcquire, Row> read)
    {
        read.From(ConsumerPrivateSubject.Collection, "subject", out BaseReadSource<ConsumerPrivateSubject> subject)
            .Where(subject.RecordId.Equal(read.Parameter(Parameters.SubjectId)))
            .ProjectSubjectReference(Row.Fields.Reference, subject, ConsumerSubject.HPDBaseSubjectRegistration);
    }
}

[BaseCollection("consumer.storedSubjects", typeof(ConsumerJsonSerializerContext))]
internal sealed partial record ConsumerStoredSubject
{
    [BaseField("reference")]
    [BaseSubjectReference(typeof(ConsumerSubject), Requirement = BaseSubjectReferenceRequirement.Exists)]
    public required BaseSubjectReference<ConsumerSubject> Reference { get; init; }

    [BaseField("otherReference")]
    [BaseSubjectReference(typeof(ConsumerOtherSubject), Requirement = BaseSubjectReferenceRequirement.Exists)]
    public required BaseSubjectReference<ConsumerOtherSubject> OtherReference { get; init; }
}

[BaseRead("consumer.stored-subject", typeof(ConsumerJsonSerializerContext), RequiredGrantId = "consumer.subject.read")]
internal sealed partial record ConsumerStoredSubjectRead
{
    public sealed partial record Row
    {
        [BaseReadField("consumer.stored-subject.row.reference")]
        public required BaseSubjectReference<ConsumerSubject> Reference { get; init; }

        [BaseReadField("consumer.stored-subject.row.other-reference")]
        public required BaseSubjectReference<ConsumerOtherSubject> OtherReference { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<ConsumerStoredSubjectRead, Row> read)
    {
        read.From(ConsumerStoredSubject.Collection, "stored", out BaseReadSource<ConsumerStoredSubject> stored)
            .ProjectStoredSubjectReference(
                Row.Fields.Reference,
                stored,
                ConsumerStoredSubject.Fields.Reference,
                ConsumerSubject.HPDBaseSubjectRegistration)
            .ProjectStoredSubjectReference(
                Row.Fields.OtherReference,
                stored,
                ConsumerStoredSubject.Fields.OtherReference,
                ConsumerOtherSubject.HPDBaseSubjectRegistration);
    }
}

[JsonSerializable(typeof(CustomerProfile))]
[JsonSerializable(typeof(ConsumerPrivateSubject))]
[JsonSerializable(typeof(ConsumerOtherPrivateSubject))]
[JsonSerializable(typeof(ConsumerStoredSubject))]
[JsonSerializable(typeof(ConsumerStoredSubjectRead))]
[JsonSerializable(typeof(ConsumerStoredSubjectRead.Row), TypeInfoPropertyName = "ConsumerStoredSubjectReadRow")]
[JsonSerializable(typeof(ConsumerSubjectAcquire))]
[JsonSerializable(typeof(ConsumerSubjectAcquire.Row), TypeInfoPropertyName = "ConsumerSubjectAcquireRow")]
[JsonSerializable(typeof(ConsumerOtherSubjectAcquire))]
[JsonSerializable(typeof(ConsumerOtherSubjectAcquire.Row), TypeInfoPropertyName = "ConsumerOtherSubjectAcquireRow")]
[JsonSerializable(typeof(HPD.Auth.Base.ConsumerProof.IdentityAndGenerationRequest))]
[JsonSerializable(typeof(HPD.Auth.Base.ConsumerProof.IdentityAndGenerationResult))]
[JsonSerializable(typeof(HPD.Auth.Base.ConsumerProof.RequestControlRequest))]
[JsonSerializable(typeof(HPD.Auth.Base.ConsumerProof.RequestControlResult))]
[JsonSerializable(typeof(HPD.Auth.Base.ConsumerProof.StaticSetRequest))]
[JsonSerializable(typeof(HPD.Auth.Base.ConsumerProof.StaticSetResult))]
[JsonSerializable(typeof(HPD.Auth.Base.ConsumerProof.PresenceAndRemovalRequest))]
[JsonSerializable(typeof(HPD.Auth.Base.ConsumerProof.PresenceAndRemovalResult))]
[JsonSerializable(typeof(HPD.Auth.Base.ConsumerProof.ProofWorkItem))]
[JsonSerializable(typeof(HPD.Auth.Base.ConsumerProof.ProofOwnerPatch))]
[JsonSerializable(typeof(HPD.Auth.Base.ConsumerProof.ProofSelectionItem))]
[JsonSerializable(typeof(HPD.Auth.Base.ConsumerProof.SemanticProofRequest))]
[JsonSerializable(typeof(HPD.Auth.Base.ConsumerProof.SemanticEnsureProofResult))]
[JsonSerializable(typeof(HPD.Auth.Base.ConsumerProof.SemanticRetireProofResult))]
[JsonSerializable(typeof(HPD.Auth.Base.ConsumerProof.ProofActivationInput))]
[JsonSerializable(typeof(HPD.Auth.Base.ConsumerProof.ProofActivationResult))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal sealed partial class ConsumerJsonSerializerContext : JsonSerializerContext;
