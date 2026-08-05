using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.DeepSeek;
using HPD.Agent.Providers.OpenAICompatible;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Tests;

public sealed class OpenAICompatibleChatClientBehaviorTests
{
    [Fact]
    public async Task MinimalProfile_OmitsEveryOptionalRequestField()
    {
        var handler = new CapturingHandler("""
            {"id":"chatcmpl-1","model":"minimal","choices":[{"message":{"role":"assistant","content":"done"},"finish_reason":"stop"}]}
            """);
        using var client = CreateClient(handler, new OpenAICompatibleRequestProfile(), "minimal");

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions
            {
                Temperature = 0.2f,
                TopP = 0.8f,
                TopK = 20,
                MaxOutputTokens = 100,
                FrequencyPenalty = 0.1f,
                PresencePenalty = 0.2f,
                StopSequences = ["END"],
                Seed = 42,
                Tools = [new TestFunction("lookup", "Looks up a value")],
                ToolMode = ChatToolMode.Auto,
                AllowMultipleToolCalls = false,
                ResponseFormat = ChatResponseFormat.Json,
                Reasoning = new Microsoft.Extensions.AI.ReasoningOptions
                {
                    Effort = Microsoft.Extensions.AI.ReasoningEffort.High
                }
            });

        using var request = JsonDocument.Parse(handler.RequestBody);
        var root = request.RootElement;
        root.TryGetProperty("temperature", out _).Should().BeFalse();
        root.TryGetProperty("top_p", out _).Should().BeFalse();
        root.TryGetProperty("top_k", out _).Should().BeFalse();
        root.TryGetProperty("max_tokens", out _).Should().BeFalse();
        root.TryGetProperty("max_completion_tokens", out _).Should().BeFalse();
        root.TryGetProperty("frequency_penalty", out _).Should().BeFalse();
        root.TryGetProperty("presence_penalty", out _).Should().BeFalse();
        root.TryGetProperty("stop", out _).Should().BeFalse();
        root.TryGetProperty("seed", out _).Should().BeFalse();
        root.TryGetProperty("tools", out _).Should().BeFalse();
        root.TryGetProperty("tool_choice", out _).Should().BeFalse();
        root.TryGetProperty("parallel_tool_calls", out _).Should().BeFalse();
        root.TryGetProperty("response_format", out _).Should().BeFalse();
        root.TryGetProperty("reasoning_effort", out _).Should().BeFalse();
    }

    [Fact]
    public void MaxCompletionTokensProfile_UsesOnlyProviderSelectedField()
    {
        var profile = new OpenAICompatibleRequestProfile
        {
            MaxTokensField = OpenAICompatibleMaxTokensField.MaxCompletionTokens
        };
        using var client = CreateInspectableClient(profile);

        var request = client.Build(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions { MaxOutputTokens = 512 });

        request.MaxTokens.Should().BeNull();
        request.MaxCompletionTokens.Should().Be(512);
    }

    [Fact]
    public void JsonObjectOnlyProfile_DropsUnsupportedJsonSchemaWithoutDowngrade()
    {
        var profile = new OpenAICompatibleRequestProfile
        {
            JsonObjectResponseFormat = true
        };
        using var client = CreateInspectableClient(profile);
        var schema = JsonDocument.Parse("""{"type":"object"}""").RootElement;

        var request = client.Build(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions
            {
                ResponseFormat = ChatResponseFormat.ForJsonSchema(schema)
            });

        request.ResponseFormat.Should().BeNull();
    }

    [Theory]
    [InlineData(Microsoft.Extensions.AI.ReasoningEffort.None, null, "disabled")]
    [InlineData(Microsoft.Extensions.AI.ReasoningEffort.Low, "high", "enabled")]
    [InlineData(Microsoft.Extensions.AI.ReasoningEffort.Medium, "high", "enabled")]
    [InlineData(Microsoft.Extensions.AI.ReasoningEffort.High, "high", "enabled")]
    [InlineData(Microsoft.Extensions.AI.ReasoningEffort.ExtraHigh, "max", "enabled")]
    public void DeepSeekProfile_TranslatesReasoningToSupportedWireValues(
        Microsoft.Extensions.AI.ReasoningEffort effort,
        string? expectedEffort,
        string expectedThinkingType)
    {
        using var client = CreateInspectableClient(DeepSeekProvider.ChatRequestProfile);

        var request = client.Build(
            [new ChatMessage(ChatRole.User, "reason")],
            new ChatOptions
            {
                Reasoning = new Microsoft.Extensions.AI.ReasoningOptions { Effort = effort }
            });

        request.ReasoningEffort.Should().Be(expectedEffort);
        request.Thinking.Should().NotBeNull();
        request.Thinking!.Type.Should().Be(expectedThinkingType);
    }

    [Fact]
    public void DeepSeekProfile_DropsUnsupportedOptionalFields()
    {
        using var client = CreateInspectableClient(DeepSeekProvider.ChatRequestProfile);

        var request = client.Build(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions
            {
                TopK = 40,
                Seed = 42,
                FrequencyPenalty = 0.2f,
                PresencePenalty = 0.3f,
                AllowMultipleToolCalls = false
            });

        request.TopK.Should().BeNull();
        request.Seed.Should().BeNull();
        request.FrequencyPenalty.Should().BeNull();
        request.PresencePenalty.Should().BeNull();
        request.ParallelToolCalls.Should().BeNull();
        request.ToolChoice.Should().BeNull();
    }

    [Fact]
    public async Task DeepSeekProfile_SerializesDisabledThinkingWithoutNoneEffort()
    {
        var handler = new CapturingHandler("""
            {"id":"chatcmpl-1","model":"deepseek-v4-pro","choices":[{"message":{"role":"assistant","content":"done"},"finish_reason":"stop"}]}
            """);
        using var client = CreateClient(
            handler,
            DeepSeekProvider.ChatRequestProfile,
            "deepseek-v4-pro");

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions
            {
                Reasoning = new Microsoft.Extensions.AI.ReasoningOptions
                {
                    Effort = Microsoft.Extensions.AI.ReasoningEffort.None
                }
            });

        using var request = JsonDocument.Parse(handler.RequestBody);
        request.RootElement.GetProperty("thinking").GetProperty("type").GetString()
            .Should().Be("disabled");
        request.RootElement.TryGetProperty("reasoning_effort", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetResponseAsync_SerializesMessagesToolsOptionsAndResponseFormat()
    {
        var handler = new CapturingHandler("""
            {"id":"chatcmpl-1","model":"test-model","created":10,"choices":[{"index":0,"message":{"role":"assistant","content":"done"},"finish_reason":"stop"}],"usage":{"prompt_tokens":3,"completion_tokens":2,"total_tokens":5}}
            """);
        using var client = CreateClient(handler, defaultModelId: "default-model");
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"answer":{"type":"string"}}}""").RootElement;

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions
            {
                Instructions = "be useful",
                ModelId = "test-model",
                Temperature = 0.25f,
                MaxOutputTokens = 42,
                StopSequences = ["END"],
                Tools = [new TestFunction("lookup", "Looks up a value")],
                ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, "answer_shape", "Answer shape")
            });

        response.Text.Should().Be("done");
        response.Usage?.TotalTokenCount.Should().Be(5);

        using var request = JsonDocument.Parse(handler.RequestBody);
        request.RootElement.GetProperty("model").GetString().Should().Be("test-model");
        request.RootElement.GetProperty("temperature").GetDouble().Should().BeApproximately(0.25, 0.001);
        request.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(42);
        request.RootElement.GetProperty("messages")[0].GetProperty("role").GetString().Should().Be("system");
        request.RootElement.GetProperty("messages")[1].GetProperty("content").GetString().Should().Be("hello");
        request.RootElement.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString().Should().Be("lookup");
        request.RootElement.GetProperty("response_format").GetProperty("type").GetString().Should().Be("json_schema");
        request.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("name").GetString().Should().Be("answer_shape");
    }

    [Fact]
    public async Task GetResponseAsync_WithDetailedChatCompletionsUsage_MapsMeaiUsageDetails()
    {
        var handler = new CapturingHandler("""
            {
              "id":"chatcmpl-usage",
              "model":"reasoning-model",
              "created":10,
              "choices":[{"index":0,"message":{"role":"assistant","content":"done"},"finish_reason":"stop"}],
              "usage":{
                "prompt_tokens":2006,
                "completion_tokens":300,
                "total_tokens":2306,
                "prompt_tokens_details":{"cached_tokens":1920},
                "completion_tokens_details":{
                  "reasoning_tokens":128,
                  "accepted_prediction_tokens":9,
                  "rejected_prediction_tokens":4,
                  "audio_tokens":3
                }
              }
            }
            """);
        using var client = CreateClient(handler, defaultModelId: "reasoning-model");

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "reason")]);

        response.Usage.Should().NotBeNull();
        response.Usage!.InputTokenCount.Should().Be(2006);
        response.Usage.OutputTokenCount.Should().Be(300);
        response.Usage.TotalTokenCount.Should().Be(2306);
        response.Usage.CachedInputTokenCount.Should().Be(1920);
        response.Usage.ReasoningTokenCount.Should().Be(128);
        response.Usage.AdditionalCounts.Should().Contain(new KeyValuePair<string, long>("completion_tokens_details.accepted_prediction_tokens", 9));
        response.Usage.AdditionalCounts.Should().Contain(new KeyValuePair<string, long>("completion_tokens_details.rejected_prediction_tokens", 4));
        response.Usage.AdditionalCounts.Should().Contain(new KeyValuePair<string, long>("completion_tokens_details.audio_tokens", 3));
    }

    [Fact]
    public async Task GetResponseAsync_WithResponsesStyleUsage_MapsMeaiUsageDetails()
    {
        var handler = new CapturingHandler("""
            {
              "id":"chatcmpl-responses-usage",
              "model":"responses-shaped-model",
              "created":10,
              "choices":[{"index":0,"message":{"role":"assistant","content":"done"},"finish_reason":"stop"}],
              "usage":{
                "input_tokens":75,
                "output_tokens":1186,
                "input_tokens_details":{"cached_tokens":10},
                "output_tokens_details":{"reasoning_tokens":1024}
              }
            }
            """);
        using var client = CreateClient(handler, defaultModelId: "responses-shaped-model");

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "reason")]);

        response.Usage.Should().NotBeNull();
        response.Usage!.InputTokenCount.Should().Be(75);
        response.Usage.OutputTokenCount.Should().Be(1186);
        response.Usage.TotalTokenCount.Should().Be(1261);
        response.Usage.CachedInputTokenCount.Should().Be(10);
        response.Usage.ReasoningTokenCount.Should().Be(1024);
    }

    [Fact]
    public async Task GetResponseAsync_PostsToChatCompletionsRelativePath()
    {
        var handler = new CapturingHandler("""
            {"id":"chatcmpl-1","model":"test-model","created":10,"choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}
            """);
        using var client = CreateClient(handler);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        handler.RequestUri.Should().Be(new Uri("https://example.test/v1/chat/completions"));
    }

    [Fact]
    public void BuildRequestBody_WithToolCallAndResultHistory_EmitsAssistantToolCallsAndToolMessages()
    {
        using var client = CreateInspectableClient();

        var request = client.Build([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent("call-1", "ReadFile", new Dictionary<string, object?> { ["path"] = "README.md" })
            ]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "contents")])
        ]);

        var assistant = request.Messages.Single(message => message.Role == "assistant");
        assistant.ToolCalls.Should().ContainSingle()
            .Which.Function.Arguments.Should().Be("""{"path":"README.md"}""");

        var tool = request.Messages.Single(message => message.Role == "tool");
        tool.ToolCallId.Should().Be("call-1");
        tool.Content?.GetString().Should().Be("contents");
    }

    [Fact]
    public void BuildRequestBody_WithMultipleToolsAndPenalties_PreservesOpenAICompatibleOptions()
    {
        using var client = CreateInspectableClient(defaultModelId: "meta/llama3-70b-instruct");

        var request = client.Build(
            [new ChatMessage(ChatRole.User, "weather and time?")],
            new ChatOptions
            {
                Temperature = 0.7f,
                FrequencyPenalty = 0.1f,
                PresencePenalty = 0.5f,
                AllowMultipleToolCalls = false,
                Reasoning = new Microsoft.Extensions.AI.ReasoningOptions
                {
                    Effort = Microsoft.Extensions.AI.ReasoningEffort.High
                },
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["strict"] = true
                },
                ToolMode = ChatToolMode.Auto,
                Tools =
                [
                    new TestFunction("get_weather", "Gets weather"),
                    new TestFunction("get_current_time", "Gets current time")
                ]
            });

        request.Model.Should().Be("meta/llama3-70b-instruct");
        request.Temperature.Should().Be(0.7f);
        request.FrequencyPenalty.Should().Be(0.1f);
        request.PresencePenalty.Should().Be(0.5f);
        request.Tools.Should().HaveCount(2);
        request.Tools!.Select(tool => tool.Function.Name)
            .Should().BeEquivalentTo("get_weather", "get_current_time");
        request.Tools.Should().OnlyContain(tool => tool.Function.Strict == true);
        request.ToolChoice?.GetString().Should().Be("auto");
        request.ParallelToolCalls.Should().BeFalse();
        request.ReasoningEffort.Should().Be("high");
    }

    [Fact]
    public void BuildRequestBody_WithSpecificRequiredTool_EmitsOpenAIToolChoiceObject()
    {
        using var client = CreateInspectableClient();

        var request = client.Build(
            [new ChatMessage(ChatRole.User, "weather?")],
            new ChatOptions
            {
                ToolMode = ChatToolMode.RequireSpecific("get_weather"),
                Tools = [new TestFunction("get_weather", "Gets weather")]
            });

        request.ToolChoice.Should().NotBeNull();
        request.ToolChoice!.Value.GetProperty("type").GetString().Should().Be("function");
        request.ToolChoice.Value.GetProperty("function").GetProperty("name").GetString().Should().Be("get_weather");
    }

    [Fact]
    public void BuildRequestBody_WithImageContent_EmitsOpenAIMultimodalContentParts()
    {
        using var client = CreateInspectableClient();

        var message = new ChatMessage(ChatRole.User, [
            new TextContent("inspect"),
            new UriContent(new Uri("https://example.test/image.png"), "image/png")
            {
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["detail"] = "high"
                }
            },
            new DataContent(new byte[] { 1, 2, 3 }, "image/png")
        ]);

        var request = client.Build([message]);

        var content = request.Messages.Single().Content!.Value;
        content.ValueKind.Should().Be(JsonValueKind.Array);
        content[0].GetProperty("type").GetString().Should().Be("text");
        content[0].GetProperty("text").GetString().Should().Be("inspect");
        content[1].GetProperty("type").GetString().Should().Be("image_url");
        content[1].GetProperty("image_url").GetProperty("url").GetString().Should().Be("https://example.test/image.png");
        content[1].GetProperty("image_url").GetProperty("detail").GetString().Should().Be("high");
        content[2].GetProperty("image_url").GetProperty("url").GetString().Should().StartWith("data:image/png;base64,");
    }

    [Fact]
    public async Task GetResponseAsync_WithReasoningCitationsAndUnknownFinishReason_MapsMeaiContent()
    {
        var handler = new CapturingHandler("""
            {
              "id":"chatcmpl-annotations",
              "model":"test-model",
              "created":12,
              "choices":[{
                "index":0,
                "message":{
                  "role":"assistant",
                  "content":"grounded answer",
                  "reasoning_content":"thinking",
                  "annotations":[{"type":"url_citation","url_citation":{"url":"https://example.test/a","title":"A","start_index":0,"end_index":8}}]
                },
                "finish_reason":"safety"
              }],
              "citations":["https://example.test/b"],
              "search_results":[{"title":"C","url":"https://example.test/c","snippet":"clip"}]
            }
            """);
        using var client = CreateClient(handler);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "ground this")]);

        response.ResponseId.Should().Be("chatcmpl-annotations");
        response.Messages.Single().MessageId.Should().Be("chatcmpl-annotations");
        response.FinishReason.Should().Be(new ChatFinishReason("safety"));
        response.Messages.Single().Contents.OfType<TextReasoningContent>().Single().Text.Should().Be("thinking");

        var text = response.Messages.Single().Contents.OfType<TextContent>().Single();
        text.Annotations.Should().HaveCount(3);
        text.Annotations!.OfType<CitationAnnotation>().Select(annotation => annotation.Url?.ToString())
            .Should().BeEquivalentTo(
                "https://example.test/a",
                "https://example.test/b",
                "https://example.test/c");
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ParsesTextToolCallsUsageAndFinishReason()
    {
        var stream = string.Join("\n", [
            """data: {"id":"chatcmpl-2","model":"test-model","created":11,"choices":[{"index":0,"delta":{"role":"assistant","content":"hel"},"finish_reason":null}]}""",
            """data: {"id":"chatcmpl-2","model":"test-model","created":11,"choices":[{"index":0,"delta":{"reasoning_content":"thinking"},"finish_reason":null}]}""",
            """data: {"id":"chatcmpl-2","model":"test-model","created":11,"choices":[{"index":0,"delta":{"content":"lo"},"finish_reason":null}]}""",
            """data: {"id":"chatcmpl-2","model":"test-model","created":11,"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call-1","type":"function","function":{"name":"Lookup","arguments":"{\"q\""}}]},"finish_reason":null}]}""",
            """data: {"id":"chatcmpl-2","model":"test-model","created":11,"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":":\"x\"}"}}]},"finish_reason":"tool_calls"}]}""",
            """data: {"id":"chatcmpl-2","model":"test-model","created":11,"choices":[],"usage":{"prompt_tokens":1,"completion_tokens":2,"total_tokens":3,"prompt_tokens_details":{"cached_tokens":1},"completion_tokens_details":{"reasoning_tokens":2}}}""",
            "data: [DONE]"
        ]);
        using var client = CreateClient(new CapturingHandler(stream, mediaType: "text/event-stream"));

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")]))
        {
            updates.Add(update);
        }

        updates.SelectMany(update => update.Contents).OfType<TextContent>().Select(content => content.Text)
            .Should().ContainInOrder("hel", "lo");
        updates.Select(update => update.MessageId).Should().OnlyContain(id => id == "chatcmpl-2");
        updates.SelectMany(update => update.Contents).OfType<TextReasoningContent>().Single().Text.Should().Be("thinking");
        var usage = updates.SelectMany(update => update.Contents).OfType<UsageContent>().Single().Details;
        usage.TotalTokenCount.Should().Be(3);
        usage.CachedInputTokenCount.Should().Be(1);
        usage.ReasoningTokenCount.Should().Be(2);
        var call = updates.SelectMany(update => update.Contents).OfType<FunctionCallContent>().Single();
        call.CallId.Should().Be("call-1");
        call.Name.Should().Be("Lookup");
        call.Arguments.Should().ContainKey("q").WhoseValue?.ToString().Should().Be("x");
    }

    [Fact]
    public async Task GetResponseAsync_WithErrorBody_ThrowsProviderExceptionWithBody()
    {
        using var client = CreateClient(new CapturingHandler(
            """{"error":{"message":"bad key","code":"unauthorized"}}""",
            HttpStatusCode.Unauthorized));

        var action = async () => await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        var ex = await action.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ex.Which.Message.Should().Contain("bad key");
    }

    [Fact]
    public async Task ProviderBase_CreateChatClient_ConfiguresEndpointAndAuth()
    {
        var handler = new CapturingHandler("""
            {"id":"chatcmpl-1","model":"default-model","created":10,"choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}
            """);
        var provider = new TestProvider(handler);

        using var client = await provider.CreateChatClientAsync(
            new ProviderClientConfig
            {
                ProviderKey = "test-openai-compatible",
                ModelName = "default-model",
                ApiKey = "test-key",
                Endpoint = "https://override.test/v1"
            });

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        provider.CapturedBaseAddress.Should().Be(new Uri("https://override.test/v1/"));
        provider.CapturedAuthorization.Should().Be(new AuthenticationHeaderValue("Bearer", "test-key"));
    }

    [Fact]
    public void ProviderBase_ValidateConfiguration_AppliesCommonConstructionRules()
    {
        var provider = new TestProvider(new CapturingHandler("{}"));

        var result = provider.ValidateConfiguration(
            new ProviderClientConfig
            {
                ProviderKey = "test-openai-compatible",
                Endpoint = "not-a-uri"
            },
            ProviderClientFamily.Embeddings);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("chat provider family", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(error => error.Contains("Model name", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().NotContain(error => error.Contains("API key", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(error => error.Contains("Endpoint", StringComparison.OrdinalIgnoreCase));
    }

    private static TestChatClient CreateInspectableClient(string defaultModelId = "test-model")
        => new(new HttpClient(new CapturingHandler("{}")) { BaseAddress = new Uri("https://example.test/v1/") }, Options(defaultModelId));

    private static TestChatClient CreateInspectableClient(
        OpenAICompatibleRequestProfile requestProfile,
        string defaultModelId = "test-model")
        => new(
            new HttpClient(new CapturingHandler("{}")) { BaseAddress = new Uri("https://example.test/v1/") },
            Options(defaultModelId, requestProfile));

    private static OpenAICompatibleChatClient CreateClient(CapturingHandler handler, string defaultModelId = "test-model")
        => new(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") }, Options(defaultModelId));

    private static OpenAICompatibleChatClient CreateClient(
        CapturingHandler handler,
        OpenAICompatibleRequestProfile requestProfile,
        string defaultModelId)
        => new(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") },
            Options(defaultModelId, requestProfile));

    private static OpenAICompatibleChatClientOptions Options(
        string defaultModelId,
        OpenAICompatibleRequestProfile? requestProfile = null)
        => new()
        {
            ProviderKey = "test",
            DisplayName = "Test Provider",
            ProviderUri = new Uri("https://example.test"),
            DefaultModelId = defaultModelId,
            RequestProfile = requestProfile ?? OpenAICompatibleRequestProfile.All
        };

    private sealed class TestChatClient(HttpClient httpClient, OpenAICompatibleChatClientOptions options)
        : OpenAICompatibleChatClient(httpClient, options)
    {
        public OpenAICompatibleChatRequest Build(IReadOnlyList<ChatMessage> messages, ChatOptions? options = null, bool stream = false)
            => BuildRequestBody(messages, options, stream);
    }

    private sealed class TestFunction(string name, string description) : AIFunctionDeclaration
    {
        public override string Name { get; } = name;
        public override string Description { get; } = description;
        public override JsonElement JsonSchema { get; } = JsonDocument.Parse("""{"type":"object","properties":{"q":{"type":"string"}}}""").RootElement;
    }

    private sealed class TestProvider(CapturingHandler handler)
        : OpenAICompatibleChatProviderBase<OpenAICompatibleProviderConfig>
    {
        private static readonly OpenAICompatibleProviderDefinition TestDefinition = new()
        {
            ProviderKey = "test-openai-compatible",
            DisplayName = "Test OpenAI Compatible",
            DefaultEndpoint = new Uri("https://default.test/v1/"),
            DefaultModelId = "default-model",
            ApiKeySecretKey = "test-openai-compatible:ApiKey",
            EndpointSecretKey = "test-openai-compatible:Endpoint",
            ProviderUri = new Uri("https://default.test/"),
            DocumentationUri = new Uri("https://default.test/docs"),
            RequestProfile = OpenAICompatibleRequestProfile.All
        };

        protected override OpenAICompatibleProviderDefinition Definition => TestDefinition;

        public Uri? CapturedBaseAddress { get; private set; }

        public AuthenticationHeaderValue? CapturedAuthorization { get; private set; }

        public override IProviderErrorHandler CreateErrorHandler() => new GenericErrorHandler();

        protected override IChatClient CreateOpenAICompatibleChatClient(
            HttpClient httpClient,
            ProviderClientConfig config,
            Uri endpoint)
        {
            CapturedBaseAddress = httpClient.BaseAddress;
            CapturedAuthorization = httpClient.DefaultRequestHeaders.Authorization;

            return new OpenAICompatibleChatClient(
                new HttpClient(handler) { BaseAddress = httpClient.BaseAddress },
                CreateChatClientOptions(config, endpoint));
        }
    }

    private sealed class CapturingHandler(
        string responseBody,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string mediaType = "application/json") : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = string.Empty;

        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, mediaType)
            };
        }
    }
}
