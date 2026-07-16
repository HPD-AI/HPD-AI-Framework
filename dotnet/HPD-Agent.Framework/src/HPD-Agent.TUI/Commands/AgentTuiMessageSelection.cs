using System.Text;
using HPD.Agent.TUI.Composition;

namespace HPD.Agent.TUI.Commands;

public enum AgentTuiMessageSelectionPolicy
{
    EffectiveContextOnly,
    IncludeCompactedWithBadge,
    RawTimeline
}

public sealed record AgentTuiSelectableMessage(
    string MessageId,
    string Text,
    int Index,
    string? MessageTurnId,
    bool IsCompacted,
    bool IsModelVisible,
    DateTimeOffset? CreatedAt);

public static class AgentTuiMessageSelection
{
    public static async Task<IReadOnlyList<AgentTuiSelectableMessage>> GetUserMessagesAsync(
        AgentTuiCommandContext context,
        AgentTuiMessageSelectionPolicy policy = AgentTuiMessageSelectionPolicy.EffectiveContextOnly,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var threadState = await context.Runtime.GetThreadStateAsync(context.Scope, cancellationToken)
            .ConfigureAwait(false);
        var events = new List<AgentEvent>();
        if (threadState.ObservedCursor.SequenceNumber > 0)
        {
            await foreach (var batch in context.Runtime.ObserveAsync(
                    context.Scope,
                    ThreadJournalCursor.Start(threadState.ObservedCursor.Generation),
                    threadState.ObservedCursor,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                events.AddRange(batch.Events.Where(evt => evt.ThreadSequenceNumber <= threadState.ObservedCursor.SequenceNumber));
                if (batch.LastCursor.SequenceNumber >= threadState.ObservedCursor.SequenceNumber)
                    break;
            }
        }

        return GetUserMessages(events, policy);
    }

    public static IReadOnlyList<AgentTuiSelectableMessage> GetUserMessages(
        IEnumerable<AgentEvent> events,
        AgentTuiMessageSelectionPolicy policy = AgentTuiMessageSelectionPolicy.EffectiveContextOnly)
    {
        ArgumentNullException.ThrowIfNull(events);

        var builders = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        var order = new List<string>();
        var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var turns = new Dictionary<string, string?>(StringComparer.Ordinal);
        var createdAt = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal);
        var compacted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var evt in events.OrderBy(static evt => evt.ThreadSequenceNumber))
        {
            switch (evt)
            {
                case TextMessageStartEvent start:
                    roles[start.MessageId] = start.Role;
                    turns[start.MessageId] = start.EventFlowId;
                    createdAt[start.MessageId] = start.Timestamp;
                    EnsureBuilder(start.MessageId, builders, order);
                    break;

                case TextDeltaEvent delta:
                    EnsureBuilder(delta.MessageId, builders, order).Append(delta.Text);
                    break;

                case ThreadHistoryCompactionCheckpointEvent checkpoint:
                    foreach (var messageId in checkpoint.CompactedMessageIds)
                    {
                        if (!string.IsNullOrWhiteSpace(messageId))
                            compacted.Add(messageId);
                    }
                    break;
            }
        }

        var messages = new List<AgentTuiSelectableMessage>();
        foreach (var messageId in order)
        {
            if (!roles.TryGetValue(messageId, out var role) ||
                !string.Equals(role, "user", StringComparison.OrdinalIgnoreCase) ||
                !builders.TryGetValue(messageId, out var builder))
            {
                continue;
            }

            var isCompacted = compacted.Contains(messageId);
            if (policy == AgentTuiMessageSelectionPolicy.EffectiveContextOnly && isCompacted)
                continue;

            var text = builder.ToString().Trim();
            if (text.Length == 0)
                continue;

            messages.Add(new AgentTuiSelectableMessage(
                messageId,
                text,
                messages.Count + 1,
                turns.TryGetValue(messageId, out var turnId) ? turnId : null,
                isCompacted,
                !isCompacted,
                createdAt.TryGetValue(messageId, out var timestamp) ? timestamp : null));
        }

        return messages;
    }

    private static StringBuilder EnsureBuilder(
        string messageId,
        Dictionary<string, StringBuilder> builders,
        List<string> order)
    {
        if (builders.TryGetValue(messageId, out var builder))
            return builder;

        builder = new StringBuilder();
        builders[messageId] = builder;
        order.Add(messageId);
        return builder;
    }
}
