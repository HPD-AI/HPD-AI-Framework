using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent;

/// <summary>
/// Adapts canonical thread compaction to the existing iteration and fork lifecycle hooks.
/// Compaction planning, transformation, and commit semantics belong to <see cref="IThreadCompactionEngine"/>.
/// </summary>
public sealed class CompactionMiddleware : IAgentMiddleware
{
    public required CompactionConfig Config { get; init; }
    public IThreadCompactionEngine Engine { get; init; } = new ThreadCompactionEngine();

    public async Task BeforeIterationAsync(
        BeforeIterationContext context,
        CancellationToken cancellationToken)
    {
        var automatic = context.RunConfig.Compaction?.Automatic ?? Config.Automatic;
        if (automatic is null || context.Thread is null || !ShouldCompact(automatic.Trigger, context.Messages))
            return;

        var engineContext = new ThreadCompactionContext(
            context.Thread,
            context.Messages,
            context.Base.ThreadEvents,
            context.ClientSet?.Summarizer ?? context.ClientSet?.Chat,
            context.Services?.GetService<IThreadJournalRebaseSeedProvider>());
        var result = await Engine.ExecuteAsync(
                engineContext,
                automatic.Compaction,
                context.AgentName,
                context.Iteration,
                CompactionOrigin.Automatic,
                automatic.Continuation,
                cancellationToken)
            .ConfigureAwait(false);
        if (result.Compaction is not { } prepared)
            return;
        context.Messages.Clear();
        context.Messages.AddRange(prepared.ResultingMessages);

        if (automatic.Continuation == CompactionContinuation.StopAfterCompaction)
        {
            context.UpdateState(state => state with
            {
                IsTerminated = true,
                TerminationReason = "Automatic compaction completed before model invocation."
            });
        }
    }

    public async Task BeforeThreadForkCommitAsync(
        BeforeThreadForkCommitContext context,
        CancellationToken cancellationToken)
    {
        var specification = context.ForkOptions.Compaction switch
        {
            DisableThreadForkCompaction => null,
            ApplyThreadForkCompaction enabled => enabled.Compaction,
            InheritThreadForkCompaction => Config.ForkCompaction,
            _ => throw new ArgumentOutOfRangeException(nameof(context), "Unknown fork compaction mode.")
        };
        if (specification is null || context.TargetThread.Messages.Count == 0)
            return;

        var engineContext = new ThreadCompactionContext(
            context.TargetThread,
            context.TargetThread.Messages,
            Publisher: null,
            context.ClientSet?.Summarizer ?? context.ClientSet?.Chat);
        var prepared = await Engine.PrepareAsync(engineContext, specification, cancellationToken)
            .ConfigureAwait(false);
        if (prepared is null)
            return;

        context.TargetThread.Messages.Clear();
        context.TargetThread.Messages.AddRange(prepared.ResultingMessages);
        context.TargetJournalEvents = ThreadJournalEncoder.Encode(
            context.TargetThread,
            prepared.ResultingMessages,
            [prepared.Checkpoint]);
    }

    private static bool ShouldCompact(CompactionTrigger trigger, IReadOnlyList<ChatMessage> messages) =>
        trigger switch
        {
            TurnCountCompactionTrigger turns => CountTurns(messages) >= turns.Turns,
            InputTokenCompactionTrigger tokens => EstimateInputTokens(messages) >= tokens.InputTokens,
            ContextPercentageCompactionTrigger percentage =>
                percentage.TotalInputTokens > 0 &&
                EstimateInputTokens(messages) >= percentage.TotalInputTokens * percentage.Percentage,
            _ => throw new ArgumentOutOfRangeException(nameof(trigger), "Unknown compaction trigger.")
        };

    private static int CountTurns(IEnumerable<ChatMessage> messages)
    {
        var turnIds = new HashSet<string>(StringComparer.Ordinal);
        var anonymous = 0;
        foreach (var message in messages)
        {
            if (message.Role != ChatRole.User)
                continue;
            if (message.AdditionalProperties?.TryGetValue<string>("hpd.messageTurnId", out var turnId) == true &&
                !string.IsNullOrWhiteSpace(turnId))
                turnIds.Add(turnId);
            else
                anonymous++;
        }
        return turnIds.Count + anonymous;
    }

    private static long EstimateInputTokens(IEnumerable<ChatMessage> messages)
    {
        long characters = 0;
        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
                characters += content.ToString()?.Length ?? 0;
        }
        return Math.Max(1, (long)Math.Ceiling(characters / 4d));
    }
}
