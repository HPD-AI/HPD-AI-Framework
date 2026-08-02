
namespace HPD.Base;

internal sealed class BaseRealtimeJsonTypeInfoContributor : IBaseJsonTypeInfoContributor
{
    /// <summary>Gets the ID.</summary>
    public string Id => BaseRealtimeModuleIds.Module;
    /// <summary>Gets the version.</summary>
    public string Version => "1.0";

    /// <summary>Executes the add to operation.</summary>
    public void AddTo(IBaseJsonTypeInfoRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.AddResolver(Id, HPDBaseRealtimeJsonSerializerContext.Default);
    }
}
