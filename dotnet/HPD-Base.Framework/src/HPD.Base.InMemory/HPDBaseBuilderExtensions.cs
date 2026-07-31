using HPD.Base;
using HPD.Base.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.InMemory;

public static class HPDBaseBuilderExtensions
{
    public static HPDBaseBuilder UseInMemory(
        this HPDBaseBuilder builder,
        Action<HPDBaseInMemoryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Use(new Installer(configure));
    }

    private sealed class Installer(Action<HPDBaseInMemoryOptions>? configure) : IHPDBaseBuilderExtension
    {
        public string Id => "inMemory";
        public bool IsRecordProvider => true;
        public bool SupportsRequiredIndexes => false;

        public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections)
        {
            services.AddHPDBaseInMemoryStore(options =>
            {
                configure?.Invoke(options);
                options.CollectionIds = collections.Select(static item => item.Id).ToArray();
                options.Collections = collections.ToArray();
            });
            services.AddHPDBaseFilesInMemoryProvider();
        }

        public void Initialize(IServiceProvider services) =>
            services.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseInMemoryStore(services);
    }
}
