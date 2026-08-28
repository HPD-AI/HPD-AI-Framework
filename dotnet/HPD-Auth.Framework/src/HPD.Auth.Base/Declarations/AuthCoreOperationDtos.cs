using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

internal sealed record AuthRoleCreateV1
{
    [BaseField("auth.operation.role.create.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.role.create.roleId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid RoleId { get; init; }
    [BaseField("auth.operation.role.create.name", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? Name { get; init; }
    [BaseField("auth.operation.role.create.normalizedName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? NormalizedName { get; init; }
    [BaseField("auth.operation.role.create.concurrencyStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string ConcurrencyStamp { get; init; }
    [BaseField("auth.operation.role.create.description", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 500, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? Description { get; init; }
    [BaseField("auth.operation.role.create.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthRoleCreateResultV1
{
    [BaseField("auth.operation.role.create.result.roleId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid RoleId { get; init; }
    [BaseField("auth.operation.role.create.result.revision")] public required RevisionToken Revision { get; init; }
    [BaseField("auth.operation.role.create.result.roleGeneration")] public required BaseModuleGeneration RoleGeneration { get; init; }
}

internal sealed record AuthRoleRenameV1
{
    [BaseField("auth.operation.role.rename.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.role.rename.roleId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid RoleId { get; init; }
    [BaseField("auth.operation.role.rename.expectedRevision")] public required RevisionToken ExpectedRevision { get; init; }
    [BaseField("auth.operation.role.rename.name", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? Name { get; init; }
    [BaseField("auth.operation.role.rename.normalizedName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? NormalizedName { get; init; }
    [BaseField("auth.operation.role.rename.concurrencyStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string ConcurrencyStamp { get; init; }
    [BaseField("auth.operation.role.rename.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthRoleMutationResultV1
{
    [BaseField("auth.operation.role.result.revision")] public required RevisionToken Revision { get; init; }
    [BaseField("auth.operation.role.result.roleGeneration")] public required BaseModuleGeneration RoleGeneration { get; init; }
}

internal sealed record AuthMembershipAddV1
{
    [BaseField("auth.operation.membership.add.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.membership.add.userId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid UserId { get; init; }
    [BaseField("auth.operation.membership.add.roleId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid RoleId { get; init; }
    [BaseField("auth.operation.membership.add.membershipId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string MembershipId { get; init; }
    [BaseField("auth.operation.membership.add.expectedUserRevision")] public required RevisionToken ExpectedUserRevision { get; init; }
    [BaseField("auth.operation.membership.add.expectedRoleRevision")] public required RevisionToken ExpectedRoleRevision { get; init; }
    [BaseField("auth.operation.membership.add.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
}

internal sealed record AuthMembershipRemoveV1
{
    [BaseField("auth.operation.membership.remove.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.membership.remove.userId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid UserId { get; init; }
    [BaseField("auth.operation.membership.remove.roleId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid RoleId { get; init; }
    [BaseField("auth.operation.membership.remove.membershipId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string MembershipId { get; init; }
    [BaseField("auth.operation.membership.remove.expectedUserRevision")] public required RevisionToken ExpectedUserRevision { get; init; }
    [BaseField("auth.operation.membership.remove.expectedRoleRevision")] public required RevisionToken ExpectedRoleRevision { get; init; }
    [BaseField("auth.operation.membership.remove.expectedMembershipRevision")] public required RevisionToken ExpectedMembershipRevision { get; init; }
    [BaseField("auth.operation.membership.remove.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthMembershipAddResultV1
{
    [BaseField("auth.operation.membership.add.result.membershipId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string MembershipId { get; init; }
    [BaseField("auth.operation.membership.add.result.revision")] public required RevisionToken Revision { get; init; }
    [BaseField("auth.operation.membership.add.result.membershipGeneration")] public required BaseModuleGeneration MembershipGeneration { get; init; }
}

internal sealed record AuthMembershipRemoveResultV1
{
    [BaseField("auth.operation.membership.remove.result.membershipGeneration")] public required BaseModuleGeneration MembershipGeneration { get; init; }
}

internal sealed record AuthLoginLinkV1
{
    [BaseField("auth.operation.login.link.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.login.link.userId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid UserId { get; init; }
    [BaseField("auth.operation.login.link.loginId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string LoginId { get; init; }
    [BaseField("auth.operation.login.link.identityId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid IdentityId { get; init; }
    [BaseField("auth.operation.login.link.expectedUserRevision")] public required RevisionToken ExpectedUserRevision { get; init; }
    [BaseField("auth.operation.login.link.loginProvider", MaximumUtf8Bytes = 128, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string LoginProvider { get; init; }
    [BaseField("auth.operation.login.link.providerKey", MaximumUtf8Bytes = 512, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string ProviderKey { get; init; }
    [BaseField("auth.operation.login.link.providerDisplayName", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public string? ProviderDisplayName { get; init; }
    [BaseField("auth.operation.login.link.providerId", MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string ProviderId { get; init; }
    [BaseField("auth.operation.login.link.identityData", MaximumCanonicalJsonBytes = 65_536, JsonShape = BaseJsonShape.Object, MaximumJsonDepth = 16, MaximumJsonArrayItems = 1_024, MaximumJsonObjectProperties = 1_024, MaximumJsonTotalNodes = 4_096, MaximumJsonTotalStringUtf8Bytes = 65_536, MaximumJsonTotalNameUtf8Bytes = 65_536)] public required BaseCanonicalJson IdentityData { get; init; }
    [BaseField("auth.operation.login.link.federationSourceId", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable)] public BaseRecordId<AuthSsoProviderRecordV1>? FederationSourceId { get; init; }
    [BaseField("auth.operation.login.link.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthLoginUnlinkV1
{
    [BaseField("auth.operation.login.unlink.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.login.unlink.userId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid UserId { get; init; }
    [BaseField("auth.operation.login.unlink.loginId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string LoginId { get; init; }
    [BaseField("auth.operation.login.unlink.identityId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid IdentityId { get; init; }
    [BaseField("auth.operation.login.unlink.expectedUserRevision")] public required RevisionToken ExpectedUserRevision { get; init; }
    [BaseField("auth.operation.login.unlink.expectedLoginRevision")] public required RevisionToken ExpectedLoginRevision { get; init; }
    [BaseField("auth.operation.login.unlink.expectedIdentityRevision")] public required RevisionToken ExpectedIdentityRevision { get; init; }
    [BaseField("auth.operation.login.unlink.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthLoginLinkResultV1
{
    [BaseField("auth.operation.login.link.result.loginId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string LoginId { get; init; }
    [BaseField("auth.operation.login.link.result.loginRevision")] public required RevisionToken LoginRevision { get; init; }
    [BaseField("auth.operation.login.link.result.identityId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid IdentityId { get; init; }
    [BaseField("auth.operation.login.link.result.identityRevision")] public required RevisionToken IdentityRevision { get; init; }
    [BaseField("auth.operation.login.link.result.userGeneration")] public required BaseModuleGeneration UserGeneration { get; init; }
}

internal sealed record AuthLoginUnlinkResultV1
{
    [BaseField("auth.operation.login.unlink.result.loginId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string LoginId { get; init; }
    [BaseField("auth.operation.login.unlink.result.identityId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid IdentityId { get; init; }
    [BaseField("auth.operation.login.unlink.result.userGeneration")] public required BaseModuleGeneration UserGeneration { get; init; }
}
