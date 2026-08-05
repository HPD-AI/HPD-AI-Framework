using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Base;

internal static class HPDBaseFilesInMemoryServiceCollectionExtensions
{
    internal static IServiceCollection AddHPDBaseFilesInMemoryProvider(
        this IServiceCollection services,
        Action<HPDBaseInMemoryFileStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new HPDBaseInMemoryFileStoreOptions();
        configure?.Invoke(options);

        services.AddOptions();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IOptions<HPDBaseInMemoryFileStoreOptions>>(Options.Create(options));
        services.TryAddSingleton<InMemoryFileStorageProvider>();
        services.AddSingleton<IFileStorageProvider>(provider => provider.GetRequiredService<InMemoryFileStorageProvider>());
        return services;
    }
}
