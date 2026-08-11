using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Hosting;

namespace HPD.Gateway;

public interface IHpdGatewayListenerFeature
{
    ListenerId ListenerId { get; }
    GatewayListenerRole Role { get; }
    string EndpointSurfaceId { get; }
}

internal sealed record HpdGatewayListenerFeature(
    ListenerId ListenerId,
    GatewayListenerRole Role,
    string EndpointSurfaceId) : IHpdGatewayListenerFeature;

public sealed record GatewayEndpointRoleMetadata(
    GatewayListenerRole Role,
    string EndpointSurfaceId,
    bool RequireListenerFeature = false);

public static class GatewayListenerRoleExtensions
{
    public static void ValidateHpdGatewayEndpointRoles(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ValidateEndpointRoles(
            endpoints.DataSources,
            endpoints.ServiceProvider.GetService<GatewayHostRuntimeStatus>());
    }

    internal static void ValidateEndpointRoles(
        IEnumerable<EndpointDataSource> dataSources,
        GatewayHostRuntimeStatus? host)
    {
        var surfaces = new Dictionary<string, GatewayListenerRole>(StringComparer.Ordinal);
        var ownedRoutes = new HashSet<(string Method, string Pattern)>();
        bool managementEndpoint = false;
        foreach (Endpoint endpoint in dataSources.SelectMany(static source => source.Endpoints))
        {
            GatewayEndpointRoleMetadata[] roles = endpoint.Metadata
                .GetOrderedMetadata<GatewayEndpointRoleMetadata>().ToArray();
            bool gatewayEndpoint = roles.Length != 0 ||
                endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName?.StartsWith("HpdGateway", StringComparison.Ordinal) == true;
            if (!gatewayEndpoint) continue;
            if (roles.Length != 1)
                throw new InvalidOperationException("Every HPD Gateway endpoint must declare exactly one listener role.");
            GatewayEndpointRoleMetadata role = roles[0];
            if (endpoint is RouteEndpoint routeEndpoint)
            {
                string pattern = routeEndpoint.RoutePattern.RawText ?? string.Empty;
                string[] methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.ToArray() ?? ["*"];
                foreach (string method in methods)
                {
                    if (!ownedRoutes.Add((method, pattern)))
                        throw new InvalidOperationException("Gateway endpoint route ownership is duplicated.");
                }
            }
            if (!surfaces.TryAdd(role.EndpointSurfaceId, role.Role) && surfaces[role.EndpointSurfaceId] != role.Role)
                throw new InvalidOperationException("A Gateway endpoint surface cannot declare conflicting listener roles.");
            if (role.Role == GatewayListenerRole.Management) managementEndpoint = true;
        }
        if (host is null) return;
        if (managementEndpoint && host.Running.Configuration.ManagementListeners.IsDefaultOrEmpty)
            throw new InvalidOperationException("Gateway Admin endpoints require an HPD-owned management listener.");
        foreach ((string surface, GatewayListenerRole role) in surfaces)
        {
            bool realized = role switch
            {
                GatewayListenerRole.DataPlane => StringComparer.Ordinal.Equals(surface, "gateway-data") &&
                    !host.Running.Configuration.DataListeners.IsDefaultOrEmpty,
                GatewayListenerRole.Management => host.Running.Configuration.ManagementListeners.Any(
                    listener => StringComparer.Ordinal.Equals(listener.EndpointSurfaceId, surface)),
                _ => false,
            };
            if (!realized)
                throw new InvalidOperationException("Gateway endpoint role metadata is not realized by the running host candidate.");
        }
    }

    public static TBuilder WithHpdGatewayEndpointRole<TBuilder>(
        this TBuilder builder,
        GatewayListenerRole role,
        string endpointSurfaceId,
        bool requireListenerFeature = false)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointSurfaceId);
        builder.Add(endpoint => endpoint.Metadata.Add(new GatewayEndpointRoleMetadata(role, endpointSurfaceId, requireListenerFeature)));
        return builder;
    }

    public static IApplicationBuilder UseHpdGatewayListenerRoles(this IApplicationBuilder application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return application.Use(async (context, next) =>
        {
            GatewayEndpointRoleMetadata[] requiredRoles = context.GetEndpoint()?.Metadata
                .GetOrderedMetadata<GatewayEndpointRoleMetadata>().ToArray() ?? [];
            if (requiredRoles.Length > 1)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            GatewayEndpointRoleMetadata? required = requiredRoles.SingleOrDefault();
            if (required is null)
            {
                await next(context);
                return;
            }
            IHpdGatewayListenerFeature? actual = context.Features.Get<IHpdGatewayListenerFeature>();
            if (actual is null && !required.RequireListenerFeature &&
                context.RequestServices.GetService<GatewayHostRuntimeStatus>() is null)
            {
                await next(context);
                return;
            }
            if (actual is null || actual.Role != required.Role ||
                !StringComparer.Ordinal.Equals(actual.EndpointSurfaceId, required.EndpointSurfaceId))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            await next(context);
        });
    }
}

internal sealed class GatewayEndpointRoleStartupFilter(
    IEnumerable<EndpointDataSource> dataSources,
    GatewayHostRuntimeStatus? host = null) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => application =>
    {
        next(application);
        GatewayListenerRoleExtensions.ValidateEndpointRoles(dataSources, host);
    };
}
