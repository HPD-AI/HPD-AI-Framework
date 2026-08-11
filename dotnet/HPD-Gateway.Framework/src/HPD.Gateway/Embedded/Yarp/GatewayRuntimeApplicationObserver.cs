using System.Collections.Immutable;
using Yarp.ReverseProxy.Configuration;

namespace HPD.Gateway;

internal sealed class GatewayRuntimeApplicationObserver : IGatewayNodeAppliedRuntimeReader, IDisposable
{
    private const int MaximumApplications = 4_096;
    private const int MaximumFailures = 256;
    private readonly GatewayDestinationResolver _resolver;
    private readonly TimeProvider _timeProvider;
    private readonly ImmutableDictionary<string, TrafficAdmissionCapability> _admissionCapabilities;
    private readonly object _gate = new();
    private readonly Dictionary<string, Envelope> _applications = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private readonly Queue<string> _failures = new();
    private GatewayAppliedRuntimeObservation? _current;
    private CancellationTokenSource _changed = new();
    private bool _disposed;

    internal GatewayRuntimeApplicationObserver(
        GatewayDestinationResolver resolver,
        TimeProvider timeProvider,
        GatewayTrafficAdmissionRegistry? admission = null)
    {
        _resolver = resolver;
        _timeProvider = timeProvider;
        _admissionCapabilities = admission?.Capabilities.ToImmutableDictionary(static value => value.Name, StringComparer.Ordinal) ??
            ImmutableDictionary<string, TrafficAdmissionCapability>.Empty.WithComparers(StringComparer.Ordinal);
    }

    public GatewayAppliedRuntimeObservation? GetCurrent()
    {
        lock (_gate) return _current;
    }

    public CancellationToken GetChangeToken()
    {
        lock (_gate) return _changed.Token;
    }

    internal bool Register(GatewayPreparedApplication application, string namespaceId, string targetNodeId)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNodeId);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_applications.TryGetValue(application.ApplicationId, out Envelope? existing))
                return ReferenceEquals(existing.Application, application) &&
                    StringComparer.Ordinal.Equals(existing.NamespaceId, namespaceId) &&
                    StringComparer.Ordinal.Equals(existing.TargetNodeId, targetNodeId);
            Prune();
            if (_applications.Count >= MaximumApplications) return false;
            _applications.Add(application.ApplicationId, new(application, namespaceId, targetNodeId));
            _order.Enqueue(application.ApplicationId);
            return true;
        }
    }

    internal void LoadingFailed()
    {
        lock (_gate)
        {
            Envelope[] pending = _applications.Values.Where(static value => !value.Applied && !value.Poisoned).Take(2).ToArray();
            if (pending.Length == 1) Poison(pending[0], "runtime.loading-failed");
            else RecordFailure("runtime.loading-failed");
        }
    }

    internal void ApplyingFailed(IReadOnlyList<IProxyConfig> configs)
    {
        lock (_gate)
        {
            Envelope[] matches = Matching(configs).Take(2).ToArray();
            foreach (Envelope envelope in matches)
                if (!envelope.Applied) Poison(envelope, "runtime.applying-failed");
                else RecordFailure($"{envelope.Application.ApplicationId}:runtime.applying-failed");
        }
    }

    internal bool TryStageApplied(IReadOnlyList<IProxyConfig> configs, out StagedAppliedRuntime? staged)
    {
        staged = null;
        if (configs.Count != 1) return false;
        lock (_gate)
        {
            if (_disposed) return false;
            Envelope[] matches = Matching(configs).Where(static value => !value.Poisoned).Take(2).ToArray();
            if (matches.Length != 1) return false;
            Envelope envelope = matches[0];
            try
            {
                if (!_resolver.TryStagePromotion(envelope.Application, configs[0], out GatewayDestinationResolver.GatewayResolutionPromotion? promotion) ||
                    promotion is null)
                    return false;
                GatewayAppliedRuntimeSnapshot snapshot = BuildSnapshot(envelope.Application, configs[0], promotion.Resolutions);
                staged = new StagedAppliedRuntime(envelope, promotion,
                    new(envelope.NamespaceId, envelope.TargetNodeId, snapshot), !envelope.Applied);
                return true;
            }
            catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                RecordFailure($"{envelope.Application.ApplicationId}:runtime.applied-staging-failed");
                return false;
            }
        }
    }

    internal bool TryPromoteStaged(StagedAppliedRuntime staged)
    {
        CancellationTokenSource? previous = null;
        CancellationTokenSource? replacement = null;
        lock (_gate)
        {
            if (_disposed || staged.Envelope.Poisoned ||
                !_applications.TryGetValue(staged.Envelope.Application.ApplicationId, out Envelope? current) ||
                !ReferenceEquals(current, staged.Envelope)) return false;
            try
            {
                replacement = new CancellationTokenSource();
                _resolver.CommitPromotion(staged.Promotion);
                staged.Envelope.Applied = true;
                _current = staged.Observation;
                previous = _changed;
                _changed = replacement;
                replacement = null;
            }
            catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                RecordFailure($"{staged.Envelope.Application.ApplicationId}:runtime.applied-promotion-failed");
                replacement?.Dispose();
                return false;
            }
        }
        if (previous is not null) CancelAndDispose(previous);
        return true;
    }

    private IEnumerable<Envelope> Matching(IReadOnlyList<IProxyConfig> configs)
    {
        if (configs.Count != 1) return [];
        IProxyConfig config = configs[0];
        return _applications.Values.Where(value => value.Applied
            ? MatchesEnvelope(value.Application, config)
            : GatewayLogicalGeneration.Create(value.Application).Matches(config));
    }

    private static bool MatchesEnvelope(GatewayPreparedApplication application, IProxyConfig config)
    {
        try
        {
            if (config.Routes.Count != application.Routes.Length || config.Clusters.Count != application.Clusters.Length)
                return false;
            string[] routeIds = config.Routes.Select(static route => route.RouteId).Order(StringComparer.Ordinal).ToArray();
            string[] expectedRoutes = application.Routes.Select(static route => route.RouteId).Order(StringComparer.Ordinal).ToArray();
            string[] clusterIds = config.Clusters.Select(static cluster => cluster.ClusterId).Order(StringComparer.Ordinal).ToArray();
            string[] expectedClusters = application.Clusters.Select(static cluster => cluster.ClusterId).Order(StringComparer.Ordinal).ToArray();
            if (!routeIds.SequenceEqual(expectedRoutes, StringComparer.Ordinal) ||
                !clusterIds.SequenceEqual(expectedClusters, StringComparer.Ordinal)) return false;
            return config.Routes.All(route => HasIdentity(route.Metadata, application)) &&
                config.Clusters.All(cluster => HasIdentity(cluster.Metadata, application));
        }
        catch { return false; }
    }

    private static bool HasIdentity(IReadOnlyDictionary<string, string>? metadata, GatewayPreparedApplication application) =>
        metadata is not null &&
        metadata.TryGetValue(GatewayRuntimePlanner.ApplicationIdMetadata, out string? id) && id == application.ApplicationId &&
        metadata.TryGetValue(GatewayRuntimePlanner.SymbolicPlanIdentityMetadata, out string? plan) && plan == application.SymbolicPlanIdentity.Value;

    private GatewayAppliedRuntimeSnapshot BuildSnapshot(
        GatewayPreparedApplication application,
        IProxyConfig config,
        ImmutableArray<GatewayPreparedDependencyResolution> resolutions)
    {
        ILookup<string, GatewayEffectiveRecord> records = application.PreparedProjectionSnapshot.Records
            .ToLookup(static record => record.TargetId, StringComparer.Ordinal);
        ImmutableArray<GatewayAppliedRoute> routes = config.Routes
            .OrderBy(static route => route.RouteId, StringComparer.Ordinal)
            .Select(route => new GatewayAppliedRoute(route.RouteId,
                records[route.RouteId].OrderBy(static record => record.Family, StringComparer.Ordinal).ToImmutableArray(),
                BuildAdmission(route)))
            .ToImmutableArray();
        ImmutableDictionary<string, GatewayRuntimeDependencyBinding> dependencies = application.Plan.Dependencies
            .ToImmutableDictionary(static value => value.UpstreamId, StringComparer.Ordinal);
        ImmutableDictionary<string, GatewayPreparedDependencyResolution> resolved = resolutions
            .ToImmutableDictionary(static value => value.UpstreamId, StringComparer.Ordinal);
        ImmutableArray<GatewayAppliedUpstream> upstreams = config.Clusters
            .OrderBy(static cluster => cluster.ClusterId, StringComparer.Ordinal)
            .Select(cluster => BuildUpstream(cluster, dependencies, resolved))
            .ToImmutableArray();
        return new(1, application.Identity.CandidateId, application.Identity.ContentHash,
            application.ApplicationId, application.SymbolicPlanIdentity, _timeProvider.GetUtcNow(), routes, upstreams, true, false);
    }

    private GatewayAppliedTrafficAdmissionPlan? BuildAdmission(RouteConfig route)
    {
        if (route.Metadata is null ||
            !route.Metadata.TryGetValue(GatewayTrafficAdmissionMetadataCodec.Plan, out string? encoded) ||
            !route.Metadata.TryGetValue(GatewayTrafficAdmissionMetadataCodec.PlanIdentity, out string? identity))
            return null;
        TrafficAdmissionPlan plan = GatewayTrafficAdmissionMetadataCodec.Decode(encoded);
        ImmutableArray<GatewayAppliedTrafficAdmissionEntry> entries = plan.Entries.Select((entry, order) =>
        {
            if (!_admissionCapabilities.TryGetValue(entry.ProfileName, out TrafficAdmissionCapability? capability))
                throw new InvalidOperationException("Applied traffic-admission profile is not installed.");
            return CreateAppliedAdmissionEntry(order, entry, capability);
        }).ToImmutableArray();
        return new(new ContentHash("sha-256", identity), entries);
    }

    internal static GatewayAppliedTrafficAdmissionEntry CreateAppliedAdmissionEntry(
        int order,
        TrafficAdmissionEntry entry,
        TrafficAdmissionCapability capability)
    {
        var fixedWindow = entry as FixedWindowAdmissionEntry;
        var slidingWindow = entry as SlidingWindowAdmissionEntry;
        var tokenBucket = entry as TokenBucketAdmissionEntry;
        var concurrency = entry as ConcurrencyAdmissionEntry;
        return new GatewayAppliedTrafficAdmissionEntry(order, capability.Name, capability.Scope, capability.Kind,
            capability.RateAlgorithm, capability.Partition, capability.FailureDisposition,
            capability.AuthorityId, capability.BehaviorIdentity, capability.AcquisitionOrdinal,
            fixedWindow?.PermitLimit ?? slidingWindow?.PermitLimit,
            fixedWindow?.Window.TotalMilliseconds is { } fixedMilliseconds ? checked((long)fixedMilliseconds) :
                slidingWindow?.Window.TotalMilliseconds is { } slidingMilliseconds ? checked((long)slidingMilliseconds) : null,
            slidingWindow?.SegmentsPerWindow,
            tokenBucket?.TokenLimit, tokenBucket?.TokensPerPeriod,
            tokenBucket?.ReplenishmentPeriod.TotalMilliseconds is { } replenishmentMilliseconds
                ? checked((long)replenishmentMilliseconds) : null,
            concurrency?.PermitLimit, concurrency?.QueueLimit,
            capability.PartitionProjectorId, capability.PartitionProjectorIdentity,
            capability.ProviderId, capability.ProviderBehaviorIdentity,
            capability.OperationTimeout?.TotalMilliseconds is { } timeoutMilliseconds ? checked((long)timeoutMilliseconds) : null,
            capability.MaximumConcurrentInvocations, capability.LocalFallbackProfile, capability.LocalFallbackIdentity);
    }

    private static GatewayAppliedUpstream BuildUpstream(
        ClusterConfig cluster,
        ImmutableDictionary<string, GatewayRuntimeDependencyBinding> dependencies,
        ImmutableDictionary<string, GatewayPreparedDependencyResolution> resolutions)
    {
        if (!dependencies.TryGetValue(cluster.ClusterId, out GatewayRuntimeDependencyBinding? dependency))
        {
            IReadOnlyDictionary<string, DestinationConfig> destinations = cluster.Destinations ?? ImmutableDictionary<string, DestinationConfig>.Empty;
            return new(cluster.ClusterId, GatewayAppliedUpstreamKind.Static, null, null, null, null,
                GatewayRuntimeGraphIdentity.ComputeMembership(destinations, GatewayPreparedMembershipDisposition.Fresh),
                destinations.Count, GatewayAppliedMembershipDisposition.Static, "Static destination membership is applied.");
        }
        GatewayPreparedDependencyResolution resolution = resolutions[cluster.ClusterId];
        return new(cluster.ClusterId, GatewayAppliedUpstreamKind.ServiceDiscovery, dependency.Profile.Value,
            dependency.Service.Value, dependency.Endpoint?.Value, resolution.MembershipGeneration,
            resolution.MembershipIdentity, resolution.DestinationCount, resolution.Disposition switch
            {
                GatewayPreparedMembershipDisposition.Fresh => GatewayAppliedMembershipDisposition.Fresh,
                GatewayPreparedMembershipDisposition.LastKnownMembership => GatewayAppliedMembershipDisposition.LastKnownMembership,
                GatewayPreparedMembershipDisposition.UnavailableWhenStale => GatewayAppliedMembershipDisposition.UnavailableWhenStale,
                _ => GatewayAppliedMembershipDisposition.RefreshFailed,
            }, resolution.Disposition switch
            {
                GatewayPreparedMembershipDisposition.Fresh => "Fresh discovered membership is applied.",
                GatewayPreparedMembershipDisposition.LastKnownMembership => "Last-known discovered membership remains applied.",
                GatewayPreparedMembershipDisposition.UnavailableWhenStale => "An empty unavailable membership generation is applied.",
                _ => "Discovery refresh failed; the previously applied membership remains in service but readiness is closed.",
            });
    }

    private void Poison(Envelope envelope, string code)
    {
        envelope.Poisoned = true;
        RecordFailure($"{envelope.Application.ApplicationId}:{code}");
    }

    private void RecordFailure(string code)
    {
        _failures.Enqueue(code);
        while (_failures.Count > MaximumFailures) _failures.Dequeue();
    }

    private void Prune()
    {
        while (_applications.Count >= MaximumApplications && _order.TryDequeue(out string? id))
            if (_applications.TryGetValue(id, out Envelope? value) && value.Applied &&
                (_current is null || _current.Snapshot.ApplicationId != id))
                _applications.Remove(id);
    }

    public void Dispose()
    {
        CancellationTokenSource changed;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _applications.Clear();
            changed = _changed;
        }
        CancelAndDispose(changed);
    }

    private static void CancelAndDispose(CancellationTokenSource source)
    {
        try { source.Cancel(); }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) { }
        try { source.Dispose(); }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) { }
    }

    internal sealed class StagedAppliedRuntime
    {
        internal StagedAppliedRuntime(
            Envelope envelope,
            GatewayDestinationResolver.GatewayResolutionPromotion promotion,
            GatewayAppliedRuntimeObservation observation,
            bool requiresAcknowledgement)
        {
            Envelope = envelope;
            Promotion = promotion;
            Observation = observation;
            RequiresAcknowledgement = requiresAcknowledgement;
        }

        internal Envelope Envelope { get; }
        internal GatewayDestinationResolver.GatewayResolutionPromotion Promotion { get; }
        internal GatewayAppliedRuntimeObservation Observation { get; }
        internal bool RequiresAcknowledgement { get; }
    }

    internal sealed class Envelope(GatewayPreparedApplication application, string namespaceId, string targetNodeId)
    {
        internal GatewayPreparedApplication Application { get; } = application;
        internal string NamespaceId { get; } = namespaceId;
        internal string TargetNodeId { get; } = targetNodeId;
        internal bool Poisoned { get; set; }
        internal bool Applied { get; set; }
    }
}
