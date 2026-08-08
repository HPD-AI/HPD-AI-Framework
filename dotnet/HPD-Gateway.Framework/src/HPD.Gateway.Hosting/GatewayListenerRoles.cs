using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using HPD.Gateway.Abstractions;
using Microsoft.Extensions.DependencyInjection;

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
            GatewayEndpointRoleMetadata? required = context.GetEndpoint()?.Metadata.GetMetadata<GatewayEndpointRoleMetadata>();
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
