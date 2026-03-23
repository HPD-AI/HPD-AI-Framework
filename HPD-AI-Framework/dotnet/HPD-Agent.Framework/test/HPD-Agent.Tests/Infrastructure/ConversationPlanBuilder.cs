using Microsoft.Extensions.AI;
using System.Collections.Generic;

namespace HPD.Agent.Tests.Infrastructure;

/// <summary>
/// Fluent API for scripting conversations using Microsoft's Plan Pattern.
/// Allows building test scenarios with expected messages and responses.
/// </summary>
public class ConversationPlanBuilder
{
    private readonly List<ChatMessage> _messages = new();
    private readonly List<ChatResponse> _plannedResponses = new();
    private string? _lastToolCallId;

    /// <summary>
    /// Adds a user message to the conversation.
    /// </summary>
    public ConversationPlanBuilder User(string message)
    {
        _messages.Add(new ChatMessage(ChatRole.User, message));
        return this;
    }

    /// <summary>
    /// Adds an assistant message with text content.
    /// </summary>
    public ConversationPlanBuilder Assistant(string message)
    {
        _messages.Add(new ChatMessage(ChatRole.Assistant, message));
        return this;
    }

    /// <summary>
    /// Adds an assistant message with custom content (for tool calls, etc.).
    /// </summary>
    public ConversationPlanBuilder Assistant(params AIContent[] contents)
    {
        _messages.Add(new ChatMessage(ChatRole.Assistant, contents));
        return this;
    }

    /// <summary>
    /// Adds a tool call to the conversation.
    /// This creates an assistant message with a function call.
    /// </summary>
    public ConversationPlanBuilder ToolCall(string name, Dictionary<string, object?>? args = null)
    {
        _lastToolCallId = $"call_{Guid.NewGuid():N}";

        var functionCall = new FunctionCallContent(
            callId: _lastToolCallId,
            name: name,
            arguments: args);

        _messages.Add(new ChatMessage(ChatRole.Assistant, new AIContent[] { functionCall }));
        return this;
    }

    /// <summary>
    /// Adds a tool result message to the conversation.
    /// </summary>
    /// <param name="callId">The call ID from the tool call. If null, uses the last tool call ID.</param>
    /// <param name="result">The result to return from the tool.</param>
    public ConversationPlanBuilder ToolResult(string? callId, object result)
    {
        var actualCallId = callId ?? _lastToolCallId ?? throw new InvalidOperationException(
            "No call ID provided and no previous tool call found. Either provide a callId or call ToolCall() first.");

        var resultContent = new FunctionResultContent(actualCallId, result);
        _messages.Add(new ChatMessage(ChatRole.Tool, new AIContent[] { resultContent }));
        return this;
    }

    /// <summary>
    /// Adds a tool result using the last tool call's ID.
    /// </summary>
    public ConversationPlanBuilder ToolResult(object result)
    {
        return ToolResult(null, result);
    }

    /// <summary>
    /// Adds a planned response that the fake LLM should return.
    /// </summary>
    public ConversationPlanBuilder ExpectResponse(ChatResponse response)
    {
        _plannedResponses.Add(response);
        return this;
    }

    /// <summary>
    /// Adds a planned text response that the fake LLM should return.
    /// </summary>
    public ConversationPlanBuilder ExpectTextResponse(string text)
    {
        var response = new ChatResponse(
            [new ChatMessage(ChatRole.Assistant, text)]);

        _plannedResponses.Add(response);
        return this;
    }

    /// <summary>
    /// Adds a planned tool call response that the fake LLM should return.
    /// </summary>
    public ConversationPlanBuilder ExpectToolCallResponse(string name, Dictionary<string, object?>? args = null)
    {
        var callId = $"call_{Guid.NewGuid():N}";
        _lastToolCallId = callId;

        var functionCall = new FunctionCallContent(
            callId: callId,
            name: name,
            arguments: args);

        var response = new ChatResponse(
            [new ChatMessage(ChatRole.Assistant, [functionCall])]);

        _plannedResponses.Add(response);
        return this;
    }

    /// <summary>
    /// Builds the conversation plan.
    /// </summary>
    public ConversationPlan Build()
    {
        return new ConversationPlan(
            _messages.ToArray(),
            _plannedResponses.ToArray());
    }
}

/// <summary>
/// Represents a planned conversation with initial messages and expected responses.
/// </summary>
public class ConversationPlan
{
    public ConversationPlan(
        IReadOnlyList<ChatMessage> initialMessages,
        IReadOnlyList<ChatResponse> plannedResponses)
    {
        InitialMessages = initialMessages;
        PlannedResponses = plannedResponses;
    }

    /// <summary>
    /// Initial messages to start the conversation with (usually just the user message).
    /// </summary>
    public IReadOnlyList<ChatMessage> InitialMessages { get; }

    /// <summary>
    /// Planned responses that the fake LLM should return in order.
    /// </summary>
    public IReadOnlyList<ChatResponse> PlannedResponses { get; }

    /// <summary>
    /// All messages in the conversation (for verification).
    /// </summary>
    public IReadOnlyList<ChatMessage> AllMessages => InitialMessages;
}
