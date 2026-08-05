using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Base;

/// <summary>Represents a hpdbase files service collection extensions.</summary>
public static class HPDBaseFilesServiceCollectionExtensions
{
    /// <summary>Executes the add hpdbase files operation.</summary>
    public static IServiceCollection AddHPDBaseFiles(this IServiceCollection services, Action<HPDBaseFilesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new HPDBaseFilesOptions();
        configure?.Invoke(options);

        services.AddOptions();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IOptions<HPDBaseFilesOptions>>(Options.Create(options));
        services.TryAddSingleton<IFileBucketRegistry, OptionsFileBucketRegistry>();
        services.TryAddSingleton<IFileStorageProviderResolver, DefaultFileStorageProviderResolver>();
        services.TryAddSingleton<IFilePolicyOrchestrator, DefaultDenyFilePolicyOrchestrator>();
        services.TryAddSingleton<IFileObjectKeyValidator, DefaultFileObjectKeyValidator>();
        services.TryAddSingleton<IFileObjectMetadataRedactor, DefaultFileObjectMetadataRedactor>();
        services.TryAddSingleton<IFileObjectService, DefaultFileObjectService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorContributor, FileDescriptorContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHealthContributor, FileHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDiagnosticContributor, FileHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseJsonTypeInfoContributor, FileJsonTypeInfoContributor>());

        return services;
    }

    /// <summary>Executes the add hpdbase files operation.</summary>
    public static IHPDBaseRuntimeBuilder AddHPDBaseFiles(this IHPDBaseRuntimeBuilder builder, Action<HPDBaseFilesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddHPDBaseFiles(configure);
        return builder;
    }
}
