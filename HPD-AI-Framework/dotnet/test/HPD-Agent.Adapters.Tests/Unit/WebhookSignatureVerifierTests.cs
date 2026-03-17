using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using HPD.Agent.Adapters;
using HPD.Agent.Adapters.AspNetCore.Verification;
using Microsoft.AspNetCore.Http;

namespace HPD.Agent.Adapters.Tests.Unit;

/// <summary>
/// Tests for <see cref="WebhookSignatureVerifier.Verify"/>.
/// All tests use <see cref="HmacFormat.V0TimestampBody"/> (Slack-style).
/// </summary>
public class WebhookSignatureVerifierTests
{
    private const string Secret          = "test-signing-secret";
    private const string SigHeader       = "X-Slack-Signature";
    private const string TsHeader        = "X-Slack-Request-Timestamp";
    private const int    WindowSeconds   = 300;

    // ── Helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Computes the expected Slack-style v0 signature for the given body + timestamp.
    /// Mirrors exactly what WebhookSignatureVerifier does internally.
    /// </summary>
    private static string ComputeSignature(string body, string timestamp, string secret = Secret)
    {
        var basestring = $"v0:{timestamp}:{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash       = hmac.ComputeHash(Encoding.UTF8.GetBytes(basestring));
        return "v0=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Returns a fresh current Unix timestamp as a string.</summary>
    private static string NowTimestamp()
        => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

    private static IHeaderDictionary MakeHeaders(string? sig, string? ts)
    {
        var headers = new HeaderDictionary();
        if (sig is not null) headers[SigHeader] = sig;
        if (ts  is not null) headers[TsHeader]  = ts;
        return headers;
    }

    // ── Happy path ────────────────────────────────────────────────────

    [Fact]
    public void Verify_V0_ValidSignature_ReturnsTrue()
    {
        var body    = Encoding.UTF8.GetBytes("body content");
        var ts      = NowTimestamp();
        var sig     = ComputeSignature("body content", ts);
        var headers = MakeHeaders(sig, ts);

        var result = WebhookSignatureVerifier.Verify(
            HmacFormat.V0TimestampBody, body, headers, Secret, SigHeader, TsHeader, WindowSeconds);

        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_V0_EmptyBody_ValidSignature_ReturnsTrue()
    {
        var body    = Array.Empty<byte>();
        var ts      = NowTimestamp();
        var sig     = ComputeSignature("", ts);
        var headers = MakeHeaders(sig, ts);

        var result = WebhookSignatureVerifier.Verify(
            HmacFormat.V0TimestampBody, body, headers, Secret, SigHeader, TsHeader, WindowSeconds);

        result.Should().BeTrue();
    }

    // ── Signature failures ────────────────────────────────────────────

    [Fact]
    public void Verify_V0_WrongSignature_ReturnsFalse()
    {
        var body    = Encoding.UTF8.GetBytes("body");
        var ts      = NowTimestamp();
        var headers = MakeHeaders("v0=badhash0000000000000000000000000000000000000000000000000000000000", ts);

        var result = WebhookSignatureVerifier.Verify(
            HmacFormat.V0TimestampBody, body, headers, Secret, SigHeader, TsHeader, WindowSeconds);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_V0_MissingSignatureHeader_ReturnsFalse()
    {
        var body    = Encoding.UTF8.GetBytes("body");
        var ts      = NowTimestamp();
        var headers = MakeHeaders(null, ts);  // no sig header

        var result = WebhookSignatureVerifier.Verify(
            HmacFormat.V0TimestampBody, body, headers, Secret, SigHeader, TsHeader, WindowSeconds);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_V0_EmptySignatureHeader_ReturnsFalse()
    {
        var body    = Encoding.UTF8.GetBytes("body");
        var ts      = NowTimestamp();
        var headers = MakeHeaders("", ts);  // present but empty

        var result = WebhookSignatureVerifier.Verify(
            HmacFormat.V0TimestampBody, body, headers, Secret, SigHeader, TsHeader, WindowSeconds);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_V0_DifferentSecret_ReturnsFalse()
    {
        var body    = Encoding.UTF8.GetBytes("body");
        var ts      = NowTimestamp();
        var sig     = ComputeSignature("body", ts, secret: "different-secret");
        var headers = MakeHeaders(sig, ts);

        var result = WebhookSignatureVerifier.Verify(
            HmacFormat.V0TimestampBody, body, headers, Secret, SigHeader, TsHeader, WindowSeconds);

        result.Should().BeFalse();
    }

    // ── Timestamp / replay window ─────────────────────────────────────

    [Fact]
    public void Verify_V0_MissingTimestampHeader_ReturnsFalse()
    {
        var body    = Encoding.UTF8.GetBytes("body");
        var headers = MakeHeaders("v0=whatever", null);  // no ts header

        var result = WebhookSignatureVerifier.Verify(
            HmacFormat.V0TimestampBody, body, headers, Secret, SigHeader, TsHeader, WindowSeconds);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_V0_NonNumericTimestamp_ReturnsFalse()
    {
        var body    = Encoding.UTF8.GetBytes("body");
        var headers = MakeHeaders("v0=whatever", "not-a-number");

        var result = WebhookSignatureVerifier.Verify(
            HmacFormat.V0TimestampBody, body, headers, Secret, SigHeader, TsHeader, WindowSeconds);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_V0_ExpiredTimestamp_ReturnsFalse()
    {
        var body      = Encoding.UTF8.GetBytes("body");
        var oldTs     = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - WindowSeconds - 60).ToString();
        var sig       = ComputeSignature("body", oldTs);
        var headers   = MakeHeaders(sig, oldTs);

        var result = WebhookSignatureVerifier.Verify(
            HmacFormat.V0TimestampBody, body, headers, Secret, SigHeader, TsHeader, WindowSeconds);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_V0_FutureTimestamp_ReturnsFalse()
    {
        var body    = Encoding.UTF8.GetBytes("body");
        var futureTs = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + WindowSeconds + 60).ToString();
        var sig     = ComputeSignature("body", futureTs);
        var headers = MakeHeaders(sig, futureTs);

        var result = WebhookSignatureVerifier.Verify(
            HmacFormat.V0TimestampBody, body, headers, Secret, SigHeader, TsHeader, WindowSeconds);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_V0_TimestampInsideWindow_ReturnsTrue()
    {
        // Timestamp exactly at window boundary - 1s (well within)
        var body  = Encoding.UTF8.GetBytes("body");
        var ts    = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (WindowSeconds - 1)).ToString();
        var sig   = ComputeSignature("body", ts);
        var headers = MakeHeaders(sig, ts);

        var result = WebhookSignatureVerifier.Verify(
            HmacFormat.V0TimestampBody, body, headers, Secret, SigHeader, TsHeader, WindowSeconds);

        result.Should().BeTrue();
    }

    // ── Timing-safe comparison ────────────────────────────────────────

    [Fact]
    public void Verify_V0_LengthMismatch_ReturnsFalse()
    {
        // Signature of wrong length — caught by length check before FixedTimeEquals
        var body    = Encoding.UTF8.GetBytes("body");
        var ts      = NowTimestamp();
        var headers = MakeHeaders("v0=tooshort", ts);

        var result = WebhookSignatureVerifier.Verify(
            HmacFormat.V0TimestampBody, body, headers, Secret, SigHeader, TsHeader, WindowSeconds);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_V0_UppercaseHex_ReturnsFalse()
    {
        // Verifier lowercases its expected hash; an uppercase-hex incoming sig won't match
        var body  = Encoding.UTF8.GetBytes("body");
        var ts    = NowTimestamp();
        var sig   = ComputeSignature("body", ts).ToUpperInvariant(); // forcibly uppercase
        var headers = MakeHeaders(sig, ts);

        var result = WebhookSignatureVerifier.Verify(
            HmacFormat.V0TimestampBody, body, headers, Secret, SigHeader, TsHeader, WindowSeconds);

        result.Should().BeFalse();
    }

    // ── Timestamp header disabled ─────────────────────────────────────

    [Fact]
    public void Verify_V0_EmptyTimestampHeaderConfig_SkipsReplayCheck()
    {
        // When timestampHeader is "", the verifier skips replay-window validation entirely.
        // The basestring becomes "v0::{body}" (null ts interpolated as empty).
        var bodyStr  = "body content";
        var body     = Encoding.UTF8.GetBytes(bodyStr);
        // With no ts header configured, timestamp in basestring is null → interpolated as ""
        var basestring = $"v0::{bodyStr}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var hash       = hmac.ComputeHash(Encoding.UTF8.GetBytes(basestring));
        var sig        = "v0=" + Convert.ToHexString(hash).ToLowerInvariant();

        var headers = new HeaderDictionary();
        headers[SigHeader] = sig;
        // No timestamp header set — none needed

        var result = WebhookSignatureVerifier.Verify(
            HmacFormat.V0TimestampBody, body, headers, Secret, SigHeader,
            timestampHeader: "",  // disable replay check
            windowSeconds: WindowSeconds);

        result.Should().BeTrue();
    }

    // ── Unicode body ──────────────────────────────────────────────────

    [Fact]
    public void Verify_V0_Utf8Body_SpecialCharacters_VerifiesCorrectly()
    {
        var bodyStr = "Hello 🌍 Unicode: 日本語";
        var body    = Encoding.UTF8.GetBytes(bodyStr);
        var ts      = NowTimestamp();
        var sig     = ComputeSignature(bodyStr, ts);
        var headers = MakeHeaders(sig, ts);

        var result = WebhookSignatureVerifier.Verify(
            HmacFormat.V0TimestampBody, body, headers, Secret, SigHeader, TsHeader, WindowSeconds);

        result.Should().BeTrue();
    }

    // ── Unknown format ────────────────────────────────────────────────

    [Fact]
    public void Verify_UnknownFormat_ReturnsFalse()
    {
        var body    = Encoding.UTF8.GetBytes("body");
        var headers = new HeaderDictionary();

        var result = WebhookSignatureVerifier.Verify(
            (HmacFormat)99, body, headers, Secret, SigHeader, TsHeader, WindowSeconds);

        result.Should().BeFalse();
    }
}
