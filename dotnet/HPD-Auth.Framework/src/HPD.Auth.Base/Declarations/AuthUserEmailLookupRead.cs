using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

[BaseRead("auth.read.userByNormalizedEmail.v1", typeof(AuthUserEmailLookupReadJsonContext),
    RequiredGrantId = "auth.identity.read", Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.userByNormalizedEmail.v1.row.tenantId", "auth.read.userByNormalizedEmail.v1.row.userName", "auth.read.userByNormalizedEmail.v1.row.normalizedUserName", "auth.read.userByNormalizedEmail.v1.row.email", "auth.read.userByNormalizedEmail.v1.row.normalizedEmail", "auth.read.userByNormalizedEmail.v1.row.phoneNumber", "auth.read.userByNormalizedEmail.v1.row.userMetadata", "auth.read.userByNormalizedEmail.v1.row.appMetadata", "auth.read.userByNormalizedEmail.v1.row.firstName", "auth.read.userByNormalizedEmail.v1.row.lastName", "auth.read.userByNormalizedEmail.v1.row.displayName", "auth.read.userByNormalizedEmail.v1.row.avatarUrl", "auth.read.userByNormalizedEmail.v1.row.lastLoginAt", "auth.read.userByNormalizedEmail.v1.row.lastLoginIp", "auth.read.userByNormalizedEmail.v1.row.emailConfirmedAt"],
    SystemSourceIds = ["auth.users"])]
internal sealed partial record AuthUserByNormalizedEmailReadV1
{
    [BaseReadParameter("auth.read.userByNormalizedEmail.v1.parameter.tenantId")] public required Guid TenantId { get; init; }
    [BaseReadParameter("auth.read.userByNormalizedEmail.v1.parameter.normalizedEmail")] public required string NormalizedEmail { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.id")] public required Guid Id { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.tenantId")] public required Guid TenantId { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.userName")] public string? UserName { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.normalizedUserName")] public string? NormalizedUserName { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.email")] public string? Email { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.normalizedEmail")] public string? NormalizedEmail { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.emailConfirmed")] public required bool EmailConfirmed { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.concurrencyStamp")] public required string ConcurrencyStamp { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.phoneNumber")] public string? PhoneNumber { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.phoneNumberConfirmed")] public required bool PhoneNumberConfirmed { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.twoFactorEnabled")] public required bool TwoFactorEnabled { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.lockoutEnd"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LockoutEnd { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.lockoutEnabled")] public required bool LockoutEnabled { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.accessFailedCount")] public required int AccessFailedCount { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.audience")] public string? Audience { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.userMetadata")] public required BaseCanonicalJson UserMetadata { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.appMetadata")] public required BaseCanonicalJson AppMetadata { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.requiredActions")] public required BaseCanonicalJson RequiredActions { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.firstName")] public string? FirstName { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.lastName")] public string? LastName { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.displayName")] public string? DisplayName { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.avatarUrl")] public string? AvatarUrl { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.isActive")] public required bool IsActive { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.isDeleted")] public required bool IsDeleted { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.deletedAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? DeletedAt { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.tombstoneGeneration")] public required long TombstoneGeneration { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.updatedAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset UpdatedAt { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.lastLoginAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LastLoginAt { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.lastLoginIp")] public string? LastLoginIp { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.subscriptionTier")] public required string SubscriptionTier { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.emailConfirmedAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? EmailConfirmedAt { get; init; }
        [BaseReadField("auth.read.userByNormalizedEmail.v1.row.revision")] public required RevisionToken Revision { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthUserByNormalizedEmailReadV1, Row> read) =>
        Configure(read, Parameters.TenantId, Parameters.NormalizedEmail, false);

    internal static void Configure(
        BaseReadDefinitionBuilder<AuthUserByNormalizedEmailReadV1, Row> read,
        BaseReadParameter<AuthUserByNormalizedEmailReadV1, Guid> tenant,
        BaseReadParameter<AuthUserByNormalizedEmailReadV1, string> normalized,
        bool byName)
    {
        read.From(AuthUserRecordV1.Collection, "user", out BaseReadSource<AuthUserRecordV1> user)
            .Where(user.Field(AuthUserRecordV1.Fields.TenantId).Equal(read.Parameter(tenant))
                .And((byName ? user.Field(AuthUserRecordV1.Fields.NormalizedUserName) : user.Field(AuthUserRecordV1.Fields.NormalizedEmail)).Equal(read.Parameter(normalized))));
        read.Project(Row.Fields.Id, user.Field(AuthUserRecordV1.Fields.Id))
            .Project(Row.Fields.TenantId, user.Field(AuthUserRecordV1.Fields.TenantId))
            .Project(Row.Fields.UserName, user.Field(AuthUserRecordV1.Fields.UserName))
            .Project(Row.Fields.NormalizedUserName, user.Field(AuthUserRecordV1.Fields.NormalizedUserName))
            .Project(Row.Fields.Email, user.Field(AuthUserRecordV1.Fields.Email))
            .Project(Row.Fields.NormalizedEmail, user.Field(AuthUserRecordV1.Fields.NormalizedEmail))
            .Project(Row.Fields.EmailConfirmed, user.Field(AuthUserRecordV1.Fields.EmailConfirmed))
            .Project(Row.Fields.ConcurrencyStamp, user.Field(AuthUserRecordV1.Fields.ConcurrencyStamp))
            .Project(Row.Fields.PhoneNumber, user.Field(AuthUserRecordV1.Fields.PhoneNumber))
            .Project(Row.Fields.PhoneNumberConfirmed, user.Field(AuthUserRecordV1.Fields.PhoneNumberConfirmed))
            .Project(Row.Fields.TwoFactorEnabled, user.Field(AuthUserRecordV1.Fields.TwoFactorEnabled))
            .Project(Row.Fields.LockoutEnd, user.Field(AuthUserRecordV1.Fields.LockoutEnd))
            .Project(Row.Fields.LockoutEnabled, user.Field(AuthUserRecordV1.Fields.LockoutEnabled))
            .Project(Row.Fields.AccessFailedCount, user.Field(AuthUserRecordV1.Fields.AccessFailedCount))
            .Project(Row.Fields.Audience, user.Field(AuthUserRecordV1.Fields.Audience))
            .Project(Row.Fields.UserMetadata, user.Field(AuthUserRecordV1.Fields.UserMetadata))
            .Project(Row.Fields.AppMetadata, user.Field(AuthUserRecordV1.Fields.AppMetadata))
            .Project(Row.Fields.RequiredActions, user.Field(AuthUserRecordV1.Fields.RequiredActions))
            .Project(Row.Fields.FirstName, user.Field(AuthUserRecordV1.Fields.FirstName))
            .Project(Row.Fields.LastName, user.Field(AuthUserRecordV1.Fields.LastName))
            .Project(Row.Fields.DisplayName, user.Field(AuthUserRecordV1.Fields.DisplayName))
            .Project(Row.Fields.AvatarUrl, user.Field(AuthUserRecordV1.Fields.AvatarUrl))
            .Project(Row.Fields.IsActive, user.Field(AuthUserRecordV1.Fields.IsActive))
            .Project(Row.Fields.IsDeleted, user.Field(AuthUserRecordV1.Fields.IsDeleted))
            .Project(Row.Fields.DeletedAt, user.Field(AuthUserRecordV1.Fields.DeletedAt))
            .Project(Row.Fields.TombstoneGeneration, user.Field(AuthUserRecordV1.Fields.TombstoneGeneration))
            .Project(Row.Fields.CreatedAt, user.Field(AuthUserRecordV1.Fields.CreatedAt))
            .Project(Row.Fields.UpdatedAt, user.Field(AuthUserRecordV1.Fields.UpdatedAt))
            .Project(Row.Fields.LastLoginAt, user.Field(AuthUserRecordV1.Fields.LastLoginAt))
            .Project(Row.Fields.LastLoginIp, user.Field(AuthUserRecordV1.Fields.LastLoginIp))
            .Project(Row.Fields.SubscriptionTier, user.Field(AuthUserRecordV1.Fields.SubscriptionTier))
            .Project(Row.Fields.EmailConfirmedAt, user.Field(AuthUserRecordV1.Fields.EmailConfirmedAt))
            .Project(Row.Fields.Revision, user.Revision)
            .OrderBy(user.Field(AuthUserRecordV1.Fields.Id))
            .Limits(1, 65_536, 8, 250);
    }
}

[JsonSerializable(typeof(AuthUserByNormalizedEmailReadV1))]
[JsonSerializable(typeof(AuthUserByNormalizedEmailReadV1.Row))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal sealed partial class AuthUserEmailLookupReadJsonContext : JsonSerializerContext;
