using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

[BaseRead("auth.read.cleanupDependents.v1", typeof(AuthBaseOperationalReadJsonSerializerContext),
    RequiredGrantId = "auth.cleanup.execute",
    Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    SystemSourceIds = [
        "auth.userClaims", "auth.roleClaims", "auth.userRoles", "auth.userLogins",
        "auth.userTokens", "auth.recoveryCodes", "auth.passkeys", "auth.refreshTokens",
        "auth.refreshTokenDeliveries", "auth.sessions", "auth.userIdentities"])]
internal sealed partial record AuthCleanupDependentsReadV1
{
    [BaseReadParameter("auth.read.cleanupDependents.v1.parameter.tenantId")] public required Guid TenantId { get; init; }
    [BaseReadParameter("auth.read.cleanupDependents.v1.parameter.subjectKind")] public required AuthCleanupSubjectKindV1 SubjectKind { get; init; }
    [BaseReadParameter("auth.read.cleanupDependents.v1.parameter.subjectId")] public required Guid SubjectId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.cleanupDependents.v1.row.collectionId")] public required string CollectionId { get; init; }
        [BaseReadField("auth.read.cleanupDependents.v1.row.count")] public required long Count { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthCleanupDependentsReadV1, Row> read)
    {
        BaseReadOperand<Guid> tenant = read.Parameter(Parameters.TenantId);
        BaseReadOperand<AuthCleanupSubjectKindV1> kind = read.Parameter(Parameters.SubjectKind);

        read.CountBranch("auth.cleanupDependents.userClaims", Row.Fields.CollectionId, "userClaims", AuthUserClaimRecordV1.Collection, Row.Fields.Count,
                branch => branch.Where(branch.Field(AuthUserClaimRecordV1.Fields.TenantId).Equal(tenant)
                    .And(branch.Field(AuthUserClaimRecordV1.Fields.UserId).Equal(branch.RecordIdParameter<AuthUserRecordV1>(Parameters.SubjectId)))
                    .And(kind.Equal(branch.ClosedEnumLiteral(AuthCleanupSubjectKindV1.user)))))
            .CountBranch("auth.cleanupDependents.roleClaims", Row.Fields.CollectionId, "roleClaims", AuthRoleClaimRecordV1.Collection, Row.Fields.Count,
                branch => branch.Where(branch.Field(AuthRoleClaimRecordV1.Fields.TenantId).Equal(tenant)
                    .And(branch.Field(AuthRoleClaimRecordV1.Fields.RoleId).Equal(branch.RecordIdParameter<AuthRoleRecordV1>(Parameters.SubjectId)))
                    .And(kind.Equal(branch.ClosedEnumLiteral(AuthCleanupSubjectKindV1.role)))))
            .CountBranch("auth.cleanupDependents.userRolesByUser", Row.Fields.CollectionId, "membershipsByUser", AuthUserRoleRecordV1.Collection, Row.Fields.Count,
                branch => branch.Where(branch.Field(AuthUserRoleRecordV1.Fields.TenantId).Equal(tenant)
                    .And(branch.Field(AuthUserRoleRecordV1.Fields.UserId).Equal(branch.RecordIdParameter<AuthUserRecordV1>(Parameters.SubjectId)))
                    .And(kind.Equal(branch.ClosedEnumLiteral(AuthCleanupSubjectKindV1.user)))))
            .CountBranch("auth.cleanupDependents.userRolesByRole", Row.Fields.CollectionId, "membershipsByRole", AuthUserRoleRecordV1.Collection, Row.Fields.Count,
                branch => branch.Where(branch.Field(AuthUserRoleRecordV1.Fields.TenantId).Equal(tenant)
                    .And(branch.Field(AuthUserRoleRecordV1.Fields.RoleId).Equal(branch.RecordIdParameter<AuthRoleRecordV1>(Parameters.SubjectId)))
                    .And(kind.Equal(branch.ClosedEnumLiteral(AuthCleanupSubjectKindV1.role)))))
            .CountBranch("auth.cleanupDependents.userLogins", Row.Fields.CollectionId, "userLogins", AuthUserLoginRecordV1.Collection, Row.Fields.Count,
                branch => branch.Where(branch.Field(AuthUserLoginRecordV1.Fields.TenantId).Equal(tenant)
                    .And(branch.Field(AuthUserLoginRecordV1.Fields.UserId).Equal(branch.RecordIdParameter<AuthUserRecordV1>(Parameters.SubjectId)))
                    .And(kind.Equal(branch.ClosedEnumLiteral(AuthCleanupSubjectKindV1.user)))))
            .CountBranch("auth.cleanupDependents.userTokens", Row.Fields.CollectionId, "userTokens", AuthUserTokenRecordV1.Collection, Row.Fields.Count,
                branch => branch.Where(branch.Field(AuthUserTokenRecordV1.Fields.TenantId).Equal(tenant)
                    .And(branch.Field(AuthUserTokenRecordV1.Fields.UserId).Equal(branch.RecordIdParameter<AuthUserRecordV1>(Parameters.SubjectId)))
                    .And(kind.Equal(branch.ClosedEnumLiteral(AuthCleanupSubjectKindV1.user)))))
            .CountBranch("auth.cleanupDependents.recoveryCodes", Row.Fields.CollectionId, "recoveryCodes", AuthRecoveryCodeRecordV1.Collection, Row.Fields.Count,
                branch => branch.Where(branch.Field(AuthRecoveryCodeRecordV1.Fields.TenantId).Equal(tenant)
                    .And(branch.Field(AuthRecoveryCodeRecordV1.Fields.UserId).Equal(branch.RecordIdParameter<AuthUserRecordV1>(Parameters.SubjectId)))
                    .And(kind.Equal(branch.ClosedEnumLiteral(AuthCleanupSubjectKindV1.user)))))
            .CountBranch("auth.cleanupDependents.passkeys", Row.Fields.CollectionId, "passkeys", AuthPasskeyRecordV1.Collection, Row.Fields.Count,
                branch => branch.Where(branch.Field(AuthPasskeyRecordV1.Fields.TenantId).Equal(tenant)
                    .And(branch.Field(AuthPasskeyRecordV1.Fields.UserId).Equal(branch.RecordIdParameter<AuthUserRecordV1>(Parameters.SubjectId)))
                    .And(kind.Equal(branch.ClosedEnumLiteral(AuthCleanupSubjectKindV1.user)))))
            .CountBranch("auth.cleanupDependents.refreshTokens", Row.Fields.CollectionId, "refreshTokens", AuthRefreshTokenRecordV1.Collection, Row.Fields.Count,
                branch => branch.Where(branch.Field(AuthRefreshTokenRecordV1.Fields.TenantId).Equal(tenant)
                    .And(branch.Field(AuthRefreshTokenRecordV1.Fields.UserId).Equal(branch.RecordIdParameter<AuthUserRecordV1>(Parameters.SubjectId)))
                    .And(kind.Equal(branch.ClosedEnumLiteral(AuthCleanupSubjectKindV1.user)))))
            .CountBranch("auth.cleanupDependents.refreshTokenDeliveries", Row.Fields.CollectionId, "refreshTokenDeliveries", AuthRefreshTokenDeliveryRecordV1.Collection, Row.Fields.Count,
                branch => branch.Where(branch.Field(AuthRefreshTokenDeliveryRecordV1.Fields.TenantId).Equal(tenant)
                    .And(branch.Field(AuthRefreshTokenDeliveryRecordV1.Fields.UserId).Equal(branch.RecordIdParameter<AuthUserRecordV1>(Parameters.SubjectId)))
                    .And(kind.Equal(branch.ClosedEnumLiteral(AuthCleanupSubjectKindV1.user)))))
            .CountBranch("auth.cleanupDependents.sessions", Row.Fields.CollectionId, "sessions", AuthSessionRecordV1.Collection, Row.Fields.Count,
                branch => branch.Where(branch.Field(AuthSessionRecordV1.Fields.TenantId).Equal(tenant)
                    .And(branch.Field(AuthSessionRecordV1.Fields.UserId).Equal(branch.RecordIdParameter<AuthUserRecordV1>(Parameters.SubjectId)))
                    .And(kind.Equal(branch.ClosedEnumLiteral(AuthCleanupSubjectKindV1.user)))))
            .CountBranch("auth.cleanupDependents.userIdentities", Row.Fields.CollectionId, "userIdentities", AuthUserIdentityRecordV1.Collection, Row.Fields.Count,
                branch => branch.Where(branch.Field(AuthUserIdentityRecordV1.Fields.TenantId).Equal(tenant)
                    .And(branch.Field(AuthUserIdentityRecordV1.Fields.UserId).Equal(branch.RecordIdParameter<AuthUserRecordV1>(Parameters.SubjectId)))
                    .And(kind.Equal(branch.ClosedEnumLiteral(AuthCleanupSubjectKindV1.user)))))
            .CompoundLimits(16_384, 96, 1_000, 12, 96);
    }
}

[BaseRead("auth.read.cleanupWork.v1", typeof(AuthBaseOperationalReadJsonSerializerContext),
    RequiredGrantId = "auth.cleanup.execute",
    Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.cleanupWork.v1.row.tenantId", "auth.read.cleanupWork.v1.row.lastChildReceiptScope"],
    SystemSourceIds = ["auth.cleanupWork"])]
internal sealed partial record AuthCleanupWorkReadV1
{
    [BaseReadParameter("auth.read.cleanupWork.v1.parameter.tenantId")] public required Guid TenantId { get; init; }
    [BaseReadParameter("auth.read.cleanupWork.v1.parameter.subjectKind")] public required AuthCleanupSubjectKindV1 SubjectKind { get; init; }
    [BaseReadParameter("auth.read.cleanupWork.v1.parameter.subjectId")] public required Guid SubjectId { get; init; }
    [BaseReadParameter("auth.read.cleanupWork.v1.parameter.incarnation", MinimumBytes = 24, MaximumBytes = 24)] public required BaseBinary Incarnation { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.cleanupWork.v1.row.id")] public required string Id { get; init; }
        [BaseReadField("auth.read.cleanupWork.v1.row.revision")] public required RevisionToken Revision { get; init; }
        [BaseReadField("auth.read.cleanupWork.v1.row.tenantId")] public required Guid TenantId { get; init; }
        [BaseReadField("auth.read.cleanupWork.v1.row.subjectKind")] public required AuthCleanupSubjectKindV1 SubjectKind { get; init; }
        [BaseReadField("auth.read.cleanupWork.v1.row.subjectId")] public required Guid SubjectId { get; init; }
        [BaseReadField("auth.read.cleanupWork.v1.row.userSubject")] public BaseSubjectReference<AuthUserSubject>? UserSubject { get; init; }
        [BaseReadField("auth.read.cleanupWork.v1.row.roleSubject")] public BaseSubjectReference<AuthRoleSubject>? RoleSubject { get; init; }
        [BaseReadField("auth.read.cleanupWork.v1.row.incarnation", MinimumBytes = 24, MaximumBytes = 24)] public required BaseBinary Incarnation { get; init; }
        [BaseReadField("auth.read.cleanupWork.v1.row.tombstoneSequence")] public required long TombstoneSequence { get; init; }
        [BaseReadField("auth.read.cleanupWork.v1.row.tombstoneRevision")] public required string TombstoneRevision { get; init; }
        [BaseReadField("auth.read.cleanupWork.v1.row.workflowVersion")] public required int WorkflowVersion { get; init; }
        [BaseReadField("auth.read.cleanupWork.v1.row.step")] public required AuthCleanupStepV1 Step { get; init; }
        [BaseReadField("auth.read.cleanupWork.v1.row.chunkOrdinal")] public required long ChunkOrdinal { get; init; }
        [BaseReadField("auth.read.cleanupWork.v1.row.retentionEligibleAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? RetentionEligibleAt { get; init; }
        [BaseReadField("auth.read.cleanupWork.v1.row.completedSteps")] public required long CompletedSteps { get; init; }
        [BaseReadField("auth.read.cleanupWork.v1.row.lastChildReceiptScope")] public string? LastChildReceiptScope { get; init; }
        [BaseReadField("auth.read.cleanupWork.v1.row.state")] public required AuthCleanupStateV1 State { get; init; }
        [BaseReadField("auth.read.cleanupWork.v1.row.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
        [BaseReadField("auth.read.cleanupWork.v1.row.updatedAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset UpdatedAt { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthCleanupWorkReadV1, Row> read)
    {
        read.From(AuthCleanupWorkRecordV1.Collection, "work", out BaseReadSource<AuthCleanupWorkRecordV1> work)
            .Where(work.Field(AuthCleanupWorkRecordV1.Fields.TenantId).Equal(read.Parameter(Parameters.TenantId))
                .And(work.Field(AuthCleanupWorkRecordV1.Fields.SubjectKind).Equal(read.Parameter(Parameters.SubjectKind)))
                .And(work.Field(AuthCleanupWorkRecordV1.Fields.SubjectId).Equal(read.Parameter(Parameters.SubjectId)))
                .And(work.Field(AuthCleanupWorkRecordV1.Fields.Incarnation).Equal(read.Parameter(Parameters.Incarnation))))
            .Project(Row.Fields.Id, work.Field(AuthCleanupWorkRecordV1.Fields.Id))
            .Project(Row.Fields.Revision, work.Revision)
            .Project(Row.Fields.TenantId, work.Field(AuthCleanupWorkRecordV1.Fields.TenantId))
            .Project(Row.Fields.SubjectKind, work.Field(AuthCleanupWorkRecordV1.Fields.SubjectKind))
            .Project(Row.Fields.SubjectId, work.Field(AuthCleanupWorkRecordV1.Fields.SubjectId))
            .ProjectStoredSubjectReference(
                Row.Fields.UserSubject,
                work,
                AuthCleanupWorkRecordV1.Fields.UserSubject,
                AuthUserSubject.HPDBaseSubjectRegistration)
            .ProjectStoredSubjectReference(
                Row.Fields.RoleSubject,
                work,
                AuthCleanupWorkRecordV1.Fields.RoleSubject,
                AuthRoleSubject.HPDBaseSubjectRegistration)
            .Project(Row.Fields.Incarnation, work.Field(AuthCleanupWorkRecordV1.Fields.Incarnation))
            .Project(Row.Fields.TombstoneSequence, work.Field(AuthCleanupWorkRecordV1.Fields.TombstoneSequence))
            .Project(Row.Fields.TombstoneRevision, work.Field(AuthCleanupWorkRecordV1.Fields.TombstoneRevision))
            .Project(Row.Fields.WorkflowVersion, work.Field(AuthCleanupWorkRecordV1.Fields.WorkflowVersion))
            .Project(Row.Fields.Step, work.Field(AuthCleanupWorkRecordV1.Fields.Step))
            .Project(Row.Fields.ChunkOrdinal, work.Field(AuthCleanupWorkRecordV1.Fields.ChunkOrdinal))
            .Project(Row.Fields.RetentionEligibleAt, work.Field(AuthCleanupWorkRecordV1.Fields.RetentionEligibleAt))
            .Project(Row.Fields.CompletedSteps, work.Field(AuthCleanupWorkRecordV1.Fields.CompletedSteps))
            .Project(Row.Fields.LastChildReceiptScope, work.Field(AuthCleanupWorkRecordV1.Fields.LastChildReceiptScope))
            .Project(Row.Fields.State, work.Field(AuthCleanupWorkRecordV1.Fields.State))
            .Project(Row.Fields.CreatedAt, work.Field(AuthCleanupWorkRecordV1.Fields.CreatedAt))
            .Project(Row.Fields.UpdatedAt, work.Field(AuthCleanupWorkRecordV1.Fields.UpdatedAt))
            .OrderBy(work.Field(AuthCleanupWorkRecordV1.Fields.Id))
            .Limits(1, 32_768, 12, 250);
    }
}

[BaseRead("auth.read.maintenanceRun.v1", typeof(AuthBaseOperationalReadJsonSerializerContext),
    RequiredGrantId = "auth.cleanup.execute",
    Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.maintenanceRun.v1.row.activationId"],
    SystemSourceIds = ["auth.maintenanceRuns"])]
internal sealed partial record AuthMaintenanceRunReadV1
{
    [BaseReadParameter("auth.read.maintenanceRun.v1.parameter.activationId")] public required string ActivationId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.maintenanceRun.v1.row.id")] public required string Id { get; init; }
        [BaseReadField("auth.read.maintenanceRun.v1.row.activationId")] public required string ActivationId { get; init; }
        [BaseReadField("auth.read.maintenanceRun.v1.row.kind")] public required AuthMaintenanceKindV1 Kind { get; init; }
        [BaseReadField("auth.read.maintenanceRun.v1.row.cutoff"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset Cutoff { get; init; }
        [BaseReadField("auth.read.maintenanceRun.v1.row.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthMaintenanceRunReadV1, Row> read)
    {
        read.From(AuthMaintenanceRunRecordV1.Collection, "run", out BaseReadSource<AuthMaintenanceRunRecordV1> run)
            .Where(run.Field(AuthMaintenanceRunRecordV1.Fields.ActivationId).Equal(read.Parameter(Parameters.ActivationId)))
            .Project(Row.Fields.Id, run.Field(AuthMaintenanceRunRecordV1.Fields.Id))
            .Project(Row.Fields.ActivationId, run.Field(AuthMaintenanceRunRecordV1.Fields.ActivationId))
            .Project(Row.Fields.Kind, run.Field(AuthMaintenanceRunRecordV1.Fields.Kind))
            .Project(Row.Fields.Cutoff, run.Field(AuthMaintenanceRunRecordV1.Fields.Cutoff))
            .Project(Row.Fields.CreatedAt, run.Field(AuthMaintenanceRunRecordV1.Fields.CreatedAt))
            .OrderBy(run.Field(AuthMaintenanceRunRecordV1.Fields.Id))
            .Limits(1, 8_192, 4, 250);
    }
}

[JsonSerializable(typeof(AuthCleanupWorkReadV1), TypeInfoPropertyName = "AuthCleanupWorkReadV1")]
[JsonSerializable(typeof(AuthCleanupWorkReadV1.Row), TypeInfoPropertyName = "AuthCleanupWorkReadV1Row")]
[JsonSerializable(typeof(AuthMaintenanceRunReadV1), TypeInfoPropertyName = "AuthMaintenanceRunReadV1")]
[JsonSerializable(typeof(AuthMaintenanceRunReadV1.Row), TypeInfoPropertyName = "AuthMaintenanceRunReadV1Row")]
[JsonSerializable(typeof(AuthCleanupDependentsReadV1), TypeInfoPropertyName = "AuthCleanupDependentsReadV1")]
[JsonSerializable(typeof(AuthCleanupDependentsReadV1.Row), TypeInfoPropertyName = "AuthCleanupDependentsReadV1Row")]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal sealed partial class AuthBaseOperationalReadJsonSerializerContext : JsonSerializerContext;
