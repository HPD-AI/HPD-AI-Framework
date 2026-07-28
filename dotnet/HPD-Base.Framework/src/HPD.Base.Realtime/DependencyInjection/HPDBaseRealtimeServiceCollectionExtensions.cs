using HPD.Base.Realtime.Configuration;
using HPD.Base.Realtime.Descriptors;
using HPD.Base.Realtime.Durability;
using HPD.Base.Realtime.Feeds;
using HPD.Base.Realtime.Health;
using HPD.Base.Realtime.Policy;
using HPD.Base.Realtime.Projection;
using HPD.Base.Realtime.Serialization;
using HPD.Base.Runtime.Builder;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Health;
using HPD.Base.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Base.Realtime.DependencyInjection;

public static class HPDBaseRealtimeServiceCollectionExtensions
{
    public static IServiceCollection AddHPDBaseRealtime(
        this IServiceCollection services,
        Action<BaseRealtimeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new BaseRealtimeOptions();
        configure?.Invoke(options);
        BaseRealtimeOptionsValidator.Validate(options);

        services.AddOptions();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IOptions<BaseRealtimeOptions>>(Options.Create(options));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<BaseRealtimeStats>();
        services.TryAddSingleton<BaseRealtimeCursorProtector>();
        services.TryAddSingleton<IBaseRealtimePolicy, DefaultBaseRealtimePolicy>();
        services.TryAddSingleton<IBaseRealtimeProjectionService, DefaultBaseRealtimeProjectionService>();
        services.TryAddSingleton<IBaseRealtimeFeedSource, DefaultBaseRealtimeFeedSource>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorContributor, BaseRealtimeDescriptorContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHealthContributor, BaseRealtimeHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDiagnosticContributor, BaseRealtimeHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseJsonTypeInfoContributor, BaseRealtimeJsonTypeInfoContributor>());

        return services;
    }

    public static IHPDBaseRuntimeBuilder AddHPDBaseRealtime(
        this IHPDBaseRuntimeBuilder builder,
        Action<BaseRealtimeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddHPDBaseRealtime(configure);
        return builder;
    }
}
