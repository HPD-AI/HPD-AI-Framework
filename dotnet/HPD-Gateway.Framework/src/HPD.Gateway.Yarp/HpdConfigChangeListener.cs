using System.Collections.Concurrent;
using Yarp.ReverseProxy.Configuration;

namespace HPD.Gateway.Yarp;

internal enum NativeAcknowledgementKind : byte
{
    Applied,
    ApplyingFailed,
    LoadingFailed,
    Disposed
}

internal readonly record struct NativeAcknowledgement(
    NativeAcknowledgementKind Kind,
    string Code);

internal sealed class HpdConfigChangeListener : IConfigChangeListener, IDisposable
{
    private readonly HpdProxyConfigProvider _provider;
    private readonly ConcurrentDictionary<OwnedProxyConfig, TaskCompletionSource<NativeAcknowledgement>> _pending =
        new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    internal HpdConfigChangeListener(HpdProxyConfigProvider provider) => _provider = provider;

    internal Task<NativeAcknowledgement> Register(OwnedProxyConfig snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var completion = new TaskCompletionSource<NativeAcknowledgement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(snapshot, completion)) throw new InvalidOperationException("Snapshot acknowledgement is already registered.");
        return completion.Task;
    }

    internal void Unregister(OwnedProxyConfig snapshot) => _pending.TryRemove(snapshot, out _);

    public void ConfigurationLoadingFailed(IProxyConfigProvider configProvider, Exception exception)
    {
        if (!ReferenceEquals(configProvider, _provider)) return;
        CompleteSolePending(new NativeAcknowledgement(NativeAcknowledgementKind.LoadingFailed, "publication.loading-failed"));
    }

    public void ConfigurationLoaded(IReadOnlyList<IProxyConfig> proxyConfigs)
    {
    }

    public void ConfigurationApplyingFailed(IReadOnlyList<IProxyConfig> proxyConfigs, Exception exception) =>
        CompleteExact(proxyConfigs, new NativeAcknowledgement(NativeAcknowledgementKind.ApplyingFailed, "publication.apply-failed"));

    public void ConfigurationApplied(IReadOnlyList<IProxyConfig> proxyConfigs) =>
        CompleteExact(proxyConfigs, new NativeAcknowledgement(NativeAcknowledgementKind.Applied, "publication.applied"));

    private void CompleteExact(IReadOnlyList<IProxyConfig> proxyConfigs, NativeAcknowledgement acknowledgement)
    {
        foreach (var config in proxyConfigs)
        {
            if (config is OwnedProxyConfig owned && _pending.TryRemove(owned, out var completion))
                completion.TrySetResult(acknowledgement);
        }
    }

    private void CompleteSolePending(NativeAcknowledgement acknowledgement)
    {
        if (_pending.Count != 1) return;
        var pair = _pending.First();
        if (_pending.TryRemove(pair.Key, out var completion)) completion.TrySetResult(acknowledgement);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var pair in _pending)
            if (_pending.TryRemove(pair.Key, out var completion))
                completion.TrySetResult(new NativeAcknowledgement(NativeAcknowledgementKind.Disposed, "publication.listener-disposed"));
    }
}
