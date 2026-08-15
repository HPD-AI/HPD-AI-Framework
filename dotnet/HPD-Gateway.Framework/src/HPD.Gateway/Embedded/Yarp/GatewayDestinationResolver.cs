using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.ServiceDiscovery;

namespace HPD.Gateway;

internal sealed class GatewayDestinationResolver : IDestinationResolver, IDisposable
{
    private const int MaximumApplications = 4_096;
    private const int MaximumFanOut = 32;
    private static readonly TimeSpan ResolutionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetryPeriod = TimeSpan.FromSeconds(5);
    private readonly GatewayDiscoveryProfileRegistry _profiles;
    private readonly GatewayRuntimeApplicationPreparer _preparer;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _fanOut = new(MaximumFanOut, MaximumFanOut);
    private readonly SemaphoreSlim _applicationCapacity = new(MaximumApplications, MaximumApplications);
    private readonly ConcurrentDictionary<string, ApplicationState> _applications = new(StringComparer.Ordinal);
    private long _membershipGeneration;
    private volatile bool _disposed;

    internal GatewayDestinationResolver(
        GatewayDiscoveryProfileRegistry profiles,
        IConfigValidator nativeValidator,
        TimeProvider timeProvider)
    {
        _profiles = profiles;
        _preparer = new GatewayRuntimeApplicationPreparer(nativeValidator);
        _timeProvider = timeProvider;
    }

    internal async ValueTask<(GatewayPreparedApplication? Application, ImmutableArray<GatewayRuntimePlanningDiagnostic> Diagnostics)> PrepareAsync(
        GatewayRuntimePlan plan,
        string nativeRevisionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (plan.Dependencies.IsEmpty)
            return await _preparer.PrepareAsync(plan, nativeRevisionId, cancellationToken).ConfigureAwait(false);
        if (!_applicationCapacity.Wait(0))
            return Reject("discovery.application-capacity-exceeded", "The bounded discovery application registry is full.");

        var capacityOwned = true;
        Task<PreparedSlot>[] tasks = [];
        try
        {
            tasks = plan.Dependencies.Select(dependency =>
                PrepareSlotAsync(dependency, cancellationToken).AsTask()).ToArray();
            PreparedSlot[] slots = await Task.WhenAll(tasks).ConfigureAwait(false);
            if (slots.Sum(static slot => slot.Destinations.Count) > GatewayRuntimePlan.MaximumResolvedEndpoints)
            {
                DisposeSlots(slots);
                return Reject("discovery.application-endpoint-bound-exceeded", "The complete discovery application exceeds its endpoint bound.");
            }

            ImmutableDictionary<string, PreparedSlot> slotMap = slots.ToImmutableDictionary(static slot => slot.Dependency.UpstreamId, StringComparer.Ordinal);
            ImmutableArray<ClusterConfig> clusters = plan.Clusters.Select(cluster =>
                slotMap.TryGetValue(cluster.ClusterId, out PreparedSlot? slot)
                    ? cluster with { Destinations = slot.Destinations }
                    : cluster).ToImmutableArray();
            ImmutableArray<GatewayPreparedDependencyResolution> resolutions = plan.Dependencies.Select(dependency =>
            {
                PreparedSlot slot = slotMap[dependency.UpstreamId];
                return GatewayPreparedApplication.DescribeResolution(
                    dependency, slot.Destinations, slot.Generation, slot.Disposition);
            }).ToImmutableArray();
            var prepared = await _preparer.PrepareAsync(plan, clusters, resolutions, nativeRevisionId, cancellationToken).ConfigureAwait(false);
            if (prepared.Application is null)
            {
                DisposeSlots(slots);
                return prepared;
            }
            var state = new ApplicationState(prepared.Application, slotMap, _applicationCapacity);
            capacityOwned = false;
            if (!_applications.TryAdd(plan.ApplicationId, state))
            {
                state.Dispose();
                return Reject("discovery.application-identity-conflict", "The discovery application identity is already registered.");
            }
            return prepared;
        }
        catch (OperationCanceledException)
        {
            return Reject("discovery.preparation-canceled", "Discovery preparation was canceled.");
        }
        catch (Exception)
        {
            return Reject("discovery.preparation-failed", "Discovery preparation failed before native exchange.");
        }
        finally
        {
            if (capacityOwned) _applicationCapacity.Release();
            if (capacityOwned)
                foreach (Task<PreparedSlot> task in tasks)
                    if (task.Status == TaskStatus.RanToCompletion)
                        task.Result.Dispose();
                    else if (!task.IsCompleted)
                        _ = task.ContinueWith(static completed =>
                        {
                            if (completed.Status == TaskStatus.RanToCompletion) completed.Result.Dispose();
                            _ = completed.Exception;
                        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    internal bool RegisterForExchange(GatewayPreparedApplication application)
    {
        if (application.Plan.Dependencies.IsEmpty) return true;
        if (!_applications.TryGetValue(application.ApplicationId, out ApplicationState? state) ||
            !ReferenceEquals(state.Application, application)) return false;
        return state.Register();
    }

    internal bool SourcesUnchanged(GatewayPreparedApplication application)
    {
        if (application.Plan.Dependencies.IsEmpty) return true;
        return _applications.TryGetValue(application.ApplicationId, out ApplicationState? state) && state.SourcesUnchanged;
    }

    internal void CompleteApplication(GatewayPreparedApplication application, bool applied)
    {
        if (application.Plan.Dependencies.IsEmpty) return;
        if (!_applications.TryGetValue(application.ApplicationId, out ApplicationState? state)) return;
        if (applied)
        {
            state.MarkApplied();
            foreach (KeyValuePair<string, ApplicationState> pair in _applications)
                if (!StringComparer.Ordinal.Equals(pair.Key, application.ApplicationId) && pair.Value.IsApplied &&
                    _applications.TryRemove(pair.Key, out ApplicationState? retired))
                    retired.Dispose();
        }
        else if (_applications.TryRemove(application.ApplicationId, out state)) state.Dispose();
    }

    internal ImmutableArray<GatewayPreparedDependencyResolution> GetPendingResolutions(string applicationId) =>
        _applications.TryGetValue(applicationId, out ApplicationState? state)
            ? state.Pending
            : [];

    internal bool TryStagePromotion(
        GatewayPreparedApplication application,
        IProxyConfig config,
        out GatewayResolutionPromotion? promotion)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(config);
        if (application.Plan.Dependencies.IsEmpty)
        {
            promotion = null;
            try
            {
                if (GatewayRuntimeGraphIdentity.ComputeNativeGeneration(config.Routes, config.Clusters) !=
                    application.NativeGraphIdentity) return false;
                promotion = new GatewayResolutionPromotion([], static () => { });
                return true;
            }
            catch { return false; }
        }
        if (!_applications.TryGetValue(application.ApplicationId, out ApplicationState? state) ||
            !ReferenceEquals(state.Application, application))
        {
            promotion = null;
            return false;
        }
        return state.TryStagePromotion(config, out promotion);
    }

    internal void CommitPromotion(GatewayResolutionPromotion promotion) => promotion.Commit();

    public async ValueTask<ResolvedDestinationCollection> ResolveDestinationsAsync(
        IReadOnlyDictionary<string, DestinationConfig> destinations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        ObjectDisposedException.ThrowIf(_disposed, this);
        KeyValuePair<string, DestinationConfig>[] symbolic = destinations
            .Where(static pair => pair.Value.Metadata?.ContainsKey(GatewayRuntimePlanner.SymbolicDestinationMetadata) == true)
            .Take(2)
            .ToArray();
        if (symbolic.Length == 0)
        {
            if (destinations.Values.Any(static value => value.Metadata?.Keys.Any(static key => key.StartsWith("hpd.gateway.", StringComparison.Ordinal)) == true))
                throw new InvalidOperationException("Malformed discovery markers cannot pass through.");
            return new ResolvedDestinationCollection(destinations, NeverChangeToken.Instance);
        }
        if (symbolic.Length != 1 || destinations.Count != 1)
            throw new InvalidOperationException("A discovery Cluster must contain exactly one symbolic destination.");
        IReadOnlyDictionary<string, string> metadata = symbolic[0].Value.Metadata!;
        if (!metadata.TryGetValue(GatewayRuntimePlanner.ApplicationIdMetadata, out string? applicationId) ||
            !metadata.TryGetValue("hpd.gateway.upstream-id", out string? upstreamId) ||
            !_applications.TryGetValue(applicationId, out ApplicationState? state) ||
            !state.TryGet(upstreamId, out PreparedSlot? slot))
            throw new InvalidOperationException("The symbolic discovery application is missing or no longer admissible.");

        PreparedSlot selected = slot!;
        if (!state.IsRegistered && !state.IsApplied)
            throw new InvalidOperationException("The prepared discovery application has not entered native exchange.");
        if (selected.TryConsumeInitial(state))
            return new ResolvedDestinationCollection(selected.Destinations, selected.ChangeToken ?? NeverChangeToken.Instance);
        return await RefreshAsync(state, selected, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ResolvedDestinationCollection> RefreshAsync(
        ApplicationState state,
        PreparedSlot slot,
        CancellationToken cancellationToken)
    {
        await slot.RefreshLease.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            slot.ConsumeFiredRetry();
            try
            {
                PreparedSlot refreshed = await ResolveFreshAsync(slot.Dependency, cancellationToken).ConfigureAwait(false);
                slot.Replace(refreshed);
                refreshed.Dispose();
                state.RecordPending(slot);
                return new ResolvedDestinationCollection(slot.Destinations, slot.ChangeToken ?? NeverChangeToken.Instance);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                if (!state.IsApplied && slot.Dependency.StaleBehavior != DiscoveryStaleBehavior.ServeUnavailableWhenStale)
                    throw;
                slot.ScheduleRetry(_timeProvider, RetryPeriod);
                switch (slot.Dependency.StaleBehavior)
                {
                    case DiscoveryStaleBehavior.RejectActivationUntilFresh:
                        slot.ForceRefreshFailed(NextGeneration());
                        break;
                    case DiscoveryStaleBehavior.PermitLastKnownMembership:
                        slot.ForceLastKnown(NextGeneration());
                        break;
                    case DiscoveryStaleBehavior.ServeUnavailableWhenStale:
                        slot.ForceUnavailable(NextGeneration());
                        break;
                }
                state.RecordPending(slot);
                return new ResolvedDestinationCollection(slot.Destinations, slot.RetryToken);
            }
        }
        finally
        {
            slot.RefreshLease.Release();
        }
    }

    private async ValueTask<PreparedSlot> PrepareSlotAsync(
        GatewayRuntimeDependencyBinding dependency,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ResolveFreshAsync(dependency, cancellationToken).ConfigureAwait(false);
        }
        catch when (dependency.StaleBehavior == DiscoveryStaleBehavior.ServeUnavailableWhenStale && !cancellationToken.IsCancellationRequested)
        {
            var slot = new PreparedSlot(dependency, ImmutableDictionary<string, DestinationConfig>.Empty,
                NextGeneration(), GatewayPreparedMembershipDisposition.UnavailableWhenStale, null);
            slot.ScheduleRetry(_timeProvider, RetryPeriod);
            return slot;
        }
    }

    private async ValueTask<PreparedSlot> ResolveFreshAsync(
        GatewayRuntimeDependencyBinding dependency,
        CancellationToken cancellationToken)
    {
        if (!_profiles.TryGet(dependency, out IGatewayDiscoveryRuntimeProfile? profile) || profile is null)
            throw new InvalidOperationException("The accepted discovery profile is not installed or does not match its capability.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using ITimer deadlineTimer = _timeProvider.CreateTimer(
            static state => ((CancellationTokenSource)state!).Cancel(), deadline,
            ResolutionTimeout, Timeout.InfiniteTimeSpan);
        await _fanOut.WaitAsync(deadline.Token).ConfigureAwait(false);
        Task<GatewayDiscoveryResult>? providerTask = null;
        var leaseTransferred = false;
        try
        {
            providerTask = profile.ResolveAsync(new GatewayDiscoveryRequest(
                    dependency.Profile, dependency.Service, dependency.Endpoint, dependency.Schemes, dependency.TlsServerName), deadline.Token)
                .AsTask();
            GatewayDiscoveryResult result;
            try
            {
                result = await providerTask.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch when (!providerTask.IsCompleted)
            {
                leaseTransferred = true;
                _ = providerTask.ContinueWith(static (completed, owner) =>
                {
                    _ = completed.Exception;
                    ((GatewayDestinationResolver)owner!).ReleaseFanOut();
                }, this, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                throw;
            }
            ArgumentNullException.ThrowIfNull(result);
            ImmutableDictionary<string, DestinationConfig> projected = GatewayDiscoveryEndpointProjector.Project(
                dependency, profile.Capability, result.Endpoints);
            return new PreparedSlot(dependency, projected, NextGeneration(), GatewayPreparedMembershipDisposition.Fresh, result.ChangeToken);
        }
        finally
        {
            if (!leaseTransferred) ReleaseFanOut();
        }
    }

    private void ReleaseFanOut()
    {
        if (_disposed) return;
        try { _fanOut.Release(); }
        catch (ObjectDisposedException) { }
    }

    private long NextGeneration()
    {
        long value = Interlocked.Increment(ref _membershipGeneration);
        if (value <= 0) throw new InvalidOperationException("Discovery membership generation space is exhausted.");
        return value;
    }

    private static (GatewayPreparedApplication?, ImmutableArray<GatewayRuntimePlanningDiagnostic>) Reject(string code, string message) =>
        (null, [new(code, "$", message)]);

    private static void DisposeSlots(IEnumerable<PreparedSlot> slots)
    {
        foreach (PreparedSlot slot in slots) slot.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (KeyValuePair<string, ApplicationState> pair in _applications)
            if (_applications.TryRemove(pair.Key, out ApplicationState? state)) state.Dispose();
        _fanOut.Dispose();
        _applicationCapacity.Dispose();
    }

    private sealed class ApplicationState : IDisposable
    {
        private readonly object _observationGate = new();
        private readonly ImmutableDictionary<string, PreparedSlot> _slots;
        private readonly SemaphoreSlim _capacity;
        private ImmutableDictionary<string, GatewayPreparedDependencyResolution> _pending =
            ImmutableDictionary<string, GatewayPreparedDependencyResolution>.Empty.WithComparers(StringComparer.Ordinal);
        private ImmutableDictionary<string, GatewayPreparedDependencyResolution> _applied;
        private int _lifecycle;
        private int _disposed;

        internal ApplicationState(
            GatewayPreparedApplication application,
            ImmutableDictionary<string, PreparedSlot> slots,
            SemaphoreSlim capacity)
        {
            Application = application;
            _slots = slots;
            _capacity = capacity;
            _applied = application.Resolutions.ToImmutableDictionary(static value => value.UpstreamId, StringComparer.Ordinal);
        }

        internal GatewayPreparedApplication Application { get; }
        internal bool IsApplied => Volatile.Read(ref _lifecycle) == 2;
        internal bool IsRegistered => Volatile.Read(ref _lifecycle) == 1;
        internal bool SourcesUnchanged => _slots.Values.All(static slot => slot.ChangeToken?.HasChanged != true);
        internal bool Register() => Interlocked.CompareExchange(ref _lifecycle, 1, 0) == 0 && SourcesUnchanged;
        internal void MarkApplied() => Interlocked.CompareExchange(ref _lifecycle, 2, 1);
        internal bool TryGet(string upstreamId, out PreparedSlot? slot) => _slots.TryGetValue(upstreamId, out slot);
        internal void RecordPending(PreparedSlot slot)
        {
            GatewayPreparedDependencyResolution next = GatewayPreparedApplication.DescribeResolution(
                slot.Dependency, slot.Destinations, slot.Generation, slot.Disposition);
            ImmutableInterlocked.AddOrUpdate(ref _pending, slot.Dependency.UpstreamId, next, static (_, replacement) => replacement);
        }
        internal ImmutableArray<GatewayPreparedDependencyResolution> Pending => _pending.Values
            .OrderBy(static value => value.UpstreamId, StringComparer.Ordinal).ToImmutableArray();
        internal bool TryStagePromotion(IProxyConfig config, out GatewayResolutionPromotion? promotion)
        {
            lock (_observationGate)
            {
                ImmutableDictionary<string, GatewayPreparedDependencyResolution> candidate = _applied;
                foreach (KeyValuePair<string, GatewayPreparedDependencyResolution> pair in _pending)
                {
                    if (_applied.TryGetValue(pair.Key, out GatewayPreparedDependencyResolution? current) &&
                        current.MembershipIdentity == pair.Value.MembershipIdentity &&
                        current.Disposition == pair.Value.Disposition &&
                        current.DestinationCount == pair.Value.DestinationCount)
                        continue;
                    candidate = candidate.SetItem(pair.Key, pair.Value);
                }
                ImmutableArray<GatewayPreparedDependencyResolution> resolutions = Application.Plan.Dependencies
                    .Select(dependency => candidate[dependency.UpstreamId])
                    .ToImmutableArray();
                try
                {
                    ImmutableArray<ClusterConfig> clusters = config.Clusters.ToImmutableArray();
                    GatewayPreparedApplication.ValidateResolvedGraph(Application.Plan, clusters, resolutions);
                }
                catch
                {
                    promotion = null;
                    return false;
                }
                promotion = new GatewayResolutionPromotion(resolutions, () => CommitPromotion(resolutions));
                return true;
            }
        }
        internal void CommitPromotion(ImmutableArray<GatewayPreparedDependencyResolution> resolutions)
        {
            lock (_observationGate)
            {
                foreach (GatewayPreparedDependencyResolution resolution in resolutions)
                {
                    _applied = _applied.SetItem(resolution.UpstreamId, resolution);
                    if (_pending.TryGetValue(resolution.UpstreamId, out GatewayPreparedDependencyResolution? pending) &&
                        pending == resolution)
                        ImmutableInterlocked.TryRemove(ref _pending, resolution.UpstreamId, out _);
                }
            }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Interlocked.Exchange(ref _lifecycle, 3);
            DisposeSlots(_slots.Values);
            _capacity.Release();
        }
    }

    internal sealed class GatewayResolutionPromotion(
        ImmutableArray<GatewayPreparedDependencyResolution> resolutions,
        Action commit)
    {
        internal ImmutableArray<GatewayPreparedDependencyResolution> Resolutions { get; } = resolutions;
        internal void Commit() => commit();
    }

    private sealed class PreparedSlot : IDisposable
    {
        private readonly object _retryGate = new();
        private IDisposable? _retry;
        private int _initialConsumed;

        internal PreparedSlot(
            GatewayRuntimeDependencyBinding dependency,
            ImmutableDictionary<string, DestinationConfig> destinations,
            long generation,
            GatewayPreparedMembershipDisposition disposition,
            IChangeToken? changeToken)
        {
            Dependency = dependency;
            Destinations = destinations;
            Generation = generation;
            Disposition = disposition;
            ChangeToken = changeToken;
        }

        internal GatewayRuntimeDependencyBinding Dependency { get; }
        internal ImmutableDictionary<string, DestinationConfig> Destinations { get; private set; }
        internal long Generation { get; private set; }
        internal GatewayPreparedMembershipDisposition Disposition { get; private set; }
        internal IChangeToken? ChangeToken { get; private set; }
        internal IChangeToken RetryToken { get; private set; } = NeverChangeToken.Instance;
        internal SemaphoreSlim RefreshLease { get; } = new(1, 1);

        internal bool TryConsumeInitial(ApplicationState state) =>
            state.IsRegistered && Interlocked.CompareExchange(ref _initialConsumed, 1, 0) == 0;

        internal void Replace(PreparedSlot source)
        {
            lock (_retryGate)
            {
                _retry?.Dispose();
                _retry = null;
                RetryToken = NeverChangeToken.Instance;
            }
            Destinations = source.Destinations; Generation = source.Generation;
            Disposition = source.Disposition; ChangeToken = source.ChangeToken;
        }

        internal void ForceUnavailable(long generation)
        {
            Destinations = ImmutableDictionary<string, DestinationConfig>.Empty;
            Generation = generation;
            Disposition = GatewayPreparedMembershipDisposition.UnavailableWhenStale;
            ChangeToken = null;
        }

        internal void ForceLastKnown(long generation)
        {
            Generation = generation;
            Disposition = GatewayPreparedMembershipDisposition.LastKnownMembership;
            ChangeToken = null;
        }

        internal void ForceRefreshFailed(long generation)
        {
            Generation = generation;
            Disposition = GatewayPreparedMembershipDisposition.RefreshFailed;
            ChangeToken = null;
        }

        internal void ScheduleRetry(TimeProvider timeProvider, TimeSpan period)
        {
            lock (_retryGate)
            {
                if (_retry is not null) return;
                var source = new CancellationTokenSource();
                ITimer timer = timeProvider.CreateTimer(static state => ((CancellationTokenSource)state!).Cancel(), source, period, Timeout.InfiniteTimeSpan);
                _retry = new CompositeDisposable(timer, source);
                RetryToken = new CancellationChangeToken(source.Token);
            }
        }

        internal void ConsumeFiredRetry()
        {
            lock (_retryGate)
            {
                if (_retry is null || !RetryToken.HasChanged) return;
                _retry.Dispose();
                _retry = null;
                RetryToken = NeverChangeToken.Instance;
            }
        }

        public void Dispose()
        {
            lock (_retryGate)
            {
                _retry?.Dispose();
                _retry = null;
                RetryToken = NeverChangeToken.Instance;
            }
            RefreshLease.Dispose();
        }
    }

    private sealed class CompositeDisposable(IDisposable first, IDisposable second) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            first.Dispose(); second.Dispose();
        }
    }

    private sealed class NeverChangeToken : IChangeToken
    {
        internal static NeverChangeToken Instance { get; } = new();
        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;
        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => EmptyDisposable.Instance;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        internal static EmptyDisposable Instance { get; } = new();
        public void Dispose() { }
    }
}
