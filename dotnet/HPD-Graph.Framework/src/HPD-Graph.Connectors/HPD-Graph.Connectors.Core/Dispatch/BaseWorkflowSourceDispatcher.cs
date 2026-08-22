using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Base;
using HPD.Events;
using HPD.Graph.Base;
using HPD.Graph.Connectors.Abstractions.Events;
using HPD.Graph.Connectors.Abstractions.Sources;
using HPD.Graph.Connectors.Core.Dedupe;

namespace HPD.Graph.Connectors.Core.Dispatch;

/// <summary>
/// Converts installed connector events into identified durable HPD.Base graph
/// activations. It owns no execution thread, queue, lease, or retry authority.
/// </summary>
public sealed class BaseWorkflowSourceDispatcher : IWorkflowSourceDispatcher
{
    private readonly IReadOnlyDictionary<string, BaseGraphActivationDefinition> _graphs;
    private readonly IWorkflowSourceStore _sources;
    private readonly IWorkflowSourceDedupeService _dedupe;
    private readonly IBaseSessionFactory _sessions;
    private readonly IEventCoordinator? _events;

    /// <summary>Initializes one dispatcher over an immutable installed graph set.</summary>
    public BaseWorkflowSourceDispatcher(
        IEnumerable<BaseGraphActivationDefinition> graphs,
        IWorkflowSourceStore sources,
        IWorkflowSourceDedupeService dedupe,
        IBaseSessionFactory sessions,
        IEventCoordinator? events = null)
    {
        ArgumentNullException.ThrowIfNull(graphs);
        _graphs = graphs.ToDictionary(static graph => graph.GraphId, StringComparer.Ordinal);
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _dedupe = dedupe ?? throw new ArgumentNullException(nameof(dedupe));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _events = events;
    }

    /// <inheritdoc />
    public async Task DispatchAsync(WorkflowSourceEmittedEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        WorkflowSource? source = await _sources.LoadAsync(evt.SourceId, ct).ConfigureAwait(false);
        if (source is null || !source.Enabled
            || !string.Equals(source.GraphId, evt.GraphId, StringComparison.Ordinal)
            || !string.Equals(source.SourceType, evt.SourceType, StringComparison.Ordinal)
            || !_graphs.TryGetValue(source.GraphId, out BaseGraphActivationDefinition? graph)
            || !await _dedupe.ShouldDispatchAsync(evt, ct).ConfigureAwait(false))
            return;

        byte[] canonical = BuildCanonicalInput(source, evt);
        string executionId = ExecutionId(evt, canonical);
        BaseGraphActivationInput input = graph.CreateInput(executionId, canonical);
        byte[] fingerprintBytes = Fingerprint(source, evt, graph, canonical);
        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
            $"graph-source:{source.SourceId}",
            "enqueue",
            evt.EventId ?? Convert.ToHexStringLower(fingerprintBytes),
            BaseMutationRequestFingerprint.Create(fingerprintBytes));
        BaseSession session = _sessions.For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectId = "hpd.graph.connectors",
        });
        OperationResult<BaseActivationEnqueueResult> result = await session.Activations
            .Get(graph.Registration.Identity)
            .EnqueueAsync(input, identity, cancellationToken: ct)
            .ConfigureAwait(false);
        if (!result.IsSuccess() || result.Value is null)
            throw new InvalidOperationException(result.Error?.Code ?? "base.activation.storeError");

        if (_events is not null)
        {
            await _events.EmitAsync(new WorkflowExecutionDispatchedEvent
            {
                SourceId = source.SourceId,
                SourceType = source.SourceType,
                GraphId = source.GraphId,
                ExecutionId = executionId,
                EventId = evt.EventId,
            }, ct).ConfigureAwait(false);
        }
    }

    private static byte[] BuildCanonicalInput(WorkflowSource source, WorkflowSourceEmittedEvent evt)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (source.DefaultInput is { ValueKind: JsonValueKind.Object } defaults)
                foreach (JsonProperty property in defaults.EnumerateObject().OrderBy(static item => item.Name, StringComparer.Ordinal))
                    property.WriteTo(writer);
            writer.WritePropertyName("source");
            writer.WriteStartObject();
            if (evt.EventId is not null) writer.WriteString("eventId", evt.EventId);
            writer.WriteString("occurredAt", evt.OccurredAt);
            writer.WritePropertyName("payload");
            evt.Payload.WriteTo(writer);
            writer.WriteString("sourceId", evt.SourceId);
            writer.WriteString("sourceType", evt.SourceType);
            if (evt.Summary is not null) writer.WriteString("summary", evt.Summary);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return BaseGraphActivationRegistration.CanonicalJson(stream.ToArray());
    }

    private static string ExecutionId(WorkflowSourceEmittedEvent evt, ReadOnlySpan<byte> canonical)
    {
        if (!string.IsNullOrWhiteSpace(evt.EventId)) return $"source:{evt.SourceId}:{evt.EventId}";
        byte[] digest = SHA256.HashData(canonical);
        return $"source:{evt.SourceId}:{Convert.ToHexStringLower(digest)}";
    }

    private static byte[] Fingerprint(
        WorkflowSource source,
        WorkflowSourceEmittedEvent evt,
        BaseGraphActivationDefinition graph,
        ReadOnlySpan<byte> canonical)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("hpd.graph.connector.activation.v1\0"u8);
        Append(hash, source.SourceId);
        Append(hash, source.SourceType);
        Append(hash, graph.GraphId);
        Append(hash, graph.GraphVersion);
        hash.AppendData(graph.GraphChecksum.Span);
        Append(hash, evt.EventId ?? string.Empty);
        hash.AppendData(SHA256.HashData(canonical));
        return hash.GetHashAndReset();
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
