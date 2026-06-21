using System.Runtime.CompilerServices;

namespace HPD.Agent;

public static class ThreadEventReplayExtensions
{
    public static async IAsyncEnumerable<AgentEvent> FilterByReplayOptions(
        this IEnumerable<AgentEvent> events,
        HPD.Events.ReplayReadOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var emitted = 0;
        foreach (var evt in events.OrderBy(e => e.SequenceNumber))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (options.From is not null && evt.Timestamp < options.From)
                continue;
            if (options.To is not null && evt.Timestamp >= options.To)
                continue;
            if (options.EventFlowId is not null && evt.EventFlowId != options.EventFlowId)
                continue;

            yield return evt;

            emitted++;
            if (options.Limit is { } limit && emitted >= limit)
                yield break;

            await Task.Yield();
        }
    }
}
