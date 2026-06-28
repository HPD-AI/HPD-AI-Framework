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
    public void BuildRequestBody_WithFunctionResult_EmitsToolMessage()
    {
        var request = BuildRequest([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "result")]),
            new ChatMessage(ChatRole.User, "next")
        ]);

        request.Messages.Should().ContainSingle(message => message.Role == "tool")
            .Which.ToolCallId.Should().Be("call-1");
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

    [Fact]
    public void BuildRequestBody_WithReasoningUnset_DoesNotForceReasoningForReasoningModel()
    {
        var request = BuildRequest(
            [new ChatMessage(ChatRole.User, "hello")],
            options: null,
            modelId: "deepseek/deepseek-r1");

        request.Reasoning.Should().BeNull();
    }

    [Fact]
    public void BuildRequestBody_WithReasoningOff_DisablesOpenRouterReasoning()
    {
        var request = BuildRequest(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions
            {
                Reasoning = new Microsoft.Extensions.AI.ReasoningOptions
                {
                    Effort = Microsoft.Extensions.AI.ReasoningEffort.None
                }
            },
            modelId: "deepseek/deepseek-r1");

        request.Reasoning.Should().NotBeNull();
        request.Reasoning!.Enabled.Should().BeNull();
        request.Reasoning.Effort.Should().Be("none");
    }

    [Fact]
    public void BuildRequestBody_WithReasoningEffort_MapsMeaiReasoningToOpenRouterReasoning()
    {
        var request = BuildRequest(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions
            {
                Reasoning = new Microsoft.Extensions.AI.ReasoningOptions
                {
                    Effort = Microsoft.Extensions.AI.ReasoningEffort.High,
                    Output = Microsoft.Extensions.AI.ReasoningOutput.Summary
                }
            },
            modelId: "deepseek/deepseek-r1");

        request.Reasoning.Should().NotBeNull();
        request.Reasoning!.Enabled.Should().BeNull();
        request.Reasoning.Effort.Should().Be("high");
        request.Reasoning.Summary.Should().Be("concise");
    }

    [Fact]
    public void BuildRequestBody_WithReasoningAdditionalProperty_AppliesExplicitReasoningEffort()
    {
        var request = BuildRequest(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions
            {
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["reasoning_effort"] = "medium"
                }
            },
            modelId: "openrouter/custom-model");

        request.Reasoning.Should().NotBeNull();
        request.Reasoning!.Enabled.Should().BeTrue();
        request.Reasoning.Effort.Should().Be("medium");
    }

    private static OpenRouterChatRequest BuildRequest(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        string modelId = "openai/gpt-5")
    {
        using var http = new HttpClient { BaseAddress = new Uri("https://openrouter.ai/api/v1/") };
        var client = new OpenRouterChatClient(http, modelId);
        var method = typeof(OpenRouterChatClient).GetMethod(
            "BuildRequestBody",
            BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull();
        return (OpenRouterChatRequest)method!.Invoke(client, [messages, options, true])!;
    }
}
