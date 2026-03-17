using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using HPD.Agent;

namespace HPD.Agent.Planning;

/// <summary>
/// Immutable representation of an agent's plan for completing a complex task.
/// Plans are conversation-scoped working memory that helps agents maintain context and progress.
/// </summary>
/// <remarks>
/// <para><b>Immutability:</b></para>
/// <para>
/// This is an immutable record that integrates with the MiddlewareState container.
/// All mutations return new instances via 'with' expressions.
/// </para>
/// </remarks>
public sealed record AgentPlanData
{
    /// <summary>Unique identifier for this plan</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>The goal or objective this plan aims to accomplish</summary>
    public string Goal { get; init; } = string.Empty;

    /// <summary>Ordered list of steps to complete the goal (immutable)</summary>
    public ImmutableList<PlanStepData> Steps { get; init; } = ImmutableList<PlanStepData>.Empty;

    /// <summary>When this plan was created</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Additional context notes discovered during execution (immutable)</summary>
    public ImmutableList<string> ContextNotes { get; init; } = ImmutableList<string>.Empty;

    /// <summary>Whether this plan has been marked as complete</summary>
    public bool IsComplete { get; init; } = false;

    /// <summary>When the plan was completed (if applicable)</summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>
    /// Creates a new plan with an updated step.
    /// </summary>
    public AgentPlanData WithUpdatedStep(string stepId, PlanStepStatus status, string? notes = null)
    {
        var stepIndex = Steps.FindIndex(s => s.Id == stepId);
        if (stepIndex < 0)
            return this;

        var existingStep = Steps[stepIndex];
        var updatedStep = existingStep with
        {
            Status = status,
            Notes = notes ?? existingStep.Notes,
            LastUpdated = DateTime.UtcNow
        };

        return this with { Steps = Steps.SetItem(stepIndex, updatedStep) };
    }

    /// <summary>
    /// Creates a new plan with an additional step.
    /// </summary>
    public AgentPlanData WithAddedStep(string description, string? afterStepId = null)
    {
        var newStepId = (Steps.Count + 1).ToString();
        var newStep = new PlanStepData
        {
            Id = newStepId,
            Description = description,
            Status = PlanStepStatus.Pending
        };

        if (afterStepId != null)
        {
            var afterIndex = Steps.FindIndex(s => s.Id == afterStepId);
            if (afterIndex >= 0)
            {
                return this with { Steps = Steps.Insert(afterIndex + 1, newStep) };
            }
        }

        return this with { Steps = Steps.Add(newStep) };
    }

    /// <summary>
    /// Creates a new plan with an additional context note.
    /// </summary>
    public AgentPlanData WithContextNote(string note)
    {
        var timestampedNote = $"[{DateTime.UtcNow:HH:mm:ss}] {note}";
        return this with { ContextNotes = ContextNotes.Add(timestampedNote) };
    }

    /// <summary>
    /// Creates a new plan marked as complete.
    /// </summary>
    public AgentPlanData AsCompleted()
    {
        return this with
        {
            IsComplete = true,
            CompletedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Builds a formatted string representation for injection into prompts.
    /// </summary>
    public string BuildPlanPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[CURRENT_PLAN]");
        sb.AppendLine($"Goal: {Goal}");
        sb.AppendLine($"Plan ID: {Id}");
        sb.AppendLine($"Created: {CreatedAt:yyyy-MM-dd HH:mm:ss}");

        if (IsComplete)
        {
            sb.AppendLine($"Status: ✓ COMPLETED at {CompletedAt:HH:mm:ss}");
        }
        else
        {
            sb.AppendLine("Status: In Progress");
        }

        sb.AppendLine();
        sb.AppendLine("Steps:");
        foreach (var step in Steps)
        {
            var statusIcon = step.Status switch
            {
                PlanStepStatus.Pending => "○",
                PlanStepStatus.InProgress => "◐",
                PlanStepStatus.Completed => "●",
                PlanStepStatus.Blocked => "✖",
                _ => "?"
            };

            sb.AppendLine($"  {statusIcon} [{step.Id}] {step.Description} ({step.Status})");
            if (!string.IsNullOrEmpty(step.Notes))
            {
                sb.AppendLine($"      Notes: {step.Notes}");
            }
        }

        if (ContextNotes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Context Notes:");
            foreach (var note in ContextNotes)
            {
                sb.AppendLine($"  • {note}");
            }
        }

        sb.AppendLine("[END_CURRENT_PLAN]");
        return sb.ToString();
    }

    /// <summary>
    /// Gets a step by ID.
    /// </summary>
    public PlanStepData? GetStep(string stepId)
    {
        return Steps.FirstOrDefault(s => s.Id == stepId);
    }
}

/// <summary>
/// Immutable representation of a single step in an agent's plan.
/// </summary>
public sealed record PlanStepData
{
    /// <summary>Unique identifier for this step (e.g., "1", "2a", "3")</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Description of what this step involves</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Current status of this step</summary>
    public PlanStepStatus Status { get; init; } = PlanStepStatus.Pending;

    /// <summary>Optional notes about this step (progress, blockers, findings)</summary>
    public string? Notes { get; init; }

    /// <summary>When this step was last updated</summary>
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Status of a plan step.
/// </summary>
public enum PlanStepStatus
{
    /// <summary>Step has not been started yet</summary>
    Pending,

    /// <summary>Step is currently being worked on</summary>
    InProgress,

    /// <summary>Step has been successfully completed</summary>
    Completed,

    /// <summary>Step is blocked and cannot proceed</summary>
    Blocked
}

/// <summary>
/// Persistent state for plan mode middleware. Immutable record with session persistence.
/// Stores multiple plans keyed by conversation ID, supporting parallel workstreams within a session.
/// </summary>
/// <remarks>
/// <para><b>Source-Generated State:</b></para>
/// <para>
/// The source generator creates MiddlewareStateRegistry.g.cs and MiddlewareStateExtensions.g.cs
/// in this assembly based on the [MiddlewareState] attribute on this record.
/// </para>
///
/// <para><b>Multi-Plan Support:</b></para>
/// <para>
/// Plans are keyed by conversation ID, allowing multiple independent plans within a session.
/// Each conversation can have at most one active task/plan at a time.
/// </para>
///
/// <para><b>Session Scoping:</b></para>
/// <para>
/// Plans are stored per-branch in Branch.MiddlewareState.
/// Each session can have multiple plans (one per conversation ID).
/// </para>
///
/// <para><b>Usage:</b></para>
/// <code>
/// // Read plan for current conversation (extension method from HPD.Agent.Planning namespace)
/// var planState = context.State.MiddlewareState.PlanModePersistent() ?? new();
/// var plan = planState.GetPlan(conversationId);
///
/// // Update plan for current conversation
/// var updated = planState.WithPlan(conversationId, newPlan);
/// context.UpdateState(s => s with
/// {
///     MiddlewareState = s.MiddlewareState.WithPlanModePersistent(updated)
/// });
/// </code>
///
/// <para><b>Persistence:</b></para>
/// <para>
/// This state is automatically saved to Branch.MiddlewareState
/// at the end of each agent run via SaveToSession() and loaded via LoadFromSession().
/// The agent's registered MiddlewareStateFactory handles serialization.
/// </para>
/// </remarks>
[MiddlewareState(Persistent = true)]
public sealed record PlanModePersistentStateData
{
    /// <summary>
    /// Plans keyed by conversation ID.
    /// Each conversation can have at most one active plan.
    /// </summary>
    public ImmutableDictionary<string, AgentPlanData> Plans { get; init; }
        = ImmutableDictionary<string, AgentPlanData>.Empty;

    /// <summary>
    /// Gets the plan for a specific conversation.
    /// Returns null if no plan exists for that conversation.
    /// </summary>
    public AgentPlanData? GetPlan(string? conversationId)
    {
        if (string.IsNullOrEmpty(conversationId))
            return null;

        return Plans.TryGetValue(conversationId, out var plan) ? plan : null;
    }

    /// <summary>
    /// Checks if an active (non-completed) plan exists for the given conversation.
    /// </summary>
    public bool HasActivePlan(string? conversationId)
    {
        var plan = GetPlan(conversationId);
        return plan != null && !plan.IsComplete;
    }

    /// <summary>
    /// Gets all active (non-completed) plans across all conversations.
    /// </summary>
    public IEnumerable<(string ConversationId, AgentPlanData Plan)> GetActivePlans()
    {
        return Plans
            .Where(kvp => !kvp.Value.IsComplete)
            .Select(kvp => (kvp.Key, kvp.Value));
    }

    /// <summary>
    /// Creates a new state with a plan added or updated for a conversation.
    /// </summary>
    public PlanModePersistentStateData WithPlan(string conversationId, AgentPlanData plan)
    {
        if (string.IsNullOrEmpty(conversationId))
            throw new ArgumentException("Conversation ID is required", nameof(conversationId));

        return this with { Plans = Plans.SetItem(conversationId, plan) };
    }

    /// <summary>
    /// Creates a new state with the plan removed for a conversation.
    /// </summary>
    public PlanModePersistentStateData WithoutPlan(string conversationId)
    {
        if (string.IsNullOrEmpty(conversationId) || !Plans.ContainsKey(conversationId))
            return this;

        return this with { Plans = Plans.Remove(conversationId) };
    }

    /// <summary>
    /// Creates a new state with all completed plans removed (cleanup).
    /// </summary>
    public PlanModePersistentStateData WithCompletedPlansCleared()
    {
        var activePlans = Plans
            .Where(kvp => !kvp.Value.IsComplete)
            .ToImmutableDictionary();

        return this with { Plans = activePlans };
    }

    /// <summary>
    /// Creates a new plan with the given goal and steps.
    /// </summary>
    public static AgentPlanData CreatePlan(string goal, string[]? initialSteps = null)
    {
        var steps = ImmutableList<PlanStepData>.Empty;

        if (initialSteps != null)
        {
            for (int i = 0; i < initialSteps.Length; i++)
            {
                steps = steps.Add(new PlanStepData
                {
                    Id = (i + 1).ToString(),
                    Description = initialSteps[i],
                    Status = PlanStepStatus.Pending
                });
            }
        }

        return new AgentPlanData
        {
            Goal = goal,
            Steps = steps,
            CreatedAt = DateTime.UtcNow
        };
    }
}
