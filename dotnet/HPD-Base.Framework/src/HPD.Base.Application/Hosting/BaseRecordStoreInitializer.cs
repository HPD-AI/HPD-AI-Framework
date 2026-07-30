using HPD.Base.Application.Sessions;
using HPD.Base.InMemory.DependencyInjection;
using HPD.Base.Runtime.Stores;
using HPD.Base.Sqlite.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Application.Hosting;

internal sealed class BaseRecordStoreInitializer(
    IServiceProvider services,
    HPDBaseInstalledFeatures features) : IBaseApplicationInitializer
{
    private readonly object _gate = new();
    private bool _initialized;

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        lock (_gate)
        {
            if (_initialized)
            {
                return;
            }

            IRecordStoreRegistry registry =
                services.GetRequiredService<IRecordStoreRegistry>();
            if (features.Provider == "inMemory")
            {
                registry.AddHPDBaseInMemoryStore(services);
            }
            else if (features.Provider == "sqlite")
            {
                registry.AddHPDBaseSqliteStore(services);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unknown HPD.BASE record provider '{features.Provider}'.");
            }

            _initialized = true;
        }
    }
}
