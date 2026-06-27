#if NET10_0_OR_GREATER
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.Together;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Agent.Providers.Tests;

public class TogetherProviderTests
{
    private readonly TogetherProvider _provider = new();

    [Fact]
    public void Provider_ShouldHaveCorrectMetadata()
    {
        var metadata = _provider.GetMetadata();

        metadata.ProviderKey.Should().Be("together");
        metadata.DisplayName.Should().Be("Together AI");
        metadata.DocumentationUri.Should().Be(new Uri("https://docs.together.ai/"));

        var chat = metadata.Families[ProviderClientFamily.Chat];
        chat.DefaultModelId.Should().Be("meta-llama/Llama-3.3-70B-Instruct-Turbo");
        chat.Capabilities!["SupportsStreaming"].Should().Be(true);
        chat.Capabilities["SupportsFunctionCalling"].Should().Be(true);
        chat.Capabilities["SupportsJsonResponseFormat"].Should().Be(true);

        metadata.Families.Should().ContainKey(ProviderClientFamily.Embeddings);
        metadata.Families[ProviderClientFamily.Embeddings].DefaultModelId.Should().Be("BAAI/bge-base-en-v1.5");
    }

    [Fact]
    public void Provider_ShouldImplementChatAndEmbeddings()
    {
        _provider.Should().BeAssignableTo<IChatClientProvider>();
        _provider.Should().BeAssignableTo<IEmbeddingGeneratorProvider>();
    }

    [Fact]
    public void ProviderDiscovery_ShouldRegisterProviderAndConfigTypes()
    {
        TogetherProviderModule.Initialize();

        ProviderDiscovery.GetProviderConfigType("together").Should().NotBeNull();
        ProviderDiscovery.GetProviderConfigType("together", ProviderClientFamily.Embeddings).Should().NotBeNull();
    }

    [Fact]
    public void ValidateConfiguration_WithValidConfig_ShouldSucceed()
    {
        var config = ValidConfig();
        config.SetProviderConfig(new TogetherProviderConfig
        {
            Temperature = 0.7,
            TopP = 0.9,
            TopK = 50,
            MaxOutputTokens = 1024,
            Seed = 123,
            StopSequences = ["END"],
            ResponseFormat = "json_object",
            EmbeddingModelId = "BAAI/bge-large-en-v1.5"
        });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateConfiguration_WithMissingModelName_ShouldFailForChat()
    {
        var config = new ClientProviderConfig
        {
            ProviderKey = "together",
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
            ProviderKey = "together",
            ModelName = "meta-llama/Llama-3.3-70B-Instruct-Turbo"
        };

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("API key") && e.Contains("TOGETHER_API_KEY"));
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
        config.SetProviderConfig(new TogetherProviderConfig { Temperature = temperature });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Temperature must be between 0 and 2"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidResponseFormat_ShouldFail()
    {
        var config = ValidConfig();
        config.SetProviderConfig(new TogetherProviderConfig { ResponseFormat = "xml" });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ResponseFormat"));
    }

    [Fact]
    public void WithTogether_ShouldConfigureBuilder()
    {
        var builder = new AgentBuilder()
            .WithTogether(
                model: "meta-llama/Llama-3.3-70B-Instruct-Turbo",
                apiKey: "test-key",
                endpoint: "https://example.test/v1",
                configure: options =>
                {
                    options.Temperature = 0.2;
                    options.TopP = 0.8;
                    options.TopK = 25;
                    options.MaxOutputTokens = 512;
                    options.ResponseFormat = "json_object";
                    options.StopSequences = ["stop"];
                });

        var config = builder.Config.Clients?.Chat;

        config.Should().NotBeNull();
        config!.ProviderKey.Should().Be("together");
        config.ModelName.Should().Be("meta-llama/Llama-3.3-70B-Instruct-Turbo");
        config.ApiKey.Should().Be("test-key");
        config.Endpoint.Should().Be("https://example.test/v1");

        var togetherConfig = config.GetProviderConfig<TogetherProviderConfig>();
        togetherConfig.Should().NotBeNull();
        togetherConfig!.Temperature.Should().Be(0.2);
        togetherConfig.TopP.Should().Be(0.8);
        togetherConfig.TopK.Should().Be(25);
        togetherConfig.MaxOutputTokens.Should().Be(512);
        togetherConfig.ResponseFormat.Should().Be("json_object");
        togetherConfig.StopSequences.Should().Equal("stop");
    }

    [Fact]
    public void WithTogetherEmbeddings_ShouldConfigureBuilder()
    {
        var builder = new AgentBuilder()
            .WithTogetherEmbeddings(model: "BAAI/bge-large-en-v1.5", apiKey: "test-key");

        var config = builder.Config.Clients?.Embeddings;

        config.Should().NotBeNull();
        config!.ProviderKey.Should().Be("together");
        config.ModelName.Should().Be("BAAI/bge-large-en-v1.5");

        var togetherConfig = config.GetProviderConfig<TogetherProviderConfig>(ProviderClientFamily.Embeddings);
        togetherConfig.Should().NotBeNull();
        togetherConfig!.EmbeddingModelId.Should().Be("BAAI/bge-large-en-v1.5");
    }

    [Fact]
    public void TogetherJsonContext_ShouldSerializeAndDeserializeConfig()
    {
        var config = new TogetherProviderConfig
        {
            Temperature = 0.3,
            TopP = 0.95,
            TopK = 40,
            MaxOutputTokens = 2048,
            Seed = 12345,
            StopSequences = ["END", "STOP"],
            ResponseFormat = "json_object",
            EmbeddingModelId = "BAAI/bge-large-en-v1.5"
        };

        var json = JsonSerializer.Serialize(config, TogetherJsonContext.Default.TogetherProviderConfig);
        var roundTrip = JsonSerializer.Deserialize(json, TogetherJsonContext.Default.TogetherProviderConfig);

        roundTrip.Should().BeEquivalentTo(config);
    }

    [Fact]
    public void ProviderDiscovery_ConfigJsonRoundTrips()
    {
        TogetherProviderModule.Initialize();
        var config = new TogetherProviderConfig
        {
            Temperature = 0.4,
            ResponseFormat = "json_object",
            EmbeddingModelId = "BAAI/bge-large-en-v1.5"
        };

        var json = ProviderDiscovery.SerializeProviderConfig("together", config);
        var roundTrip = ProviderDiscovery.DeserializeProviderConfig("together", json);

        roundTrip.Should().BeEquivalentTo(config);
    }

    [Fact]
    public void CreateChatClient_WithFakeSecret_ShouldCreateClient()
    {
        var config = ValidConfig();

        using var chatClient = _provider.CreateChatClient(config, CreateServices());

        chatClient.Should().NotBeNull();
        chatClient.GetService(typeof(ChatClientMetadata))
            .Should()
            .BeOfType<ChatClientMetadata>()
            .Which.DefaultModelId.Should().Be("meta-llama/Llama-3.3-70B-Instruct-Turbo");
    }

    [Fact]
    public void CreateChatClient_ShouldResolveTogetherApiKeyAliasFromEnvironment()
    {
        TogetherProviderModule.Initialize();
        global::System.Environment.SetEnvironmentVariable("TOGETHER_API_KEY", "env-key");

        try
        {
            var config = new ClientProviderConfig
            {
                ProviderKey = "together",
                ModelName = "meta-llama/Llama-3.3-70B-Instruct-Turbo"
            };

            using var chatClient = _provider.CreateChatClient(config, CreateEnvironmentServices());

            chatClient.Should().NotBeNull();
        }
        finally
        {
            global::System.Environment.SetEnvironmentVariable("TOGETHER_API_KEY", null);
        }
    }

    [Fact]
    public void CreateEmbeddingGenerator_WithValidConfig_ShouldCreateClient()
    {
        var config = new ClientProviderConfig
        {
            ProviderKey = "together",
            ModelName = "BAAI/bge-base-en-v1.5",
            ApiKey = "test-key"
        };

        using var embeddingGenerator = _provider.CreateEmbeddingGenerator(config, CreateServices());

        embeddingGenerator.Should().NotBeNull();
        embeddingGenerator.GetService(typeof(EmbeddingGeneratorMetadata))
            .Should()
            .BeOfType<EmbeddingGeneratorMetadata>()
            .Which.DefaultModelId.Should().Be("BAAI/bge-base-en-v1.5");
    }

    [Fact]
    public async Task ChatWrapper_ShouldApplyDefaultModelAndOptions()
    {
        var fake = new RecordingChatClient();
        using var client = CreateConfiguredChatClient(fake, "model-default", new TogetherProviderConfig
        {
            Temperature = 0.6,
            TopP = 0.8,
            TopK = 42,
            MaxOutputTokens = 256,
            Seed = 99,
            StopSequences = ["END"],
            ResponseFormat = "json_object"
        });

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        fake.LastOptions.Should().NotBeNull();
        fake.LastOptions!.ModelId.Should().Be("model-default");
        fake.LastOptions.Temperature.Should().Be(0.6f);
        fake.LastOptions.TopP.Should().Be(0.8f);
        fake.LastOptions.TopK.Should().Be(42);
        fake.LastOptions.MaxOutputTokens.Should().Be(256);
        fake.LastOptions.Seed.Should().Be(99);
        fake.LastOptions.StopSequences.Should().Equal("END");
        fake.LastOptions.ResponseFormat.Should().BeOfType<ChatResponseFormatJson>();
    }

    [Fact]
    public void ErrorHandler_ShouldClassifyApiAuthError()
    {
        var handler = new TogetherErrorHandler();
        var exception = new global::Together.ApiException("Unauthorized", HttpStatusCode.Unauthorized)
        {
            ResponseBody = """{"error":{"message":"invalid api key"},"code":"invalid_api_key"}"""
        };

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
        var handler = new TogetherErrorHandler();
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
            ProviderKey = "together",
            ModelName = "meta-llama/Llama-3.3-70B-Instruct-Turbo",
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

    private static IChatClient CreateConfiguredChatClient(IChatClient inner, string model, TogetherProviderConfig config)
    {
        var type = typeof(TogetherProvider).GetNestedType("TogetherConfiguredChatClient", BindingFlags.NonPublic);
        type.Should().NotBeNull();

        return (IChatClient)Activator.CreateInstance(
            type!,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: [inner, model, config],
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
