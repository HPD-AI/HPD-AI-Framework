using HPD.Agent.Audio.Ledger;

namespace HPD.Agent.Audio.Runtime.Branch;

public sealed class InMemoryBranchProjectionSink : IBranchProjectionSink
{
    private readonly List<ProjectedBranchTurn> _projectedTurns = [];
    private readonly object _gate = new();
    private long _sequence;

    public ValueTask<BranchProjectedEventRef> ProjectAsync(
        BranchRef branch,
        BranchProjectionRecord record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sequence = Interlocked.Increment(ref _sequence);
        var projected = new ProjectedBranchTurn(
            branch,
            record,
            new BranchProjectedEventRef($"branch-event-{sequence:D4}", sequence));

        lock (_gate)
        {
            _projectedTurns.Add(projected);
        }

        return ValueTask.FromResult(projected.ProjectedEvent);
    }

    public IReadOnlyList<ProjectedBranchTurn> ProjectedTurns
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

public sealed record ProjectedBranchTurn(
    BranchRef Branch,
    BranchProjectionRecord Record,
    BranchProjectedEventRef ProjectedEvent);
