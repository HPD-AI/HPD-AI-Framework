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
    [BaseField("auth.operation.user.profile.userName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? UserName { get; init; }
    [BaseField("auth.operation.user.profile.normalizedUserName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? NormalizedUserName { get; init; }
    [BaseField("auth.operation.user.profile.email", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 320, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? Email { get; init; }
    [BaseField("auth.operation.user.profile.normalizedEmail", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 320, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? NormalizedEmail { get; init; }
    [BaseField("auth.operation.user.profile.phoneNumber", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 64, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? PhoneNumber { get; init; }
    [BaseField("auth.operation.user.profile.displayName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 500, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? DisplayName { get; init; }
    [BaseField("auth.operation.user.profile.firstName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 100, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? FirstName { get; init; }
    [BaseField("auth.operation.user.profile.lastName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 100, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? LastName { get; init; }
    [BaseField("auth.operation.user.profile.avatarUrl", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 2048, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? AvatarUrl { get; init; }
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
    [BaseField("auth.operation.user.password.passwordHash", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 4096, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required string PasswordHash { get; init; }
    [BaseField("auth.operation.user.password.securityStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required string SecurityStamp { get; init; }
    [BaseField("auth.operation.user.password.concurrencyStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string ConcurrencyStamp { get; init; }
    [BaseField("auth.operation.user.password.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthResetPasswordV1
{
    [BaseField("auth.operation.user.password.reset.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.user.password.reset.userId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid UserId { get; init; }
    [BaseField("auth.operation.user.password.reset.expectedRevision")] public required RevisionToken ExpectedRevision { get; init; }
    [BaseField("auth.operation.user.password.reset.passwordHash", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 4096, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required string PasswordHash { get; init; }
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
