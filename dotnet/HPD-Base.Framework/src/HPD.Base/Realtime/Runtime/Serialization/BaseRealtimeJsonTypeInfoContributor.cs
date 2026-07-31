
namespace HPD.Base;

internal sealed class BaseRealtimeJsonTypeInfoContributor : IBaseJsonTypeInfoContributor
{
    public string Id => BaseRealtimeModuleIds.Module;
    public string Version => "1.0";

    public void AddTo(IBaseJsonTypeInfoRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.AddResolver(Id, HPDBaseRealtimeJsonSerializerContext.Default);
    }
}
