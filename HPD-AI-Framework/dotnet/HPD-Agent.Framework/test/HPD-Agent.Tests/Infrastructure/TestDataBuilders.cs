using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Tests.Infrastructure;

/// <summary>
/// Factory methods for creating common test data.
/// Reduces boilerplate in tests and ensures consistent test data patterns.
///
/// NOTE: This is a PARTIAL implementation that works with the current architecture.
/// State builders (AgentLoopState, AgentConfiguration) will be added after refactoring.
/// </summary>
public static class TestDataBuilders
{
    //      
    // MESSAGE BUILDERS
    //      

    /// <summary>
    /// Creates a user message with the given text.
    /// </summary>
    public static ChatMessage UserMessage(string text)
    {
        return new ChatMessage(ChatRole.User, text);
    }

    /// <summary>
    /// Creates an assistant message with the given text.
    /// </summary>
    public static ChatMessage AssistantMessage(string text)
    {
        return new ChatMessage(ChatRole.Assistant, text);
    }

    /// <summary>
    /// Creates an assistant message with a tool call.
    /// </summary>
    public static ChatMessage AssistantWithToolCall(string name, Dictionary<string, object?>? args = null)
    {
        var callId = $"call_{Guid.NewGuid():N}";
        var functionCall = new FunctionCallContent(callId, name, args);
        return new ChatMessage(ChatRole.Assistant, new AIContent[] { functionCall });
    }

    /// <summary>
    /// Creates a tool result message.
    /// </summary>
    public static ChatMessage ToolResult(string callId, object result)
    {
        var resultContent = new FunctionResultContent(callId, result);
        return new ChatMessage(ChatRole.Tool, new AIContent[] { resultContent });
    }

    /// <summary>
    /// Creates a system message with the given text.
    /// </summary>
    public static ChatMessage SystemMessage(string text)
    {
        return new ChatMessage(ChatRole.System, text);
    }

    //      
    // RESPONSE BUILDERS
    //      

    /// <summary>
    /// Creates a ChatResponse with text content.
    /// </summary>
    public static ChatResponse ResponseWithText(string text)
    {
        return new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
    }

    /// <summary>
    /// Creates a ChatResponse with a single tool call.
    /// </summary>
    public static ChatResponse ResponseWithToolCall(string name, Dictionary<string, object?>? args = null)
    {
        var callId = $"call_{Guid.NewGuid():N}";
        var functionCall = new FunctionCallContent(callId, name, args);
        return new ChatResponse([new ChatMessage(ChatRole.Assistant, [functionCall])]);
    }

    /// <summary>
    /// Creates a ChatResponse with multiple tool calls.
    /// </summary>
    public static ChatResponse ResponseWithToolCalls(params (string name, Dictionary<string, object?>? args)[] calls)
    {
        var contents = new List<AIContent>();

        foreach (var (name, args) in calls)
        {
            var callId = $"call_{Guid.NewGuid():N}";
            contents.Add(new FunctionCallContent(callId, name, args));
        }

        return new ChatResponse([new ChatMessage(ChatRole.Assistant, contents.ToArray())]);
    }

    /// <summary>
    /// Creates a ChatResponse with both text and a tool call.
    /// </summary>
    public static ChatResponse ResponseWithTextAndToolCall(
        string text,
        string toolName,
        Dictionary<string, object?>? args = null)
    {
        var callId = $"call_{Guid.NewGuid():N}";
        var contents = new AIContent[]
        {
            new TextContent(text),
            new FunctionCallContent(callId, toolName, args)
        };

        return new ChatResponse([new ChatMessage(ChatRole.Assistant, contents)]);
    }

    //      
    // FUNCTION BUILDERS
    //      

    /// <summary>
    /// Creates a simple AIFunction with no parameters that returns a string.
    /// </summary>
    public static AIFunction SimpleFunction(string name, Func<string> implementation)
    {
        return AIFunctionFactory.Create(
            (Func<string>)implementation,
            name: name);
    }

    /// <summary>
    /// Creates an AIFunction with a single parameter.
    /// </summary>
    public static AIFunction FunctionWithArgs<T>(string name, Func<T, string> implementation)
    {
        return AIFunctionFactory.Create(
            implementation,
            name: name);
    }

    /// <summary>
    /// Creates an async AIFunction with no parameters.
    /// </summary>
    public static AIFunction AsyncFunction(string name, Func<System.Threading.Tasks.Task<string>> implementation)
    {
        return AIFunctionFactory.Create(
            implementation,
            name: name);
    }

    /// <summary>
    /// Creates an async AIFunction with parameters.
    /// </summary>
    public static AIFunction AsyncFunctionWithArgs<T>(string name, Func<T, System.Threading.Tasks.Task<string>> implementation)
    {
        return AIFunctionFactory.Create(
            implementation,
            name: name);
    }

    /// <summary>
    /// Creates an AIFunction that always throws an exception (for error testing).
    /// </summary>
    public static AIFunction FailingFunction(string name, string errorMessage = "Test error")
    {
        Func<string> failFunc = () => throw new InvalidOperationException(errorMessage);
        return AIFunctionFactory.Create(
            failFunc,
            name: name);
    }

    /// <summary>
    /// Creates an AIFunction that returns after a delay (for timeout testing).
    /// </summary>
    public static AIFunction DelayedFunction(string name, TimeSpan delay, string result = "Done")
    {
        return AIFunctionFactory.Create(
            async () =>
            {
                await System.Threading.Tasks.Task.Delay(delay);
                return result;
            },
            name: name);
    }

    //      
    // CONVERSATION BUILDERS
    //      

    /// <summary>
    /// Creates a simple conversation with one user message.
    /// </summary>
    public static ChatMessage[] SimpleConversation(string userMessage)
    {
        return [UserMessage(userMessage)];
    }

    /// <summary>
    /// Creates a multi-turn conversation.
    /// </summary>
    public static ChatMessage[] MultiTurnConversation(params (ChatRole role, string text)[] turns)
    {
        var messages = new List<ChatMessage>();

        foreach (var (role, text) in turns)
        {
            messages.Add(new ChatMessage(role, text));
        }

        return messages.ToArray();
    }

    //      
    // NOTE: State and Config builders will be added after refactor
    //      
    //
    // The following builders require types that don't exist yet:
    // - AgentLoopState InitialState(params ChatMessage[] messages)
    // - AgentLoopState StateAfterIterations(int count)
    // - AgentConfiguration DefaultConfiguration()
    // - AgentConfiguration ConfigurationWithMaxIterations(int max)
    //
    // These will be added in Phase 1 after the refactor introduces
    // AgentLoopState and potentially renames AgentConfig to AgentConfiguration.
}
