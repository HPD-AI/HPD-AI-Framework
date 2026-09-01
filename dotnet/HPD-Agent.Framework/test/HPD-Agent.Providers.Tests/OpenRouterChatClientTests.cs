using FluentAssertions;
using HPD.Agent.Providers.OpenRouter;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;
using System.Net.Http;
using System.Net;
using System.Reflection;
using System.Text;

namespace HPD.Agent.Tests.Providers;

public sealed class OpenRouterChatClientTests
{
    [Fact]
    public async Task Streaming_adapter_emits_one_terminal_usage_snapshot()
    {
        var sse = string.Join("\n", new[]
        {
            """data: {"id":"r1","model":"fixture","created":1,"choices":[{"index":0,"delta":{"role":"assistant","content":"ok"},"finish_reason":null}]}""",
            """data: {"id":"r1","model":"fixture","created":1,"choices":[{"index":0,"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":11,"completion_tokens":7,"total_tokens":18}}""",
            "data: [DONE]"
        });
        using var http = new HttpClient(new SseHandler(sse))
        {
            BaseAddress = new Uri("https://openrouter.ai/api/v1/")
        };
        using var client = new OpenRouterChatClient(http, "fixture");

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new(ChatRole.User, "fixture")]))
            updates.Add(update);

        var usage = updates.SelectMany(static update => update.Contents).OfType<UsageContent>()
            .Should().ContainSingle().Subject.Details;
        usage.InputTokenCount.Should().Be(11);
        usage.OutputTokenCount.Should().Be(7);
        ProviderStreamingUsageSemanticsCatalog.Resolve("openrouter", ProviderClientFamily.Chat)
            .Should().Be(UsageUpdateSemantics.FinalOnly);
    }

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

    private sealed class SseHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/event-stream")
        });
    }
}
