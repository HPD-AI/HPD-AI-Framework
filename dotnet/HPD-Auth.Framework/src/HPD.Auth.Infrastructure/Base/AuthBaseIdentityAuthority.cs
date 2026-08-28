using HPD.Auth.Core.Entities;
using HPD.Auth.Base;
using HPD.Base;

namespace HPD.Auth.Infrastructure.Base;

[Flags]
internal enum AuthUserDirtyFields
{
    None = 0,
    Profile = 1 << 0,
    EmailConfirmation = 1 << 1,
    PhoneConfirmation = 1 << 2,
    PasswordHash = 1 << 3,
    SecurityStamp = 1 << 4,
    SecurityState = 1 << 5,
    AuthenticatorKey = 1 << 6,
}

internal sealed class AuthUserAuthorityLease
{
    internal required Guid TenantId { get; init; }
    internal required Guid RecordId { get; init; }
    internal required RevisionToken Revision { get; set; }
    internal required BaseRegisteredReadSnapshotAuthority Authority { get; init; }
    internal required AuthUserOrdinarySnapshot Snapshot { get; set; }
    internal string? PasswordHash { get; set; }
    internal string? SecurityStamp { get; set; }
    internal string? AuthenticatorKey { get; set; }
    internal AuthUserDirtyFields DirtyFields { get; set; }
    internal AuthIdentityPendingAttempt? PendingAttempt { get; set; }
    internal bool Consumed { get; set; }
}

internal sealed class AuthRoleAuthorityLease
{
    internal required Guid TenantId { get; init; }
    internal required Guid RecordId { get; init; }
    internal required RevisionToken Revision { get; set; }
    internal required BaseRegisteredReadSnapshotAuthority Authority { get; init; }
    internal required AuthRoleOrdinarySnapshot Snapshot { get; set; }
    internal AuthIdentityPendingAttempt? PendingAttempt { get; set; }
    internal bool Consumed { get; set; }
}

internal sealed record AuthIdentityPendingAttempt
{
    internal required string OperationId { get; init; }
    internal required string IdempotencyKey { get; init; }
    internal required string NextConcurrencyStamp { get; init; }
    internal required DateTimeOffset OperationTime { get; init; }
}

internal sealed record AuthUserOrdinarySnapshot
{
    internal required Guid Id { get; init; }
    internal required Guid TenantId { get; init; }
    internal string? UserName { get; init; }
    internal string? NormalizedUserName { get; init; }
    internal string? Email { get; init; }
    internal string? NormalizedEmail { get; init; }
    internal required bool EmailConfirmed { get; init; }
    internal required string ConcurrencyStamp { get; init; }
    internal string? PhoneNumber { get; init; }
    internal required bool PhoneNumberConfirmed { get; init; }
    internal required bool TwoFactorEnabled { get; init; }
    internal DateTimeOffset? LockoutEnd { get; init; }
    internal required bool LockoutEnabled { get; init; }
    internal required int AccessFailedCount { get; init; }
    internal string? DisplayName { get; init; }
    internal string? FirstName { get; init; }
    internal string? LastName { get; init; }
    internal string? AvatarUrl { get; init; }

    internal static AuthUserOrdinarySnapshot Capture(ApplicationUser user) => new()
    {
        Id = user.Id,
        TenantId = user.InstanceId,
        UserName = user.UserName,
        NormalizedUserName = user.NormalizedUserName,
        Email = user.Email,
        NormalizedEmail = user.NormalizedEmail,
        EmailConfirmed = user.EmailConfirmed,
        ConcurrencyStamp = user.ConcurrencyStamp ?? string.Empty,
        PhoneNumber = user.PhoneNumber,
        PhoneNumberConfirmed = user.PhoneNumberConfirmed,
        TwoFactorEnabled = user.TwoFactorEnabled,
        LockoutEnd = user.LockoutEnd,
        LockoutEnabled = user.LockoutEnabled,
        AccessFailedCount = user.AccessFailedCount,
        DisplayName = user.DisplayName,
        FirstName = user.FirstName,
        LastName = user.LastName,
        AvatarUrl = user.AvatarUrl,
    };
}

internal sealed record AuthRoleOrdinarySnapshot
{
    internal required Guid Id { get; init; }
    internal required Guid TenantId { get; init; }
    internal string? Name { get; init; }
    internal string? NormalizedName { get; init; }
    internal required string ConcurrencyStamp { get; init; }
    internal string? Description { get; init; }

    internal static AuthRoleOrdinarySnapshot Capture(ApplicationRole role) => new()
    {
        Id = role.Id,
        TenantId = role.InstanceId,
        Name = role.Name,
        NormalizedName = role.NormalizedName,
        ConcurrencyStamp = role.ConcurrencyStamp ?? string.Empty,
        Description = role.Description,
    };
}
