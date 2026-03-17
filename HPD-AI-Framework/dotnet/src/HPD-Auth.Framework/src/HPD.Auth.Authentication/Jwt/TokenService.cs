using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Models;
using HPD.Auth.Core.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace HPD.Auth.Authentication.Jwt;

/// <summary>
/// Concrete implementation of <see cref="ITokenService"/> that issues HS256-signed
/// JWT access tokens and cryptographically random, single-use refresh tokens.
///
/// <para>
/// When <see cref="JwtOptions.Secret"/> is null or empty the service still operates
/// but skips JWT generation — the <see cref="Core.Models.TokenResponse.AccessToken"/>
/// field will contain an empty string. This supports cookie-only mode where the
/// application never needs to issue JWTs.
/// </para>
///
/// <para>
/// Security stamp validation for in-flight token revocation is handled at the
/// middleware layer (<see cref="JwtBearerConfigurator"/>) not here. This service
/// is responsible only for issuance, rotation, and storage.
/// </para>
/// </summary>
internal sealed class TokenService : ITokenService
{
    private readonly HPDAuthOptions _options;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IRefreshTokenStore _refreshTokenStore;

    public TokenService(
        HPDAuthOptions options,
        UserManager<ApplicationUser> userManager,
        IRefreshTokenStore refreshTokenStore)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _refreshTokenStore = refreshTokenStore ?? throw new ArgumentNullException(nameof(refreshTokenStore));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GenerateTokensAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<TokenResponse> GenerateTokensAsync(
        ApplicationUser user,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var jti = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var accessExpiry = now + _options.Jwt.AccessTokenLifetime;

        // ── 1. Build claims ───────────────────────────────────────────────────
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti,   jti),
            new(ClaimTypes.NameIdentifier,     user.Id.ToString()),
            new("instance_id",                 user.InstanceId.ToString()),
            new("subscription_tier",           user.SubscriptionTier),
        };

        // Embed the security stamp so OnTokenValidated can do instant revocation checks.
        // ValidateSecurityStampAsync reads "AspNet.Identity.SecurityStamp" from the principal;
        // without it the stamp comparison is null == <guid> → always false → every token rejected.
        var securityStamp = await _userManager.GetSecurityStampAsync(user);
        claims.Add(new Claim("AspNet.Identity.SecurityStamp", securityStamp));

        // Add role claims via UserManager (avoids loading the navigation property).
        var roles = await _userManager.GetRolesAsync(user);
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        // Invoke additional-claims factory if the host has registered one.
        if (_options.AdditionalClaimsFactory is not null)
        {
            await _options.AdditionalClaimsFactory(user, claims);
        }

        // ── 2. Build JWT access token (only when a signing secret is configured) ─
        var accessToken = string.Empty;
        if (!string.IsNullOrEmpty(_options.Jwt.Secret))
        {
            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_options.Jwt.Secret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var jwt = new JwtSecurityToken(
                issuer: _options.Jwt.Issuer,
                audience: _options.Jwt.Audience,
                claims: claims,
                notBefore: now,
                expires: accessExpiry,
                signingCredentials: credentials);

            accessToken = new JwtSecurityTokenHandler().WriteToken(jwt);
        }

        // ── 3. Generate opaque refresh token ──────────────────────────────────
        var refreshTokenValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        var refreshTokenEntity = new RefreshToken
        {
            Id            = Guid.NewGuid(),
            Token         = refreshTokenValue,
            UserId        = user.Id,
            InstanceId    = user.InstanceId,
            JwtId         = jti,
            SecurityStamp = securityStamp,
            ExpiresAt     = now + _options.Jwt.RefreshTokenLifetime,
            CreatedAt     = now,
            IsUsed        = false,
            IsRevoked     = false,
        };

        await _refreshTokenStore.CreateAsync(refreshTokenEntity, ct);

        // ── 4. Compose TokenResponse ──────────────────────────────────────────
        var expiresInSeconds = (int)_options.Jwt.AccessTokenLifetime.TotalSeconds;
        var expiresAt        = new DateTimeOffset(accessExpiry, TimeSpan.Zero).ToUnixTimeSeconds();

        var userDto = BuildUserDto(user);

        return new TokenResponse
        {
            AccessToken  = accessToken,
            TokenType    = "bearer",
            ExpiresIn    = expiresInSeconds,
            ExpiresAt    = expiresAt,
            RefreshToken = refreshTokenValue,
            User         = userDto,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RefreshAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<TokenResponse?> RefreshAsync(
        string refreshToken,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);

        // ── 1. Load the stored token ──────────────────────────────────────────
        var stored = await _refreshTokenStore.GetByTokenAsync(refreshToken, ct);

        // ── 2. Validate all failure conditions ────────────────────────────────
        if (stored is null)
            return null;

        if (stored.IsUsed)
            return null;

        if (stored.IsRevoked)
            return null;

        if (stored.ExpiresAt < DateTime.UtcNow)
            return null;

        // ── 3. Mark old token as used (one-time-use rotation) ─────────────────
        stored.IsUsed = true;
        await _refreshTokenStore.UpdateAsync(stored, ct);

        // ── 4. Validate the associated user is still active ───────────────────
        var user = await _userManager.FindByIdAsync(stored.UserId.ToString());
        if (user is null || !user.IsActive || user.IsDeleted)
            return null;

        // ── 5. Validate security stamp (detects global logout / password reset) ─
        var currentStamp = await _userManager.GetSecurityStampAsync(user);
        if (stored.SecurityStamp != currentStamp)
            return null;

        // ── 6. Issue a new token pair ─────────────────────────────────────────
        return await GenerateTokensAsync(user, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RevokeAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<bool> RevokeAsync(string refreshToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);

        var stored = await _refreshTokenStore.GetByTokenAsync(refreshToken, ct);
        if (stored is null)
            return false;

        stored.IsRevoked = true;
        stored.RevokedAt = DateTime.UtcNow;
        await _refreshTokenStore.UpdateAsync(stored, ct);

        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RevokeAllForUserAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
        => _refreshTokenStore.RevokeAllForUserAsync(userId, ct);

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static UserTokenDto BuildUserDto(ApplicationUser user)
    {
        // Deserialize the JSONB strings stored on the entity.
        // Defaults to an empty JSON object if the column is null or empty.
        var userMetadata = TryParseJson(user.UserMetadata);
        var appMetadata  = TryParseJson(user.AppMetadata);

        return new UserTokenDto
        {
            Id               = user.Id,
            Email            = user.Email ?? string.Empty,
            EmailConfirmedAt = user.EmailConfirmedAt,
            UserMetadata     = userMetadata,
            AppMetadata      = appMetadata,
            RequiredActions  = user.RequiredActions,
            CreatedAt        = user.Created,
            SubscriptionTier = user.SubscriptionTier,
        };
    }

    private static JsonElement TryParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return JsonDocument.Parse("{}").RootElement;

        try
        {
            return JsonDocument.Parse(json).RootElement;
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}").RootElement;
        }
    }
}
