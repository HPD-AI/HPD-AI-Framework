using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace HPD.Agent.Planning;

/// <summary>
/// Injects plan mode instructions and active execution plans into the conversation.
/// </summary>
/// <remarks>
/// <para><b>UNIFIED MIDDLEWARE:</b></para>
/// <para>
/// Implements <see cref="IAgentMiddleware"/> with <see cref="IAgentMiddleware.BeforeMessageTurnAsync"/>
/// to inject plan mode instructions and active plans at the start of each message turn.
/// </para>
///
/// <para><b>Two-Phase Injection:</b></para>
/// <list type="number">
/// <item><b>Instructions (Options.Instructions):</b> General plan mode guidance explaining how to use plan tools.
/// Injected when plan mode is enabled, regardless of whether an active plan exists.</item>
/// <item><b>Active Plan (System Message):</b> Current plan state (goals, steps, progress).
/// Only injected when a plan exists for the current conversation.</item>
/// </list>
///
/// <para><b>Plan Lifecycle:</b></para>
/// <para>
/// Plans are stored in MiddlewareState (PlanModePersistentStateData) keyed by conversation ID.
/// Each conversation can have its own independent plan, supporting parallel workstreams.
/// Plans persist across agent runs within the same session via the automatic session persistence mechanism.
/// </para>
///
/// <para><b>Session Persistence:</b></para>
/// <para>
/// Plans are automatically saved to Branch.MiddlewareState at the end of each run
/// and restored at agent start via LoadFromSession/SaveToSession (source-generated).
/// </para>
/// </remarks>
public class AgentPlanAgentMiddleware : IAgentMiddleware
{
    private readonly PlanModeConfig? _config;
    private readonly ILogger<AgentPlanAgentMiddleware>? _logger;

    public AgentPlanAgentMiddleware(
        PlanModeConfig? config = null,
        ILogger<AgentPlanAgentMiddleware>? logger = null)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Injects plan mode instructions and active plan (if exists) into the conversation.
    /// </summary>
    /// <remarks>
    /// <para><b>Instruction Injection</b></para>
    /// <para>
    /// If plan mode is enabled, appends plan mode instructions to Options.Instructions.
    /// This happens regardless of whether an active plan exists, so the LLM knows how to use plan tools.
    /// </para>
    ///
    /// <para><b>Phase 2: Active Plan Injection</b></para>
    /// <para>
    /// If an active plan exists for the current conversation, injects it as a system message.
    /// This provides the current plan state (goals, steps, progress).
    /// </para>
    /// </remarks>
    public Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        // PHASE 1: Inject plan mode instructions via AdditionalSystemInstructions
        if (_config?.Enabled == true)
        {
            var planModeInstructions = _config.CustomInstructions ?? GetDefaultPlanModeInstructions();

            // Append to existing additional instructions (if any)
            if (string.IsNullOrEmpty(context.RunConfig.AdditionalSystemInstructions))
            {
                context.RunConfig.AdditionalSystemInstructions = planModeInstructions;
            }
            else if (!context.RunConfig.AdditionalSystemInstructions.Contains("[PLAN MODE ENABLED]"))
            {
                // Only add if not already present
                context.RunConfig.AdditionalSystemInstructions += "\n\n" + planModeInstructions;
            }

            _logger?.LogDebug("Injected plan mode instructions for agent {AgentName}", context.AgentName);
        }

        // PHASE 2: Inject active plan (if exists) from MiddlewareState
        var conversationId = context.ConversationId;
        if (string.IsNullOrEmpty(conversationId))
        {
            return Task.CompletedTask;
        }

        var planState = context.Analyze(s => s.MiddlewareState.PlanModePersistent());

        // Check if we have an active plan for this conversation
        if (planState == null || !planState.HasActivePlan(conversationId))
        {
            return Task.CompletedTask;
        }

        var plan = planState.GetPlan(conversationId);
        if (plan == null)
        {
            return Task.CompletedTask;
        }

        var planPrompt = plan.BuildPlanPrompt();
        if (string.IsNullOrEmpty(planPrompt))
        {
            return Task.CompletedTask;
        }

        // Inject plan as a system message at the beginning
        var messagesWithPlan = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, planPrompt)
        };
        messagesWithPlan.AddRange(context.ConversationHistory);

        // V2: ConversationHistory is mutable - replace content
        context.ConversationHistory.Clear();
        foreach (var msg in messagesWithPlan)
        {
            context.ConversationHistory.Add(msg);
        }

        _logger?.LogDebug(
            "Injected active plan {PlanId} into prompt for agent {AgentName}, conversation {ConversationId}",
            plan.Id,
            context.AgentName,
            conversationId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets default plan mode instructions explaining how to use plan tools.
    /// Moved from Agent to make plan mode fully middleware-based.
    /// </summary>
    public static string GetDefaultPlanModeInstructions()
    {
        return @"[PLAN MODE ENABLED]
You have access to plan management tools for complex multi-step tasks.

Available functions:
- create_plan(goal, steps[]): Create a new plan with a goal and initial steps
- update_plan_step(stepId, status, notes): Update step status (pending/in_progress/completed/blocked) and add notes
- add_plan_step(description, afterStepId): Add a new step when you discover additional work needed
- add_context_note(note): Record important discoveries, learnings, or context during execution
- complete_plan(): Mark the entire plan as complete when goal is achieved

Best practices:
- Create plans for tasks requiring 3+ steps, affecting multiple files, or with uncertain scope
- Update step status as you progress to maintain context across conversation turns
- Add context notes when discovering important information (e.g., ""Found auth uses JWT, not sessions"")
- Plans are conversation-scoped working memory - they help you maintain progress and avoid repeating failed approaches
- When a step is blocked, mark it as 'blocked' with notes explaining why, then continue with other steps if possible";
    }
}
