using HPD.Agent.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Goals;

/// <summary>Immutable facts supplied to Goal policies after execution.</summary>
public sealed record GoalPolicyContext(
    GoalData Goal,
    string ExecutionId,
    MessageTurnUsageSummary Usage,
    bool HasIncompletePlan,
    bool HasProgress,
    bool RuntimeRunning,
    bool HasUnresolvedRequest,
    int ConsecutiveNoProgressExecutions,
    int RequiredConsecutiveBlockerExecutions,
    int? MaximumConsecutiveNoProgressExecutions)
{
    /// <summary>Explains why this boundary cannot admit another execution.</summary>
    public string ContinuationUnavailableReason { get; init; } = "runtime_not_started";
    /// <summary>Gets whether a restored active Goal is being reconciled by an explicitly started runtime.</summary>
    public bool IsRecovery { get; init; }
}

/// <summary>A policy decision; terminal closure remains owned by the runtime.</summary>
public sealed record GoalPolicyDecision(GoalPolicyDisposition Disposition, string Reason);

/// <summary>Policy outcomes; only the runtime may commit a corresponding transition.</summary>
public enum GoalPolicyDisposition { Continue, Completed, Blocked, Paused, AwaitingInput, Rejected }

/// <summary>Verifies proposed completion against authoritative application evidence.</summary>
public interface IGoalCompletionPolicy
{
    ValueTask<GoalPolicyDecision> EvaluateAsync(GoalPolicyContext context, CancellationToken cancellationToken);
}

/// <summary>Determines whether structured evidence establishes an impasse.</summary>
public interface IGoalBlockerPolicy
{
    ValueTask<GoalPolicyDecision> EvaluateAsync(GoalPolicyContext context, CancellationToken cancellationToken);
}

/// <summary>Determines whether another execution is permitted.</summary>
public interface IGoalContinuationPolicy
{
    ValueTask<GoalPolicyDecision> EvaluateAsync(GoalPolicyContext context, CancellationToken cancellationToken);
}

/// <summary>Projects compatible usage without introducing a resource budget.</summary>
public interface IGoalAccountingPolicy
{
    GoalUsageProjection Project(MessageTurnUsageSummary usage);
}

/// <summary>Creates independent Goal state for a forked thread.</summary>
public interface IGoalForkPolicy
{
    GoalData Fork(GoalData source, string newGoalId, DateTimeOffset now);
}

/// <summary>Observed compatible token usage and its completeness.</summary>
public sealed record GoalUsageProjection(long Tokens, GoalUsageQuality Quality);

internal sealed class DefaultGoalPolicies : IGoalCompletionPolicy, IGoalBlockerPolicy,
    IGoalContinuationPolicy, IGoalAccountingPolicy, IGoalForkPolicy
{
    internal static DefaultGoalPolicies Instance { get; } = new();

    ValueTask<GoalPolicyDecision> IGoalCompletionPolicy.EvaluateAsync(GoalPolicyContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var proposal = context.Goal.CompletionProposal;
        return ValueTask.FromResult(proposal is null || proposal.ExecutionId != context.ExecutionId
            ? new GoalPolicyDecision(GoalPolicyDisposition.Rejected, "completion_proposal_missing")
            : !proposal.RemainingWork.IsDefaultOrEmpty
                ? new(GoalPolicyDisposition.Rejected, "required_work_remaining")
            : context.HasIncompletePlan
                ? new(GoalPolicyDisposition.Rejected, "required_plan_work_remaining")
                : context.HasUnresolvedRequest
                    ? new(GoalPolicyDisposition.AwaitingInput, "request_pending")
                    : proposal.Evidence.IsDefaultOrEmpty || proposal.Evidence.Any(e =>
                        e is null || string.IsNullOrWhiteSpace(e.Kind) || string.IsNullOrWhiteSpace(e.Description))
                        ? new(GoalPolicyDisposition.Rejected, "completion_evidence_missing")
                        : new(GoalPolicyDisposition.Completed, "completion_evidence_accepted"));
    }

    ValueTask<GoalPolicyDecision> IGoalBlockerPolicy.EvaluateAsync(GoalPolicyContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.RequiredConsecutiveBlockerExecutions <= 0)
            throw new ArgumentOutOfRangeException(nameof(context.RequiredConsecutiveBlockerExecutions));
        return ValueTask.FromResult(context.HasUnresolvedRequest
            ? new GoalPolicyDecision(GoalPolicyDisposition.AwaitingInput, "request_pending")
            : context.Goal.Blocker is { } blocker && blocker.LastExecutionId == context.ExecutionId &&
              blocker.ConsecutiveExecutions >= context.RequiredConsecutiveBlockerExecutions
                ? new(GoalPolicyDisposition.Blocked, "consecutive_blocker_verified")
                : new(GoalPolicyDisposition.Continue, "blocker_audit_incomplete"));
    }

    ValueTask<GoalPolicyDecision> IGoalContinuationPolicy.EvaluateAsync(GoalPolicyContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reason = context.Goal.Status != GoalStatus.Active ? "goal_not_active"
            : context.Goal.Continuation is not null ? "continuation_already_reserved"
            : context.Goal.CompletionProposal is not null ? "completion_pending"
            : !context.RuntimeRunning ? context.ContinuationUnavailableReason : null;
        return ValueTask.FromResult(reason is not null
            ? new GoalPolicyDecision(GoalPolicyDisposition.Rejected, reason)
            : context.HasUnresolvedRequest
                ? new(GoalPolicyDisposition.AwaitingInput, "request_pending")
                : context.MaximumConsecutiveNoProgressExecutions is { } maximum &&
                  context.ConsecutiveNoProgressExecutions >= maximum
                    ? new(GoalPolicyDisposition.Paused, "no_progress_limit")
                    : !context.HasProgress && !context.IsRecovery && context.Goal.Blocker?.LastExecutionId != context.ExecutionId
                        ? new(GoalPolicyDisposition.Paused, "no_progress")
                        : new(GoalPolicyDisposition.Continue, "continuation_permitted"));
    }

    public GoalUsageProjection Project(MessageTurnUsageSummary usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        long tokens = 0;
        var available = 0;
        var missing = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in usage.Operations)
        {
            // Speech/image/embedding measurements may use incompatible units.
            if (operation.Family is not (ProviderClientFamily.Chat or ProviderClientFamily.Realtime)) continue;
            if (string.IsNullOrWhiteSpace(operation.OperationId) || !seen.Add(operation.OperationId))
                throw new InvalidOperationException("goal_usage_identity_invalid");
            var observed = operation.Usage;
            if (observed?.InputTokenCount is < 0 || observed?.OutputTokenCount is < 0 || observed?.TotalTokenCount is < 0)
                throw new InvalidOperationException("goal_usage_negative");
            long? count = observed?.TotalTokenCount;
            if (count is null && observed?.InputTokenCount is { } input && observed.OutputTokenCount is { } output)
                count = checked(input + output);
            if (count is null) { missing++; continue; }
            tokens = checked(tokens + count.Value);
            available++;
        }
        return new(tokens, available == 0 ? GoalUsageQuality.Unavailable
            : missing == 0 ? GoalUsageQuality.Exact : GoalUsageQuality.Partial);
    }

    public GoalData Fork(GoalData source, string newGoalId, DateTimeOffset now)
        => GoalTransitions.ForkPaused(source, newGoalId, now);
}

internal sealed record EffectiveGoalPolicies(GoalToolAccess ToolAccess, IGoalCompletionPolicy Completion,
    IGoalBlockerPolicy Blocker, IGoalContinuationPolicy Continuation, IGoalAccountingPolicy Accounting);

internal sealed class GoalPolicyResolver
{
    private readonly GoalConfig _config;
    private readonly IServiceProvider? _services;

    internal GoalPolicyResolver(GoalConfig config, IServiceProvider? services)
    {
        _config = config.Snapshot();
        _services = services;
        var errors = _config.Validate().ToArray();
        if (errors.Length != 0) throw new ArgumentException(string.Join(" ", errors), nameof(config));
        _ = Resolve(null);
        Fork = ResolveKey<IGoalForkPolicy>(_config.Policies.Fork, "Goals.Policies.Fork");
    }

    internal IGoalForkPolicy Fork { get; }

    internal EffectiveGoalPolicies Resolve(GoalRunConfig? run)
    {
        var access = run?.ToolAccess ?? GoalToolAccess.All;
        if (!Enum.IsDefined(access))
            throw new AgentRunConfigurationException("goal_access_invalid", "Goals.ToolAccess", "Unknown Goal tool access.");
        return new(access,
            ResolveKey<IGoalCompletionPolicy>(run?.Policies?.Completion ?? _config.Policies.Completion, "Goals.Policies.Completion"),
            ResolveKey<IGoalBlockerPolicy>(run?.Policies?.Blocker ?? _config.Policies.Blocker, "Goals.Policies.Blocker"),
            ResolveKey<IGoalContinuationPolicy>(run?.Policies?.Continuation ?? _config.Policies.Continuation, "Goals.Policies.Continuation"),
            ResolveKey<IGoalAccountingPolicy>(run?.Policies?.Accounting ?? _config.Policies.Accounting, "Goals.Policies.Accounting"));
    }

    private T ResolveKey<T>(string key, string path) where T : class
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            if (_services?.GetKeyedService<T>(key) is { } custom) return custom;
            if (key == "default" && DefaultGoalPolicies.Instance is T builtIn) return builtIn;
        }
        throw new AgentRunConfigurationException("goal_policy_missing", path, $"{path}: no policy is registered for key '{key}'.");
    }
}
