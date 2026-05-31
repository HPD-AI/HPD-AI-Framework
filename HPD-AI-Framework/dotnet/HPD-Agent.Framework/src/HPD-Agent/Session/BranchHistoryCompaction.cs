using Microsoft.Extensions.AI;

namespace HPD.Agent;

public sealed record BranchCompactionPlan(
    IReadOnlyList<ChatMessage> ModelCompactedMessages,
    IReadOnlyList<ChatMessage> DurableCompactedMessages,
    IReadOnlyList<ChatMessage> ReplacementMessages,
    CompactionStrategyOptions Strategy,
    CompactionRetentionOptions Retention,
    CompactionBoundaryOptions Boundary,
    string? SummaryContent);

public sealed record BranchCompactionResult(
    string CompactionId,
    IReadOnlyList<string> ModelCompactedMessageIds,
    IReadOnlyList<string> DurableCompactedMessageIds,
    IReadOnlyList<string> ReplacementMessageIds);

internal static class BranchHistoryCompactionMetadata
{
    public const string MessageTurnIdPropertyName = "hpd.messageTurnId";
}

public interface IBranchCompactionPlanner
{
    BranchCompactionPlan? Plan(
        Branch branch,
        CompactionResult compaction,
        CompactionRetentionOptions retention);
}

public interface IBranchHistoryCompactor
{
    Task<BranchCompactionResult> CompactAsync(
        Branch branch,
        BranchCompactionPlan plan,
        CancellationToken cancellationToken);
}

public sealed class BranchCompactionPlanner : IBranchCompactionPlanner
{
    public BranchCompactionPlan? Plan(
        Branch branch,
        CompactionResult compaction,
        CompactionRetentionOptions retention)
    {
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentNullException.ThrowIfNull(compaction);
        ArgumentNullException.ThrowIfNull(retention);

        if (retention is PreserveBranchHistoryOptions)
            return null;

        var boundary = retention switch
        {
            CompactBranchHistoryOptions compact => compact.Boundary,
            DeleteCompactedMessagesOptions delete => delete.Boundary,
            _ => new ExactCompactedMessagesBoundaryOptions()
        };

        var durableRemoved = ExpandBoundary(branch, compaction, boundary);
        var replacementMessages = retention is CompactBranchHistoryOptions
            ? compaction.ReplacementMessages.ToList()
            : [];

        return new BranchCompactionPlan(
            compaction.ModelCompactedMessages,
            durableRemoved,
            replacementMessages,
            compaction.Strategy,
            retention,
            boundary,
            compaction.SummaryContent);
    }

    private static IReadOnlyList<ChatMessage> ExpandBoundary(
        Branch branch,
        CompactionResult compaction,
        CompactionBoundaryOptions boundary)
    {
        var selectedIds = compaction.ModelCompactedMessages
            .Where(message => !string.IsNullOrWhiteSpace(message.MessageId))
            .Select(message => message.MessageId!)
            .ToHashSet(StringComparer.Ordinal);

        ApplyBoundary(branch, selectedIds, compaction, boundary);

        var retainedIds = compaction.RetainedMessages
            .Where(message => !string.IsNullOrWhiteSpace(message.MessageId))
            .Select(message => message.MessageId!)
            .ToHashSet(StringComparer.Ordinal);

        return branch.Messages
            .Where(message =>
                !string.IsNullOrWhiteSpace(message.MessageId) &&
                selectedIds.Contains(message.MessageId!) &&
                !retainedIds.Contains(message.MessageId!) &&
                message.Role != ChatRole.System)
            .ToList();
    }

    private static void ApplyBoundary(
        Branch branch,
        HashSet<string> selectedIds,
        CompactionResult compaction,
        CompactionBoundaryOptions boundary)
    {
        switch (boundary)
        {
            case ExactCompactedMessagesBoundaryOptions:
                return;

            case IncludeMessageTurnBoundaryOptions:
                IncludeMessageTurns(branch, selectedIds);
                return;

            case IncludeToolCallGroupBoundaryOptions:
                IncludeToolCallGroups(branch, selectedIds);
                return;

            case IncludePreviousMessagesBoundaryOptions previous:
                IncludePreviousMessages(branch, selectedIds, compaction, previous.Count);
                return;

            case CompositeCompactionBoundaryOptions composite:
                foreach (var child in composite.Policies)
                    ApplyBoundary(branch, selectedIds, compaction, child);
                return;
        }
    }

    private static void IncludePreviousMessages(
        Branch branch,
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

        var firstSelectedIndex = branch.Messages.FindIndex(message =>
            !string.IsNullOrWhiteSpace(message.MessageId) && selectedIds.Contains(message.MessageId!));

        if (firstSelectedIndex <= 0)
            return;

        for (var i = firstSelectedIndex - 1; i >= 0 && count > 0; i--)
        {
            var message = branch.Messages[i];
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
        Branch branch,
        HashSet<string> selectedIds)
    {
        var selectedTurnIds = branch.Messages
            .Where(message => !string.IsNullOrWhiteSpace(message.MessageId) && selectedIds.Contains(message.MessageId!))
            .Select(GetMessageTurnId)
            .Where(turnId => !string.IsNullOrWhiteSpace(turnId))
            .ToHashSet(StringComparer.Ordinal);

        if (selectedTurnIds.Count == 0)
            return;

        foreach (var message in branch.Messages)
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
        Branch branch,
        HashSet<string> selectedIds)
    {
        var selectedCallIds = branch.Messages
            .Where(message => !string.IsNullOrWhiteSpace(message.MessageId) && selectedIds.Contains(message.MessageId!))
            .SelectMany(GetToolCallIds)
            .ToHashSet(StringComparer.Ordinal);

        if (selectedCallIds.Count == 0)
            return;

        foreach (var message in branch.Messages)
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
            BranchHistoryCompactionMetadata.MessageTurnIdPropertyName,
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

public sealed class BranchHistoryCompactor : IBranchHistoryCompactor
{
    public async Task<BranchCompactionResult> CompactAsync(
        Branch branch,
        BranchCompactionPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentNullException.ThrowIfNull(plan);

        var durableRemovedIds = GetMessageIds(plan.DurableCompactedMessages);
        if (durableRemovedIds.Count == 0)
        {
            return new BranchCompactionResult(Guid.NewGuid().ToString(), [], [], []);
        }

        var branchIds = branch.Messages
            .Where(message => !string.IsNullOrWhiteSpace(message.MessageId))
            .Select(message => message.MessageId!)
            .ToHashSet(StringComparer.Ordinal);

        if (durableRemovedIds.Any(id => !branchIds.Contains(id)))
            throw new InvalidOperationException("Cannot compact branch history because at least one durable-removed message is not present in the branch.");

        var replacementMessages = plan.Retention is CompactBranchHistoryOptions
            ? EnsureReplacementMessages(plan.ReplacementMessages)
            : [];

        var compactionId = Guid.NewGuid().ToString();
        var evt = new BranchHistoryCompactedEvent(
            compactionId,
            GetMessageIds(plan.ModelCompactedMessages),
            durableRemovedIds,
            replacementMessages,
            plan.Strategy.GetType().Name,
            plan.Retention.GetType().Name,
            plan.Boundary.GetType().Name,
            plan.SummaryContent,
            DateTimeOffset.UtcNow);

        ApplyToLiveBranch(branch, durableRemovedIds, replacementMessages);

        if (branch.Session?.Store is { } store)
        {
            await store.AppendBranchEventAsync(
                branch.SessionId,
                branch.Id,
                BranchEventFactory.BranchHistoryCompacted(branch.SessionId, branch.Id, evt),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return new BranchCompactionResult(
            compactionId,
            GetMessageIds(plan.ModelCompactedMessages),
            durableRemovedIds,
            GetMessageIds(replacementMessages));
    }

    private static void ApplyToLiveBranch(
        Branch branch,
        IReadOnlyList<string> durableRemovedIds,
        IReadOnlyList<ChatMessage> replacementMessages)
    {
        var removed = durableRemovedIds.ToHashSet(StringComparer.Ordinal);
        var insertIndex = branch.Messages.FindIndex(message =>
            !string.IsNullOrWhiteSpace(message.MessageId) && removed.Contains(message.MessageId!));

        if (insertIndex < 0)
            insertIndex = branch.Messages.Count;

        branch.Messages.RemoveAll(message =>
            !string.IsNullOrWhiteSpace(message.MessageId) && removed.Contains(message.MessageId!));

        branch.Messages.InsertRange(insertIndex, replacementMessages);
        branch.LastActivity = DateTime.UtcNow;
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
