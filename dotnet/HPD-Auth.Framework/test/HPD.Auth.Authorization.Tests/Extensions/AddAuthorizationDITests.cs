using FluentAssertions;
using HPD.Auth.Auth.Authorization.Tests.Helpers;
using HPD.Auth.Authorization.Extensions;
using HPD.Auth.Authorization.Handlers;
using HPD.Auth.Authorization.Services;
using Microsoft.AspNetCore.Authorization;
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
    public void FeatureFlagHandler_registered_as_IAuthorizationHandler()
    {
        var provider = BuildProvider();

        using var scope = provider.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IAuthorizationHandler>();

        handlers.Should().ContainSingle(h => h is FeatureFlagHandler);
    }

    [Fact]
    public async Task Optional_authorization_services_registered_as_safe_defaults()
    {
        var provider = BuildProvider();

        using var scope = provider.CreateScope();

        var appAccess = await scope.ServiceProvider.GetRequiredService<IAppPermissionService>()
            .UserHasAppAccessAsync(Guid.NewGuid(), "app");
        appAccess
            .Should()
            .BeFalse();

        var subscription = await scope.ServiceProvider.GetRequiredService<ISubscriptionService>()
            .GetUserSubscriptionAsync(Guid.NewGuid());
        subscription
            .Should()
            .BeNull();

        var featureEnabled = await scope.ServiceProvider.GetRequiredService<IFeatureFlagService>()
            .IsEnabledAsync("feature", new FeatureContext("user", "free", []));
        featureEnabled
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Custom_optional_authorization_services_override_defaults()
    {
        var provider = BuildProvider(services =>
        {
            services.AddScoped<IAppPermissionService, StubAppPermissionService>();
            services.AddScoped<ISubscriptionService, StubSubscriptionService>();
            services.AddScoped<IFeatureFlagService, StubFeatureFlagService>();
        });

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IAppPermissionService>()
            .Should()
            .BeOfType<StubAppPermissionService>();

        scope.ServiceProvider.GetRequiredService<ISubscriptionService>()
            .Should()
            .BeOfType<StubSubscriptionService>();

        scope.ServiceProvider.GetRequiredService<IFeatureFlagService>()
            .Should()
            .BeOfType<StubFeatureFlagService>();
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
