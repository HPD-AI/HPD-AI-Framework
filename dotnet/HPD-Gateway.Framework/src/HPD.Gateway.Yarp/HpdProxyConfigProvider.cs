using System.Collections.Immutable;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace HPD.Gateway.Yarp;

internal sealed class HpdProxyConfigProvider : IProxyConfigProvider, IDisposable
{
    private OwnedProxyConfig _current = OwnedProxyConfig.Empty();
    private bool _disposed;

    public IProxyConfig GetConfig() => Volatile.Read(ref _current);

    internal OwnedProxyConfig Prepare(NativePublicationBundle bundle) => new(
        bundle.NativeRevisionId,
        bundle.Routes,
        bundle.Clusters);

    internal void Install(OwnedProxyConfig next)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(next);
        var previous = Interlocked.Exchange(ref _current, next);
        previous.SignalChange();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Volatile.Read(ref _current).Dispose();
    }
}

internal sealed class OwnedProxyConfig : IProxyConfig, IDisposable
{
    private readonly CancellationTokenSource _change = new();

    internal OwnedProxyConfig(
        string revisionId,
        ImmutableArray<RouteConfig> routes,
        ImmutableArray<ClusterConfig> clusters)
    {
        RevisionId = revisionId;
        Routes = routes;
        Clusters = clusters;
        ChangeToken = new CancellationChangeToken(_change.Token);
    }

    public string RevisionId { get; }
    public IReadOnlyList<RouteConfig> Routes { get; }
    public IReadOnlyList<ClusterConfig> Clusters { get; }
    public IChangeToken ChangeToken { get; }

    internal static OwnedProxyConfig Empty() => new(
        $"hpd-bootstrap-{Guid.NewGuid():N}",
        [],
        []);

    internal void SignalChange() => _change.Cancel();

    public void Dispose() => _change.Dispose();
}
