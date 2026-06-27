#if NET10_0_OR_GREATER
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.Moonshot;
using HPD.Agent.Providers.OpenAICompatible;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Agent.Providers.Tests;

public class MoonshotProviderTests
{
    private readonly MoonshotProvider _provider = new();

    [Fact]
    public void Provider_ShouldHaveCorrectMetadata()
    {
        var metadata = _provider.GetMetadata();

        metadata.ProviderKey.Should().Be("moonshot");
        metadata.DisplayName.Should().Be("Moonshot");
        metadata.DocumentationUri.Should().Be(new Uri("https://platform.moonshot.ai/docs/"));

        var chat = metadata.Families[ProviderClientFamily.Chat];
        chat.DefaultModelId.Should().Be("kimi-k2.5");
        chat.Capabilities!["SupportsStreaming"].Should().Be(true);
        chat.Capabilities["SupportsFunctionCalling"].Should().Be(true);
        chat.Capabilities["SupportsJsonResponseFormat"].Should().Be(true);
        chat.Capabilities["SupportsThinking"].Should().Be(true);
    }

    [Fact]
    public void Provider_ShouldImplementChatOnly()
    {
        _provider.Should().BeAssignableTo<IChatClientProvider>();
        _provider.Should().NotBeAssignableTo<IEmbeddingGeneratorProvider>();
    }

    [Fact]
    public void ProviderDiscovery_ShouldRegisterProviderConfigAndSecretAliases()
    {
        MoonshotProviderModule.Initialize();

        ProviderDiscovery.GetProviderConfigType("moonshot").Should().NotBeNull();
        SecretAliasRegistry.GetAll()["moonshot:ApiKey"].Should().Equal("MOONSHOT_API_KEY", "KIMI_API_KEY");
        SecretAliasRegistry.GetAll()["moonshot:Endpoint"].Should().Equal(
            "MOONSHOT_ENDPOINT",
            "MOONSHOT_BASE_URL",
            "KIMI_ENDPOINT",
            "KIMI_BASE_URL");
    }

    [Fact]
    public void ValidateConfiguration_WithValidConfig_ShouldSucceed()
    {
        var config = ValidConfig();
        config.Endpoint = "https://api.moonshot.ai/v1/";
        config.SetProviderConfig(new MoonshotProviderConfig
        {
            Temperature = 0.7f,
            TopP = 0.9f,
            MaxOutputTokens = 1024,
            Seed = 123,
            StopSequences = ["END"],
            ResponseFormat = "json_object",
            ToolChoice = "required",
            ThinkingType = "enabled",
            ThinkingKeep = "all"
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
            ProviderKey = "moonshot",
            ModelName = "kimi-k2.5"
        };

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("API key") && e.Contains("MOONSHOT_API_KEY") && e.Contains("KIMI_API_KEY"));
    }

    [Fact]
    public void ValidateConfiguration_WithUnsupportedFamily_ShouldFail()
    {
        var result = _provider.ValidateConfiguration(ValidConfig(), ProviderClientFamily.Embeddings);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("chat provider family"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidOptions_ShouldFail()
    {
        var config = ValidConfig();
        config.SetProviderConfig(new MoonshotProviderConfig
        {
            Temperature = 2.1f,
            TopP = -0.1f,
            MaxOutputTokens = 0,
            StopSequences = [""],
            ResponseFormat = "xml",
            ToolChoice = "named",
            ThinkingType = "maximum",
            ThinkingKeep = "forever"
        });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Temperature"));
        result.Errors.Should().Contain(e => e.Contains("TopP"));
        result.Errors.Should().Contain(e => e.Contains("MaxOutputTokens"));
        result.Errors.Should().Contain(e => e.Contains("StopSequences"));
        result.Errors.Should().Contain(e => e.Contains("ResponseFormat"));
        result.Errors.Should().Contain(e => e.Contains("ToolChoice"));
        result.Errors.Should().Contain(e => e.Contains("ThinkingType"));
        result.Errors.Should().Contain(e => e.Contains("ThinkingKeep"));
    }

    [Fact]
    public void CreateChatClient_WithFakeSecret_ShouldCreateClientWithDefaultEndpointAndModel()
    {
        var config = new ClientProviderConfig
        {
            ProviderKey = "moonshot",
            ApiKey = "test-key"
        };

        using var chatClient = _provider.CreateChatClient(config, CreateServices());

        chatClient.Should().NotBeNull();
        chatClient.GetService(typeof(ChatClientMetadata))
            .Should()
            .BeOfType<ChatClientMetadata>()
            .Which.DefaultModelId.Should().Be("kimi-k2.5");

        chatClient.GetService(typeof(HttpClient))
            .Should()
            .BeOfType<HttpClient>()
            .Which.BaseAddress.Should().Be(new Uri("https://api.moonshot.ai/v1/"));
    }

    [Fact]
    public void CreateChatClient_ShouldResolveKimiAliasesFromEnvironment()
    {
        MoonshotProviderModule.Initialize();
        global::System.Environment.SetEnvironmentVariable("KIMI_API_KEY", "env-key");
        global::System.Environment.SetEnvironmentVariable("KIMI_ENDPOINT", "https://proxy.example/v1");

        try
        {
            using var chatClient = _provider.CreateChatClient(
                new ClientProviderConfig
                {
                    ProviderKey = "moonshot",
                    ModelName = "moonshot-v1-128k"
                },
                CreateEnvironmentServices());

            chatClient.GetService(typeof(HttpClient))
                .Should()
                .BeOfType<HttpClient>()
                .Which.BaseAddress.Should().Be(new Uri("https://proxy.example/v1/"));
        }
        finally
        {
            global::System.Environment.SetEnvironmentVariable("KIMI_API_KEY", null);
            global::System.Environment.SetEnvironmentVariable("KIMI_ENDPOINT", null);
        }
    }

    [Fact]
    public void WithMoonshot_ShouldConfigureBuilder()
    {
        var builder = new AgentBuilder()
            .WithMoonshot(
                model: "kimi-k2.6",
                apiKey: "test-key",
                endpoint: "https://api.moonshot.ai/v1/",
                configure: options =>
                {
                    options.Temperature = 0.2f;
                    options.TopP = 0.8f;
                    options.MaxOutputTokens = 512;
                    options.ResponseFormat = "json_object";
                    options.ToolChoice = "required";
                    options.ThinkingType = "enabled";
                    options.ThinkingKeep = "all";
                    options.StopSequences = ["stop"];
                });

        var config = builder.Config.Clients?.Chat;

        config.Should().NotBeNull();
        config!.ProviderKey.Should().Be("moonshot");
        config.ModelName.Should().Be("kimi-k2.6");
        config.ApiKey.Should().Be("test-key");
        config.Endpoint.Should().Be("https://api.moonshot.ai/v1/");

        var moonshotConfig = config.GetProviderConfig<MoonshotProviderConfig>();
        moonshotConfig.Should().NotBeNull();
        moonshotConfig!.Temperature.Should().Be(0.2f);
        moonshotConfig.TopP.Should().Be(0.8f);
        moonshotConfig.MaxOutputTokens.Should().Be(512);
        moonshotConfig.ResponseFormat.Should().Be("json_object");
        moonshotConfig.ToolChoice.Should().Be("required");
        moonshotConfig.ThinkingType.Should().Be("enabled");
        moonshotConfig.ThinkingKeep.Should().Be("all");
        moonshotConfig.StopSequences.Should().Equal("stop");
    }

    [Fact]
    public void MoonshotJsonContext_ShouldSerializeAndDeserializeConfig()
    {
        var config = new MoonshotProviderConfig
        {
            Temperature = 0.3f,
            TopP = 0.95f,
            MaxOutputTokens = 2048,
            Seed = 12345,
            StopSequences = ["END", "STOP"],
            ResponseFormat = "json_object",
            ToolChoice = "none",
            ThinkingType = "enabled",
            ThinkingKeep = "all"
        };

        var json = JsonSerializer.Serialize(config, MoonshotJsonContext.Default.MoonshotProviderConfig);
        var roundTrip = JsonSerializer.Deserialize(json, MoonshotJsonContext.Default.MoonshotProviderConfig);

        roundTrip.Should().BeEquivalentTo(config);
    }

    [Fact]
    public void ProviderDiscovery_ConfigJsonRoundTrips()
    {
        MoonshotProviderModule.Initialize();
        var config = new MoonshotProviderConfig
        {
            Temperature = 0.4f,
            ResponseFormat = "json_object",
            ThinkingType = "disabled"
        };

        var json = ProviderDiscovery.SerializeProviderConfig("moonshot", config);
        var roundTrip = ProviderDiscovery.DeserializeProviderConfig("moonshot", json);

        roundTrip.Should().BeEquivalentTo(config);
    }

    [Fact]
    public async Task MoonshotChatClient_ShouldApplyDefaultModelOptionsAndThinking()
    {
        var handler = new CapturingHandler("""
            {"id":"chatcmpl-1","model":"kimi-k2.6","created":10,"choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}
            """);
        using var client = new MoonshotChatClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.moonshot.ai/v1/") },
            new OpenAICompatibleChatClientOptions
            {
                ProviderKey = "moonshot",
                DisplayName = "Moonshot",
                ProviderUri = new Uri("https://api.moonshot.ai/v1/"),
                DefaultModelId = "kimi-k2.6"
            },
            new MoonshotProviderConfig
            {
                Temperature = 0.6f,
                TopP = 0.8f,
                MaxOutputTokens = 256,
                Seed = 99,
                StopSequences = ["END"],
                ResponseFormat = "json_object",
                ToolChoice = "required",
                ThinkingType = "enabled",
                ThinkingKeep = "all"
            });

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        using var request = JsonDocument.Parse(handler.RequestBody);
        request.RootElement.GetProperty("model").GetString().Should().Be("kimi-k2.6");
        request.RootElement.GetProperty("temperature").GetDouble().Should().BeApproximately(0.6, 0.001);
        request.RootElement.GetProperty("top_p").GetDouble().Should().BeApproximately(0.8, 0.001);
        request.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(256);
        request.RootElement.GetProperty("seed").GetInt64().Should().Be(99);
        request.RootElement.GetProperty("stop")[0].GetString().Should().Be("END");
        request.RootElement.GetProperty("response_format").GetProperty("type").GetString().Should().Be("json_object");
        request.RootElement.GetProperty("tool_choice").GetString().Should().Be("required");
        request.RootElement.GetProperty("thinking").GetProperty("type").GetString().Should().Be("enabled");
        request.RootElement.GetProperty("thinking").GetProperty("keep").GetString().Should().Be("all");
    }

    [Fact]
    public void ErrorHandler_ShouldClassifyAuthAndRateLimit()
    {
        var handler = new MoonshotErrorHandler();

        var auth = handler.ParseError(new HttpRequestException(
            """Moonshot API request failed [Status: 401 Unauthorized]. Response: {"error":{"code":"invalid_api_key","message":"bad key"}}""",
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
            ProviderKey = "moonshot",
            ModelName = "kimi-k2.5",
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
#endif
