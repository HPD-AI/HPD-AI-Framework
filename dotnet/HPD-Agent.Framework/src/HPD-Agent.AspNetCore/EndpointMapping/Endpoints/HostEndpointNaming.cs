using Microsoft.AspNetCore.Builder;

namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

internal static class HostEndpointNaming
{
    internal static RouteHandlerBuilder WithHostName(
        this RouteHandlerBuilder builder,
        string endpointName,
        string operationName) =>
        builder.WithName($"{endpointName}.{operationName}");
}
