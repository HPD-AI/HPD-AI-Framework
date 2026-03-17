namespace HPD.Auth.Admin.Models;

/// <summary>
/// Full admin view of a user, including roles, metadata, and lockout state.
/// </summary>
public record AdminUserResponse(
    Guid Id,
    string Email,
    bool EmailConfirmed,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string SubscriptionTier,
    bool IsActive,
    bool IsDeleted,
    DateTime? LastLoginAt,
    string? LastLoginIp,
    DateTime Created,
    IList<string> Roles,
    string UserMetadata,
    string AppMetadata,
    List<string> RequiredActions,
    bool IsLockedOut,
    DateTimeOffset? LockoutEnd
);
