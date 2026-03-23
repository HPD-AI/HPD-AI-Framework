using System.Security.Claims;
using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.OAuth.Services;
using Xunit;

namespace HPD.Auth.OAuth.Tests;

/// <summary>
/// Unit tests for <see cref="ExternalProviderService"/>.
/// Covers sections 4 (GetEmail, GetAvatarUrl, GetDisplayName,
/// NormalizeProviderName, IsProviderSupported) and
/// section 6 (MapClaimsToUser) from TESTS.md.
/// </summary>
public class ExternalProviderServiceTests
{
    private readonly ExternalProviderService _svc = new();

    // ── Helper ────────────────────────────────────────────────────────────────

    private static ClaimsPrincipal MakePrincipal(params (string type, string value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.type, c.value)),
            authenticationType: "test");
        return new ClaimsPrincipal(identity);
    }

    // ── Section 4.1: GetEmail ─────────────────────────────────────────────────

    // 26 — ClaimTypes.Email present
    [Fact]
    public void GetEmail_ClaimTypesEmail_ReturnsThatValue()
    {
        var principal = MakePrincipal((ClaimTypes.Email, "user@example.com"));
        _svc.GetEmail(principal).Should().Be("user@example.com");
    }

    // 27 — no ClaimTypes.Email, bare "email" claim
    [Fact]
    public void GetEmail_BareEmailClaim_ReturnsThatValue()
    {
        var principal = MakePrincipal(("email", "bare@example.com"));
        _svc.GetEmail(principal).Should().Be("bare@example.com");
    }

    // 28 — neither claim present → null
    [Fact]
    public void GetEmail_NoClaims_ReturnsNull()
    {
        var principal = MakePrincipal();
        _svc.GetEmail(principal).Should().BeNull();
    }

    // ── Section 4.2: GetAvatarUrl ─────────────────────────────────────────────

    // 29 — google, "picture" present
    [Fact]
    public void GetAvatarUrl_Google_PicturePresent_ReturnsPicture()
    {
        var principal = MakePrincipal(("picture", "https://lh3.google.com/photo.jpg"));
        _svc.GetAvatarUrl(principal, "google").Should().Be("https://lh3.google.com/photo.jpg");
    }

    // 30 — google, no "picture" → null
    [Fact]
    public void GetAvatarUrl_Google_NoPicture_ReturnsNull()
    {
        var principal = MakePrincipal();
        _svc.GetAvatarUrl(principal, "google").Should().BeNull();
    }

    // 31 — github, "urn:github:avatar" present
    [Fact]
    public void GetAvatarUrl_GitHub_UrnAvatarPresent_ReturnsThatUrl()
    {
        var principal = MakePrincipal(("urn:github:avatar", "https://avatars.github.com/u/123"));
        _svc.GetAvatarUrl(principal, "github").Should().Be("https://avatars.github.com/u/123");
    }

    // 32 — github, no "urn:github:avatar" → null
    [Fact]
    public void GetAvatarUrl_GitHub_NoAvatarClaim_ReturnsNull()
    {
        var principal = MakePrincipal();
        _svc.GetAvatarUrl(principal, "github").Should().BeNull();
    }

    // 33 — microsoft, "avatar_url" claim
    [Fact]
    public void GetAvatarUrl_Microsoft_AvatarUrlClaim_ReturnsThatUrl()
    {
        var principal = MakePrincipal(("avatar_url", "https://ms.example.com/avatar.jpg"));
        _svc.GetAvatarUrl(principal, "microsoft").Should().Be("https://ms.example.com/avatar.jpg");
    }

    // 34 — microsoft, no claims → null
    [Fact]
    public void GetAvatarUrl_Microsoft_NoClaims_ReturnsNull()
    {
        var principal = MakePrincipal();
        _svc.GetAvatarUrl(principal, "microsoft").Should().BeNull();
    }

    // 35 — discord, "avatar_url" claim
    [Fact]
    public void GetAvatarUrl_Discord_AvatarUrlClaim_ReturnsThatUrl()
    {
        var principal = MakePrincipal(("avatar_url", "https://cdn.discordapp.com/avatars/123/abc.png"));
        _svc.GetAvatarUrl(principal, "discord").Should().Be("https://cdn.discordapp.com/avatars/123/abc.png");
    }

    // 36 — twitter, "avatar_url" claim
    [Fact]
    public void GetAvatarUrl_Twitter_AvatarUrlClaim_ReturnsThatUrl()
    {
        var principal = MakePrincipal(("avatar_url", "https://pbs.twimg.com/profile_images/123.jpg"));
        _svc.GetAvatarUrl(principal, "twitter").Should().Be("https://pbs.twimg.com/profile_images/123.jpg");
    }

    // 37 — apple → always null
    [Fact]
    public void GetAvatarUrl_Apple_AnyClaimsAlwaysNull()
    {
        var principal = MakePrincipal(("picture", "https://example.com"), ("avatar_url", "https://example.com"));
        _svc.GetAvatarUrl(principal, "apple").Should().BeNull();
    }

    // 38 — unknown provider, "avatar_url" claim
    [Fact]
    public void GetAvatarUrl_UnknownProvider_AvatarUrlPresent_ReturnsThatUrl()
    {
        var principal = MakePrincipal(("avatar_url", "https://custom.example.com/avatar.png"));
        _svc.GetAvatarUrl(principal, "customProvider").Should().Be("https://custom.example.com/avatar.png");
    }

    // 39 — unknown provider, "picture" present but no "avatar_url"
    [Fact]
    public void GetAvatarUrl_UnknownProvider_PictureOnlyNoAvatarUrl_ReturnsPicture()
    {
        var principal = MakePrincipal(("picture", "https://custom.example.com/picture.png"));
        _svc.GetAvatarUrl(principal, "customProvider").Should().Be("https://custom.example.com/picture.png");
    }

    // ── Section 4.3: GetDisplayName ───────────────────────────────────────────

    // 40 — "name" claim present
    [Fact]
    public void GetDisplayName_NameClaim_ReturnsThatValue()
    {
        var principal = MakePrincipal(("name", "Jane Doe"));
        _svc.GetDisplayName(principal).Should().Be("Jane Doe");
    }

    // 41 — no "name", ClaimTypes.Name present
    [Fact]
    public void GetDisplayName_NoNameButClaimTypesName_ReturnsThatValue()
    {
        var principal = MakePrincipal((ClaimTypes.Name, "Jane Doe"));
        _svc.GetDisplayName(principal).Should().Be("Jane Doe");
    }

    // 42 — no "name" or ClaimTypes.Name, GivenName present
    [Fact]
    public void GetDisplayName_GivenNameFallback_ReturnsFirstName()
    {
        var principal = MakePrincipal((ClaimTypes.GivenName, "Jane"));
        _svc.GetDisplayName(principal).Should().Be("Jane");
    }

    // 43 — none of the above → null
    [Fact]
    public void GetDisplayName_NoClaims_ReturnsNull()
    {
        var principal = MakePrincipal();
        _svc.GetDisplayName(principal).Should().BeNull();
    }

    // ── Section 4.4: NormalizeProviderName ───────────────────────────────────

    // 44 — "google" → "Google"
    [Fact]
    public void NormalizeProviderName_Lowercase_google_ReturnsGoogle()
    {
        _svc.NormalizeProviderName("google").Should().Be("Google");
    }

    // 45 — "GITHUB" → "GitHub"
    [Fact]
    public void NormalizeProviderName_Uppercase_GITHUB_ReturnsGitHub()
    {
        _svc.NormalizeProviderName("GITHUB").Should().Be("GitHub");
    }

    // 46 — "Microsoft" → "Microsoft"
    [Fact]
    public void NormalizeProviderName_Microsoft_ReturnsMicrosoft()
    {
        _svc.NormalizeProviderName("Microsoft").Should().Be("Microsoft");
    }

    // 47 — "unknown" → null
    [Fact]
    public void NormalizeProviderName_UnknownProvider_ReturnsNull()
    {
        _svc.NormalizeProviderName("unknown").Should().BeNull();
    }

    // 48 — null → null
    [Fact]
    public void NormalizeProviderName_Null_ReturnsNull()
    {
        _svc.NormalizeProviderName(null).Should().BeNull();
    }

    // 48b — empty string → null
    [Fact]
    public void NormalizeProviderName_Empty_ReturnsNull()
    {
        _svc.NormalizeProviderName(string.Empty).Should().BeNull();
    }

    // ── Section 4.5: IsProviderSupported ─────────────────────────────────────

    // 49 — "Google" (exact casing) → true
    [Fact]
    public void IsProviderSupported_Google_ReturnsTrue()
    {
        _svc.IsProviderSupported("Google").Should().BeTrue();
    }

    // 50 — "google" (lowercase) → true (case-insensitive)
    [Fact]
    public void IsProviderSupported_LowercaseGoogle_ReturnsTrue()
    {
        _svc.IsProviderSupported("google").Should().BeTrue();
    }

    // 51 — "stripe" → false
    [Fact]
    public void IsProviderSupported_Stripe_ReturnsFalse()
    {
        _svc.IsProviderSupported("stripe").Should().BeFalse();
    }

    // 52 — null → false
    [Fact]
    public void IsProviderSupported_Null_ReturnsFalse()
    {
        _svc.IsProviderSupported(null).Should().BeFalse();
    }

    // ── Section 4.6: MapClaimsToUser ─────────────────────────────────────────

    // 53 — no FirstName; GivenName claim → FirstName set
    [Fact]
    public void MapClaimsToUser_NoFirstName_GivenNameClaim_SetsFirstName()
    {
        var user = new ApplicationUser { Email = "user@example.com" };
        var principal = MakePrincipal((ClaimTypes.GivenName, "Jane"));

        _svc.MapClaimsToUser(user, principal, "Google");

        user.FirstName.Should().Be("Jane");
    }

    // 54 — existing FirstName; different GivenName → FirstName unchanged
    [Fact]
    public void MapClaimsToUser_ExistingFirstName_GivenNameClaim_DoesNotOverwrite()
    {
        var user = new ApplicationUser { Email = "user@example.com", FirstName = "Existing" };
        var principal = MakePrincipal((ClaimTypes.GivenName, "Different"));

        _svc.MapClaimsToUser(user, principal, "Google");

        user.FirstName.Should().Be("Existing");
    }

    // 55 — no AvatarUrl; Google principal with "picture" → AvatarUrl set
    [Fact]
    public void MapClaimsToUser_NoAvatarUrl_GooglePictureClaim_SetsAvatarUrl()
    {
        var user = new ApplicationUser { Email = "user@example.com" };
        var principal = MakePrincipal(("picture", "https://lh3.google.com/photo.jpg"));

        _svc.MapClaimsToUser(user, principal, "Google");

        user.AvatarUrl.Should().Be("https://lh3.google.com/photo.jpg");
    }

    // 56 — existing AvatarUrl; provider has claim → AvatarUrl unchanged
    [Fact]
    public void MapClaimsToUser_ExistingAvatarUrl_ClaimPresent_DoesNotOverwrite()
    {
        var user = new ApplicationUser
        {
            Email = "user@example.com",
            AvatarUrl = "https://existing.example.com/avatar.png"
        };
        var principal = MakePrincipal(("picture", "https://new.example.com/photo.jpg"));

        _svc.MapClaimsToUser(user, principal, "Google");

        user.AvatarUrl.Should().Be("https://existing.example.com/avatar.png");
    }
}
