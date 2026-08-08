using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using HPD.Gateway.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing;

namespace HPD.Gateway.Hosting;

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
        GatewayHostRuntimeStatus? host = endpoints.ServiceProvider.GetService<GatewayHostRuntimeStatus>();
        var surfaces = new Dictionary<string, GatewayListenerRole>(StringComparer.Ordinal);
        bool managementEndpoint = false;
        foreach (Endpoint endpoint in endpoints.DataSources.SelectMany(static source => source.Endpoints))
        {
            GatewayEndpointRoleMetadata[] roles = endpoint.Metadata
                .GetOrderedMetadata<GatewayEndpointRoleMetadata>().ToArray();
            bool gatewayEndpoint = roles.Length != 0 ||
                endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName?.StartsWith("HpdGateway", StringComparison.Ordinal) == true;
            if (!gatewayEndpoint) continue;
            if (roles.Length != 1)
                throw new InvalidOperationException("Every HPD Gateway endpoint must declare exactly one listener role.");
            GatewayEndpointRoleMetadata role = roles[0];
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
