using FluentAssertions;
using HPD.Agent.Providers.OpenRouter;
using Microsoft.Extensions.AI;
using System.Net.Http;
using System.Reflection;

namespace HPD.Agent.Tests.Providers;

public sealed class OpenRouterChatClientTests
{
    [Fact]
    public void SerializeFunctionArguments_WithNullArguments_ReturnsEmptyJsonObject()
    {
        var json = OpenRouterChatClient.SerializeFunctionArguments(null);

        json.Should().Be("{}");
    }

    [Fact]
    public void SerializeFunctionArguments_WithArguments_ReturnsJsonObject()
    {
        var json = OpenRouterChatClient.SerializeFunctionArguments(
            new Dictionary<string, object?>
            {
                ["command"] = "curl wttr.in/NewYork"
            });

        json.Should().Be("""{"command":"curl wttr.in/NewYork"}""");
    }

    [Fact]
    public void BuildRequestBody_WithOrphanFunctionResult_DropsToolMessage()
    {
        var request = BuildRequest([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-orphan", "result")]),
            new ChatMessage(ChatRole.User, "next")
        ]);

        request.Messages.Should().HaveCount(2);
        request.Messages.Should().NotContain(message => message.Role == "tool");
    }

    [Fact]
    public void BuildRequestBody_WithMatchingFunctionCall_EmitsToolMessage()
    {
        var request = BuildRequest([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent("call-1", "ReadFile", new Dictionary<string, object?> { ["path"] = "README.md" })
            ]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "contents")])
        ]);

        request.Messages.Should().ContainSingle(message => message.Role == "tool")
            .Which.ToolCallId.Should().Be("call-1");
    }

    private static OpenRouterChatRequest BuildRequest(IReadOnlyList<ChatMessage> messages)
    {
        using var http = new HttpClient { BaseAddress = new Uri("https://openrouter.ai/api/v1/") };
        var client = new OpenRouterChatClient(http, "openai/gpt-5");
        var method = typeof(OpenRouterChatClient).GetMethod(
            "BuildRequestBody",
            BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull();
        return (OpenRouterChatRequest)method!.Invoke(client, [messages, null, true])!;
    }
}
