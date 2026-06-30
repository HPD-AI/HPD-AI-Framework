using HPD.Base.Files.Configuration;
using HPD.Base.Files.Descriptors;
using HPD.Base.Files.Health;
using HPD.Base.Files.Objects;
using HPD.Base.Files.Policy;
using HPD.Base.Files.Providers;
using HPD.Base.Files.Runtime;
using HPD.Base.Files.Serialization;
using HPD.Base.Files.Validation;
using HPD.Base.Runtime.Builder;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Health;
using HPD.Base.Runtime.Serialization;
using HPD.Base.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Base.Files.DependencyInjection;

public static class HPDBaseFilesServiceCollectionExtensions
{
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

    public static IHPDBaseRuntimeBuilder AddHPDBaseFiles(this IHPDBaseRuntimeBuilder builder, Action<HPDBaseFilesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddHPDBaseFiles(configure);
        return builder;
    }
}
