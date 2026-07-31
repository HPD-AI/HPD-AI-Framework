
namespace HPD.Base;

internal sealed class BaseRecordStoreInitializer(
    IServiceProvider services,
    HPDBaseInstalledFeatures features) : IBaseApplicationInitializer
{
    private readonly object _gate = new();
    private bool _initialized;

    public void Initialize()
    {
        if (_initialized)
            return;
        lock (_gate)
        {
            if (_initialized)
                return;
            foreach (IHPDBaseBuilderExtension extension in features.Extensions)
                extension.Initialize(services);
            _initialized = true;
        }
    }
}
