#if NET10_0_OR_GREATER
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.Fireworks;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Agent.Providers.Tests;

public class FireworksProviderTests
{
    private readonly FireworksProvider _provider = new();

    [Fact]
    public void Provider_ShouldHaveCorrectMetadata()
    {
        var metadata = _provider.GetMetadata();

        metadata.ProviderKey.Should().Be("fireworks");
        metadata.DisplayName.Should().Be("Fireworks AI");
        metadata.DocumentationUri.Should().Be(new Uri("https://docs.fireworks.ai/"));

        metadata.Families.Should().ContainKey(ProviderClientFamily.Chat);
        var chat = metadata.Families[ProviderClientFamily.Chat];
        chat.DefaultModelId.Should().Be("accounts/fireworks/models/llama-v3p1-8b-instruct");
        chat.Capabilities!["SupportsStreaming"].Should().Be(true);
        chat.Capabilities["SupportsFunctionCalling"].Should().Be(true);
        chat.Capabilities["SupportsJsonResponseFormat"].Should().Be(true);
    }

    [Fact]
    public void Provider_ShouldImplementChatOnly()
    {
        _provider.Should().BeAssignableTo<IChatClientProvider>();
        _provider.Should().NotBeAssignableTo<IEmbeddingGeneratorProvider>();
    }

    [Fact]
    public void ProviderDiscovery_ShouldRegisterProviderAndConfigType()
    {
        FireworksProviderModule.Initialize();

        ProviderDiscovery.GetProviderConfigType("fireworks").Should().NotBeNull();
    }

    [Fact]
    public void ValidateConfiguration_WithValidConfig_ShouldSucceed()
    {
        var config = ValidConfig();
        config.Endpoint = "https://api.fireworks.ai/inference/v1/";
        config.SetProviderConfig(new FireworksProviderConfig
        {
            Temperature = 0.7,
            TopP = 0.9,
            MaxOutputTokens = 1024,
            Seed = 123,
            StopSequences = ["END"],
            ResponseFormat = "json_object",
            ToolChoice = "required"
        });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateConfiguration_WithMissingModelName_ShouldFail()
    {
        var config = new ClientProviderConfig
        {
            ProviderKey = "fireworks",
            ApiKey = "test-key"
        };

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Model name") && e.Contains("required"));
    }

    [Fact]
    public void ValidateConfiguration_WithMissingApiKey_ShouldFail()
    {
        var config = new ClientProviderConfig
        {
            ProviderKey = "fireworks",
            ModelName = "accounts/fireworks/models/llama-v3p1-8b-instruct"
        };

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("API key") && e.Contains("FIREWORKS_API_KEY"));
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

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.1)]
    public void ValidateConfiguration_WithInvalidTemperature_ShouldFail(double temperature)
    {
        var config = ValidConfig();
        config.SetProviderConfig(new FireworksProviderConfig { Temperature = temperature });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Temperature must be between 0 and 2"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidResponseFormat_ShouldFail()
    {
        var config = ValidConfig();
        config.SetProviderConfig(new FireworksProviderConfig { ResponseFormat = "xml" });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ResponseFormat"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidToolChoice_ShouldFail()
    {
        var config = ValidConfig();
        config.SetProviderConfig(new FireworksProviderConfig { ToolChoice = "maybe" });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ToolChoice"));
    }

    [Fact]
    public void WithFireworks_ShouldConfigureBuilder()
    {
        var builder = new AgentBuilder()
            .WithFireworks(
                model: "accounts/fireworks/models/llama-v3p1-70b-instruct",
                apiKey: "test-key",
                endpoint: "https://example.test/v1/",
                configure: options =>
                {
                    options.Temperature = 0.2;
                    options.TopP = 0.8;
                    options.MaxOutputTokens = 512;
                    options.ResponseFormat = "json_object";
                    options.ToolChoice = "required";
                    options.StopSequences = ["stop"];
                });

        var config = builder.Config.Clients?.Chat;

        config.Should().NotBeNull();
        config!.ProviderKey.Should().Be("fireworks");
        config.ModelName.Should().Be("accounts/fireworks/models/llama-v3p1-70b-instruct");
        config.ApiKey.Should().Be("test-key");
        config.Endpoint.Should().Be("https://example.test/v1/");

        var fireworksConfig = config.GetProviderConfig<FireworksProviderConfig>();
        fireworksConfig.Should().NotBeNull();
        fireworksConfig!.Temperature.Should().Be(0.2);
        fireworksConfig.TopP.Should().Be(0.8);
        fireworksConfig.MaxOutputTokens.Should().Be(512);
        fireworksConfig.ResponseFormat.Should().Be("json_object");
        fireworksConfig.ToolChoice.Should().Be("required");
        fireworksConfig.StopSequences.Should().Equal("stop");
    }

    [Fact]
    public void FireworksJsonContext_ShouldSerializeAndDeserializeConfig()
    {
        var config = new FireworksProviderConfig
        {
            Temperature = 0.3,
            TopP = 0.95,
            MaxOutputTokens = 2048,
            Seed = 12345,
            StopSequences = ["END", "STOP"],
            ResponseFormat = "json_object",
            ToolChoice = "none"
        };

        var json = JsonSerializer.Serialize(config, FireworksJsonContext.Default.FireworksProviderConfig);
        var roundTrip = JsonSerializer.Deserialize(json, FireworksJsonContext.Default.FireworksProviderConfig);

        roundTrip.Should().BeEquivalentTo(config);
    }

    [Fact]
    public void ProviderDiscovery_ConfigJsonRoundTrips()
    {
        FireworksProviderModule.Initialize();
        var config = new FireworksProviderConfig
        {
            Temperature = 0.4,
            ResponseFormat = "json_object",
            ToolChoice = "required"
        };

        var json = ProviderDiscovery.SerializeProviderConfig("fireworks", config);
        var roundTrip = ProviderDiscovery.DeserializeProviderConfig("fireworks", json);

        roundTrip.Should().BeEquivalentTo(config);
    }

    [Fact]
    public void CreateChatClient_WithValidConfig_ShouldCreateOpenAICompatibleClient()
    {
        var config = ValidConfig();

        using var chatClient = _provider.CreateChatClient(config, CreateServices());

        chatClient.Should().NotBeNull();
        chatClient.GetService(typeof(ChatClientMetadata))
            .Should()
            .BeOfType<ChatClientMetadata>()
            .Which.DefaultModelId.Should().Be("accounts/fireworks/models/llama-v3p1-8b-instruct");

        chatClient.GetService(typeof(HttpClient))
            .Should()
            .BeOfType<HttpClient>()
            .Which.BaseAddress.Should().Be(new Uri("https://api.fireworks.ai/inference/v1/"));
    }

    [Fact]
    public void CreateChatClient_ShouldResolveFireworksApiKeyAliasFromEnvironment()
    {
        FireworksProviderModule.Initialize();
        global::System.Environment.SetEnvironmentVariable("FIREWORKS_API_KEY", "env-key");

        try
        {
            var config = new ClientProviderConfig
            {
                ProviderKey = "fireworks",
                ModelName = "accounts/fireworks/models/llama-v3p1-8b-instruct"
            };

            using var chatClient = _provider.CreateChatClient(config, CreateEnvironmentServices());

            chatClient.Should().NotBeNull();
        }
        finally
        {
            global::System.Environment.SetEnvironmentVariable("FIREWORKS_API_KEY", null);
        }
    }

    [Fact]
    public void CreateChatClient_ShouldResolveEndpointAliasFromEnvironment()
    {
        FireworksProviderModule.Initialize();
        global::System.Environment.SetEnvironmentVariable("FIREWORKS_API_KEY", "env-key");
        global::System.Environment.SetEnvironmentVariable("FIREWORKS_ENDPOINT", "https://proxy.fireworks.test/v1/");

        try
        {
            var config = new ClientProviderConfig
            {
                ProviderKey = "fireworks",
                ModelName = "accounts/fireworks/models/llama-v3p1-8b-instruct"
            };

            using var chatClient = _provider.CreateChatClient(config, CreateEnvironmentServices());

            chatClient.GetService(typeof(HttpClient))
                .Should()
                .BeOfType<HttpClient>()
                .Which.BaseAddress.Should().Be(new Uri("https://proxy.fireworks.test/v1/"));
        }
        finally
        {
            global::System.Environment.SetEnvironmentVariable("FIREWORKS_API_KEY", null);
            global::System.Environment.SetEnvironmentVariable("FIREWORKS_ENDPOINT", null);
        }
    }

    [Fact]
    public async Task ChatWrapper_ShouldApplyDefaultModelAndOptions()
    {
        var fake = new RecordingChatClient();
        using var client = CreateConfiguredChatClient(fake, "model-default", new FireworksProviderConfig
        {
            Temperature = 0.6,
            TopP = 0.8,
            MaxOutputTokens = 256,
            Seed = 99,
            StopSequences = ["END"],
            ResponseFormat = "json_object",
            ToolChoice = "required"
        });

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        fake.LastOptions.Should().NotBeNull();
        fake.LastOptions!.ModelId.Should().Be("model-default");
        fake.LastOptions.Temperature.Should().Be(0.6f);
        fake.LastOptions.TopP.Should().Be(0.8f);
        fake.LastOptions.MaxOutputTokens.Should().Be(256);
        fake.LastOptions.Seed.Should().Be(99);
        fake.LastOptions.StopSequences.Should().Equal("END");
        fake.LastOptions.ResponseFormat.Should().BeOfType<ChatResponseFormatJson>();
        fake.LastOptions.ToolMode.Should().BeOfType<RequiredChatToolMode>();
    }

    [Fact]
    public void ErrorHandler_ShouldClassifyApiAuthError()
    {
        var handler = new FireworksErrorHandler();
        var exception = new HttpRequestException(
            """Fireworks AI API request failed [Status: 401 Unauthorized]. Response: {"error":{"code":"invalid_api_key","message":"invalid api key"}}""",
            null,
            HttpStatusCode.Unauthorized);

        var details = handler.ParseError(exception);

        details.Should().NotBeNull();
        details!.StatusCode.Should().Be(401);
        details.Category.Should().Be(ErrorCategory.AuthError);
        details.ErrorCode.Should().Be("invalid_api_key");
        handler.RequiresSpecialHandling(details).Should().BeTrue();
    }

    [Fact]
    public void ErrorHandler_ShouldClassifyRateLimitAsRetryable()
    {
        var handler = new FireworksErrorHandler();
        var exception = new HttpRequestException("too many requests", null, HttpStatusCode.TooManyRequests);

        var details = handler.ParseError(exception);

        details.Should().NotBeNull();
        details!.Category.Should().Be(ErrorCategory.RateLimitRetryable);
        handler.GetRetryDelay(details, 1, TimeSpan.FromMilliseconds(100), 2, TimeSpan.FromSeconds(1))
            .Should()
            .Be(TimeSpan.FromMilliseconds(200));
    }

    private static ClientProviderConfig ValidConfig()
        => new()
        {
            ProviderKey = "fireworks",
            ModelName = "accounts/fireworks/models/llama-v3p1-8b-instruct",
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

    private static IChatClient CreateConfiguredChatClient(IChatClient inner, string model, FireworksProviderConfig config)
    {
        var type = typeof(FireworksProvider).GetNestedType("FireworksConfiguredChatClient", BindingFlags.NonPublic);
        type.Should().NotBeNull();

        return (IChatClient)Activator.CreateInstance(
            type!,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: [inner, model, new Uri("https://api.fireworks.ai/inference/v1/"), config],
            culture: null)!;
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public ChatOptions? LastOptions { get; private set; }

        public void Dispose()
        {
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            await Task.CompletedTask;
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new TextContent("ok")]
            };
        }
    }
}
#endif
