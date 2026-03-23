using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace HPD.Agent.Bots.AspNetCore.Verification;

/// <summary>
/// Static webhook signature verification. Called directly from generated dispatch
/// code — no virtual dispatch, fully AOT-compatible.
/// </summary>
public static class WebhookSignatureVerifier
{
    /// <summary>
    /// Verifies the webhook request signature.
    /// </summary>
    /// <param name="format">The HMAC signing format to use.</param>
    /// <param name="body">Raw request body bytes (read once before this call).</param>
    /// <param name="headers">HTTP request headers.</param>
    /// <param name="secret">Signing secret from adapter configuration.</param>
    /// <param name="signatureHeader">Header name carrying the signature.</param>
    /// <param name="timestampHeader">
    /// Header name carrying the request timestamp.
    /// Pass empty string to skip timestamp validation.
    /// </param>
    /// <param name="windowSeconds">Maximum age of the request in seconds.</param>
    /// <returns><c>true</c> if the signature is valid; <c>false</c> otherwise.</returns>
    public static bool Verify(
        HmacFormat format,
        byte[] body,
        IHeaderDictionary headers,
        string secret,
        string signatureHeader,
        string timestampHeader,
        int windowSeconds)
    {
        return format switch
        {
            HmacFormat.V0TimestampBody => VerifyV0TimestampBody(
                body, headers, secret, signatureHeader, timestampHeader, windowSeconds),
            _ => false,
        };
    }

    // ── V0TimestampBody (Slack-style) ─────────────────────────────────────────

    private static bool VerifyV0TimestampBody(
        byte[] body,
        IHeaderDictionary headers,
        string secret,
        string signatureHeader,
        string timestampHeader,
        int windowSeconds)
    {
        var signature = headers[signatureHeader].FirstOrDefault();
        if (string.IsNullOrEmpty(signature))
            return false;

        var timestamp = string.IsNullOrEmpty(timestampHeader)
            ? null
            : headers[timestampHeader].FirstOrDefault();

        // Replay-attack window check (only when timestamp header is configured)
        if (!string.IsNullOrEmpty(timestampHeader))
        {
            if (string.IsNullOrEmpty(timestamp) || !long.TryParse(timestamp, out var ts))
                return false;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (Math.Abs(now - ts) > windowSeconds)
                return false;
        }

        // Compute expected signature: v0=HMAC-SHA256("v0:{ts}:{body}")
        var sigBasestring = $"v0:{timestamp}:{Encoding.UTF8.GetString(body)}";
        using var hmac    = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash          = hmac.ComputeHash(Encoding.UTF8.GetBytes(sigBasestring));
        var expected      = "v0=" + Convert.ToHexString(hash).ToLowerInvariant();

        // Timing-safe comparison
        if (signature.Length != expected.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(signature),
            Encoding.UTF8.GetBytes(expected));
    }
}
