using System.Collections.Immutable;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>Identifies one independently owned source in the agent capability catalog.</summary>
/// <param name="Value">The stable source identifier.</param>
public readonly record struct CapabilitySourceId(string Value)
{
    /// <summary>Creates a validated capability-source identifier.</summary>
    /// <param name="value">The non-empty stable identifier.</param>
    /// <returns>The validated identifier.</returns>
    public static CapabilitySourceId Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new(value);
    }

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identifies an immutable revision published by a capability source.</summary>
/// <param name="Value">The non-negative, source-local revision number.</param>
public readonly record struct CapabilitySourceRevision(long Value)
{
    /// <summary>Creates a validated source revision.</summary>
    /// <param name="value">The non-negative revision number.</param>
    /// <returns>The validated revision.</returns>
    public static CapabilitySourceRevision Create(long value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        return new(value);
    }
}

/// <summary>Controls how source failure affects initial agent construction.</summary>
public enum CapabilitySourceInitialLoadPolicy
{
    /// <summary>Fail agent construction when the source cannot load.</summary>
    Required,
    /// <summary>Construct the agent without the unavailable source.</summary>
    Optional,
    /// <summary>Publish an empty source revision and load at the first turn boundary.</summary>
    Deferred
}

/// <summary>Controls how source failure contributes to a refresh candidate.</summary>
public enum CapabilitySourceRefreshFailurePolicy
{
    /// <summary>Reject the complete candidate and retain the published snapshot.</summary>
    RejectCandidate,
    /// <summary>Reuse the exact last-known-good revision owner.</summary>
    RetainLastKnownGood,
    /// <summary>Remove the failed source from the complete candidate.</summary>
    RemoveSource
}

/// <summary>Describes one protocol-neutral capability in an immutable catalog revision.</summary>
public sealed record CapabilityDescriptor
{
    /// <summary>Gets the stable capability identifier.</summary>
    public required CapabilityId Id { get; init; }
    /// <summary>Gets the source that owns the capability.</summary>
    public required CapabilitySourceId SourceId { get; init; }
    /// <summary>Gets the exact owning source revision.</summary>
    public required CapabilitySourceRevision SourceRevision { get; init; }
    /// <summary>Gets the model-facing function name.</summary>
    public required string ModelName { get; init; }
    /// <summary>Gets the protocol-neutral capability classification.</summary>
    public required HPDCapabilityKind Kind { get; init; }
    /// <summary>Gets bounded, non-secret source metadata.</summary>
    public ImmutableDictionary<string, string> Metadata { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>Reports the outcome of one complete capability-catalog refresh.</summary>
/// <param name="Published">Whether the candidate became authoritative.</param>
/// <param name="Epoch">The authoritative epoch after the attempt.</param>
/// <param name="Error">A bounded failure description when publication was rejected.</param>
public sealed record AgentCapabilityRefreshResult(bool Published, long Epoch, string? Error = null);

/// <summary>Contains the immutable functions and descriptors owned by one source revision.</summary>
public sealed record CapabilitySourceSnapshot
{
    /// <summary>Gets the immutable model-facing functions in this revision.</summary>
    public required ImmutableArray<AIFunction> Functions { get; init; }
    /// <summary>Gets descriptors indexed by stable capability identifier.</summary>
    public required ImmutableDictionary<CapabilityId, CapabilityDescriptor> Descriptors { get; init; }
    /// <summary>Gets bounded, protocol-neutral metadata describing the complete source revision.</summary>
    public ImmutableDictionary<string, string> Metadata { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>Owns all resources required by one immutable source revision.</summary>
public interface ICapabilitySourceRevisionOwner : IAsyncDisposable
{
    /// <summary>Gets the stable owning source identifier.</summary>
    CapabilitySourceId SourceId { get; }
    /// <summary>Gets the immutable source-local revision.</summary>
    CapabilitySourceRevision Revision { get; }
    /// <summary>Gets the immutable capability payload.</summary>
    CapabilitySourceSnapshot Snapshot { get; }
}

/// <summary>Restores observation of a durable provider operation owned by a capability revision.</summary>
internal interface IAgentOperationRecoveryProvider
{
    /// <summary>Gets whether this provider understands the versioned recovery reference.</summary>
    bool CanRecover(AgentOperationRecoveryReference recoveryReference);

    /// <summary>Attaches live controls and observation, retaining the supplied revision lease on success.</summary>
    ValueTask<bool> TryRecoverAsync(
        AgentOperation operation,
        AgentCapabilityLease revisionLease,
        CancellationToken cancellationToken);
}

/// <summary>Transfers a successfully loaded revision owner to the catalog candidate.</summary>
public sealed record CapabilitySourceLoadResult(ICapabilitySourceRevisionOwner Owner);
/// <summary>Supplies source-independent facts for one candidate load.</summary>
public sealed record CapabilityLoadContext(long CandidateEpoch, IServiceProvider? Services);
/// <summary>Signals that a source partition is eligible for refresh.</summary>
public sealed record CapabilityInvalidation(CapabilitySourceId SourceId, string Reason);

/// <summary>Loads and watches one independently owned capability source.</summary>
public interface IAgentCapabilitySource : IAsyncDisposable
{
    /// <summary>Gets the stable source identifier.</summary>
    CapabilitySourceId Id { get; }
    /// <summary>Loads one complete immutable source revision.</summary>
    ValueTask<CapabilitySourceLoadResult> LoadAsync(
        CapabilityLoadContext context,
        CancellationToken cancellationToken);
    /// <summary>Watches bounded hints that this source should be refreshed.</summary>
    IAsyncEnumerable<CapabilityInvalidation> WatchAsync(CancellationToken cancellationToken);
}

/// <summary>Creates an optional-package capability source without leaking protocol types into core.</summary>
public interface IAgentCapabilitySourceFactory
{
    /// <summary>Gets the stable source identifier created by this factory.</summary>
    CapabilitySourceId Id { get; }
    /// <summary>Creates the asynchronously owned source.</summary>
    ValueTask<IAgentCapabilitySource> CreateAsync(
        IServiceProvider? services,
        CancellationToken cancellationToken);
}

/// <summary>Registers a source factory and its explicit failure policies.</summary>
public sealed record AgentCapabilitySourceRegistration(
    IAgentCapabilitySourceFactory Factory,
    CapabilitySourceInitialLoadPolicy InitialLoadPolicy,
    CapabilitySourceRefreshFailurePolicy RefreshFailurePolicy)
{
    /// <summary>Gets the maximum duration allowed for source creation or one revision load.</summary>
    public TimeSpan LoadTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>Contains one complete, immutable agent capability epoch.</summary>
internal sealed record AgentCapabilitySnapshot
{
    public required long Epoch { get; init; }
    public required ImmutableArray<AIFunction> Functions { get; init; }
    public required CapabilityGraph Graph { get; init; }
    public required ImmutableDictionary<CapabilityId, CapabilityDescriptor> Descriptors { get; init; }
    public required ImmutableDictionary<CapabilitySourceId, ICapabilitySourceRevisionOwner> Revisions { get; init; }
}

/// <summary>Pins a complete capability epoch and its revision-owned resources.</summary>
internal sealed class AgentCapabilityLease : IAsyncDisposable
{
    private AgentCapabilityCatalog.PublishedSnapshot? _owner;

    internal AgentCapabilityLease(AgentCapabilityCatalog.PublishedSnapshot owner)
    {
        _owner = owner;
        Snapshot = owner.Snapshot;
    }

    /// <summary>Gets the immutable snapshot pinned by this lease.</summary>
    public AgentCapabilitySnapshot Snapshot { get; }

    /// <summary>Releases this turn's reference to the published snapshot.</summary>
    public ValueTask DisposeAsync() =>
        Interlocked.Exchange(ref _owner, null)?.ReleaseAsync() ?? ValueTask.CompletedTask;
}

/// <summary>
/// Owns immutable, complete capability snapshots. Publication is atomic and source
/// resources retire only after the final snapshot/turn lease releases them.
/// </summary>
internal sealed class AgentCapabilityCatalog : IAsyncDisposable
{
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly SemaphoreSlim _deferredLock = new(1, 1);
    private readonly object _ownerGate = new();
    private readonly Dictionary<ICapabilitySourceRevisionOwner, RevisionOwnerReference> _owners =
        new(ReferenceEqualityComparer.Instance);
    private readonly Func<long, CancellationToken, ValueTask<IReadOnlyList<ICapabilitySourceRevisionOwner>>>? _rebuild;
    private PublishedSnapshot _current;
    private int _deferred;
    private int _disposed;

    public AgentCapabilityCatalog(
        long initialEpoch,
        IEnumerable<ICapabilitySourceRevisionOwner> revisions,
        Func<long, CancellationToken, ValueTask<IReadOnlyList<ICapabilitySourceRevisionOwner>>>? rebuild = null,
        bool hasDeferredSources = false)
    {
        _rebuild = rebuild;
        _current = BuildPublished(initialEpoch, revisions);
        _deferred = hasDeferredSources ? 1 : 0;
    }

    public AgentCapabilityLease Acquire()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        while (true)
        {
            var current = Volatile.Read(ref _current);
            if (current.TryAcquire())
                return new(current);
        }
    }

    /// <summary>Gets the currently published immutable epoch.</summary>
    internal long CurrentEpoch => Volatile.Read(ref _current).Snapshot.Epoch;

    /// <summary>Loads deferred sources once at the first safe asynchronous boundary.</summary>
    internal async ValueTask EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _deferred) == 0) return;
        await _deferredLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _deferred) == 0) return;
            var result = await RefreshAsync("deferred-initial-load", cancellationToken).ConfigureAwait(false);
            if (!result.Published)
                throw new InvalidOperationException($"Deferred capability loading failed: {result.Error}");
            Interlocked.Exchange(ref _deferred, 0);
        }
        finally
        {
            _deferredLock.Release();
        }
    }

    /// <summary>Gets the exact currently published owner for a source, when present.</summary>
    internal bool TryGetCurrentRevision(
        CapabilitySourceId sourceId,
        out ICapabilitySourceRevisionOwner? owner) =>
        Volatile.Read(ref _current).Snapshot.Revisions.TryGetValue(sourceId, out owner);

    /// <summary>Attempts to restore each detached, recoverable provider operation.</summary>
    internal async ValueTask ReconcileAsync(
        IEnumerable<AgentOperation> operations,
        CancellationToken cancellationToken)
    {
        foreach (var operation in operations)
        {
            var snapshot = operation.Snapshot;
            if (snapshot.ObservationStatus != AgentOperationObservationStatus.Detached || snapshot.Recovery is null)
                continue;

            var candidateLease = Acquire();
            var leaseTransferred = false;
            var provider = candidateLease.Snapshot.Revisions.Values
                .OfType<IAgentOperationRecoveryProvider>()
                .FirstOrDefault(candidate => candidate.CanRecover(snapshot.Recovery));
            if (provider is null)
            {
                await candidateLease.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            try
            {
                await TransitionLatestAsync(operation, new AgentOperationTransition
                {
                    ObservationStatus = AgentOperationObservationStatus.Reconciling,
                    ProviderDeduplicationKey = $"reconciling:{snapshot.OperationId}:{snapshot.Version}"
                }, cancellationToken).ConfigureAwait(false);
                if (await provider.TryRecoverAsync(operation, candidateLease, cancellationToken).ConfigureAwait(false))
                {
                    leaseTransferred = true;
                    await TransitionLatestAsync(operation, new AgentOperationTransition
                    {
                        ObservationStatus = AgentOperationObservationStatus.Attached,
                        ProviderDeduplicationKey = $"reattached:{snapshot.OperationId}:{snapshot.Version}"
                    }, CancellationToken.None).ConfigureAwait(false);
                    continue;
                }
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                // Preserve the protected recovery reference for a later materialization attempt.
            }

            finally
            {
                if (!leaseTransferred)
                    await candidateLease.DisposeAsync().ConfigureAwait(false);
            }

            if (operation.Snapshot.ObservationStatus == AgentOperationObservationStatus.Reconciling)
            {
                await TransitionLatestAsync(operation, new AgentOperationTransition
                {
                    ObservationStatus = AgentOperationObservationStatus.Detached,
                    ProviderDeduplicationKey = $"reconcile-unavailable:{snapshot.OperationId}:{snapshot.Version}"
                }, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private static async ValueTask TransitionLatestAsync(
        AgentOperation operation,
        AgentOperationTransition transition,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                await operation.TransitionAsync(transition, operation.Snapshot.Version, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (AgentOperationVersionConflictException)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    public async ValueTask PublishAsync(
        long epoch,
        IEnumerable<ICapabilitySourceRevisionOwner> revisions,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(revisions);

        var materialized = revisions.ToArray();
        PublishedSnapshot? candidate = null;
        await _publishLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var current = Volatile.Read(ref _current);
            if (epoch <= current.Snapshot.Epoch)
                throw new InvalidOperationException(
                    $"Capability epoch {epoch} must be greater than the current epoch {current.Snapshot.Epoch}.");

            candidate = BuildPublished(epoch, materialized);
            var retired = Interlocked.Exchange(ref _current, candidate);
            candidate = null;
            await retired.ReleaseAsync().ConfigureAwait(false);
        }
        catch
        {
            if (candidate is not null)
                await candidate.ReleaseAsync().ConfigureAwait(false);
            else
                await DisposeRejectedAsync(materialized).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _publishLock.Release();
        }
    }

    /// <summary>Builds, validates, and atomically publishes one complete replacement candidate.</summary>
    internal async ValueTask<AgentCapabilityRefreshResult> RefreshAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_rebuild is null)
            return new(false, Volatile.Read(ref _current).Snapshot.Epoch, "No capability rebuild was configured.");

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var epoch = Volatile.Read(ref _current).Snapshot.Epoch + 1;
            var revisions = await _rebuild(epoch, cancellationToken).ConfigureAwait(false);
            await PublishAsync(epoch, revisions, cancellationToken).ConfigureAwait(false);
            return new(true, epoch);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var error = exception.Message.Replace('\r', ' ').Replace('\n', ' ');
            return new(false, Volatile.Read(ref _current).Snapshot.Epoch,
                error.Length <= 512 ? error : error[..512]);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Releases the catalog while abandoning resources still pinned by leaked leases.</summary>
    public async ValueTask DisposeAsync() =>
        _ = await ShutdownAsync(AgentLeaseLeakPolicy.ReportAndAbandonResources).ConfigureAwait(false);

    /// <summary>Releases the catalog and applies the configured leaked-lease escalation policy.</summary>
    /// <returns>The number of revision owners that remained pinned after releasing the catalog snapshot.</returns>
    internal async ValueTask<int> ShutdownAsync(AgentLeaseLeakPolicy leakPolicy)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return 0;

        await _refreshLock.WaitAsync().ConfigureAwait(false);
        await _publishLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await Volatile.Read(ref _current).ReleaseAsync().ConfigureAwait(false);
            RevisionOwnerReference[] leaked;
            lock (_ownerGate)
                leaked = _owners.Values.ToArray();
            if (leakPolicy == AgentLeaseLeakPolicy.ReportAndForceDispose)
            {
                foreach (var owner in leaked)
                    await owner.ForceDisposeAsync().ConfigureAwait(false);
            }
            return leaked.Length;
        }
        finally
        {
            _publishLock.Release();
            _refreshLock.Release();
            _publishLock.Dispose();
            _refreshLock.Dispose();
            _deferredLock.Dispose();
        }
    }

    private PublishedSnapshot BuildPublished(
        long epoch,
        IEnumerable<ICapabilitySourceRevisionOwner> revisions)
    {
        if (epoch < 0)
            throw new ArgumentOutOfRangeException(nameof(epoch));

        var bySource = ImmutableDictionary.CreateBuilder<CapabilitySourceId, ICapabilitySourceRevisionOwner>();
        var functions = ImmutableArray.CreateBuilder<AIFunction>();
        var descriptors = ImmutableDictionary.CreateBuilder<CapabilityId, CapabilityDescriptor>();
        foreach (var owner in revisions.OrderBy(static owner => owner.SourceId.Value, StringComparer.Ordinal))
        {
            ArgumentNullException.ThrowIfNull(owner);
            if (!bySource.TryAdd(owner.SourceId, owner))
                throw new InvalidOperationException($"Duplicate capability source '{owner.SourceId}'.");

            foreach (var function in owner.Snapshot.Functions.OrderBy(static function => function.Name, StringComparer.Ordinal))
                functions.Add(function);

            foreach (var pair in owner.Snapshot.Descriptors.OrderBy(static pair => pair.Key.Value, StringComparer.Ordinal))
            {
                if (pair.Value.SourceId != owner.SourceId || pair.Value.SourceRevision != owner.Revision)
                    throw new InvalidOperationException($"Capability '{pair.Key}' has mismatched source ownership.");
                if (!descriptors.TryAdd(pair.Key, pair.Value))
                    throw new InvalidOperationException($"Duplicate capability ID '{pair.Key}'.");
            }
        }

        var functionArray = functions.ToImmutable();
        if (functionArray.Length != descriptors.Count)
            throw new InvalidOperationException("Every capability function must have exactly one descriptor.");

        var graph = CapabilityGraph.CreateFromFunctions(functionArray);
        foreach (var id in graph.Nodes.Keys)
        {
            if (!descriptors.ContainsKey(id))
                throw new InvalidOperationException($"Capability '{id}' has no descriptor.");
        }

        // Acquire ownership only after complete graph validation. This keeps rejection
        // asynchronous and prevents a failed candidate from touching reused owners.
        var ownerReferences = bySource.Values.Select(AcquireOwner).ToArray();
        return new PublishedSnapshot(new AgentCapabilitySnapshot
        {
            Epoch = epoch,
            Functions = functionArray,
            Graph = graph,
            Descriptors = descriptors.ToImmutable(),
            Revisions = bySource.ToImmutable()
        }, ownerReferences);
    }

    private RevisionOwnerReference AcquireOwner(ICapabilitySourceRevisionOwner owner)
    {
        lock (_ownerGate)
        {
            if (_owners.TryGetValue(owner, out var existing) && existing.TryAcquire())
                return existing;

            var created = new RevisionOwnerReference(owner, RemoveOwner);
            _owners[owner] = created;
            return created;
        }
    }

    private async ValueTask DisposeRejectedAsync(
        IEnumerable<ICapabilitySourceRevisionOwner> candidateOwners)
    {
        foreach (var owner in candidateOwners.Distinct(RevisionOwnerIdentityComparer.Instance))
        {
            var published = false;
            lock (_ownerGate)
                published = _owners.ContainsKey(owner);
            if (!published)
                await owner.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class RevisionOwnerIdentityComparer : IEqualityComparer<ICapabilitySourceRevisionOwner>
    {
        internal static RevisionOwnerIdentityComparer Instance { get; } = new();

        public bool Equals(ICapabilitySourceRevisionOwner? x, ICapabilitySourceRevisionOwner? y) =>
            ReferenceEquals(x, y);

        public int GetHashCode(ICapabilitySourceRevisionOwner obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private void RemoveOwner(RevisionOwnerReference reference)
    {
        lock (_ownerGate)
        {
            if (_owners.TryGetValue(reference.Owner, out var current) && ReferenceEquals(current, reference))
                _owners.Remove(reference.Owner);
        }
    }

    internal sealed class PublishedSnapshot
    {
        private int _references = 1;
        private int _resourcesDisposed;

        private readonly IReadOnlyList<RevisionOwnerReference> _owners;

        internal PublishedSnapshot(
            AgentCapabilitySnapshot snapshot,
            IReadOnlyList<RevisionOwnerReference> owners)
        {
            Snapshot = snapshot;
            _owners = owners;
        }
        internal AgentCapabilitySnapshot Snapshot { get; }

        internal bool TryAcquire()
        {
            while (true)
            {
                var observed = Volatile.Read(ref _references);
                if (observed == 0)
                    return false;
                if (Interlocked.CompareExchange(ref _references, observed + 1, observed) == observed)
                    return true;
            }
        }

        internal async ValueTask ReleaseAsync()
        {
            var remaining = Interlocked.Decrement(ref _references);
            if (remaining < 0)
                throw new InvalidOperationException("Capability snapshot lease was released more than once.");
            if (remaining != 0 || Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
                return;

            foreach (var owner in _owners)
                await owner.ReleaseAsync().ConfigureAwait(false);
        }
    }

    internal sealed class RevisionOwnerReference
    {
        private readonly Action<RevisionOwnerReference> _onDisposed;
        private int _references = 1;
        private int _disposed;

        internal RevisionOwnerReference(
            ICapabilitySourceRevisionOwner owner,
            Action<RevisionOwnerReference> onDisposed)
        {
            Owner = owner;
            _onDisposed = onDisposed;
        }

        internal ICapabilitySourceRevisionOwner Owner { get; }

        internal bool TryAcquire()
        {
            while (true)
            {
                var observed = Volatile.Read(ref _references);
                if (observed == 0)
                    return false;
                if (Interlocked.CompareExchange(ref _references, observed + 1, observed) == observed)
                    return true;
            }
        }

        internal async ValueTask ReleaseAsync()
        {
            var remaining = Interlocked.Decrement(ref _references);
            if (remaining < 0)
                throw new InvalidOperationException("Capability revision owner was released more than once.");
            if (remaining != 0 || Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _onDisposed(this);
            await Owner.DisposeAsync().ConfigureAwait(false);
        }

        internal async ValueTask ForceDisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _onDisposed(this);
            await Owner.DisposeAsync().ConfigureAwait(false);
        }
    }
}
