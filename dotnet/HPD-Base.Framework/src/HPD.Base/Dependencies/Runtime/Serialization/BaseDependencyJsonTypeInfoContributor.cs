
namespace HPD.Base;

internal sealed class BaseDependencyJsonTypeInfoContributor : IBaseJsonTypeInfoContributor
{
    public string Id => BaseDependencyModuleIds.Module;
    public string Version => "1.0";

    public void AddTo(IBaseJsonTypeInfoRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.AddResolver(Id, HPDBaseDependenciesJsonSerializerContext.Default);
    }
}
