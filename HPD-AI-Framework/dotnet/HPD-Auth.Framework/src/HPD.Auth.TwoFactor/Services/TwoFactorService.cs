using System.Text;

namespace HPD.Auth.TwoFactor.Services;

/// <summary>
/// Helper service for TOTP authenticator setup.
///
/// <para>
/// Provides key formatting (groups of 4 for human readability) and
/// <c>otpauth://</c> URI generation for QR-code based authenticator enrollment.
/// The URI format follows the Google Authenticator Key URI specification:
/// https://github.com/google/google-authenticator/wiki/Key-Uri-Format
/// </para>
///
/// <para>
/// This service is stateless — all methods are pure functions of their inputs.
/// Register it with <see cref="Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped{TService}"/>.
/// </para>
/// </summary>
public class TwoFactorService
{
    // otpauth://totp/{issuer}:{account}?secret={secret}&issuer={issuer}&digits=6
    private const string AuthenticatorUriFormat =
        "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";

    /// <summary>
    /// Formats a raw authenticator key into space-separated groups of 4 characters
    /// for easier manual entry by the user.
    ///
    /// <example>
    /// Input:  "JBSWY3DPEHPK3PXP"
    /// Output: "jbsw y3dp ehpk 3pxp"
    /// </example>
    /// </summary>
    /// <param name="unformattedKey">The raw base32 key returned by UserManager.GetAuthenticatorKeyAsync.</param>
    /// <returns>The key formatted in lowercase groups of 4, separated by spaces.</returns>
    public string FormatAuthenticatorKey(string unformattedKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(unformattedKey);

        var result = new StringBuilder();
        int pos = 0;

        while (pos + 4 < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(pos, 4)).Append(' ');
            pos += 4;
        }

        if (pos < unformattedKey.Length)
            result.Append(unformattedKey.AsSpan(pos));

        return result.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// Generates an <c>otpauth://totp/</c> URI that can be encoded into a QR code
    /// for scanning by any RFC 6238 / TOTP authenticator app.
    ///
    /// <para>
    /// Both <paramref name="issuer"/> and <paramref name="email"/> are percent-encoded
    /// using <see cref="Uri.EscapeDataString"/> to ensure the URI is valid even when
    /// those values contain special characters (spaces, @, ampersands, etc.).
    /// Note: <c>UrlEncoder.Default</c> follows HTML encoding rules and leaves <c>@</c>
    /// unencoded; the Key URI spec requires <c>@</c> → <c>%40</c>.
    /// </para>
    ///
    /// <para>
    /// Pass the <em>unformatted</em> key (not the formatted/grouped version) as
    /// <paramref name="unformattedKey"/>. The TOTP algorithm requires the raw
    /// base32 string without whitespace.
    /// </para>
    /// </summary>
    /// <param name="issuer">Application/issuer name shown in the authenticator app (e.g., "HPD").</param>
    /// <param name="email">Account identifier — typically the user's email address.</param>
    /// <param name="unformattedKey">Raw base32 TOTP shared secret from UserManager.</param>
    /// <returns>A valid <c>otpauth://totp/</c> URI string.</returns>
    public string GenerateAuthenticatorUri(string issuer, string email, string unformattedKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(issuer);
        ArgumentException.ThrowIfNullOrEmpty(email);
        ArgumentException.ThrowIfNullOrEmpty(unformattedKey);

        return string.Format(
            AuthenticatorUriFormat,
            Uri.EscapeDataString(issuer),
            Uri.EscapeDataString(email),
            unformattedKey);
    }
}
