using System.Collections.Immutable;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Agent.Goals;

/// <summary>The persistent operational Goal owned by one thread.</summary>
[MiddlewareState(Persistent = true, Scope = StateScope.Thread, Version = 1)]
public sealed record GoalPersistentState
{
    /// <summary>Gets the current Goal; historical Goals remain in the journal.</summary>
    public GoalData? Current { get; init; }
    /// <summary>Gets the bounded in-flight attribution retained even if the current Goal is cleared.</summary>
    public GoalPendingExecution? PendingExecution { get; init; }
    /// <summary>Gets the latest closed terminal position applied to Goal accounting.</summary>
    public ThreadJournalCursor? AccountingCheckpoint { get; init; }
}

/// <summary>Durable ownership of one message turn until its provider accounting closes.</summary>
public sealed record GoalPendingExecution(GoalData GoalSnapshot, string ExecutionId, string MessageTurnId,
    DateTimeOffset StartedAt)
{
    /// <summary>Gets whether this turn performed work beyond Goal bookkeeping.</summary>
    public bool HasProgress { get; init; }
    /// <summary>Gets whether the current plan still has required unfinished steps.</summary>
    public bool HasIncompletePlan { get; init; }
}

/// <summary>The authoritative outcome, transition identity, and evidence for one Goal.</summary>
public sealed record GoalData
{
    /// <summary>Gets the stable framework-generated Goal identity.</summary>
    public required string GoalId { get; init; }
    /// <summary>Gets the complete user-authorized outcome.</summary>
    public required string Objective { get; init; }
    /// <summary>Gets the current lifecycle status.</summary>
    public required GoalStatus Status { get; init; }
    /// <summary>Gets the version of material Goal state.</summary>
    public long Revision { get; init; }
    /// <summary>Gets the latest continuation generation, including invalidated reservations.</summary>
    public long ContinuationGeneration { get; init; }
    /// <summary>Gets the one outstanding continuation reservation.</summary>
    public GoalContinuationReservation? Continuation { get; init; }
    /// <summary>Gets closed usage and execution accounting.</summary>
    public GoalAccounting Accounting { get; init; } = new();
    /// <summary>Gets the consecutive closed executions without observed progress.</summary>
    public int ConsecutiveNoProgressExecutions { get; init; }
    /// <summary>Gets the pending completion claim; it does not imply terminal completion.</summary>
    public GoalCompletionProposal? CompletionProposal { get; init; }
    /// <summary>Gets policy-owned consecutive blocker evidence.</summary>
    public GoalBlockerEvidence? Blocker { get; init; }
    /// <summary>Gets when this Goal was committed.</summary>
    public DateTimeOffset CreatedAt { get; init; }
    /// <summary>Gets when material state last changed.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Goal lifecycle states; stopped but resumable states are unfinished.</summary>
[JsonConverter(typeof(GoalStatusJsonConverter))]
public enum GoalStatus
{
    /// <summary>Unfinished; a running runtime and continuation policy may admit work.</summary>
    Active,
    /// <summary>Paused until explicitly resumed.</summary>
    Paused,
    /// <summary>Suspended until a first-class request resolves.</summary>
    AwaitingInput,
    /// <summary>Policy-verified success with successful terminal closure.</summary>
    Completed,
    /// <summary>Policy-verified impasse, explicitly resumable.</summary>
    Blocked,
    /// <summary>Externally limited; resumable after the limit resolves.</summary>
    UsageLimited,
    /// <summary>Terminal runtime failure.</summary>
    Faulted
}

/// <summary>A committed reservation for exactly one future Goal execution.</summary>
public sealed record GoalContinuationReservation(
    long Generation,
    long ExpectedRevision,
    DateTimeOffset ReservedAt,
    string SourceExecutionId)
{
    /// <summary>Gets the runtime incarnation that claimed admission of this reservation.</summary>
    public string? ActivationOwner { get; init; }
}

/// <summary>Observed closed accounting; never a Goal-owned resource budget.</summary>
public sealed record GoalAccounting
{
    /// <summary>Gets compatible observed token usage, excluding double-counted subcategories.</summary>
    public long TokensUsed { get; init; }
    /// <summary>Gets whether selected provider usage is complete.</summary>
    public GoalUsageQuality UsageQuality { get; init; } = GoalUsageQuality.Unavailable;
    /// <summary>Gets accumulated attributed execution duration.</summary>
    public TimeSpan ExecutionTime { get; init; }
    /// <summary>Gets the number of distinct attributed executions.</summary>
    public int ExecutionCount { get; init; }
    /// <summary>Gets a diagnostic last-accounted execution identity.</summary>
    public string? LastAccountedExecutionId { get; init; }
    /// <summary>Gets a diagnostic last-accounted message-turn identity.</summary>
    public string? LastAccountedMessageTurnId { get; init; }
}

/// <summary>Completeness of the selected provider-usage projection.</summary>
[JsonConverter(typeof(GoalUsageQualityJsonConverter))]
public enum GoalUsageQuality
{
    /// <summary>Every selected measurement reported trustworthy usage.</summary>
    Exact,
    /// <summary>Some selected measurements lack usage.</summary>
    Partial,
    /// <summary>No trustworthy selected usage is available.</summary>
    Unavailable
}

/// <summary>A pending model claim awaiting policy and successful turn finalization.</summary>
public sealed record GoalCompletionProposal(
    string Summary,
    ImmutableArray<GoalEvidenceItem> Evidence,
    DateTimeOffset ProposedAt,
    string ExecutionId)
{
    /// <summary>Gets admitted remaining or unverified required work.</summary>
    public ImmutableArray<string> RemainingWork { get; init; } = [];
}

/// <summary>Normalized blocker recurrence maintained by framework policy.</summary>
public sealed record GoalBlockerEvidence(
    GoalBlockerCategory Category,
    string Fingerprint,
    string Description,
    string RequiredChange,
    ImmutableArray<string> Evidence,
    int ConsecutiveExecutions,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt,
    string LastExecutionId,
    int LastExecutionOrdinal);

/// <summary>Pure centrally validated Goal transitions.</summary>
internal static class GoalTransitions
{
    internal static bool IsTerminal(GoalStatus status) => status is GoalStatus.Completed or GoalStatus.Faulted;

    internal static GoalPersistentState Create(GoalPersistentState state, string objective,
        GoalConfig config, string goalId, DateTimeOffset now)
    {
        if (state.Current is { } current)
        {
            Validate(current);
            if (!IsTerminal(current.Status)) throw new InvalidOperationException("goal_already_exists");
        }
        ValidateObjective(objective, config.MaximumObjectiveLength);
        ArgumentException.ThrowIfNullOrWhiteSpace(goalId);
        return state with { Current = new GoalData
        {
            GoalId = goalId, Objective = objective, Status = GoalStatus.Active,
            Revision = 1, CreatedAt = now, UpdatedAt = now
        } };
    }

    internal static GoalData Require(GoalPersistentState state, string goalId, long revision)
    {
        var goal = state.Current ?? throw new InvalidOperationException("goal_missing");
        Validate(goal);
        if (!string.Equals(goal.GoalId, goalId, StringComparison.Ordinal) || goal.Revision != revision)
            throw new InvalidOperationException("goal_revision_conflict");
        return goal;
    }

    internal static void Validate(GoalData goal)
    {
        if (!Enum.IsDefined(goal.Status) || goal.Revision <= 0 || goal.ContinuationGeneration < 0 ||
            string.IsNullOrWhiteSpace(goal.GoalId) || string.IsNullOrWhiteSpace(goal.Objective) ||
            goal.Accounting is null || !Enum.IsDefined(goal.Accounting.UsageQuality) ||
            goal.Accounting.TokensUsed < 0 || goal.Accounting.ExecutionCount < 0 || goal.ConsecutiveNoProgressExecutions < 0 ||
            goal.Accounting.ExecutionTime < TimeSpan.Zero)
            throw new InvalidOperationException("goal_state_invalid");
        if (goal.Continuation is { } reservation &&
            (goal.Status != GoalStatus.Active || reservation.Generation != goal.ContinuationGeneration ||
             reservation.Generation <= 0 || reservation.ExpectedRevision != goal.Revision ||
             string.IsNullOrWhiteSpace(reservation.SourceExecutionId)))
            throw new InvalidOperationException("goal_reservation_invalid");
        if (goal.CompletionProposal is { } proposal &&
            (string.IsNullOrWhiteSpace(proposal.Summary) || string.IsNullOrWhiteSpace(proposal.ExecutionId) ||
             proposal.Evidence.IsDefault || proposal.Evidence.Any(item => item is null ||
                 string.IsNullOrWhiteSpace(item.Kind) || string.IsNullOrWhiteSpace(item.Description))))
            throw new InvalidOperationException("goal_completion_state_invalid");
        if (goal.Blocker is { } blocker &&
            (!Enum.IsDefined(blocker.Category) || string.IsNullOrWhiteSpace(blocker.Fingerprint) ||
             string.IsNullOrWhiteSpace(blocker.Description) || string.IsNullOrWhiteSpace(blocker.RequiredChange) ||
             string.IsNullOrWhiteSpace(blocker.LastExecutionId) || blocker.ConsecutiveExecutions <= 0 ||
             blocker.LastExecutionOrdinal < blocker.ConsecutiveExecutions || blocker.Evidence.IsDefault ||
             blocker.Evidence.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidOperationException("goal_blocker_state_invalid");
    }

    internal static void ValidateObjective(string objective, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(objective) || objective.Length > maximumLength)
            throw new ArgumentException($"Goal objective must contain 1-{maximumLength} characters.", nameof(objective));
    }

    internal static GoalData ChangeStatus(GoalData goal, GoalStatus status, DateTimeOffset now)
    {
        Validate(goal);
        if (!Enum.IsDefined(status)) throw new InvalidOperationException("goal_status_invalid");
        var allowed = goal.Status == GoalStatus.Active
            ? status != GoalStatus.Active
            : goal.Status is GoalStatus.Paused or GoalStatus.AwaitingInput or GoalStatus.Blocked or GoalStatus.UsageLimited
                && status == GoalStatus.Active;
        if (!allowed) throw new InvalidOperationException("goal_transition_invalid");
        return Changed(goal, now) with
        {
            Status = status,
            Blocker = status == GoalStatus.Active ? null : goal.Blocker,
            ConsecutiveNoProgressExecutions = status == GoalStatus.Active ? 0 : goal.ConsecutiveNoProgressExecutions,
            CompletionProposal = null
        };
    }

    internal static GoalData Edit(GoalData goal, string objective, int maximumLength, DateTimeOffset now)
    {
        Validate(goal);
        if (IsTerminal(goal.Status)) throw new InvalidOperationException("goal_terminal");
        ValidateObjective(objective, maximumLength);
        return Changed(goal, now) with { Objective = objective, CompletionProposal = null, Blocker = null };
    }

    internal static GoalData Propose(GoalData goal, GoalCompletionProposal proposal, DateTimeOffset now)
    {
        RequireActive(goal);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposal.Summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposal.ExecutionId);
        return Changed(goal, now) with { CompletionProposal = proposal };
    }

    internal static GoalData Reserve(GoalData goal, string sourceExecutionId, DateTimeOffset now)
    {
        RequireActive(goal);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceExecutionId);
        if (goal.Continuation is not null) throw new InvalidOperationException("goal_continuation_reserved");
        if (goal.CompletionProposal is not null) throw new InvalidOperationException("goal_completion_pending");
        var next = Changed(goal, now) with { ContinuationGeneration = checked(goal.ContinuationGeneration + 1) };
        return next with { Continuation = new(next.ContinuationGeneration, next.Revision, now, sourceExecutionId) };
    }

    internal static GoalData ReportBlocker(GoalData goal, ReportGoalBlockerAction report,
        string executionId, int executionOrdinal, DateTimeOffset now)
    {
        RequireActive(goal);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(report.Description);
        ArgumentException.ThrowIfNullOrWhiteSpace(report.RequiredChange);
        if (!Enum.IsDefined(report.Category) || executionOrdinal <= 0)
            throw new InvalidOperationException("goal_blocker_invalid");
        static string Normalize(string value) => string.Join(' ', value.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{report.Category}\n{Normalize(report.Description)}\n{Normalize(report.RequiredChange)}")));
        var previous = goal.Blocker;
        if (previous is not null && (executionOrdinal < previous.LastExecutionOrdinal ||
            (executionOrdinal == previous.LastExecutionOrdinal && previous.LastExecutionId != executionId)))
            throw new InvalidOperationException("goal_execution_order_invalid");
        var same = previous?.Fingerprint == fingerprint;
        var repeatedInExecution = same && previous!.LastExecutionId == executionId;
        var consecutive = same && previous!.LastExecutionId != executionId &&
            previous.LastExecutionOrdinal == executionOrdinal - 1;
        var count = repeatedInExecution ? previous!.ConsecutiveExecutions
            : consecutive ? checked(previous!.ConsecutiveExecutions + 1) : 1;
        return Changed(goal, now) with
        {
            Blocker = new(report.Category, fingerprint, report.Description, report.RequiredChange,
                report.Evidence?.ToImmutableArray() ?? [], count,
                repeatedInExecution || consecutive ? previous!.FirstObservedAt : now,
                now, executionId, executionOrdinal)
        };
    }

    internal static GoalPersistentState Consume(GoalPersistentState state, string goalId,
        long revision, long generation, DateTimeOffset now)
    {
        if (state.Current is not { } goal) return state;
        Validate(goal);
        if (goal.Status != GoalStatus.Active || goal.GoalId != goalId || goal.Revision != revision ||
            goal.Continuation is not { } reservation || reservation.Generation != generation ||
            reservation.ExpectedRevision != revision) return state;
        return state with { Current = Changed(goal, now) };
    }

    internal static GoalData Activate(GoalData goal, string owner, DateTimeOffset now)
    {
        RequireActive(goal);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        var reservation = goal.Continuation ?? throw new InvalidOperationException("goal_reservation_missing");
        if (reservation.ActivationOwner == owner) return goal;
        var revision = checked(goal.Revision + 1);
        return goal with { Revision = revision, UpdatedAt = now,
            Continuation = reservation with { ExpectedRevision = revision, ActivationOwner = owner } };
    }

    internal static GoalData ForkPaused(GoalData goal, string newGoalId, DateTimeOffset now)
    {
        Validate(goal);
        ArgumentException.ThrowIfNullOrWhiteSpace(newGoalId);
        if (newGoalId == goal.GoalId) throw new InvalidOperationException("goal_fork_identity_reused");
        return goal with
        {
            GoalId = newGoalId, Revision = 1, Status = GoalStatus.Paused,
            ContinuationGeneration = 0, Continuation = null, Accounting = new(),
            ConsecutiveNoProgressExecutions = 0,
            Blocker = null, CompletionProposal = null, CreatedAt = now, UpdatedAt = now
        };
    }

    private static void RequireActive(GoalData goal)
    {
        Validate(goal);
        if (goal.Status != GoalStatus.Active) throw new InvalidOperationException("goal_not_active");
    }

    private static GoalData Changed(GoalData goal, DateTimeOffset now) => goal with
    {
        Revision = checked(goal.Revision + 1), Continuation = null, UpdatedAt = now
    };
}
