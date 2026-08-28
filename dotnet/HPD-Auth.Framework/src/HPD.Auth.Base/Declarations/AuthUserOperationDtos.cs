using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

internal sealed record AuthCreateUserV1
{
    [BaseField("auth.operation.user.create.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.user.create.userId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid UserId { get; init; }
    [BaseField("auth.operation.user.create.userName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? UserName { get; init; }
    [BaseField("auth.operation.user.create.normalizedUserName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? NormalizedUserName { get; init; }
    [BaseField("auth.operation.user.create.email", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 320, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? Email { get; init; }
    [BaseField("auth.operation.user.create.normalizedEmail", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 320, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? NormalizedEmail { get; init; }
    [BaseField("auth.operation.user.create.passwordHash", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 4096, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public string? PasswordHash { get; init; }
    [BaseField("auth.operation.user.create.securityStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required string SecurityStamp { get; init; }
    [BaseField("auth.operation.user.create.concurrencyStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string ConcurrencyStamp { get; init; }
    [BaseField("auth.operation.user.create.lockoutEnabled")] public required bool LockoutEnabled { get; init; }
    [BaseField("auth.operation.user.create.emailConfirmed")] public required bool EmailConfirmed { get; init; }
    [BaseField("auth.operation.user.create.emailConfirmedAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? EmailConfirmedAt { get; init; }
    [BaseField("auth.operation.user.create.phoneNumber", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 64, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? PhoneNumber { get; init; }
    [BaseField("auth.operation.user.create.phoneNumberConfirmed")] public required bool PhoneNumberConfirmed { get; init; }
    [BaseField("auth.operation.user.create.twoFactorEnabled")] public required bool TwoFactorEnabled { get; init; }
    [BaseField("auth.operation.user.create.lockoutEnd", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LockoutEnd { get; init; }
    [BaseField("auth.operation.user.create.accessFailedCount", HasMinimumInt32 = true, MinimumInt32 = 0, HasMaximumInt32 = true, MaximumInt32 = 1_000_000)] public required int AccessFailedCount { get; init; }
    [BaseField("auth.operation.user.create.audience", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 50, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? Audience { get; init; }
    [BaseField("auth.operation.user.create.userMetadata", MaximumCanonicalJsonBytes = 32768, JsonShape = BaseJsonShape.Object, MaximumJsonDepth = 16, MaximumJsonArrayItems = 1024, MaximumJsonObjectProperties = 1024, MaximumJsonTotalNodes = 4096, MaximumJsonTotalStringUtf8Bytes = 32768, MaximumJsonTotalNameUtf8Bytes = 32768)] public required BaseCanonicalJson UserMetadata { get; init; }
    [BaseField("auth.operation.user.create.appMetadata", MaximumCanonicalJsonBytes = 32768, JsonShape = BaseJsonShape.Object, MaximumJsonDepth = 16, MaximumJsonArrayItems = 1024, MaximumJsonObjectProperties = 1024, MaximumJsonTotalNodes = 4096, MaximumJsonTotalStringUtf8Bytes = 32768, MaximumJsonTotalNameUtf8Bytes = 32768), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required BaseCanonicalJson AppMetadata { get; init; }
    [BaseField("auth.operation.user.create.requiredActions", MaximumCanonicalJsonBytes = 4096, JsonShape = BaseJsonShape.Array, MaximumJsonDepth = 2, MaximumJsonArrayItems = 32, MaximumJsonObjectProperties = 1, MaximumJsonTotalNodes = 33, MaximumJsonTotalStringUtf8Bytes = 4096, MaximumJsonTotalNameUtf8Bytes = 1)] public required BaseCanonicalJson RequiredActions { get; init; }
    [BaseField("auth.operation.user.create.firstName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 100, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? FirstName { get; init; }
    [BaseField("auth.operation.user.create.lastName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 100, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? LastName { get; init; }
    [BaseField("auth.operation.user.create.displayName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 500, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? DisplayName { get; init; }
    [BaseField("auth.operation.user.create.avatarUrl", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 2048, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? AvatarUrl { get; init; }
    [BaseField("auth.operation.user.create.isActive")] public required bool IsActive { get; init; }
    [BaseField("auth.operation.user.create.lastLoginAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LastLoginAt { get; init; }
    [BaseField("auth.operation.user.create.lastLoginIp", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 45, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? LastLoginIp { get; init; }
    [BaseField("auth.operation.user.create.subscriptionTier", MaximumUtf8Bytes = 50, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string SubscriptionTier { get; init; }
    [BaseField("auth.operation.user.create.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthCreateUserResultV1
{
    [BaseField("auth.operation.user.create.result.userId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid UserId { get; init; }
    [BaseField("auth.operation.user.create.result.revision")] public required RevisionToken Revision { get; init; }
    [BaseField("auth.operation.user.create.result.userGeneration")] public required BaseModuleGeneration UserGeneration { get; init; }
    [BaseField("auth.operation.user.create.result.securityGeneration")] public required BaseModuleGeneration SecurityGeneration { get; init; }
}

internal sealed record AuthUpdateUserProfileV1
{
    [BaseField("auth.operation.user.profile.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.user.profile.userId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid UserId { get; init; }
    [BaseField("auth.operation.user.profile.expectedRevision")] public required RevisionToken ExpectedRevision { get; init; }
    [BaseField("auth.operation.user.profile.appMetadata", MaximumCanonicalJsonBytes = 32768, JsonShape = BaseJsonShape.Object, MaximumJsonDepth = 16, MaximumJsonArrayItems = 1024, MaximumJsonObjectProperties = 1024, MaximumJsonTotalNodes = 4096, MaximumJsonTotalStringUtf8Bytes = 32768, MaximumJsonTotalNameUtf8Bytes = 32768)] public required BaseCanonicalJson AppMetadata { get; init; }
    [BaseField("auth.operation.user.profile.audience", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 50, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? Audience { get; init; }
    [BaseField("auth.operation.user.profile.userName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? UserName { get; init; }
    [BaseField("auth.operation.user.profile.normalizedUserName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? NormalizedUserName { get; init; }
    [BaseField("auth.operation.user.profile.email", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 320, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? Email { get; init; }
    [BaseField("auth.operation.user.profile.normalizedEmail", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 320, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? NormalizedEmail { get; init; }
    [BaseField("auth.operation.user.profile.emailConfirmed")] public required bool EmailConfirmed { get; init; }
    [BaseField("auth.operation.user.profile.emailConfirmedAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? EmailConfirmedAt { get; init; }
    [BaseField("auth.operation.user.profile.phoneNumber", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 64, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? PhoneNumber { get; init; }
    [BaseField("auth.operation.user.profile.phoneNumberConfirmed")] public required bool PhoneNumberConfirmed { get; init; }
    [BaseField("auth.operation.user.profile.displayName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 500, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? DisplayName { get; init; }
    [BaseField("auth.operation.user.profile.firstName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 100, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? FirstName { get; init; }
    [BaseField("auth.operation.user.profile.lastName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 100, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? LastName { get; init; }
    [BaseField("auth.operation.user.profile.avatarUrl", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 2048, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? AvatarUrl { get; init; }
    [BaseField("auth.operation.user.profile.isActive")] public required bool IsActive { get; init; }
    [BaseField("auth.operation.user.profile.userMetadata", MaximumCanonicalJsonBytes = 32768, JsonShape = BaseJsonShape.Object, MaximumJsonDepth = 16, MaximumJsonArrayItems = 1024, MaximumJsonObjectProperties = 1024, MaximumJsonTotalNodes = 4096, MaximumJsonTotalStringUtf8Bytes = 32768, MaximumJsonTotalNameUtf8Bytes = 32768)] public required BaseCanonicalJson UserMetadata { get; init; }
    [BaseField("auth.operation.user.profile.lastLoginAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LastLoginAt { get; init; }
    [BaseField("auth.operation.user.profile.lastLoginIp", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 45, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? LastLoginIp { get; init; }
    [BaseField("auth.operation.user.profile.requiredActions", MaximumCanonicalJsonBytes = 4096, JsonShape = BaseJsonShape.Array, MaximumJsonDepth = 2, MaximumJsonArrayItems = 32, MaximumJsonObjectProperties = 1, MaximumJsonTotalNodes = 33, MaximumJsonTotalStringUtf8Bytes = 4096, MaximumJsonTotalNameUtf8Bytes = 1)] public required BaseCanonicalJson RequiredActions { get; init; }
    [BaseField("auth.operation.user.profile.subscriptionTier", MaximumUtf8Bytes = 50, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string SubscriptionTier { get; init; }
    [BaseField("auth.operation.user.profile.concurrencyStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string ConcurrencyStamp { get; init; }
    [BaseField("auth.operation.user.profile.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthUpdateUserProfileResultV1
{
    [BaseField("auth.operation.user.profile.result.revision")] public required RevisionToken Revision { get; init; }
    [BaseField("auth.operation.user.profile.result.userGeneration")] public required BaseModuleGeneration UserGeneration { get; init; }
}

internal sealed record AuthChangePasswordV1
{
    [BaseField("auth.operation.user.password.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.user.password.userId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid UserId { get; init; }
    [BaseField("auth.operation.user.password.expectedRevision")] public required RevisionToken ExpectedRevision { get; init; }
    [BaseField("auth.operation.user.password.passwordHash", MaximumUtf8Bytes = 4096, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required string PasswordHash { get; init; }
    [BaseField("auth.operation.user.password.securityStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required string SecurityStamp { get; init; }
    [BaseField("auth.operation.user.password.concurrencyStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string ConcurrencyStamp { get; init; }
    [BaseField("auth.operation.user.password.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthRemovePasswordV1
{
    [BaseField("auth.operation.user.password.remove.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.user.password.remove.userId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid UserId { get; init; }
    [BaseField("auth.operation.user.password.remove.expectedRevision")] public required RevisionToken ExpectedRevision { get; init; }
    [BaseField("auth.operation.user.password.remove.securityStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required string SecurityStamp { get; init; }
    [BaseField("auth.operation.user.password.remove.concurrencyStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string ConcurrencyStamp { get; init; }
    [BaseField("auth.operation.user.password.remove.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthResetPasswordV1
{
    [BaseField("auth.operation.user.password.reset.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.user.password.reset.userId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid UserId { get; init; }
    [BaseField("auth.operation.user.password.reset.expectedRevision")] public required RevisionToken ExpectedRevision { get; init; }
    [BaseField("auth.operation.user.password.reset.passwordHash", MaximumUtf8Bytes = 4096, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required string PasswordHash { get; init; }
    [BaseField("auth.operation.user.password.reset.securityStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required string SecurityStamp { get; init; }
    [BaseField("auth.operation.user.password.reset.concurrencyStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string ConcurrencyStamp { get; init; }
    [BaseField("auth.operation.user.password.reset.lockoutEnabled")] public required bool LockoutEnabled { get; init; }
    [BaseField("auth.operation.user.password.reset.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthSetSecurityStateV1
{
    [BaseField("auth.operation.user.security.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.user.security.userId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid UserId { get; init; }
    [BaseField("auth.operation.user.security.expectedRevision")] public required RevisionToken ExpectedRevision { get; init; }
    [BaseField("auth.operation.user.security.twoFactorEnabled")] public required bool TwoFactorEnabled { get; init; }
    [BaseField("auth.operation.user.security.authenticatorKey", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 2048, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public string? AuthenticatorKey { get; init; }
    [BaseField("auth.operation.user.security.clearLockoutEnd")] public required bool ClearLockoutEnd { get; init; }
    [BaseField("auth.operation.user.security.lockoutEnd", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LockoutEnd { get; init; }
    [BaseField("auth.operation.user.security.lockoutEnabled")] public required bool LockoutEnabled { get; init; }
    [BaseField("auth.operation.user.security.accessFailedCount", MinimumInt32 = 0, HasMinimumInt32 = true, MaximumInt32 = 1_000_000, HasMaximumInt32 = true)] public required int AccessFailedCount { get; init; }
    [BaseField("auth.operation.user.security.securityStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required string SecurityStamp { get; init; }
    [BaseField("auth.operation.user.security.concurrencyStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string ConcurrencyStamp { get; init; }
    [BaseField("auth.operation.user.security.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthSecurityMutationResultV1
{
    [BaseField("auth.operation.user.security.result.revision")] public required RevisionToken Revision { get; init; }
    [BaseField("auth.operation.user.security.result.userGeneration")] public required BaseModuleGeneration UserGeneration { get; init; }
    [BaseField("auth.operation.user.security.result.securityGeneration")] public required BaseModuleGeneration SecurityGeneration { get; init; }
}
