using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

[BaseRead("auth.read.roleByNormalizedName.v1", typeof(AuthBaseMembershipReadJsonSerializerContext),
    RequiredGrantId = "auth.identity.read", Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.roleByNormalizedName.v1.row.tenantId"],
    SystemSourceIds = ["auth.roles"])]
internal sealed partial record AuthRoleByNormalizedNameReadV1
{
    [BaseReadParameter("auth.read.roleByNormalizedName.v1.parameter.tenantId")] public required Guid TenantId { get; init; }
    [BaseReadParameter("auth.read.roleByNormalizedName.v1.parameter.normalizedName")] public required string NormalizedName { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.roleByNormalizedName.v1.row.id")] public required Guid Id { get; init; }
        [BaseReadField("auth.read.roleByNormalizedName.v1.row.tenantId")] public required Guid TenantId { get; init; }
        [BaseReadField("auth.read.roleByNormalizedName.v1.row.name")] public string? Name { get; init; }
        [BaseReadField("auth.read.roleByNormalizedName.v1.row.normalizedName")] public string? NormalizedName { get; init; }
        [BaseReadField("auth.read.roleByNormalizedName.v1.row.concurrencyStamp")] public required string ConcurrencyStamp { get; init; }
        [BaseReadField("auth.read.roleByNormalizedName.v1.row.description")] public string? Description { get; init; }
        [BaseReadField("auth.read.roleByNormalizedName.v1.row.isActive")] public required bool IsActive { get; init; }
        [BaseReadField("auth.read.roleByNormalizedName.v1.row.isDeleted")] public required bool IsDeleted { get; init; }
        [BaseReadField("auth.read.roleByNormalizedName.v1.row.deletedAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? DeletedAt { get; init; }
        [BaseReadField("auth.read.roleByNormalizedName.v1.row.tombstoneGeneration")] public required long TombstoneGeneration { get; init; }
        [BaseReadField("auth.read.roleByNormalizedName.v1.row.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
        [BaseReadField("auth.read.roleByNormalizedName.v1.row.updatedAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset UpdatedAt { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthRoleByNormalizedNameReadV1, Row> read)
    {
        read.From(AuthRoleRecordV1.Collection, "role", out BaseReadSource<AuthRoleRecordV1> role)
            .Where(role.Field(AuthRoleRecordV1.Fields.TenantId).Equal(read.Parameter(Parameters.TenantId))
                .And(role.Field(AuthRoleRecordV1.Fields.NormalizedName).Equal(read.Parameter(Parameters.NormalizedName))))
            .Project(Row.Fields.Id, role.Field(AuthRoleRecordV1.Fields.Id))
            .Project(Row.Fields.TenantId, role.Field(AuthRoleRecordV1.Fields.TenantId))
            .Project(Row.Fields.Name, role.Field(AuthRoleRecordV1.Fields.Name))
            .Project(Row.Fields.NormalizedName, role.Field(AuthRoleRecordV1.Fields.NormalizedName))
            .Project(Row.Fields.ConcurrencyStamp, role.Field(AuthRoleRecordV1.Fields.ConcurrencyStamp))
            .Project(Row.Fields.Description, role.Field(AuthRoleRecordV1.Fields.Description))
            .Project(Row.Fields.IsActive, role.Field(AuthRoleRecordV1.Fields.IsActive))
            .Project(Row.Fields.IsDeleted, role.Field(AuthRoleRecordV1.Fields.IsDeleted))
            .Project(Row.Fields.DeletedAt, role.Field(AuthRoleRecordV1.Fields.DeletedAt))
            .Project(Row.Fields.TombstoneGeneration, role.Field(AuthRoleRecordV1.Fields.TombstoneGeneration))
            .Project(Row.Fields.CreatedAt, role.Field(AuthRoleRecordV1.Fields.CreatedAt))
            .Project(Row.Fields.UpdatedAt, role.Field(AuthRoleRecordV1.Fields.UpdatedAt))
            .OrderBy(role.Field(AuthRoleRecordV1.Fields.Id))
            .Limits(1, 32_768, 8, 250);
    }
}

[BaseRead("auth.read.userRoles.v1", typeof(AuthBaseMembershipReadJsonSerializerContext),
    RequiredGrantId = "auth.identity.read", Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.userRoles.v1.row.tenantId"],
    SystemSourceIds = ["auth.userRoles", "auth.roles"])]
internal sealed partial record AuthUserRolesReadV1
{
    [BaseReadParameter("auth.read.userRoles.v1.parameter.tenantId")] public required Guid TenantId { get; init; }
    [BaseReadParameter("auth.read.userRoles.v1.parameter.userId")] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.userRoles.v1.row.tenantId")] public required Guid TenantId { get; init; }
        [BaseReadField("auth.read.userRoles.v1.row.roleId")] public required BaseRecordId<AuthRoleRecordV1> RoleId { get; init; }
        [BaseReadField("auth.read.userRoles.v1.row.name")] public string? Name { get; init; }
        [BaseReadField("auth.read.userRoles.v1.row.normalizedName")] public string? NormalizedName { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthUserRolesReadV1, Row> read)
    {
        read.From(AuthUserRoleRecordV1.Collection, "membership", out BaseReadSource<AuthUserRoleRecordV1> membership)
            .Join(AuthRoleRecordV1.Collection, "role", membership.Field(AuthUserRoleRecordV1.Fields.RoleId), BaseFields.RecordId, BaseJoinKind.Inner, out BaseReadSource<AuthRoleRecordV1> role)
            .Where(membership.Field(AuthUserRoleRecordV1.Fields.TenantId).Equal(read.Parameter(Parameters.TenantId))
                .And(membership.Field(AuthUserRoleRecordV1.Fields.UserId).Equal(read.Parameter(Parameters.UserId))))
            .Project(Row.Fields.TenantId, membership.Field(AuthUserRoleRecordV1.Fields.TenantId))
            .Project(Row.Fields.RoleId, membership.Field(AuthUserRoleRecordV1.Fields.RoleId))
            .Project(Row.Fields.Name, role.Field(AuthRoleRecordV1.Fields.Name))
            .Project(Row.Fields.NormalizedName, role.Field(AuthRoleRecordV1.Fields.NormalizedName))
            .OrderBy(role.Field(AuthRoleRecordV1.Fields.NormalizedName), QuerySortDirection.Asc, QueryNullOrder.First)
            .OrderBy(role.Field(AuthRoleRecordV1.Fields.Id))
            .Limits(256, 262_144, 12, 500);
    }
}

[BaseRead("auth.read.userLogins.v1", typeof(AuthBaseMembershipReadJsonSerializerContext),
    RequiredGrantId = "auth.identity.read", Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.userLogins.v1.row.providerKey", "auth.read.userLogins.v1.row.tenantId"],
    SystemSourceIds = ["auth.userLogins"])]
internal sealed partial record AuthUserLoginsReadV1
{
    [BaseReadParameter("auth.read.userLogins.v1.parameter.tenantId")] public required Guid TenantId { get; init; }
    [BaseReadParameter("auth.read.userLogins.v1.parameter.userId")] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.userLogins.v1.row.id")] public required string Id { get; init; }
        [BaseReadField("auth.read.userLogins.v1.row.tenantId")] public required Guid TenantId { get; init; }
        [BaseReadField("auth.read.userLogins.v1.row.userId")] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
        [BaseReadField("auth.read.userLogins.v1.row.loginProvider")] public required string LoginProvider { get; init; }
        [BaseReadField("auth.read.userLogins.v1.row.providerKey")] public required string ProviderKey { get; init; }
        [BaseReadField("auth.read.userLogins.v1.row.providerDisplayName")] public string? ProviderDisplayName { get; init; }
        [BaseReadField("auth.read.userLogins.v1.row.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthUserLoginsReadV1, Row> read)
    {
        read.From(AuthUserLoginRecordV1.Collection, "login", out BaseReadSource<AuthUserLoginRecordV1> login)
            .Where(login.Field(AuthUserLoginRecordV1.Fields.TenantId).Equal(read.Parameter(Parameters.TenantId))
                .And(login.Field(AuthUserLoginRecordV1.Fields.UserId).Equal(read.Parameter(Parameters.UserId))))
            .Project(Row.Fields.Id, login.Field(AuthUserLoginRecordV1.Fields.Id))
            .Project(Row.Fields.TenantId, login.Field(AuthUserLoginRecordV1.Fields.TenantId))
            .Project(Row.Fields.UserId, login.Field(AuthUserLoginRecordV1.Fields.UserId))
            .Project(Row.Fields.LoginProvider, login.Field(AuthUserLoginRecordV1.Fields.LoginProvider))
            .Project(Row.Fields.ProviderKey, login.Field(AuthUserLoginRecordV1.Fields.ProviderKey))
            .Project(Row.Fields.ProviderDisplayName, login.Field(AuthUserLoginRecordV1.Fields.ProviderDisplayName))
            .Project(Row.Fields.CreatedAt, login.Field(AuthUserLoginRecordV1.Fields.CreatedAt))
            .OrderBy(login.Field(AuthUserLoginRecordV1.Fields.LoginProvider))
            .OrderBy(login.Field(AuthUserLoginRecordV1.Fields.Id))
            .Limits(64, 131_072, 8, 500);
    }
}

[JsonSerializable(typeof(AuthRoleByNormalizedNameReadV1), TypeInfoPropertyName = "AuthRoleByNormalizedNameReadV1")]
[JsonSerializable(typeof(AuthRoleByNormalizedNameReadV1.Row), TypeInfoPropertyName = "AuthRoleByNormalizedNameReadV1Row")]
[JsonSerializable(typeof(AuthUserRolesReadV1), TypeInfoPropertyName = "AuthUserRolesReadV1")]
[JsonSerializable(typeof(AuthUserRolesReadV1.Row), TypeInfoPropertyName = "AuthUserRolesReadV1Row")]
[JsonSerializable(typeof(AuthUserLoginsReadV1), TypeInfoPropertyName = "AuthUserLoginsReadV1")]
[JsonSerializable(typeof(AuthUserLoginsReadV1.Row), TypeInfoPropertyName = "AuthUserLoginsReadV1Row")]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal sealed partial class AuthBaseMembershipReadJsonSerializerContext : JsonSerializerContext;
