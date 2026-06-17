using HPD.Agent.Audio.Ledger;

namespace HPD.Agent.Audio.Runtime.Thread;

public sealed class InMemoryThreadProjectionSink : IThreadProjectionSink
{
    private readonly List<ProjectedThreadTurn> _projectedTurns = [];
    private readonly object _gate = new();
    private long _sequence;

    public ValueTask<ThreadProjectedEventRef> ProjectAsync(
        ThreadRef thread,
        ThreadProjectionRecord record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sequence = Interlocked.Increment(ref _sequence);
        var projected = new ProjectedThreadTurn(
            thread,
            record,
            new ThreadProjectedEventRef($"thread-event-{sequence:D4}", sequence));

        lock (_gate)
        {
            _projectedTurns.Add(projected);
        }

        return ValueTask.FromResult(projected.ProjectedEvent);
    }

    public IReadOnlyList<ProjectedThreadTurn> ProjectedTurns
    {
        get
        {
            lock (_gate)
            {
                return _projectedTurns.ToArray();
            }
        }
    }
}

public sealed record ProjectedThreadTurn(
    ThreadRef Thread,
    ThreadProjectionRecord Record,
    ThreadProjectedEventRef ProjectedEvent);
