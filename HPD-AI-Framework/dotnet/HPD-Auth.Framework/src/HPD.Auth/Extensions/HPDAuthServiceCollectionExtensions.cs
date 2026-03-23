using HPD.Auth.Builder;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Options;
using HPD.Auth.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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
/// After calling AddHPDAuth() you may chain additional registrations via the returned
/// <see cref="IHPDAuthBuilder"/>. Phase 2/3 packages (e.g., HPD.Auth.PostgreSQL,
/// HPD.Auth.Admin) provide extension methods on IHPDAuthBuilder.
/// </summary>
public static class HPDAuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers all HPD.Auth services into the DI container with a single call.
    ///
    /// Registration order:
    /// 1. Build and bind <see cref="HPDAuthOptions"/> from the <paramref name="configure"/> action.
    /// 2. Register <see cref="ITenantContext"/> → <see cref="SingleTenantContext"/> (single-tenant default).
    /// 3. Register <see cref="HPDAuthDbContext"/> with the EF Core in-memory provider.
    /// 4. Register ASP.NET Core Identity (<see cref="UserManager{TUser}"/>, <see cref="SignInManager{TUser}"/>, etc.).
    /// 5. Register ASP.NET Data Protection, persisting keys to the database.
    /// 6. Register HPD store implementations (<see cref="IAuditLogger"/>, <see cref="ISessionManager"/>, <see cref="IRefreshTokenStore"/>).
    /// 7. Register no-op email and SMS senders (replaced by real implementations via TryAdd semantics).
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

        // ── Step 3: Register HPDAuthDbContext with SQLite in-memory ───────────────
        // Uses SQLite with a shared in-memory database. This behaves like an in-memory
        // provider (all RAM, no disk I/O) but correctly supports ComplexProperty().ToJson()
        // required for ASP.NET Identity passkey storage (IdentityUserPasskey.Data).
        // A keep-alive connection prevents SQLite from discarding the database when
        // all DbContext connections close.
        var sqliteConnectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = $"file:{options.AppName}?mode=memory&cache=shared",
            ForeignKeys = true
        }.ToString();
        var keepAliveConnection = new Microsoft.Data.Sqlite.SqliteConnection(sqliteConnectionString);
        keepAliveConnection.Open();
        services.AddSingleton(keepAliveConnection);

        services.AddDbContext<HPDAuthDbContext>((sp, dbOptions) =>
        {
            dbOptions.UseSqlite(sqliteConnectionString);
        }, ServiceLifetime.Scoped);

        // ── Step 4: Register ASP.NET Core Identity ────────────────────────────────
        // Map HPDAuthOptions → IdentityOptions for password policy, lockout, and sign-in.
        services.AddIdentity<ApplicationUser, ApplicationRole>(identityOptions =>
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
        })
        .AddEntityFrameworkStores<HPDAuthDbContext>()
        .AddDefaultTokenProviders();

        // ── Step 5: Register ASP.NET Data Protection ──────────────────────────────
        // Persist encryption keys to the database so they survive app restarts and
        // are shared across load-balanced nodes. The application name scopes the key
        // ring to this specific app, preventing cross-app cookie/token forgery.
        services.AddDataProtection()
            .SetApplicationName(options.AppName)
            .PersistKeysToDbContext<HPDAuthDbContext>();

        // ── Step 6: Register HPD store implementations ────────────────────────────
        services.AddScoped<IAuditLogger, HPD.Auth.Infrastructure.Stores.AuditLogStore>();
        services.AddScoped<ISessionManager, HPD.Auth.Infrastructure.Stores.SessionStore>();
        services.AddScoped<IRefreshTokenStore, HPD.Auth.Infrastructure.Stores.RefreshTokenStore>();

        // ── Step 7: Register no-op email and SMS senders ─────────────────────────
        // TryAdd ensures these are skipped if the caller has already registered a
        // real sender before calling AddHPDAuth() — or can be replaced afterwards
        // by calling services.AddScoped<IHPDAuthEmailSender, RealEmailSender>() before
        // the first request. If a developer forgets to configure real senders, the
        // no-op implementations log a warning with the content that would have been
        // sent, making the omission immediately visible in logs.
        services.TryAddScoped<IHPDAuthEmailSender, NoOpEmailSender>();
        services.TryAddScoped<IHPDAuthSmsSender, NoOpSmsSender>();

        // ── Step 8: Return fluent builder ─────────────────────────────────────────
        return new HPDAuthBuilder(services, options);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // No-op sender implementations
    //
    // These are registered by default so that the application starts without a real
    // email/SMS provider. Both implementations log a Warning that includes the full
    // content that would have been delivered, making it immediately obvious in
    // structured logs that a real sender needs to be wired up.
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
                "Register a real IHPDAuthEmailSender to deliver emails. " +
                "Recipient={Email} UserId={UserId} Token={Token}",
                email, userId, token);
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
                "Register a real IHPDAuthEmailSender to deliver emails. " +
                "Recipient={Email} UserId={UserId} Token={Token}",
                email, userId, token);
            return Task.CompletedTask;
        }

        public Task SendMagicLinkAsync(
            string email,
            string link,
            CancellationToken ct = default)
        {
            _logger.LogWarning(
                "[HPD.Auth] NoOpEmailSender: Magic link email NOT sent. " +
                "Register a real IHPDAuthEmailSender to deliver emails. " +
                "Recipient={Email} Link={Link}",
                email, link);
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
                "Register a real IHPDAuthEmailSender to deliver emails. " +
                "Recipient={Email} IpAddress={IpAddress} DeviceInfo={DeviceInfo}",
                email, ipAddress, deviceInfo);
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
                "Register a real IHPDAuthSmsSender to deliver SMS messages. " +
                "Recipient={PhoneNumber} Code={Code}",
                phoneNumber, code);
            return Task.CompletedTask;
        }

        public Task SendVerificationAsync(
            string phoneNumber,
            string code,
            CancellationToken ct = default)
        {
            _logger.LogWarning(
                "[HPD.Auth] NoOpSmsSender: Verification SMS NOT sent. " +
                "Register a real IHPDAuthSmsSender to deliver SMS messages. " +
                "Recipient={PhoneNumber} Code={Code}",
                phoneNumber, code);
            return Task.CompletedTask;
        }
    }
}
