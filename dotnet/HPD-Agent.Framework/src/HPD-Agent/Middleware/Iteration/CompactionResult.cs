using Microsoft.Extensions.AI;

namespace HPD.Agent;

public sealed record CompactionResult(
    IReadOnlyList<ChatMessage> OriginalMessages,
    IReadOnlyList<ChatMessage> ModelVisibleMessages,
    IReadOnlyList<ChatMessage> ModelCompactedMessages,
    IReadOnlyList<ChatMessage> RetainedMessages,
    IReadOnlyList<ChatMessage> ReplacementMessages,
    CompactionStrategyOptions Strategy)
{
    public string? SummaryContent =>
        ReplacementMessages.FirstOrDefault(message => message.Role == ChatRole.Assistant)?.Text;

    public static CompactionResult FromOriginalAndCompacted(
        IReadOnlyList<ChatMessage> originalMessages,
        IReadOnlyList<ChatMessage> modelVisibleMessages,
        CompactionStrategyOptions strategy)
    {
        foreach (var message in originalMessages.Concat(modelVisibleMessages))
            EnsureMessageIdentity(message);

        var originalIds = originalMessages
            .Where(message => !string.IsNullOrWhiteSpace(message.MessageId))
            .ToDictionary(message => message.MessageId!, message => message);

        var retained = new List<ChatMessage>();
        var replacement = new List<ChatMessage>();

        foreach (var message in modelVisibleMessages)
        {
            if (!string.IsNullOrWhiteSpace(message.MessageId) && originalIds.ContainsKey(message.MessageId!))
                retained.Add(message);
            else
                replacement.Add(message);
        }

        var retainedIds = retained
            .Where(message => !string.IsNullOrWhiteSpace(message.MessageId))
            .Select(message => message.MessageId!)
            .ToHashSet(StringComparer.Ordinal);

        var compacted = originalMessages
            .Where(message => string.IsNullOrWhiteSpace(message.MessageId) || !retainedIds.Contains(message.MessageId!))
            .ToList();

        return new CompactionResult(
            originalMessages.ToList(),
            modelVisibleMessages.ToList(),
            compacted,
            retained,
            replacement,
            strategy);
    }

    private static void EnsureMessageIdentity(ChatMessage message)
    {
        message.MessageId ??= Guid.NewGuid().ToString();
        message.CreatedAt ??= DateTimeOffset.UtcNow;
    }
}

public interface ICompactionStrategy
{
    Task<CompactionResult> ReduceAsync(
        IReadOnlyList<ChatMessage> originalMessages,
        CancellationToken cancellationToken);
}

public sealed class ChatReducerCompactionStrategy : ICompactionStrategy
{
    private readonly Func<IReadOnlyList<ChatMessage>, IChatReducer> _reducerFactory;
    private readonly CompactionStrategyOptions _options;

    public ChatReducerCompactionStrategy(
        IChatReducer reducer,
        CompactionStrategyOptions options)
        : this(_ => reducer, options)
    {
    }

    public ChatReducerCompactionStrategy(
        Func<IReadOnlyList<ChatMessage>, IChatReducer> reducerFactory,
        CompactionStrategyOptions options)
    {
        _reducerFactory = reducerFactory;
        _options = options;
    }

    public async Task<CompactionResult> ReduceAsync(
        IReadOnlyList<ChatMessage> originalMessages,
        CancellationToken cancellationToken)
    {
        var reducer = _reducerFactory(originalMessages);
        var compacted = await reducer.ReduceAsync(originalMessages, cancellationToken).ConfigureAwait(false);
        var result = CompactionResult.FromOriginalAndCompacted(
            originalMessages,
            compacted?.ToList() ?? originalMessages,
            _options);

        return _options is SummarizingCompactionOptions summarizingOptions
            ? CompactionMementoBuilder.Apply(result, summarizingOptions)
            : result;
    }
}

internal static class CompactionMementoBuilder
{
    internal const string MementoPropertyName = "hpd.compaction.memento";
    internal const string SourceMessageIdPropertyName = "hpd.compaction.sourceMessageId";
    private const string HandoffPrefix = "Conversation handoff summary for continuation:";

    public static CompactionResult Apply(
        CompactionResult result,
        SummarizingCompactionOptions options)
    {
        if (options.SummaryStyle != SummaryStyle.Handoff)
            return result;

        var modelVisible = options.Memory.FilterGeneratedContextWrappers
            ? result.ModelVisibleMessages.Where(message => !IsMementoMessage(message)).ToList()
            : result.ModelVisibleMessages.ToList();

        var originalIds = result.OriginalMessages
            .Where(message => !string.IsNullOrWhiteSpace(message.MessageId))
            .Select(message => message.MessageId!)
            .ToHashSet(StringComparer.Ordinal);

        var retainedMessages = modelVisible
            .Where(message => !string.IsNullOrWhiteSpace(message.MessageId) && originalIds.Contains(message.MessageId!))
            .ToList();

        var summaryMessage = result.ReplacementMessages
            .FirstOrDefault(message => message.Role == ChatRole.Assistant && !IsMementoMessage(message))
            ?? modelVisible.FirstOrDefault(message =>
                message.Role == ChatRole.Assistant &&
                !IsMementoMessage(message) &&
                (string.IsNullOrWhiteSpace(message.MessageId) || !originalIds.Contains(message.MessageId!)));

        var replacementMessages = new List<ChatMessage>();
        if (options.Memory.ReinjectCurrentContextAfterCompaction)
            replacementMessages.Add(CreateCurrentContextBoundary());

        if (options.Memory.PreserveRecentUserMessagesSeparately)
            replacementMessages.AddRange(CloneRecentCompactedUserMessages(result, options.Memory.RecentUserMessageTokenBudget));

        if (summaryMessage is not null)
            replacementMessages.Add(CreateHandoffSummary(summaryMessage));

        var replacementIds = replacementMessages
            .Where(message => !string.IsNullOrWhiteSpace(message.MessageId))
            .Select(message => message.MessageId!)
            .ToHashSet(StringComparer.Ordinal);

        var finalModelVisible = replacementMessages
            .Concat(retainedMessages)
            .Where(message =>
                !options.Memory.FilterGeneratedContextWrappers ||
                !IsMementoMessage(message) ||
                (!string.IsNullOrWhiteSpace(message.MessageId) && replacementIds.Contains(message.MessageId!)))
            .ToList();

        return CompactionResult.FromOriginalAndCompacted(
            result.OriginalMessages,
            finalModelVisible,
            options);
    }

    private static IEnumerable<ChatMessage> CloneRecentCompactedUserMessages(
        CompactionResult result,
        int tokenBudget)
    {
        if (tokenBudget <= 0)
            yield break;

        var selected = new Stack<ChatMessage>();
        var usedTokens = 0;

        foreach (var message in result.ModelCompactedMessages.Reverse())
        {
            if (message.Role != ChatRole.User || IsMementoMessage(message))
                continue;

            var estimatedTokens = EstimateTokens(message);
            if (usedTokens > 0 && usedTokens + estimatedTokens > tokenBudget)
                break;

            selected.Push(message);
            usedTokens += estimatedTokens;
        }

        while (selected.Count > 0)
            yield return CloneAsMemento(selected.Pop());
    }

    private static ChatMessage CloneAsMemento(ChatMessage message)
    {
        var clone = new ChatMessage(message.Role, message.Contents.ToArray())
        {
            MessageId = Guid.NewGuid().ToString(),
            AuthorName = message.AuthorName,
            CreatedAt = DateTimeOffset.UtcNow,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [MementoPropertyName] = true,
                [SourceMessageIdPropertyName] = message.MessageId
            }
        };

        return clone;
    }

    private static ChatMessage CreateCurrentContextBoundary() =>
        new(ChatRole.System, "The earlier conversation has been compacted. Treat the following memento messages as current context for continuing the task.")
        {
            MessageId = Guid.NewGuid().ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [MementoPropertyName] = true
            }
        };

    private static ChatMessage CreateHandoffSummary(ChatMessage summaryMessage)
    {
        var summaryText = summaryMessage.Text ?? string.Empty;
        var text = summaryText.StartsWith(HandoffPrefix, StringComparison.Ordinal)
            ? summaryText
            : $"{HandoffPrefix}{System.Environment.NewLine}{System.Environment.NewLine}{summaryText}";

        return new ChatMessage(ChatRole.Assistant, text)
        {
            MessageId = Guid.NewGuid().ToString(),
            AuthorName = summaryMessage.AuthorName,
            CreatedAt = DateTimeOffset.UtcNow,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [MementoPropertyName] = true,
                [SourceMessageIdPropertyName] = summaryMessage.MessageId
            }
        };
    }

    private static bool IsMementoMessage(ChatMessage message) =>
        message.AdditionalProperties?.TryGetValue(MementoPropertyName, out var value) == true &&
        value is true;

    private static int EstimateTokens(ChatMessage message)
    {
        var textLength = message.Contents.OfType<TextContent>().Sum(content => content.Text?.Length ?? 0);
        return Math.Max(1, (int)Math.Ceiling(textLength / 4.0));
    }
}

public enum CompactionTriggerReason
{
    None,
    ExplicitRunTrigger,
    CountThreshold,
    ContextWindowThreshold,
    Composite
}

public sealed record CompactionTriggerDecision(
    bool ShouldReduce,
    CompactionTriggerReason Reason,
    int? CurrentCount,
    long? LastInputTokens,
    string? Description);
