using System.Globalization;
using System.Net.Http;
using Yarp.ReverseProxy.Forwarder;

namespace HPD.Gateway.Yarp;

internal sealed class HpdForwarderHttpClientFactory : ForwarderHttpClientFactory
{
    internal const string UseProxyMetadata = "hpd.gateway.transport.use-proxy";
    internal const string ConnectTimeoutTicksMetadata = "hpd.gateway.transport.connect-timeout-ticks";

    protected override bool CanReuseOldClient(ForwarderHttpClientContext context) =>
        base.CanReuseOldClient(context) &&
        Value(context.OldMetadata, UseProxyMetadata) == Value(context.NewMetadata, UseProxyMetadata) &&
        Value(context.OldMetadata, ConnectTimeoutTicksMetadata) == Value(context.NewMetadata, ConnectTimeoutTicksMetadata);

    protected override void ConfigureHandler(ForwarderHttpClientContext context, SocketsHttpHandler handler)
    {
        base.ConfigureHandler(context, handler);
        ApplyReservedSettings(context.NewMetadata, handler);
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
