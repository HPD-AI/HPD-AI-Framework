using System.Collections.Concurrent;
using Yarp.ReverseProxy.Configuration;

namespace HPD.Gateway;

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
    private readonly GatewayRuntimeApplicationObserver? _runtimeObserver;
    private readonly ConcurrentDictionary<OwnedProxyConfig, TaskCompletionSource<NativeAcknowledgement>> _pending =
        new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<OwnedProxyConfig, byte> _requiresAppliedRuntime =
        new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    internal HpdConfigChangeListener(HpdProxyConfigProvider provider, GatewayRuntimeApplicationObserver? runtimeObserver = null)
    {
        _provider = provider;
        _runtimeObserver = runtimeObserver;
    }

    internal Task<NativeAcknowledgement> Register(OwnedProxyConfig snapshot, bool requireAppliedRuntime = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var completion = new TaskCompletionSource<NativeAcknowledgement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(snapshot, completion)) throw new InvalidOperationException("Snapshot acknowledgement is already registered.");
        if (requireAppliedRuntime && !_requiresAppliedRuntime.TryAdd(snapshot, 0))
        {
            _pending.TryRemove(snapshot, out _);
            throw new InvalidOperationException("Snapshot applied-runtime acknowledgement is already registered.");
        }
        return completion.Task;
    }

    internal void Unregister(OwnedProxyConfig snapshot)
    {
        _pending.TryRemove(snapshot, out _);
        _requiresAppliedRuntime.TryRemove(snapshot, out _);
    }

    public void ConfigurationLoadingFailed(IProxyConfigProvider configProvider, Exception exception)
    {
        if (!ReferenceEquals(configProvider, _provider)) return;
        _runtimeObserver?.LoadingFailed();
        CompleteSolePending(new NativeAcknowledgement(NativeAcknowledgementKind.LoadingFailed, "publication.loading-failed"));
    }

    public void ConfigurationLoaded(IReadOnlyList<IProxyConfig> proxyConfigs)
    {
    }

    public void ConfigurationApplyingFailed(IReadOnlyList<IProxyConfig> proxyConfigs, Exception exception)
    {
        _runtimeObserver?.ApplyingFailed(proxyConfigs);
        CompleteExact(proxyConfigs, new NativeAcknowledgement(NativeAcknowledgementKind.ApplyingFailed, "publication.apply-failed"));
    }

    public void ConfigurationApplied(IReadOnlyList<IProxyConfig> proxyConfigs)
    {
        NativeAcknowledgement acknowledgement = new(NativeAcknowledgementKind.Applied, "publication.applied");
        if (_runtimeObserver is null)
        {
            CompleteExact(proxyConfigs, acknowledgement);
            return;
        }
        bool hasMatch = TryFindExact(proxyConfigs, out var match);
        if (!_runtimeObserver.TryStageApplied(proxyConfigs, out GatewayRuntimeApplicationObserver.StagedAppliedRuntime? staged))
        {
            if (hasMatch && !_requiresAppliedRuntime.ContainsKey(match.Key)) Complete(match, acknowledgement);
            return;
        }
        if (hasMatch)
        {
            if (!TryClaim(match, out ClaimedAcknowledgement? claimed)) return;
            bool promoted = _runtimeObserver.TryPromoteStaged(staged!);
            claimed!.Signal(promoted
                ? acknowledgement
                : new NativeAcknowledgement(NativeAcknowledgementKind.ApplyingFailed, "publication.applied-promotion-failed"));
            return;
        }
        if (!staged!.RequiresAcknowledgement)
            _runtimeObserver.TryPromoteStaged(staged);
    }

    private void CompleteExact(IReadOnlyList<IProxyConfig> proxyConfigs, NativeAcknowledgement acknowledgement)
    {
        if (TryFindExact(proxyConfigs, out var match)) Complete(match, acknowledgement);
    }

    private bool TryFindExact(
        IReadOnlyList<IProxyConfig> proxyConfigs,
        out KeyValuePair<OwnedProxyConfig, TaskCompletionSource<NativeAcknowledgement>> match)
    {
        match = default;
        if (proxyConfigs.Count != 1) return false;
        KeyValuePair<OwnedProxyConfig, TaskCompletionSource<NativeAcknowledgement>>[] matches = _pending
            .Where(pair => pair.Key.LogicalGeneration?.Matches(proxyConfigs[0]) == true)
            .Take(2)
            .ToArray();
        if (matches.Length != 1) return false;
        match = matches[0];
        return true;
    }

    private bool Complete(
        KeyValuePair<OwnedProxyConfig, TaskCompletionSource<NativeAcknowledgement>> match,
        NativeAcknowledgement acknowledgement)
    {
        if (!TryClaim(match, out ClaimedAcknowledgement? claimed)) return false;
        return claimed!.Signal(acknowledgement);
    }

    private bool TryClaim(
        KeyValuePair<OwnedProxyConfig, TaskCompletionSource<NativeAcknowledgement>> match,
        out ClaimedAcknowledgement? claimed)
    {
        claimed = null;
        if (!_pending.TryRemove(match.Key, out TaskCompletionSource<NativeAcknowledgement>? completion)) return false;
        _requiresAppliedRuntime.TryRemove(match.Key, out _);
        claimed = new ClaimedAcknowledgement(completion);
        return true;
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
            {
                _requiresAppliedRuntime.TryRemove(pair.Key, out _);
                completion.TrySetResult(new NativeAcknowledgement(NativeAcknowledgementKind.Disposed, "publication.listener-disposed"));
            }
    }

    private sealed class ClaimedAcknowledgement(TaskCompletionSource<NativeAcknowledgement> completion)
    {
        internal bool Signal(NativeAcknowledgement acknowledgement) => completion.TrySetResult(acknowledgement);
    }
}
