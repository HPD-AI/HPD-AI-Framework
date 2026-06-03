using Microsoft.Extensions.Logging;
using System.ComponentModel;
using HPD.Agent;
using HPD.Agent.Middleware;

namespace HPD.Agent.Planning;

/// <summary>
/// HPD-Agent AI ToolHarness for Plan Mode management.
/// Provides functions for agents to create and manage execution plans.
/// Uses MiddlewareState (PlanModePersistentStateData) for session-persistent plan storage.
/// </summary>
/// <remarks>
/// <para><b>Multi-Plan Support:</b></para>
/// <para>
/// Plans are keyed by conversation ID, allowing multiple independent plans within a session.
/// Each conversation can have at most one active plan at a time.
/// </para>
///
/// <para><b>Session Persistence:</b></para>
/// <para>
/// Plans are automatically persisted to Branch.MiddlewareState at the end of each run
/// and restored at agent start. This means plans survive across agent runs within the same session.
/// </para>
///
/// <para><b>State Access:</b></para>
/// <para>
/// Plan tools receive FunctionExecutionContext as a runtime-only parameter.
/// The context provides Analyze() for safe state reads, ResultMetadata for
/// scheduler-owned state commits, and ConversationId for plan scoping.
/// </para>
/// </remarks>
public class AgentPlanToolHarness
{
    private readonly ILogger<AgentPlanToolHarness>? _logger;

    public AgentPlanToolHarness(ILogger<AgentPlanToolHarness>? logger = null)
    {
        _logger = logger;
    }

    private static Task<object> Result(object value)
        => Task.FromResult(value);

    private static Task<object> Result(
        FunctionExecutionContext context,
        string modelText,
        Func<PlanModePersistentStateData, PlanModePersistentStateData> apply,
        PlanUpdatedEvent evt)
    {
        context.ResultMetadata.Set(PlanToolMetadataKeys.Apply, apply);
        context.ResultMetadata.Set(PlanToolMetadataKeys.Event, evt);
        return Result(modelText);
    }

    [AIFunction]
    [Description("Create a new execution plan to track progress on multi-step tasks. Use when you need to plan and track complex work.")]
    public Task<object> CreatePlanAsync(
        [Description("The goal or objective this plan aims to accomplish")] string goal,
        [Description("Array of step descriptions (e.g., ['Analyze code', 'Refactor auth', 'Run tests'])")] string[] steps,
        FunctionExecutionContext context)
    {
        var conversationId = context.ConversationId;
        if (string.IsNullOrEmpty(conversationId))
        {
            return Result("Error: No conversation ID available.");
        }

        if (string.IsNullOrEmpty(goal))
        {
            return Result("Error: Goal is required for creating a plan.");
        }

        if (steps == null || steps.Length == 0)
        {
            return Result("Error: At least one step is required for creating a plan.");
        }

        // Create the new plan using the immutable helper
        var plan = PlanModePersistentStateData.CreatePlan(goal, steps);

        var evt = new PlanUpdatedEvent(
            PlanId: plan.Id,
            ConversationId: conversationId,
            UpdateType: PlanUpdateType.Created,
            Plan: plan,
            Explanation: $"Created plan with goal '{goal}' and {plan.Steps.Count} steps",
            UpdatedAt: DateTimeOffset.UtcNow);

        var stepList = string.Join("\n", plan.Steps.Select((step, i) => $"  {step.Id}. {step.Description}"));
        _logger?.LogInformation("Created plan {PlanId} for conversation {ConversationId} with goal: {Goal}", plan.Id, conversationId, goal);

        return Result(
            context,
            $"Created plan {plan.Id} with {plan.Steps.Count} steps:\n{stepList}\n\nUse update_plan_step() to mark progress.",
            state => state.WithPlan(conversationId, plan),
            evt);
    }

    [AIFunction]
    [Description("Update the status of a specific step in the current plan. Use this as you make progress.")]
    public Task<object> UpdatePlanStepAsync(
        [Description("The step ID to update (e.g., '1', '2', '3')")] string stepId,
        [Description("The new status: 'pending', 'in_progress', 'completed', or 'blocked'")] string status,
        FunctionExecutionContext context,
        [Description("Optional notes about this step's progress, findings, or blockers")] string? notes = null)
    {
        var conversationId = context.ConversationId;
        if (string.IsNullOrEmpty(conversationId))
        {
            return Result("Error: No conversation ID available.");
        }

        if (string.IsNullOrEmpty(stepId))
        {
            return Result("Error: Step ID is required.");
        }

        // Check if plan exists for this conversation
        var planState = context.Analyze(s => s.MiddlewareState.PlanModePersistent());
        if (planState == null || !planState.HasActivePlan(conversationId))
        {
            return Result("Error: No active plan exists for this conversation. Create a plan first using create_plan().");
        }

        var plan = planState.GetPlan(conversationId);
        if (plan == null)
        {
            return Result("Error: No active plan exists for this conversation. Create a plan first using create_plan().");
        }

        var parsedStatus = status.ToLowerInvariant() switch
        {
            "pending" => PlanStepStatus.Pending,
            "in_progress" or "inprogress" => PlanStepStatus.InProgress,
            "completed" or "complete" or "done" => PlanStepStatus.Completed,
            "blocked" => PlanStepStatus.Blocked,
            _ => (PlanStepStatus?)null
        };

        if (parsedStatus == null)
        {
            return Result($"Error: Invalid status '{status}'. Use: pending, in_progress, completed, or blocked.");
        }

        // Check if step exists
        var existingStep = plan.GetStep(stepId);
        if (existingStep == null)
        {
            return Result($"Error: Step '{stepId}' not found in current plan.");
        }

        var oldStatus = existingStep.Status.ToString();
        var updatedPlan = plan.WithUpdatedStep(stepId, parsedStatus.Value, notes);
        var evt = new PlanUpdatedEvent(
            PlanId: plan.Id,
            ConversationId: conversationId,
            UpdateType: PlanUpdateType.StepUpdated,
            Plan: updatedPlan,
            Explanation: $"Updated step {stepId} from {oldStatus} to {parsedStatus.Value}" + (notes != null ? $": {notes}" : ""),
            UpdatedAt: DateTimeOffset.UtcNow);

        _logger?.LogInformation("Updated step {StepId} to {Status} for conversation {ConversationId}", stepId, parsedStatus, conversationId);

        var response = $"Updated step {stepId} to {parsedStatus}";
        if (notes != null)
        {
            response += $" with notes: {notes}";
        }
        return Result(
            context,
            response,
            state => state.WithPlan(conversationId, updatedPlan),
            evt);
    }

    [AIFunction]
    [Description("Add a new step to the current plan. Use this when you discover additional work is needed.")]
    public Task<object> AddPlanStepAsync(
        [Description("Description of the new step to add")] string description,
        FunctionExecutionContext context,
        [Description("Optional: ID of step to insert after (e.g., '2'). If omitted, adds to end.")] string? afterStepId = null)
    {
        var conversationId = context.ConversationId;
        if (string.IsNullOrEmpty(conversationId))
        {
            return Result("Error: No conversation ID available.");
        }

        if (string.IsNullOrEmpty(description))
        {
            return Result("Error: Step description is required.");
        }

        // Check if plan exists for this conversation
        var planState = context.Analyze(s => s.MiddlewareState.PlanModePersistent());
        if (planState == null || !planState.HasActivePlan(conversationId))
        {
            return Result("Error: No active plan exists for this conversation. Create a plan first using create_plan().");
        }

        var plan = planState.GetPlan(conversationId);
        if (plan == null)
        {
            return Result("Error: No active plan exists for this conversation.");
        }

        var updatedPlan = plan.WithAddedStep(description, afterStepId);
        var newStepId = updatedPlan.Steps.LastOrDefault()?.Id;
        var evt = new PlanUpdatedEvent(
            PlanId: plan.Id,
            ConversationId: conversationId,
            UpdateType: PlanUpdateType.StepAdded,
            Plan: updatedPlan,
            Explanation: $"Added step {newStepId}: {description}" + (afterStepId != null ? $" after step {afterStepId}" : ""),
            UpdatedAt: DateTimeOffset.UtcNow);

        _logger?.LogInformation("Added step {StepId}: {Description} for conversation {ConversationId}", newStepId, description, conversationId);

        return Result(
            context,
            $"Added step {newStepId}: {description}",
            state => state.WithPlan(conversationId, updatedPlan),
            evt);
    }

    [AIFunction]
    [Description("Add a context note to the current plan. Use this to record important discoveries, learnings, or context.")]
    public Task<object> AddContextNoteAsync(
        [Description("The note to add (e.g., 'Discovered auth uses JWT not sessions')")] string note,
        FunctionExecutionContext context)
    {
        var conversationId = context.ConversationId;
        if (string.IsNullOrEmpty(conversationId))
        {
            return Result("Error: No conversation ID available.");
        }

        if (string.IsNullOrEmpty(note))
        {
            return Result("Error: Note content is required.");
        }

        // Check if plan exists for this conversation
        var planState = context.Analyze(s => s.MiddlewareState.PlanModePersistent());
        if (planState == null || !planState.HasActivePlan(conversationId))
        {
            return Result("Error: No active plan exists for this conversation. Create a plan first using create_plan().");
        }

        var plan = planState.GetPlan(conversationId);
        if (plan == null)
        {
            return Result("Error: No active plan exists for this conversation.");
        }

        var updatedPlan = plan.WithContextNote(note);
        var evt = new PlanUpdatedEvent(
            PlanId: plan.Id,
            ConversationId: conversationId,
            UpdateType: PlanUpdateType.NoteAdded,
            Plan: updatedPlan,
            Explanation: $"Added context note: {note}",
            UpdatedAt: DateTimeOffset.UtcNow);

        _logger?.LogInformation("Added context note for conversation {ConversationId}: {Note}", conversationId, note);

        return Result(
            context,
            $"Added context note: {note}",
            state => state.WithPlan(conversationId, updatedPlan),
            evt);
    }

    // Note: GetCurrentPlanAsync() removed - the plan is automatically injected into every request
    // via AgentPlanAgentMiddleware, so the agent always has the current plan in context without needing
    // to call a function. This saves tokens and simplifies the API.

    [AIFunction]
    [Description("Mark the entire plan as complete. Use this when all steps are done and the goal is achieved.")]
    public Task<object> CompletePlanAsync(FunctionExecutionContext context)
    {
        var conversationId = context.ConversationId;
        if (string.IsNullOrEmpty(conversationId))
        {
            return Result("Error: No conversation ID available.");
        }

        // Check if plan exists for this conversation
        var planState = context.Analyze(s => s.MiddlewareState.PlanModePersistent());
        if (planState == null || !planState.HasActivePlan(conversationId))
        {
            return Result("Error: No active plan exists for this conversation.");
        }

        var plan = planState.GetPlan(conversationId);
        if (plan == null)
        {
            return Result("Error: No active plan exists for this conversation.");
        }

        var incompleteSteps = plan.Steps.Where(s => s.Status != PlanStepStatus.Completed).ToList();

        if (incompleteSteps.Count > 0)
        {
            var incompleteList = string.Join(", ", incompleteSteps.Select(s => s.Id));
            return Result($"Warning: Plan has incomplete steps: {incompleteList}. Mark them as completed first or complete anyway?");
        }

        var completedPlan = plan.AsCompleted();
        var evt = new PlanUpdatedEvent(
            PlanId: plan.Id,
            ConversationId: conversationId,
            UpdateType: PlanUpdateType.Completed,
            Plan: completedPlan,
            Explanation: $"Completed plan: {plan.Goal}",
            UpdatedAt: DateTimeOffset.UtcNow);

        _logger?.LogInformation("Completed plan {PlanId} for conversation {ConversationId}: {Goal}", plan.Id, conversationId, plan.Goal);

        return Result(
            context,
            $"Plan {plan.Id} marked as complete! Goal '{plan.Goal}' achieved.",
            state => state.WithPlan(conversationId, completedPlan),
            evt);
    }
}

internal static class PlanToolMetadataKeys
{
    public const string Apply = "plan.apply";
    public const string Event = "plan.event";
}
