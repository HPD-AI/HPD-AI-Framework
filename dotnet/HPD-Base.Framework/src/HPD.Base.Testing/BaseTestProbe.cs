using System.Collections.Concurrent;
using HPD.Base.Dependencies;
using HPD.Base.Events;
using HPD.Base.Runtime.Events;

namespace HPD.Base.Testing;

/// <summary>
/// Captures committed mutations and dependency invalidations for assertions.
/// </summary>
public sealed class BaseTestProbe(
    BaseTestFaults faults,
    IBaseDependencyInvalidationMapper? invalidations = null)
    : IBaseCommittedMutationObserver
{
    private readonly ConcurrentQueue<BaseRecordMutationEvent> _mutations = new();
    private readonly ConcurrentQueue<BaseDependencyInvalidation> _invalidations = new();

    /// <summary>Gets a stable snapshot of mutations observed by the test host.</summary>
    public IReadOnlyList<BaseRecordMutationEvent> Mutations => _mutations.ToArray();

    /// <summary>Gets a stable snapshot of mapped dependency invalidations.</summary>
    public IReadOnlyList<BaseDependencyInvalidation> Invalidations =>
        _invalidations.ToArray();

    /// <summary>Clears all captured observations.</summary>
    public void Clear()
    {
        _mutations.Clear();
        _invalidations.Clear();
    }

    /// <inheritdoc />
    public async ValueTask ObserveAsync(
        BaseRecordMutationEvent mutation,
        CancellationToken cancellationToken = default)
    {
        _mutations.Enqueue(mutation);
        if (invalidations is not null)
        {
            _invalidations.Enqueue(
                await invalidations.MapAsync(mutation, cancellationToken)
                    .ConfigureAwait(false));
        }

        if (faults.TakeObserverFailure())
        {
            throw new InvalidOperationException(
                "The test host injected a post-commit observer failure.");
        }
    }
}
