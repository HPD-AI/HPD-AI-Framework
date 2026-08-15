using System.Collections.Immutable;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace HPD.Gateway;

internal sealed class GatewayRuntimePublisher : IGatewayPublicationObservationReader, IDisposable
{
    private const int MaximumRememberedAttempts = 4_096;
    private readonly HpdProxyConfigProvider _provider;
    private readonly HpdConfigChangeListener _listener;
    private readonly GatewayDestinationResolver _destinationResolver;
    private readonly GatewayRuntimeApplicationObserver? _runtimeObserver;
    private readonly bool _ownsDestinationResolver;
    private readonly SemaphoreSlim _publicationLease = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _stateLock = new();
    private readonly object _lifecycleLock = new();
    private readonly Dictionary<AttemptKey, Attempt> _attempts = [];
    private readonly Dictionary<string, AttemptKey> _authorityHeads = new(StringComparer.Ordinal);
    private readonly Queue<AttemptKey> _attemptOrder = [];
    private readonly HashSet<string> _nativeRevisions = new(StringComparer.Ordinal);
    private ActivePublicationIdentity? _lastKnownGood;
    private ActivePublicationIdentity? _active;
    private ImmutableArray<GatewayPublishedUpstream> _activeUpstreams = [];
    private GatewayPublicationObservation _observation = new(0, DateTimeOffset.UtcNow, null, null, null, []);
    private CancellationTokenSource _observationChanged = new();
    private volatile bool _disposed;

    internal GatewayRuntimePublisher(
        HpdProxyConfigProvider provider,
        HpdConfigChangeListener listener,
        IEnumerable<IProxyConfigProvider> configuredProviders,
        GatewayDestinationResolver? destinationResolver = null,
        GatewayRuntimeApplicationObserver? runtimeObserver = null)
    {
        _provider = provider;
        _listener = listener;
        _ownsDestinationResolver = destinationResolver is null;
        _destinationResolver = destinationResolver ?? new GatewayDestinationResolver(
            new GatewayDiscoveryProfileRegistry([]), new PassthroughConfigValidator(), TimeProvider.System);
        _runtimeObserver = runtimeObserver;
        var providers = configuredProviders.ToArray();
        if (providers.Length != 1 || !ReferenceEquals(providers[0], provider))
            throw new InvalidOperationException("Managed publication requires exactly one HPD-owned IProxyConfigProvider.");
    }

    internal Task<GatewayPublicationOutcome> PublishAsync(
        GatewayPreparedApplication application,
        TimeSpan acknowledgementTimeout,
        CancellationToken cancellationToken = default)
        => PublishCoreAsync(application, null, null, acknowledgementTimeout, cancellationToken);

    internal Task<GatewayPublicationOutcome> PublishAsync(
        GatewayPreparedApplication application,
        string namespaceId,
        string targetNodeId,
        TimeSpan acknowledgementTimeout,
        CancellationToken cancellationToken = default)
        => PublishCoreAsync(application, namespaceId, targetNodeId, acknowledgementTimeout, cancellationToken);

    private Task<GatewayPublicationOutcome> PublishCoreAsync(
        GatewayPreparedApplication application,
        string? namespaceId,
        string? targetNodeId,
        TimeSpan acknowledgementTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (acknowledgementTimeout <= TimeSpan.Zero || acknowledgementTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(acknowledgementTimeout));

        Attempt? duplicate;
        Attempt? attempt = null;
        GatewayPublicationOutcome? immediate = null;
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var key = AttemptKey.From(application.Identity);
            if (_attempts.TryGetValue(key, out duplicate))
            {
                if (duplicate.PreparedApplication.Identity.ContentHash != application.Identity.ContentHash ||
                    duplicate.PreparedApplication.SymbolicPlanIdentity != application.SymbolicPlanIdentity ||
                    duplicate.PreparedApplication.NativeGraphIdentity != application.NativeGraphIdentity)
                    immediate = Immediate(GatewayPublicationState.IdentityConflict, application, "candidate.identity-conflict", "The authority key was reused with different candidate or runtime-plan content.");
            }
            else if (_authorityHeads.TryGetValue(application.Identity.AuthorityId, out var head))
            {
                if (!StringComparer.Ordinal.Equals(head.Epoch, key.Epoch))
                    immediate = Immediate(GatewayPublicationState.IdentityConflict, application, "candidate.epoch-conflict", "Authority epoch changes require an explicit reset operation.");
                else if (key.Version < head.Version)
                    immediate = Immediate(GatewayPublicationState.Stale, application, "candidate.stale", "A newer authority version is already admitted.");
                else if (!EnsureAttemptCapacity())
                    immediate = CapacityExceeded(application);
                else if (!_nativeRevisions.Add(application.NativeRevisionId))
                    immediate = Immediate(GatewayPublicationState.IdentityConflict, application, "publication.revision-reused", "Native revision correlation must be unique.");
                else
                    attempt = Admit(application, key);
            }
            else if (_authorityHeads.Count >= MaximumRememberedAttempts || !EnsureAttemptCapacity())
            {
                immediate = CapacityExceeded(application);
            }
            else if (!_nativeRevisions.Add(application.NativeRevisionId))
            {
                immediate = Immediate(GatewayPublicationState.IdentityConflict, application, "publication.revision-reused", "Native revision correlation must be unique.");
            }
            else
            {
                attempt = Admit(application, key);
            }
        }

        if (immediate is not null)
        {
            PublishObservation(immediate);
            return Task.FromResult(immediate);
        }
        if (attempt is null) return DuplicateAsync(application.Identity, duplicate!);
        if (namespaceId is not null && (!_runtimeObserver?.Register(application, namespaceId, targetNodeId!) ?? true))
        {
            GatewayPublicationOutcome rejected = Immediate(GatewayPublicationState.RejectedBeforePublish, application,
                "publication.applied-observer-registration-failed", "The applied-runtime observation envelope could not be registered.");
            Complete(attempt, rejected);
            return attempt.Completion.Task;
        }
        attempt.RequiresAppliedRuntime = namespaceId is not null;
        _ = RunAttemptAsync(attempt, acknowledgementTimeout, cancellationToken);
        return attempt.Completion.Task;
    }

    private Attempt Admit(GatewayPreparedApplication application, AttemptKey key)
    {
        var attempt = new Attempt(application);
        _attempts.Add(key, attempt);
        _authorityHeads[application.Identity.AuthorityId] = key;
        _attemptOrder.Enqueue(key);
        return attempt;
    }

    private async Task RunAttemptAsync(Attempt attempt, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var acquired = false;
        var applied = false;
        var applicationCompleted = false;
        OwnedProxyConfig? snapshot = null;
        try
        {
            try
            {
                using var preExchange = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
                await _publicationLease.WaitAsync(preExchange.Token).ConfigureAwait(false);
                acquired = true;
            }
            catch (OperationCanceledException)
            {
                Complete(attempt, Immediate(GatewayPublicationState.CanceledBeforePublish, attempt.PreparedApplication, "publication.canceled-before-publish", "Publication was canceled before entering the native boundary."));
                return;
            }

            lock (_stateLock)
            {
                var key = AttemptKey.From(attempt.PreparedApplication.Identity);
                if (!_authorityHeads.TryGetValue(attempt.PreparedApplication.Identity.AuthorityId, out var head) || head != key)
                {
                    Complete(attempt, Immediate(GatewayPublicationState.Superseded, attempt.PreparedApplication, "candidate.superseded", "A newer admitted candidate displaced this attempt."));
                    return;
                }
            }

            if (cancellationToken.IsCancellationRequested || _disposed)
            {
                Complete(attempt, Immediate(GatewayPublicationState.CanceledBeforePublish, attempt.PreparedApplication, "publication.canceled-before-publish", "Publication was canceled before entering the native boundary."));
                return;
            }

            if (!_destinationResolver.RegisterForExchange(attempt.PreparedApplication))
            {
                Complete(attempt, Immediate(GatewayPublicationState.RejectedBeforePublish, attempt.PreparedApplication, "publication.discovery-registration-failed", "Prepared discovery results could not be registered for exchange."));
                return;
            }
            snapshot = _provider.Prepare(attempt.PreparedApplication);
            var acknowledgement = _listener.Register(snapshot, attempt.RequiresAppliedRuntime);
            lock (_lifecycleLock)
            {
                if (cancellationToken.IsCancellationRequested || _disposed)
                {
                    Complete(attempt, Immediate(GatewayPublicationState.CanceledBeforePublish, attempt.PreparedApplication, "publication.canceled-before-publish", "Publication was canceled immediately before native exchange."));
                    return;
                }

                if (!_destinationResolver.SourcesUnchanged(attempt.PreparedApplication))
                {
                    Complete(attempt, Immediate(GatewayPublicationState.RejectedBeforePublish, attempt.PreparedApplication, "publication.discovery-invalidated", "Prepared discovery changed before native exchange."));
                    return;
                }
                attempt.Boundary = PublicationBoundary.ExchangeStarted;
                try
                {
                    _provider.Install(snapshot);
                }
                catch
                {
                    Complete(attempt, Indeterminate(attempt.PreparedApplication, "publication.notification-failed", "Native state exchanged, but change notification failed."));
                    return;
                }
            }

            NativeAcknowledgement observed;
            try
            {
                observed = await acknowledgement.WaitAsync(timeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                Complete(attempt, Indeterminate(attempt.PreparedApplication, "publication.timeout", "Exact native acknowledgement was not observed before the deadline."));
                return;
            }

            if (observed.Kind != NativeAcknowledgementKind.Applied)
            {
                Complete(attempt, Indeterminate(attempt.PreparedApplication, observed.Code, "YARP did not acknowledge successful application of the exact native snapshot."));
                return;
            }

            var active = new ActivePublicationIdentity(
                attempt.PreparedApplication.Identity,
                attempt.PreparedApplication.ApplicationId,
                attempt.PreparedApplication.SymbolicPlanIdentity,
                attempt.PreparedApplication.NativeRevisionId,
                DateTimeOffset.UtcNow);
            lock (_stateLock)
            {
                _lastKnownGood = active;
                _active = active;
                _activeUpstreams = GetActiveUpstreams(attempt.PreparedApplication);
            }
            applied = true;
            _destinationResolver.CompleteApplication(attempt.PreparedApplication, applied: true);
            applicationCompleted = true;
            Complete(attempt, new GatewayPublicationOutcome(
                GatewayPublicationState.ActiveAcknowledged,
                attempt.PreparedApplication.Identity,
                active,
                active,
                attempt.PreparedApplication.NativeRevisionId,
                []));
        }
        catch (Exception)
        {
            Complete(attempt, attempt.Boundary == PublicationBoundary.ExchangeStarted
                ? Indeterminate(attempt.PreparedApplication, "publication.correlation-lost", "Publication correlation was unexpectedly interrupted.")
                : cancellationToken.IsCancellationRequested || _disposed
                    ? Immediate(GatewayPublicationState.CanceledBeforePublish, attempt.PreparedApplication, "publication.canceled-before-publish", "Publication stopped before native exchange.")
                    : Immediate(GatewayPublicationState.RejectedBeforePublish, attempt.PreparedApplication, "publication.preparation-failed", "Publication failed before native exchange."));
        }
        finally
        {
            if (snapshot is not null) _listener.Unregister(snapshot);
            if (!applicationCompleted) _destinationResolver.CompleteApplication(attempt.PreparedApplication, applied);
            if (acquired) _publicationLease.Release();
        }
    }

    private async Task<GatewayPublicationOutcome> DuplicateAsync(PublicationCandidateIdentity attempted, Attempt original)
    {
        var outcome = await original.Completion.Task.ConfigureAwait(false);
        var duplicate = outcome with
        {
            State = GatewayPublicationState.Duplicate,
            Attempted = attempted,
            Diagnostics = [new GatewayPublicationDiagnostic("candidate.duplicate", "The same authority key and content were already processed.")]
        };
        PublishObservation(duplicate);
        return duplicate;
    }

    private GatewayPublicationOutcome Immediate(
        GatewayPublicationState state,
        GatewayPreparedApplication application,
        string code,
        string message)
    {
        lock (_stateLock)
            return new GatewayPublicationOutcome(state, application.Identity, null, _lastKnownGood, application.NativeRevisionId, [new GatewayPublicationDiagnostic(code, message)]);
    }

    private GatewayPublicationOutcome Indeterminate(GatewayPreparedApplication application, string code, string message)
    {
        lock (_stateLock)
        {
            _active = null;
            _activeUpstreams = [];
            return new GatewayPublicationOutcome(GatewayPublicationState.PublicationIndeterminate, application.Identity, null, _lastKnownGood, application.NativeRevisionId, [new GatewayPublicationDiagnostic(code, message)]);
        }
    }

    private void Complete(Attempt attempt, GatewayPublicationOutcome outcome)
    {
        PublishObservation(outcome);
        attempt.Completion.TrySetResult(outcome);
        lock (_stateLock) PruneHistory();
    }

    public GatewayPublicationObservation GetCurrent()
    {
        lock (_stateLock) return _observation;
    }

    public IChangeToken GetChangeToken()
    {
        lock (_stateLock) return new CancellationChangeToken(_observationChanged.Token);
    }

    private void PublishObservation(GatewayPublicationOutcome outcome)
    {
        CancellationTokenSource? previous = null;
        lock (_stateLock)
        {
            _observation = new GatewayPublicationObservation(
                _observation.Sequence == ulong.MaxValue ? ulong.MaxValue : _observation.Sequence + 1,
                DateTimeOffset.UtcNow,
                outcome,
                _active,
                _lastKnownGood,
                _activeUpstreams);
            if (!_disposed)
            {
                previous = _observationChanged;
                _observationChanged = new();
            }
        }
        if (previous is not null) CancelAndDispose(previous);
    }

    private static ImmutableArray<GatewayPublishedUpstream> GetActiveUpstreams(GatewayPreparedApplication application)
    {
        return application.Clusters
            .OrderBy(static cluster => cluster.ClusterId, StringComparer.Ordinal)
            .Select(static cluster => new GatewayPublishedUpstream(
                cluster.ClusterId,
                cluster.HealthCheck?.AvailableDestinationsPolicy ?? "HealthyOrPanic"))
            .ToImmutableArray();
    }

    private void PruneHistory()
    {
        var count = _attemptOrder.Count;
        for (var index = 0; index < count; index++)
        {
            var key = _attemptOrder.Dequeue();
            var isHead = _authorityHeads.TryGetValue(key.Authority, out var head) && head == key;
            if (!isHead && _attempts.TryGetValue(key, out var attempt) && attempt.Completion.Task.IsCompleted)
            {
                _attempts.Remove(key);
                _nativeRevisions.Remove(attempt.PreparedApplication.NativeRevisionId);
            }
            else if (_attempts.ContainsKey(key))
                _attemptOrder.Enqueue(key);
        }
    }

    private bool EnsureAttemptCapacity()
    {
        PruneHistory();
        return _attempts.Count < MaximumRememberedAttempts;
    }

    private GatewayPublicationOutcome CapacityExceeded(GatewayPreparedApplication application) =>
        Immediate(GatewayPublicationState.RejectedBeforePublish, application, "publication.admission-capacity-exceeded", "The bounded publication identity history is full; restart or explicit future authority retirement is required before admitting another candidate.");

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
        }
        _listener.Dispose();
        if (_ownsDestinationResolver) _destinationResolver.Dispose();
        CancellationTokenSource changed;
        lock (_stateLock)
        {
            changed = _observationChanged;
        }
        CancelAndDispose(changed);
    }

    private static void CancelAndDispose(CancellationTokenSource source)
    {
        try { source.Cancel(); }
        catch (AggregateException) { }
        catch (ObjectDisposedException) { }
        source.Dispose();
    }

    private readonly record struct AttemptKey(string Authority, string Epoch, ulong Version)
    {
        internal static AttemptKey From(PublicationCandidateIdentity identity) => new(identity.AuthorityId, identity.AuthorityEpoch, identity.AuthorityVersion);
    }

    private sealed class Attempt(GatewayPreparedApplication application)
    {
        internal GatewayPreparedApplication PreparedApplication { get; } = application;
        internal PublicationBoundary Boundary { get; set; }
        internal bool RequiresAppliedRuntime { get; set; }
        internal TaskCompletionSource<GatewayPublicationOutcome> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private enum PublicationBoundary : byte
    {
        PreExchange,
        ExchangeStarted
    }
}

internal sealed class PassthroughConfigValidator : IConfigValidator
{
    public ValueTask<IList<Exception>> ValidateClusterAsync(ClusterConfig cluster) => ValueTask.FromResult<IList<Exception>>([]);
    public ValueTask<IList<Exception>> ValidateRouteAsync(RouteConfig route) => ValueTask.FromResult<IList<Exception>>([]);
}
