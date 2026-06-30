using HPD.Base.AspNetCore.Results;
using HPD.Base.Files.AspNetCore.Descriptors;
using HPD.Base.Files.AspNetCore.EndpointMapping;
using HPD.Base.Files.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace HPD.Base.Files.AspNetCore;

public static class HPDBaseFilesEndpointRouteBuilderExtensions
{
    public static RouteGroupBuilder MapHPDBaseFilesApi(this IEndpointRouteBuilder endpoints, string routePrefix = "/base/files")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        if (string.IsNullOrWhiteSpace(routePrefix) || !routePrefix.StartsWith('/'))
            throw new ArgumentException("Route prefix must be an absolute route pattern.", nameof(routePrefix));

        var normalizedRoutePrefix = routePrefix == "/" ? routePrefix : routePrefix.TrimEnd('/');
        var group = endpoints.MapGroup(normalizedRoutePrefix);
        endpoints.ServiceProvider.GetRequiredService<FileAspNetCoreRouteMappingState>().MarkMapped(normalizedRoutePrefix);
        FileObjectEndpoints.Map(group);
        return group;
    }
}
