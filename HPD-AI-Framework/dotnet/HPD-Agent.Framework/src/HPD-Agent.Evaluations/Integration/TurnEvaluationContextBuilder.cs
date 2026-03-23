// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.AI;
using HPD.Agent.Middleware;
using HPD.Agent.Evaluations.Tracing;

namespace HPD.Agent.Evaluations.Integration;

/// <summary>
/// Internal builder that constructs TurnEvaluationContext from AfterMessageTurnContext
/// and a TurnEventBuffer populated during the turn by EvaluationMiddleware.
/// </summary>
internal static class TurnEvaluationContextBuilder
{
    internal static TurnEvaluationContext FromAfterMessageTurn(
        AfterMessageTurnContext context,
        TurnEventBuffer buffer,
        EvalContextData evalData,
        string? groundTruth)
    {
        var toolCalls = BuildToolCallRecords(context.TurnHistory, buffer);
        var trace = BuildTurnTrace(context, buffer, toolCalls, context.TurnHistory);

        // Aggregate reasoning text from turn history
        string? reasoningText = AggregateReasoningText(context.TurnHistory);

        // Determine user input from TurnHistory (last user message before assistant)
        string userInput = ExtractUserInput(context.TurnHistory);

        // Conversation history = all messages before the current turn's user message
        var conversationHistory = ExtractConversationHistory(context.TurnHistory);

        // StopKind from output text and finish reason
        var stopKind = InferStopKind(context.FinalResponse);

        return new TurnEvaluationContext
        {
            AgentName = context.AgentName,
            SessionId = context.Session?.Id ?? string.Empty,
            BranchId = context.Branch?.Id ?? string.Empty,
            ConversationId = context.ConversationId ?? string.Empty,
            TurnIndex = CountPriorUserMessages(context.Branch),
            UserInput = userInput,
            ConversationHistory = conversationHistory,
            OutputText = context.FinalResponse.Text ?? string.Empty,
            FinalResponse = context.FinalResponse,
            ReasoningText = reasoningText,
            ToolCalls = toolCalls,
            Trace = trace,
            TurnUsage = context.TurnUsage,
            IterationUsage = context.IterationUsage,
            IterationCount = context.IterationUsage.Count,
            Duration = buffer.TurnDuration,
            ModelId = context.RunConfig.ModelId,
            ProviderKey = context.RunConfig.ProviderKey,
            Attributes = new Dictionary<string, object>(evalData.Attributes),
            Metrics = new Dictionary<string, double>(evalData.Metrics),
            StopKind = stopKind,
            GroundTruth = groundTruth,
            ExperimentContext = context.RunConfig.ContextOverrides,
        };
    }

    private static IReadOnlyList<ToolCallRecord> BuildToolCallRecords(
        List<ChatMessage> turnHistory, TurnEventBuffer buffer)
    {
        var records = new List<ToolCallRecord>();

        foreach (var message in turnHistory)
        {
            foreach (var content in message.Contents.OfType<FunctionCallContent>())
            {
                // Find matching result
                string result = string.Empty;
                foreach (var resultMsg in turnHistory)
                foreach (var resultContent in resultMsg.Contents.OfType<FunctionResultContent>())
                {
                    if (resultContent.CallId == content.CallId)
                    {
                        result = resultContent.Result?.ToString() ?? string.Empty;
                        break;
                    }
                }

                var info = buffer.GetToolCallInfo(content.CallId);
                records.Add(new ToolCallRecord(
                    CallId: content.CallId,
                    Name: content.Name,
                    ToolkitName: info?.ToolkitName,
                    ArgumentsJson: content.Arguments?.ToString() ?? "{}",
                    Result: result,
                    Duration: buffer.GetToolCallDuration(content.CallId),
                    WasPermissionDenied: buffer.WasPermissionDenied(content.CallId)));
            }
        }

        return records;
    }

    private static TurnTrace BuildTurnTrace(
        AfterMessageTurnContext context,
        TurnEventBuffer buffer,
        IReadOnlyList<ToolCallRecord> allToolCalls,
        List<ChatMessage> turnHistory)
    {
        // Build a lookup for fast ToolCallRecord access by CallId
        var toolCallByCallId = allToolCalls.ToDictionary(tc => tc.CallId);

        var iterationNumbers = buffer.GetIterationNumbers();

        List<IterationSpan> iterations;

        if (iterationNumbers.Count == 0)
        {
            // No iteration events buffered (observer may not have fired yet or agent
            // completed in a single synchronous call with no events). Fall back to a
            // single iteration containing all tool calls.
            var allSpans = allToolCalls.Select(tc => new ToolCallSpan
            {
                CallId = tc.CallId,
                Name = tc.Name,
                ToolkitName = tc.ToolkitName,
                ArgumentsJson = tc.ArgumentsJson,
                Result = tc.Result,
                Duration = tc.Duration,
                WasPermissionDenied = tc.WasPermissionDenied,
            }).ToList();

            iterations =
            [
                new()
                {
                    IterationNumber = 1,
                    Usage = context.IterationUsage.Count > 0 ? context.IterationUsage[0] : null,
                    ToolCalls = allSpans,
                    AssistantText = context.FinalResponse.Text,
                    ReasoningText = AggregateReasoningText(turnHistory),
                    FinishReason = context.FinalResponse.FinishReason?.Value,
                    Duration = buffer.TurnDuration,
                }
            ];
        }
        else
        {
            // Build one IterationSpan per recorded iteration.
            // Tool calls are assigned to the iteration whose window contains their start time.
            // Assistant text and reasoning text are extracted from the turn history:
            // TurnHistory assistant messages are ordered chronologically; the i-th assistant
            // message corresponds approximately to the i-th iteration. We index into it by
            // the iteration's position in the sorted list (0-based).
            var assistantMessages = turnHistory
                .Where(m => m.Role == ChatRole.Assistant)
                .ToList();

            iterations = [];
            for (int idx = 0; idx < iterationNumbers.Count; idx++)
            {
                int iterNum = iterationNumbers[idx];
                var callIds = buffer.GetToolCallIdsForIteration(iterNum);

                var spans = callIds
                    .Select(id => toolCallByCallId.TryGetValue(id, out var tc)
                        ? new ToolCallSpan
                        {
                            CallId = tc.CallId,
                            Name = tc.Name,
                            ToolkitName = tc.ToolkitName,
                            ArgumentsJson = tc.ArgumentsJson,
                            Result = tc.Result,
                            Duration = tc.Duration,
                            WasPermissionDenied = tc.WasPermissionDenied,
                        }
                        : null)
                    .Where(s => s is not null)
                    .Cast<ToolCallSpan>()
                    .ToList();

                // Match assistant message to this iteration by position (0-based index)
                var assistantMsg = idx < assistantMessages.Count ? assistantMessages[idx] : null;

                string? assistantText = assistantMsg?.Text;
                string? reasoningText = assistantMsg is not null
                    ? ExtractReasoningFromMessage(assistantMsg)
                    : null;

                // IterationUsage is 0-indexed and aligned to iteration order
                var usage = idx < context.IterationUsage.Count ? context.IterationUsage[idx] : null;

                iterations.Add(new IterationSpan
                {
                    IterationNumber = iterNum,
                    Usage = usage,
                    ToolCalls = spans,
                    AssistantText = assistantText,
                    ReasoningText = reasoningText,
                    // FinishReason only available on the final iteration
                    FinishReason = idx == iterationNumbers.Count - 1
                        ? context.FinalResponse.FinishReason?.Value
                        : null,
                    Duration = buffer.GetIterationDuration(iterNum),
                });
            }
        }

        return new TurnTrace
        {
            MessageTurnId = buffer.MessageTurnId,
            AgentName = context.AgentName,
            StartedAt = buffer.TurnStartedAt,
            Duration = buffer.TurnDuration,
            Iterations = iterations,
        };
    }

    private static string? ExtractReasoningFromMessage(ChatMessage message)
    {
        var parts = message.Contents
            .OfType<TextContent>()
            .Where(t => t.AdditionalProperties?.ContainsKey("type") == true &&
                        t.AdditionalProperties["type"]?.ToString() == "reasoning")
            .Select(t => t.Text ?? string.Empty)
            .Where(t => !string.IsNullOrEmpty(t));

        var result = string.Join("\n", parts);
        return string.IsNullOrEmpty(result) ? null : result;
    }

    private static string? AggregateReasoningText(List<ChatMessage> turnHistory)
    {
        var parts = turnHistory
            .SelectMany(m => m.Contents)
            .OfType<TextContent>()
            .Where(t => t.AdditionalProperties?.ContainsKey("type") == true &&
                        t.AdditionalProperties["type"]?.ToString() == "reasoning")
            .Select(t => t.Text ?? string.Empty)
            .Where(t => !string.IsNullOrEmpty(t));

        var aggregated = string.Join("\n", parts);
        return string.IsNullOrEmpty(aggregated) ? null : aggregated;
    }

    private static string ExtractUserInput(List<ChatMessage> turnHistory)
    {
        // Last user message in the turn history
        return turnHistory
            .LastOrDefault(m => m.Role == ChatRole.User)
            ?.Text ?? string.Empty;
    }

    private static IReadOnlyList<ChatMessage> ExtractConversationHistory(List<ChatMessage> turnHistory)
    {
        // All messages before the last user message
        var lastUserIdx = -1;
        for (int i = turnHistory.Count - 1; i >= 0; i--)
        {
            if (turnHistory[i].Role == ChatRole.User)
            {
                lastUserIdx = i;
                break;
            }
        }

        return lastUserIdx > 0
            ? turnHistory.Take(lastUserIdx).ToList()
            : [];
    }

    private static AgentStopKind InferStopKind(ChatResponse response)
    {
        var text = response.Text ?? string.Empty;

        if (text.TrimEnd().EndsWith('?'))
            return AgentStopKind.AskedClarification;

        // Check finish reason
        var finishReason = response.FinishReason?.Value?.ToLowerInvariant();
        if (finishReason is "stop" or "end_turn")
            return AgentStopKind.Completed;

        return AgentStopKind.Unknown;
    }

    /// <summary>
    /// Reconstructs a list of TurnEvaluationContext objects from a saved Branch's message history.
    /// Used by RetroactiveScorer to evaluate persisted conversations without re-running the agent.
    /// Each user→assistant exchange in the branch becomes one TurnEvaluationContext.
    /// Token usage and trace data are unavailable in retroactive contexts (left null/empty).
    /// </summary>
    internal static IReadOnlyList<TurnEvaluationContext> FromBranch(Branch branch, string agentName)
    {
        var results = new List<TurnEvaluationContext>();
        var messages = branch.Messages;

        // Walk the message list, grouping each user message with the assistant reply that follows it
        int turnIndex = 0;
        for (int i = 0; i < messages.Count; i++)
        {
            if (messages[i].Role != ChatRole.User) continue;

            var userMessage = messages[i];
            var userInput = userMessage.Text ?? string.Empty;

            // Conversation history = all messages before this user message
            var history = messages.Take(i).ToList();

            // Find the assistant response immediately following this user message
            ChatResponse finalResponse;
            IReadOnlyList<ToolCallRecord> toolCalls = [];
            int nextIdx = i + 1;

            // Skip any tool call / result messages between user and final assistant reply
            // The last assistant message before the next user message (or end) is the final response
            ChatMessage? assistantMsg = null;
            for (int j = nextIdx; j < messages.Count; j++)
            {
                if (messages[j].Role == ChatRole.User) break;
                if (messages[j].Role == ChatRole.Assistant)
                    assistantMsg = messages[j];
            }

            if (assistantMsg is null)
            {
                // Incomplete turn (user message with no assistant reply) — skip
                continue;
            }

            // Build a minimal ChatResponse from the assistant message
            finalResponse = new ChatResponse(assistantMsg);

            // Build tool call records from messages between this user turn and the assistant reply
            var turnMessages = new List<ChatMessage>();
            for (int j = nextIdx; j < messages.Count; j++)
            {
                if (messages[j].Role == ChatRole.User) break;
                turnMessages.Add(messages[j]);
            }
            toolCalls = BuildToolCallRecordsFromMessages(turnMessages);

            var stopKind = InferStopKind(finalResponse);

            results.Add(new TurnEvaluationContext
            {
                AgentName = agentName,
                SessionId = branch.SessionId,
                BranchId = branch.Id,
                ConversationId = string.Empty,
                TurnIndex = turnIndex,
                UserInput = userInput,
                ConversationHistory = history,
                OutputText = assistantMsg.Text ?? string.Empty,
                FinalResponse = finalResponse,
                ReasoningText = null,
                ToolCalls = toolCalls,
                Trace = new HPD.Agent.Evaluations.Tracing.TurnTrace
                {
                    MessageTurnId = string.Empty,
                    AgentName = agentName,
                    StartedAt = DateTimeOffset.MinValue,
                    Duration = TimeSpan.Zero,
                    Iterations = [],
                },
                TurnUsage = null,
                IterationUsage = [],
                IterationCount = 0,
                Duration = TimeSpan.Zero,
                ModelId = null,
                ProviderKey = null,
                Attributes = new Dictionary<string, object>(),
                Metrics = new Dictionary<string, double>(),
                StopKind = stopKind,
                GroundTruth = null,
                ExperimentContext = null,
            });

            turnIndex++;
        }

        return results;
    }

    private static IReadOnlyList<ToolCallRecord> BuildToolCallRecordsFromMessages(List<ChatMessage> messages)
    {
        var records = new List<ToolCallRecord>();
        foreach (var message in messages)
        {
            foreach (var content in message.Contents.OfType<FunctionCallContent>())
            {
                string result = string.Empty;
                foreach (var resultMsg in messages)
                foreach (var resultContent in resultMsg.Contents.OfType<FunctionResultContent>())
                {
                    if (resultContent.CallId == content.CallId)
                    {
                        result = resultContent.Result?.ToString() ?? string.Empty;
                        break;
                    }
                }

                records.Add(new ToolCallRecord(
                    CallId: content.CallId,
                    Name: content.Name,
                    ToolkitName: null,
                    ArgumentsJson: content.Arguments?.ToString() ?? "{}",
                    Result: result,
                    Duration: TimeSpan.Zero,
                    WasPermissionDenied: false));
            }
        }
        return records;
    }

    private static int CountPriorUserMessages(Branch? branch)
    {
        if (branch is null) return 0;
        return branch.Messages.Count(m => m.Role == ChatRole.User);
    }
}
