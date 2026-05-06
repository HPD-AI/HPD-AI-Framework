using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Storage;

namespace HPDAgent.Graph.Core.Storage;

/// <summary>
/// In-memory scheduled graph store for development and tests.
/// </summary>
public sealed class InMemoryScheduledGraphStore : IScheduledGraphStore
{
    private readonly Dictionary<string, ScheduledGraph> _schedules = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public Task<ScheduledGraph?> LoadAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            _schedules.TryGetValue(graphId, out var schedule);
            return Task.FromResult(schedule);
        }
    }

    public Task SaveAsync(ScheduledGraph schedule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentException.ThrowIfNullOrWhiteSpace(schedule.GraphId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            _schedules[schedule.GraphId] = schedule;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            _schedules.Remove(graphId);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ScheduledGraph>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            var schedules = _schedules.Values
                .OrderBy(schedule => schedule.GraphId, StringComparer.Ordinal)
                .ToList();

            return Task.FromResult<IReadOnlyList<ScheduledGraph>>(schedules);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _schedules.Clear();
        }
    }
}
