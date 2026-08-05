
namespace HPD.Base;

internal sealed class FileJsonTypeInfoContributor : IBaseJsonTypeInfoContributor
{
    /// <summary>Gets the ID.</summary>
    public string Id => FileModuleIds.Module;
    /// <summary>Gets the version.</summary>
    public string Version => "1.0";

    /// <summary>Executes the add to operation.</summary>
    public void AddTo(IBaseJsonTypeInfoRegistry registry)
    {
        registry.AddResolver(Id, HPDBaseFilesJsonSerializerContext.Default);
    }
}
