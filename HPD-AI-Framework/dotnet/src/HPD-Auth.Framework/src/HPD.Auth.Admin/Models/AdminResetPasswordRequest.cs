namespace HPD.Auth.Admin.Models;

/// <summary>
/// Request body for POST /api/admin/users/{id}/reset-password.
/// No current password is required — admin authority is sufficient.
/// </summary>
public record AdminResetPasswordRequest(
    /// <summary>The new password to set.</summary>
    string Password,

    /// <summary>
    /// If true, the user is forced to change their password on next login
    /// by adding "UPDATE_PASSWORD" to their RequiredActions list.
    /// </summary>
    bool Temporary = false
);
