using System.Security.Cryptography;
using System.Text;
using HPDAgent.Graph.Connectors.Abstractions.Events;
using HPDAgent.Graph.Connectors.Abstractions.Sources;

namespace HPDAgent.Graph.Connectors.Core.Dedupe;

public sealed class WorkflowSourceDedupeService : IWorkflowSourceDedupeService
{
    private const string LastEventIdKey = "dedupe:lastEventId";
    private const string SeenPrefix = "dedupe:seen:";

    private readonly IWorkflowSourceStore _sourceStore;
    private readonly TimeProvider _timeProvider;

    public WorkflowSourceDedupeService(
        IWorkflowSourceStore sourceStore,
        TimeProvider? timeProvider = null)
    {
        _sourceStore = sourceStore ?? throw new ArgumentNullException(nameof(sourceStore));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<bool> ShouldDispatchAsync(
        WorkflowSourceEmittedEvent evt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (evt.DedupeStrategy == DedupeStrategy.None)
        {
            return true;
        }

        var dedupeId = GetDedupeId(evt);
        var state = await _sourceStore.LoadStateAsync(evt.SourceId, ct).ConfigureAwait(false)
            ?? new WorkflowSourceState
            {
                SourceId = evt.SourceId,
                UpdatedAt = _timeProvider.GetUtcNow()
            };

        var values = new Dictionary<string, string>(state.Values, StringComparer.Ordinal);

        if (evt.DedupeStrategy == DedupeStrategy.Unique)
        {
            var seenKey = SeenPrefix + dedupeId;
            if (values.ContainsKey(seenKey))
            {
                return false;
            }

            values[seenKey] = _timeProvider.GetUtcNow().ToString("O");
            values[LastEventIdKey] = dedupeId;
            await SaveAsync(state, values, ct).ConfigureAwait(false);
            return true;
        }

        if (values.TryGetValue(LastEventIdKey, out var lastEventId) &&
            string.Equals(lastEventId, dedupeId, StringComparison.Ordinal))
        {
            return false;
        }

        values[LastEventIdKey] = dedupeId;
        await SaveAsync(state, values, ct).ConfigureAwait(false);
        return true;
    }

    private Task SaveAsync(
        WorkflowSourceState state,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct)
    {
        return _sourceStore.SaveStateAsync(state with
        {
            Values = values,
            UpdatedAt = _timeProvider.GetUtcNow()
        }, ct);
    }

    private static string GetDedupeId(WorkflowSourceEmittedEvent evt)
    {
        if (!string.IsNullOrWhiteSpace(evt.EventId))
        {
            return evt.EventId;
        }

        var material = $"{evt.SourceId}|{evt.SourceType}|{evt.Payload.GetRawText()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(bytes);
    }
}
