using System.Collections.Immutable;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Health;
using Yarp.ReverseProxy.Model;
using Yarp.ReverseProxy.Configuration;

namespace HPD.Gateway;

internal sealed class GatewayStatusCoordinator : BackgroundService, IGatewayStatusReader
{
    private const int MaximumUpstreams = 4_096;
    private const int MaximumReasons = 64;
    private readonly object _sync = new();
    private readonly IGatewayPublicationObservationReader _publication;
    private readonly IGatewayNodeAppliedRuntimeReader _appliedRuntime;
    private readonly IProxyStateLookup _proxy;
    private readonly GatewayHostRuntimeStatus? _host;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly string _processInstanceId = Guid.NewGuid().ToString("N");
    private readonly IDisposable _publicationSubscription;
    private readonly IDisposable _appliedRuntimeSubscription;
    private GatewayStatusSnapshot _current;
    private string _currentKey = string.Empty;
    private CancellationTokenSource _changed = new();
    private bool _disposed;

    internal GatewayStatusCoordinator(
        IEnumerable<IGatewayPublicationObservationReader> publications,
        IEnumerable<IGatewayNodeAppliedRuntimeReader> appliedRuntimeReaders,
        IProxyStateLookup proxy,
        IEnumerable<GatewayHostRuntimeStatus> hosts,
        IHostApplicationLifetime lifetime)
    {
        var installedPublications = publications.ToArray();
        if (installedPublications.Length != 1)
            throw new InvalidOperationException("Exactly one HPD Gateway publication status authority must be installed.");
        _publication = installedPublications[0];
        var installedAppliedRuntimeReaders = appliedRuntimeReaders.ToArray();
        if (installedAppliedRuntimeReaders.Length != 1)
            throw new InvalidOperationException("Exactly one HPD Gateway applied-runtime authority must be installed.");
        _appliedRuntime = installedAppliedRuntimeReaders[0];
        _proxy = proxy;
        _lifetime = lifetime;
        var installedHosts = hosts.ToArray();
        if (installedHosts.Length > 1) throw new InvalidOperationException("At most one HPD Gateway host status authority may be installed.");
        _host = installedHosts.SingleOrDefault();
        _current = Build(1, DateTimeOffset.UtcNow);
        _currentKey = Key(_current);
        _publicationSubscription = ChangeToken.OnChange(_publication.GetChangeToken, Refresh);
        _appliedRuntimeSubscription = ChangeToken.OnChange(
            () => new CancellationChangeToken(_appliedRuntime.GetChangeToken()), Refresh);
    }

    public GatewayStatusSnapshot GetCurrent()
    {
        Refresh();
        lock (_sync) return _current;
    }

    public IChangeToken GetChangeToken()
    {
        lock (_sync) return new CancellationChangeToken(_changed.Token);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) Refresh();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        Refresh(forceStopping: true);
        return base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        CancellationTokenSource changed;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            changed = _changed;
        }
        _publicationSubscription.Dispose();
        _appliedRuntimeSubscription.Dispose();
        try { changed.Cancel(); }
        catch (AggregateException) { }
        changed.Dispose();
        base.Dispose();
    }

    private void Refresh() => Refresh(forceStopping: false);

    private void Refresh(bool forceStopping)
    {
        GatewayStatusSnapshot candidate;
        string key;
        CancellationTokenSource? previous = null;
        lock (_sync)
        {
            if (_disposed) return;
            var nextSequence = _current.SnapshotSequence == ulong.MaxValue ? ulong.MaxValue : _current.SnapshotSequence + 1;
            candidate = Build(nextSequence, DateTimeOffset.UtcNow, forceStopping);
            key = Key(candidate);
            if (StringComparer.Ordinal.Equals(key, _currentKey)) return;
            candidate = candidate with { Conditions = PreserveTransitions(candidate.Conditions, _current.Conditions) };
            _current = candidate;
            _currentKey = key;
            previous = _changed;
            _changed = new();
        }
        try { previous.Cancel(); }
        catch (AggregateException) { }
        previous.Dispose();
    }

    private GatewayStatusSnapshot Build(ulong sequence, DateTimeOffset now, bool forceStopping = false)
    {
        var publication = _publication.GetCurrent();
        GatewayAppliedRuntimeObservation? applied = _appliedRuntime.GetCurrent();
        var host = BuildHost(sequence, now, forceStopping);
        var publicationStatus = BuildPublication(publication);
        bool appliedMatches = MatchesApplied(publicationStatus.Active, applied?.Snapshot);
        var upstreams = BuildUpstreams(publication, applied?.Snapshot, appliedMatches,
            sequence, now, out var truncated, out var upstreamsReady);
        var reasons = ImmutableArray.CreateBuilder<GatewayStatusReason>();
        var configurationReady = publicationStatus.Active is not null &&
            publicationStatus.State != GatewayStatusPublicationState.PublicationIndeterminate &&
            appliedMatches;
        if (!configurationReady)
            reasons.Add(Reason(publicationStatus.State == GatewayStatusPublicationState.PublicationIndeterminate
                ? "gateway.publication.indeterminate"
                : publicationStatus.Active is null ? "gateway.config.no_active_acknowledgement"
                : "gateway.runtime.applied_mismatch", "Configuration is not positively acknowledged and correlated to applied runtime truth."));
        var hostReady = host.State is GatewayStatusHostState.NotApplicable or GatewayStatusHostState.Ready or GatewayStatusHostState.RestartRequired;
        if (!hostReady) reasons.Add(Reason("gateway.host.not_ready", "The required host is not ready."));
        var upstreamPoliciesReady = !truncated && upstreamsReady;
        var destinationsEligible = !truncated && upstreams.All(static upstream => upstream.AvailableDestinationCount > 0);
        if (!upstreamPoliciesReady) reasons.Add(Reason("gateway.destination.none_eligible", "At least one active Upstream does not satisfy its discovery and native-readiness rule."));
        var providersAcceptable = upstreams.All(static upstream => upstream.Discovery.State is
            GatewayDiscoveryObservationState.NotRequired or GatewayDiscoveryObservationState.AppliedFresh or
            GatewayDiscoveryObservationState.AppliedFreshEmpty or GatewayDiscoveryObservationState.AppliedLastKnownDegraded or
            GatewayDiscoveryObservationState.AppliedUnavailable);
        if (!providersAcceptable) reasons.Add(Reason("gateway.discovery.not_acceptable", "At least one discovery observation is unresolved or failed."));
        if (truncated) reasons.Add(Reason("gateway.status.details_truncated", "Status details were truncated at the configured bound."));
        var boundedReasons = reasons.DistinctBy(static reason => reason.Code).OrderBy(static reason => reason.Code, StringComparer.Ordinal).Take(MaximumReasons).ToImmutableArray();
        var servingReady = configurationReady && hostReady && upstreamPoliciesReady && !forceStopping && !_lifetime.ApplicationStopping.IsCancellationRequested;
        var stamp = Stamp("hpd.status", "node", sequence, publicationStatus.Active?.NativeRevisionId, now);
        var readiness = new GatewayReadinessStatus(
            configurationReady ? GatewayReadinessState.Ready : GatewayReadinessState.NotReady,
            servingReady ? GatewayReadinessState.Ready : GatewayReadinessState.NotReady,
            boundedReasons,
            stamp);
        var conditions = BuildConditions(sequence, now, configurationReady, servingReady, host, publicationStatus, providersAcceptable, destinationsEligible);
        return new GatewayStatusSnapshot(
            _processInstanceId,
            sequence,
            now,
            new(GatewayStatusIntentState.NotManaged, Stamp("embedded", "embedded", sequence, null, now)),
            new(publication.LatestOutcome is null ? GatewayStatusPreparationState.NotPrepared : GatewayStatusPreparationState.Prepared,
                publication.LatestOutcome?.Attempted.CandidateId.Value,
                Stamp("hpd.core", "candidate", sequence, publication.LatestOutcome?.Attempted.ContentHash.Value, now)),
            host,
            publicationStatus,
            upstreams,
            readiness,
            conditions,
            truncated || reasons.Count > MaximumReasons);
    }

    private static bool MatchesApplied(
        GatewayActiveConfigurationIdentity? active,
        GatewayAppliedRuntimeSnapshot? applied) =>
        active is not null && applied is not null && applied.IsComplete && !applied.IsTruncated &&
        StringComparer.Ordinal.Equals(active.CandidateId, applied.CandidateId.Value) &&
        StringComparer.Ordinal.Equals(active.ContentHash, applied.CandidateContentHash.Value) &&
        StringComparer.Ordinal.Equals(active.ApplicationId, applied.ApplicationId) &&
        active.SymbolicPlanIdentity == applied.SymbolicPlanIdentity;

    private GatewayHostStatus BuildHost(ulong sequence, DateTimeOffset now, bool forceStopping)
    {
        if (_host is null)
            return new(GatewayStatusHostState.NotApplicable, null, null, [], Stamp("external", "host", sequence, null, now));
        var source = _host.GetSnapshot();
        var state = forceStopping ? GatewayStatusHostState.Stopping : source.State switch
        {
            GatewayHostRealizationState.NotStarted => GatewayStatusHostState.NotStarted,
            GatewayHostRealizationState.Starting => GatewayStatusHostState.Starting,
            GatewayHostRealizationState.Ready => GatewayStatusHostState.Ready,
            GatewayHostRealizationState.RestartRequired => GatewayStatusHostState.RestartRequired,
            GatewayHostRealizationState.Failed => GatewayStatusHostState.Failed,
            GatewayHostRealizationState.Stopping => GatewayStatusHostState.Stopping,
            GatewayHostRealizationState.Stopped => GatewayStatusHostState.Stopped,
            _ => GatewayStatusHostState.Failed
        };
        return new(state, source.RunningConfigurationHash, source.DesiredConfigurationHash, [],
            Stamp("hpd.hosting", source.HostId.Value, sequence, source.RunningConfigurationHash, now));
    }

    private GatewayPublicationStatus BuildPublication(GatewayPublicationObservation source)
    {
        var outcome = source.LatestOutcome;
        var state = outcome?.State switch
        {
            null => GatewayStatusPublicationState.NotAttempted,
            GatewayPublicationState.ActiveAcknowledged => GatewayStatusPublicationState.ActiveAcknowledged,
            GatewayPublicationState.PublicationIndeterminate => GatewayStatusPublicationState.PublicationIndeterminate,
            GatewayPublicationState.Duplicate => GatewayStatusPublicationState.Duplicate,
            GatewayPublicationState.Stale => GatewayStatusPublicationState.Stale,
            GatewayPublicationState.IdentityConflict => GatewayStatusPublicationState.IdentityConflict,
            GatewayPublicationState.Superseded => GatewayStatusPublicationState.Superseded,
            GatewayPublicationState.CanceledBeforePublish => GatewayStatusPublicationState.CanceledBeforePublish,
            _ => GatewayStatusPublicationState.RejectedBeforePublish
        };
        var reasons = outcome?.Diagnostics.OrderBy(static value => value.Code, StringComparer.Ordinal)
            .Take(4).Select(static value => new GatewayStatusReason(value.Code, null, null, value.SafeMessage)).ToImmutableArray() ?? [];
        return new(state, outcome?.Attempted.CandidateId.Value, Convert(source.Active), Convert(source.LastKnownGood), reasons,
            new GatewayStatusObservationStamp("hpd.yarp", "publication", _processInstanceId,
                source.Sequence, source.Active?.NativeRevisionId, source.ObservedAt));
    }

    private ImmutableArray<GatewayNativeUpstreamStatus> BuildUpstreams(
        GatewayPublicationObservation publication,
        GatewayAppliedRuntimeSnapshot? applied,
        bool appliedCorrelated,
        ulong sequence,
        DateTimeOffset now,
        out bool truncated,
        out bool ready)
    {
        truncated = publication.ActiveUpstreams.Length > MaximumUpstreams || applied?.Upstreams.Length > MaximumUpstreams;
        ready = appliedCorrelated;
        var result = ImmutableArray.CreateBuilder<GatewayNativeUpstreamStatus>();
        ImmutableDictionary<string, GatewayAppliedUpstream> appliedById = applied?.Upstreams
            .ToImmutableDictionary(static value => value.UpstreamId, StringComparer.Ordinal) ??
            ImmutableDictionary<string, GatewayAppliedUpstream>.Empty.WithComparers(StringComparer.Ordinal);
        foreach (var expected in publication.ActiveUpstreams
            .OrderBy(static value => value.UpstreamId, StringComparer.Ordinal)
            .Take(MaximumUpstreams))
        {
            if (!appliedById.TryGetValue(expected.UpstreamId, out GatewayAppliedUpstream? appliedUpstream))
            {
                ready = false;
                result.Add(NotObserved(expected, null, sequence, now,
                    "gateway.runtime.upstream_not_applied", "The active Upstream has no correlated applied-runtime observation."));
                continue;
            }
            if (!appliedCorrelated)
            {
                ready = false;
                result.Add(new(expected.UpstreamId, 0, 0, 0, 0, 0, 0, 0, 0,
                    GatewayNativeEligibilityState.NotObserved, expected.AvailabilityPolicy,
                    BuildDiscoveryStatus(appliedUpstream, applied!.AppliedAt, GatewayDiscoveryObservationState.Indeterminate), false,
                    [Reason("gateway.runtime.applied_mismatch", "Applied runtime identity does not match the acknowledged application.", "Upstream", expected.UpstreamId)],
                    Stamp("hpd.runtime", expected.UpstreamId, sequence, applied.ApplicationId, now)));
                continue;
            }
            try
            {
                if (!_proxy.TryGetCluster(expected.UpstreamId, out var cluster))
                {
                    ready = false;
                    result.Add(NotObserved(expected, appliedUpstream, sequence, now,
                        "gateway.destination.cluster_not_observed", "The active native Cluster was not observed."));
                    continue;
                }
                var state = cluster.DestinationsState;
                var all = state.AllDestinations.ToArray();
                var available = state.AvailableDestinations.Count;
                var health = cluster.Model.Config.HealthCheck;
                var activeEnabled = health?.Active?.Enabled == true;
                var passiveEnabled = health?.Passive?.Enabled == true;
                var panic = StringComparer.OrdinalIgnoreCase.Equals(expected.AvailabilityPolicy, HealthCheckConstants.AvailableDestinations.HealthyOrPanic) &&
                    (activeEnabled || passiveEnabled) && all.Length > 0 && available == all.Length && all.All(destination =>
                        activeEnabled && destination.Health.Active == DestinationHealth.Unhealthy ||
                        passiveEnabled && destination.Health.Passive == DestinationHealth.Unhealthy);
                GatewayPreparedMembershipDisposition disposition = appliedUpstream.Disposition switch
                {
                    GatewayAppliedMembershipDisposition.LastKnownMembership => GatewayPreparedMembershipDisposition.LastKnownMembership,
                    GatewayAppliedMembershipDisposition.UnavailableWhenStale => GatewayPreparedMembershipDisposition.UnavailableWhenStale,
                    GatewayAppliedMembershipDisposition.RefreshFailed => GatewayPreparedMembershipDisposition.RefreshFailed,
                    _ => GatewayPreparedMembershipDisposition.Fresh,
                };
                ContentHash nativeMembership = GatewayRuntimeGraphIdentity.ComputeMembership(cluster.Model.Config.Destinations ??
                    ImmutableDictionary<string, DestinationConfig>.Empty, disposition);
                IReadOnlyDictionary<string, string>? metadata = cluster.Model.Config.Metadata;
                bool nativeConsistent = all.Length == appliedUpstream.DestinationCount && nativeMembership == appliedUpstream.MembershipIdentity &&
                    publication.Active is { } active &&
                    metadata is not null &&
                    metadata.TryGetValue(GatewayRuntimePlanner.ApplicationIdMetadata, out string? applicationId) &&
                    StringComparer.Ordinal.Equals(applicationId, active.ApplicationId) &&
                    metadata.TryGetValue(GatewayRuntimePlanner.SymbolicPlanIdentityMetadata, out string? planIdentity) &&
                    StringComparer.Ordinal.Equals(planIdentity, active.SymbolicPlanIdentity.Value);
                GatewayDiscoveryStatus discovery = BuildDiscoveryStatus(appliedUpstream, applied!.AppliedAt,
                    appliedCorrelated && nativeConsistent ? null : GatewayDiscoveryObservationState.Indeterminate);
                bool upstreamReady = appliedCorrelated && nativeConsistent && IsUpstreamReady(discovery.State, available);
                ready &= upstreamReady;
                ImmutableArray<GatewayStatusReason> upstreamReasons = nativeConsistent ? [] :
                    [Reason("gateway.runtime.native_membership_mismatch", "Applied membership does not match native destination state.", "Upstream", expected.UpstreamId)];
                result.Add(new(expected.UpstreamId, all.Length, available,
                    Count(all, true, DestinationHealth.Healthy), Count(all, true, DestinationHealth.Unhealthy), Count(all, true, DestinationHealth.Unknown),
                    Count(all, false, DestinationHealth.Healthy), Count(all, false, DestinationHealth.Unhealthy), Count(all, false, DestinationHealth.Unknown),
                    available == 0 ? GatewayNativeEligibilityState.NoEligibleDestinations : panic ? GatewayNativeEligibilityState.PanicFallbackInUse : GatewayNativeEligibilityState.EligibleDestinationsPresent,
                    expected.AvailabilityPolicy, discovery, false, upstreamReasons, Stamp("yarp", expected.UpstreamId, sequence, nativeMembership.Value, now)));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                ready = false;
                result.Add(NotObserved(expected, appliedUpstream, sequence, now,
                    "gateway.destination.observation_failed", "Native destination state could not be observed."));
            }
        }
        if (applied is not null && applied.Upstreams.Any(value => !publication.ActiveUpstreams.Any(expected => expected.UpstreamId == value.UpstreamId)))
            truncated = ready = false;
        return result.ToImmutable();
    }

    internal static GatewayDiscoveryStatus BuildDiscoveryStatus(
        GatewayAppliedUpstream upstream,
        DateTimeOffset appliedAt,
        GatewayDiscoveryObservationState? forced = null) => new(
            forced ?? (upstream.Kind == GatewayAppliedUpstreamKind.Static
                ? GatewayDiscoveryObservationState.NotRequired
                : upstream.Disposition switch
                {
                    GatewayAppliedMembershipDisposition.Fresh when upstream.DestinationCount == 0 => GatewayDiscoveryObservationState.AppliedFreshEmpty,
                    GatewayAppliedMembershipDisposition.Fresh => GatewayDiscoveryObservationState.AppliedFresh,
                    GatewayAppliedMembershipDisposition.LastKnownMembership => GatewayDiscoveryObservationState.AppliedLastKnownDegraded,
                    GatewayAppliedMembershipDisposition.UnavailableWhenStale => GatewayDiscoveryObservationState.AppliedUnavailable,
                    GatewayAppliedMembershipDisposition.RefreshFailed => GatewayDiscoveryObservationState.RefreshFailed,
                    _ => GatewayDiscoveryObservationState.Indeterminate,
                }),
            upstream.DiscoveryProfile, upstream.Service, upstream.Endpoint, upstream.MembershipGeneration,
            upstream.MembershipIdentity, upstream.DestinationCount, appliedAt, upstream.SafeDiagnostic);

    internal static bool IsUpstreamReady(GatewayDiscoveryObservationState state, int available) => state switch
    {
        GatewayDiscoveryObservationState.NotRequired or GatewayDiscoveryObservationState.AppliedFresh or
            GatewayDiscoveryObservationState.AppliedLastKnownDegraded => available > 0,
        GatewayDiscoveryObservationState.AppliedUnavailable => true,
        _ => false,
    };

    private static int Count(DestinationState[] values, bool active, DestinationHealth expected) =>
        values.Count(destination => (active ? destination.Health.Active : destination.Health.Passive) == expected);

    private GatewayNativeUpstreamStatus NotObserved(
        GatewayPublishedUpstream expected, GatewayAppliedUpstream? applied, ulong sequence, DateTimeOffset now, string code, string message) =>
        new(expected.UpstreamId, 0, 0, 0, 0, 0, 0, 0, 0,
            GatewayNativeEligibilityState.NotObserved, expected.AvailabilityPolicy,
            applied is null
                ? new(GatewayDiscoveryObservationState.Resolving, null, null, null, null, null, 0, null, message)
                : BuildDiscoveryStatus(applied, DateTimeOffset.MinValue, GatewayDiscoveryObservationState.NotObserved), false,
            [Reason(code, message, "Upstream", expected.UpstreamId)],
            Stamp("yarp", expected.UpstreamId, sequence, null, now));

    private ImmutableArray<GatewayCondition> BuildConditions(
        ulong sequence, DateTimeOffset now, bool configurationReady, bool servingReady,
        GatewayHostStatus host, GatewayPublicationStatus publication, bool providersAcceptable, bool destinationsReady)
    {
        var hostReady = host.State is GatewayStatusHostState.NotApplicable or GatewayStatusHostState.Ready or GatewayStatusHostState.RestartRequired;
        return
        [
            Condition(GatewayConditionType.ConfigurationReady, configurationReady, configurationReady ? "gateway.ready" : "gateway.config.not_ready", sequence, now),
            Condition(GatewayConditionType.ServingReady, servingReady, servingReady ? "gateway.ready" : "gateway.serving.not_ready", sequence, now),
            Condition(GatewayConditionType.HostReady, hostReady, hostReady ? "gateway.host.ready" : "gateway.host.not_ready", sequence, now),
            Condition(GatewayConditionType.HostRestartRequired, host.State == GatewayStatusHostState.RestartRequired, host.State == GatewayStatusHostState.RestartRequired ? "gateway.host.restart_required" : "gateway.host.current", sequence, now),
            Condition(GatewayConditionType.PublicationCertain, publication.State != GatewayStatusPublicationState.PublicationIndeterminate, publication.State == GatewayStatusPublicationState.PublicationIndeterminate ? "gateway.publication.indeterminate" : "gateway.publication.certain", sequence, now),
            Condition(GatewayConditionType.ProvidersAcceptable, providersAcceptable, providersAcceptable ? "gateway.discovery.acceptable" : "gateway.discovery.not_acceptable", sequence, now),
            Condition(GatewayConditionType.DestinationsEligible, destinationsReady, destinationsReady ? "gateway.destination.eligible" : "gateway.destination.none_eligible", sequence, now)
        ];
    }

    private static GatewayCondition Condition(GatewayConditionType type, bool value, string reason, ulong sequence, DateTimeOffset now) =>
        new(type, value ? GatewayConditionValue.True : GatewayConditionValue.False, reason, now, sequence);

    private static ImmutableArray<GatewayCondition> PreserveTransitions(ImmutableArray<GatewayCondition> next, ImmutableArray<GatewayCondition> previous) =>
        next.Select(condition => previous.FirstOrDefault(old => old.Type == condition.Type) is { } old && old.Value == condition.Value && old.ReasonCode == condition.ReasonCode
            ? condition with { LastTransitionAt = old.LastTransitionAt }
            : condition).ToImmutableArray();

    private GatewayStatusObservationStamp Stamp(string kind, string id, ulong sequence, string? identity, DateTimeOffset now) =>
        new(kind, id, _processInstanceId, sequence, identity, now);

    private static GatewayActiveConfigurationIdentity? Convert(ActivePublicationIdentity? value) => value is null ? null :
        new(value.Candidate.CandidateId.Value, value.Candidate.ContentHash.Value,
            value.ApplicationId, value.SymbolicPlanIdentity, value.NativeRevisionId, value.AcknowledgedAt);

    private static GatewayStatusReason Reason(string code, string message, string? resourceKind = null, string? resourceId = null) =>
        new(code, resourceKind, resourceId, message);

    private static string Key(GatewayStatusSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.Append(snapshot.Host.State).Append('|').Append(snapshot.Host.RunningConfigurationHash).Append('|').Append(snapshot.Host.DesiredConfigurationHash)
            .Append('|').Append(snapshot.Publication.State).Append('|').Append(snapshot.Publication.AttemptedCandidateId).Append('|').Append(snapshot.Publication.Active?.NativeRevisionId)
            .Append('|').Append(snapshot.Publication.Active?.ApplicationId).Append('|').Append(snapshot.Publication.Active?.SymbolicPlanIdentity.Value)
            .Append('|').Append(snapshot.Publication.LastKnownGood?.NativeRevisionId).Append('|').Append(snapshot.Publication.Stamp.ObservationSequence)
            .Append('|').Append(snapshot.Readiness.Configuration).Append('|').Append(snapshot.Readiness.Serving)
            .Append('|').Append(snapshot.DetailsTruncated);
        foreach (var reason in snapshot.Publication.Reasons) builder.Append('|').Append(reason.Code);
        foreach (var reason in snapshot.Readiness.Reasons) builder.Append('|').Append(reason.Code);
        foreach (var condition in snapshot.Conditions)
            builder.Append('|').Append(condition.Type).Append(':').Append(condition.Value).Append(':').Append(condition.ReasonCode);
        foreach (var upstream in snapshot.Upstreams)
            builder.Append('|').Append(upstream.UpstreamId).Append(':').Append(upstream.AllDestinationCount).Append(':').Append(upstream.AvailableDestinationCount)
                .Append(':').Append(upstream.ActiveHealthyCount).Append(':').Append(upstream.ActiveUnhealthyCount).Append(':').Append(upstream.ActiveUnknownCount)
                .Append(':').Append(upstream.PassiveHealthyCount).Append(':').Append(upstream.PassiveUnhealthyCount).Append(':').Append(upstream.PassiveUnknownCount)
                .Append(':').Append(upstream.Eligibility).Append(':').Append(upstream.AvailabilityPolicy)
                .Append(':').Append(upstream.Discovery.State).Append(':').Append(upstream.Discovery.MembershipGeneration)
                .Append(':').Append(upstream.Discovery.MembershipIdentity?.Value).Append(':').Append(upstream.Discovery.AppliedDestinationCount)
                .Append(':').Append(upstream.Discovery.AppliedAt?.UtcTicks).Append(':').Append(upstream.Discovery.SafeDiagnostic);
        return builder.ToString();
    }
}
