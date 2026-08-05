// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.AspNetCore.EndpointMapping.Endpoints;
using HPD.Agent.ClientTools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.AspNetCore;

public static class HPDClientToolProviderEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps an application-gateway provider attachment route that always
    /// requires <see cref="IClientToolProviderConnectionAuthorizer"/>.
    /// </summary>
    public static IEndpointConventionBuilder
        MapAuthorizedHPDClientToolProviderConnection(
            this IEndpointRouteBuilder endpoints,
            string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        var registry = endpoints.ServiceProvider
            .GetRequiredService<IClientToolProviderRegistry>();
        return ClientToolProviderEndpoints.MapConnection(
            endpoints,
            registry,
            pattern,
            requireAuthorization: true);
    }
}
