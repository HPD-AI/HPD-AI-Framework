using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

internal static class ThreadMessageEventConverter
{
    public static IReadOnlyList<AgentEvent> ToThreadEvents(
        string sessionId,
        string threadId,
        ChatMessage message,
        string? messageTurnId = null,
        int iteration = 0,
        string? clientInputId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(message);

        message.MessageId ??= Guid.NewGuid().ToString();
        message.CreatedAt ??= DateTimeOffset.UtcNow;

        var messageId = message.MessageId;
        var role = message.Role.Value;
        var events = new List<AgentEvent>
        {
            ThreadEventFactory.MessageStarted(sessionId, threadId, message, clientInputId)
        };

        foreach (var content in CoalesceTextContents(message.Contents.ToList()))
        {
            switch (content)
            {
                case TextReasoningContent reasoning:
                    events.Add(ThreadEventFactory.ReasoningStarted(
                        sessionId,
                        threadId,
                        messageTurnId,
                        messageId,
                        role,
                        iteration));
                    if (!string.IsNullOrEmpty(reasoning.Text) || reasoning.ProtectedData != null)
                    {
                        events.Add(ThreadEventFactory.ReasoningDelta(
                            sessionId,
                            threadId,
                            messageTurnId,
                            messageId,
                            reasoning.Text,
                            reasoning.ProtectedData,
                            iteration));
                    }
                    events.Add(ThreadEventFactory.ReasoningCompleted(
                        sessionId,
                        threadId,
                        messageTurnId,
                        messageId,
                        iteration));
                    break;

                case TextContent text:
                    events.Add(ThreadEventFactory.TextMessageStarted(
                        sessionId,
                        threadId,
                        messageTurnId,
                        messageId,
                        role,
                        iteration));
                    if (!string.IsNullOrEmpty(text.Text))
                    {
                        events.Add(ThreadEventFactory.TextDelta(
                            sessionId,
                            threadId,
                            messageTurnId,
                            messageId,
                            text.Text,
                            iteration));
                    }
                    events.Add(ThreadEventFactory.TextMessageCompleted(
                        sessionId,
                        threadId,
                        messageTurnId,
                        messageId,
                        iteration));
                    break;

                case FunctionCallContent call:
                    var argsJson = call.Arguments is { Count: > 0 }
                        ? JsonSerializer.Serialize(call.Arguments, SessionJsonContext.Combined.Options)
                        : "{}";
                    events.Add(ThreadEventFactory.ToolCallStarted(
                        sessionId,
                        threadId,
                        messageTurnId,
                        call.CallId,
                        call.Name,
                        messageId,
                        null,
                        null,
                        iteration));
                    events.Add(ThreadEventFactory.ToolCallArgs(
                        sessionId,
                        threadId,
                        messageTurnId,
                        call.CallId,
                        argsJson,
                        iteration));
                    events.Add(ThreadEventFactory.ToolCallCompleted(
                        sessionId,
                        threadId,
                        messageTurnId,
                        call.CallId,
                        iteration));
                    break;

                case FunctionResultContent result:
                    events.Add(ThreadEventFactory.ToolCallResult(
                        sessionId,
                        threadId,
                        messageTurnId,
                        result.CallId,
                        messageId,
                        ToolResultPayload.FromResult(result.Result),
                        null,
                        null,
                        iteration));
                    break;

                default:
                    events.Add(ThreadEventFactory.ContentAdded(sessionId, threadId, messageId, content));
                    break;
            }
        }

        events.Add(ThreadEventFactory.MessageCompleted(sessionId, threadId, messageId));
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
