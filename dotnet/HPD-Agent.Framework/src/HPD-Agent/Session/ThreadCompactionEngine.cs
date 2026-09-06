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

[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("COMPACTION")]
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

/// <summary>Provides resolved thread, model history, and summarizer dependencies for one compaction.</summary>
/// <param name="Thread">The thread being compacted.</param>
/// <param name="ModelHistory">The model-visible history selected for compaction.</param>
/// <param name="Publisher">The optional durable thread-event publisher.</param>
/// <param name="SummarizerClient">The resolved specialized Chat client, when summarization is required.</param>
/// <param name="RebaseSeedProvider">The optional provider of control facts preserved across destructive rebases.</param>
/// <param name="SummarizerOptions">The compiled request options for the resolved summarizer.</param>
public sealed record ThreadCompactionContext(
    Thread Thread,
    IReadOnlyList<ChatMessage> ModelHistory,
    IAgentEventPublisher? Publisher,
    IChatClient? SummarizerClient,
    IThreadJournalRebaseSeedProvider? RebaseSeedProvider = null,
    ChatOptions? SummarizerOptions = null);

/// <summary>
/// Supplies newly encoded authoritative control facts that must survive a destructive journal rebase.
/// </summary>
public interface IThreadJournalRebaseSeedProvider
{
    ValueTask<IReadOnlyList<AgentEvent>> CreateSeedEventsAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default);
}

/// <summary>Combines structural seed contributors in deterministic framework order.</summary>
public sealed class CompositeThreadJournalRebaseSeedProvider(
    IReadOnlyList<IThreadJournalRebaseSeedProvider> providers) : IThreadJournalRebaseSeedProvider
{
    private readonly IReadOnlyList<IThreadJournalRebaseSeedProvider> _providers =
        providers ?? throw new ArgumentNullException(nameof(providers));

    /// <summary>Creates the framework registry contributor plus an optional host contributor.</summary>
    public static IThreadJournalRebaseSeedProvider Create(
        ISessionStore store,
        IThreadJournalRebaseSeedProvider? hostProvider = null)
    {
        var registry = new SubAgentRegistryRebaseSeedProvider(new SubAgentChildRegistry(store));
        var forks = new ThreadForkOperationRebaseSeedProvider(store);
        var continuations = new SubAgentContinuationRebaseSeedProvider(store);
        var controllerAuthorities = new SubAgentControllerAuthorityRebaseSeedProvider(store);
        return hostProvider is null
            ? new CompositeThreadJournalRebaseSeedProvider([registry, forks, continuations, controllerAuthorities])
            : new CompositeThreadJournalRebaseSeedProvider([hostProvider, registry, forks, continuations, controllerAuthorities]);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentEvent>> CreateSeedEventsAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default)
    {
        var events = new List<AgentEvent>();
        foreach (var provider in _providers)
            events.AddRange(await provider.CreateSeedEventsAsync(thread, cancellationToken).ConfigureAwait(false));
        return events;
    }
}

/// <summary>Preserves the latest exact child/controller authority through journal rebase.</summary>
public sealed class SubAgentControllerAuthorityRebaseSeedProvider(ISessionStore store)
    : IThreadJournalRebaseSeedProvider
{
    private readonly ISessionStore _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentEvent>> CreateSeedEventsAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default)
    {
        var head = await _store.GetThreadEventHeadAsync(thread, cancellationToken).ConfigureAwait(false);
        if (head is null) return [];
        var latest = new Dictionary<(ThreadKey Controller, SubAgentLocalId LocalId), SubAgentChildControllerAuthorityEvent>();
        await foreach (var batch in _store.ReadThreadEventsAsync(
                           thread,
                           new ThreadEventReadRequest(ThreadJournalCursor.Start(head.Generation), head.ThreadSequenceNumber),
                           cancellationToken).ConfigureAwait(false))
            foreach (var evt in batch.Events)
                if (evt is SubAgentChildControllerAuthorityEvent authority)
                    latest[(authority.Controller, authority.LocalId)] = authority;
        return latest.Values
            .OrderBy(static authority => authority.Controller.SessionId, StringComparer.Ordinal)
            .ThenBy(static authority => authority.Controller.ThreadId, StringComparer.Ordinal)
            .ThenBy(static authority => authority.LocalId.Value, StringComparer.Ordinal)
            .Select(authority => (AgentEvent)(authority with
            {
                SessionId = thread.SessionId,
                ThreadId = thread.ThreadId,
                ThreadSequenceNumber = 0
            }))
            .ToArray();
    }
}

/// <summary>
/// Preserves deterministic subagent-continuation admission and terminal receipts across a destructive rebase.
/// </summary>
public sealed class SubAgentContinuationRebaseSeedProvider(ISessionStore store)
    : IThreadJournalRebaseSeedProvider
{
    private readonly ISessionStore _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentEvent>> CreateSeedEventsAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default)
    {
        var head = await _store.GetThreadEventHeadAsync(thread, cancellationToken).ConfigureAwait(false);
        if (head is null) return [];
        var starts = new Dictionary<string, ThreadExecutionStartedEvent>(StringComparer.Ordinal);
        var terminals = new Dictionary<string, ThreadExecutionFinishedEvent>(StringComparer.Ordinal);
        var receipts = new Dictionary<string, SubAgentContinuationReceiptEvent>(StringComparer.Ordinal);
        await foreach (var batch in _store.ReadThreadEventsAsync(
                           thread,
                           new ThreadEventReadRequest(ThreadJournalCursor.Start(head.Generation), head.ThreadSequenceNumber),
                           cancellationToken).ConfigureAwait(false))
        {
            foreach (var evt in batch.Events)
            {
                if (evt is ThreadExecutionStartedEvent started &&
                    started.ThreadExecutionId.StartsWith("continue-", StringComparison.Ordinal))
                    starts[started.ThreadExecutionId] = started;
                else if (evt is ThreadExecutionFinishedEvent finished &&
                         finished.ThreadExecutionId.StartsWith("continue-", StringComparison.Ordinal))
                    terminals[finished.ThreadExecutionId] = finished;
                else if (evt is SubAgentContinuationReceiptEvent receipt)
                    receipts[receipt.ContinuationExecutionId] = receipt;
            }
        }
        var seed = new List<AgentEvent>(starts.Count * 2);
        foreach (var (executionId, started) in starts.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            seed.Add(started with
            {
                SessionId = thread.SessionId,
                ThreadId = thread.ThreadId,
                ThreadSequenceNumber = 0
            });
            if (terminals.TryGetValue(executionId, out var terminal))
            {
                if (receipts.TryGetValue(executionId, out var receipt))
                {
                    seed.Add(receipt with
                    {
                        SessionId = thread.SessionId,
                        ThreadId = thread.ThreadId,
                        ThreadSequenceNumber = 0
                    });
                }
                seed.Add(terminal with
                {
                    SessionId = thread.SessionId,
                    ThreadId = thread.ThreadId,
                    ThreadSequenceNumber = 0
                });
            }
        }
        return seed;
    }
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
        invent information, critique the prior work, end with a question, offer more help, call
        tools, or emit tool-call protocol markup.
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

        cancellationToken.ThrowIfCancellationRequested();
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

    /// <summary>Creates a text handoff only after rejecting errors, incomplete generation, and tool-dependent output.</summary>
    /// <remarks>Providers own protocol completion evidence. An absent MEAI finish reason alone is not a failure.</remarks>
    private static async Task<ChatMessage> SummarizeAsync(
        ThreadCompactionContext context,
        IReadOnlyList<ChatMessage> selected,
        SummarizingCompaction strategy,
        CancellationToken cancellationToken)
    {
        var client = context.SummarizerClient
            ?? throw new InvalidOperationException("Summarizing compaction requires a chat client.");
        var messages = CreateSummarizerMessages(selected, strategy);
        var options = context.SummarizerOptions?.Clone() ?? new ChatOptions();
        options.Tools = [];
        options.ToolMode = ChatToolMode.None;
        var response = await client.GetResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (response.Messages.SelectMany(static message => message.Contents).Any(static content => content is ErrorContent))
            throw new InvalidOperationException("The compaction summarizer returned an error or refusal.");
        if (response.FinishReason is { } finish && finish != ChatFinishReason.Stop)
            throw new InvalidOperationException($"The compaction summarizer did not complete a continuation handoff ({finish}).");
        if (response.Messages.SelectMany(static message => message.Contents).Any(IsToolDependentContent))
            throw new InvalidOperationException("The compaction summarizer returned a tool request instead of a continuation handoff.");
        var text = response.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("The compaction summarizer returned an empty continuation handoff.");
        return new ChatMessage(ChatRole.Assistant, text)
        {
            MessageId = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static IReadOnlyList<ChatMessage> CreateSummarizerMessages(
        IReadOnlyList<ChatMessage> selected,
        SummarizingCompaction strategy)
    {
        var messages = new List<ChatMessage>(selected.Count + 1);
        foreach (var message in selected)
        {
            // Agent instructions must not compete with the summarization instruction. Tool and
            // interaction protocol messages are relational and can prompt the model to continue
            // an old call instead of summarizing the conversation.
            if (message.Role == ChatRole.System || message.Contents.Any(IsToolDependentContent))
                continue;

            messages.Add(message);
        }

        // Keep the summarization instruction closest to generation, matching MEAI's reducer
        // behavior and preventing older conversational instructions from taking precedence.
        messages.Add(new ChatMessage(
            ChatRole.System,
            string.IsNullOrWhiteSpace(strategy.Instructions) ? DefaultInstructions : strategy.Instructions));
        return messages;
    }

    private static bool IsToolDependentContent(AIContent content) => content
        is ToolCallContent
        or ToolResultContent
        or InputRequestContent
        or InputResponseContent;

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
