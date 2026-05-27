using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

internal static class BranchMessageEventConverter
{
    public static IReadOnlyList<AgentEvent> ToBranchEvents(
        string sessionId,
        string branchId,
        ChatMessage message,
        string? messageTurnId = null,
        int iteration = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentNullException.ThrowIfNull(message);

        message.MessageId ??= Guid.NewGuid().ToString();
        message.CreatedAt ??= DateTimeOffset.UtcNow;

        var messageId = message.MessageId;
        var role = message.Role.Value;
        var events = new List<AgentEvent>
        {
            BranchEventFactory.MessageStarted(sessionId, branchId, message)
        };

        foreach (var content in CoalesceTextContents(message.Contents.ToList()))
        {
            switch (content)
            {
                case TextReasoningContent reasoning:
                    events.Add(BranchEventFactory.ReasoningStarted(
                        sessionId,
                        branchId,
                        messageTurnId,
                        messageId,
                        role,
                        iteration));
                    if (!string.IsNullOrEmpty(reasoning.Text) || reasoning.ProtectedData != null)
                    {
                        events.Add(BranchEventFactory.ReasoningDelta(
                            sessionId,
                            branchId,
                            messageTurnId,
                            messageId,
                            reasoning.Text,
                            reasoning.ProtectedData,
                            iteration));
                    }
                    events.Add(BranchEventFactory.ReasoningCompleted(
                        sessionId,
                        branchId,
                        messageTurnId,
                        messageId,
                        iteration));
                    break;

                case TextContent text:
                    events.Add(BranchEventFactory.TextMessageStarted(
                        sessionId,
                        branchId,
                        messageTurnId,
                        messageId,
                        role,
                        iteration));
                    if (!string.IsNullOrEmpty(text.Text))
                    {
                        events.Add(BranchEventFactory.TextDelta(
                            sessionId,
                            branchId,
                            messageTurnId,
                            messageId,
                            text.Text,
                            iteration));
                    }
                    events.Add(BranchEventFactory.TextMessageCompleted(
                        sessionId,
                        branchId,
                        messageTurnId,
                        messageId,
                        iteration));
                    break;

                case FunctionCallContent call:
                    var argsJson = call.Arguments is { Count: > 0 }
                        ? JsonSerializer.Serialize(call.Arguments, SessionJsonContext.Combined.Options)
                        : "{}";
                    events.Add(BranchEventFactory.ToolCallStarted(
                        sessionId,
                        branchId,
                        messageTurnId,
                        call.CallId,
                        call.Name,
                        messageId,
                        null,
                        null,
                        iteration));
                    events.Add(BranchEventFactory.ToolCallArgs(
                        sessionId,
                        branchId,
                        messageTurnId,
                        call.CallId,
                        argsJson,
                        iteration));
                    events.Add(BranchEventFactory.ToolCallCompleted(
                        sessionId,
                        branchId,
                        messageTurnId,
                        call.CallId,
                        iteration));
                    break;

                case FunctionResultContent result:
                    events.Add(BranchEventFactory.ToolCallResult(
                        sessionId,
                        branchId,
                        messageTurnId,
                        result.CallId,
                        messageId,
                        ToolResultPayload.FromResult(result.Result),
                        null,
                        null,
                        iteration));
                    break;

                default:
                    events.Add(BranchEventFactory.ContentAdded(sessionId, branchId, messageId, content));
                    break;
            }
        }

        events.Add(BranchEventFactory.MessageCompleted(sessionId, branchId, messageId));
        return events;
    }

    public static List<AIContent> CoalesceTextContents(List<AIContent> contents)
    {
        if (contents.Count <= 1)
            return contents;

        var result = new List<AIContent>();
        var textBuilder = new System.Text.StringBuilder();
        var reasoningBuilder = new System.Text.StringBuilder();
        string? reasoningProtectedData = null;

        void FlushReasoning()
        {
            if (reasoningBuilder.Length > 0 || reasoningProtectedData != null)
            {
                result.Add(new TextReasoningContent(reasoningBuilder.ToString())
                {
                    ProtectedData = reasoningProtectedData
                });
                reasoningBuilder.Clear();
                reasoningProtectedData = null;
            }
        }

        void FlushText()
        {
            if (textBuilder.Length > 0)
            {
                result.Add(new TextContent(textBuilder.ToString()));
                textBuilder.Clear();
            }
        }

        foreach (var content in contents)
        {
            if (content is TextReasoningContent reasoningContent)
            {
                FlushText();

                if (reasoningProtectedData != null)
                    FlushReasoning();

                reasoningBuilder.Append(reasoningContent.Text);

                if (!string.IsNullOrEmpty(reasoningContent.ProtectedData))
                {
                    reasoningProtectedData = reasoningContent.ProtectedData;
                    FlushReasoning();
                }
            }
            else if (content is TextContent textContent)
            {
                FlushReasoning();
                textBuilder.Append(textContent.Text);
            }
            else
            {
                FlushText();
                FlushReasoning();
                result.Add(content);
            }
        }

        FlushText();
        FlushReasoning();

        return result;
    }
}
