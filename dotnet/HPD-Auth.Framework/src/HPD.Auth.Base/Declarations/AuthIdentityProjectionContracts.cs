using HPD.Base;

namespace HPD.Auth.Base;

internal interface IAuthUserIdentityProjectionV1
{
    Guid Id { get; }
    Guid TenantId { get; }
    string? UserName { get; }
    string? NormalizedUserName { get; }
    string? Email { get; }
    string? NormalizedEmail { get; }
    bool EmailConfirmed { get; }
    string ConcurrencyStamp { get; }
    string? PhoneNumber { get; }
    bool PhoneNumberConfirmed { get; }
    bool TwoFactorEnabled { get; }
    DateTimeOffset? LockoutEnd { get; }
    bool LockoutEnabled { get; }
    int AccessFailedCount { get; }
    string? Audience { get; }
    BaseCanonicalJson UserMetadata { get; }
    BaseCanonicalJson AppMetadata { get; }
    BaseCanonicalJson RequiredActions { get; }
    string? FirstName { get; }
    string? LastName { get; }
    string? DisplayName { get; }
    string? AvatarUrl { get; }
    bool IsActive { get; }
    bool IsDeleted { get; }
    DateTimeOffset? DeletedAt { get; }
    long TombstoneGeneration { get; }
    DateTimeOffset CreatedAt { get; }
    DateTimeOffset UpdatedAt { get; }
    DateTimeOffset? LastLoginAt { get; }
    string? LastLoginIp { get; }
    string SubscriptionTier { get; }
    DateTimeOffset? EmailConfirmedAt { get; }
    RevisionToken Revision { get; }
}

internal interface IAuthRoleIdentityProjectionV1
{
    Guid Id { get; }
    Guid TenantId { get; }
    string? Name { get; }
    string? NormalizedName { get; }
    string ConcurrencyStamp { get; }
    string? Description { get; }
    bool IsActive { get; }
    bool IsDeleted { get; }
    DateTimeOffset? DeletedAt { get; }
    long TombstoneGeneration { get; }
    DateTimeOffset CreatedAt { get; }
    DateTimeOffset UpdatedAt { get; }
    RevisionToken Revision { get; }
}

internal sealed partial record AuthUserByIdReadV1
{
    public sealed partial record Row : IAuthUserIdentityProjectionV1;
}

internal sealed partial record AuthUserByNormalizedNameReadV1
{
    public sealed partial record Row : IAuthUserIdentityProjectionV1;
}

internal sealed partial record AuthUserByNormalizedEmailReadV1
{
    public sealed partial record Row : IAuthUserIdentityProjectionV1;
}

internal sealed partial record AuthUsersInRoleReadV1
{
    public sealed partial record Row : IAuthUserIdentityProjectionV1;
}

internal sealed partial record AuthRoleByIdReadV1
{
    public sealed partial record Row : IAuthRoleIdentityProjectionV1;
}

internal sealed partial record AuthRoleByNormalizedNameReadV1
{
    public sealed partial record Row : IAuthRoleIdentityProjectionV1;
}
