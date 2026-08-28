using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

internal interface IAuthAdminUserReadRow
{
    Guid Id { get; }
    string? Email { get; }
    bool EmailConfirmed { get; }
    string? FirstName { get; }
    string? LastName { get; }
    string? DisplayName { get; }
    string SubscriptionTier { get; }
    bool IsActive { get; }
    bool IsDeleted { get; }
    DateTimeOffset? LastLoginAt { get; }
    string? LastLoginIp { get; }
    DateTimeOffset CreatedAt { get; }
    BaseCanonicalJson UserMetadata { get; }
    BaseCanonicalJson AppMetadata { get; }
    BaseCanonicalJson RequiredActions { get; }
    DateTimeOffset? LockoutEnd { get; }
}

internal enum AuthAdminUserReadSort { CreatedAt, Email, LastLoginAt }

internal sealed record AuthAdminUserReadParameters<TParameters>
{
    internal required BaseReadParameter<TParameters, Guid> TenantId { get; init; }
    internal required BaseReadParameter<TParameters, bool> ApplySearch { get; init; }
    internal required BaseReadParameter<TParameters, string> Search { get; init; }
    internal required BaseReadParameter<TParameters, bool> ApplyEmail { get; init; }
    internal required BaseReadParameter<TParameters, string> Email { get; init; }
    internal required BaseReadParameter<TParameters, bool> ApplyEmailVerified { get; init; }
    internal required BaseReadParameter<TParameters, bool> EmailVerified { get; init; }
    internal required BaseReadParameter<TParameters, bool> ApplyEnabled { get; init; }
    internal required BaseReadParameter<TParameters, bool> Enabled { get; init; }
    internal required BaseReadParameter<TParameters, bool> ApplyRole { get; init; }
    internal required BaseReadParameter<TParameters, Guid> RoleId { get; init; }
}

internal sealed record AuthAdminUserReadFields<TRow>
{
    internal required BaseReadField<TRow, Guid> Id { get; init; }
    internal required BaseReadField<TRow, string> Email { get; init; }
    internal required BaseReadField<TRow, bool> EmailConfirmed { get; init; }
    internal required BaseReadField<TRow, string> FirstName { get; init; }
    internal required BaseReadField<TRow, string> LastName { get; init; }
    internal required BaseReadField<TRow, string> DisplayName { get; init; }
    internal required BaseReadField<TRow, string> SubscriptionTier { get; init; }
    internal required BaseReadField<TRow, bool> IsActive { get; init; }
    internal required BaseReadField<TRow, bool> IsDeleted { get; init; }
    internal required BaseReadField<TRow, DateTimeOffset?> LastLoginAt { get; init; }
    internal required BaseReadField<TRow, string> LastLoginIp { get; init; }
    internal required BaseReadField<TRow, DateTimeOffset> CreatedAt { get; init; }
    internal required BaseReadField<TRow, BaseCanonicalJson> UserMetadata { get; init; }
    internal required BaseReadField<TRow, BaseCanonicalJson> AppMetadata { get; init; }
    internal required BaseReadField<TRow, BaseCanonicalJson> RequiredActions { get; init; }
    internal required BaseReadField<TRow, DateTimeOffset?> LockoutEnd { get; init; }
}

internal static class AuthAdminUserReadDefinition
{
    internal static void Configure<TParameters, TRow>(
        BaseReadDefinitionBuilder<TParameters, TRow> read,
        AuthAdminUserReadParameters<TParameters> parameters,
        AuthAdminUserReadFields<TRow> fields,
        AuthAdminUserReadSort sort,
        QuerySortDirection direction)
    {
        read.From(AuthUserRecordV1.Collection, "user", out BaseReadSource<AuthUserRecordV1> user)
            .LeftJoin(AuthUserRoleRecordV1.Collection, "membership", user.RecordId,
                AuthUserRoleRecordV1.Fields.UserId, out BaseReadSource<AuthUserRoleRecordV1> membership);

        BaseReadPredicate search = read.Literal(false).Equal(read.Parameter(parameters.ApplySearch)).Or(
            user.Field(AuthUserRecordV1.Fields.Email).Contains(read.Parameter(parameters.Search))
                .Or(user.Field(AuthUserRecordV1.Fields.UserName).Contains(read.Parameter(parameters.Search)))
                .Or(user.Field(AuthUserRecordV1.Fields.FirstName).Contains(read.Parameter(parameters.Search)))
                .Or(user.Field(AuthUserRecordV1.Fields.LastName).Contains(read.Parameter(parameters.Search))));
        BaseReadPredicate email = read.Literal(false).Equal(read.Parameter(parameters.ApplyEmail)).Or(
            user.Field(AuthUserRecordV1.Fields.Email).Contains(read.Parameter(parameters.Email)));
        BaseReadPredicate verified = read.Literal(false).Equal(read.Parameter(parameters.ApplyEmailVerified)).Or(
            user.Field(AuthUserRecordV1.Fields.EmailConfirmed).Equal(read.Parameter(parameters.EmailVerified)));
        BaseReadPredicate enabled = read.Literal(false).Equal(read.Parameter(parameters.ApplyEnabled)).Or(
            user.Field(AuthUserRecordV1.Fields.IsActive).Equal(read.Parameter(parameters.Enabled)));
        BaseReadPredicate role = read.Literal(false).Equal(read.Parameter(parameters.ApplyRole)).Or(
            membership.Field(AuthUserRoleRecordV1.Fields.RoleId).Equal(
                read.RecordIdParameter<AuthRoleRecordV1>(parameters.RoleId)));

        read.Where(user.Field(AuthUserRecordV1.Fields.TenantId).Equal(read.Parameter(parameters.TenantId))
                .And(user.Field(AuthUserRecordV1.Fields.IsDeleted).Equal(read.Literal(false)))
                .And(search).And(email).And(verified).And(enabled).And(role))
            .Project(fields.Id, user.Field(AuthUserRecordV1.Fields.Id))
            .Project(fields.Email, user.Field(AuthUserRecordV1.Fields.Email))
            .Project(fields.EmailConfirmed, user.Field(AuthUserRecordV1.Fields.EmailConfirmed))
            .Project(fields.FirstName, user.Field(AuthUserRecordV1.Fields.FirstName))
            .Project(fields.LastName, user.Field(AuthUserRecordV1.Fields.LastName))
            .Project(fields.DisplayName, user.Field(AuthUserRecordV1.Fields.DisplayName))
            .Project(fields.SubscriptionTier, user.Field(AuthUserRecordV1.Fields.SubscriptionTier))
            .Project(fields.IsActive, user.Field(AuthUserRecordV1.Fields.IsActive))
            .Project(fields.IsDeleted, user.Field(AuthUserRecordV1.Fields.IsDeleted))
            .Project(fields.LastLoginAt, user.Field(AuthUserRecordV1.Fields.LastLoginAt))
            .Project(fields.LastLoginIp, user.Field(AuthUserRecordV1.Fields.LastLoginIp))
            .Project(fields.CreatedAt, user.Field(AuthUserRecordV1.Fields.CreatedAt))
            .Project(fields.UserMetadata, user.Field(AuthUserRecordV1.Fields.UserMetadata))
            .Project(fields.AppMetadata, user.Field(AuthUserRecordV1.Fields.AppMetadata))
            .Project(fields.RequiredActions, user.Field(AuthUserRecordV1.Fields.RequiredActions))
            .Project(fields.LockoutEnd, user.Field(AuthUserRecordV1.Fields.LockoutEnd))
            .Distinct();

        QueryNullOrder nulls = direction == QuerySortDirection.Asc ? QueryNullOrder.First : QueryNullOrder.Last;
        switch (sort)
        {
            case AuthAdminUserReadSort.CreatedAt:
                read.OrderBy(user.Field(AuthUserRecordV1.Fields.CreatedAt), direction);
                break;
            case AuthAdminUserReadSort.Email:
                read.OrderBy(user.Field(AuthUserRecordV1.Fields.Email), direction, nulls);
                break;
            case AuthAdminUserReadSort.LastLoginAt:
                read.OrderBy(user.Field(AuthUserRecordV1.Fields.LastLoginAt), direction, nulls);
                break;
            default:
                throw new InvalidOperationException("auth.admin.userRead.invalidSort");
        }
        read.OrderBy(user.Field(AuthUserRecordV1.Fields.Id), direction)
            .Limits(200, 16_777_216, 64, 2_000)
            .AllowOffsetPagination(100_000);
    }
}

[BaseRead("auth.read.adminUsers.createdAt.desc.v1", typeof(AuthAdminUsersReadJsonContext),
    RequiredGrantId = "auth.admin.read", Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.adminUsers.createdAt.desc.v1.row.email", "auth.read.adminUsers.createdAt.desc.v1.row.firstName", "auth.read.adminUsers.createdAt.desc.v1.row.lastName", "auth.read.adminUsers.createdAt.desc.v1.row.displayName", "auth.read.adminUsers.createdAt.desc.v1.row.lastLoginAt", "auth.read.adminUsers.createdAt.desc.v1.row.lastLoginIp", "auth.read.adminUsers.createdAt.desc.v1.row.userMetadata", "auth.read.adminUsers.createdAt.desc.v1.row.appMetadata"],
    SystemSourceIds = ["auth.users", "auth.userRoles"])]
internal sealed partial record AuthAdminUsersCreatedAtDescReadV1
{
    [BaseReadParameter("auth.read.adminUsers.parameter.tenantId")] public required Guid TenantId { get; init; }
    [BaseReadParameter("auth.read.adminUsers.parameter.applySearch")] public required bool ApplySearch { get; init; }
    [BaseReadParameter("auth.read.adminUsers.parameter.search")] public required string Search { get; init; }
    [BaseReadParameter("auth.read.adminUsers.parameter.applyEmail")] public required bool ApplyEmail { get; init; }
    [BaseReadParameter("auth.read.adminUsers.parameter.email")] public required string Email { get; init; }
    [BaseReadParameter("auth.read.adminUsers.parameter.applyEmailVerified")] public required bool ApplyEmailVerified { get; init; }
    [BaseReadParameter("auth.read.adminUsers.parameter.emailVerified")] public required bool EmailVerified { get; init; }
    [BaseReadParameter("auth.read.adminUsers.parameter.applyEnabled")] public required bool ApplyEnabled { get; init; }
    [BaseReadParameter("auth.read.adminUsers.parameter.enabled")] public required bool Enabled { get; init; }
    [BaseReadParameter("auth.read.adminUsers.parameter.applyRole")] public required bool ApplyRole { get; init; }
    [BaseReadParameter("auth.read.adminUsers.parameter.roleId")] public required Guid RoleId { get; init; }

    public sealed partial record Row : IAuthAdminUserReadRow
    {
        [BaseReadField("auth.read.adminUsers.createdAt.desc.v1.row.id")] public required Guid Id { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.desc.v1.row.email")] public string? Email { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.desc.v1.row.emailConfirmed")] public required bool EmailConfirmed { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.desc.v1.row.firstName")] public string? FirstName { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.desc.v1.row.lastName")] public string? LastName { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.desc.v1.row.displayName")] public string? DisplayName { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.desc.v1.row.subscriptionTier")] public required string SubscriptionTier { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.desc.v1.row.isActive")] public required bool IsActive { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.desc.v1.row.isDeleted")] public required bool IsDeleted { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.desc.v1.row.lastLoginAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LastLoginAt { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.desc.v1.row.lastLoginIp")] public string? LastLoginIp { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.desc.v1.row.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.desc.v1.row.userMetadata")] public required BaseCanonicalJson UserMetadata { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.desc.v1.row.appMetadata")] public required BaseCanonicalJson AppMetadata { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.desc.v1.row.requiredActions")] public required BaseCanonicalJson RequiredActions { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.desc.v1.row.lockoutEnd"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LockoutEnd { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthAdminUsersCreatedAtDescReadV1, Row> read) =>
        AuthAdminUserReadDefinition.Configure(read, new()
        {
            TenantId = Parameters.TenantId, ApplySearch = Parameters.ApplySearch, Search = Parameters.Search,
            ApplyEmail = Parameters.ApplyEmail, Email = Parameters.Email,
            ApplyEmailVerified = Parameters.ApplyEmailVerified, EmailVerified = Parameters.EmailVerified,
            ApplyEnabled = Parameters.ApplyEnabled, Enabled = Parameters.Enabled,
            ApplyRole = Parameters.ApplyRole, RoleId = Parameters.RoleId,
        }, new()
        {
            Id = Row.Fields.Id, Email = Row.Fields.Email, EmailConfirmed = Row.Fields.EmailConfirmed,
            FirstName = Row.Fields.FirstName, LastName = Row.Fields.LastName, DisplayName = Row.Fields.DisplayName,
            SubscriptionTier = Row.Fields.SubscriptionTier, IsActive = Row.Fields.IsActive, IsDeleted = Row.Fields.IsDeleted,
            LastLoginAt = Row.Fields.LastLoginAt, LastLoginIp = Row.Fields.LastLoginIp, CreatedAt = Row.Fields.CreatedAt,
            UserMetadata = Row.Fields.UserMetadata, AppMetadata = Row.Fields.AppMetadata,
            RequiredActions = Row.Fields.RequiredActions, LockoutEnd = Row.Fields.LockoutEnd,
        }, AuthAdminUserReadSort.CreatedAt, QuerySortDirection.Desc);
}

[JsonSerializable(typeof(AuthAdminUsersCreatedAtDescReadV1))]
[JsonSerializable(typeof(AuthAdminUsersCreatedAtDescReadV1.Row), TypeInfoPropertyName = "AuthAdminUsersCreatedAtDescReadV1Row")]
[JsonSerializable(typeof(AuthAdminUsersCreatedAtAscReadV1))]
[JsonSerializable(typeof(AuthAdminUsersCreatedAtAscReadV1.Row), TypeInfoPropertyName = "AuthAdminUsersCreatedAtAscReadV1Row")]
[JsonSerializable(typeof(AuthAdminUsersEmailAscReadV1))]
[JsonSerializable(typeof(AuthAdminUsersEmailAscReadV1.Row), TypeInfoPropertyName = "AuthAdminUsersEmailAscReadV1Row")]
[JsonSerializable(typeof(AuthAdminUsersEmailDescReadV1))]
[JsonSerializable(typeof(AuthAdminUsersEmailDescReadV1.Row), TypeInfoPropertyName = "AuthAdminUsersEmailDescReadV1Row")]
[JsonSerializable(typeof(AuthAdminUsersLastLoginAtAscReadV1))]
[JsonSerializable(typeof(AuthAdminUsersLastLoginAtAscReadV1.Row), TypeInfoPropertyName = "AuthAdminUsersLastLoginAtAscReadV1Row")]
[JsonSerializable(typeof(AuthAdminUsersLastLoginAtDescReadV1))]
[JsonSerializable(typeof(AuthAdminUsersLastLoginAtDescReadV1.Row), TypeInfoPropertyName = "AuthAdminUsersLastLoginAtDescReadV1Row")]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal sealed partial class AuthAdminUsersReadJsonContext : JsonSerializerContext;
