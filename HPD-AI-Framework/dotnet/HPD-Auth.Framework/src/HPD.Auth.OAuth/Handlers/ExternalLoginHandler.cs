using System.Security.Claims;
using System.Text.Json;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Events;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Options;
using HPD.Auth.Infrastructure.Data;
using HPD.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HPD.Auth.OAuth.Handlers;

/// <summary>
/// Core service invoked after an OAuth provider callback succeeds.
/// </summary>
public sealed class ExternalLoginHandler
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly HPDAuthDbContext _context;
    private readonly IEventCoordinator _eventCoordinator;
    private readonly IAuditLogger _auditLogger;
    private readonly HPDAuthOptions _options;
    private readonly ILogger<ExternalLoginHandler> _logger;

    public ExternalLoginHandler(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        HPDAuthDbContext context,
        IEventCoordinator eventCoordinator,
        IAuditLogger auditLogger,
        HPDAuthOptions options,
        ILogger<ExternalLoginHandler> logger)
    {
        _userManager      = userManager      ?? throw new ArgumentNullException(nameof(userManager));
        _signInManager    = signInManager    ?? throw new ArgumentNullException(nameof(signInManager));
        _context          = context          ?? throw new ArgumentNullException(nameof(context));
        _eventCoordinator = eventCoordinator ?? throw new ArgumentNullException(nameof(eventCoordinator));
        _auditLogger      = auditLogger      ?? throw new ArgumentNullException(nameof(auditLogger));
        _options          = options          ?? throw new ArgumentNullException(nameof(options));
        _logger           = logger           ?? throw new ArgumentNullException(nameof(logger));
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
            await _auditLogger.LogAsync(new AuditLogEntry(
                Action: "oauth_callback", Category: "authentication", Success: false,
                IpAddress: ipAddress, UserAgent: userAgent,
                ErrorMessage: "External login info not available"), ct);
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
            await _auditLogger.LogAsync(new AuditLogEntry(
                Action: "oauth_callback", Category: "authentication", Success: false,
                IpAddress: ipAddress, UserAgent: userAgent,
                ErrorMessage: "Account is locked out",
                Metadata: new { Provider = info.LoginProvider }), ct);
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
                await _auditLogger.LogAsync(new AuditLogEntry(
                    Action: "oauth_callback", Category: "authentication", Success: false,
                    IpAddress: ipAddress, UserAgent: userAgent,
                    ErrorMessage: "Account not found and auto-provisioning is disabled",
                    Metadata: new { Provider = info.LoginProvider }), ct);
                return ExternalLoginResult.Failed("Account not found and auto-provisioning is disabled");
            }

            user = await ProvisionUserAsync(info, ipAddress, userAgent, ct);
            if (user is null)
            {
                await _auditLogger.LogAsync(new AuditLogEntry(
                    Action: "oauth_callback", Category: "authentication", Success: false,
                    IpAddress: ipAddress, UserAgent: userAgent,
                    ErrorMessage: "Failed to create user account",
                    Metadata: new { Provider = info.LoginProvider }), ct);
                return ExternalLoginResult.Failed("Failed to create user account");
            }
        }

        if (user is null)
        {
            await _auditLogger.LogAsync(new AuditLogEntry(
                Action: "oauth_callback", Category: "authentication", Success: false,
                IpAddress: ipAddress, UserAgent: userAgent,
                ErrorMessage: "User not found after external login"), ct);
            return ExternalLoginResult.Failed("User not found");
        }

        user.LastLoginAt = DateTime.UtcNow;
        user.LastLoginIp = ipAddress is not null ? ipAddress[..Math.Min(ipAddress.Length, 45)] : null;
        user.Updated = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        await UpsertUserIdentityAsync(user, info, ct);

        _eventCoordinator.Emit(new UserLoggedInEvent
        {
            UserId     = user.Id,
            Email      = user.Email!,
            AuthMethod = "oauth",
            AuthContext = new AuthExecutionContext { IpAddress = ipAddress, UserAgent = userAgent },
        });

        await _auditLogger.LogAsync(new AuditLogEntry(
            Action: "oauth_login", Category: "authentication", Success: true,
            UserId: user.Id, IpAddress: ipAddress, UserAgent: userAgent,
            Metadata: new { Provider = info.LoginProvider }), ct);

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
            EmailConfirmedAt = DateTime.UtcNow,
            FirstName        = info.Principal.FindFirstValue(ClaimTypes.GivenName)
                            ?? info.Principal.FindFirstValue("first_name"),
            LastName         = info.Principal.FindFirstValue(ClaimTypes.Surname)
                            ?? info.Principal.FindFirstValue("last_name"),
            DisplayName      = displayName,
            AvatarUrl        = avatarUrl,
            Created          = DateTime.UtcNow,
            Updated          = DateTime.UtcNow,
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

        await _auditLogger.LogAsync(new AuditLogEntry(
            Action: "oauth_register", Category: "registration", Success: true,
            UserId: user.Id, IpAddress: ipAddress, UserAgent: userAgent,
            Metadata: new { Provider = info.LoginProvider, Email = email }), ct);

        return user;
    }

    private async Task UpsertUserIdentityAsync(ApplicationUser user, ExternalLoginInfo info, CancellationToken ct)
    {
        if (!_options.OAuth.StoreRawProfileData) return;

        var identityData = JsonSerializer.Serialize(
            info.Principal.Claims
                .GroupBy(c => c.Type)
                .ToDictionary(g => g.Key, g => g.Select(c => c.Value).ToArray()));

        var existing = await _context.UserIdentities
            .FirstOrDefaultAsync(i => i.UserId == user.Id && i.Provider == info.LoginProvider, ct);

        if (existing is not null)
        {
            existing.LastSignInAt = DateTime.UtcNow;
            existing.IdentityData = identityData;
            existing.UpdatedAt    = DateTime.UtcNow;
        }
        else
        {
            _context.UserIdentities.Add(new UserIdentity
            {
                UserId       = user.Id,
                InstanceId   = user.InstanceId,
                Provider     = info.LoginProvider,
                ProviderId   = info.ProviderKey,
                IdentityData = identityData,
                LastSignInAt = DateTime.UtcNow,
                CreatedAt    = DateTime.UtcNow,
            });
        }

        await _context.SaveChangesAsync(ct);
    }
}

public sealed record ExternalLoginResult(bool IsSuccess, ApplicationUser? User, string? ErrorMessage)
{
    public static ExternalLoginResult Success(ApplicationUser user) => new(true, user, null);
    public static ExternalLoginResult Failed(string error) => new(false, null, error);
}
