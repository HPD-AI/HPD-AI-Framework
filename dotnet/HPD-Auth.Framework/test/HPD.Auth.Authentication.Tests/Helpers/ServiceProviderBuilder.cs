using HPD.Auth.Authentication.Extensions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Options;
using HPD.Auth.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Auth.Authentication.Tests.Helpers;

/// <summary>
/// Builds a fully configured DI service provider for TokenService and
/// authentication tests over the real HPD Base SQLite authority.
/// </summary>
internal static class ServiceProviderBuilder
{
    /// <summary>
    /// Creates a root ServiceProvider with HPDAuth registered.
    /// Multiple scopes can be created from the same provider — they share the
    /// same isolated Base store, simulating multiple HTTP requests.
    /// </summary>
    public static TestServiceProvider CreateProvider(Action<HPDAuthOptions>? configure = null)
    {
        var services = new ServiceCollection();

        // Required for ASP.NET Identity and authentication middleware logging.
        services.AddLogging();

        // Use a unique DB name per call so tests are isolated.
        var dbName = Guid.NewGuid().ToString();

        services.AddHPDAuth(opts =>
        {
            opts.AppName = dbName; // unique in-memory DB per test
            opts.Jwt.Secret              = TokenServiceFixture.DefaultSecret;
            opts.Jwt.Issuer              = TokenServiceFixture.DefaultIssuer;
            opts.Jwt.Audience            = TokenServiceFixture.DefaultAudience;
            opts.Jwt.AccessTokenLifetime  = TimeSpan.FromMinutes(15);
            opts.Jwt.RefreshTokenLifetime = TimeSpan.FromDays(14);
            configure?.Invoke(opts);
        })
        .UseBaseTestHost()
        .AddAuthentication();

        var serviceProvider = services.BuildServiceProvider();
        serviceProvider.InitializeHPDAuthBaseTestHostAsync().GetAwaiter().GetResult();

        return new TestServiceProvider(serviceProvider);
    }

    /// <summary>
    /// Creates a scoped service provider with HPDAuth registered.
    /// Each call creates its own isolated ServiceProvider (and in-memory DB).
    /// Use this for tests that do not require cross-scope token operations.
    /// </summary>
    public static IServiceScope CreateScope(Action<HPDAuthOptions>? configure = null)
    {
        return CreateProvider(configure).CreateOwnedScope();
    }

    /// <summary>
    /// Creates a user via UserManager within the scope and returns it.
    /// The user will have a hashed password "Test@1234" so Identity is happy.
    /// </summary>
    public static async Task<ApplicationUser> CreateUserAsync(
        IServiceScope scope,
        Action<ApplicationUser>? configure = null)
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName         = $"{Guid.NewGuid():N}@test.example",
            Email            = $"{Guid.NewGuid():N}@test.example",
            // SingleTenantContext always returns Guid.Empty, so InstanceId must match
            // for the global query filters on RefreshToken/ApplicationUser to work.
            InstanceId       = Guid.Empty,
            SubscriptionTier = "pro",
            IsActive         = true,
            IsDeleted        = false,
            EmailConfirmedAt = DateTime.UtcNow,
            Created          = DateTime.UtcNow,
        };
        configure?.Invoke(user);

        var result = await userManager.CreateAsync(user, "Test@1234!");
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to create test user: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        return user;
    }

    /// <summary>Owns a test root while safely bridging its asynchronous Base disposal.</summary>
    internal sealed class TestServiceProvider(ServiceProvider inner) : IServiceProvider, IDisposable, IAsyncDisposable
    {
        private int _disposed;

        /// <inheritdoc />
        public object? GetService(Type serviceType) => inner.GetService(serviceType);

        /// <summary>Creates an asynchronously disposable scope with a safe synchronous test bridge.</summary>
        public TestServiceScope CreateScope()
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            return new TestServiceScope(inner.CreateAsyncScope(), this, disposeRoot: false);
        }

        internal TestServiceScope CreateOwnedScope()
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            return new TestServiceScope(inner.CreateAsyncScope(), this, disposeRoot: true);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                inner.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                await inner.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Owns one test scope and its root Base authority.</summary>
    internal sealed class TestServiceScope(
        AsyncServiceScope inner,
        TestServiceProvider root,
        bool disposeRoot) : IServiceScope, IAsyncDisposable
    {
        private int _disposed;

        /// <inheritdoc />
        public IServiceProvider ServiceProvider => inner.ServiceProvider;

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            try { inner.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            finally { if (disposeRoot) root.Dispose(); }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            try { await inner.DisposeAsync().ConfigureAwait(false); }
            finally { if (disposeRoot) await root.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
