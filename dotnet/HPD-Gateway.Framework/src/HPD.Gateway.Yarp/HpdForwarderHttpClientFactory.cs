using System.Globalization;
using System.Net.Http;
using Yarp.ReverseProxy.Forwarder;

namespace HPD.Gateway.Yarp;

internal sealed class HpdForwarderHttpClientFactory : ForwarderHttpClientFactory
{
    internal const string UseProxyMetadata = "hpd.gateway.transport.use-proxy";
    internal const string ConnectTimeoutTicksMetadata = "hpd.gateway.transport.connect-timeout-ticks";
    internal const string ResilienceProfileMetadata = "hpd.gateway.resilience.profile";
    internal const string ResilienceVersionMetadata = "hpd.gateway.resilience.version";
    private readonly GatewayUpstreamResilienceProvider? _resilience;

    public HpdForwarderHttpClientFactory() { }

    public HpdForwarderHttpClientFactory(IEnumerable<GatewayUpstreamResilienceProvider> registries)
    {
        var values = registries.ToArray();
        if (values.Length > 1) throw new InvalidOperationException("Only one Gateway resilience registry may be installed.");
        _resilience = values.SingleOrDefault();
    }

    protected override bool CanReuseOldClient(ForwarderHttpClientContext context) =>
        base.CanReuseOldClient(context) &&
        Value(context.OldMetadata, UseProxyMetadata) == Value(context.NewMetadata, UseProxyMetadata) &&
        Value(context.OldMetadata, ConnectTimeoutTicksMetadata) == Value(context.NewMetadata, ConnectTimeoutTicksMetadata) &&
        Value(context.OldMetadata, ResilienceProfileMetadata) == Value(context.NewMetadata, ResilienceProfileMetadata) &&
        Value(context.OldMetadata, ResilienceVersionMetadata) == Value(context.NewMetadata, ResilienceVersionMetadata);

    protected override void ConfigureHandler(ForwarderHttpClientContext context, SocketsHttpHandler handler)
    {
        base.ConfigureHandler(context, handler);
        ApplyReservedSettings(context.NewMetadata, handler);
    }

    protected override HttpMessageHandler WrapHandler(ForwarderHttpClientContext context, HttpMessageHandler handler)
    {
        var name = Value(context.NewMetadata, ResilienceProfileMetadata);
        if (name is null) return base.WrapHandler(context, handler);
        if (_resilience is null ||
            !int.TryParse(Value(context.NewMetadata, ResilienceVersionMetadata), NumberStyles.None, CultureInfo.InvariantCulture, out var version) ||
            !_resilience.IsInstalled(name, version))
            throw new InvalidOperationException("The selected Gateway resilience profile is not installed at its materialized version.");
        return _resilience.Wrap(name, version, base.WrapHandler(context, handler));
    }

    internal static void ApplyReservedSettings(IReadOnlyDictionary<string, string>? metadata, SocketsHttpHandler handler)
    {
        if (bool.TryParse(Value(metadata, UseProxyMetadata), out var useProxy)) handler.UseProxy = useProxy;
        if (long.TryParse(Value(metadata, ConnectTimeoutTicksMetadata), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
            handler.ConnectTimeout = TimeSpan.FromTicks(ticks);
    }

    private static string? Value(IReadOnlyDictionary<string, string>? metadata, string name) =>
        metadata is not null && metadata.TryGetValue(name, out var value) ? value : null;
}
