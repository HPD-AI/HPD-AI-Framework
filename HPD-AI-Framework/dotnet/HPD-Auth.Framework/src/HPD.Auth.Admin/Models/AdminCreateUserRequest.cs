namespace HPD.Auth.Admin.Models;

/// <summary>
/// Request body for POST /api/admin/users.
/// All fields except Email are optional.
/// </summary>
public record AdminCreateUserRequest(
    /// <summary>The user's email address (required).</summary>
    string Email,

    /// <summary>Optional password. If omitted, CreateAsync(user) is called (no password).</summary>
    string? Password = null,

    /// <summary>If true, auto-confirm the email via token immediately after creation.</summary>
    bool? EmailConfirm = null,

    /// <summary>Optional role to assign immediately after creation.</summary>
    string? Role = null,

    /// <summary>Optional first name.</summary>
    string? FirstName = null,

    /// <summary>Optional last name.</summary>
    string? LastName = null,

    /// <summary>Optional display name.</summary>
    string? DisplayName = null,

    /// <summary>Optional subscription tier (defaults to "free").</summary>
    string? SubscriptionTier = null,

    /// <summary>Optional user metadata JSON blob.</summary>
    string? UserMetadata = null,

    /// <summary>Optional app metadata JSON blob (admin-controlled).</summary>
    string? AppMetadata = null
);
