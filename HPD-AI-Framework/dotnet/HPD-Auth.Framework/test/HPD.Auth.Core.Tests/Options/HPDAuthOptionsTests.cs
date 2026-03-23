using System.Text.Json;
using FluentAssertions;
using HPD.Auth.Core.Options;
using Xunit;

namespace HPD.Auth.Core.Tests.Options;

[Trait("Category", "Options")]
public class HPDAuthOptionsTests
{
    [Fact]
    public void HPDAuthOptions_AppName_DefaultsToHPD()
    {
        new HPDAuthOptions().AppName.Should().Be("HPD");
    }

    [Fact]
    public void HPDAuthOptions_AllSubOptions_AreNotNull()
    {
        var opts = new HPDAuthOptions();

        opts.Database.Should().NotBeNull();
        opts.Jwt.Should().NotBeNull();
        opts.Cookie.Should().NotBeNull();
        opts.Password.Should().NotBeNull();
        opts.Lockout.Should().NotBeNull();
        opts.RateLimiting.Should().NotBeNull();
        opts.OAuth.Should().NotBeNull();
        opts.Features.Should().NotBeNull();
        opts.MagicLink.Should().NotBeNull();
        opts.Security.Should().NotBeNull();
        opts.Sms.Should().NotBeNull();
    }

    [Fact]
    public void HPDAuthOptions_AdditionalClaimsFactory_DefaultsToNull()
    {
        new HPDAuthOptions().AdditionalClaimsFactory.Should().BeNull();
    }

    [Fact]
    public void HPDAuthOptions_JsonSerializationRoundtrip()
    {
        var original = new HPDAuthOptions();
        var options = new JsonSerializerOptions { WriteIndented = false };

        var json = JsonSerializer.Serialize(original, options);
        var deserialized = JsonSerializer.Deserialize<HPDAuthOptions>(json, options);

        deserialized.Should().NotBeNull();
        deserialized!.AppName.Should().Be(original.AppName);
        deserialized.Jwt.Issuer.Should().Be(original.Jwt.Issuer);
        deserialized.Jwt.AccessTokenLifetime.Should().Be(original.Jwt.AccessTokenLifetime);
        deserialized.Jwt.RefreshTokenLifetime.Should().Be(original.Jwt.RefreshTokenLifetime);
        deserialized.Cookie.HttpOnly.Should().Be(original.Cookie.HttpOnly);
        deserialized.Cookie.SecurePolicy.Should().Be(original.Cookie.SecurePolicy);
        deserialized.Password.RequiredLength.Should().Be(original.Password.RequiredLength);
        deserialized.Lockout.MaxFailedAttempts.Should().Be(original.Lockout.MaxFailedAttempts);
        deserialized.Security.RotateRefreshTokens.Should().Be(original.Security.RotateRefreshTokens);
        deserialized.Features.EnableAuditLog.Should().Be(original.Features.EnableAuditLog);
    }
}
