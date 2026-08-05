using System.Collections.Immutable;
using System.Text;
using HPD.Gateway.Hosting;
using HPD.Gateway.Yarp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Health;
using Yarp.ReverseProxy.Model;

namespace HPD.Gateway.Status;

internal sealed class GatewayStatusCoordinator : BackgroundService, IGatewayStatusReader
{
    private const int MaximumUpstreams = 4_096;
    private const int MaximumReasons = 64;
    private readonly object _sync = new();
    private readonly IGatewayPublicationObservationReader _publication;
    private readonly IProxyStateLookup _proxy;
    private readonly GatewayHostRuntimeStatus? _host;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly string _processInstanceId = Guid.NewGuid().ToString("N");
    private readonly IDisposable _publicationSubscription;
    private GatewayStatusSnapshot _current;
    private string _currentKey = string.Empty;
    private CancellationTokenSource _changed = new();
    private bool _disposed;

    internal GatewayStatusCoordinator(
        IEnumerable<IGatewayPublicationObservationReader> publications,
        IProxyStateLookup proxy,
        IEnumerable<GatewayHostRuntimeStatus> hosts,
        IHostApplicationLifetime lifetime)
    {
        var installedPublications = publications.ToArray();
        if (installedPublications.Length != 1)
            throw new InvalidOperationException("Exactly one HPD Gateway publication status authority must be installed.");
        _publication = installedPublications[0];
        _proxy = proxy;
        _lifetime = lifetime;
        var installedHosts = hosts.ToArray();
        if (installedHosts.Length > 1) throw new InvalidOperationException("At most one HPD Gateway host status authority may be installed.");
        _host = installedHosts.SingleOrDefault();
        _current = Build(1, DateTimeOffset.UtcNow);
        _currentKey = Key(_current);
        _publicationSubscription = ChangeToken.OnChange(_publication.GetChangeToken, Refresh);
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
        var host = BuildHost(sequence, now, forceStopping);
        var publicationStatus = BuildPublication(publication);
        var upstreams = BuildUpstreams(publication, sequence, now, out var truncated);
        var reasons = ImmutableArray.CreateBuilder<GatewayStatusReason>();
        var configurationReady = publicationStatus.Active is not null &&
            publicationStatus.State != GatewayStatusPublicationState.PublicationIndeterminate;
        if (!configurationReady)
            reasons.Add(Reason(publicationStatus.State == GatewayStatusPublicationState.PublicationIndeterminate
                ? "gateway.publication.indeterminate" : "gateway.config.no_active_acknowledgement", "Configuration is not positively acknowledged."));
        var hostReady = host.State is GatewayStatusHostState.NotApplicable or GatewayStatusHostState.Ready or GatewayStatusHostState.RestartRequired;
        if (!hostReady) reasons.Add(Reason("gateway.host.not_ready", "The required host is not ready."));
        var destinationsReady = !truncated && upstreams.All(static upstream => upstream.AvailableDestinationCount > 0);
        if (!destinationsReady) reasons.Add(Reason("gateway.destination.none_eligible", "At least one active Upstream has no eligible destination."));
        if (truncated) reasons.Add(Reason("gateway.status.details_truncated", "Status details were truncated at the configured bound."));
        var boundedReasons = reasons.DistinctBy(static reason => reason.Code).OrderBy(static reason => reason.Code, StringComparer.Ordinal).Take(MaximumReasons).ToImmutableArray();
        var servingReady = configurationReady && hostReady && destinationsReady && !forceStopping && !_lifetime.ApplicationStopping.IsCancellationRequested;
        var stamp = Stamp("hpd.status", "node", sequence, publicationStatus.Active?.NativeRevisionId, now);
        var readiness = new GatewayReadinessStatus(
            configurationReady ? GatewayReadinessState.Ready : GatewayReadinessState.NotReady,
            servingReady ? GatewayReadinessState.Ready : GatewayReadinessState.NotReady,
            boundedReasons,
            stamp);
        var conditions = BuildConditions(sequence, now, configurationReady, servingReady, host, publicationStatus, destinationsReady);
        return new GatewayStatusSnapshot(
            _processInstanceId,
            sequence,
            now,
            new(GatewayStatusIntentState.NotManaged, Stamp("embedded", "embedded", sequence, null, now)),
            new(publication.LatestOutcome is null ? GatewayStatusPreparationState.NotPrepared : GatewayStatusPreparationState.Materialized,
                publication.LatestOutcome?.Attempted.CandidateId.Value,
                Stamp("hpd.core", "candidate", sequence, publication.LatestOutcome?.Attempted.ContentHash.Value, now)),
            host,
            publicationStatus,
            upstreams,
            readiness,
            conditions,
            truncated || reasons.Count > MaximumReasons);
    }

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
        ulong sequence,
        DateTimeOffset now,
        out bool truncated)
    {
        truncated = publication.ActiveUpstreams.Length > MaximumUpstreams;
        var result = ImmutableArray.CreateBuilder<GatewayNativeUpstreamStatus>();
        foreach (var expected in publication.ActiveUpstreams
            .OrderBy(static value => value.UpstreamId, StringComparer.Ordinal)
            .Take(MaximumUpstreams))
        {
            try
            {
                if (!_proxy.TryGetCluster(expected.UpstreamId, out var cluster))
                {
                    result.Add(NotObserved(expected, sequence, now,
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
                result.Add(new(expected.UpstreamId, all.Length, available,
                    Count(all, true, DestinationHealth.Healthy), Count(all, true, DestinationHealth.Unhealthy), Count(all, true, DestinationHealth.Unknown),
                    Count(all, false, DestinationHealth.Healthy), Count(all, false, DestinationHealth.Unhealthy), Count(all, false, DestinationHealth.Unknown),
                    available == 0 ? GatewayNativeEligibilityState.NoEligibleDestinations : panic ? GatewayNativeEligibilityState.PanicFallbackInUse : GatewayNativeEligibilityState.EligibleDestinationsPresent,
                    expected.AvailabilityPolicy, false, [], Stamp("yarp", expected.UpstreamId, sequence, null, now)));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                result.Add(NotObserved(expected, sequence, now,
                    "gateway.destination.observation_failed", "Native destination state could not be observed."));
            }
        }
        return result.ToImmutable();
    }

    private static int Count(DestinationState[] values, bool active, DestinationHealth expected) =>
        values.Count(destination => (active ? destination.Health.Active : destination.Health.Passive) == expected);

    private GatewayNativeUpstreamStatus NotObserved(
        GatewayPublishedUpstream expected, ulong sequence, DateTimeOffset now, string code, string message) =>
        new(expected.UpstreamId, 0, 0, 0, 0, 0, 0, 0, 0,
            GatewayNativeEligibilityState.NotObserved, expected.AvailabilityPolicy, false,
            [Reason(code, message, "Upstream", expected.UpstreamId)],
            Stamp("yarp", expected.UpstreamId, sequence, null, now));

    private ImmutableArray<GatewayCondition> BuildConditions(
        ulong sequence, DateTimeOffset now, bool configurationReady, bool servingReady,
        GatewayHostStatus host, GatewayPublicationStatus publication, bool destinationsReady)
    {
        var hostReady = host.State is GatewayStatusHostState.NotApplicable or GatewayStatusHostState.Ready or GatewayStatusHostState.RestartRequired;
        return
        [
            Condition(GatewayConditionType.ConfigurationReady, configurationReady, configurationReady ? "gateway.ready" : "gateway.config.not_ready", sequence, now),
            Condition(GatewayConditionType.ServingReady, servingReady, servingReady ? "gateway.ready" : "gateway.serving.not_ready", sequence, now),
            Condition(GatewayConditionType.HostReady, hostReady, hostReady ? "gateway.host.ready" : "gateway.host.not_ready", sequence, now),
            Condition(GatewayConditionType.HostRestartRequired, host.State == GatewayStatusHostState.RestartRequired, host.State == GatewayStatusHostState.RestartRequired ? "gateway.host.restart_required" : "gateway.host.current", sequence, now),
            Condition(GatewayConditionType.PublicationCertain, publication.State != GatewayStatusPublicationState.PublicationIndeterminate, publication.State == GatewayStatusPublicationState.PublicationIndeterminate ? "gateway.publication.indeterminate" : "gateway.publication.certain", sequence, now),
            Condition(GatewayConditionType.ProvidersAcceptable, true, "gateway.providers.not_applicable", sequence, now),
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
        new(value.Candidate.CandidateId.Value, value.Candidate.ContentHash.Value, value.NativeRevisionId, value.AcknowledgedAt);

    private static GatewayStatusReason Reason(string code, string message, string? resourceKind = null, string? resourceId = null) =>
        new(code, resourceKind, resourceId, message);

    private static string Key(GatewayStatusSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.Append(snapshot.Host.State).Append('|').Append(snapshot.Host.RunningConfigurationHash).Append('|').Append(snapshot.Host.DesiredConfigurationHash)
            .Append('|').Append(snapshot.Publication.State).Append('|').Append(snapshot.Publication.AttemptedCandidateId).Append('|').Append(snapshot.Publication.Active?.NativeRevisionId)
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
                .Append(':').Append(upstream.Eligibility).Append(':').Append(upstream.AvailabilityPolicy);
        return builder.ToString();
    }
}
