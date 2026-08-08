using System.Collections.Immutable;
using HPD.Gateway.Core;
using HPD.Gateway.Hosting;
using HPD.Gateway.Status;
using HPD.Gateway.Yarp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HPD.Gateway;

public static class GatewayServiceCollectionExtensions
{
    public static IServiceCollection AddHpdGateway(
        this IServiceCollection services,
        Action<GatewayBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(HpdGatewayCompositionMarker)))
            throw new InvalidOperationException("AddHpdGateway may be called only once for a governed host.");

        var staged = new ServiceCollection();
        staged.AddReverseProxy();
        staged.AddHpdGatewayYarpPublication();
        staged.AddHpdGatewayYarpMaterialization();
        staged.AddHpdGatewayStatus();

        var builder = new GatewayBuilder(staged);
        configure(builder);
        var state = builder.Seal();
        staged.AddSingleton(state);
        staged.AddSingleton(static provider => CreateCapabilities(
            provider.GetRequiredService<GatewayCompositionState>(),
            provider.GetService<GatewayHostRuntimeStatus>()));
        staged.AddSingleton<HpdGatewayCompositionMarker>();
        staged.AddSingleton<HpdGatewayMappingMarker>();
        staged.AddSingleton<IGatewayEndpointMappingParticipant>(static provider =>
            provider.GetRequiredService<HpdGatewayMappingMarker>());
        staged.AddSingleton<IGatewayApplicationPipelineParticipant, GatewayNativePolicyPipeline>();
        staged.AddSingleton<GatewayNodeActivator>();
        staged.AddSingleton<IGatewayNodeActivator>(static provider =>
            provider.GetRequiredService<GatewayNodeActivator>());
        staged.AddSingleton<IGatewayNodeEffectiveReader>(static provider =>
            provider.GetRequiredService<GatewayNodeActivator>());
        staged.AddSingleton<IHostedService, GatewayInitialActivationService>();

        foreach (var descriptor in staged)
            services.Add(descriptor);
        return services;
    }

    private static HostCapabilitySnapshot CreateCapabilities(
        GatewayCompositionState state,
        GatewayHostRuntimeStatus? host)
    {
        var listeners = host is null
            ? []
            : host.Running.Configuration.DataListeners
                .Select(static listener => new ListenerCapability(
                    listener.Id,
                    ListenerRole.DataPlane,
                    ConvertProtocols(listener.Protocols),
                    listener.Tls.Sni
                        .Select(static entry => entry.HostnamePattern)
                        .Order(StringComparer.Ordinal)
                        .ToImmutableArray(),
                    true))
                .ToImmutableArray();
        return HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            Listeners = listeners,
            InstalledFamilies = state.InstalledFamilies,
            RequestInspectors = state.RequestInspectors,
            UpstreamResilienceProfiles = state.ResilienceProfiles,
            OutputCacheProfiles = state.OutputCacheProfiles,
            ProtectedCredentialHeaders = state.ProtectedCredentialHeaders,
            AuthorizationPolicies = state.AuthorizationPolicies,
            CorsPolicies = state.CorsPolicies,
            TrafficAdmissionPolicies = state.TrafficAdmissionPolicies,
            RequestTimeoutPolicies = state.RequestTimeoutPolicies,
            AllowInspectionFileSpill = state.AllowInspectionFileSpill
        });
    }

    private static ListenerProtocols ConvertProtocols(GatewayListenerProtocols protocols)
    {
        var result = ListenerProtocols.None;
        if (protocols.HasFlag(GatewayListenerProtocols.Http1)) result |= ListenerProtocols.Http1;
        if (protocols.HasFlag(GatewayListenerProtocols.Http2)) result |= ListenerProtocols.Http2;
        return result;
    }
}
