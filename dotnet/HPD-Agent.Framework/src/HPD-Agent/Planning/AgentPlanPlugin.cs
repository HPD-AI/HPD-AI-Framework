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
/// Plans are automatically persisted to Thread.MiddlewareState at the end of each run
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

    public AgentPlanToolHarness()
    {
    }

    public AgentPlanToolHarness(ILogger<AgentPlanToolHarness>? logger)
    {
        _logger = logger;
    }

    /// <summary>Manages the current execution plan through one typed action contract.</summary>
    [AIFunction(Name = "plan", InvocationModePolicy = AgentInvocationModePolicy.SynchronousOnly)]
    [Description("Track multi-step work using create, updateStep, addStep, addNote, or complete. The current plan is supplied in context. Continue executing work while tracking progress; completing a plan does not complete a persistent Goal.")]
    public Task<object> PlanAsync(
        [Description("The plan action and its action-specific arguments.")] PlanAction operation,
        FunctionExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation switch
        {
            CreatePlanAction action => CreatePlanAsync(action.Goal, action.Steps, context),
            UpdatePlanStepAction action when Enum.IsDefined(action.Status) =>
                UpdatePlanStepAsync(action.StepId, action.Status, context, action.Notes),
            UpdatePlanStepAction => Result("Error: Invalid plan step status."),
            AddPlanStepAction action => AddPlanStepAsync(action.Description, context, action.AfterStepId),
            AddPlanNoteAction action => AddContextNoteAsync(action.Note, context),
            CompletePlanAction => CompletePlanAsync(context),
            _ => throw new ArgumentException("Unknown plan action.", nameof(operation))
        };
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

    private Task<object> CreatePlanAsync(
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

        var planState = context.Analyze(s => s.MiddlewareState.PlanModePersistent());
        if (planState?.HasActivePlan(conversationId) == true)
        {
            var activePlan = planState.GetPlan(conversationId)!;
            return Result(
                $"Error: Plan {activePlan.Id} is still active for this conversation. Complete it before creating another plan.");
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
            $"Created plan {plan.Id} with {plan.Steps.Count} steps:\n{stepList}\n\nUse plan with action updateStep to mark progress.",
            state => state.WithPlan(conversationId, plan),
            evt);
    }

    private Task<object> UpdatePlanStepAsync(
        [Description("The step ID to update (e.g., '1', '2', '3')")] string stepId,
        PlanStepStatus status,
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
            return Result("Error: No active plan exists for this conversation. Create a plan first using plan with action create.");
        }

        var plan = planState.GetPlan(conversationId);
        if (plan == null)
        {
            return Result("Error: No active plan exists for this conversation. Create a plan first using plan with action create.");
        }

        // Check if step exists
        var existingStep = plan.GetStep(stepId);
        if (existingStep == null)
        {
            return Result($"Error: Step '{stepId}' not found in current plan.");
        }

        var oldStatus = existingStep.Status.ToString();
        var updatedPlan = plan.WithUpdatedStep(stepId, status, notes);
        var evt = new PlanUpdatedEvent(
            PlanId: plan.Id,
            ConversationId: conversationId,
            UpdateType: PlanUpdateType.StepUpdated,
            Plan: updatedPlan,
            Explanation: $"Updated step {stepId} from {oldStatus} to {status}" + (notes != null ? $": {notes}" : ""),
            UpdatedAt: DateTimeOffset.UtcNow);

        _logger?.LogInformation("Updated step {StepId} to {Status} for conversation {ConversationId}", stepId, status, conversationId);

        var response = $"Updated step {stepId} to {status}";
        if (notes != null)
        {
            response += $" with notes: {notes}";
        }
        return Result(
            context,
            response,
            state => ApplyToActivePlan(
                state,
                conversationId,
                current => current.WithUpdatedStep(stepId, status, notes)),
            evt);
    }

    private Task<object> AddPlanStepAsync(
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
            return Result("Error: No active plan exists for this conversation. Create a plan first using plan with action create.");
        }

        var plan = planState.GetPlan(conversationId);
        if (plan == null)
        {
            return Result("Error: No active plan exists for this conversation.");
        }

        if (afterStepId != null && plan.GetStep(afterStepId) == null)
        {
            return Result($"Error: Step '{afterStepId}' not found in current plan.");
        }

        var updatedPlan = plan.WithAddedStep(description, afterStepId);
        var newStepId = (plan.Steps.Count + 1).ToString();
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
            state => ApplyToActivePlan(
                state,
                conversationId,
                current => current.WithAddedStep(description, afterStepId)),
            evt);
    }

    private Task<object> AddContextNoteAsync(
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
            return Result("Error: No active plan exists for this conversation. Create a plan first using plan with action create.");
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
            state => ApplyToActivePlan(
                state,
                conversationId,
                current => current.WithContextNote(note)),
            evt);
    }

    // Note: GetCurrentPlanAsync() removed - the plan is automatically injected into every request
    // via AgentPlanAgentMiddleware, so the agent always has the current plan in context without needing
    // to call a function. This saves tokens and simplifies the API.

    private Task<object> CompletePlanAsync(FunctionExecutionContext context)
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
            return Result($"Error: Plan has incomplete steps: {incompleteList}. Mark them as completed before completing the plan.");
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
            state => ApplyToActivePlan(
                state,
                conversationId,
                static current => current.AsCompleted()),
            evt);
    }

    private static PlanModePersistentStateData ApplyToActivePlan(
        PlanModePersistentStateData state,
        string conversationId,
        Func<AgentPlanData, AgentPlanData> update)
    {
        var current = state.GetPlan(conversationId);
        return current is null || current.IsComplete
            ? state
            : state.WithPlan(conversationId, update(current));
    }
}

internal static class PlanToolMetadataKeys
{
    public const string Apply = "plan.apply";
    public const string Event = "plan.event";
}
