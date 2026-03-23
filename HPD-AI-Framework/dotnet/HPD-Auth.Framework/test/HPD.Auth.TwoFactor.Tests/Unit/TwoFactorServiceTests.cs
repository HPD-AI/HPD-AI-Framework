using FluentAssertions;
using HPD.Auth.TwoFactor.Services;
using Xunit;

namespace HPD.Auth.TwoFactor.Tests.Unit;

/// <summary>
/// Unit tests for TwoFactorService — pure functions, no DI or HTTP pipeline needed.
/// Covers sections 1.1 (FormatAuthenticatorKey) and 1.2 (GenerateAuthenticatorUri).
/// </summary>
public class TwoFactorServiceTests
{
    private readonly TwoFactorService _sut = new();

    // ─────────────────────────────────────────────────────────────────────────
    // Section 1.1 — FormatAuthenticatorKey
    // ─────────────────────────────────────────────────────────────────────────

    // 1.1.1 — 16-char input produces space-separated groups of 4 (lowercase)
    [Fact]
    public void FormatAuthenticatorKey_16Chars_ProducesSpacedGroups()
    {
        var result = _sut.FormatAuthenticatorKey("JBSWY3DPEHPK3PXP");
        result.Should().Be("jbsw y3dp ehpk 3pxp");
    }

    // 1.1.2 — 8-char input (exactly two groups of 4)
    [Fact]
    public void FormatAuthenticatorKey_8Chars_ProducesTwoGroups()
    {
        var result = _sut.FormatAuthenticatorKey("JBSWY3DP");
        result.Should().Be("jbsw y3dp");
    }

    // 1.1.3 — 3-char input (shorter than one group): no space appended
    [Fact]
    public void FormatAuthenticatorKey_3Chars_NoSpace()
    {
        var result = _sut.FormatAuthenticatorKey("ABC");
        result.Should().Be("abc");
    }

    // 1.1.4 — 5-char input: first group + remainder without trailing space
    [Fact]
    public void FormatAuthenticatorKey_5Chars_FirstGroupPlusRemainder()
    {
        var result = _sut.FormatAuthenticatorKey("ABCDE");
        result.Should().Be("abcd e");
    }

    // 1.1.5 — empty string throws ArgumentException
    [Fact]
    public void FormatAuthenticatorKey_EmptyString_ThrowsArgumentException()
    {
        var act = () => _sut.FormatAuthenticatorKey(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    // 1.1.6 — null throws ArgumentException
    [Fact]
    public void FormatAuthenticatorKey_Null_ThrowsArgumentException()
    {
        var act = () => _sut.FormatAuthenticatorKey(null!);
        act.Should().Throw<ArgumentException>();
    }

    // 1.1.7 — key length not a multiple of 4: last group contains remaining chars, no trailing space
    [Fact]
    public void FormatAuthenticatorKey_NonMultipleOf4_LastGroupHasRemainder()
    {
        // 9 chars → "abcd efgh i"  — wait: 9 = 4+4+1, loop runs while pos+4 < 9 (pos=0→true, pos=4→false), then appends rest
        var result = _sut.FormatAuthenticatorKey("ABCDEFGHI");
        result.Should().Be("abcd efgh i");
    }

    // 1.1.8 — output is always lowercase regardless of input case
    [Fact]
    public void FormatAuthenticatorKey_MixedCase_OutputIsLowercase()
    {
        var result = _sut.FormatAuthenticatorKey("abcdEFGH");
        result.Should().Be("abcd efgh");
        result.Should().Be(result.ToLowerInvariant());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 1.2 — GenerateAuthenticatorUri
    // ─────────────────────────────────────────────────────────────────────────

    // 1.2.1 — URI starts with otpauth://totp/
    [Fact]
    public void GenerateAuthenticatorUri_ValidInputs_StartsWithOtpauth()
    {
        var result = _sut.GenerateAuthenticatorUri("HPD", "user@example.com", "JBSWY3DP");
        result.Should().StartWith("otpauth://totp/");
    }

    // 1.2.2 — URI contains the secret key
    [Fact]
    public void GenerateAuthenticatorUri_ValidInputs_ContainsSecret()
    {
        var result = _sut.GenerateAuthenticatorUri("HPD", "user@example.com", "JBSWY3DP");
        result.Should().Contain("secret=JBSWY3DP");
    }

    // 1.2.3 — URI contains issuer both as label-prefix and query param
    [Fact]
    public void GenerateAuthenticatorUri_ValidInputs_ContainsIssuerTwice()
    {
        var result = _sut.GenerateAuthenticatorUri("HPD", "user@example.com", "JBSWY3DP");
        // label part: otpauth://totp/HPD:...
        result.Should().Contain("HPD:");
        // query param: issuer=HPD
        result.Should().Contain("issuer=HPD");
    }

    // 1.2.4 — email is percent-encoded (@ → %40)
    [Fact]
    public void GenerateAuthenticatorUri_ValidInputs_EmailIsPercentEncoded()
    {
        var result = _sut.GenerateAuthenticatorUri("HPD", "user@example.com", "JBSWY3DP");
        result.Should().Contain("user%40example.com");
    }

    // 1.2.5 — issuer with spaces is percent-encoded
    [Fact]
    public void GenerateAuthenticatorUri_IssuerWithSpaces_IsPercentEncoded()
    {
        var result = _sut.GenerateAuthenticatorUri("My App", "user@example.com", "JBSWY3DP");
        result.Should().Contain("My%20App");
    }

    // 1.2.6 — email with '+' is percent-encoded
    [Fact]
    public void GenerateAuthenticatorUri_EmailWithPlus_IsPercentEncoded()
    {
        var result = _sut.GenerateAuthenticatorUri("HPD", "user+tag@example.com", "JBSWY3DP");
        // UrlEncoder encodes '+' as %2B
        result.Should().Contain("%2B");
    }

    // 1.2.7 — empty issuer throws ArgumentException
    [Fact]
    public void GenerateAuthenticatorUri_EmptyIssuer_ThrowsArgumentException()
    {
        var act = () => _sut.GenerateAuthenticatorUri(string.Empty, "user@example.com", "JBSWY3DP");
        act.Should().Throw<ArgumentException>();
    }

    // 1.2.8 — empty email throws ArgumentException
    [Fact]
    public void GenerateAuthenticatorUri_EmptyEmail_ThrowsArgumentException()
    {
        var act = () => _sut.GenerateAuthenticatorUri("HPD", string.Empty, "JBSWY3DP");
        act.Should().Throw<ArgumentException>();
    }

    // 1.2.9 — empty key throws ArgumentException
    [Fact]
    public void GenerateAuthenticatorUri_EmptyKey_ThrowsArgumentException()
    {
        var act = () => _sut.GenerateAuthenticatorUri("HPD", "user@example.com", string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    // 1.2.10 — full URI format matches expected pattern
    [Fact]
    public void GenerateAuthenticatorUri_ValidInputs_FullFormatCorrect()
    {
        var result = _sut.GenerateAuthenticatorUri("HPD", "user@example.com", "JBSWY3DP");
        // otpauth://totp/HPD:user%40example.com?secret=JBSWY3DP&issuer=HPD&digits=6
        result.Should().Be("otpauth://totp/HPD:user%40example.com?secret=JBSWY3DP&issuer=HPD&digits=6");
    }
}
