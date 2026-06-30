using HPD.Base.Files.InMemory.Configuration;
using HPD.Base.Files.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Base.Files.InMemory.DependencyInjection;

public static class HPDBaseFilesInMemoryServiceCollectionExtensions
{
    public static IServiceCollection AddHPDBaseFilesInMemoryProvider(
        this IServiceCollection services,
        Action<HPDBaseFilesInMemoryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new HPDBaseFilesInMemoryOptions();
        configure?.Invoke(options);

        services.AddOptions();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IOptions<HPDBaseFilesInMemoryOptions>>(Options.Create(options));
        services.TryAddSingleton<InMemoryFileStorageProvider>();
        services.AddSingleton<IFileStorageProvider>(provider => provider.GetRequiredService<InMemoryFileStorageProvider>());
        return services;
    }
}
