using HPD.Base;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Base.AspNetCore;

/// <summary>Represents a hpdbase files asp net core service collection extensions.</summary>
public static class HPDBaseFilesAspNetCoreServiceCollectionExtensions
{
    /// <summary>Executes the add hpdbase files asp net core operation.</summary>
    public static IServiceCollection AddHPDBaseFilesAspNetCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<FileAspNetCoreRouteMappingState>();
        services.TryAddSingleton<FileDownloadResponseWriter>();
        services.TryAddSingleton<IFileAspNetCoreRouteMappingState>(services => services.GetRequiredService<FileAspNetCoreRouteMappingState>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<JsonOptions>, HPDBaseFilesAspNetCoreJsonOptionsSetup>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorContributor, FileAspNetCoreDescriptorContributor>());
        return services;
    }

    /// <summary>Executes the add hpdbase files asp net core operation.</summary>
    public static IHPDBaseRuntimeBuilder AddHPDBaseFilesAspNetCore(this IHPDBaseRuntimeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddHPDBaseFilesAspNetCore();
        return builder;
    }
}
