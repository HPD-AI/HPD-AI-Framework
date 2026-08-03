using HPD.Gateway.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;

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
        var values = Materialize(candidate.Configuration, sources);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        webHost.UseKestrelHttpsConfiguration();
        webHost.ConfigureKestrel(options => options.Configure(configuration, reloadOnChange: false));
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

    private static string Protocols(GatewayListenerProtocols protocols) => protocols switch
    {
        GatewayListenerProtocols.Http1 => nameof(HttpProtocols.Http1),
        GatewayListenerProtocols.Http2 => nameof(HttpProtocols.Http2),
        GatewayListenerProtocols.Http1 | GatewayListenerProtocols.Http2 => nameof(HttpProtocols.Http1AndHttp2),
        _ => throw new InvalidOperationException("Unsupported listener protocols.")
    };
}
