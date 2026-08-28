using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

[BaseRead("auth.read.adminUsers.createdAt.asc.v1", typeof(AuthAdminUsersReadJsonContext),
    RequiredGrantId = "auth.admin.read", Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.adminUsers.createdAt.asc.v1.row.email", "auth.read.adminUsers.createdAt.asc.v1.row.firstName", "auth.read.adminUsers.createdAt.asc.v1.row.lastName", "auth.read.adminUsers.createdAt.asc.v1.row.displayName", "auth.read.adminUsers.createdAt.asc.v1.row.lastLoginAt", "auth.read.adminUsers.createdAt.asc.v1.row.lastLoginIp", "auth.read.adminUsers.createdAt.asc.v1.row.userMetadata", "auth.read.adminUsers.createdAt.asc.v1.row.appMetadata"],
    SystemSourceIds = ["auth.users", "auth.userRoles"])]
internal sealed partial record AuthAdminUsersCreatedAtAscReadV1
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
        [BaseReadField("auth.read.adminUsers.createdAt.asc.v1.row.id")] public required Guid Id { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.asc.v1.row.email")] public string? Email { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.asc.v1.row.emailConfirmed")] public required bool EmailConfirmed { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.asc.v1.row.firstName")] public string? FirstName { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.asc.v1.row.lastName")] public string? LastName { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.asc.v1.row.displayName")] public string? DisplayName { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.asc.v1.row.subscriptionTier")] public required string SubscriptionTier { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.asc.v1.row.isActive")] public required bool IsActive { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.asc.v1.row.isDeleted")] public required bool IsDeleted { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.asc.v1.row.lastLoginAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LastLoginAt { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.asc.v1.row.lastLoginIp")] public string? LastLoginIp { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.asc.v1.row.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.asc.v1.row.userMetadata")] public required BaseCanonicalJson UserMetadata { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.asc.v1.row.appMetadata")] public required BaseCanonicalJson AppMetadata { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.asc.v1.row.requiredActions")] public required BaseCanonicalJson RequiredActions { get; init; }
        [BaseReadField("auth.read.adminUsers.createdAt.asc.v1.row.lockoutEnd"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LockoutEnd { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthAdminUsersCreatedAtAscReadV1, Row> read) =>
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
        }, AuthAdminUserReadSort.CreatedAt, QuerySortDirection.Asc);
}
[BaseRead("auth.read.adminUsers.email.asc.v1", typeof(AuthAdminUsersReadJsonContext),
    RequiredGrantId = "auth.admin.read", Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.adminUsers.email.asc.v1.row.email", "auth.read.adminUsers.email.asc.v1.row.firstName", "auth.read.adminUsers.email.asc.v1.row.lastName", "auth.read.adminUsers.email.asc.v1.row.displayName", "auth.read.adminUsers.email.asc.v1.row.lastLoginAt", "auth.read.adminUsers.email.asc.v1.row.lastLoginIp", "auth.read.adminUsers.email.asc.v1.row.userMetadata", "auth.read.adminUsers.email.asc.v1.row.appMetadata"],
    SystemSourceIds = ["auth.users", "auth.userRoles"])]
internal sealed partial record AuthAdminUsersEmailAscReadV1
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
        [BaseReadField("auth.read.adminUsers.email.asc.v1.row.id")] public required Guid Id { get; init; }
        [BaseReadField("auth.read.adminUsers.email.asc.v1.row.email")] public string? Email { get; init; }
        [BaseReadField("auth.read.adminUsers.email.asc.v1.row.emailConfirmed")] public required bool EmailConfirmed { get; init; }
        [BaseReadField("auth.read.adminUsers.email.asc.v1.row.firstName")] public string? FirstName { get; init; }
        [BaseReadField("auth.read.adminUsers.email.asc.v1.row.lastName")] public string? LastName { get; init; }
        [BaseReadField("auth.read.adminUsers.email.asc.v1.row.displayName")] public string? DisplayName { get; init; }
        [BaseReadField("auth.read.adminUsers.email.asc.v1.row.subscriptionTier")] public required string SubscriptionTier { get; init; }
        [BaseReadField("auth.read.adminUsers.email.asc.v1.row.isActive")] public required bool IsActive { get; init; }
        [BaseReadField("auth.read.adminUsers.email.asc.v1.row.isDeleted")] public required bool IsDeleted { get; init; }
        [BaseReadField("auth.read.adminUsers.email.asc.v1.row.lastLoginAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LastLoginAt { get; init; }
        [BaseReadField("auth.read.adminUsers.email.asc.v1.row.lastLoginIp")] public string? LastLoginIp { get; init; }
        [BaseReadField("auth.read.adminUsers.email.asc.v1.row.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
        [BaseReadField("auth.read.adminUsers.email.asc.v1.row.userMetadata")] public required BaseCanonicalJson UserMetadata { get; init; }
        [BaseReadField("auth.read.adminUsers.email.asc.v1.row.appMetadata")] public required BaseCanonicalJson AppMetadata { get; init; }
        [BaseReadField("auth.read.adminUsers.email.asc.v1.row.requiredActions")] public required BaseCanonicalJson RequiredActions { get; init; }
        [BaseReadField("auth.read.adminUsers.email.asc.v1.row.lockoutEnd"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LockoutEnd { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthAdminUsersEmailAscReadV1, Row> read) =>
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
        }, AuthAdminUserReadSort.Email, QuerySortDirection.Asc);
}

[BaseRead("auth.read.adminUsers.email.desc.v1", typeof(AuthAdminUsersReadJsonContext),
    RequiredGrantId = "auth.admin.read", Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.adminUsers.email.desc.v1.row.email", "auth.read.adminUsers.email.desc.v1.row.firstName", "auth.read.adminUsers.email.desc.v1.row.lastName", "auth.read.adminUsers.email.desc.v1.row.displayName", "auth.read.adminUsers.email.desc.v1.row.lastLoginAt", "auth.read.adminUsers.email.desc.v1.row.lastLoginIp", "auth.read.adminUsers.email.desc.v1.row.userMetadata", "auth.read.adminUsers.email.desc.v1.row.appMetadata"],
    SystemSourceIds = ["auth.users", "auth.userRoles"])]
internal sealed partial record AuthAdminUsersEmailDescReadV1
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
        [BaseReadField("auth.read.adminUsers.email.desc.v1.row.id")] public required Guid Id { get; init; }
        [BaseReadField("auth.read.adminUsers.email.desc.v1.row.email")] public string? Email { get; init; }
        [BaseReadField("auth.read.adminUsers.email.desc.v1.row.emailConfirmed")] public required bool EmailConfirmed { get; init; }
        [BaseReadField("auth.read.adminUsers.email.desc.v1.row.firstName")] public string? FirstName { get; init; }
        [BaseReadField("auth.read.adminUsers.email.desc.v1.row.lastName")] public string? LastName { get; init; }
        [BaseReadField("auth.read.adminUsers.email.desc.v1.row.displayName")] public string? DisplayName { get; init; }
        [BaseReadField("auth.read.adminUsers.email.desc.v1.row.subscriptionTier")] public required string SubscriptionTier { get; init; }
        [BaseReadField("auth.read.adminUsers.email.desc.v1.row.isActive")] public required bool IsActive { get; init; }
        [BaseReadField("auth.read.adminUsers.email.desc.v1.row.isDeleted")] public required bool IsDeleted { get; init; }
        [BaseReadField("auth.read.adminUsers.email.desc.v1.row.lastLoginAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LastLoginAt { get; init; }
        [BaseReadField("auth.read.adminUsers.email.desc.v1.row.lastLoginIp")] public string? LastLoginIp { get; init; }
        [BaseReadField("auth.read.adminUsers.email.desc.v1.row.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
        [BaseReadField("auth.read.adminUsers.email.desc.v1.row.userMetadata")] public required BaseCanonicalJson UserMetadata { get; init; }
        [BaseReadField("auth.read.adminUsers.email.desc.v1.row.appMetadata")] public required BaseCanonicalJson AppMetadata { get; init; }
        [BaseReadField("auth.read.adminUsers.email.desc.v1.row.requiredActions")] public required BaseCanonicalJson RequiredActions { get; init; }
        [BaseReadField("auth.read.adminUsers.email.desc.v1.row.lockoutEnd"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LockoutEnd { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthAdminUsersEmailDescReadV1, Row> read) =>
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
        }, AuthAdminUserReadSort.Email, QuerySortDirection.Desc);
}

[BaseRead("auth.read.adminUsers.lastLoginAt.asc.v1", typeof(AuthAdminUsersReadJsonContext),
    RequiredGrantId = "auth.admin.read", Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.adminUsers.lastLoginAt.asc.v1.row.email", "auth.read.adminUsers.lastLoginAt.asc.v1.row.firstName", "auth.read.adminUsers.lastLoginAt.asc.v1.row.lastName", "auth.read.adminUsers.lastLoginAt.asc.v1.row.displayName", "auth.read.adminUsers.lastLoginAt.asc.v1.row.lastLoginAt", "auth.read.adminUsers.lastLoginAt.asc.v1.row.lastLoginIp", "auth.read.adminUsers.lastLoginAt.asc.v1.row.userMetadata", "auth.read.adminUsers.lastLoginAt.asc.v1.row.appMetadata"],
    SystemSourceIds = ["auth.users", "auth.userRoles"])]
internal sealed partial record AuthAdminUsersLastLoginAtAscReadV1
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
        [BaseReadField("auth.read.adminUsers.lastLoginAt.asc.v1.row.id")] public required Guid Id { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.asc.v1.row.email")] public string? Email { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.asc.v1.row.emailConfirmed")] public required bool EmailConfirmed { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.asc.v1.row.firstName")] public string? FirstName { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.asc.v1.row.lastName")] public string? LastName { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.asc.v1.row.displayName")] public string? DisplayName { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.asc.v1.row.subscriptionTier")] public required string SubscriptionTier { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.asc.v1.row.isActive")] public required bool IsActive { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.asc.v1.row.isDeleted")] public required bool IsDeleted { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.asc.v1.row.lastLoginAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LastLoginAt { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.asc.v1.row.lastLoginIp")] public string? LastLoginIp { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.asc.v1.row.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.asc.v1.row.userMetadata")] public required BaseCanonicalJson UserMetadata { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.asc.v1.row.appMetadata")] public required BaseCanonicalJson AppMetadata { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.asc.v1.row.requiredActions")] public required BaseCanonicalJson RequiredActions { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.asc.v1.row.lockoutEnd"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LockoutEnd { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthAdminUsersLastLoginAtAscReadV1, Row> read) =>
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
        }, AuthAdminUserReadSort.LastLoginAt, QuerySortDirection.Asc);
}

[BaseRead("auth.read.adminUsers.lastLoginAt.desc.v1", typeof(AuthAdminUsersReadJsonContext),
    RequiredGrantId = "auth.admin.read", Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    ConfidentialOutputFieldIds = ["auth.read.adminUsers.lastLoginAt.desc.v1.row.email", "auth.read.adminUsers.lastLoginAt.desc.v1.row.firstName", "auth.read.adminUsers.lastLoginAt.desc.v1.row.lastName", "auth.read.adminUsers.lastLoginAt.desc.v1.row.displayName", "auth.read.adminUsers.lastLoginAt.desc.v1.row.lastLoginAt", "auth.read.adminUsers.lastLoginAt.desc.v1.row.lastLoginIp", "auth.read.adminUsers.lastLoginAt.desc.v1.row.userMetadata", "auth.read.adminUsers.lastLoginAt.desc.v1.row.appMetadata"],
    SystemSourceIds = ["auth.users", "auth.userRoles"])]
internal sealed partial record AuthAdminUsersLastLoginAtDescReadV1
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
        [BaseReadField("auth.read.adminUsers.lastLoginAt.desc.v1.row.id")] public required Guid Id { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.desc.v1.row.email")] public string? Email { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.desc.v1.row.emailConfirmed")] public required bool EmailConfirmed { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.desc.v1.row.firstName")] public string? FirstName { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.desc.v1.row.lastName")] public string? LastName { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.desc.v1.row.displayName")] public string? DisplayName { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.desc.v1.row.subscriptionTier")] public required string SubscriptionTier { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.desc.v1.row.isActive")] public required bool IsActive { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.desc.v1.row.isDeleted")] public required bool IsDeleted { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.desc.v1.row.lastLoginAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LastLoginAt { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.desc.v1.row.lastLoginIp")] public string? LastLoginIp { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.desc.v1.row.createdAt"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset CreatedAt { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.desc.v1.row.userMetadata")] public required BaseCanonicalJson UserMetadata { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.desc.v1.row.appMetadata")] public required BaseCanonicalJson AppMetadata { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.desc.v1.row.requiredActions")] public required BaseCanonicalJson RequiredActions { get; init; }
        [BaseReadField("auth.read.adminUsers.lastLoginAt.desc.v1.row.lockoutEnd"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? LockoutEnd { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<AuthAdminUsersLastLoginAtDescReadV1, Row> read) =>
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
        }, AuthAdminUserReadSort.LastLoginAt, QuerySortDirection.Desc);
}
