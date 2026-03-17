using Microsoft.Extensions.Logging;
using System.ComponentModel;
using HPD.Agent;

namespace HPD.Agent.Planning;

/// <summary>
/// HPD-Agent AI Toolkit for Plan Mode management.
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
/// Uses Agent.CurrentFunctionContext to access the HookContext, which provides:
/// - Analyze() for safe state reads
/// - UpdateState() for immutable state updates
/// - ConversationId for plan scoping
/// </para>
/// </remarks>
public class AgentPlanToolkit
{
    private readonly ILogger<AgentPlanToolkit>? _logger;

    public AgentPlanToolkit(ILogger<AgentPlanToolkit>? logger = null)
    {
        _logger = logger;
    }

    [AIFunction]
    [Description("Create a new execution plan to track progress on multi-step tasks. Use when you need to plan and track complex work.")]
    public Task<string> CreatePlanAsync(
        [Description("The goal or objective this plan aims to accomplish")] string goal,
        [Description("Array of step descriptions (e.g., ['Analyze code', 'Refactor auth', 'Run tests'])")] string[] steps)
    {
        var context = Agent.CurrentFunctionContext;
        if (context == null)
        {
            return Task.FromResult("Error: No execution context available.");
        }

        var conversationId = context.ConversationId;
        if (string.IsNullOrEmpty(conversationId))
        {
            return Task.FromResult("Error: No conversation ID available.");
        }

        if (string.IsNullOrEmpty(goal))
        {
            return Task.FromResult("Error: Goal is required for creating a plan.");
        }

        if (steps == null || steps.Length == 0)
        {
            return Task.FromResult("Error: At least one step is required for creating a plan.");
        }

        // Create the new plan using the immutable helper
        var plan = PlanModePersistentStateData.CreatePlan(goal, steps);

        // Update middleware state with the new plan for this conversation
        context.UpdateState(s =>
        {
            var planState = s.MiddlewareState.PlanModePersistent() ?? new PlanModePersistentStateData();
            var updatedPlanState = planState.WithPlan(conversationId, plan);

            return s with
            {
                MiddlewareState = s.MiddlewareState.WithPlanModePersistent(updatedPlanState)
            };
        });

        // Emit consolidated plan updated event for external observability
        context.Emit(new PlanUpdatedEvent(
            PlanId: plan.Id,
            ConversationId: conversationId,
            UpdateType: PlanUpdateType.Created,
            Plan: plan,
            Explanation: $"Created plan with goal '{goal}' and {plan.Steps.Count} steps",
            UpdatedAt: DateTimeOffset.UtcNow));

        var stepList = string.Join("\n", plan.Steps.Select((step, i) => $"  {step.Id}. {step.Description}"));
        _logger?.LogInformation("Created plan {PlanId} for conversation {ConversationId} with goal: {Goal}", plan.Id, conversationId, goal);

        return Task.FromResult($"Created plan {plan.Id} with {plan.Steps.Count} steps:\n{stepList}\n\nUse update_plan_step() to mark progress.");
    }

    [AIFunction]
    [Description("Update the status of a specific step in the current plan. Use this as you make progress.")]
    public Task<string> UpdatePlanStepAsync(
        [Description("The step ID to update (e.g., '1', '2', '3')")] string stepId,
        [Description("The new status: 'pending', 'in_progress', 'completed', or 'blocked'")] string status,
        [Description("Optional notes about this step's progress, findings, or blockers")] string? notes = null)
    {
        var context = Agent.CurrentFunctionContext;
        if (context == null)
        {
            return Task.FromResult("Error: No execution context available.");
        }

        var conversationId = context.ConversationId;
        if (string.IsNullOrEmpty(conversationId))
        {
            return Task.FromResult("Error: No conversation ID available.");
        }

        if (string.IsNullOrEmpty(stepId))
        {
            return Task.FromResult("Error: Step ID is required.");
        }

        // Check if plan exists for this conversation
        var planState = context.Analyze(s => s.MiddlewareState.PlanModePersistent());
        if (planState == null || !planState.HasActivePlan(conversationId))
        {
            return Task.FromResult("Error: No active plan exists for this conversation. Create a plan first using create_plan().");
        }

        var plan = planState.GetPlan(conversationId);
        if (plan == null)
        {
            return Task.FromResult("Error: No active plan exists for this conversation. Create a plan first using create_plan().");
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
            return Task.FromResult($"Error: Invalid status '{status}'. Use: pending, in_progress, completed, or blocked.");
        }

        // Check if step exists
        var existingStep = plan.GetStep(stepId);
        if (existingStep == null)
        {
            return Task.FromResult($"Error: Step '{stepId}' not found in current plan.");
        }

        var oldStatus = existingStep.Status.ToString();
        AgentPlanData? updatedPlan = null;

        // Update the step immutably
        context.UpdateState(s =>
        {
            var currentPlanState = s.MiddlewareState.PlanModePersistent();
            if (currentPlanState == null)
                return s;

            var currentPlan = currentPlanState.GetPlan(conversationId);
            if (currentPlan == null)
                return s;

            updatedPlan = currentPlan.WithUpdatedStep(stepId, parsedStatus.Value, notes);
            var updatedPlanState = currentPlanState.WithPlan(conversationId, updatedPlan);

            return s with
            {
                MiddlewareState = s.MiddlewareState.WithPlanModePersistent(updatedPlanState)
            };
        });

        // Emit consolidated plan updated event for external observability
        context.Emit(new PlanUpdatedEvent(
            PlanId: plan.Id,
            ConversationId: conversationId,
            UpdateType: PlanUpdateType.StepUpdated,
            Plan: updatedPlan!,
            Explanation: $"Updated step {stepId} from {oldStatus} to {parsedStatus.Value}" + (notes != null ? $": {notes}" : ""),
            UpdatedAt: DateTimeOffset.UtcNow));

        _logger?.LogInformation("Updated step {StepId} to {Status} for conversation {ConversationId}", stepId, parsedStatus, conversationId);

        var response = $"Updated step {stepId} to {parsedStatus}";
        if (notes != null)
        {
            response += $" with notes: {notes}";
        }
        return Task.FromResult(response);
    }

    [AIFunction]
    [Description("Add a new step to the current plan. Use this when you discover additional work is needed.")]
    public Task<string> AddPlanStepAsync(
        [Description("Description of the new step to add")] string description,
        [Description("Optional: ID of step to insert after (e.g., '2'). If omitted, adds to end.")] string? afterStepId = null)
    {
        var context = Agent.CurrentFunctionContext;
        if (context == null)
        {
            return Task.FromResult("Error: No execution context available.");
        }

        var conversationId = context.ConversationId;
        if (string.IsNullOrEmpty(conversationId))
        {
            return Task.FromResult("Error: No conversation ID available.");
        }

        if (string.IsNullOrEmpty(description))
        {
            return Task.FromResult("Error: Step description is required.");
        }

        // Check if plan exists for this conversation
        var planState = context.Analyze(s => s.MiddlewareState.PlanModePersistent());
        if (planState == null || !planState.HasActivePlan(conversationId))
        {
            return Task.FromResult("Error: No active plan exists for this conversation. Create a plan first using create_plan().");
        }

        var plan = planState.GetPlan(conversationId);
        if (plan == null)
        {
            return Task.FromResult("Error: No active plan exists for this conversation.");
        }

        string? newStepId = null;
        AgentPlanData? updatedPlan = null;

        // Add the step immutably
        context.UpdateState(s =>
        {
            var currentPlanState = s.MiddlewareState.PlanModePersistent();
            if (currentPlanState == null)
                return s;

            var currentPlan = currentPlanState.GetPlan(conversationId);
            if (currentPlan == null)
                return s;

            updatedPlan = currentPlan.WithAddedStep(description, afterStepId);
            newStepId = updatedPlan.Steps.LastOrDefault()?.Id; // Get the new step ID
            var updatedPlanState = currentPlanState.WithPlan(conversationId, updatedPlan);

            return s with
            {
                MiddlewareState = s.MiddlewareState.WithPlanModePersistent(updatedPlanState)
            };
        });

        // Emit consolidated plan updated event for external observability
        context.Emit(new PlanUpdatedEvent(
            PlanId: plan.Id,
            ConversationId: conversationId,
            UpdateType: PlanUpdateType.StepAdded,
            Plan: updatedPlan!,
            Explanation: $"Added step {newStepId}: {description}" + (afterStepId != null ? $" after step {afterStepId}" : ""),
            UpdatedAt: DateTimeOffset.UtcNow));

        _logger?.LogInformation("Added step {StepId}: {Description} for conversation {ConversationId}", newStepId, description, conversationId);

        return Task.FromResult($"Added step {newStepId}: {description}");
    }

    [AIFunction]
    [Description("Add a context note to the current plan. Use this to record important discoveries, learnings, or context.")]
    public Task<string> AddContextNoteAsync(
        [Description("The note to add (e.g., 'Discovered auth uses JWT not sessions')")] string note)
    {
        var context = Agent.CurrentFunctionContext;
        if (context == null)
        {
            return Task.FromResult("Error: No execution context available.");
        }

        var conversationId = context.ConversationId;
        if (string.IsNullOrEmpty(conversationId))
        {
            return Task.FromResult("Error: No conversation ID available.");
        }

        if (string.IsNullOrEmpty(note))
        {
            return Task.FromResult("Error: Note content is required.");
        }

        // Check if plan exists for this conversation
        var planState = context.Analyze(s => s.MiddlewareState.PlanModePersistent());
        if (planState == null || !planState.HasActivePlan(conversationId))
        {
            return Task.FromResult("Error: No active plan exists for this conversation. Create a plan first using create_plan().");
        }

        var plan = planState.GetPlan(conversationId);
        if (plan == null)
        {
            return Task.FromResult("Error: No active plan exists for this conversation.");
        }

        AgentPlanData? updatedPlan = null;

        // Add the context note immutably
        context.UpdateState(s =>
        {
            var currentPlanState = s.MiddlewareState.PlanModePersistent();
            if (currentPlanState == null)
                return s;

            var currentPlan = currentPlanState.GetPlan(conversationId);
            if (currentPlan == null)
                return s;

            updatedPlan = currentPlan.WithContextNote(note);
            var updatedPlanState = currentPlanState.WithPlan(conversationId, updatedPlan);

            return s with
            {
                MiddlewareState = s.MiddlewareState.WithPlanModePersistent(updatedPlanState)
            };
        });

        // Emit consolidated plan updated event for external observability
        context.Emit(new PlanUpdatedEvent(
            PlanId: plan.Id,
            ConversationId: conversationId,
            UpdateType: PlanUpdateType.NoteAdded,
            Plan: updatedPlan!,
            Explanation: $"Added context note: {note}",
            UpdatedAt: DateTimeOffset.UtcNow));

        _logger?.LogInformation("Added context note for conversation {ConversationId}: {Note}", conversationId, note);

        return Task.FromResult($"Added context note: {note}");
    }

    // Note: GetCurrentPlanAsync() removed - the plan is automatically injected into every request
    // via AgentPlanAgentMiddleware, so the agent always has the current plan in context without needing
    // to call a function. This saves tokens and simplifies the API.

    [AIFunction]
    [Description("Mark the entire plan as complete. Use this when all steps are done and the goal is achieved.")]
    public Task<string> CompletePlanAsync()
    {
        var context = Agent.CurrentFunctionContext;
        if (context == null)
        {
            return Task.FromResult("Error: No execution context available.");
        }

        var conversationId = context.ConversationId;
        if (string.IsNullOrEmpty(conversationId))
        {
            return Task.FromResult("Error: No conversation ID available.");
        }

        // Check if plan exists for this conversation
        var planState = context.Analyze(s => s.MiddlewareState.PlanModePersistent());
        if (planState == null || !planState.HasActivePlan(conversationId))
        {
            return Task.FromResult("Error: No active plan exists for this conversation.");
        }

        var plan = planState.GetPlan(conversationId);
        if (plan == null)
        {
            return Task.FromResult("Error: No active plan exists for this conversation.");
        }

        var incompleteSteps = plan.Steps.Where(s => s.Status != PlanStepStatus.Completed).ToList();

        if (incompleteSteps.Count > 0)
        {
            var incompleteList = string.Join(", ", incompleteSteps.Select(s => s.Id));
            return Task.FromResult($"Warning: Plan has incomplete steps: {incompleteList}. Mark them as completed first or complete anyway?");
        }

        AgentPlanData? completedPlan = null;

        // Mark plan as complete immutably
        context.UpdateState(s =>
        {
            var currentPlanState = s.MiddlewareState.PlanModePersistent();
            if (currentPlanState == null)
                return s;

            var currentPlan = currentPlanState.GetPlan(conversationId);
            if (currentPlan == null)
                return s;

            completedPlan = currentPlan.AsCompleted();
            var updatedPlanState = currentPlanState.WithPlan(conversationId, completedPlan);

            return s with
            {
                MiddlewareState = s.MiddlewareState.WithPlanModePersistent(updatedPlanState)
            };
        });

        // Emit consolidated plan updated event for external observability
        context.Emit(new PlanUpdatedEvent(
            PlanId: plan.Id,
            ConversationId: conversationId,
            UpdateType: PlanUpdateType.Completed,
            Plan: completedPlan!,
            Explanation: $"Completed plan: {plan.Goal}",
            UpdatedAt: DateTimeOffset.UtcNow));

        _logger?.LogInformation("Completed plan {PlanId} for conversation {ConversationId}: {Goal}", plan.Id, conversationId, plan.Goal);

        return Task.FromResult($"Plan {plan.Id} marked as complete! Goal '{plan.Goal}' achieved.");
    }
}
