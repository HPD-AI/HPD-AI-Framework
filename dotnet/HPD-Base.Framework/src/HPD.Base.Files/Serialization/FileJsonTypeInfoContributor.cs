using HPD.Base.Files.Serialization;
using HPD.Base.Files.Runtime;
using HPD.Base.Serialization;

namespace HPD.Base.Files.Serialization;

internal sealed class FileJsonTypeInfoContributor : IBaseJsonTypeInfoContributor
{
    public string Id => FileModuleIds.Module;
    public string Version => "1.0";

    public void AddTo(IBaseJsonTypeInfoRegistry registry)
    {
        registry.AddResolver(Id, HPDBaseFilesJsonSerializerContext.Default);
    }
}
