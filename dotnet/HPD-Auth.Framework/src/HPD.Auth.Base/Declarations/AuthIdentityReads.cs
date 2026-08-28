using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

[BaseRead("auth.read.userClaims.v1", typeof(AuthBaseIdentityReadJsonSerializerContext),
    RequiredGrantId = "auth.identity.read", Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.userClaims.v1.row.claimValue", "auth.read.userClaims.v1.row.tenantId"],
    SystemSourceIds = ["auth.userClaims"])]
internal sealed partial record AuthUserClaimsReadV1
{
    [BaseReadParameter("auth.read.userClaims.v1.parameter.tenantId")] public required Guid TenantId { get; init; }
    [BaseReadParameter("auth.read.userClaims.v1.parameter.userId")] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.userClaims.v1.row.id")] public required Guid Id { get; init; }
        [BaseReadField("auth.read.userClaims.v1.row.tenantId")] public required Guid TenantId { get; init; }
        [BaseReadField("auth.read.userClaims.v1.row.userId")] public required BaseRecordId<AuthUserRecordV1> UserId { get; init; }
        [BaseReadField("auth.read.userClaims.v1.row.claimType")] public string? ClaimType { get; init; }
        [BaseReadField("auth.read.userClaims.v1.row.claimValue")] public string? ClaimValue { get; init; }
        [BaseReadField("auth.read.userClaims.v1.row.issuer")] public required string Issuer { get; init; }
        [BaseReadField("auth.read.userClaims.v1.row.originalIssuer")] public required string OriginalIssuer { get; init; }
        [BaseReadField("auth.read.userClaims.v1.row.valueType")] public required string ValueType { get; init; }
        [BaseReadField("auth.read.userClaims.v1.row.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
        [BaseReadField("auth.read.userClaims.v1.row.revision")] public required RevisionToken Revision { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthUserClaimsReadV1, Row> read)
    {
        read.From(AuthUserClaimRecordV1.Collection, "claim", out BaseReadSource<AuthUserClaimRecordV1> claim)
            .Where(claim.Field(AuthUserClaimRecordV1.Fields.TenantId).Equal(read.Parameter(Parameters.TenantId))
                .And(claim.Field(AuthUserClaimRecordV1.Fields.UserId).Equal(read.Parameter(Parameters.UserId))))
            .Project(Row.Fields.Id, claim.Field(AuthUserClaimRecordV1.Fields.Id))
            .Project(Row.Fields.TenantId, claim.Field(AuthUserClaimRecordV1.Fields.TenantId))
            .Project(Row.Fields.UserId, claim.Field(AuthUserClaimRecordV1.Fields.UserId))
            .Project(Row.Fields.ClaimType, claim.Field(AuthUserClaimRecordV1.Fields.ClaimType))
            .Project(Row.Fields.ClaimValue, claim.Field(AuthUserClaimRecordV1.Fields.ClaimValue))
            .Project(Row.Fields.Issuer, claim.Field(AuthUserClaimRecordV1.Fields.Issuer))
            .Project(Row.Fields.OriginalIssuer, claim.Field(AuthUserClaimRecordV1.Fields.OriginalIssuer))
            .Project(Row.Fields.ValueType, claim.Field(AuthUserClaimRecordV1.Fields.ValueType))
            .Project(Row.Fields.CreatedAt, claim.Field(AuthUserClaimRecordV1.Fields.CreatedAt))
            .Project(Row.Fields.Revision, claim.Revision)
            .OrderBy(claim.Field(AuthUserClaimRecordV1.Fields.Id))
            .Limits(256, 262_144, 8, 500);
    }
}

[BaseRead("auth.read.roleClaims.v1", typeof(AuthBaseIdentityReadJsonSerializerContext),
    RequiredGrantId = "auth.identity.read", Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.roleClaims.v1.row.claimValue", "auth.read.roleClaims.v1.row.tenantId"],
    SystemSourceIds = ["auth.roleClaims"])]
internal sealed partial record AuthRoleClaimsReadV1
{
    [BaseReadParameter("auth.read.roleClaims.v1.parameter.tenantId")] public required Guid TenantId { get; init; }
    [BaseReadParameter("auth.read.roleClaims.v1.parameter.roleId")] public required BaseRecordId<AuthRoleRecordV1> RoleId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.roleClaims.v1.row.id")] public required Guid Id { get; init; }
        [BaseReadField("auth.read.roleClaims.v1.row.tenantId")] public required Guid TenantId { get; init; }
        [BaseReadField("auth.read.roleClaims.v1.row.roleId")] public required BaseRecordId<AuthRoleRecordV1> RoleId { get; init; }
        [BaseReadField("auth.read.roleClaims.v1.row.claimType")] public string? ClaimType { get; init; }
        [BaseReadField("auth.read.roleClaims.v1.row.claimValue")] public string? ClaimValue { get; init; }
        [BaseReadField("auth.read.roleClaims.v1.row.issuer")] public required string Issuer { get; init; }
        [BaseReadField("auth.read.roleClaims.v1.row.originalIssuer")] public required string OriginalIssuer { get; init; }
        [BaseReadField("auth.read.roleClaims.v1.row.valueType")] public required string ValueType { get; init; }
        [BaseReadField("auth.read.roleClaims.v1.row.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
        [BaseReadField("auth.read.roleClaims.v1.row.revision")] public required RevisionToken Revision { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthRoleClaimsReadV1, Row> read)
    {
        read.From(AuthRoleClaimRecordV1.Collection, "claim", out BaseReadSource<AuthRoleClaimRecordV1> claim)
            .Where(claim.Field(AuthRoleClaimRecordV1.Fields.TenantId).Equal(read.Parameter(Parameters.TenantId))
                .And(claim.Field(AuthRoleClaimRecordV1.Fields.RoleId).Equal(read.Parameter(Parameters.RoleId))))
            .Project(Row.Fields.Id, claim.Field(AuthRoleClaimRecordV1.Fields.Id))
            .Project(Row.Fields.TenantId, claim.Field(AuthRoleClaimRecordV1.Fields.TenantId))
            .Project(Row.Fields.RoleId, claim.Field(AuthRoleClaimRecordV1.Fields.RoleId))
            .Project(Row.Fields.ClaimType, claim.Field(AuthRoleClaimRecordV1.Fields.ClaimType))
            .Project(Row.Fields.ClaimValue, claim.Field(AuthRoleClaimRecordV1.Fields.ClaimValue))
            .Project(Row.Fields.Issuer, claim.Field(AuthRoleClaimRecordV1.Fields.Issuer))
            .Project(Row.Fields.OriginalIssuer, claim.Field(AuthRoleClaimRecordV1.Fields.OriginalIssuer))
            .Project(Row.Fields.ValueType, claim.Field(AuthRoleClaimRecordV1.Fields.ValueType))
            .Project(Row.Fields.CreatedAt, claim.Field(AuthRoleClaimRecordV1.Fields.CreatedAt))
            .Project(Row.Fields.Revision, claim.Revision)
            .OrderBy(claim.Field(AuthRoleClaimRecordV1.Fields.Id))
            .Limits(256, 262_144, 8, 500);
    }
}

[BaseRead("auth.read.tenantSettings.v1", typeof(AuthBaseIdentityReadJsonSerializerContext),
    RequiredGrantId = "auth.identity.read", Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.tenantSettings.v1.row.settings"],
    SystemSourceIds = ["auth.tenantSettings"])]
internal sealed partial record AuthTenantSettingsReadV1
{
    [BaseReadParameter("auth.read.tenantSettings.v1.parameter.tenantId")]
    public required Guid TenantId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.tenantSettings.v1.row.settings")]
        public required BaseCanonicalJson Settings { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthTenantSettingsReadV1, Row> read)
    {
        read.From(AuthTenantSettingsRecordV1.Collection, "settings", out BaseReadSource<AuthTenantSettingsRecordV1> settings)
            .Where(settings.Field(AuthTenantSettingsRecordV1.Fields.TenantId).Equal(read.Parameter(Parameters.TenantId)))
            .Project(Row.Fields.Settings, settings.Field(AuthTenantSettingsRecordV1.Fields.Settings))
            .OrderBy(settings.RecordId)
            .Limits(1, 131_072, 4, 250);
    }
}

[JsonSerializable(typeof(AuthUserClaimsReadV1), TypeInfoPropertyName = "AuthUserClaimsReadV1")]
[JsonSerializable(typeof(AuthUserClaimsReadV1.Row), TypeInfoPropertyName = "AuthUserClaimsReadV1Row")]
[JsonSerializable(typeof(AuthRoleClaimsReadV1), TypeInfoPropertyName = "AuthRoleClaimsReadV1")]
[JsonSerializable(typeof(AuthRoleClaimsReadV1.Row), TypeInfoPropertyName = "AuthRoleClaimsReadV1Row")]
[JsonSerializable(typeof(AuthTenantSettingsReadV1), TypeInfoPropertyName = "AuthTenantSettingsReadV1")]
[JsonSerializable(typeof(AuthTenantSettingsReadV1.Row), TypeInfoPropertyName = "AuthTenantSettingsReadV1Row")]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal sealed partial class AuthBaseIdentityReadJsonSerializerContext : JsonSerializerContext;
