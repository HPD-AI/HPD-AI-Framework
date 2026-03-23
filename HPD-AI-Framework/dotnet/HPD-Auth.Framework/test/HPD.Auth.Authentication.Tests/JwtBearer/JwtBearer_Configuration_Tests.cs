using FluentAssertions;
using HPD.Auth.Authentication.Tests.Helpers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HPD.Auth.Authentication.Tests.JwtBearer;

/// <summary>
/// Tests 122–127: JWT Bearer token validation parameter configuration (TESTS.md §5.5).
/// </summary>
[Trait("Category", "JwtBearer")]
[Trait("Section", "5.5-JwtBearer-Config")]
public class JwtBearer_Configuration_Tests
{
    private static JwtBearerOptions ResolveOptions(
        Action<Core.Options.HPDAuthOptions>? configure = null)
    {
        using var scope = ServiceProviderBuilder.CreateScope(configure);
        var monitor = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();
        return monitor.Get(JwtBearerDefaults.AuthenticationScheme);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 122 — ValidateIssuer = true
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void JWT_Config_ValidateIssuer_True()
    {
        var opts = ResolveOptions();
        opts.TokenValidationParameters.ValidateIssuer.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 123 — ValidateAudience = true
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void JWT_Config_ValidateAudience_True()
    {
        var opts = ResolveOptions();
        opts.TokenValidationParameters.ValidateAudience.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 124 — ValidateLifetime matches config
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void JWT_Config_ValidateLifetime_Matches_Config()
    {
        var opts = ResolveOptions(o => o.Jwt.ValidateLifetime = false);
        opts.TokenValidationParameters.ValidateLifetime.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 125 — ClockSkew matches config
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void JWT_Config_ClockSkew_Matches_Config()
    {
        var opts = ResolveOptions(o => o.Jwt.ClockSkew = TimeSpan.FromSeconds(10));
        opts.TokenValidationParameters.ClockSkew.Should().Be(TimeSpan.FromSeconds(10));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 126 — RequireExpirationTime = true
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void JWT_Config_RequireExpirationTime_True()
    {
        var opts = ResolveOptions();
        opts.TokenValidationParameters.RequireExpirationTime.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 127 — RequireSignedTokens = true
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void JWT_Config_RequireSignedTokens_True()
    {
        var opts = ResolveOptions();
        opts.TokenValidationParameters.RequireSignedTokens.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Extra: ValidIssuer and ValidAudience match config values
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void JWT_Config_ValidIssuer_Matches_Config()
    {
        var opts = ResolveOptions();
        opts.TokenValidationParameters.ValidIssuer.Should().Be(TokenServiceFixture.DefaultIssuer);
    }

    [Fact]
    public void JWT_Config_ValidAudience_Matches_Config()
    {
        var opts = ResolveOptions();
        opts.TokenValidationParameters.ValidAudience.Should().Be(TokenServiceFixture.DefaultAudience);
    }
}
