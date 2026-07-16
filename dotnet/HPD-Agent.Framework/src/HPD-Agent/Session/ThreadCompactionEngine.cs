using Microsoft.Extensions.AI;

namespace HPD.Agent;

public enum CompactionStatus
{
    Started,
    Skipped,
    Failed,
    Completed
}

public enum CompactionOrigin
{
    Automatic,
    Explicit,
    Fork
}

public sealed record CompactionEvent(
    string AgentName,
    int Iteration,
    CompactionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int? OriginalMessageCount = null,
    int? CompactedMessageCount = null,
    int? MessagesRemoved = null,
    string? SummaryContent = null,
    string? Reason = null,
    string? Strategy = null,
    CompactionContinuation Continuation = CompactionContinuation.Continue,
    CompactionOrigin Origin = CompactionOrigin.Automatic) : AgentEvent;

public sealed record ThreadCompactionContext(
    Thread Thread,
    IReadOnlyList<ChatMessage> ModelHistory,
    IThreadEventPublisher? Publisher,
    IChatClient? SummarizerClient,
    IThreadJournalRebaseSeedProvider? RebaseSeedProvider = null);

/// <summary>
/// Supplies newly encoded authoritative control facts that must survive a destructive journal rebase.
/// </summary>
public interface IThreadJournalRebaseSeedProvider
{
    ValueTask<IReadOnlyList<AgentEvent>> CreateSeedEventsAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default);
}

public sealed record PreparedThreadCompaction(
    string CompactionId,
    ThreadJournalCursor? ExpectedCursor,
    CompactionSpecification Specification,
    IReadOnlyList<string> CompactedMessageIds,
    IReadOnlyList<string> PreservedMessageIds,
    IReadOnlyList<string> CarriedUserMessageSourceIds,
    IReadOnlyList<string> AfterPointMessageIds,
    IReadOnlyList<ChatMessage> ResultingMessages,
    ThreadHistoryCompactionCheckpointEvent Checkpoint);

public sealed record ThreadCompactionCommitResult(
    PreparedThreadCompaction Compaction,
    AgentEvent CommittedCheckpoint);

public sealed record ThreadCompactionExecutionResult(
    PreparedThreadCompaction? Compaction,
    ThreadCompactionCommitResult? Commit,
    CompactionEvent TerminalEvent);

public interface IThreadCompactionEngine
{
    ValueTask<ThreadCompactionExecutionResult> ExecuteAsync(
        ThreadCompactionContext context,
        CompactionSpecification specification,
        string agentName,
        int iteration,
        CompactionOrigin origin,
        CompactionContinuation continuation,
        CancellationToken cancellationToken = default);

    ValueTask<PreparedThreadCompaction?> PrepareAsync(
        ThreadCompactionContext context,
        CompactionSpecification specification,
        CancellationToken cancellationToken = default);

    ValueTask<ThreadCompactionCommitResult> CommitAsync(
        ThreadCompactionContext context,
        PreparedThreadCompaction compaction,
        CancellationToken cancellationToken = default);
}

public sealed class ThreadCompactionEngine : IThreadCompactionEngine
{
    private const string DefaultInstructions = """
        Produce a continuation handoff for another agent. Do not reply conversationally.

        Use exactly these headings:
        ## Goal
        ## User constraints and preferences
        ## Decisions made
        ## Work completed
        ## Current state
        ## Important files, symbols, commands, and results
        ## Failures and rejected approaches
        ## Remaining work
        ## Exact next action

        Preserve concrete paths, symbols, commands, errors, tests, results, and applicable
        tool-derived findings. Incorporate an earlier handoff as authoritative context. Do not
        invent information, critique the prior work, end with a question, or offer more help.
        """;

    public async ValueTask<ThreadCompactionExecutionResult> ExecuteAsync(
        ThreadCompactionContext context,
        CompactionSpecification specification,
        string agentName,
        int iteration,
        CompactionOrigin origin,
        CompactionContinuation continuation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        var startedAt = DateTimeOffset.UtcNow;
        await PublishLifecycleAsync(context, new CompactionEvent(
            agentName,
            iteration,
            CompactionStatus.Started,
            startedAt,
            startedAt,
            OriginalMessageCount: context.ModelHistory.Count,
            Strategy: GetStrategyKind(specification.Strategy),
            Continuation: continuation,
            Origin: origin), cancellationToken).ConfigureAwait(false);

        try
        {
            var prepared = await PrepareAsync(context, specification, cancellationToken).ConfigureAwait(false);
            if (prepared is null)
            {
                var skipped = new CompactionEvent(
                    agentName,
                    iteration,
                    CompactionStatus.Skipped,
                    startedAt,
                    DateTimeOffset.UtcNow,
                    OriginalMessageCount: context.ModelHistory.Count,
                    CompactedMessageCount: context.ModelHistory.Count,
                    MessagesRemoved: 0,
                    Reason: "The selected compaction range was empty.",
                    Strategy: GetStrategyKind(specification.Strategy),
                    Continuation: continuation,
                    Origin: origin);
                await PublishLifecycleAsync(context, skipped, cancellationToken).ConfigureAwait(false);
                return new ThreadCompactionExecutionResult(null, null, skipped);
            }

            var commit = await CommitAsync(context, prepared, cancellationToken).ConfigureAwait(false);
            var completed = new CompactionEvent(
                agentName,
                iteration,
                CompactionStatus.Completed,
                startedAt,
                DateTimeOffset.UtcNow,
                OriginalMessageCount: context.ModelHistory.Count,
                CompactedMessageCount: prepared.ResultingMessages.Count,
                MessagesRemoved: prepared.CompactedMessageIds.Count,
                SummaryContent: GetSummary(prepared),
                Strategy: GetStrategyKind(specification.Strategy),
                Continuation: continuation,
                Origin: origin);
            await PublishLifecycleAsync(context, completed, cancellationToken).ConfigureAwait(false);
            return new ThreadCompactionExecutionResult(prepared, commit, completed);
        }
        catch (Exception error)
        {
            var failed = new CompactionEvent(
                agentName,
                iteration,
                CompactionStatus.Failed,
                startedAt,
                DateTimeOffset.UtcNow,
                OriginalMessageCount: context.ModelHistory.Count,
                Reason: error.Message,
                Strategy: GetStrategyKind(specification.Strategy),
                Continuation: continuation,
                Origin: origin);
            await PublishLifecycleAsync(context, failed, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask<PreparedThreadCompaction?> PrepareAsync(
        ThreadCompactionContext context,
        CompactionSpecification specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(specification);

        ValidateSpecification(specification);
        var expectedCursor = context.Publisher is null
            ? null
            : (await context.Publisher.GetHeadAsync(
                new ThreadKey(context.Thread.SessionId, context.Thread.Id),
                cancellationToken).ConfigureAwait(false))?.Cursor;
        var requestedGeneration = specification.Point switch
        {
            CompactAtMessage message => message.ExpectedJournalGeneration,
            CompactAtTurn turn => turn.ExpectedJournalGeneration,
            _ => null
        };
        if (requestedGeneration is long generation &&
            (expectedCursor is not ThreadJournalCursor actual || actual.Generation != generation))
        {
            var key = new ThreadKey(context.Thread.SessionId, context.Thread.Id);
            throw new ThreadCursorConflictException(
                key,
                ThreadJournalCursor.Start(generation),
                expectedCursor ?? ThreadJournalCursor.Start(0));
        }
        var messages = context.ModelHistory.Where(static message => message.Role != ChatRole.System).ToList();
        EnsureMessageIdentity(messages);
        if (messages.Count == 0)
            return null;

        var pointIndex = ResolvePoint(messages, specification.Point);
        if (pointIndex <= 0)
            return null;

        var beforePoint = messages.Take(pointIndex).ToList();
        var afterPoint = messages.Skip(pointIndex).ToList();
        var (selected, preserved, carriedUsers) = SelectHistory(beforePoint, specification.Preservation);
        if (selected.Count == 0)
            return null;

        var replacements = specification.Strategy switch
        {
            RemovalCompaction => Array.Empty<ChatMessage>(),
            SummarizingCompaction summarizing =>
                [await SummarizeAsync(context, selected, summarizing, cancellationToken).ConfigureAwait(false)],
            _ => throw new ArgumentOutOfRangeException(nameof(specification), "Unknown compaction strategy.")
        };

        var carriedCopies = carriedUsers.Select(CloneCarriedUserMessage).ToList();
        var result = carriedCopies
            .Concat(replacements)
            .Concat(preserved)
            .Concat(afterPoint)
            .ToList();

        var compactionId = Guid.NewGuid().ToString("N");
        var checkpoint = (ThreadHistoryCompactionCheckpointEvent)ThreadEventFactory
            .ThreadHistoryCompactionCheckpoint(
                context.Thread.SessionId,
                context.Thread.Id,
                new ThreadHistoryCompactionCheckpointEvent(
                    compactionId,
                    CompactionPointDescriptor.From(specification.Point),
                    CompactionPreservationDescriptor.From(specification.Preservation),
                    GetMessageIds(selected),
                    GetMessageIds(preserved),
                    GetMessageIds(carriedUsers),
                    GetMessageIds(afterPoint),
                    replacements,
                    CompactionStrategyDescriptor.From(specification.Strategy),
                    specification.CommitMode,
                    DateTimeOffset.UtcNow));

        return new PreparedThreadCompaction(
            compactionId,
            expectedCursor,
            specification,
            GetMessageIds(selected),
            GetMessageIds(preserved),
            GetMessageIds(carriedUsers),
            GetMessageIds(afterPoint),
            result,
            checkpoint);
    }

    public async ValueTask<ThreadCompactionCommitResult> CommitAsync(
        ThreadCompactionContext context,
        PreparedThreadCompaction compaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(compaction);

        AgentEvent committed;
        if (context.Publisher is null)
        {
            committed = compaction.Checkpoint;
        }
        else if (compaction.Specification.CommitMode == CompactionCommitMode.Hard)
        {
            if (compaction.ExpectedCursor is not ThreadJournalCursor expectedCursor)
                throw new InvalidOperationException("Hard compaction requires an existing canonical journal.");
            var key = new ThreadKey(context.Thread.SessionId, context.Thread.Id);
            var seedEvents = context.RebaseSeedProvider is null
                ? []
                : await context.RebaseSeedProvider.CreateSeedEventsAsync(key, cancellationToken)
                    .ConfigureAwait(false);
            var replacement = ThreadJournalEncoder.Encode(
                context.Thread,
                compaction.ResultingMessages,
                [compaction.Checkpoint, .. seedEvents]);
            var result = await context.Publisher.ReplaceAndPublishAsync(
                key,
                replacement,
                expectedCursor,
                cancellationToken).ConfigureAwait(false);
            committed = result.CommittedEvents.Single(evt => evt.EventId == compaction.Checkpoint.EventId);
        }
        else
        {
            var result = await context.Publisher.CommitAndPublishAsync(
                new ThreadKey(context.Thread.SessionId, context.Thread.Id),
                [compaction.Checkpoint],
                new ThreadAppendCondition(compaction.ExpectedCursor),
                cancellationToken).ConfigureAwait(false);
            committed = result.CommittedEvents[0];
        }

        context.Thread.Messages.Clear();
        context.Thread.Messages.AddRange(compaction.ResultingMessages);
        context.Thread.LastActivity = DateTime.UtcNow;
        return new ThreadCompactionCommitResult(compaction, committed);
    }

    private static void ValidateSpecification(CompactionSpecification specification)
    {
        switch (specification.Preservation)
        {
            case PreservePreviousTurns { Count: < 0 }:
                throw new ArgumentOutOfRangeException(nameof(specification), "Preserved turn count cannot be negative.");
            case PreservePreviousUserMessages { Limit: PreviousItemCountLimit { Count: < 0 } }:
                throw new ArgumentOutOfRangeException(nameof(specification), "Preserved user-message count cannot be negative.");
            case PreservePreviousUserMessages { Limit: PreviousTokenBudgetLimit { Tokens: < 0 } }:
                throw new ArgumentOutOfRangeException(nameof(specification), "Preserved user-message token budget cannot be negative.");
        }
    }

    private static int ResolvePoint(IReadOnlyList<ChatMessage> messages, CompactionPoint point)
    {
        if (point is CompactAtCurrentHead)
            return messages.Count;

        var index = point switch
        {
            CompactAtMessage message => FindMessage(messages, message.MessageId),
            CompactAtTurn turn => FindTurn(messages, turn.TurnId),
            _ => throw new ArgumentOutOfRangeException(nameof(point), "Unknown compaction point.")
        };

        if (index < 0)
            throw new InvalidOperationException("The requested compaction point does not exist in this thread generation.");

        var turnId = GetTurnId(messages[index]);
        if (string.IsNullOrWhiteSpace(turnId))
            return index;

        while (index > 0 && string.Equals(GetTurnId(messages[index - 1]), turnId, StringComparison.Ordinal))
            index--;
        return index;
    }

    private static int FindMessage(IReadOnlyList<ChatMessage> messages, string messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            throw new ArgumentException("Message ID is required.", nameof(messageId));
        for (var i = 0; i < messages.Count; i++)
        {
            if (string.Equals(messages[i].MessageId, messageId, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    private static int FindTurn(IReadOnlyList<ChatMessage> messages, string turnId)
    {
        if (string.IsNullOrWhiteSpace(turnId))
            throw new ArgumentException("Turn ID is required.", nameof(turnId));
        for (var i = 0; i < messages.Count; i++)
        {
            if (string.Equals(GetTurnId(messages[i]), turnId, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    private static (List<ChatMessage> Selected, List<ChatMessage> Preserved, List<ChatMessage> CarriedUsers)
        SelectHistory(IReadOnlyList<ChatMessage> beforePoint, CompactionPreservation preservation)
    {
        return preservation switch
        {
            PreserveNoPreviousHistory => (beforePoint.ToList(), [], []),
            PreservePreviousTurns turns => PreserveTurns(beforePoint, turns.Count),
            PreservePreviousUserMessages users => PreserveUsers(beforePoint, users.Limit),
            _ => throw new ArgumentOutOfRangeException(nameof(preservation), "Unknown preservation policy.")
        };
    }

    private static (List<ChatMessage>, List<ChatMessage>, List<ChatMessage>) PreserveTurns(
        IReadOnlyList<ChatMessage> messages,
        int count)
    {
        if (count == 0)
            return (messages.ToList(), [], []);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var boundary = messages.Count;
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var turn = GetTurnId(messages[i]) ?? $"message:{messages[i].MessageId}";
            seen.Add(turn);
            boundary = i;
            if (seen.Count == count && (i == 0 || !string.Equals(GetTurnId(messages[i - 1]), GetTurnId(messages[i]), StringComparison.Ordinal)))
                break;
        }
        return (messages.Take(boundary).ToList(), messages.Skip(boundary).ToList(), []);
    }

    private static (List<ChatMessage>, List<ChatMessage>, List<ChatMessage>) PreserveUsers(
        IReadOnlyList<ChatMessage> messages,
        PreviousHistoryLimit limit)
    {
        var carried = new Stack<ChatMessage>();
        long used = 0;
        foreach (var message in messages.Reverse())
        {
            if (message.Role != ChatRole.User)
                continue;
            if (limit is PreviousItemCountLimit count && carried.Count >= count.Count)
                break;
            var tokens = EstimateTokens(message);
            if (limit is PreviousTokenBudgetLimit budget && carried.Count > 0 && used + tokens > budget.Tokens)
                break;
            carried.Push(message);
            used += tokens;
        }
        return (messages.ToList(), [], carried.ToList());
    }

    private static async Task<ChatMessage> SummarizeAsync(
        ThreadCompactionContext context,
        IReadOnlyList<ChatMessage> selected,
        SummarizingCompaction strategy,
        CancellationToken cancellationToken)
    {
        var client = context.SummarizerClient
            ?? throw new InvalidOperationException("Summarizing compaction requires a chat client.");
        var prompt = new ChatMessage(ChatRole.System,
            string.IsNullOrWhiteSpace(strategy.Instructions) ? DefaultInstructions : strategy.Instructions);
        var response = await client.GetResponseAsync([prompt, .. selected], cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var text = response.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text) || !text.Contains("## Exact next action", StringComparison.Ordinal))
            throw new InvalidOperationException("The compaction summarizer returned an invalid continuation handoff.");
        return new ChatMessage(ChatRole.Assistant, text)
        {
            MessageId = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static ChatMessage CloneCarriedUserMessage(ChatMessage source) =>
        new(source.Role, source.Contents.ToArray())
        {
            MessageId = Guid.NewGuid().ToString("N"),
            AuthorName = source.AuthorName,
            CreatedAt = DateTimeOffset.UtcNow,
            AdditionalProperties = source.AdditionalProperties is null
                ? null
                : new AdditionalPropertiesDictionary(source.AdditionalProperties)
        };

    private static void EnsureMessageIdentity(IEnumerable<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            message.MessageId ??= Guid.NewGuid().ToString("N");
            message.CreatedAt ??= DateTimeOffset.UtcNow;
        }
    }

    private static string? GetTurnId(ChatMessage message) =>
        message.AdditionalProperties?.TryGetValue<string>("hpd.messageTurnId", out var turnId) == true
            ? turnId
            : null;

    private static IReadOnlyList<string> GetMessageIds(IEnumerable<ChatMessage> messages) =>
        messages.Select(static message => message.MessageId)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id!)
            .ToArray();

    private static long EstimateTokens(ChatMessage message) =>
        Math.Max(1, (long)Math.Ceiling(message.Contents.OfType<TextContent>()
            .Sum(static content => content.Text?.Length ?? 0) / 4d));

    private static ValueTask PublishLifecycleAsync(
        ThreadCompactionContext context,
        CompactionEvent evt,
        CancellationToken cancellationToken) =>
        context.Publisher is null
            ? ValueTask.CompletedTask
            : new ValueTask(context.Publisher.CommitAndPublishAsync(
                new ThreadKey(context.Thread.SessionId, context.Thread.Id),
                evt,
                cancellationToken).AsTask());

    private static string GetStrategyKind(CompactionStrategy strategy) => strategy switch
    {
        RemovalCompaction => "removal",
        SummarizingCompaction => "summarizing",
        _ => throw new ArgumentOutOfRangeException(nameof(strategy))
    };

    private static string? GetSummary(PreparedThreadCompaction prepared) =>
        prepared.Checkpoint.ReplacementMessages
            .SelectMany(static message => message.Contents)
            .OfType<TextContent>()
            .Select(static content => content.Text)
            .FirstOrDefault(static text => !string.IsNullOrWhiteSpace(text));
}
