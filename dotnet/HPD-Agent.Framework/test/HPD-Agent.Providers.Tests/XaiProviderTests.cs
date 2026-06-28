using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.OpenAICompatible;
using HPD.Agent.Providers.Xai;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Agent.Providers.Tests;

public class XaiProviderTests
{
    private readonly XaiProvider _provider = new();

    [Fact]
    public void Provider_ShouldHaveCorrectMetadata()
    {
        var metadata = _provider.GetMetadata();

        metadata.ProviderKey.Should().Be("xai");
        metadata.DisplayName.Should().Be("xAI");
        metadata.DocumentationUri.Should().Be(new Uri("https://docs.x.ai/"));

        var chat = metadata.Families[ProviderClientFamily.Chat];
        chat.DefaultModelId.Should().Be("grok-4.3");
        chat.Capabilities!["SupportsStreaming"].Should().Be(true);
        chat.Capabilities["SupportsFunctionCalling"].Should().Be(true);
        chat.Capabilities["SupportsJsonResponseFormat"].Should().Be(true);
        chat.Capabilities["SupportsReasoningEffort"].Should().Be(true);
    }

    [Fact]
    public void Provider_ShouldImplementChatOnly()
    {
        _provider.Should().BeAssignableTo<IChatClientProvider>();
        _provider.Should().NotBeAssignableTo<IEmbeddingGeneratorProvider>();
    }

    [Fact]
    public void ProviderContributionRegistry_ShouldRegisterProviderAndConfigType()
    {
        XaiProviderModule.Initialize();

        ProviderContributionRegistry.GetProviderConfigType("xai").Should().NotBeNull();
    }

    [Fact]
    public void ValidateConfiguration_WithValidConfig_ShouldSucceed()
    {
        var config = ValidConfig();
        config.SetProviderConfig(new XaiProviderConfig
        {
            Temperature = 0.7f,
            TopP = 0.9f,
            MaxOutputTokens = 1024,
            Seed = 123,
            StopSequences = ["END"],
            ResponseFormat = "json_object",
            ToolChoice = "required",
            ReasoningEffort = "high"
        });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateConfiguration_WithMissingApiKey_ShouldFail()
    {
        var config = new ClientProviderConfig
        {
            ProviderKey = "xai",
            ModelName = "grok-4.3"
        };

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("API key") && e.Contains("XAI_API_KEY"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidEndpoint_ShouldFail()
    {
        var config = ValidConfig();
        config.Endpoint = "not a uri";

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Endpoint"));
    }

    [Fact]
    public void ValidateConfiguration_WithUnsupportedFamily_ShouldFail()
    {
        var result = _provider.ValidateConfiguration(ValidConfig(), ProviderClientFamily.Embeddings);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("chat provider family"));
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(2.1f)]
    public void ValidateConfiguration_WithInvalidTemperature_ShouldFail(float temperature)
    {
        var config = ValidConfig();
        config.SetProviderConfig(new XaiProviderConfig { Temperature = temperature });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Temperature must be between 0 and 2"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidReasoningEffort_ShouldFail()
    {
        var config = ValidConfig();
        config.SetProviderConfig(new XaiProviderConfig { ReasoningEffort = "maximum" });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ReasoningEffort"));
    }

    [Fact]
    public void WithXai_ShouldConfigureBuilder()
    {
        var builder = new AgentBuilder()
            .WithXai(
                model: "grok-4.3",
                apiKey: "test-key",
                endpoint: "https://example.test/v1",
                configure: options =>
                {
                    options.Temperature = 0.2f;
                    options.TopP = 0.8f;
                    options.MaxOutputTokens = 512;
                    options.ResponseFormat = "json_object";
                    options.ToolChoice = "required";
                    options.ReasoningEffort = "high";
                    options.StopSequences = ["stop"];
                });

        var config = builder.Config.Clients?.Chat;

        config.Should().NotBeNull();
        config!.ProviderKey.Should().Be("xai");
        config.ModelName.Should().Be("grok-4.3");
        config.ApiKey.Should().Be("test-key");
        config.Endpoint.Should().Be("https://example.test/v1");

        var xaiConfig = config.GetProviderConfig<XaiProviderConfig>();
        xaiConfig.Should().NotBeNull();
        xaiConfig!.Temperature.Should().Be(0.2f);
        xaiConfig.TopP.Should().Be(0.8f);
        xaiConfig.MaxOutputTokens.Should().Be(512);
        xaiConfig.ResponseFormat.Should().Be("json_object");
        xaiConfig.ToolChoice.Should().Be("required");
        xaiConfig.ReasoningEffort.Should().Be("high");
        xaiConfig.StopSequences.Should().Equal("stop");
    }

    [Fact]
    public void XaiJsonContext_ShouldSerializeAndDeserializeConfig()
    {
        var config = new XaiProviderConfig
        {
            Temperature = 0.3f,
            TopP = 0.95f,
            MaxOutputTokens = 2048,
            Seed = 12345,
            StopSequences = ["END", "STOP"],
            ResponseFormat = "json_object",
            ToolChoice = "none",
            ReasoningEffort = "medium"
        };

        var json = JsonSerializer.Serialize(config, XaiJsonContext.Default.XaiProviderConfig);
        var roundTrip = JsonSerializer.Deserialize(json, XaiJsonContext.Default.XaiProviderConfig);

        roundTrip.Should().BeEquivalentTo(config);
    }

    [Fact]
    public void ProviderContributionRegistry_ConfigJsonRoundTrips()
    {
        XaiProviderModule.Initialize();
        var config = new XaiProviderConfig
        {
            Temperature = 0.4f,
            ResponseFormat = "json_object",
            ReasoningEffort = "low"
        };

        var json = ProviderContributionRegistry.SerializeProviderConfig("xai", config);
        var roundTrip = ProviderContributionRegistry.DeserializeProviderConfig("xai", json);

        roundTrip.Should().BeEquivalentTo(config);
    }

    [Fact]
    public void CreateChatClient_WithFakeSecret_ShouldCreateClientWithDefaultEndpointAndModel()
    {
        var config = new ClientProviderConfig
        {
            ProviderKey = "xai",
            ApiKey = "test-key"
        };

        using var chatClient = _provider.CreateChatClient(config, CreateServices());

        chatClient.Should().NotBeNull();
        chatClient.GetService(typeof(ChatClientMetadata))
            .Should()
            .BeOfType<ChatClientMetadata>()
            .Which.DefaultModelId.Should().Be("grok-4.3");

        chatClient.GetService(typeof(HttpClient))
            .Should()
            .BeOfType<HttpClient>()
            .Which.BaseAddress.Should().Be(new Uri("https://api.x.ai/v1/"));
    }

    [Fact]
    public void CreateChatClient_ShouldResolveXaiAliasesFromEnvironment()
    {
        XaiProviderModule.Initialize();
        global::System.Environment.SetEnvironmentVariable("XAI_API_KEY", "env-key");
        global::System.Environment.SetEnvironmentVariable("XAI_ENDPOINT", "https://example.test/v1");

        try
        {
            var config = new ClientProviderConfig
            {
                ProviderKey = "xai",
                ModelName = "grok-4.3"
            };

            using var chatClient = _provider.CreateChatClient(config, CreateEnvironmentServices());

            chatClient.Should().NotBeNull();
            chatClient.GetService(typeof(HttpClient))
                .Should()
                .BeOfType<HttpClient>()
                .Which.BaseAddress.Should().Be(new Uri("https://example.test/v1/"));
        }
        finally
        {
            global::System.Environment.SetEnvironmentVariable("XAI_API_KEY", null);
            global::System.Environment.SetEnvironmentVariable("XAI_ENDPOINT", null);
        }
    }

    [Fact]
    public async Task XaiChatClient_ShouldApplyDefaultModelOptionsAndReasoningEffort()
    {
        var handler = new CapturingHandler("""
            {"id":"chatcmpl-1","model":"grok-4.3","created":10,"choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}
            """);
        using var client = new XaiChatClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.x.ai/v1/") },
            new OpenAICompatibleChatClientOptions
            {
                ProviderKey = "xai",
                DisplayName = "xAI",
                ProviderUri = new Uri("https://api.x.ai/v1/"),
                DefaultModelId = "grok-4.3"
            },
            new XaiProviderConfig
            {
                Temperature = 0.6f,
                TopP = 0.8f,
                MaxOutputTokens = 256,
                Seed = 99,
                StopSequences = ["END"],
                ResponseFormat = "json_object",
                ToolChoice = "required",
                ReasoningEffort = "high"
            });

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        using var request = JsonDocument.Parse(handler.RequestBody);
        request.RootElement.GetProperty("model").GetString().Should().Be("grok-4.3");
        request.RootElement.GetProperty("temperature").GetDouble().Should().BeApproximately(0.6, 0.001);
        request.RootElement.GetProperty("top_p").GetDouble().Should().BeApproximately(0.8, 0.001);
        request.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(256);
        request.RootElement.GetProperty("seed").GetInt64().Should().Be(99);
        request.RootElement.GetProperty("stop")[0].GetString().Should().Be("END");
        request.RootElement.GetProperty("response_format").GetProperty("type").GetString().Should().Be("json_object");
        request.RootElement.GetProperty("tool_choice").GetString().Should().Be("required");
        request.RootElement.GetProperty("reasoning_effort").GetString().Should().Be("high");
    }

    [Fact]
    public void ErrorHandler_ShouldClassifyAuthAndRateLimit()
    {
        var handler = new XaiErrorHandler();

        var auth = handler.ParseError(new HttpRequestException(
            """xAI API request failed [Status: 401 Unauthorized]. Response: {"error":{"code":"invalid_api_key","message":"bad key"}}""",
            null,
            HttpStatusCode.Unauthorized));
        var rate = handler.ParseError(new HttpRequestException("too many requests", null, HttpStatusCode.TooManyRequests));

        auth.Should().NotBeNull();
        auth!.StatusCode.Should().Be(401);
        auth.Category.Should().Be(ErrorCategory.AuthError);
        auth.ErrorCode.Should().Be("invalid_api_key");
        handler.RequiresSpecialHandling(auth).Should().BeTrue();

        rate.Should().NotBeNull();
        rate!.Category.Should().Be(ErrorCategory.RateLimitRetryable);
        handler.GetRetryDelay(rate, 1, TimeSpan.FromMilliseconds(100), 2, TimeSpan.FromSeconds(1))
            .Should()
            .Be(TimeSpan.FromMilliseconds(200));
    }

    private static ClientProviderConfig ValidConfig()
        => new()
        {
            ProviderKey = "xai",
            ModelName = "grok-4.3",
            ApiKey = "test-key"
        };

    private static IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISecretResolver>(new ExplicitSecretResolver());
        return services.BuildServiceProvider();
    }

    private static IServiceProvider CreateEnvironmentServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISecretResolver>(new EnvironmentSecretResolver());
        return services.BuildServiceProvider();
    }

    private sealed class CapturingHandler(
        string responseBody,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string mediaType = "application/json") : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
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
