using HPD.Base;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Base.AspNetCore;

/// <summary>Represents a hpdbase realtime asp net core service collection extensions.</summary>
public static class HPDBaseRealtimeAspNetCoreServiceCollectionExtensions
{
    /// <summary>Executes the add hpdbase realtime asp net core operation.</summary>
    public static IServiceCollection AddHPDBaseRealtimeAspNetCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<JsonOptions>, HPDBaseRealtimeAspNetCoreJsonOptionsSetup>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorContributor, BaseRealtimeAspNetCoreDescriptorContributor>());
        services.TryAddSingleton<BaseRealtimeWebSocketEndpoint>();
        return services;
    }

    /// <summary>Executes the add hpdbase realtime asp net core operation.</summary>
    public static IHPDBaseRuntimeBuilder AddHPDBaseRealtimeAspNetCore(this IHPDBaseRuntimeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddHPDBaseRealtimeAspNetCore();
        return builder;
    }
}
