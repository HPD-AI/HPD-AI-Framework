using System.Text.Json;
using FluentAssertions;
using HPD.Auth.Core.Models;
using Xunit;

namespace HPD.Auth.Authentication.Tests.TokenResponse;

/// <summary>
/// Tests 60–75: JSON serialization of TokenResponse and UserTokenDto (TESTS.md §2).
/// </summary>
[Trait("Category", "TokenResponse")]
[Trait("Section", "2-JSON-Serialization")]
public class TokenResponse_Serialization_Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly Guid _userId = Guid.NewGuid();
    private static readonly DateTime _confirmedAt  = new(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime _createdAt    = new(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Core.Models.TokenResponse BuildResponse(
        string userMetadataJson = "{}",
        string appMetadataJson  = "{}",
        DateTime? emailConfirmedAt = null) =>
        new Core.Models.TokenResponse
        {
            AccessToken  = "eyJaccess",
            TokenType    = "bearer",
            ExpiresIn    = 900,
            ExpiresAt    = 1_700_000_000L,
            RefreshToken = "refreshTokenValue",
            User = new UserTokenDto
            {
                Id               = _userId,
                Email            = "user@example.com",
                EmailConfirmedAt = emailConfirmedAt,
                UserMetadata     = JsonDocument.Parse(userMetadataJson).RootElement,
                AppMetadata      = JsonDocument.Parse(appMetadataJson).RootElement,
                RequiredActions  = new List<string> { "VERIFY_EMAIL" },
                CreatedAt        = _createdAt,
                SubscriptionTier = "pro",
            },
        };

    private static JsonDocument Serialize(Core.Models.TokenResponse r)
        => JsonDocument.Parse(JsonSerializer.Serialize(r));

    // ─────────────────────────────────────────────────────────────────────────
    // Tests 60–65: Top-level TokenResponse keys
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TokenResponse_Serializes_access_token()
    {
        using var doc = Serialize(BuildResponse());
        doc.RootElement.TryGetProperty("access_token", out _).Should().BeTrue();
        doc.RootElement.GetProperty("access_token").GetString().Should().Be("eyJaccess");
    }

    [Fact]
    public void TokenResponse_Serializes_token_type()
    {
        using var doc = Serialize(BuildResponse());
        doc.RootElement.GetProperty("token_type").GetString().Should().Be("bearer");
    }

    [Fact]
    public void TokenResponse_Serializes_expires_in()
    {
        using var doc = Serialize(BuildResponse());
        var prop = doc.RootElement.GetProperty("expires_in");
        prop.ValueKind.Should().Be(JsonValueKind.Number);
        prop.GetInt32().Should().Be(900);
    }

    [Fact]
    public void TokenResponse_Serializes_expires_at()
    {
        using var doc = Serialize(BuildResponse());
        var prop = doc.RootElement.GetProperty("expires_at");
        prop.ValueKind.Should().Be(JsonValueKind.Number);
        prop.GetInt64().Should().Be(1_700_000_000L);
    }

    [Fact]
    public void TokenResponse_Serializes_refresh_token()
    {
        using var doc = Serialize(BuildResponse());
        doc.RootElement.GetProperty("refresh_token").GetString().Should().Be("refreshTokenValue");
    }

    [Fact]
    public void TokenResponse_Serializes_user()
    {
        using var doc = Serialize(BuildResponse());
        var user = doc.RootElement.GetProperty("user");
        user.ValueKind.Should().Be(JsonValueKind.Object);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tests 66–73: Nested UserTokenDto keys
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void UserTokenDto_Serializes_id()
    {
        using var doc  = Serialize(BuildResponse());
        var user = doc.RootElement.GetProperty("user");
        user.TryGetProperty("id", out var idProp).Should().BeTrue();
        idProp.GetGuid().Should().Be(_userId);
    }

    [Fact]
    public void UserTokenDto_Serializes_email()
    {
        using var doc = Serialize(BuildResponse());
        doc.RootElement.GetProperty("user").GetProperty("email").GetString()
           .Should().Be("user@example.com");
    }

    [Fact]
    public void UserTokenDto_Serializes_email_confirmed_at()
    {
        using var doc = Serialize(BuildResponse(emailConfirmedAt: _confirmedAt));
        doc.RootElement.GetProperty("user").TryGetProperty("email_confirmed_at", out _)
           .Should().BeTrue();
    }

    [Fact]
    public void UserTokenDto_Serializes_user_metadata()
    {
        using var doc = Serialize(BuildResponse(userMetadataJson: """{"theme":"dark"}"""));
        var meta = doc.RootElement.GetProperty("user").GetProperty("user_metadata");
        meta.ValueKind.Should().Be(JsonValueKind.Object);
        meta.GetProperty("theme").GetString().Should().Be("dark");
    }

    [Fact]
    public void UserTokenDto_Serializes_app_metadata()
    {
        using var doc = Serialize(BuildResponse(appMetadataJson: """{"plan":"pro"}"""));
        var meta = doc.RootElement.GetProperty("user").GetProperty("app_metadata");
        meta.ValueKind.Should().Be(JsonValueKind.Object);
        meta.GetProperty("plan").GetString().Should().Be("pro");
    }

    [Fact]
    public void UserTokenDto_Serializes_required_actions()
    {
        using var doc = Serialize(BuildResponse());
        var actions = doc.RootElement.GetProperty("user").GetProperty("required_actions");
        actions.ValueKind.Should().Be(JsonValueKind.Array);
        actions.EnumerateArray().Select(e => e.GetString()).Should().Contain("VERIFY_EMAIL");
    }

    [Fact]
    public void UserTokenDto_Serializes_created_at()
    {
        using var doc = Serialize(BuildResponse());
        doc.RootElement.GetProperty("user").TryGetProperty("created_at", out _).Should().BeTrue();
    }

    [Fact]
    public void UserTokenDto_Serializes_subscription_tier()
    {
        using var doc = Serialize(BuildResponse());
        doc.RootElement.GetProperty("user").GetProperty("subscription_tier").GetString()
           .Should().Be("pro");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 74 — expires_at is a number (not ISO 8601 string)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TokenResponse_ExpiresAt_Is_Unix_Timestamp_Not_DateTime()
    {
        using var doc = Serialize(BuildResponse());
        var expiresAt = doc.RootElement.GetProperty("expires_at");
        expiresAt.ValueKind.Should().Be(JsonValueKind.Number,
            "expires_at must serialize as a numeric Unix timestamp, not an ISO 8601 string");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 75 — round-trip deserialization produces identical values
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TokenResponse_Roundtrip_Deserializes_Correctly()
    {
        var original = BuildResponse(
            userMetadataJson: """{"theme":"dark"}""",
            appMetadataJson : """{"plan":"pro"}""",
            emailConfirmedAt: _confirmedAt);

        var json         = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<Core.Models.TokenResponse>(json)!;

        deserialized.AccessToken.Should().Be(original.AccessToken);
        deserialized.TokenType.Should().Be(original.TokenType);
        deserialized.ExpiresIn.Should().Be(original.ExpiresIn);
        deserialized.ExpiresAt.Should().Be(original.ExpiresAt);
        deserialized.RefreshToken.Should().Be(original.RefreshToken);
        deserialized.User.Id.Should().Be(original.User.Id);
        deserialized.User.Email.Should().Be(original.User.Email);
        deserialized.User.EmailConfirmedAt.Should().Be(original.User.EmailConfirmedAt);
        deserialized.User.SubscriptionTier.Should().Be(original.User.SubscriptionTier);
        deserialized.User.CreatedAt.Should().Be(original.User.CreatedAt);
        deserialized.User.RequiredActions.Should().BeEquivalentTo(original.User.RequiredActions);
        deserialized.User.UserMetadata.GetProperty("theme").GetString().Should().Be("dark");
        deserialized.User.AppMetadata.GetProperty("plan").GetString().Should().Be("pro");
    }
}
