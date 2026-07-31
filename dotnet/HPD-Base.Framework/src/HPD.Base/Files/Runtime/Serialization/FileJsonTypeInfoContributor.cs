
namespace HPD.Base;

internal sealed class FileJsonTypeInfoContributor : IBaseJsonTypeInfoContributor
{
    public string Id => FileModuleIds.Module;
    public string Version => "1.0";

    public void AddTo(IBaseJsonTypeInfoRegistry registry)
    {
        registry.AddResolver(Id, HPDBaseFilesJsonSerializerContext.Default);
    }
}
