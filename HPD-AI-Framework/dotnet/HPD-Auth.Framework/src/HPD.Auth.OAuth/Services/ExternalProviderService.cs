using System.Security.Claims;
using HPD.Auth.Core.Entities;

namespace HPD.Auth.OAuth.Services;

/// <summary>
/// Maps provider-specific OAuth claim types to normalised, application-level values.
///
/// <para>
/// Different OAuth providers use different claim names and payload shapes for the same
/// conceptual data. For example, a user's avatar is <c>"picture"</c> on Google,
/// <c>"urn:github:avatar"</c> on GitHub (via the AspNet.Security.OAuth.GitHub
/// ClaimActions mapping), and assembled as a CDN URL on Discord. This service
/// encapsulates all of those provider-specific rules behind a single, stable API that
/// the rest of the OAuth package (and host applications) can depend on.
/// </para>
///
/// <para>
/// Provider avatar claim mapping:
/// <list type="table">
///   <listheader><term>Provider</term><description>Claim key(s) checked</description></listheader>
///   <item><term>Google</term>      <description>"picture" (standard OIDC)</description></item>
///   <item><term>GitHub</term>      <description>"urn:github:avatar" (AspNet.Security.OAuth.GitHub)</description></item>
///   <item><term>Microsoft</term>   <description>Not available in claims (requires Graph API call)</description></item>
///   <item><term>Discord</term>     <description>"avatar_url" (assembled in OnCreatingTicket)</description></item>
///   <item><term>Twitter/X</term>   <description>"avatar_url" (assembled in OnCreatingTicket)</description></item>
///   <item><term>Apple</term>       <description>Not available</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class ExternalProviderService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Canonical provider scheme names
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Google authentication scheme name.</summary>
    public const string Google = "Google";

    /// <summary>GitHub authentication scheme name (AspNet.Security.OAuth.GitHub).</summary>
    public const string GitHub = "GitHub";

    /// <summary>Microsoft Account authentication scheme name.</summary>
    public const string Microsoft = "Microsoft";

    /// <summary>Apple Sign-In authentication scheme name.</summary>
    public const string Apple = "Apple";

    /// <summary>Discord authentication scheme name.</summary>
    public const string Discord = "Discord";

    /// <summary>Twitter/X authentication scheme name.</summary>
    public const string Twitter = "Twitter";

    /// <summary>
    /// Canonical set of supported provider scheme names for validation.
    /// Comparison is case-insensitive so URL path segments like "google" and "Google"
    /// are both accepted.
    /// </summary>
    public static readonly IReadOnlySet<string> SupportedProviders =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Google,
            GitHub,
            Microsoft,
            Apple,
            Discord,
            Twitter,
        };

    // ─────────────────────────────────────────────────────────────────────────
    // GetEmail
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the user's email address from the provider's <see cref="ClaimsPrincipal"/>.
    ///
    /// <para>
    /// Checks <see cref="ClaimTypes.Email"/> first (the standard ASP.NET Core mapping),
    /// then the bare <c>"email"</c> claim name used by some providers.
    /// </para>
    ///
    /// <para>
    /// May return <c>null</c> for GitHub users who have set their email to private
    /// and have not granted the <c>user:email</c> scope.
    /// </para>
    /// </summary>
    public string? GetEmail(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue("email");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetAvatarUrl
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the URL of the user's profile picture for the given provider.
    ///
    /// <para>
    /// Returns <c>null</c> for Microsoft (avatar requires a separate Microsoft Graph API
    /// call that is performed in the <c>OnCreatingTicket</c> event handler, not stored as a
    /// standard claim) and Apple (does not provide avatars).
    /// </para>
    /// </summary>
    /// <param name="principal">The <see cref="ClaimsPrincipal"/> returned by the provider.</param>
    /// <param name="provider">
    /// The authentication scheme name (e.g., "Google", "GitHub").
    /// Case-insensitive.
    /// </param>
    public string? GetAvatarUrl(ClaimsPrincipal principal, string provider)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrEmpty(provider);

        return provider.ToLowerInvariant() switch
        {
            // Google maps "picture" via ClaimActions.MapJsonKey("picture", "picture")
            // in the provider configuration.
            "google" => principal.FindFirstValue("picture"),

            // AspNet.Security.OAuth.GitHub maps the avatar_url field to the
            // "urn:github:avatar" claim type via its built-in ClaimActions.
            "github" => principal.FindFirstValue("urn:github:avatar"),

            // Microsoft does not expose a photo claim; the avatar must be fetched
            // separately from https://graph.microsoft.com/v1.0/me/photo/$value
            // and is injected as "avatar_url" in OnCreatingTicket by the
            // OAuthSchemeRegistrar configuration.  Fall through to the generic path.
            "microsoft" => principal.FindFirstValue("avatar_url"),

            // Discord and Twitter assemble a CDN URL in OnCreatingTicket and add it
            // as an "avatar_url" claim.
            "discord" => principal.FindFirstValue("avatar_url"),
            "twitter" => principal.FindFirstValue("avatar_url"),

            // Apple does not provide an avatar image.
            "apple" => null,

            // Unknown provider — attempt the two most common claim names.
            _ => principal.FindFirstValue("avatar_url")
              ?? principal.FindFirstValue("picture"),
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetDisplayName
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the user's display name from the provider's claims.
    ///
    /// <para>
    /// Priority order:
    /// <list type="number">
    ///   <item><c>"name"</c> — the full display name used by Google and Discord.</item>
    ///   <item><see cref="ClaimTypes.Name"/> — the standard ASP.NET Core full-name claim.</item>
    ///   <item><see cref="ClaimTypes.GivenName"/> — first name only, used as a last resort.</item>
    /// </list>
    /// </para>
    /// </summary>
    public string? GetDisplayName(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal.FindFirstValue("name")
            ?? principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue(ClaimTypes.GivenName);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MapClaimsToUser
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies provider claims to the mutable <paramref name="user"/> object,
    /// filling in profile fields that are currently null or empty.
    ///
    /// <para>
    /// This method is intentionally non-destructive: existing non-empty values are
    /// never overwritten, so user-edited profile data is preserved across subsequent
    /// OAuth sign-ins.
    /// </para>
    /// </summary>
    /// <param name="user">The user entity to update.</param>
    /// <param name="principal">The <see cref="ClaimsPrincipal"/> returned by the provider.</param>
    /// <param name="provider">
    /// The authentication scheme name (e.g., "Google", "GitHub"). Used for
    /// provider-specific avatar resolution via <see cref="GetAvatarUrl"/>.
    /// </param>
    public void MapClaimsToUser(
        ApplicationUser user,
        ClaimsPrincipal principal,
        string provider)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrEmpty(provider);

        if (string.IsNullOrEmpty(user.FirstName))
        {
            user.FirstName = principal.FindFirstValue(ClaimTypes.GivenName)
                          ?? principal.FindFirstValue("first_name");
        }

        if (string.IsNullOrEmpty(user.LastName))
        {
            user.LastName = principal.FindFirstValue(ClaimTypes.Surname)
                         ?? principal.FindFirstValue("last_name");
        }

        if (string.IsNullOrEmpty(user.DisplayName))
        {
            user.DisplayName = GetDisplayName(principal) ?? user.Email;
        }

        if (string.IsNullOrEmpty(user.AvatarUrl))
        {
            user.AvatarUrl = GetAvatarUrl(principal, provider);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NormalizeProviderName
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a case-insensitive URL path segment (e.g., <c>"google"</c>) to the
    /// canonical authentication scheme name (e.g., <c>"Google"</c>).
    ///
    /// <para>
    /// Returns <c>null</c> when the segment does not match any supported provider,
    /// which lets endpoints return a 400 Bad Request instead of issuing an
    /// invalid challenge.
    /// </para>
    /// </summary>
    public string? NormalizeProviderName(string? rawName)
    {
        if (string.IsNullOrEmpty(rawName))
            return null;

        foreach (var provider in SupportedProviders)
        {
            if (provider.Equals(rawName, StringComparison.OrdinalIgnoreCase))
                return provider;
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IsProviderSupported
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when <paramref name="providerName"/> matches one of the
    /// built-in supported scheme names (case-insensitive).
    /// </summary>
    public bool IsProviderSupported(string? providerName) =>
        !string.IsNullOrEmpty(providerName) &&
        SupportedProviders.Contains(providerName);
}
