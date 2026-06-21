namespace HPD.Auth.Admin.Models;

/// <summary>
/// Request body for POST /api/admin/generate-link.
/// </summary>
public record AdminGenerateLinkRequest(
    /// <summary>
    /// Link type. Valid values:
    ///   "signup"       — New account confirmation link
    ///   "invite"       — Invite link (same as signup, pre-confirmed)
    ///   "magiclink"    — Passwordless sign-in link
    ///   "recovery"     — Password recovery / reset link
    ///   "verify_email" — Email verification link for existing user
    /// </summary>
    string Type,

    /// <summary>The email address of the user to generate the link for.</summary>
    string Email,

    /// <summary>
    /// Optional base URL to redirect to after the action.
    /// The generated token and userId are appended as query parameters.
    /// </summary>
    string? RedirectTo = null
);
