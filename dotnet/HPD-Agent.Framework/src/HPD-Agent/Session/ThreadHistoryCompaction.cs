using Microsoft.Extensions.AI;

namespace HPD.Agent;

public sealed record ThreadCompactionPlan(
    IReadOnlyList<ChatMessage> ModelCompactedMessages,
    IReadOnlyList<ChatMessage> RetainedMessages,
    IReadOnlyList<ChatMessage> DurableCompactedMessages,
    IReadOnlyList<ChatMessage> ReplacementMessages,
    CompactionStrategyOptions Strategy,
    CompactionRetentionOptions Retention,
    CompactionBoundaryOptions Boundary,
    string? SummaryContent);

public sealed record ThreadCompactionResult(
    string CompactionId,
    IReadOnlyList<string> ModelCompactedMessageIds,
    IReadOnlyList<string> DurableCompactedMessageIds,
    IReadOnlyList<string> ReplacementMessageIds,
    ThreadHistoryCompactionCheckpointEvent CheckpointEvent);

internal static class ThreadHistoryCompactionMetadata
{
    public const string MessageTurnIdPropertyName = "hpd.messageTurnId";
}

public interface IThreadCompactionPlanner
{
    ThreadCompactionPlan? Plan(
        Thread thread,
        CompactionResult compaction,
        CompactionRetentionOptions retention);
}

public interface IThreadHistoryCompactor
{
    ThreadCompactionResult Compact(
        Thread thread,
        ThreadCompactionPlan plan);
}

public sealed class ThreadCompactionPlanner : IThreadCompactionPlanner
{
    public ThreadCompactionPlan? Plan(
        Thread thread,
        CompactionResult compaction,
        CompactionRetentionOptions retention)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(compaction);
        ArgumentNullException.ThrowIfNull(retention);

        var boundary = retention switch
        {
            CompactThreadHistoryOptions compact => compact.Boundary,
            _ => new ExactCompactedMessagesBoundaryOptions()
        };

        var durableRemoved = retention is PreserveThreadHistoryOptions
            ? []
            : ExpandBoundary(thread, compaction, boundary);
        var replacementMessages = compaction.ReplacementMessages.ToList();

        return new ThreadCompactionPlan(
            compaction.ModelCompactedMessages,
            compaction.RetainedMessages,
            durableRemoved,
            replacementMessages,
            compaction.Strategy,
            retention,
            boundary,
            compaction.SummaryContent);
    }

    private static IReadOnlyList<ChatMessage> ExpandBoundary(
        Thread thread,
        CompactionResult compaction,
        CompactionBoundaryOptions boundary)
    {
        var selectedIds = compaction.ModelCompactedMessages
            .Where(message => !string.IsNullOrWhiteSpace(message.MessageId))
            .Select(message => message.MessageId!)
            .ToHashSet(StringComparer.Ordinal);

        ApplyBoundary(thread, selectedIds, compaction, boundary);

        var retainedIds = compaction.RetainedMessages
            .Where(message => !string.IsNullOrWhiteSpace(message.MessageId))
            .Select(message => message.MessageId!)
            .ToHashSet(StringComparer.Ordinal);

        return thread.Messages
            .Where(message =>
                !string.IsNullOrWhiteSpace(message.MessageId) &&
                selectedIds.Contains(message.MessageId!) &&
                !retainedIds.Contains(message.MessageId!) &&
                message.Role != ChatRole.System)
            .ToList();
    }

    private static void ApplyBoundary(
        Thread thread,
        HashSet<string> selectedIds,
        CompactionResult compaction,
        CompactionBoundaryOptions boundary)
    {
        switch (boundary)
        {
            case ExactCompactedMessagesBoundaryOptions:
                return;

            case IncludeMessageTurnBoundaryOptions:
                IncludeMessageTurns(thread, selectedIds);
                return;

            case IncludeToolCallGroupBoundaryOptions:
                IncludeToolCallGroups(thread, selectedIds);
                return;

            case IncludePreviousMessagesBoundaryOptions previous:
                IncludePreviousMessages(thread, selectedIds, compaction, previous.Count);
                return;

            case CompositeCompactionBoundaryOptions composite:
                foreach (var child in composite.Policies)
                    ApplyBoundary(thread, selectedIds, compaction, child);
                return;
        }
    }

    private static void IncludePreviousMessages(
        Thread thread,
        HashSet<string> selectedIds,
        CompactionResult compaction,
        int count)
    {
        if (count <= 0 || selectedIds.Count == 0)
            return;

        var retainedIds = compaction.RetainedMessages
            .Where(message => !string.IsNullOrWhiteSpace(message.MessageId))
            .Select(message => message.MessageId!)
            .ToHashSet(StringComparer.Ordinal);

        var firstSelectedIndex = thread.Messages.FindIndex(message =>
            !string.IsNullOrWhiteSpace(message.MessageId) && selectedIds.Contains(message.MessageId!));

        if (firstSelectedIndex <= 0)
            return;

        for (var i = firstSelectedIndex - 1; i >= 0 && count > 0; i--)
        {
            var message = thread.Messages[i];
            if (string.IsNullOrWhiteSpace(message.MessageId) ||
                retainedIds.Contains(message.MessageId!) ||
                message.Role == ChatRole.System)
            {
                continue;
            }

            selectedIds.Add(message.MessageId!);
            count--;
        }
    }

    private static void IncludeMessageTurns(
        Thread thread,
        HashSet<string> selectedIds)
    {
        var selectedTurnIds = thread.Messages
            .Where(message => !string.IsNullOrWhiteSpace(message.MessageId) && selectedIds.Contains(message.MessageId!))
            .Select(GetMessageTurnId)
            .Where(turnId => !string.IsNullOrWhiteSpace(turnId))
            .ToHashSet(StringComparer.Ordinal);

        if (selectedTurnIds.Count == 0)
            return;

        foreach (var message in thread.Messages)
        {
            if (!string.IsNullOrWhiteSpace(message.MessageId) &&
                GetMessageTurnId(message) is { } turnId &&
                selectedTurnIds.Contains(turnId))
            {
                selectedIds.Add(message.MessageId!);
            }
        }
    }

    private static void IncludeToolCallGroups(
        Thread thread,
        HashSet<string> selectedIds)
    {
        var selectedCallIds = thread.Messages
            .Where(message => !string.IsNullOrWhiteSpace(message.MessageId) && selectedIds.Contains(message.MessageId!))
            .SelectMany(GetToolCallIds)
            .ToHashSet(StringComparer.Ordinal);

        if (selectedCallIds.Count == 0)
            return;

        foreach (var message in thread.Messages)
        {
            if (!string.IsNullOrWhiteSpace(message.MessageId) &&
                GetToolCallIds(message).Any(selectedCallIds.Contains))
            {
                selectedIds.Add(message.MessageId!);
            }
        }
    }

    private static string? GetMessageTurnId(ChatMessage message) =>
        message.AdditionalProperties?.TryGetValue<string>(
            ThreadHistoryCompactionMetadata.MessageTurnIdPropertyName,
            out var turnId) == true
            ? turnId
            : null;

    private static IEnumerable<string> GetToolCallIds(ChatMessage message) =>
        message.Contents
            .Select(content => content switch
            {
                ToolCallContent toolCall => toolCall.CallId,
                ToolResultContent toolResult => toolResult.CallId,
                _ => null
            })
            .Where(callId => !string.IsNullOrWhiteSpace(callId))
            .Select(callId => callId!);
}

public sealed class ThreadHistoryCompactor : IThreadHistoryCompactor
{
    public ThreadCompactionResult Compact(
        Thread thread,
        ThreadCompactionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(plan);

        var durableRemovedIds = GetMessageIds(plan.DurableCompactedMessages);
        var threadIds = thread.Messages
            .Where(message => !string.IsNullOrWhiteSpace(message.MessageId))
            .Select(message => message.MessageId!)
            .ToHashSet(StringComparer.Ordinal);

        if (durableRemovedIds.Any(id => !threadIds.Contains(id)))
            throw new InvalidOperationException("Cannot compact thread history because at least one durable-removed message is not present in the thread.");

        var replacementMessages = EnsureReplacementMessages(plan.ReplacementMessages);

        var compactionId = Guid.NewGuid().ToString();
        var checkpoint = new ThreadHistoryCompactionCheckpointEvent(
            compactionId,
            GetMessageIds(plan.ModelCompactedMessages),
            GetMessageIds(plan.RetainedMessages),
            durableRemovedIds,
            replacementMessages,
            plan.Strategy.GetType().Name,
            plan.Retention.GetType().Name,
            plan.Boundary.GetType().Name,
            plan.SummaryContent,
            DateTimeOffset.UtcNow,
            durableRemovedIds.Count == 0
                ? ThreadHistoryCompactionMode.Soft
                : ThreadHistoryCompactionMode.Hard);
        var checkpointEvent = (ThreadHistoryCompactionCheckpointEvent)
            ThreadEventFactory.ThreadHistoryCompactionCheckpoint(thread.SessionId, thread.Id, checkpoint);

        if (durableRemovedIds.Count > 0)
            ApplyToLiveThread(thread, durableRemovedIds, replacementMessages);

        return new ThreadCompactionResult(
            compactionId,
            GetMessageIds(plan.ModelCompactedMessages),
            durableRemovedIds,
            GetMessageIds(replacementMessages),
            checkpointEvent);
    }

    private static void ApplyToLiveThread(
        Thread thread,
        IReadOnlyList<string> durableRemovedIds,
        IReadOnlyList<ChatMessage> replacementMessages)
    {
        var removed = durableRemovedIds.ToHashSet(StringComparer.Ordinal);
        var insertIndex = thread.Messages.FindIndex(message =>
            !string.IsNullOrWhiteSpace(message.MessageId) && removed.Contains(message.MessageId!));

        if (insertIndex < 0)
            insertIndex = thread.Messages.Count;

        thread.Messages.RemoveAll(message =>
            !string.IsNullOrWhiteSpace(message.MessageId) && removed.Contains(message.MessageId!));

        thread.Messages.InsertRange(insertIndex, replacementMessages);
        thread.LastActivity = DateTime.UtcNow;
    }

    private static IReadOnlyList<ChatMessage> EnsureReplacementMessages(IReadOnlyList<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            message.MessageId ??= Guid.NewGuid().ToString();
            message.CreatedAt ??= DateTimeOffset.UtcNow;
        }

        return messages;
    }

    private static IReadOnlyList<string> GetMessageIds(IEnumerable<ChatMessage> messages) =>
        messages
            .Select(message => message.MessageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();
}
