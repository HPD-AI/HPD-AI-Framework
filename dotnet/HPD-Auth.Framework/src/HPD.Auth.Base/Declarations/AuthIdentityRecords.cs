using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

[BaseCollection("auth.users", typeof(AuthBaseJsonSerializerContext), SystemOwnerModuleId = AuthBaseContract.ModuleId)]
[BaseIndex("auth.idx.users.tenantUserName", Unique = true)]
[BaseIndexPart("auth.idx.users.tenantUserName", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.users.tenantUserName", 1, nameof(NormalizedUserName))]
[BaseIndexPredicate("auth.idx.users.tenantUserName", "root", BaseIndexPredicateNodeKind.And, Children = ["defined", "not-null"])]
[BaseIndexPredicate("auth.idx.users.tenantUserName", "defined", BaseIndexPredicateNodeKind.IsDefined, Field = nameof(NormalizedUserName))]
[BaseIndexPredicate("auth.idx.users.tenantUserName", "not-null", BaseIndexPredicateNodeKind.Not, Children = ["null"])]
[BaseIndexPredicate("auth.idx.users.tenantUserName", "null", BaseIndexPredicateNodeKind.IsNull, Field = nameof(NormalizedUserName))]
[BaseIndex("auth.idx.users.tenantEmail", Unique = true)]
[BaseIndexPart("auth.idx.users.tenantEmail", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.users.tenantEmail", 1, nameof(NormalizedEmail))]
[BaseIndexPredicate("auth.idx.users.tenantEmail", "root", BaseIndexPredicateNodeKind.And, Children = ["defined", "not-null"])]
[BaseIndexPredicate("auth.idx.users.tenantEmail", "defined", BaseIndexPredicateNodeKind.IsDefined, Field = nameof(NormalizedEmail))]
[BaseIndexPredicate("auth.idx.users.tenantEmail", "not-null", BaseIndexPredicateNodeKind.Not, Children = ["null"])]
[BaseIndexPredicate("auth.idx.users.tenantEmail", "null", BaseIndexPredicateNodeKind.IsNull, Field = nameof(NormalizedEmail))]
internal sealed partial record AuthUserRecordV1
{
    [BaseField("auth.users.id", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required Guid Id { get; init; }
    [BaseField("auth.users.tenantId", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required Guid TenantId { get; init; }
    [BaseField("auth.users.userName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc, Operators = BaseFieldOperator.Text), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? UserName { get; init; }
    [BaseField("auth.users.normalizedUserName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc, Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? NormalizedUserName { get; init; }
    [BaseField("auth.users.email", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 320, StringNormalization = BaseStringNormalizationRequirement.RequireNfc, Operators = BaseFieldOperator.Text | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? Email { get; init; }
    [BaseField("auth.users.normalizedEmail", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 320, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? NormalizedEmail { get; init; }
    [BaseField("auth.users.emailConfirmed", Operators = BaseFieldOperator.Equal), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required bool EmailConfirmed { get; init; }
    [BaseField("auth.users.passwordHash", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 4096, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public string? PasswordHash { get; init; }
    [BaseField("auth.users.securityStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required string SecurityStamp { get; init; }
    [BaseField("auth.users.concurrencyStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string ConcurrencyStamp { get; init; }
    [BaseField("auth.users.phoneNumber", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 64, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? PhoneNumber { get; init; }
    [BaseField("auth.users.phoneNumberConfirmed"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required bool PhoneNumberConfirmed { get; init; }
    [BaseField("auth.users.twoFactorEnabled"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required bool TwoFactorEnabled { get; init; }
    [BaseField("auth.users.lockoutEnd", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LockoutEnd { get; init; }
    [BaseField("auth.users.lockoutEnabled"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required bool LockoutEnabled { get; init; }
    [BaseField("auth.users.accessFailedCount", MinimumInt32 = 0, HasMinimumInt32 = true, MaximumInt32 = 1_000_000, HasMaximumInt32 = true), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required int AccessFailedCount { get; init; }
    [BaseField("auth.users.authenticatorKey", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 2048, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public string? AuthenticatorKey { get; init; }
    [BaseField("auth.users.audience", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 50, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public string? Audience { get; init; }
    [BaseField("auth.users.userMetadata", MaximumCanonicalJsonBytes = 32768, JsonShape = BaseJsonShape.Object, MaximumJsonDepth = 16, MaximumJsonArrayItems = 1024, MaximumJsonObjectProperties = 1024, MaximumJsonTotalNodes = 4096, MaximumJsonTotalStringUtf8Bytes = 32768, MaximumJsonTotalNameUtf8Bytes = 32768), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required BaseCanonicalJson UserMetadata { get; init; }
    [BaseField("auth.users.appMetadata", MaximumCanonicalJsonBytes = 32768, JsonShape = BaseJsonShape.Object, MaximumJsonDepth = 16, MaximumJsonArrayItems = 1024, MaximumJsonObjectProperties = 1024, MaximumJsonTotalNodes = 4096, MaximumJsonTotalStringUtf8Bytes = 32768, MaximumJsonTotalNameUtf8Bytes = 32768), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required BaseCanonicalJson AppMetadata { get; init; }
    [BaseField("auth.users.requiredActions", MaximumCanonicalJsonBytes = 4096, JsonShape = BaseJsonShape.Array, MaximumJsonDepth = 2, MaximumJsonArrayItems = 32, MaximumJsonObjectProperties = 1, MaximumJsonTotalNodes = 33, MaximumJsonTotalStringUtf8Bytes = 4096, MaximumJsonTotalNameUtf8Bytes = 1), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required BaseCanonicalJson RequiredActions { get; init; }
    [BaseField("auth.users.firstName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 100, StringNormalization = BaseStringNormalizationRequirement.RequireNfc, Operators = BaseFieldOperator.Text), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? FirstName { get; init; }
    [BaseField("auth.users.lastName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 100, StringNormalization = BaseStringNormalizationRequirement.RequireNfc, Operators = BaseFieldOperator.Text), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? LastName { get; init; }
    [BaseField("auth.users.displayName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 500, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? DisplayName { get; init; }
    [BaseField("auth.users.avatarUrl", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 2048, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? AvatarUrl { get; init; }
    [BaseField("auth.users.isActive", Operators = BaseFieldOperator.Equal), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required bool IsActive { get; init; }
    [BaseField("auth.users.isDeleted", Operators = BaseFieldOperator.Equal), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required bool IsDeleted { get; init; }
    [BaseField("auth.users.deletedAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? DeletedAt { get; init; }
    [BaseField("auth.users.tombstoneGeneration", MinimumInt64 = 0, HasMinimumInt64 = true), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required long TombstoneGeneration { get; init; }
    [BaseField("auth.users.createdAt", Operators = BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
    [BaseField("auth.users.updatedAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset UpdatedAt { get; init; }
    [BaseField("auth.users.lastLoginAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, Operators = BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LastLoginAt { get; init; }
    [BaseField("auth.users.lastLoginIp", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 45, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? LastLoginIp { get; init; }
    [BaseField("auth.users.subscriptionTier", MaximumUtf8Bytes = 50, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string SubscriptionTier { get; init; }
    [BaseField("auth.users.emailConfirmedAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? EmailConfirmedAt { get; init; }
}

[BaseCollection("auth.roles", typeof(AuthBaseJsonSerializerContext), SystemOwnerModuleId = AuthBaseContract.ModuleId)]
[BaseIndex("auth.idx.roles.tenantName", Unique = true)]
[BaseIndexPart("auth.idx.roles.tenantName", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.roles.tenantName", 1, nameof(NormalizedName))]
[BaseIndexPredicate("auth.idx.roles.tenantName", "root", BaseIndexPredicateNodeKind.And, Children = ["defined", "not-null"])]
[BaseIndexPredicate("auth.idx.roles.tenantName", "defined", BaseIndexPredicateNodeKind.IsDefined, Field = nameof(NormalizedName))]
[BaseIndexPredicate("auth.idx.roles.tenantName", "not-null", BaseIndexPredicateNodeKind.Not, Children = ["null"])]
[BaseIndexPredicate("auth.idx.roles.tenantName", "null", BaseIndexPredicateNodeKind.IsNull, Field = nameof(NormalizedName))]
internal sealed partial record AuthRoleRecordV1
{
    [BaseField("auth.roles.id", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required Guid Id { get; init; }
    [BaseField("auth.roles.tenantId", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required Guid TenantId { get; init; }
    [BaseField("auth.roles.name", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public string? Name { get; init; }
    [BaseField("auth.roles.normalizedName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public string? NormalizedName { get; init; }
    [BaseField("auth.roles.concurrencyStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string ConcurrencyStamp { get; init; }
    [BaseField("auth.roles.description", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 500, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public string? Description { get; init; }
    [BaseField("auth.roles.isActive"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required bool IsActive { get; init; }
    [BaseField("auth.roles.isDeleted", Operators = BaseFieldOperator.Equal), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required bool IsDeleted { get; init; }
    [BaseField("auth.roles.deletedAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? DeletedAt { get; init; }
    [BaseField("auth.roles.tombstoneGeneration", MinimumInt64 = 0, HasMinimumInt64 = true), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required long TombstoneGeneration { get; init; }
    [BaseField("auth.roles.createdAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
    [BaseField("auth.roles.updatedAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Marks the exported logical identity of an HPD Auth user.</summary>
[BaseExportedSubject(
    "hpd.auth.user-subject",
    Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId,
    SubjectIdKind = BaseSubjectIdKind.Guid,
    MaximumSubjectIdUtf8Bytes = 36,
    PrivateRecordType = typeof(AuthUserRecordV1),
    AcquisitionGrantId = "auth.subject.user.acquire",
    ValidationGrantId = "auth.subject.user.validate",
    AdministrationGrantId = "auth.subject.user.admin",
    ValidationPlanId = "hpd.auth.user-subject.validation.v1",
    Scope = BaseSubjectScopeKind.Tenant,
    ScopeFieldId = "auth.users.tenantId",
    ActiveFieldId = "auth.users.isActive",
    TombstoneFieldId = "auth.users.isDeleted",
    TombstoneInstantFieldId = "auth.users.deletedAt",
    TombstoneSequenceFieldId = "auth.users.tombstoneGeneration",
    FinalRetirementExecutionMode = BaseSubjectFinalExecutionMode.ActivationGuardRequired,
    Audiences = [HPDBaseEndpointAudience.Application],
    SupportsCoordinatedRetirement = true)]
public sealed partial class AuthUserSubject;

/// <summary>Marks the exported logical identity of an HPD Auth role.</summary>
[BaseExportedSubject(
    "hpd.auth.role-subject",
    Version = 1,
    OwningModuleId = AuthBaseContract.ModuleId,
    SubjectIdKind = BaseSubjectIdKind.Guid,
    MaximumSubjectIdUtf8Bytes = 36,
    PrivateRecordType = typeof(AuthRoleRecordV1),
    AcquisitionGrantId = "auth.subject.role.acquire",
    ValidationGrantId = "auth.subject.role.validate",
    AdministrationGrantId = "auth.subject.role.admin",
    ValidationPlanId = "hpd.auth.role-subject.validation.v1",
    Scope = BaseSubjectScopeKind.Tenant,
    ScopeFieldId = "auth.roles.tenantId",
    ActiveFieldId = "auth.roles.isActive",
    TombstoneFieldId = "auth.roles.isDeleted",
    TombstoneInstantFieldId = "auth.roles.deletedAt",
    TombstoneSequenceFieldId = "auth.roles.tombstoneGeneration",
    FinalRetirementExecutionMode = BaseSubjectFinalExecutionMode.ActivationGuardRequired,
    Audiences = [HPDBaseEndpointAudience.Application],
    SupportsCoordinatedRetirement = true)]
public sealed partial class AuthRoleSubject;

internal static class AuthBaseContract
{
    internal const string ApplicationId = "hpd.auth.identity.v1";
    internal const string ModuleId = "hpd.auth";
}

[JsonSerializable(typeof(AuthUserRecordV1))]
[JsonSerializable(typeof(AuthRoleRecordV1))]
[JsonSerializable(typeof(AuthUserClaimRecordV1))]
[JsonSerializable(typeof(AuthRoleClaimRecordV1))]
[JsonSerializable(typeof(AuthUserRoleRecordV1))]
[JsonSerializable(typeof(AuthUserLoginRecordV1))]
[JsonSerializable(typeof(AuthUserTokenRecordV1))]
[JsonSerializable(typeof(AuthRecoveryCodeRecordV1))]
[JsonSerializable(typeof(AuthPasskeyRecordV1))]
[JsonSerializable(typeof(AuthRefreshTokenRecordV1))]
[JsonSerializable(typeof(AuthRefreshTokenDeliveryRecordV1))]
[JsonSerializable(typeof(AuthSessionRecordV1))]
[JsonSerializable(typeof(AuthSsoProviderRecordV1))]
[JsonSerializable(typeof(AuthUserIdentityRecordV1))]
[JsonSerializable(typeof(AuthTenantSettingsRecordV1))]
[JsonSerializable(typeof(AuthSecurityAuditRecordV1))]
[JsonSerializable(typeof(AuthDataProtectionKeyRecordV1))]
[JsonSerializable(typeof(AuthImportStateRecordV1))]
[JsonSerializable(typeof(AuthCleanupWorkRecordV1))]
[JsonSerializable(typeof(AuthMaintenanceCursorRecordV1))]
[JsonSerializable(typeof(AuthMaintenanceRunRecordV1))]
[JsonSerializable(typeof(AuthCreateUserV1))]
[JsonSerializable(typeof(AuthCreateUserResultV1))]
[JsonSerializable(typeof(AuthUpdateUserProfileV1))]
[JsonSerializable(typeof(AuthUpdateUserProfileResultV1))]
[JsonSerializable(typeof(AuthChangePasswordV1))]
[JsonSerializable(typeof(AuthRemovePasswordV1))]
[JsonSerializable(typeof(AuthResetPasswordV1))]
[JsonSerializable(typeof(AuthSetSecurityStateV1))]
[JsonSerializable(typeof(AuthSecurityMutationResultV1))]
[JsonSerializable(typeof(AuthRoleCreateV1))]
[JsonSerializable(typeof(AuthRoleCreateResultV1))]
[JsonSerializable(typeof(AuthRoleRenameV1))]
[JsonSerializable(typeof(AuthRoleMutationResultV1))]
[JsonSerializable(typeof(AuthMembershipAddV1))]
[JsonSerializable(typeof(AuthMembershipRemoveV1))]
[JsonSerializable(typeof(AuthMembershipAddResultV1))]
[JsonSerializable(typeof(AuthMembershipRemoveResultV1))]
[JsonSerializable(typeof(AuthLoginLinkV1))]
[JsonSerializable(typeof(AuthLoginUnlinkV1))]
[JsonSerializable(typeof(AuthLoginLinkResultV1))]
[JsonSerializable(typeof(AuthLoginUnlinkResultV1))]
[JsonSerializable(typeof(AuthAuditAppendV1))]
[JsonSerializable(typeof(AuthAuditAppendResultV1))]
[JsonSerializable(typeof(AuthPasskeyRegisterV1))]
[JsonSerializable(typeof(AuthPasskeyRegisterResultV1))]
[JsonSerializable(typeof(AuthPasskeyRemoveV1))]
[JsonSerializable(typeof(AuthPasskeyRemoveResultV1))]
[JsonSerializable(typeof(AuthPasskeyRecordAssertionV1))]
[JsonSerializable(typeof(AuthPasskeyAssertionResultV1))]
[JsonSerializable(typeof(AuthSessionCreateV1))]
[JsonSerializable(typeof(AuthSessionCreateResultV1))]
[JsonSerializable(typeof(AuthSessionTouchV1))]
[JsonSerializable(typeof(AuthSessionTouchResultV1))]
[JsonSerializable(typeof(AuthRefreshIssueV1))]
[JsonSerializable(typeof(AuthRefreshIssueResultV1))]
[JsonSerializable(typeof(AuthRefreshRotateV1))]
[JsonSerializable(typeof(AuthRefreshRotateResultV1))]
[JsonSerializable(typeof(AuthRecoveryCodeConsumeV1))]
[JsonSerializable(typeof(AuthRecoveryCodeMutationResultV1))]
[JsonSerializable(typeof(AuthRecoveryCodesReplaceV1))]
[JsonSerializable(typeof(AuthMaintenanceRunInitializeV1))]
[JsonSerializable(typeof(AuthMaintenanceRunResultV1))]
[JsonSerializable(typeof(AuthUserCleanupInitializeV1))]
[JsonSerializable(typeof(AuthRoleCleanupInitializeV1))]
[JsonSerializable(typeof(AuthCleanupInitializeResultV1))]
[JsonSerializable(typeof(AuthCleanupRetirementResultV1))]
[JsonSerializable(typeof(AuthCleanupAdvanceV1))]
[JsonSerializable(typeof(AuthCleanupPrepareRetirementV1))]
[JsonSerializable(typeof(AuthCleanupMutationResultV1))]
[JsonSerializable(typeof(AuthCleanupReconcileCursorV1))]
[JsonSerializable(typeof(AuthCleanupReconcileCursorResultV1))]
[JsonSerializable(typeof(AuthUserCleanupInputV1))]
[JsonSerializable(typeof(AuthRoleCleanupInputV1))]
[JsonSerializable(typeof(AuthCleanupResultV1))]
[JsonSerializable(typeof(AuthCleanupReconcileInputV1))]
[JsonSerializable(typeof(AuthCleanupReconcileResultV1))]
[JsonSerializable(typeof(AuthExpirationTriggerInputV1))]
[JsonSerializable(typeof(AuthExpirationResultV1))]
[JsonSerializable(typeof(AuthDataProtectionRefreshInputV1))]
[JsonSerializable(typeof(AuthDataProtectionRefreshResultV1))]
[JsonSerializable(typeof(AuthModuleMutationArtifact))]
[JsonSerializable(typeof(BaseActivationDefinition))]
[JsonSerializable(typeof(BaseModuleGenerationCellDefinition))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal sealed partial class AuthBaseJsonSerializerContext : JsonSerializerContext;
