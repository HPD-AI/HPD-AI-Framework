using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using HPD.Auth.Authentication.Tests.Helpers;
using HPD.Auth.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace HPD.Auth.Authentication.Tests.TokenService;

/// <summary>
/// Tests 1–14: JWT claims within the access token (TESTS.md §1.1).
/// Tests 15–16: TokenResponse ExpiresIn / ExpiresAt shape (§1.2 partial).
/// </summary>
[Trait("Category", "TokenService")]
[Trait("Section", "1.1-JWT-Claims")]
public class TokenService_GenerateTokensAsync_JWT_Claims_Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static JwtSecurityToken DecodeJwt(string rawToken)
    {
        var handler = new JwtSecurityTokenHandler();
        handler.CanReadToken(rawToken).Should().BeTrue("the access token must be a valid JWT string");
        return handler.ReadJwtToken(rawToken);
    }

    private static async Task<(Core.Models.TokenResponse response, JwtSecurityToken jwt)> GenerateAsync(
        Action<Core.Options.HPDAuthOptions>? configure = null,
        Action<Core.Entities.ApplicationUser>? configureUser = null)
    {
        using var scope = ServiceProviderBuilder.CreateScope(configure);
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope, configureUser);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);
        var jwt      = DecodeJwt(response.AccessToken);
        return (response, jwt);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 1 — sub claim
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_JWT_Contains_Sub_Claim()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user      = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc       = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response  = await svc.GenerateTokensAsync(user);
        var jwt       = DecodeJwt(response.AccessToken);

        jwt.Subject.Should().Be(user.Id.ToString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 2 — email claim
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_JWT_Contains_Email_Claim()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user      = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc       = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response  = await svc.GenerateTokensAsync(user);
        var jwt       = DecodeJwt(response.AccessToken);

        var emailClaim = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Email);
        emailClaim.Should().NotBeNull();
        emailClaim!.Value.Should().Be(user.Email);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 3 — jti claim
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_JWT_Contains_Jti_Claim()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user      = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc       = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response  = await svc.GenerateTokensAsync(user);
        var jwt       = DecodeJwt(response.AccessToken);

        var jti = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti);
        jti.Should().NotBeNull();
        Guid.TryParse(jti!.Value, out _).Should().BeTrue("jti must be a valid GUID");
        jti.Value.Should().NotBeNullOrEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 4 — NameIdentifier claim
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_JWT_Contains_NameIdentifier_Claim()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user      = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc       = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response  = await svc.GenerateTokensAsync(user);
        var jwt       = DecodeJwt(response.AccessToken);

        // ClaimTypes.NameIdentifier maps to the long URI form in JWT claims.
        var nameId = jwt.Claims.FirstOrDefault(c =>
            c.Type == ClaimTypes.NameIdentifier ||
            c.Type == "nameid");
        nameId.Should().NotBeNull("NameIdentifier claim must be present");
        nameId!.Value.Should().Be(user.Id.ToString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 5 — instance_id claim
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_JWT_Contains_InstanceId_Claim()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user      = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc       = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response  = await svc.GenerateTokensAsync(user);
        var jwt       = DecodeJwt(response.AccessToken);

        var instanceId = jwt.Claims.FirstOrDefault(c => c.Type == "instance_id");
        instanceId.Should().NotBeNull();
        instanceId!.Value.Should().Be(user.InstanceId.ToString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 6 — subscription_tier claim
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_JWT_Contains_SubscriptionTier_Claim()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user      = await ServiceProviderBuilder.CreateUserAsync(scope, u => u.SubscriptionTier = "enterprise");
        var svc       = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response  = await svc.GenerateTokensAsync(user);
        var jwt       = DecodeJwt(response.AccessToken);

        var tier = jwt.Claims.FirstOrDefault(c => c.Type == "subscription_tier");
        tier.Should().NotBeNull();
        tier!.Value.Should().Be("enterprise");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 7 — role claims when user has roles
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_JWT_Contains_Role_Claims()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user        = await ServiceProviderBuilder.CreateUserAsync(scope);
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Core.Entities.ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Core.Entities.ApplicationRole>>();

        await roleManager.CreateAsync(new Core.Entities.ApplicationRole { Name = "Admin" });
        await roleManager.CreateAsync(new Core.Entities.ApplicationRole { Name = "User" });
        await userManager.AddToRoleAsync(user, "Admin");
        await userManager.AddToRoleAsync(user, "User");

        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);
        var jwt      = DecodeJwt(response.AccessToken);

        var roles = jwt.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .Select(c => c.Value)
            .ToList();

        roles.Should().Contain("Admin");
        roles.Should().Contain("User");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 8 — no role claims when user has no roles
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_JWT_Contains_No_Extra_Role_Claims_When_No_Roles()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);
        var jwt      = DecodeJwt(response.AccessToken);

        var roles = jwt.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .ToList();

        roles.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 9 — AdditionalClaimsFactory is invoked
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_AdditionalClaimsFactory_Is_Invoked()
    {
        using var scope = ServiceProviderBuilder.CreateScope(opts =>
        {
            opts.AdditionalClaimsFactory = (_, claims) =>
            {
                claims.Add(new System.Security.Claims.Claim("custom_claim", "custom_value"));
                return Task.CompletedTask;
            };
        });

        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);
        var jwt      = DecodeJwt(response.AccessToken);

        jwt.Claims.Should().Contain(c => c.Type == "custom_claim" && c.Value == "custom_value");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 10 — null AdditionalClaimsFactory does not throw
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_AdditionalClaimsFactory_Not_Invoked_When_Null()
    {
        using var scope = ServiceProviderBuilder.CreateScope(opts =>
        {
            opts.AdditionalClaimsFactory = null;
        });

        var user = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc  = scope.ServiceProvider.GetRequiredService<ITokenService>();

        Func<Task> act = () => svc.GenerateTokensAsync(user);
        await act.Should().NotThrowAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 11 — iss matches config
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_JWT_Issuer_Matches_Config()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);
        var jwt      = DecodeJwt(response.AccessToken);

        jwt.Issuer.Should().Be(TokenServiceFixture.DefaultIssuer);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 12 — aud matches config
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_JWT_Audience_Matches_Config()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);
        var jwt      = DecodeJwt(response.AccessToken);

        jwt.Audiences.Should().Contain(TokenServiceFixture.DefaultAudience);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 13 — exp is approximately UtcNow + AccessTokenLifetime
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_JWT_Expiry_Matches_Config()
    {
        var lifetime = TimeSpan.FromMinutes(15);
        using var scope = ServiceProviderBuilder.CreateScope(opts =>
        {
            opts.Jwt.AccessTokenLifetime = lifetime;
        });

        var before   = DateTime.UtcNow;
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);
        var jwt      = DecodeJwt(response.AccessToken);
        var after    = DateTime.UtcNow;

        var expectedMin = before + lifetime - TimeSpan.FromSeconds(5);
        var expectedMax = after  + lifetime + TimeSpan.FromSeconds(5);

        jwt.ValidTo.Should().BeAfter(expectedMin).And.BeBefore(expectedMax);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 14 — algorithm is HS256
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_JWT_Signed_With_HS256()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);
        var jwt      = DecodeJwt(response.AccessToken);

        jwt.Header.Alg.Should().Be(SecurityAlgorithms.HmacSha256);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test: JWT is verifiable with the configured secret (sanity)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_JWT_Is_Verifiable_With_Secret()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        var handler = new JwtSecurityTokenHandler();
        var key     = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TokenServiceFixture.DefaultSecret));
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = TokenServiceFixture.DefaultIssuer,
            ValidateAudience         = true,
            ValidAudience            = TokenServiceFixture.DefaultAudience,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = key,
            ClockSkew                = TimeSpan.FromSeconds(5),
        };

        var act = () => handler.ValidateToken(response.AccessToken, validationParams, out _);
        act.Should().NotThrow("a valid token must pass signature validation");
    }
}
