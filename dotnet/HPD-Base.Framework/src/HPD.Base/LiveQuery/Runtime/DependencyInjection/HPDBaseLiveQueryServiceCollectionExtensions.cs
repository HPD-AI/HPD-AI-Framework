using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Base;

public static class HPDBaseLiveQueryServiceCollectionExtensions
{
    public static IServiceCollection AddHPDBaseLiveQuery(
        this IServiceCollection services,
        Action<BaseLiveQueryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new BaseLiveQueryOptions();
        configure?.Invoke(options);
        Validate(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<DefaultBaseLiveQueryCoordinator>();
        services.TryAddSingleton<IBaseLiveQueryCoordinator>(
            static provider => provider.GetRequiredService<DefaultBaseLiveQueryCoordinator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IBaseCommittedMutationObserver, BaseLiveQueryCommittedMutationObserver>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IBaseDescriptorContributor, BaseLiveQueryDescriptorContributor>());
        return services;
    }

    private static void Validate(BaseLiveQueryOptions options)
    {
        if (options.MaxActiveSubscriptions is < 1 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(options), "Live-query subscription limit must be between 1 and 100000.");
        if (options.MaxDependenciesPerEvaluation is < 1 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(options), "Live-query dependency limit must be between 1 and 1024.");
        if (options.TransitionBufferCapacity is < 1 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(options), "Live-query transition capacity must be between 1 and 1024.");
        if (options.MaxQueryIdLength is < 16 or > 256)
            throw new ArgumentOutOfRangeException(nameof(options), "Live-query id limit must be between 16 and 256.");
    }
}
