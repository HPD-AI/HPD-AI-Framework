using HPD.Auth.ControlPlane;
using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Auth;

/// <summary>Maps BASE endpoints through the validated HPD.Auth control-plane convention.</summary>
public static class HPDBaseControlPlaneEndpointRouteBuilderExtensions
{
    /// <summary>Maps the selected BASE control-plane surface.</summary>
    public static RouteGroupBuilder MapHPDBaseControlPlane(
        this IEndpointRouteBuilder endpoints,
        HPDBaseControlPlaneEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RoutePrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Profile);
        if (!options.RoutePrefix.StartsWith("/", StringComparison.Ordinal) || options.RoutePrefix.Length > 256 || options.RoutePrefix.Any(char.IsControl))
            throw new ArgumentException("Route prefix must be an absolute bounded route pattern.", nameof(options));

        string prefix = options.RoutePrefix == "/" ? options.RoutePrefix : options.RoutePrefix.TrimEnd('/');
        if (options.MapFiles)
            endpoints.ServiceProvider.GetRequiredService<FileAspNetCoreRouteMappingState>().MarkMapped(prefix + "/files");
        if (options.MapRealtime && endpoints.ServiceProvider.GetService<BaseRealtimeWebSocketEndpoint>() is null)
            throw new InvalidOperationException("Realtime ASP.NET services must be installed before mapping realtime endpoints.");
        string profile = new(options.Profile.AsSpan());
        RouteGroupBuilder group = endpoints.MapHPDControlPlaneGroup(prefix, profile)
            .WithMetadata(new HPDBaseSelectedControlPlaneProfileMetadata(profile));
        group.AddEndpointFilter(static async (context, next) =>
        {
            try
            {
                return await next(context).ConfigureAwait(false);
            }
            catch (HPDBaseAuthProjectionException exception)
            {
                return Results.Problem(
                    statusCode: exception.StatusCode,
                    title: exception.StatusCode == StatusCodes.Status403Forbidden
                        ? "The authenticated actor could not be projected."
                        : "The control-plane correlation context is unavailable.",
                    extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
            }
        });
        group.MapHPDBaseControlPlaneEndpoints(
            endpoints,
            new HPDBaseControlPlaneEndpointSelection
            {
                MapRecords = options.MapRecords,
                MapRegisteredReads = options.MapRegisteredReads,
                MapAdministration = options.MapAdministration,
                MapArtifactAdministration = options.MapArtifactAdministration,
                MapPolicyExplain = options.MapPolicyExplain,
                MapFiles = options.MapFiles,
                MapRealtime = options.MapRealtime,
                MapClientGeneration = options.MapClientGeneration
            },
            (endpoint, descriptor) => endpoint.RequireHPDControlPlaneCapability(endpoints, descriptor.Capability!));
        return group;
    }
}
