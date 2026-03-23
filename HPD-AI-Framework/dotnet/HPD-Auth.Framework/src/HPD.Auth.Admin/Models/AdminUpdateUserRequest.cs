namespace HPD.Auth.Admin.Models;

/// <summary>
/// Request body for PUT /api/admin/users/{id}.
/// All fields are optional; only non-null fields are applied.
/// </summary>
public record AdminUpdateUserRequest(
    string? Email = null,
    bool? EmailConfirmed = null,
    string? FirstName = null,
    string? LastName = null,
    string? DisplayName = null,
    string? SubscriptionTier = null,
    bool? IsActive = null,
    string? UserMetadata = null,
    string? AppMetadata = null,
    List<string>? RequiredActions = null
);
