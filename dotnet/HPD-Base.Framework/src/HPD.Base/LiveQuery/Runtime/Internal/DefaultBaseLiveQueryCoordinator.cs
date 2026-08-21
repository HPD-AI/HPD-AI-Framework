using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace HPD.Base;

internal interface IBaseLiveQueryState
{
    /// <summary>Gets the subscription ID.</summary>
    string SubscriptionId { get; }
    /// <summary>Executes the invalidate operation.</summary>
    void Invalidate(BaseDependencyInvalidation invalidation);
    /// <summary>Publishes one ordered subject-authority transition and schedules replacement.</summary>
    void InvalidateSubjectAuthority(BaseSubjectAuthorityPublicationFact publication, BaseDependencyInvalidation invalidation);
    /// <summary>Executes the fail operation.</summary>
    void Fail(string code, string safeMessage);
}

internal sealed class DefaultBaseLiveQueryCoordinator : IBaseLiveQueryCoordinator
{
    private readonly BaseLiveQueryOptions _options;
    private readonly BaseSubjectControlOperationalState? _subjectControlState;
    private readonly object _subscriptionsSync = new();
    private readonly ConcurrentDictionary<string, IBaseLiveQueryState> _subscriptions =
        new(StringComparer.Ordinal);
    private int _reservedSubscriptions;
    private long _invalidationGeneration;
    private readonly object _invalidationIdentitySync = new();
    private readonly HashSet<string> _deliveredInvalidationIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _deliveredInvalidationOrder = new();
    private const int MaximumRetainedInvalidationIdentities = 4096;

    /// <summary>Initializes a new instance.</summary>
    public DefaultBaseLiveQueryCoordinator(
        BaseLiveQueryOptions options,
        BaseSubjectControlOperationalState? subjectControlState = null)
    {
        _options = options;
        _subjectControlState = subjectControlState;
    }

    /// <summary>Executes the subscribe async operation.</summary>
    public async ValueTask<IBaseLiveQuerySubscription<T>> SubscribeAsync<T>(
        BaseLiveQueryRequest<T> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateQueryId(request.QueryId);
        ArgumentNullException.ThrowIfNull(request.ExecuteAsync);
        cancellationToken.ThrowIfCancellationRequested();
        RequireSubjectControlAdmission();

        ReserveSubscription();
        var reservationHeld = true;
        var executionGeneration = CurrentGeneration();
        try
        {
            var initial = await ExecuteAsync(
                request.ExecuteAsync,
                cancellationToken,
                _options.MaxEvaluationDuration).ConfigureAwait(false);
            ValidateDependencies(initial.Dependencies);
            RequireSubjectControlAdmission();

            var id = Guid.NewGuid().ToString("N");
            var state = new BaseLiveQueryState<T>(
                id,
                request,
                initial,
                _options,
                CurrentGeneration,
                Remove);
            lock (_subscriptionsSync)
            {
                _reservedSubscriptions--;
                reservationHeld = false;
                if (!_subscriptions.TryAdd(id, state))
                    throw Failure(BaseLiveQueryErrorCodes.CapacityExceeded, "The live-query subscription could not be registered.");
            }

            state.Start(CurrentGeneration() != executionGeneration);
            return state;
        }
        catch
        {
            if (reservationHeld)
                ReleaseReservation();
            throw;
        }
    }

    /// <summary>Executes the invalidate async operation.</summary>
    public ValueTask InvalidateAsync(
        BaseDependencyInvalidation invalidation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invalidation);
        cancellationToken.ThrowIfCancellationRequested();
        if (invalidation.References is not { Length: > 0 } references
            || references.Any(static reference =>
                reference is null
                || string.IsNullOrWhiteSpace(reference.TemplateId)
                || string.IsNullOrWhiteSpace(reference.Value)
                || reference.TemplateId.Length > 128
                || reference.Value.Length > 128))
            throw Failure(BaseLiveQueryErrorCodes.InvalidationFailed, "The live-query invalidation is invalid.");
        if (!AdmitInvalidationIdentity(invalidation.EventId)) return ValueTask.CompletedTask;
        Interlocked.Increment(ref _invalidationGeneration);
        foreach (var state in _subscriptions.Values)
            state.Invalidate(invalidation);
        return ValueTask.CompletedTask;
    }

    private bool AdmitInvalidationIdentity(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 256)
            throw Failure(BaseLiveQueryErrorCodes.InvalidationFailed, "The live-query invalidation is invalid.");
        lock (_invalidationIdentitySync)
        {
            if (!_deliveredInvalidationIds.Add(eventId)) return false;
            _deliveredInvalidationOrder.Enqueue(eventId);
            while (_deliveredInvalidationOrder.Count > MaximumRetainedInvalidationIdentities)
                _deliveredInvalidationIds.Remove(_deliveredInvalidationOrder.Dequeue());
            return true;
        }
    }

    /// <inheritdoc />
    public ValueTask InvalidateSubjectAuthorityAsync(
        BaseSubjectAuthorityPublicationFact publication,
        BaseDependencyInvalidation invalidation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publication);
        ArgumentNullException.ThrowIfNull(invalidation);
        cancellationToken.ThrowIfCancellationRequested();
        if (publication.PublishedStateGeneration <= 0)
            throw Failure(BaseLiveQueryErrorCodes.InvalidationFailed, "The subject-authority invalidation is invalid.");
        Interlocked.Increment(ref _invalidationGeneration);
        foreach (var state in _subscriptions.Values)
            state.InvalidateSubjectAuthority(publication, invalidation);
        return ValueTask.CompletedTask;
    }

    internal void FailAll(string code, string safeMessage)
    {
        foreach (var state in _subscriptions.Values)
            state.Fail(code, safeMessage);
    }

    private void Remove(string id)
    {
        lock (_subscriptionsSync)
            _subscriptions.TryRemove(id, out _);
    }

    private void ReserveSubscription()
    {
        lock (_subscriptionsSync)
        {
            if (_subscriptions.Count + _reservedSubscriptions >= _options.MaxActiveSubscriptions)
                throw Failure(BaseLiveQueryErrorCodes.CapacityExceeded, "The live-query subscription limit has been reached.");
            _reservedSubscriptions++;
        }
    }

    private void RequireSubjectControlAdmission()
    {
        if (_subjectControlState is not null && !_subjectControlState.AdmitsLiveState)
            throw Failure(
                BaseSubjectErrorCodes.ValidationUnavailable,
                "Exported-subject control publication reconciliation is unavailable.");
    }

    private void ReleaseReservation()
    {
        lock (_subscriptionsSync)
        {
            if (_reservedSubscriptions > 0)
                _reservedSubscriptions--;
        }
    }

    private long CurrentGeneration() => Volatile.Read(ref _invalidationGeneration);

    private void ValidateQueryId(string queryId)
    {
        if (string.IsNullOrWhiteSpace(queryId)
            || queryId.Length > _options.MaxQueryIdLength
            || queryId.Any(static character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')))
            throw Failure(BaseLiveQueryErrorCodes.RequestInvalid, "The live-query id is invalid.");
    }

    private void ValidateDependencies(BaseDependencySet dependencies)
    {
        if (dependencies?.References is not { Length: > 0 } references
            || references.Length > _options.MaxDependenciesPerEvaluation
            || references.Any(static reference =>
                reference is null
                || string.IsNullOrWhiteSpace(reference.TemplateId)
                || string.IsNullOrWhiteSpace(reference.Value)
                || reference.TemplateId.Length > 128
                || reference.Value.Length > 128)
            || references.DistinctBy(static reference => (reference.TemplateId, reference.Value)).Count() != references.Length)
            throw Failure(BaseLiveQueryErrorCodes.DependenciesInvalid, "The live-query dependency set is invalid.");
    }

    private static async ValueTask<BaseLiveQueryEvaluation<T>> ExecuteAsync<T>(
        Func<CancellationToken, ValueTask<BaseLiveQueryEvaluation<T>>> executor,
        CancellationToken cancellationToken,
        TimeSpan maximumDuration)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(maximumDuration);
        try
        {
            return await executor(lifetime.Token)
                .AsTask()
                .WaitAsync(lifetime.Token)
                .ConfigureAwait(false)
                ?? throw Failure(BaseLiveQueryErrorCodes.DependenciesInvalid, "The live-query evaluation is invalid.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw Failure(
                BaseLiveQueryErrorCodes.ExecutionFailed,
                "The live query exceeded the configured evaluation duration.");
        }
        catch (BaseLiveQueryException)
        {
            throw;
        }
        catch (Exception)
        {
            throw Failure(BaseLiveQueryErrorCodes.ExecutionFailed, "The live query could not be executed safely.");
        }
    }

    private static BaseLiveQueryException Failure(string code, string message) => new(code, message);

    private sealed class BaseLiveQueryState<T> : IBaseLiveQueryState, IBaseLiveQuerySubscription<T>
    {
        private readonly BaseLiveQueryRequest<T> _request;
        private readonly BaseLiveQueryOptions _options;
        private readonly Func<long> _currentGeneration;
        private readonly Action<string> _remove;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Channel<bool> _signals;
        private readonly Channel<BaseLiveQueryTransition<T>> _transitions;
        private readonly object _sync = new();
        private BaseDependencyReference[] _dependencies;
        private Task? _worker;
        private long _version = 1;
        private int _stopped;
        private int _transitionReaderActive;

        /// <summary>Initializes a new instance.</summary>
        public BaseLiveQueryState(
            string id,
            BaseLiveQueryRequest<T> request,
            BaseLiveQueryEvaluation<T> initial,
            BaseLiveQueryOptions options,
            Func<long> currentGeneration,
            Action<string> remove)
        {
            SubscriptionId = id;
            QueryId = request.QueryId;
            _request = request;
            _options = options;
            _currentGeneration = currentGeneration;
            _remove = remove;
            _dependencies = initial.Dependencies.References;
            _signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
            _transitions = Channel.CreateBounded<BaseLiveQueryTransition<T>>(
                new BoundedChannelOptions(options.TransitionBufferCapacity)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false
                });
            _transitions.Writer.TryWrite(Snapshot(initial.Value, _version));
        }

        /// <summary>Gets the subscription ID.</summary>
        public string SubscriptionId { get; }
        /// <summary>Gets the query ID.</summary>
        public string QueryId { get; }
        /// <summary>Gets the transitions.</summary>
        public IAsyncEnumerable<BaseLiveQueryTransition<T>> Transitions => ReadTransitionsAsync();

        /// <summary>Executes the start operation.</summary>
        public void Start(bool rerunRequired)
        {
            _worker = RunAsync();
            if (rerunRequired)
                _signals.Writer.TryWrite(true);
        }

        /// <summary>Executes the invalidate operation.</summary>
        public void Invalidate(BaseDependencyInvalidation invalidation)
        {
            if (Volatile.Read(ref _stopped) != 0)
                return;
            BaseDependencyReference[] dependencies;
            lock (_sync)
                dependencies = _dependencies;
            if (invalidation.References.Any(invalidated =>
                dependencies.Any(dependency =>
                    string.Equals(dependency.TemplateId, invalidated.TemplateId, StringComparison.Ordinal)
                    && string.Equals(dependency.Value, invalidated.Value, StringComparison.Ordinal))))
                _signals.Writer.TryWrite(true);
        }

        /// <summary>Publishes an ordered subject-authority control before scheduling a replacement evaluation.</summary>
        public void InvalidateSubjectAuthority(BaseSubjectAuthorityPublicationFact publication, BaseDependencyInvalidation invalidation)
        {
            if (Volatile.Read(ref _stopped) != 0)
                return;
            BaseDependencyReference[] dependencies;
            lock (_sync)
                dependencies = _dependencies;
            if (!invalidation.References.Any(invalidated => dependencies.Any(dependency =>
                    string.Equals(dependency.TemplateId, invalidated.TemplateId, StringComparison.Ordinal)
                    && string.Equals(dependency.Value, invalidated.Value, StringComparison.Ordinal))))
                return;
            _transitions.Writer.TryWrite(new BaseLiveQueryTransition<T>
            {
                Kind = BaseLiveQueryTransitionKind.SubjectAuthorityChanged,
                Version = Volatile.Read(ref _version),
                SubjectContractId = publication.ContractId,
                SubjectContractVersion = publication.ContractVersion,
                SubjectStateGeneration = publication.PublishedStateGeneration,
            });
            _signals.Writer.TryWrite(true);
        }

        /// <summary>Executes the fail operation.</summary>
        public void Fail(string code, string safeMessage)
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0)
                return;
            _transitions.Writer.TryWrite(new BaseLiveQueryTransition<T>
            {
                Kind = BaseLiveQueryTransitionKind.Failed,
                Version = Volatile.Read(ref _version),
                Failure = new BaseLiveQueryFailure { Code = code, Message = safeMessage }
            });
            _transitions.Writer.TryComplete();
            _signals.Writer.TryComplete();
            _cancellation.Cancel();
            _remove(SubscriptionId);
        }

        /// <summary>Executes the dispose async operation.</summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 0)
            {
                _signals.Writer.TryComplete();
                await _cancellation.CancelAsync().ConfigureAwait(false);
                _transitions.Writer.TryComplete();
                _remove(SubscriptionId);
            }

            if (_worker is not null)
            {
                try
                {
                    await _worker.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
            _cancellation.Dispose();
        }

        private async Task RunAsync()
        {
            try
            {
                await foreach (var _ in _signals.Reader.ReadAllAsync(_cancellation.Token).ConfigureAwait(false))
                {
                    var executionGeneration = _currentGeneration();
                    BaseLiveQueryEvaluation<T> evaluation;
                    try
                    {
                        evaluation = await ExecuteAsync(
                            _request.ExecuteAsync,
                            _cancellation.Token,
                            _options.MaxEvaluationDuration).ConfigureAwait(false);
                        ValidateEvaluationDependencies(evaluation.Dependencies);
                    }
                    catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (BaseLiveQueryException exception)
                    {
                        Fail(exception.Code, exception.SafeMessage);
                        return;
                    }

                    lock (_sync)
                        _dependencies = evaluation.Dependencies.References;
                    var version = Interlocked.Increment(ref _version);
                    _transitions.Writer.TryWrite(Snapshot(evaluation.Value, version));
                    if (_currentGeneration() != executionGeneration)
                        _signals.Writer.TryWrite(true);
                }
            }
            finally
            {
                if (Volatile.Read(ref _stopped) == 0)
                {
                    Interlocked.Exchange(ref _stopped, 1);
                    _transitions.Writer.TryComplete();
                    _remove(SubscriptionId);
                }
            }
        }

        private void ValidateEvaluationDependencies(BaseDependencySet dependencies)
        {
            if (dependencies?.References is not { Length: > 0 } references
                || references.Length > _options.MaxDependenciesPerEvaluation
                || references.Any(static reference =>
                    reference is null
                    || string.IsNullOrWhiteSpace(reference.TemplateId)
                    || string.IsNullOrWhiteSpace(reference.Value)
                    || reference.TemplateId.Length > 128
                    || reference.Value.Length > 128)
                || references.DistinctBy(static reference => (reference.TemplateId, reference.Value)).Count() != references.Length)
                throw Failure(BaseLiveQueryErrorCodes.DependenciesInvalid, "The live-query dependency set is invalid.");
        }

        private async IAsyncEnumerable<BaseLiveQueryTransition<T>> ReadTransitionsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _transitionReaderActive, 1, 0) != 0)
                throw Failure(
                    BaseLiveQueryErrorCodes.RequestInvalid,
                    "The live-query transition stream already has an active reader.");
            try
            {
                await foreach (var transition in _transitions.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                    yield return transition;
            }
            finally
            {
                Volatile.Write(ref _transitionReaderActive, 0);
            }
        }

        private static BaseLiveQueryTransition<T> Snapshot(T value, long version) => new()
        {
            Kind = BaseLiveQueryTransitionKind.Snapshot,
            Version = version,
            Value = value
        };
    }
}
