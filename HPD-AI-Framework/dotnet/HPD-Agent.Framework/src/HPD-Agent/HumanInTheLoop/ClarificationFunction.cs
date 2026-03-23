using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using HPD.Agent;

/// <summary>
/// Provides a clarification function that enables parent/orchestrator agents to ask users for
/// additional information during execution. This supports human-in-the-loop workflows where
/// sub-agents return questions that the parent agent cannot answer on its own.
/// </summary>
public static class ClarificationFunction
{
    /// <summary>
    /// Creates an AIFunction that allows parent/orchestrator agents to request clarification from the user
    /// mid-turn. This function emits clarification events that bubble up to the root agent's event handlers,
    /// enabling the user to provide answers without ending the current message turn.
    /// </summary>
    /// <param name="options">Optional function configuration options</param>
    /// <param name="timeout">Maximum time to wait for user response. Defaults to 5 minutes.</param>
    /// <returns>An AIFunction that can be registered on parent/orchestrator agents</returns>
    /// <remarks>
    /// Usage example:
    /// <code>
    /// var orchestrator = new Agent(...);
    /// var codingAgent = new Agent(...);
    ///
    /// // Register sub-agent and clarification function on PARENT
    /// orchestrator.AddFunction(codingAgent.AsAIFunction());
    /// orchestrator.AddFunction(ClarificationFunction.Create(timeout: TimeSpan.FromMinutes(10)));
    ///
    /// // Flow:
    /// // 1. Orchestrator calls codingAgent("Build auth")
    /// // 2. CodingAgent returns: "I need to know which framework?"
    /// // 3. Orchestrator doesn't know, so it calls AskUserForClarification("Which framework?")
    /// // 4. User responds: "Express"
    /// // 5. Orchestrator continues in same turn, calls codingAgent("Build Express auth")
    /// </code>
    /// </remarks>
    public static AIFunction Create(AIFunctionFactoryOptions? options = null, TimeSpan? timeout = null)
    {
        [Description("Ask the user for clarification or additional information needed to complete the task. Only use if sub-agents asks you a question you cannot answer.")]
        async Task<string> AskUserForClarificationAsync(
            [Description("The question to ask the user. Be specific and clear about what information you need.")]
            string question,
            CancellationToken cancellationToken)
        {
            // V2: Get the current function execution context (HookContext with Emit/WaitForResponseAsync)
            var context = Agent.CurrentFunctionContext;

            if (context == null)
            {
                throw new InvalidOperationException(
                    "AskUserForClarification can only be called from within an agent function execution context. " +
                    "Ensure this function is registered with an agent that has proper context setup.");
            }

            if (string.IsNullOrWhiteSpace(question))
            {
                throw new ArgumentException("Question cannot be empty", nameof(question));
            }

            // Generate unique request ID for correlation
            var requestId = Guid.NewGuid().ToString();

            // Emit clarification request event (will bubble to root agent/orchestrator)
            // Include the agent name so UI can show which agent is asking
            context.Emit(new ClarificationRequestEvent(
                requestId,
                SourceName: "ClarificationFunction",
                question,
                AgentName: context.AgentName,
                Options: null));

            // Wait for user's response (blocks here while event is processed)
            ClarificationResponseEvent response;
            var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(5);
            try
            {
                response = await context.WaitForResponseAsync<ClarificationResponseEvent>(
                    requestId,
                    timeout: effectiveTimeout);
            }
            catch (TimeoutException)
            {
                return $"  Clarification request timed out after {effectiveTimeout.TotalMinutes} minutes. Please proceed with available information or ask the user to respond more promptly.";
            }
            catch (OperationCanceledException)
            {
                return "  Clarification request was cancelled. Please proceed with available information.";
            }

            // Return the user's answer
            return response.Answer;
        }

        options ??= new AIFunctionFactoryOptions();
        options.Name ??= "AskUserForClarification";
        options.Description ??= "Ask the user for clarification or additional information when needed to complete a task.";

        return AIFunctionFactory.Create(AskUserForClarificationAsync, options);
    }
}
