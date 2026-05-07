using System.Collections.Concurrent;
using HPDAgent.Graph.Connectors.Abstractions.Sources;

namespace HPDAgent.Graph.Connectors.Core.Sources;

public sealed class InMemoryWorkflowSourceStore : IWorkflowSourceStore
{
    private readonly ConcurrentDictionary<string, WorkflowSource> _sources =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, WorkflowSourceState> _states =
        new(StringComparer.Ordinal);

    public Task SaveAsync(WorkflowSource source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.SourceId);

        ct.ThrowIfCancellationRequested();
        _sources[source.SourceId] = source;
        return Task.CompletedTask;
    }

    public Task<WorkflowSource?> LoadAsync(string sourceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        ct.ThrowIfCancellationRequested();
        _sources.TryGetValue(sourceId, out var source);
        return Task.FromResult(source);
    }

    public Task<IReadOnlyList<WorkflowSource>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<WorkflowSource>>(
            _sources.Values
                .OrderBy(static source => source.SourceId, StringComparer.Ordinal)
                .ToArray());
    }

    public Task<IReadOnlyList<WorkflowSource>> ListByGraphAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);

        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<WorkflowSource>>(
            _sources.Values
                .Where(source => string.Equals(source.GraphId, graphId, StringComparison.Ordinal))
                .OrderBy(static source => source.SourceId, StringComparer.Ordinal)
                .ToArray());
    }

    public Task DeleteAsync(string sourceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        ct.ThrowIfCancellationRequested();
        _sources.TryRemove(sourceId, out _);
        _states.TryRemove(sourceId, out _);
        return Task.CompletedTask;
    }

    public Task<WorkflowSourceState?> LoadStateAsync(string sourceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        ct.ThrowIfCancellationRequested();
        _states.TryGetValue(sourceId, out var state);
        return Task.FromResult(state);
    }

    public Task SaveStateAsync(WorkflowSourceState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.SourceId);

        ct.ThrowIfCancellationRequested();
        _states[state.SourceId] = state;
        return Task.CompletedTask;
    }
}
