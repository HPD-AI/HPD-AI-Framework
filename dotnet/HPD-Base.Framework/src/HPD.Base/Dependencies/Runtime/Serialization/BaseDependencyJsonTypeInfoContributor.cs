using HPD.Base.Serialization;

namespace HPD.Base.Dependencies.Serialization;

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
