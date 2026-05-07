using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HPDAgent.Graph.Connectors.Abstractions.Sources;

namespace HPDAgent.Graph.Connectors.Core.Sources;

public sealed class WorkflowSourceStateAccessor : IWorkflowSourceStateAccessor
{
    private readonly IWorkflowSourceStore _store;
    private readonly string _sourceId;
    private readonly TimeProvider _timeProvider;

    public WorkflowSourceStateAccessor(
        IWorkflowSourceStore store,
        string sourceId,
        TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sourceId = string.IsNullOrWhiteSpace(sourceId)
            ? throw new ArgumentException("Source id cannot be empty.", nameof(sourceId))
            : sourceId;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<T?> GetAsync<T>(
        string key,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        var state = await _store.LoadStateAsync(_sourceId, ct).ConfigureAwait(false);
        if (state is null || !state.Values.TryGetValue(key, out var raw))
        {
            return default;
        }

        return JsonSerializer.Deserialize(raw, jsonTypeInfo);
    }

    public async ValueTask SetAsync<T>(
        string key,
        T value,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        var state = await _store.LoadStateAsync(_sourceId, ct).ConfigureAwait(false)
            ?? new WorkflowSourceState
            {
                SourceId = _sourceId,
                UpdatedAt = _timeProvider.GetUtcNow()
            };

        var values = new Dictionary<string, string>(state.Values, StringComparer.Ordinal)
        {
            [key] = JsonSerializer.Serialize(value, jsonTypeInfo)
        };

        await _store.SaveStateAsync(state with
        {
            Values = values,
            UpdatedAt = _timeProvider.GetUtcNow()
        }, ct).ConfigureAwait(false);
    }

    public async ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var state = await _store.LoadStateAsync(_sourceId, ct).ConfigureAwait(false);
        if (state is null || !state.Values.ContainsKey(key))
        {
            return;
        }

        var values = new Dictionary<string, string>(state.Values, StringComparer.Ordinal);
        values.Remove(key);

        await _store.SaveStateAsync(state with
        {
            Values = values,
            UpdatedAt = _timeProvider.GetUtcNow()
        }, ct).ConfigureAwait(false);
    }
}
