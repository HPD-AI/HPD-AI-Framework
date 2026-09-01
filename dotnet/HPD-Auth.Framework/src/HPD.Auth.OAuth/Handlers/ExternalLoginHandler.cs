using System.Security.Claims;
using System.Text.Json;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Events;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Options;
using HPD.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace HPD.Auth.OAuth.Handlers;

/// <summary>
/// Core service invoked after an OAuth provider callback succeeds.
/// </summary>
public sealed class ExternalLoginHandler
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IAuthExternalIdentityProfileStore _profiles;
    private readonly IEventCoordinator _eventCoordinator;
    private readonly HPDAuthOptions _options;
    private readonly ILogger<ExternalLoginHandler> _logger;
    private readonly TimeProvider _timeProvider;

    public ExternalLoginHandler(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IAuthExternalIdentityProfileStore profiles,
        IEventCoordinator eventCoordinator,
        HPDAuthOptions options,
        ILogger<ExternalLoginHandler> logger,
        TimeProvider? timeProvider = null)
    {
        _userManager      = userManager      ?? throw new ArgumentNullException(nameof(userManager));
        _signInManager    = signInManager    ?? throw new ArgumentNullException(nameof(signInManager));
        _profiles         = profiles         ?? throw new ArgumentNullException(nameof(profiles));
        _eventCoordinator = eventCoordinator ?? throw new ArgumentNullException(nameof(eventCoordinator));
        _options          = options          ?? throw new ArgumentNullException(nameof(options));
        _logger           = logger           ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider     = timeProvider ?? TimeProvider.System;
    }

    public async Task<ExternalLoginResult> HandleCallbackAsync(
        HttpContext httpContext,
        CancellationToken ct = default)
    {
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            _logger.LogWarning("OAuth callback: external login info not available");
            return ExternalLoginResult.Failed("External login info not available");
        }

        var signInResult = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: false);

        ApplicationUser? user;

        if (signInResult.Succeeded)
        {
            user = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        }
        else if (signInResult.IsLockedOut)
        {
            _logger.LogWarning("OAuth callback: account locked out for provider {Provider}", info.LoginProvider);
            return ExternalLoginResult.Failed("Account is locked out");
        }
        else if (signInResult.RequiresTwoFactor)
        {
            user = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            _logger.LogInformation("OAuth callback: 2FA required for user {UserId}", user?.Id);
            return new ExternalLoginResult(false, user, "requires_two_factor");
        }
        else
        {
            if (!_options.OAuth.AutoProvisionUsers)
            {
                return ExternalLoginResult.Failed("Account not found and auto-provisioning is disabled");
            }

            user = await ProvisionUserAsync(info, ipAddress, userAgent, ct);
            if (user is null)
            {
                return ExternalLoginResult.Failed("Failed to create user account");
            }
        }

        if (user is null)
        {
            return ExternalLoginResult.Failed("User not found");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        user.LastLoginAt = now.UtcDateTime;
        user.LastLoginIp = ipAddress is not null ? ipAddress[..Math.Min(ipAddress.Length, 45)] : null;
        user.Updated = now.UtcDateTime;
        await _userManager.UpdateAsync(user);

        await UpsertUserIdentityAsync(user, info, ct);

        _eventCoordinator.Emit(new UserLoggedInEvent
        {
            UserId     = user.Id,
            Email      = user.Email!,
            AuthMethod = "oauth",
            AuthContext = new AuthExecutionContext { IpAddress = ipAddress, UserAgent = userAgent },
        });

        return ExternalLoginResult.Success(user);
    }

    private async Task<ApplicationUser?> ProvisionUserAsync(
        ExternalLoginInfo info, string? ipAddress, string? userAgent, CancellationToken ct)
    {
        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrEmpty(email))
        {
            _logger.LogWarning("OAuth provisioning: no email claim for provider {Provider}", info.LoginProvider);
            return null;
        }

        var existingUser = await _userManager.FindByEmailAsync(email);
        if (existingUser is not null)
        {
            if (!_options.OAuth.AutoLinkAccounts)
            {
                _logger.LogWarning("OAuth provisioning: email {Email} taken and AutoLinkAccounts=false", email);
                return null;
            }
            var linkResult = await _userManager.AddLoginAsync(existingUser, info);
            if (!linkResult.Succeeded)
            {
                _logger.LogWarning("OAuth provisioning: failed to link {Provider} to {UserId}: {Errors}",
                    info.LoginProvider, existingUser.Id,
                    string.Join("; ", linkResult.Errors.Select(e => e.Description)));
                return null;
            }
            return existingUser;
        }

        var displayName = info.Principal.FindFirstValue("name")
                       ?? info.Principal.FindFirstValue(ClaimTypes.Name)
                       ?? email;
        var avatarUrl = info.Principal.FindFirstValue("picture")
                     ?? info.Principal.FindFirstValue("avatar_url");

        var user = new ApplicationUser
        {
            UserName         = email,
            Email            = email,
            EmailConfirmed   = true,
            EmailConfirmedAt = _timeProvider.GetUtcNow().UtcDateTime,
            FirstName        = info.Principal.FindFirstValue(ClaimTypes.GivenName)
                            ?? info.Principal.FindFirstValue("first_name"),
            LastName         = info.Principal.FindFirstValue(ClaimTypes.Surname)
                            ?? info.Principal.FindFirstValue("last_name"),
            DisplayName      = displayName,
            AvatarUrl        = avatarUrl,
            Created          = _timeProvider.GetUtcNow().UtcDateTime,
            Updated          = _timeProvider.GetUtcNow().UtcDateTime,
        };

        var createResult = await _userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            _logger.LogWarning("OAuth provisioning: CreateAsync failed for {Email}: {Errors}",
                email, string.Join("; ", createResult.Errors.Select(e => e.Description)));
            return null;
        }

        var addLoginResult = await _userManager.AddLoginAsync(user, info);
        if (!addLoginResult.Succeeded)
        {
            _logger.LogWarning("OAuth provisioning: AddLoginAsync failed for {UserId}: {Errors}",
                user.Id, string.Join("; ", addLoginResult.Errors.Select(e => e.Description)));
            return null;
        }

        await _userManager.AddToRoleAsync(user, "User");

        _eventCoordinator.Emit(new UserRegisteredEvent
        {
            UserId             = user.Id,
            Email              = email,
            RegistrationMethod = info.LoginProvider,
            AuthContext        = new AuthExecutionContext { IpAddress = ipAddress, UserAgent = userAgent },
        });

        return user;
    }

    private async Task UpsertUserIdentityAsync(ApplicationUser user, ExternalLoginInfo info, CancellationToken ct)
    {
        if (!_options.OAuth.StoreRawProfileData) return;

        string identityData = CanonicalIdentityClaims(info.Principal.Claims);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        await _profiles.UpsertAsync(new AuthExternalIdentityProfileUpdate
        {
            UserId = user.Id,
            Provider = info.LoginProvider,
            ProviderId = info.ProviderKey,
            CanonicalIdentityJson = identityData,
            SignedInAt = now,
        }, ct);
    }

    private static string CanonicalIdentityClaims(IEnumerable<Claim> claims)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (IGrouping<string, Claim> group in claims
                .GroupBy(static claim => claim.Type, StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(group.Key);
                writer.WriteStartArray();
                foreach (string value in group.Select(static claim => claim.Value)
                    .Order(StringComparer.Ordinal))
                    writer.WriteStringValue(value);
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }
}

public sealed record ExternalLoginResult(bool IsSuccess, ApplicationUser? User, string? ErrorMessage)
{
    public static ExternalLoginResult Success(ApplicationUser user) => new(true, user, null);
    public static ExternalLoginResult Failed(string error) => new(false, null, error);
}
