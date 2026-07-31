using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Base;

internal static class HPDBaseFilesVolatileServiceCollectionExtensions
{
    internal static IServiceCollection AddHPDBaseFilesVolatileProvider(
        this IServiceCollection services,
        Action<HPDBaseVolatileFileStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new HPDBaseVolatileFileStoreOptions();
        configure?.Invoke(options);

        services.AddOptions();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IOptions<HPDBaseVolatileFileStoreOptions>>(Options.Create(options));
        services.TryAddSingleton<VolatileFileStorageProvider>();
        services.AddSingleton<IFileStorageProvider>(provider => provider.GetRequiredService<VolatileFileStorageProvider>());
        return services;
    }
}
