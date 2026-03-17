namespace HPD.Auth.Admin.Models;

/// <summary>
/// Response for POST /api/admin/generate-link.
/// </summary>
public record AdminGenerateLinkResponse(
    /// <summary>
    /// The full action link that can be sent to the user.
    /// Format: {RedirectTo}?token={token}&userId={userId}&type={type}
    /// </summary>
    string ActionLink,

    /// <summary>
    /// A SHA-256 hash of the token for server-side verification records.
    /// The raw token is NOT returned here for security — only the link contains it.
    /// </summary>
    string HashedToken,

    /// <summary>The type of action this link performs (mirrors the request type).</summary>
    string VerificationType,

    /// <summary>The redirect base URL (mirrors the request value, or empty string if none provided).</summary>
    string RedirectTo
);
