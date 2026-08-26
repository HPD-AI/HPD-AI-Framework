using System.Text.Json.Serialization;
using HPD.Auth.Base;
using HPD.Base;

_ = CustomerProfile.Collection.Definition;
_ = typeof(AuthUserSubject);
_ = typeof(AuthRoleSubject);
_ = (Func<BaseSession, BaseExportedSubjectContract<AuthUserSubject>>)AuthSubjects.Users;

_ = ConsumerStoredSubjectRead.Definition;

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
    ActiveFieldId = "active", TombstoneFieldId = "tombstoned", ScopeFieldId = "tenant")]
internal sealed partial class ConsumerSubject;

[BaseCollection("consumer.storedSubjects", typeof(ConsumerJsonSerializerContext))]
internal sealed partial record ConsumerStoredSubject
{
    [BaseField("reference")]
    [BaseSubjectReference(typeof(ConsumerSubject), Requirement = BaseSubjectReferenceRequirement.Exists)]
    public required BaseSubjectReference<ConsumerSubject> Reference { get; init; }
}

[BaseRead("consumer.stored-subject", typeof(ConsumerJsonSerializerContext), RequiredGrantId = "consumer.subject.read")]
internal sealed partial record ConsumerStoredSubjectRead
{
    public sealed partial record Row
    {
        [BaseReadField("consumer.stored-subject.row.reference")]
        public required BaseSubjectReference<ConsumerSubject> Reference { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<ConsumerStoredSubjectRead, Row> read)
    {
        read.From(ConsumerStoredSubject.Collection, "stored", out BaseReadSource<ConsumerStoredSubject> stored)
            .ProjectStoredSubjectReference(
                Row.Fields.Reference,
                stored,
                ConsumerStoredSubject.Fields.Reference,
                ConsumerSubject.HPDBaseSubjectRegistration);
    }
}

[JsonSerializable(typeof(CustomerProfile))]
[JsonSerializable(typeof(ConsumerPrivateSubject))]
[JsonSerializable(typeof(ConsumerStoredSubject))]
[JsonSerializable(typeof(ConsumerStoredSubjectRead))]
[JsonSerializable(typeof(ConsumerStoredSubjectRead.Row), TypeInfoPropertyName = "ConsumerStoredSubjectReadRow")]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal sealed partial class ConsumerJsonSerializerContext : JsonSerializerContext;
