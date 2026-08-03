using System;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using HPD.Agent;
using HPD.Agent.Middleware;

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
        async Task<object?> AskUserForClarificationAsync(
            AIFunctionArguments arguments,
            FunctionExecutionContext context,
            CancellationToken cancellationToken)
        {
            var question = HPDToolArgumentBinder.BindRequired<string>(
                arguments.GetJson(),
                "question",
                arguments.GetJsonSerializerOptions());

            if (string.IsNullOrWhiteSpace(question))
            {
                throw new ArgumentException("Question cannot be empty", nameof(question));
            }

            // Generate unique request ID for correlation
            var requestId = Guid.NewGuid().ToString();

            // Wait for user's response (blocks here while event is processed)
            ClarificationResponseEvent response;
            try
            {
                response = await context.RequestAsync<ClarificationRequestEvent, ClarificationResponseEvent>(
                    new ClarificationRequestEvent(
                        requestId,
                        SourceName: "ClarificationFunction",
                        question,
                        AgentName: context.AgentName,
                        Options: null),
                    cancellationToken,
                    timeout);
            }
            catch (TimeoutException)
            {
                return $"  Clarification request timed out after {timeout!.Value.TotalMinutes} minutes. Please proceed with available information or ask the user to respond more promptly.";
            }
            catch (OperationCanceledException)
            {
                return "  Clarification request was cancelled. Please proceed with available information.";
            }

            // Return the user's answer
            return response.Answer;
        }

        options ??= new AIFunctionFactoryOptions();

        return HPDAIFunctionFactory.Create(
            AskUserForClarificationAsync,
            new HPDAIFunctionFactoryOptions
            {
                Name = options.Name ?? "AskUserForClarification",
                Description = options.Description ?? "Ask the user for clarification or additional information when needed to complete a task.",
                ResultType = typeof(string),
                SchemaProvider = CreateClarificationSchema
            });
    }

    private static JsonElement CreateClarificationSchema()
    {
        using var document = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "question": {
              "type": "string",
              "description": "The question to ask the user. Be specific and clear about what information you need."
            }
          },
          "required": [ "question" ],
          "additionalProperties": false
        }
        """);

        return document.RootElement.Clone();
    }
}
