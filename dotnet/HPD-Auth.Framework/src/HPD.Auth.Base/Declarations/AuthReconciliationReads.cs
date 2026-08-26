using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

[BaseRead("auth.read.tombstonedUsersForReconciliation.v1", typeof(AuthReconciliationReadJsonContext),
    RequiredGrantId = "auth.cleanup.execute", Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.tombstonedUsersForReconciliation.v1.row.tenantId"],
    SystemSourceIds = ["auth.users"])]
internal sealed partial record AuthTombstonedUsersForReconciliationReadV1
{
    [BaseReadParameter("auth.read.tombstonedUsersForReconciliation.v1.parameter.afterTenantId")] public Guid? AfterTenantId { get; init; }
    [BaseReadParameter("auth.read.tombstonedUsersForReconciliation.v1.parameter.afterSubjectKind")] public AuthCleanupSubjectKindV1? AfterSubjectKind { get; init; }
    [BaseReadParameter("auth.read.tombstonedUsersForReconciliation.v1.parameter.afterSubjectId")] public Guid? AfterSubjectId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.tombstonedUsersForReconciliation.v1.row.tenantId")] public required Guid TenantId { get; init; }
        [BaseReadField("auth.read.tombstonedUsersForReconciliation.v1.row.subjectId")] public required Guid SubjectId { get; init; }
        [BaseReadField("auth.read.tombstonedUsersForReconciliation.v1.row.privateRevision")] public required RevisionToken PrivateRevision { get; init; }
        [BaseReadField("auth.read.tombstonedUsersForReconciliation.v1.row.subject")] public required BaseSubjectReference<AuthUserSubject> Subject { get; init; }
        [BaseReadField("auth.read.tombstonedUsersForReconciliation.v1.row.tombstoneSequence")] public required long TombstoneSequence { get; init; }
        [BaseReadField("auth.read.tombstonedUsersForReconciliation.v1.row.tombstonedAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? TombstonedAt { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthTombstonedUsersForReconciliationReadV1, Row> read)
    {
        read.From(AuthUserRecordV1.Collection, "user", out BaseReadSource<AuthUserRecordV1> user);
        BaseReadOperand<Guid> tenant = read.OptionalParameter(Parameters.AfterTenantId);
        BaseReadOperand<AuthCleanupSubjectKindV1> kind = read.OptionalParameter(Parameters.AfterSubjectKind);
        BaseReadOperand<Guid> subject = read.OptionalParameter(Parameters.AfterSubjectId);
        BaseReadPredicate boundary = tenant.IsNull()
            .Or(user.Field(AuthUserRecordV1.Fields.TenantId).GreaterThan(tenant))
            .Or(user.Field(AuthUserRecordV1.Fields.TenantId).Equal(tenant)
                .And(kind.Equal(read.ClosedEnumLiteral(AuthCleanupSubjectKindV1.user)))
                .And(user.Field(AuthUserRecordV1.Fields.Id).GreaterThan(subject)));
        read.Where(user.Field(AuthUserRecordV1.Fields.IsDeleted).Equal(read.Literal(true)).And(boundary))
            .Project(Row.Fields.TenantId, user.Field(AuthUserRecordV1.Fields.TenantId))
            .Project(Row.Fields.SubjectId, user.Field(AuthUserRecordV1.Fields.Id))
            .Project(Row.Fields.PrivateRevision, user.Revision)
            .ProjectSubjectReference(Row.Fields.Subject, user, AuthUserSubject.HPDBaseSubjectRegistration)
            .Project(Row.Fields.TombstoneSequence, user.Field(AuthUserRecordV1.Fields.TombstoneGeneration))
            .Project(Row.Fields.TombstonedAt, user.Field(AuthUserRecordV1.Fields.DeletedAt))
            .OrderBy(user.Field(AuthUserRecordV1.Fields.TenantId))
            .OrderBy(user.Field(AuthUserRecordV1.Fields.Id))
            .Limits(200, 262_144, 12, 750);
    }
}

[BaseRead("auth.read.tombstonedRolesForReconciliation.v1", typeof(AuthReconciliationReadJsonContext),
    RequiredGrantId = "auth.cleanup.execute", Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.tombstonedRolesForReconciliation.v1.row.tenantId"],
    SystemSourceIds = ["auth.roles"])]
internal sealed partial record AuthTombstonedRolesForReconciliationReadV1
{
    [BaseReadParameter("auth.read.tombstonedRolesForReconciliation.v1.parameter.afterTenantId")] public Guid? AfterTenantId { get; init; }
    [BaseReadParameter("auth.read.tombstonedRolesForReconciliation.v1.parameter.afterSubjectKind")] public AuthCleanupSubjectKindV1? AfterSubjectKind { get; init; }
    [BaseReadParameter("auth.read.tombstonedRolesForReconciliation.v1.parameter.afterSubjectId")] public Guid? AfterSubjectId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.tombstonedRolesForReconciliation.v1.row.tenantId")] public required Guid TenantId { get; init; }
        [BaseReadField("auth.read.tombstonedRolesForReconciliation.v1.row.subjectId")] public required Guid SubjectId { get; init; }
        [BaseReadField("auth.read.tombstonedRolesForReconciliation.v1.row.privateRevision")] public required RevisionToken PrivateRevision { get; init; }
        [BaseReadField("auth.read.tombstonedRolesForReconciliation.v1.row.subject")] public required BaseSubjectReference<AuthRoleSubject> Subject { get; init; }
        [BaseReadField("auth.read.tombstonedRolesForReconciliation.v1.row.tombstoneSequence")] public required long TombstoneSequence { get; init; }
        [BaseReadField("auth.read.tombstonedRolesForReconciliation.v1.row.tombstonedAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? TombstonedAt { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthTombstonedRolesForReconciliationReadV1, Row> read)
    {
        read.From(AuthRoleRecordV1.Collection, "role", out BaseReadSource<AuthRoleRecordV1> role);
        BaseReadOperand<Guid> tenant = read.OptionalParameter(Parameters.AfterTenantId);
        BaseReadOperand<AuthCleanupSubjectKindV1> kind = read.OptionalParameter(Parameters.AfterSubjectKind);
        BaseReadOperand<Guid> subject = read.OptionalParameter(Parameters.AfterSubjectId);
        BaseReadPredicate sameTenantBoundary = kind.Equal(read.ClosedEnumLiteral(AuthCleanupSubjectKindV1.user))
            .Or(kind.Equal(read.ClosedEnumLiteral(AuthCleanupSubjectKindV1.role))
                .And(role.Field(AuthRoleRecordV1.Fields.Id).GreaterThan(subject)));
        BaseReadPredicate boundary = tenant.IsNull()
            .Or(role.Field(AuthRoleRecordV1.Fields.TenantId).GreaterThan(tenant))
            .Or(role.Field(AuthRoleRecordV1.Fields.TenantId).Equal(tenant)
                .And(sameTenantBoundary));
        read.Where(role.Field(AuthRoleRecordV1.Fields.IsDeleted).Equal(read.Literal(true)).And(boundary))
            .Project(Row.Fields.TenantId, role.Field(AuthRoleRecordV1.Fields.TenantId))
            .Project(Row.Fields.SubjectId, role.Field(AuthRoleRecordV1.Fields.Id))
            .Project(Row.Fields.PrivateRevision, role.Revision)
            .ProjectSubjectReference(Row.Fields.Subject, role, AuthRoleSubject.HPDBaseSubjectRegistration)
            .Project(Row.Fields.TombstoneSequence, role.Field(AuthRoleRecordV1.Fields.TombstoneGeneration))
            .Project(Row.Fields.TombstonedAt, role.Field(AuthRoleRecordV1.Fields.DeletedAt))
            .OrderBy(role.Field(AuthRoleRecordV1.Fields.TenantId))
            .OrderBy(role.Field(AuthRoleRecordV1.Fields.Id))
            .Limits(200, 262_144, 12, 750);
    }
}

[JsonSerializable(typeof(AuthTombstonedUsersForReconciliationReadV1), TypeInfoPropertyName = "AuthTombstonedUsersForReconciliationReadV1")]
[JsonSerializable(typeof(AuthTombstonedUsersForReconciliationReadV1.Row), TypeInfoPropertyName = "AuthTombstonedUsersForReconciliationReadV1Row")]
[JsonSerializable(typeof(AuthTombstonedRolesForReconciliationReadV1), TypeInfoPropertyName = "AuthTombstonedRolesForReconciliationReadV1")]
[JsonSerializable(typeof(AuthTombstonedRolesForReconciliationReadV1.Row), TypeInfoPropertyName = "AuthTombstonedRolesForReconciliationReadV1Row")]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal sealed partial class AuthReconciliationReadJsonContext : JsonSerializerContext;
