using System.Collections.Immutable;
using Yarp.ReverseProxy.Configuration;

namespace HPD.Gateway.Yarp;

internal sealed class GatewayYarpPublisher : IDisposable
{
    private const int MaximumRememberedAttempts = 4_096;
    private readonly HpdProxyConfigProvider _provider;
    private readonly HpdConfigChangeListener _listener;
    private readonly SemaphoreSlim _publicationLease = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _stateLock = new();
    private readonly object _lifecycleLock = new();
    private readonly Dictionary<AttemptKey, Attempt> _attempts = [];
    private readonly Dictionary<string, AttemptKey> _authorityHeads = new(StringComparer.Ordinal);
    private readonly Queue<AttemptKey> _attemptOrder = [];
    private readonly HashSet<string> _nativeRevisions = new(StringComparer.Ordinal);
    private ActivePublicationIdentity? _lastKnownGood;
    private volatile bool _disposed;

    internal GatewayYarpPublisher(
        HpdProxyConfigProvider provider,
        HpdConfigChangeListener listener,
        IEnumerable<IProxyConfigProvider> configuredProviders)
    {
        _provider = provider;
        _listener = listener;
        var providers = configuredProviders.ToArray();
        if (providers.Length != 1 || !ReferenceEquals(providers[0], provider))
            throw new InvalidOperationException("Managed publication requires exactly one HPD-owned IProxyConfigProvider.");
    }

    internal Task<GatewayPublicationOutcome> PublishAsync(
        NativePublicationBundle bundle,
        TimeSpan acknowledgementTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (acknowledgementTimeout <= TimeSpan.Zero || acknowledgementTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(acknowledgementTimeout));

        Attempt? duplicate;
        Attempt? attempt = null;
        GatewayPublicationOutcome? immediate = null;
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var key = AttemptKey.From(bundle.Identity);
            if (_attempts.TryGetValue(key, out duplicate))
            {
                if (duplicate.Bundle.Identity.ContentHash != bundle.Identity.ContentHash)
                    immediate = Immediate(GatewayPublicationState.IdentityConflict, bundle, "candidate.identity-conflict", "The authority key was reused with different content.");
            }
            else if (_authorityHeads.TryGetValue(bundle.Identity.AuthorityId, out var head))
            {
                if (!StringComparer.Ordinal.Equals(head.Epoch, key.Epoch))
                    immediate = Immediate(GatewayPublicationState.IdentityConflict, bundle, "candidate.epoch-conflict", "Authority epoch changes require an explicit reset operation.");
                else if (key.Version < head.Version)
                    immediate = Immediate(GatewayPublicationState.Stale, bundle, "candidate.stale", "A newer authority version is already admitted.");
                else if (!EnsureAttemptCapacity())
                    immediate = CapacityExceeded(bundle);
                else if (!_nativeRevisions.Add(bundle.NativeRevisionId))
                    immediate = Immediate(GatewayPublicationState.IdentityConflict, bundle, "publication.revision-reused", "Native revision correlation must be unique.");
                else
                    attempt = Admit(bundle, key);
            }
            else if (_authorityHeads.Count >= MaximumRememberedAttempts || !EnsureAttemptCapacity())
            {
                immediate = CapacityExceeded(bundle);
            }
            else if (!_nativeRevisions.Add(bundle.NativeRevisionId))
            {
                immediate = Immediate(GatewayPublicationState.IdentityConflict, bundle, "publication.revision-reused", "Native revision correlation must be unique.");
            }
            else
            {
                attempt = Admit(bundle, key);
            }
        }

        if (immediate is not null) return Task.FromResult(immediate);
        if (attempt is null) return DuplicateAsync(bundle.Identity, duplicate!);
        _ = RunAttemptAsync(attempt, acknowledgementTimeout, cancellationToken);
        return attempt.Completion.Task;
    }

    private Attempt Admit(NativePublicationBundle bundle, AttemptKey key)
    {
        var attempt = new Attempt(bundle);
        _attempts.Add(key, attempt);
        _authorityHeads[bundle.Identity.AuthorityId] = key;
        _attemptOrder.Enqueue(key);
        return attempt;
    }

    private async Task RunAttemptAsync(Attempt attempt, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var acquired = false;
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
                Complete(attempt, Immediate(GatewayPublicationState.CanceledBeforePublish, attempt.Bundle, "publication.canceled-before-publish", "Publication was canceled before entering the native boundary."));
                return;
            }

            lock (_stateLock)
            {
                var key = AttemptKey.From(attempt.Bundle.Identity);
                if (!_authorityHeads.TryGetValue(attempt.Bundle.Identity.AuthorityId, out var head) || head != key)
                {
                    Complete(attempt, Immediate(GatewayPublicationState.Superseded, attempt.Bundle, "candidate.superseded", "A newer admitted candidate displaced this attempt."));
                    return;
                }
            }

            if (cancellationToken.IsCancellationRequested || _disposed)
            {
                Complete(attempt, Immediate(GatewayPublicationState.CanceledBeforePublish, attempt.Bundle, "publication.canceled-before-publish", "Publication was canceled before entering the native boundary."));
                return;
            }

            snapshot = _provider.Prepare(attempt.Bundle);
            var acknowledgement = _listener.Register(snapshot);
            lock (_lifecycleLock)
            {
                if (cancellationToken.IsCancellationRequested || _disposed)
                {
                    Complete(attempt, Immediate(GatewayPublicationState.CanceledBeforePublish, attempt.Bundle, "publication.canceled-before-publish", "Publication was canceled immediately before native exchange."));
                    return;
                }

                attempt.Boundary = PublicationBoundary.ExchangeStarted;
                try
                {
                    _provider.Install(snapshot);
                }
                catch
                {
                    Complete(attempt, Indeterminate(attempt.Bundle, "publication.notification-failed", "Native state exchanged, but change notification failed."));
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
                Complete(attempt, Indeterminate(attempt.Bundle, "publication.timeout", "Exact native acknowledgement was not observed before the deadline."));
                return;
            }

            if (observed.Kind != NativeAcknowledgementKind.Applied)
            {
                Complete(attempt, Indeterminate(attempt.Bundle, observed.Code, "YARP did not acknowledge successful application of the exact native snapshot."));
                return;
            }

            var active = new ActivePublicationIdentity(attempt.Bundle.Identity, attempt.Bundle.NativeRevisionId, DateTimeOffset.UtcNow);
            lock (_stateLock) _lastKnownGood = active;
            Complete(attempt, new GatewayPublicationOutcome(
                GatewayPublicationState.ActiveAcknowledged,
                attempt.Bundle.Identity,
                active,
                active,
                attempt.Bundle.NativeRevisionId,
                []));
        }
        catch (Exception)
        {
            Complete(attempt, attempt.Boundary == PublicationBoundary.ExchangeStarted
                ? Indeterminate(attempt.Bundle, "publication.correlation-lost", "Publication correlation was unexpectedly interrupted.")
                : cancellationToken.IsCancellationRequested || _disposed
                    ? Immediate(GatewayPublicationState.CanceledBeforePublish, attempt.Bundle, "publication.canceled-before-publish", "Publication stopped before native exchange.")
                    : Immediate(GatewayPublicationState.RejectedBeforePublish, attempt.Bundle, "publication.preparation-failed", "Publication failed before native exchange."));
        }
        finally
        {
            if (snapshot is not null) _listener.Unregister(snapshot);
            if (acquired) _publicationLease.Release();
        }
    }

    private async Task<GatewayPublicationOutcome> DuplicateAsync(PublicationCandidateIdentity attempted, Attempt original)
    {
        var outcome = await original.Completion.Task.ConfigureAwait(false);
        return outcome with
        {
            State = GatewayPublicationState.Duplicate,
            Attempted = attempted,
            Diagnostics = [new GatewayPublicationDiagnostic("candidate.duplicate", "The same authority key and content were already processed.")]
        };
    }

    private GatewayPublicationOutcome Immediate(
        GatewayPublicationState state,
        NativePublicationBundle bundle,
        string code,
        string message)
    {
        lock (_stateLock)
            return new GatewayPublicationOutcome(state, bundle.Identity, null, _lastKnownGood, bundle.NativeRevisionId, [new GatewayPublicationDiagnostic(code, message)]);
    }

    private GatewayPublicationOutcome Indeterminate(NativePublicationBundle bundle, string code, string message)
    {
        lock (_stateLock)
            return new GatewayPublicationOutcome(GatewayPublicationState.PublicationIndeterminate, bundle.Identity, null, _lastKnownGood, bundle.NativeRevisionId, [new GatewayPublicationDiagnostic(code, message)]);
    }

    private void Complete(Attempt attempt, GatewayPublicationOutcome outcome)
    {
        attempt.Completion.TrySetResult(outcome);
        lock (_stateLock) PruneHistory();
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
                _nativeRevisions.Remove(attempt.Bundle.NativeRevisionId);
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

    private GatewayPublicationOutcome CapacityExceeded(NativePublicationBundle bundle) =>
        Immediate(GatewayPublicationState.RejectedBeforePublish, bundle, "publication.admission-capacity-exceeded", "The bounded publication identity history is full; restart or explicit future authority retirement is required before admitting another candidate.");

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
        }
        _listener.Dispose();
    }

    private readonly record struct AttemptKey(string Authority, string Epoch, ulong Version)
    {
        internal static AttemptKey From(PublicationCandidateIdentity identity) => new(identity.AuthorityId, identity.AuthorityEpoch, identity.AuthorityVersion);
    }

    private sealed class Attempt(NativePublicationBundle bundle)
    {
        internal NativePublicationBundle Bundle { get; } = bundle;
        internal PublicationBoundary Boundary { get; set; }
        internal TaskCompletionSource<GatewayPublicationOutcome> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private enum PublicationBoundary : byte
    {
        PreExchange,
        ExchangeStarted
    }
}
