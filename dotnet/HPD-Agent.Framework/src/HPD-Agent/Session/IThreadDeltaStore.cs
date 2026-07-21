namespace HPD.Agent;

/// <summary>
/// Stages progressive model-output deltas outside the canonical journal and settles
/// them as compact journal events when their message boundary is reached.
/// </summary>
public interface IThreadDeltaStore
{
    /// <summary>
    /// Durably stages one scoped text or reasoning delta before it is delivered live.
    /// </summary>
    ValueTask StageThreadDeltaAsync(
        ThreadKey thread,
        AgentEvent delta,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically appends the staged compact delta events followed by the supplied
    /// message-end event, then removes the completed staging record.
    /// </summary>
    ValueTask<ThreadEventAppendResult> FinalizeThreadDeltasAsync(
        ThreadKey thread,
        AgentEvent messageEnd,
        CancellationToken cancellationToken = default);

    /// <summary>Settles incomplete durable staging records before journal replay.</summary>
    ValueTask RecoverThreadDeltasAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default);
}

internal static class ThreadDeltaCoalescer
{
    public static IReadOnlyList<AgentEvent> Coalesce(
        IReadOnlyList<AgentEvent> deltas,
        AgentEvent messageEnd)
    {
        if (deltas.Count == 0)
            return [messageEnd];

        var result = new List<AgentEvent>();
        if (deltas[0] is TextDeltaEvent firstText)
        {
            result.Add(firstText with
            {
                Text = string.Concat(deltas.Cast<TextDeltaEvent>().Select(delta => delta.Text)),
                ThreadSequenceNumber = 0
            });
        }
        else if (deltas[0] is ReasoningDeltaEvent)
        {
            var segment = new List<ReasoningDeltaEvent>();
            foreach (var delta in deltas.Cast<ReasoningDeltaEvent>())
            {
                if (segment.Count > 0 && !StringComparer.Ordinal.Equals(segment[0].ProtectedData, delta.ProtectedData))
                {
                    result.Add(CoalesceReasoningSegment(segment, result.Count));
                    segment.Clear();
                }
                segment.Add(delta);
            }
            if (segment.Count > 0)
                result.Add(CoalesceReasoningSegment(segment, result.Count));
        }
        else
        {
            throw new ArgumentException("Only text and reasoning deltas can be coalesced.", nameof(deltas));
        }

        result.Add(messageEnd);
        return result;
    }

    private static ReasoningDeltaEvent CoalesceReasoningSegment(
        IReadOnlyList<ReasoningDeltaEvent> segment,
        int segmentIndex)
    {
        var first = segment[0];
        return first with
        {
            EventId = segmentIndex == 0 ? first.EventId : $"{first.EventId}-{segmentIndex}",
            Text = string.Concat(segment.Select(delta => delta.Text)),
            ThreadSequenceNumber = 0
        };
    }
}
