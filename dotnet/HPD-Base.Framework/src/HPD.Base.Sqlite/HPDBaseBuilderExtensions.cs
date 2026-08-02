using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Sqlite;
/// <summary>Represents hPDBase Builder Extensions.</summary>
public static class HPDBaseBuilderExtensions
{
    /// <summary>Performs use Sqlite.</summary>
    public static HPDBaseBuilder UseSqlite(this HPDBaseBuilder builder, Action<HPDBaseSqliteOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Use(new Installer(configure));
    }

    private sealed class Installer(Action<HPDBaseSqliteOptions>? configure) : IHPDBaseBuilderExtension
    {
        /// <summary>Gets id.</summary>
        public string Id => "sqlite";
        /// <summary>Gets is Record Provider.</summary>
        public bool IsRecordProvider => true;
        /// <summary>Gets supports Required Indexes.</summary>
        public bool SupportsRequiredIndexes => true;

        /// <summary>Performs configure.</summary>
        public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections) => services.AddHPDBaseSqliteStore(options =>
        {
            configure?.Invoke(options);
            options.Collections = collections.ToArray();
        });
        /// <summary>Performs initialize Async.</summary>
        public ValueTask InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            services.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseSqliteStore(services);
            return ValueTask.CompletedTask;
        }
    }
}
