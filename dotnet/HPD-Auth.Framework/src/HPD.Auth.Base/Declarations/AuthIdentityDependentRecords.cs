using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

[BaseCollection("auth.userClaims", typeof(AuthBaseJsonSerializerContext), SystemOwnerModuleId = AuthBaseContract.ModuleId)]
[BaseIndex("auth.idx.userClaims.user")]
[BaseIndexPart("auth.idx.userClaims.user", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.userClaims.user", 1, nameof(UserId))]
[BaseIndexPart("auth.idx.userClaims.user", 2, nameof(Id))]
internal sealed partial record AuthUserClaimRecordV1
{
    [BaseField("auth.userClaims.id", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required Guid Id { get; init; }
    [BaseField("auth.userClaims.tenantId", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required Guid TenantId { get; init; }
    [BaseField("auth.userClaims.userId", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), BaseRelation("auth.rel.userClaim.user", typeof(AuthUserRecordV1), LocalMultiplicity = BaseRelationMultiplicity.ExactlyOne)] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
    [BaseField("auth.userClaims.claimType", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public string? ClaimType { get; init; }
    [BaseField("auth.userClaims.claimValue", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 4096, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? ClaimValue { get; init; }
    [BaseField("auth.userClaims.issuer", MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Issuer { get; init; }
    [BaseField("auth.userClaims.originalIssuer", MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string OriginalIssuer { get; init; }
    [BaseField("auth.userClaims.valueType", MaximumUtf8Bytes = 128, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string ValueType { get; init; }
    [BaseField("auth.userClaims.createdAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
}

[BaseCollection("auth.roleClaims", typeof(AuthBaseJsonSerializerContext), SystemOwnerModuleId = AuthBaseContract.ModuleId)]
[BaseIndex("auth.idx.roleClaims.role")]
[BaseIndexPart("auth.idx.roleClaims.role", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.roleClaims.role", 1, nameof(RoleId))]
[BaseIndexPart("auth.idx.roleClaims.role", 2, nameof(Id))]
internal sealed partial record AuthRoleClaimRecordV1
{
    [BaseField("auth.roleClaims.id", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required Guid Id { get; init; }
    [BaseField("auth.roleClaims.tenantId", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required Guid TenantId { get; init; }
    [BaseField("auth.roleClaims.roleId", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), BaseRelation("auth.rel.roleClaim.role", typeof(AuthRoleRecordV1), LocalMultiplicity = BaseRelationMultiplicity.ExactlyOne)] public required BaseRecordId<AuthRoleRecordV1> RoleId { get; init; }
    [BaseField("auth.roleClaims.claimType", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public string? ClaimType { get; init; }
    [BaseField("auth.roleClaims.claimValue", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 4096, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public string? ClaimValue { get; init; }
    [BaseField("auth.roleClaims.issuer", MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Issuer { get; init; }
    [BaseField("auth.roleClaims.originalIssuer", MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string OriginalIssuer { get; init; }
    [BaseField("auth.roleClaims.valueType", MaximumUtf8Bytes = 128, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string ValueType { get; init; }
    [BaseField("auth.roleClaims.createdAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
}

[BaseCollection("auth.userRoles", typeof(AuthBaseJsonSerializerContext), SystemOwnerModuleId = AuthBaseContract.ModuleId)]
[BaseIndex("auth.idx.userRoles.user", Unique = true)]
[BaseIndexPart("auth.idx.userRoles.user", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.userRoles.user", 1, nameof(UserId))]
[BaseIndexPart("auth.idx.userRoles.user", 2, nameof(RoleId))]
[BaseIndex("auth.idx.userRoles.role")]
[BaseIndexPart("auth.idx.userRoles.role", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.userRoles.role", 1, nameof(RoleId))]
[BaseIndexPart("auth.idx.userRoles.role", 2, nameof(UserId))]
internal sealed partial record AuthUserRoleRecordV1
{
    [BaseField("auth.userRoles.id", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Id { get; init; }
    [BaseField("auth.userRoles.tenantId", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required Guid TenantId { get; init; }
    [BaseField("auth.userRoles.userId", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), BaseRelation("auth.rel.userRole.user", typeof(AuthUserRecordV1), LocalMultiplicity = BaseRelationMultiplicity.ExactlyOne)] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
    [BaseField("auth.userRoles.roleId", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), BaseRelation("auth.rel.userRole.role", typeof(AuthRoleRecordV1), LocalMultiplicity = BaseRelationMultiplicity.ExactlyOne)] public required BaseRecordId<AuthRoleRecordV1> RoleId { get; init; }
    [BaseField("auth.userRoles.createdAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
}

[BaseCollection("auth.userLogins", typeof(AuthBaseJsonSerializerContext), SystemOwnerModuleId = AuthBaseContract.ModuleId)]
[BaseIndex("auth.idx.logins.providerKey", Unique = true)]
[BaseIndexPart("auth.idx.logins.providerKey", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.logins.providerKey", 1, nameof(LoginProvider))]
[BaseIndexPart("auth.idx.logins.providerKey", 2, nameof(ProviderKey))]
[BaseIndex("auth.idx.logins.user")]
[BaseIndexPart("auth.idx.logins.user", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.logins.user", 1, nameof(UserId))]
[BaseIndexPart("auth.idx.logins.user", 2, nameof(Id))]
internal sealed partial record AuthUserLoginRecordV1
{
    [BaseField("auth.userLogins.id", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64, Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Id { get; init; }
    [BaseField("auth.userLogins.tenantId", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required Guid TenantId { get; init; }
    [BaseField("auth.userLogins.userId", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), BaseRelation("auth.rel.userLogin.user", typeof(AuthUserRecordV1), LocalMultiplicity = BaseRelationMultiplicity.ExactlyOne)] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
    [BaseField("auth.userLogins.loginProvider", MaximumUtf8Bytes = 128, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string LoginProvider { get; init; }
    [BaseField("auth.userLogins.providerKey", MaximumUtf8Bytes = 512, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required string ProviderKey { get; init; }
    [BaseField("auth.userLogins.providerDisplayName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public string? ProviderDisplayName { get; init; }
    [BaseField("auth.userLogins.createdAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
}

[BaseCollection("auth.userTokens", typeof(AuthBaseJsonSerializerContext), SystemOwnerModuleId = AuthBaseContract.ModuleId)]
[BaseIndex("auth.idx.tokens.key", Unique = true)]
[BaseIndexPart("auth.idx.tokens.key", 0, nameof(TenantId))]
[BaseIndexPart("auth.idx.tokens.key", 1, nameof(UserId))]
[BaseIndexPart("auth.idx.tokens.key", 2, nameof(LoginProvider))]
[BaseIndexPart("auth.idx.tokens.key", 3, nameof(Name))]
internal sealed partial record AuthUserTokenRecordV1
{
    [BaseField("auth.userTokens.id", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Id { get; init; }
    [BaseField("auth.userTokens.tenantId", Operators = BaseFieldOperator.Equal), BaseFieldConfidentiality(BaseFieldConfidentiality.Confidential)] public required Guid TenantId { get; init; }
    [BaseField("auth.userTokens.userId", Operators = BaseFieldOperator.Equal), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), BaseRelation("auth.rel.userToken.user", typeof(AuthUserRecordV1), LocalMultiplicity = BaseRelationMultiplicity.ExactlyOne)] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
    [BaseField("auth.userTokens.loginProvider", MaximumUtf8Bytes = 128, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string LoginProvider { get; init; }
    [BaseField("auth.userTokens.name", MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Name { get; init; }
    [BaseField("auth.userTokens.value", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 16384, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public string? Value { get; init; }
    [BaseField("auth.userTokens.updatedAt"), BaseFieldConfidentiality(BaseFieldConfidentiality.Internal), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset UpdatedAt { get; init; }
}
