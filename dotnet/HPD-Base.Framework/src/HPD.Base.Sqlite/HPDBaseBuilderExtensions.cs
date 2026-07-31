using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Sqlite;

public static class HPDBaseBuilderExtensions
{
    public static HPDBaseBuilder UseSqlite(
        this HPDBaseBuilder builder,
        Action<HPDBaseSqliteOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Use(new Installer(configure));
    }

    private sealed class Installer(Action<HPDBaseSqliteOptions>? configure) : IHPDBaseBuilderExtension
    {
        public string Id => "sqlite";
        public bool IsRecordProvider => true;
        public bool SupportsRequiredIndexes => false;

        public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections) =>
            services.AddHPDBaseSqliteStore(options =>
            {
                configure?.Invoke(options);
                options.CollectionIds = collections.Select(static item => item.Id).ToArray();
                options.Collections = collections.ToArray();
            });

        public void Initialize(IServiceProvider services) =>
            services.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseSqliteStore(services);
    }
}
