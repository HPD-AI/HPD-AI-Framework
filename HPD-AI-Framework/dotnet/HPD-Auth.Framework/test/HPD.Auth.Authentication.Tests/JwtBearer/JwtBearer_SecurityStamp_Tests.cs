using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using HPD.Auth.Authentication.Extensions;
using HPD.Auth.Authentication.Tests.Helpers;
using HPD.Auth.Core.Entities;
using HPD.Auth.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace HPD.Auth.Authentication.Tests.JwtBearer;

/// <summary>
/// Tests 118–121: OnTokenValidated security stamp validation (TESTS.md §5.4).
///
/// These tests use a full HPD.Auth test host (AddHPDAuth + AddAuthentication) so
/// that the real SignInManager pipeline, security stamp validation, and the
/// JwtBearerConfigurator's OnTokenValidated hook are all exercised together.
/// </summary>
[Trait("Category", "JwtBearer")]
[Trait("Section", "5.4-Security-Stamp")]
public class JwtBearer_SecurityStamp_Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Test server that wires up the full HPD.Auth stack
    // ─────────────────────────────────────────────────────────────────────────

    private static (TestServer Server, IServiceProvider RootServices) CreateServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var dbName = Guid.NewGuid().ToString(); // isolated in-memory DB per test

        builder.Services.AddHPDAuth(opts =>
        {
            opts.AppName                  = dbName;
            opts.Jwt.Secret               = TokenServiceFixture.DefaultSecret;
            opts.Jwt.Issuer               = TokenServiceFixture.DefaultIssuer;
            opts.Jwt.Audience             = TokenServiceFixture.DefaultAudience;
            opts.Jwt.AccessTokenLifetime  = TimeSpan.FromMinutes(15);
            opts.Jwt.RefreshTokenLifetime = TimeSpan.FromDays(14);
        })
        .AddAuthentication();

        builder.Services.AddAuthorization();

        var app = builder.Build();

        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/protected", (HttpContext ctx) =>
        {
            if (ctx.User.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();
            return Results.Ok(new { authenticated = true });
        }).RequireAuthorization();

        app.Start();

        return (app.GetTestServer(), app.Services);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a user via UserManager in the root service provider scope,
    /// then issues a JWT that embeds the user's ID and security stamp in
    /// the standard ASP.NET Identity claims so ValidateSecurityStampAsync can verify them.
    /// </summary>
    private static async Task<(ApplicationUser User, string Token)> CreateUserAndIssueMintedJwtAsync(
        IServiceProvider rootServices)
    {
        using var scope      = rootServices.CreateScope();
        var userManager      = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName         = $"{Guid.NewGuid():N}@test.example",
            Email            = $"{Guid.NewGuid():N}@test.example",
            InstanceId       = Guid.Empty,
            SubscriptionTier = "pro",
            IsActive         = true,
            IsDeleted        = false,
            EmailConfirmedAt = DateTime.UtcNow,
            Created          = DateTime.UtcNow,
        };

        var result = await userManager.CreateAsync(user, "Test@1234!");
        result.Succeeded.Should().BeTrue("user creation must succeed");

        // Read back the user so the SecurityStamp field is populated by Identity.
        var created = await userManager.FindByIdAsync(user.Id.ToString());
        created.Should().NotBeNull();

        var token = MintJwt(created!);
        return (created!, token);
    }

    /// <summary>
    /// Issues a JWT that contains the claims ASP.NET Identity's
    /// <c>ValidateSecurityStampAsync</c> requires: sub (NameIdentifier)
    /// and the AspNet.Identity.SecurityStamp claim.
    /// </summary>
    private static string MintJwt(ApplicationUser user)
    {
        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TokenServiceFixture.DefaultSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now         = DateTime.UtcNow;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   user.Id.ToString()),
            new(ClaimTypes.NameIdentifier,     user.Id.ToString()),
            // Identity uses this claim to match against UserManager.GetSecurityStampAsync()
            new("AspNet.Identity.SecurityStamp", user.SecurityStamp ?? string.Empty),
        };

        var jwt = new JwtSecurityToken(
            issuer:             TokenServiceFixture.DefaultIssuer,
            audience:           TokenServiceFixture.DefaultAudience,
            claims:             claims,
            notBefore:          now,
            expires:            now.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 118 — changed security stamp → OnTokenValidated fails → 401
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Issue a JWT, rotate the user's security stamp (simulating a password change
    /// or admin force-logout), then send the original JWT. The OnTokenValidated hook
    /// should detect the stamp mismatch and return 401.
    /// </summary>
    [Fact]
    public async Task JWT_SecurityStamp_Changed_Fails_OnTokenValidated()
    {
        var (server, rootServices) = CreateServer();
        using var _ = server;

        var (user, token) = await CreateUserAndIssueMintedJwtAsync(rootServices);

        // Rotate the security stamp — simulates password reset / force-logout.
        using (var scope = rootServices.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var refreshed   = await userManager.FindByIdAsync(user.Id.ToString());
            await userManager.UpdateSecurityStampAsync(refreshed!);
        }

        var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 119 — inactive user → OnTokenValidated fails with "disabled" → 401
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Issue a JWT, then set IsActive = false on the user. The OnTokenValidated
    /// hook checks IsActive and calls context.Fail() with a "disabled" message.
    /// </summary>
    [Fact]
    public async Task JWT_Inactive_User_Fails_OnTokenValidated()
    {
        var (server, rootServices) = CreateServer();
        using var _ = server;

        var (user, token) = await CreateUserAndIssueMintedJwtAsync(rootServices);

        // Deactivate the user.
        using (var scope = rootServices.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var refreshed   = await userManager.FindByIdAsync(user.Id.ToString());
            refreshed!.IsActive = false;
            await userManager.UpdateAsync(refreshed);
        }

        var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 120 — deleted user → OnTokenValidated fails → 401
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Issue a JWT, then set IsDeleted = true. The OnTokenValidated hook
    /// checks IsDeleted and rejects the principal.
    /// </summary>
    [Fact]
    public async Task JWT_Deleted_User_Fails_OnTokenValidated()
    {
        var (server, rootServices) = CreateServer();
        using var _ = server;

        var (user, token) = await CreateUserAndIssueMintedJwtAsync(rootServices);

        // Soft-delete the user.
        using (var scope = rootServices.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var refreshed   = await userManager.FindByIdAsync(user.Id.ToString());
            refreshed!.IsDeleted = true;
            await userManager.UpdateAsync(refreshed);
        }

        var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 121 — unchanged security stamp → OnTokenValidated succeeds → 200
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When the security stamp has not changed and the user is active and not deleted
    /// the OnTokenValidated hook should succeed and the request returns 200.
    /// </summary>
    [Fact]
    public async Task JWT_SecurityStamp_Valid_Allows_Request()
    {
        var (server, rootServices) = CreateServer();
        using var _ = server;

        var (_, token) = await CreateUserAndIssueMintedJwtAsync(rootServices);

        var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
