using HPD.Base;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Base.AspNetCore;

public static class HPDBaseRealtimeAspNetCoreServiceCollectionExtensions
{
    public static IServiceCollection AddHPDBaseRealtimeAspNetCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<JsonOptions>, HPDBaseRealtimeAspNetCoreJsonOptionsSetup>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorContributor, BaseRealtimeAspNetCoreDescriptorContributor>());
        services.TryAddSingleton<BaseRealtimeWebSocketEndpoint>();
        return services;
    }

    public static IHPDBaseRuntimeBuilder AddHPDBaseRealtimeAspNetCore(this IHPDBaseRuntimeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddHPDBaseRealtimeAspNetCore();
        return builder;
    }
}
