namespace HPD.Base.AspNetCore;

public interface IFileAspNetCoreRouteMappingState
{
    bool IsMapped { get; }
    string RoutePrefix { get; }
}

internal sealed class FileAspNetCoreRouteMappingState : IFileAspNetCoreRouteMappingState
{
    public bool IsMapped { get; private set; }
    public string RoutePrefix { get; private set; } = "/base/files";

    public void MarkMapped(string routePrefix)
    {
        IsMapped = true;
        RoutePrefix = routePrefix;
    }
}
