using FluentAssertions;
using HPD.Auth.Auth.Authorization.Tests.Helpers;
using HPD.Auth.Authorization.Extensions;
using HPD.Auth.Authorization.Handlers;
using HPD.Auth.Authorization.Middleware;
using HPD.Auth.Authorization.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Authorization.Tests.Extensions;

[Trait("Category", "DI")]
public class AddAuthorizationDITests
{
    private static IServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Register stub implementations for services the scoped handlers depend on.
        services.AddScoped<IAppPermissionService, StubAppPermissionService>();
        services.AddScoped<ISubscriptionService, StubSubscriptionService>();
        services.AddScoped<IFeatureFlagService, StubFeatureFlagService>();

        var builder = new StubHPDAuthBuilder(services);
        builder.AddAuthorization();

        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AppAccessHandler_registered_as_IAuthorizationHandler()
    {
        var provider = BuildProvider();

        using var scope = provider.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IAuthorizationHandler>();

        handlers.Should().ContainSingle(h => h is AppAccessHandler);
    }

    [Fact]
    public void ResourceOwnerHandler_registered_as_IAuthorizationHandler()
    {
        var provider = BuildProvider();

        using var scope = provider.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IAuthorizationHandler>();

        handlers.Should().ContainSingle(h => h is ResourceOwnerHandler);
    }

    [Fact]
    public void SubscriptionTierHandler_registered_as_IAuthorizationHandler()
    {
        var provider = BuildProvider();

        using var scope = provider.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IAuthorizationHandler>();

        handlers.Should().ContainSingle(h => h is SubscriptionTierHandler);
    }

    [Fact]
    public void RateLimitHandler_registered_as_IAuthorizationHandler()
    {
        var provider = BuildProvider();

        using var scope = provider.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IAuthorizationHandler>();

        handlers.Should().ContainSingle(h => h is RateLimitHandler);
    }

    [Fact]
    public void FeatureFlagHandler_registered_as_IAuthorizationHandler()
    {
        var provider = BuildProvider();

        using var scope = provider.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IAuthorizationHandler>();

        handlers.Should().ContainSingle(h => h is FeatureFlagHandler);
    }

    [Fact]
    public void IRateLimitService_registered_as_singleton_InMemoryRateLimitService()
    {
        var provider = BuildProvider();

        var instance1 = provider.GetRequiredService<IRateLimitService>();
        var instance2 = provider.GetRequiredService<IRateLimitService>();

        instance1.Should().BeOfType<InMemoryRateLimitService>();
        instance1.Should().BeSameAs(instance2, "singleton should return the same instance");
    }

    [Fact]
    public void IAuthorizationMiddlewareResultHandler_registered_as_HPDAuthorizationMiddlewareResultHandler()
    {
        var provider = BuildProvider();

        var handler = provider.GetRequiredService<IAuthorizationMiddlewareResultHandler>();

        handler.Should().BeOfType<HPDAuthorizationMiddlewareResultHandler>();
    }

    [Fact]
    public void Custom_IRateLimitService_overrides_in_memory_default()
    {
        var provider = BuildProvider(services =>
        {
            // Registered after AddAuthorization() — should win
            services.AddSingleton<IRateLimitService, FakeRateLimitService>();
        });

        var service = provider.GetRequiredService<IRateLimitService>();

        service.Should().BeOfType<FakeRateLimitService>();
    }

    [Fact]
    public void AddAuthorization_returns_same_builder_instance()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new StubHPDAuthBuilder(services);

        var returned = builder.AddAuthorization();

        returned.Should().BeSameAs(builder);
    }

    private sealed class FakeRateLimitService : IRateLimitService
    {
        public Task<bool> CheckRateLimitAsync(string key, int maxRequests, TimeSpan window, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    private sealed class StubAppPermissionService : IAppPermissionService
    {
        public Task<bool> UserHasAppAccessAsync(Guid userId, string appId, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private sealed class StubSubscriptionService : ISubscriptionService
    {
        public Task<SubscriptionInfo?> GetUserSubscriptionAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<SubscriptionInfo?>(null);
    }

    private sealed class StubFeatureFlagService : IFeatureFlagService
    {
        public Task<bool> IsEnabledAsync(string featureKey, FeatureContext context, CancellationToken ct = default)
            => Task.FromResult(false);
    }
}
