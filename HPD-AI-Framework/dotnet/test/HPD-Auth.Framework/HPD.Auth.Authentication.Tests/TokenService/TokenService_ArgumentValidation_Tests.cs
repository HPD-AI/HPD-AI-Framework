using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using HPD.Auth.Authentication.Tests.Helpers;
using HPD.Auth.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Authentication.Tests.TokenService;

/// <summary>
/// Extra gap tests: argument-null / argument-empty guards that are implemented
/// in the production code but were not yet covered by the §1 test suite.
///
/// These complement TESTS.md §1 by exercising the ArgumentNullException and
/// ArgumentException.ThrowIfNullOrEmpty guards at the top of each public method.
/// </summary>
[Trait("Category", "TokenService")]
[Trait("Section", "1-ArgumentValidation")]
public class TokenService_ArgumentValidation_Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // GenerateTokensAsync — null user throws ArgumentNullException
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Passing null as the user to GenerateTokensAsync should throw
    /// ArgumentNullException immediately via ArgumentNullException.ThrowIfNull.
    /// </summary>
    [Fact]
    public async Task GenerateTokensAsync_Null_User_Throws_ArgumentNullException()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITokenService>();

        Func<Task> act = () => svc.GenerateTokensAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RefreshAsync — null token throws ArgumentException
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Passing null as refreshToken to RefreshAsync should throw
    /// ArgumentException via ArgumentException.ThrowIfNullOrEmpty.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_Null_Token_Throws_ArgumentException()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITokenService>();

        Func<Task> act = () => svc.RefreshAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Passing an empty string as refreshToken to RefreshAsync should throw
    /// ArgumentException via ArgumentException.ThrowIfNullOrEmpty.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_Empty_Token_Throws_ArgumentException()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITokenService>();

        Func<Task> act = () => svc.RefreshAsync(string.Empty);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RevokeAsync — null/empty token throws ArgumentException
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Passing null as refreshToken to RevokeAsync should throw ArgumentException.
    /// </summary>
    [Fact]
    public async Task RevokeAsync_Null_Token_Throws_ArgumentException()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITokenService>();

        Func<Task> act = () => svc.RevokeAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Passing an empty string as refreshToken to RevokeAsync should throw
    /// ArgumentException.
    /// </summary>
    [Fact]
    public async Task RevokeAsync_Empty_Token_Throws_ArgumentException()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITokenService>();

        Func<Task> act = () => svc.RevokeAsync(string.Empty);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TokenResponse — ExpiresAt consistent with ExpiresIn
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ExpiresAt (Unix timestamp) should equal approximately DateTimeOffset.UtcNow
    /// plus ExpiresIn seconds. Ensures the two expiry fields are self-consistent
    /// rather than being set from independent calculations.
    /// </summary>
    [Fact]
    public async Task GenerateTokensAsync_ExpiresAt_Consistent_With_ExpiresIn()
    {
        var lifetime = TimeSpan.FromMinutes(42);
        using var scope = ServiceProviderBuilder.CreateScope(opts =>
            opts.Jwt.AccessTokenLifetime = lifetime);

        var user    = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc     = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var before  = DateTimeOffset.UtcNow;
        var response = await svc.GenerateTokensAsync(user);
        var after   = DateTimeOffset.UtcNow;

        // ExpiresIn must equal the configured lifetime in seconds.
        response.ExpiresIn.Should().Be((int)lifetime.TotalSeconds);

        // ExpiresAt (Unix seconds) should fall between:
        //   before + lifetime   and   after + lifetime
        // with a small 5-second tolerance for test execution time.
        var expectedMin = before.AddSeconds(response.ExpiresIn).ToUnixTimeSeconds() - 5;
        var expectedMax = after.AddSeconds(response.ExpiresIn).ToUnixTimeSeconds()  + 5;

        response.ExpiresAt.Should().BeInRange(expectedMin, expectedMax,
            "ExpiresAt must be consistent with ExpiresIn and the time of issuance");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GenerateTokensAsync — each call produces a unique JTI
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every issued access token must carry a unique jti claim. Duplicate JTIs
    /// would undermine replay-protection mechanisms.
    /// </summary>
    [Fact]
    public async Task GenerateTokensAsync_Each_Call_Generates_Unique_Jti()
    {
        using var sp = ServiceProviderBuilder.CreateProvider();

        string jti1, jti2;

        using (var scope1 = sp.CreateScope())
        {
            var user = await ServiceProviderBuilder.CreateUserAsync(scope1);
            var svc  = scope1.ServiceProvider.GetRequiredService<ITokenService>();
            var r    = await svc.GenerateTokensAsync(user);
            var jwt  = new JwtSecurityTokenHandler().ReadJwtToken(r.AccessToken);
            jti1     = jwt.Id;
        }

        // Second issuance for a different user (or the same user) must yield a
        // different JTI — the implementation uses Guid.NewGuid() per call.
        using (var scope2 = sp.CreateScope())
        {
            var user = await ServiceProviderBuilder.CreateUserAsync(scope2);
            var svc  = scope2.ServiceProvider.GetRequiredService<ITokenService>();
            var r    = await svc.GenerateTokensAsync(user);
            var jwt  = new JwtSecurityTokenHandler().ReadJwtToken(r.AccessToken);
            jti2     = jwt.Id;
        }

        jti1.Should().NotBe(jti2, "each token issuance must generate a unique jti");
    }
}
