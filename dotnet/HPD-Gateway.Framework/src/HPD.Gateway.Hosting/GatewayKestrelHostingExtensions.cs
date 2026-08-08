using HPD.Gateway.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HPD.Gateway.Hosting;

public static class GatewayKestrelHostingExtensions
{
    public static IWebHostBuilder UseHpdGatewayHost(
        this IWebHostBuilder webHost,
        GatewayHostCandidate candidate,
        Action<GatewayCertificateSourceRegistryBuilder> configureCertificates)
    {
        ArgumentNullException.ThrowIfNull(webHost);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(configureCertificates);
        var sourceBuilder = new GatewayCertificateSourceRegistryBuilder();
        configureCertificates(sourceBuilder);
        var sources = sourceBuilder.Build();
        var runtimeStatus = new GatewayHostRuntimeStatus(candidate);
        webHost.ConfigureServices(services =>
        {
            if (services.Any(static descriptor => descriptor.ServiceType == typeof(GatewayHostRuntimeStatus)))
                throw new InvalidOperationException("HPD Gateway Hosting may be installed only once.");
            services.AddSingleton(runtimeStatus);
            services.AddSingleton<IHostedService, GatewayHostLifetimeObserver>();
        });
        return ConfigureKestrel(webHost, candidate, sources);
    }

    internal static IWebHostBuilder UseHpdGatewayHost(
        this IWebHostBuilder webHost,
        IServiceCollection services,
        GatewayHostCandidate candidate,
        Action<GatewayCertificateSourceRegistryBuilder> configureCertificates)
    {
        ArgumentNullException.ThrowIfNull(webHost);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(configureCertificates);
        var sourceBuilder = new GatewayCertificateSourceRegistryBuilder();
        configureCertificates(sourceBuilder);
        var sources = sourceBuilder.Build();
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(GatewayHostRuntimeStatus)))
            throw new InvalidOperationException("HPD Gateway Hosting may be installed only once.");
        services.AddSingleton(new GatewayHostRuntimeStatus(candidate));
        services.AddSingleton<IHostedService, GatewayHostLifetimeObserver>();
        return ConfigureKestrel(webHost, candidate, sources);
    }

    private static IWebHostBuilder ConfigureKestrel(
        IWebHostBuilder webHost,
        GatewayHostCandidate candidate,
        GatewayCertificateSourceRegistry sources)
    {
        var values = Materialize(candidate.Configuration, sources);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        webHost.UseKestrelHttpsConfiguration();
        Dictionary<int, HpdGatewayListenerFeature> identities = candidate.Configuration.DataListeners
            .ToDictionary(static value => (int)value.Port,
                static value => new HpdGatewayListenerFeature(value.Id, GatewayListenerRole.DataPlane, "gateway-data"));
        foreach (GatewayManagementListenerDeclaration listener in candidate.Configuration.ManagementListeners)
            identities.Add(listener.Port, new HpdGatewayListenerFeature(
                listener.Id, GatewayListenerRole.Management, listener.EndpointSurfaceId));
        webHost.ConfigureKestrel((context, options) =>
        {
            if (!context.HostingEnvironment.IsDevelopment() && candidate.Configuration.ManagementListeners.Any(static value => value.Tls is null))
                throw new InvalidOperationException("Cleartext Gateway management listeners are restricted to Development.");
            options.ConfigureEndpointDefaults(listenOptions =>
            {
                if (listenOptions.IPEndPoint is { } endpoint &&
                    identities.TryGetValue(endpoint.Port, out HpdGatewayListenerFeature? identity))
                    listenOptions.Use(next => connection =>
                    {
                        connection.Features.Set<IHpdGatewayListenerFeature>(identity);
                        return next(connection);
                    });
            });
            options.Configure(configuration, reloadOnChange: false);
        });
        return webHost;
    }

    internal static Dictionary<string, string?> Materialize(
        GatewayHostConfiguration configuration,
        GatewayCertificateSourceRegistry sources)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var listener in configuration.DataListeners)
        {
            var prefix = $"Endpoints:{listener.Id.Value}";
            values.Add($"{prefix}:Url", Address(listener));
            values.Add($"{prefix}:Protocols", Protocols(listener.Protocols));
            foreach (var sni in listener.Tls.Sni)
            {
                var source = sources.Resolve(sni.Certificate, sni.HostnamePattern);
                var sniPrefix = $"{prefix}:Sni:{sni.HostnamePattern}";
                values.Add($"{sniPrefix}:Certificate:Path", source.Path);
                if (source.Password is not null) values.Add($"{sniPrefix}:Certificate:Password", source.Password);
            }
        }
        foreach (GatewayManagementListenerDeclaration listener in configuration.ManagementListeners)
        {
            string prefix = $"Endpoints:{listener.Id.Value}";
            values.Add($"{prefix}:Url", Address(listener));
            values.Add($"{prefix}:Protocols", Protocols(listener.Protocols));
            if (listener.Tls is null) continue;
            foreach (GatewaySniTlsDeclaration sni in listener.Tls.Sni)
            {
                var source = sources.Resolve(sni.Certificate, sni.HostnamePattern);
                string sniPrefix = $"{prefix}:Sni:{sni.HostnamePattern}";
                values.Add($"{sniPrefix}:Certificate:Path", source.Path);
                if (source.Password is not null) values.Add($"{sniPrefix}:Certificate:Password", source.Password);
            }
        }
        return values;
    }

    private static string Address(GatewayHttpsListenerDeclaration listener)
    {
        var host = listener.Binding switch
        {
            GatewayListenerBindingKind.AnyIp => "*",
            GatewayListenerBindingKind.Loopback => "localhost",
            GatewayListenerBindingKind.IpAddress when listener.IpAddress!.Contains(':') => $"[{listener.IpAddress}]",
            GatewayListenerBindingKind.IpAddress => listener.IpAddress!,
            _ => throw new InvalidOperationException("Unsupported listener binding.")
        };
        return $"https://{host}:{listener.Port}";
    }

    private static string Address(GatewayManagementListenerDeclaration listener)
    {
        string host = listener.Binding switch
        {
            GatewayListenerBindingKind.AnyIp => "*",
            GatewayListenerBindingKind.Loopback => "localhost",
            GatewayListenerBindingKind.IpAddress when listener.IpAddress!.Contains(':') => $"[{listener.IpAddress}]",
            GatewayListenerBindingKind.IpAddress => listener.IpAddress!,
            _ => throw new InvalidOperationException("Unsupported listener binding."),
        };
        return $"{(listener.Tls is null ? "http" : "https")}://{host}:{listener.Port}";
    }

    private static string Protocols(GatewayListenerProtocols protocols) => protocols switch
    {
        GatewayListenerProtocols.Http1 => nameof(HttpProtocols.Http1),
        GatewayListenerProtocols.Http2 => nameof(HttpProtocols.Http2),
        GatewayListenerProtocols.Http1 | GatewayListenerProtocols.Http2 => nameof(HttpProtocols.Http1AndHttp2),
        _ => throw new InvalidOperationException("Unsupported listener protocols.")
    };
}
