namespace HPD.Base.AspNetCore;

/// <summary>Defines the ifile asp net core route mapping state contract.</summary>
public interface IFileAspNetCoreRouteMappingState
{
    /// <summary>Gets the is mapped.</summary>
    bool IsMapped { get; }
    /// <summary>Gets the route prefix.</summary>
    string RoutePrefix { get; }
}

internal sealed class FileAspNetCoreRouteMappingState : IFileAspNetCoreRouteMappingState
{
    /// <summary>Gets or sets the is mapped.</summary>
    public bool IsMapped { get; private set; }
    /// <summary>Gets or sets the route prefix.</summary>
    public string RoutePrefix { get; private set; } = "/base/files";

    /// <summary>Executes the mark mapped operation.</summary>
    public void MarkMapped(string routePrefix)
    {
        IsMapped = true;
        RoutePrefix = routePrefix;
    }
}
