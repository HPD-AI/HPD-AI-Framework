using FluentAssertions;
using HPD.Auth.Authentication.Extensions;
using HPD.Auth.Authentication.Tests.Helpers;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HPD.Auth.Authentication.Tests.Extensions;

/// <summary>
/// Tests 128–141: HPDAuthAuthenticationBuilderExtensions and cookie-only mode (TESTS.md §6–7).
/// </summary>
[Trait("Category", "Extensions")]
[Trait("Section", "6-7-BuilderExtensions")]
public class HPDAuthBuilderExtensions_Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static IServiceProvider BuildJwtModeProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDAuth(opts =>
        {
            opts.AppName             = Guid.NewGuid().ToString();
            opts.Jwt.Secret          = TokenServiceFixture.DefaultSecret;
            opts.Jwt.Issuer          = TokenServiceFixture.DefaultIssuer;
            opts.Jwt.Audience        = TokenServiceFixture.DefaultAudience;
        })
        .AddAuthentication();
        return services.BuildServiceProvider();
    }

    private static IServiceProvider BuildCookieOnlyProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDAuth(opts =>
        {
            opts.AppName    = Guid.NewGuid().ToString();
            opts.Jwt.Secret = null;  // cookie-only
        })
        .AddAuthentication();
        return services.BuildServiceProvider();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 128 — cookie-only mode: no JwtBearer scheme registered
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CookieOnlyMode_No_JWT_Scheme_Registered()
    {
        var sp      = BuildCookieOnlyProvider();
        var schemes = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var jwt     = await schemes.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme);

        jwt.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 129 — cookie-only mode: no "HPD" policy scheme registered
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CookieOnlyMode_No_PolicyScheme_Registered()
    {
        var sp      = BuildCookieOnlyProvider();
        var schemes = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var hpd     = await schemes.GetSchemeAsync("HPD");

        hpd.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 130 — cookie-only mode: default scheme is Cookie
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CookieOnlyMode_Default_Scheme_Is_Cookie()
    {
        var sp      = BuildCookieOnlyProvider();
        var schemes = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var def     = await schemes.GetDefaultAuthenticateSchemeAsync();

        def.Should().NotBeNull();
        def!.Name.Should().Be(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 131 — cookie-only mode: ITokenService is resolvable
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void CookieOnlyMode_ITokenService_Is_Registered()
    {
        var sp  = BuildCookieOnlyProvider();
        using var scope = sp.CreateScope();
        var svc = scope.ServiceProvider.GetService<ITokenService>();

        svc.Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 132 — cookie-only mode: GenerateTokensAsync returns empty AccessToken
    // (covered also in TokenService tests 37–39, but repeated here for completeness)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CookieOnlyMode_GenerateTokensAsync_Returns_Empty_AccessToken()
    {
        var sp = BuildCookieOnlyProvider();
        using var scope = sp.CreateScope();
        var user = new Core.Entities.ApplicationUser
        {
            Id               = Guid.NewGuid(),
            UserName         = "cookie@test.local",
            Email            = "cookie@test.local",
            InstanceId       = Guid.Empty,   // must match SingleTenantContext
            SubscriptionTier = "free",
        };

        // We need to create the user via UserManager in this scope.
        var userManager = scope.ServiceProvider
            .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Core.Entities.ApplicationUser>>();
        await userManager.CreateAsync(user, "Test@1234!");

        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        response.AccessToken.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 134 — AddAuthentication() returns the same builder (chaining)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void AddAuthentication_Returns_Same_Builder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddHPDAuth(opts =>
        {
            opts.AppName    = Guid.NewGuid().ToString();
            opts.Jwt.Secret = TokenServiceFixture.DefaultSecret;
        });

        var returnedBuilder = builder.AddAuthentication();

        returnedBuilder.Should().BeSameAs(builder);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 135 — JWT mode: "HPD" policy scheme is registered
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task AddAuthentication_JWT_Mode_Registers_PolicyScheme()
    {
        var sp      = BuildJwtModeProvider();
        var schemes = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var hpd     = await schemes.GetSchemeAsync("HPD");

        hpd.Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 136 — JWT mode: Cookie scheme is registered
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task AddAuthentication_JWT_Mode_Registers_Cookie_Scheme()
    {
        var sp      = BuildJwtModeProvider();
        var schemes = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var cookie  = await schemes.GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        cookie.Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 137 — JWT mode: JwtBearer scheme is registered
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task AddAuthentication_JWT_Mode_Registers_JwtBearer_Scheme()
    {
        var sp      = BuildJwtModeProvider();
        var schemes = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var jwt     = await schemes.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme);

        jwt.Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 138 — JWT mode: default scheme is "HPD"
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task AddAuthentication_JWT_Mode_Default_Scheme_Is_HPD()
    {
        var sp      = BuildJwtModeProvider();
        var schemes = sp.GetRequiredService<IAuthenticationSchemeProvider>();

        var defaultAuthenticate = await schemes.GetDefaultAuthenticateSchemeAsync();
        var defaultChallenge    = await schemes.GetDefaultChallengeSchemeAsync();

        defaultAuthenticate!.Name.Should().Be("HPD");
        defaultChallenge!.Name.Should().Be("HPD");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 139 — JWT mode: DefaultSignIn is Cookie
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task AddAuthentication_JWT_Mode_DefaultSignIn_Is_Cookie()
    {
        var sp      = BuildJwtModeProvider();
        var schemes = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var signIn  = await schemes.GetDefaultSignInSchemeAsync();

        signIn!.Name.Should().Be(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 140 — ITokenService is registered as Scoped
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void AddAuthentication_Registers_ITokenService_As_Scoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDAuth(opts =>
        {
            opts.AppName    = Guid.NewGuid().ToString();
            opts.Jwt.Secret = TokenServiceFixture.DefaultSecret;
        })
        .AddAuthentication();

        var descriptor = services.First(d => d.ServiceType == typeof(ITokenService));

        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 141 — null builder throws ArgumentNullException
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void AddAuthentication_Null_Builder_Throws_ArgumentNullException()
    {
        HPD.Auth.Builder.IHPDAuthBuilder? nullBuilder = null;

        Action act = () => nullBuilder!.AddAuthentication();

        act.Should().Throw<ArgumentNullException>();
    }
}
