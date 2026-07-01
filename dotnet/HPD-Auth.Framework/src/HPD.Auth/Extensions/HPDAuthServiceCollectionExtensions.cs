using HPD.Auth.Builder;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Options;
using HPD.Auth.Serialization;
using HPD.Auth.Infrastructure.Data;
using HPD.Events;
using HPD.Events.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
/// })
/// .UseSqlite(connectionString);
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
    /// 3. Register ASP.NET Core Identity (<see cref="UserManager{TUser}"/>, <see cref="SignInManager{TUser}"/>, etc.).
    /// 4. Register ASP.NET Data Protection, persisting keys to the configured auth database.
    /// 5. Register HPD store implementations (<see cref="IAuditLogger"/>, <see cref="ISessionManager"/>, <see cref="IRefreshTokenStore"/>).
    /// 6. Register infrastructure required by auth endpoints, including memory cache and event coordination.
    /// 7. Register no-op email and SMS senders (replaced by real implementations via TryAdd semantics).
    ///
    /// Storage is intentionally not implicit. Call a storage extension such as
    /// <see cref="UseSqlite(IHPDAuthBuilder, string)"/> or
    /// <see cref="UseInMemorySqliteForTests(IHPDAuthBuilder)"/> on the returned builder.
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

        services.AddOptions<HPDAuthStorageOptions>()
            .Validate(static o => o.IsConfigured,
                "HPD.Auth storage is required. Chain a storage provider such as UseSqlite(...) after AddHPDAuth(...).")
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<HPDAuthStorageOptions>,
                HPDAuthStorageOptionsValidator>());

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

        services.TryAddScoped<IUserStore<ApplicationUser>,
            UserStore<ApplicationUser, ApplicationRole, HPDAuthDbContext, Guid,
                IdentityUserClaim<Guid>,
                IdentityUserRole<Guid>,
                IdentityUserLogin<Guid>,
                IdentityUserToken<Guid>,
                IdentityRoleClaim<Guid>,
                IdentityUserPasskey<Guid>>>();

        services.TryAddScoped<IRoleStore<ApplicationRole>,
            RoleStore<ApplicationRole, HPDAuthDbContext, Guid,
                IdentityUserRole<Guid>,
                IdentityRoleClaim<Guid>>>();

        // ── Step 4: Register ASP.NET Data Protection ──────────────────────────────
        // Persist encryption keys to the configured database so they survive app
        // restarts and are shared across load-balanced nodes. The application name
        // scopes the key ring to this app, preventing cross-app cookie/token forgery.
        services.AddDataProtection()
            .SetApplicationName(options.AppName)
            .PersistKeysToDbContext<HPDAuthDbContext>();

        // ── Step 5: Register HPD store implementations ────────────────────────────
        services.AddScoped<IAuditLogger, HPD.Auth.Infrastructure.Stores.AuditLogStore>();
        services.AddScoped<ISessionManager, HPD.Auth.Infrastructure.Stores.SessionStore>();
        services.AddScoped<IRefreshTokenStore, HPD.Auth.Infrastructure.Stores.RefreshTokenStore>();

        // ── Step 6: Register auth endpoint infrastructure ───────────────────────
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
        services.AddHPDEvents(options => options.Lifetime = HPDEventsServiceLifetime.Scoped);

        // ── Step 7: Register no-op email and SMS senders ─────────────────────────
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

    /// <summary>
    /// Configures HPD.Auth to store identity, session, audit, refresh-token, and
    /// Data Protection state in SQLite.
    /// </summary>
    /// <param name="builder">The HPD.Auth builder returned by <see cref="AddHPDAuth(IServiceCollection, Action{HPDAuthOptions})"/>.</param>
    /// <param name="connectionString">The SQLite connection string for the auth database.</param>
    /// <returns>The same <see cref="IHPDAuthBuilder"/> for fluent chaining.</returns>
    public static IHPDAuthBuilder UseSqlite(
        this IHPDAuthBuilder builder,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A SQLite connection string is required.", nameof(connectionString));

        builder.Services.Configure<HPDAuthStorageOptions>(o =>
        {
            o.IsConfigured = true;
            o.ProviderName = "sqlite";
            o.IsEphemeral = false;
        });

        builder.Services.AddDbContext<HPDAuthDbContext>((_, dbOptions) =>
        {
            dbOptions.UseSqlite(connectionString)
                // HPDAuthDbContext has runtime tenant query filters. EF migration
                // snapshots do not produce schema operations for those filters, so
                // MigrateAsync can otherwise fail on a non-schema model warning.
                .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        }, ServiceLifetime.Scoped);

        return builder;
    }

    /// <summary>
    /// Configures HPD.Auth to use a shared SQLite in-memory database for tests.
    /// This storage is process-local and must not be used for production hosts.
    /// </summary>
    /// <param name="builder">The HPD.Auth builder returned by <see cref="AddHPDAuth(IServiceCollection, Action{HPDAuthOptions})"/>.</param>
    /// <returns>The same <see cref="IHPDAuthBuilder"/> for fluent chaining.</returns>
    public static IHPDAuthBuilder UseInMemorySqliteForTests(this IHPDAuthBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var sqliteConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"file:{builder.Options.AppName}?mode=memory&cache=shared",
            ForeignKeys = true
        }.ToString();

        var keepAliveConnection = new SqliteConnection(sqliteConnectionString);
        keepAliveConnection.Open();

        builder.Services.Configure<HPDAuthStorageOptions>(o =>
        {
            o.IsConfigured = true;
            o.ProviderName = "sqlite-memory";
            o.IsEphemeral = true;
        });

        builder.Services.AddSingleton(keepAliveConnection);
        builder.Services.AddDbContext<HPDAuthDbContext>((sp, dbOptions) =>
        {
            dbOptions.UseSqlite(sp.GetRequiredService<SqliteConnection>())
                // See UseSqlite: tenant query filters are runtime policy, not schema.
                .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        }, ServiceLifetime.Scoped);

        return builder;
    }

    /// <summary>
    /// Creates the configured HPD.Auth database schema for development and tests.
    /// Production hosts should use an explicit migration pipeline instead.
    /// </summary>
    /// <param name="serviceProvider">The application service provider.</param>
    /// <param name="cancellationToken">A token that can cancel schema initialization.</param>
    /// <returns>A task that completes when the database has been initialized.</returns>
    public static async Task InitializeHPDAuthDevelopmentDatabaseAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HPDAuthDbContext>();
        await dbContext.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies pending HPD.Auth database migrations for the configured storage provider.
    /// Production hosts should call this from an explicit deployment or startup
    /// migration step, not from arbitrary request handling.
    /// </summary>
    /// <param name="serviceProvider">The application service provider.</param>
    /// <param name="cancellationToken">A token that can cancel migration application.</param>
    /// <returns>A task that completes when pending migrations have been applied.</returns>
    public static async Task MigrateHPDAuthDatabaseAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HPDAuthDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies that the configured HPD.Auth database has no pending migrations.
    /// Production hosts can call this after their deployment migration step to fail
    /// startup before serving traffic against an old schema.
    /// </summary>
    /// <param name="serviceProvider">The application service provider.</param>
    /// <param name="cancellationToken">A token that can cancel migration inspection.</param>
    /// <returns>A task that completes when the database is confirmed current.</returns>
    /// <exception cref="InvalidOperationException">Thrown when one or more migrations are pending.</exception>
    public static async Task ValidateHPDAuthDatabaseMigratedAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HPDAuthDbContext>();
        var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
        var pending = pendingMigrations.ToArray();
        if (pending.Length > 0)
        {
            throw new InvalidOperationException(
                "HPD.Auth database has pending migrations: " +
                string.Join(", ", pending) +
                ". Apply migrations before serving production traffic.");
        }
    }

    private sealed class HPDAuthStorageOptions
    {
        public bool IsConfigured { get; set; }

        public string? ProviderName { get; set; }

        public bool IsEphemeral { get; set; }
    }

    private sealed class HPDAuthStorageOptionsValidator : IValidateOptions<HPDAuthStorageOptions>
    {
        private readonly IServiceProvider _serviceProvider;

        public HPDAuthStorageOptionsValidator(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public ValidateOptionsResult Validate(string? name, HPDAuthStorageOptions options)
        {
            var environment = _serviceProvider.GetService<IHostEnvironment>();
            if (environment is null || !environment.IsProduction() || !options.IsEphemeral)
            {
                return ValidateOptionsResult.Success;
            }

            return ValidateOptionsResult.Fail(
                "HPD.Auth production hosts must use durable auth storage. " +
                "UseSqlite(...) or another durable provider is required; " +
                "UseInMemorySqliteForTests() is only for tests.");
        }
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
