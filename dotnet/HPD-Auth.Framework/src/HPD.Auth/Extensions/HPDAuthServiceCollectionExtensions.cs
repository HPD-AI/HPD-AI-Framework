using HPD.Auth.Builder;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Audit;
using HPD.Auth.Core.Options;
using HPD.Auth.Serialization;
using HPD.Auth.Infrastructure.Base;
using HPD.Events;
using HPD.Events.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HPD.Auth.Extensions;

/// <summary>
/// Extension methods on <see cref="IServiceCollection"/> for registering HPD.Auth services.
///
/// Usage (Program.cs / Startup.cs):
/// <code>
/// services.AddHPDAuth(options =>
/// {
///     options.AppName = "MyApp";
///     options.Password.RequiredLength = 12;
///     options.Lockout.MaxFailedAttempts = 3;
/// });
/// </code>
///
/// After calling AddHPDAuth() you may chain optional Auth feature registrations via
/// the returned <see cref="IHPDAuthBuilder"/>. Persistence is supplied only through
/// the host's separately configured HPD Base application graph.
/// </summary>
public static class HPDAuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers all HPD.Auth services into the DI container with a single call.
    ///
    /// Registration order:
    /// 1. Build and bind <see cref="HPDAuthOptions"/> from the <paramref name="configure"/> action.
    /// 2. Register <see cref="ITenantContext"/> → <see cref="SingleTenantContext"/> (single-tenant default).
    /// 3. Register ASP.NET Core Identity (<see cref="UserManager{TUser}"/>, <see cref="SignInManager{TUser}"/>, etc.).
    /// 4. Register ASP.NET Data Protection using the Base-backed Auth key repository.
    /// 5. Register HPD audit, session, and refresh-token store implementations.
    /// 6. Register infrastructure required by auth endpoints, including memory cache and event coordination.
    /// 7. Register no-op email and SMS senders (replaced by real implementations via TryAdd semantics).
    ///
    /// HPD Auth storage is supplied by the host's HPD Base application graph. Install
    /// <c>AuthBaseModule</c> while configuring HPD Base before building the provider.
    /// </summary>
    /// <param name="services">The application's <see cref="IServiceCollection"/>.</param>
    /// <param name="configure">
    /// Delegate that configures <see cref="HPDAuthOptions"/>. Called immediately
    /// and the resulting options object is shared across all registrations.
    /// </param>
    /// <returns>
    /// An <see cref="IHPDAuthBuilder"/> that exposes <see cref="IServiceCollection"/> and
    /// <see cref="HPDAuthOptions"/> for downstream extension packages.
    /// </returns>
    public static IHPDAuthBuilder AddHPDAuth(
        this IServiceCollection services,
        Action<HPDAuthOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        // ── Step 1: Build and register options ───────────────────────────────────
        // Build the options eagerly so downstream steps can read them synchronously.
        var options = new HPDAuthOptions();
        configure(options);

        // Register as singleton for direct injection (e.g., services that need the options object).
        services.AddSingleton(options);

        // Also register via IOptions<T> pattern so ConfigureOptions and the options
        // validation pipeline works correctly for consumers that inject IOptions<HPDAuthOptions>.
        services.Configure<HPDAuthOptions>(o => configure(o));

        // ── Step 2: Register ITenantContext ───────────────────────────────────────
        // Default: single-tenant mode — always returns Guid.Empty.
        // Multi-tenant extensions override this registration with a scoped
        // implementation that resolves InstanceId from the JWT claim or HTTP header.
        services.AddScoped<ITenantContext, SingleTenantContext>();

        // ── Step 3: Register ASP.NET Core Identity ────────────────────────────────
        // Map HPDAuthOptions → IdentityOptions for password policy, lockout, and sign-in.
        services.AddAuthenticationCore();

        var identityBuilder = services.AddIdentityCore<ApplicationUser>(identityOptions =>
        {
            // Password policy
            identityOptions.Password.RequiredLength = options.Password.RequiredLength;
            identityOptions.Password.RequireDigit = options.Password.RequireDigit;
            identityOptions.Password.RequireLowercase = options.Password.RequireLowercase;
            identityOptions.Password.RequireUppercase = options.Password.RequireUppercase;
            identityOptions.Password.RequireNonAlphanumeric = options.Password.RequireNonAlphanumeric;
            identityOptions.Password.RequiredUniqueChars = options.Password.RequiredUniqueChars;

            // Lockout policy
            identityOptions.Lockout.DefaultLockoutTimeSpan = options.Lockout.Duration;
            identityOptions.Lockout.MaxFailedAccessAttempts = options.Lockout.MaxFailedAttempts;
            identityOptions.Lockout.AllowedForNewUsers = options.Lockout.Enabled;

            // Sign-in requirements
            identityOptions.SignIn.RequireConfirmedEmail = options.Features.RequireEmailConfirmation;

            // User requirements
            identityOptions.User.RequireUniqueEmail = true;

            // Enable passkey (FIDO2) support — requires Identity schema v3
            // which adds the IdentityUserPasskey<TKey> entity to the model.
            identityOptions.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        });

        identityBuilder
            .AddRoles<ApplicationRole>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // ── Step 4: Register Base stores and ASP.NET Data Protection ──────────────
        // Register the Base-backed key repository first so its hosted startup cache
        // is loaded before ASP.NET Core's key-ring hosted service can synchronously
        // read it. The application name isolates this app's key ring.
        services.AddHPDAuthBaseStores();
        services.AddDataProtection()
            .SetApplicationName(options.AppName);

        // ── Step 5: Register auth endpoint infrastructure ───────────────────────
        // Register source-generated JSON metadata for Minimal API request/response
        // types so HPD.Auth can run in apps that disable reflection JSON fallback.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<JsonOptions>,
                HPDAuthJsonOptionsSetup>());

        // Password resend endpoints use IMemoryCache for short-lived cooldowns.
        // Register it here so MapHPDAuthEndpoints() works after AddHPDAuth()
        // without requiring host apps to know endpoint internals.
        services.AddMemoryCache();

        // Core auth endpoints emit AuthEvent instances for signup, login, logout,
        // password reset, etc. The coordinator is required even when callers do not
        // opt into the audit package; AddAudit() only attaches observers.
        services.AddHPDEvents(options => options.Lifetime = HPDEventsServiceLifetime.Singleton);

        // ── Step 6: Register no-op email and SMS senders ─────────────────────────
        // TryAdd ensures these are skipped if the caller has already registered a
        // real sender before calling AddHPDAuth() — or can be replaced afterwards
        // by calling services.AddScoped<IHPDAuthEmailSender, RealEmailSender>() before
        // the first request. If a developer forgets to configure real senders, the
        // no-op implementations log a warning without recipients, tokens, links,
        // OTP codes, IP addresses, or device details.
        services.TryAddScoped<IHPDAuthEmailSender, NoOpEmailSender>();
        services.TryAddScoped<IHPDAuthSmsSender, NoOpSmsSender>();

        return new HPDAuthBuilder(services, options);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // No-op sender implementations
    //
    // These are registered by default so that the application starts without a real
    // email/SMS provider. Both implementations log a Warning without including
    // delivery secrets or recipient identifiers.
    //
    // To replace: register your real implementation *before* AddHPDAuth() is called,
    // or use services.Replace() after the call:
    //
    //   services.Replace(ServiceDescriptor.Scoped<IHPDAuthEmailSender, SendGridEmailSender>());
    //
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// No-op <see cref="IHPDAuthEmailSender"/> that logs a warning instead of sending email.
    /// Registered when no real implementation has been provided. Replace with a
    /// real implementation for production use.
    /// </summary>
    private sealed class NoOpEmailSender : IHPDAuthEmailSender
    {
        private readonly ILogger<NoOpEmailSender> _logger;

        public NoOpEmailSender(ILogger<NoOpEmailSender> logger)
        {
            _logger = logger;
        }

        public Task SendEmailConfirmationAsync(
            string email,
            string userId,
            string token,
            CancellationToken ct = default)
        {
            _logger.LogWarning(
                "[HPD.Auth] NoOpEmailSender: Email confirmation NOT sent. " +
                "Register a real IHPDAuthEmailSender to deliver emails.");
            return Task.CompletedTask;
        }

        public Task SendPasswordResetAsync(
            string email,
            string userId,
            string token,
            CancellationToken ct = default)
        {
            _logger.LogWarning(
                "[HPD.Auth] NoOpEmailSender: Password reset email NOT sent. " +
                "Register a real IHPDAuthEmailSender to deliver emails.");
            return Task.CompletedTask;
        }

        public Task SendMagicLinkAsync(
            string email,
            string link,
            CancellationToken ct = default)
        {
            _logger.LogWarning(
                "[HPD.Auth] NoOpEmailSender: Magic link email NOT sent. " +
                "Register a real IHPDAuthEmailSender to deliver emails.");
            return Task.CompletedTask;
        }

        public Task SendLoginAlertAsync(
            string email,
            string ipAddress,
            string deviceInfo,
            CancellationToken ct = default)
        {
            _logger.LogWarning(
                "[HPD.Auth] NoOpEmailSender: Login alert email NOT sent. " +
                "Register a real IHPDAuthEmailSender to deliver emails.");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// No-op <see cref="IHPDAuthSmsSender"/> that logs a warning instead of sending SMS.
    /// Registered when no real implementation has been provided. Replace with a
    /// real implementation (e.g., Twilio) for production use.
    /// </summary>
    private sealed class NoOpSmsSender : IHPDAuthSmsSender
    {
        private readonly ILogger<NoOpSmsSender> _logger;

        public NoOpSmsSender(ILogger<NoOpSmsSender> logger)
        {
            _logger = logger;
        }

        public Task SendOtpAsync(
            string phoneNumber,
            string code,
            CancellationToken ct = default)
        {
            _logger.LogWarning(
                "[HPD.Auth] NoOpSmsSender: OTP SMS NOT sent. " +
                "Register a real IHPDAuthSmsSender to deliver SMS messages.");
            return Task.CompletedTask;
        }

        public Task SendVerificationAsync(
            string phoneNumber,
            string code,
            CancellationToken ct = default)
        {
            _logger.LogWarning(
                "[HPD.Auth] NoOpSmsSender: Verification SMS NOT sent. " +
                "Register a real IHPDAuthSmsSender to deliver SMS messages.");
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// Configures JSON serialization for HPD.Auth core endpoint DTOs.
/// </summary>
internal sealed class HPDAuthJsonOptionsSetup : IConfigureOptions<JsonOptions>
{
    public void Configure(JsonOptions options)
    {
        options.SerializerOptions.TypeInfoResolverChain.Insert(0,
            HPDAuthJsonSerializerContext.Default);
    }
}
