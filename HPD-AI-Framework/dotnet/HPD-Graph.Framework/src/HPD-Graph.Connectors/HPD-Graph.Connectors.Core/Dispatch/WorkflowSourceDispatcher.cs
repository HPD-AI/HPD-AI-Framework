using System.Text.Json;
using HPD.Events;
using HPD.Graph.Connectors.Abstractions.Events;
using HPD.Graph.Connectors.Abstractions.Sources;
using HPD.Graph.Connectors.Core.Dedupe;
using HPD.Graph.Hosting.Data;
using HPD.Graph.Hosting.Lifecycle;

namespace HPD.Graph.Connectors.Core.Dispatch;

public sealed class WorkflowSourceDispatcher : IWorkflowSourceDispatcher
{
    private readonly IWorkflowSourceStore _sourceStore;
    private readonly IWorkflowExecutionRunner _executionRunner;
    private readonly IWorkflowSourceDedupeService _dedupe;
    private readonly IEventCoordinator? _events;

    public WorkflowSourceDispatcher(
        IWorkflowSourceStore sourceStore,
        IWorkflowExecutionRunner executionRunner,
        IWorkflowSourceDedupeService dedupe,
        IEventCoordinator? events = null)
    {
        _sourceStore = sourceStore ?? throw new ArgumentNullException(nameof(sourceStore));
        _executionRunner = executionRunner ?? throw new ArgumentNullException(nameof(executionRunner));
        _dedupe = dedupe ?? throw new ArgumentNullException(nameof(dedupe));
        _events = events;
    }

    public async Task DispatchAsync(
        WorkflowSourceEmittedEvent evt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var source = await _sourceStore.LoadAsync(evt.SourceId, ct).ConfigureAwait(false);
        if (source is null || !source.Enabled)
        {
            return;
        }

        if (!string.Equals(source.GraphId, evt.GraphId, StringComparison.Ordinal) ||
            !string.Equals(source.SourceType, evt.SourceType, StringComparison.Ordinal))
        {
            return;
        }

        if (!await _dedupe.ShouldDispatchAsync(evt, ct).ConfigureAwait(false))
        {
            return;
        }

        var execution = await _executionRunner.StartAsync(
            source.GraphId,
            new ExecuteWorkflowRequest
            {
                Input = BuildInput(source, evt),
                TriggeredBy = $"source:{source.SourceId}"
            },
            ct).ConfigureAwait(false);

        if (_events is not null)
        {
            await _events.EmitAsync(new WorkflowExecutionDispatchedEvent
            {
                SourceId = source.SourceId,
                SourceType = source.SourceType,
                GraphId = source.GraphId,
                ExecutionId = execution.ExecutionId,
                EventId = evt.EventId
            }, ct).ConfigureAwait(false);
        }
    }

    private static JsonElement BuildInput(
        WorkflowSource source,
        WorkflowSourceEmittedEvent evt)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

        if (source.DefaultInput is { ValueKind: JsonValueKind.Object } defaultInput)
        {
            foreach (var property in defaultInput.EnumerateObject())
            {
                    property.WriteTo(writer);
            }
        }

            writer.WritePropertyName("source");
            writer.WriteStartObject();
            writer.WriteString("sourceId", evt.SourceId);
            writer.WriteString("sourceType", evt.SourceType);
            if (evt.EventId is not null)
            {
                writer.WriteString("eventId", evt.EventId);
            }

            if (evt.Summary is not null)
            {
                writer.WriteString("summary", evt.Summary);
            }

            writer.WriteString("occurredAt", evt.OccurredAt);
            writer.WritePropertyName("payload");
            evt.Payload.WriteTo(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }
}
