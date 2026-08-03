using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.Anthropic;
using HPD.Agent.Providers.AzureAI;
using HPD.Agent.Providers.Cerebras;
using HPD.Agent.Providers.DeepSeek;
using HPD.Agent.Providers.GoogleAI;
using HPD.Agent.Providers.Hyperbolic;
using HPD.Agent.Providers.LMStudio;
using HPD.Agent.Providers.MiniMax;
using HPD.Agent.Providers.Nebius;
using HPD.Agent.Providers.Nscale;
using HPD.Agent.Providers.NvidiaNim;
using HPD.Agent.Providers.Ollama;
using HPD.Agent.Providers.OVHcloud;
using HPD.Agent.Providers.OpenAICompatible;
using HPD.Agent.Providers.Perplexity;
using HPD.Agent.Providers.SambaNova;
using HPD.Agent.Providers.Scaleway;
using HPD.Agent.Providers.SiliconFlow;
using HPD.Agent.Providers.Venice;
using HPD.Agent.Providers.Xai;
using HPD.Agent.Providers.Zai;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

#if NET10_0_OR_GREATER
using HPD.Agent.Providers.Cohere;
using HPD.Agent.Providers.DashScope;
using HPD.Agent.Providers.DeepInfra;
using HPD.Agent.Providers.Fireworks;
using HPD.Agent.Providers.Groq;
using HPD.Agent.Providers.HuggingFace;
using HPD.Agent.Providers.Mistral;
using HPD.Agent.Providers.Moonshot;
using HPD.Agent.Providers.Together;
#endif

namespace HPD.Agent.Providers.Tests;

public sealed class ProviderBehaviorRestorationTests
{
    public static IEnumerable<object[]> OpenAICompatibleProviders()
    {
        yield return [new ProviderCase("cerebras", () => new CerebrasProvider(), RequiresApiKey: true)];
        yield return [new ProviderCase("deepseek", () => new DeepSeekProvider(), RequiresApiKey: true)];
        yield return [new ProviderCase("sambanova", () => new SambaNovaProvider(), RequiresApiKey: true)];
        yield return [new ProviderCase("hyperbolic", () => new HyperbolicProvider(), RequiresApiKey: true)];
        yield return [new ProviderCase("ovhcloud", () => new OVHcloudProvider(), RequiresApiKey: true)];
        yield return [new ProviderCase("nscale", () => new NscaleProvider(), RequiresApiKey: true)];
        yield return [new ProviderCase("venice", () => new VeniceProvider(), RequiresApiKey: true)];
        yield return [new ProviderCase("perplexity", () => new PerplexityProvider(), RequiresApiKey: true)];
        yield return [new ProviderCase("nebius", () => new NebiusProvider(), RequiresApiKey: true)];
        yield return [new ProviderCase("nvidia-nim", () => new NvidiaNimProvider(), RequiresApiKey: true)];
        yield return [new ProviderCase("siliconflow", () => new SiliconFlowProvider(), RequiresApiKey: true)];
        yield return [new ProviderCase("scaleway", () => new ScalewayProvider(), RequiresApiKey: true)];
        yield return [new ProviderCase("zai", () => new ZaiProvider(), RequiresApiKey: true)];
        yield return [new ProviderCase("minimax", () => new MiniMaxProvider(), RequiresApiKey: true)];
        yield return [new ProviderCase("lmstudio", () => new LMStudioProvider(), RequiresApiKey: false)];
    }

    public static IEnumerable<object[]> ErrorHandlers()
    {
        yield return ["anthropic", new AnthropicProvider().CreateErrorHandler()];
        yield return ["azure-ai", new AzureAIProvider().CreateErrorHandler()];
        yield return ["google-ai", new GoogleAIProvider().CreateErrorHandler()];
        yield return ["xai", new XaiProvider().CreateErrorHandler()];

        foreach (var providerCase in OpenAICompatibleProviders())
        {
            var provider = ((ProviderCase)providerCase[0]).Create();
            yield return [provider.ProviderKey, provider.CreateErrorHandler()];
        }

#if NET10_0_OR_GREATER
        yield return ["cohere", new CohereProvider().CreateErrorHandler()];
        yield return ["dashscope", new DashScopeProvider().CreateErrorHandler()];
        yield return ["deepinfra", new DeepInfraProvider().CreateErrorHandler()];
        yield return ["fireworks", new FireworksProvider().CreateErrorHandler()];
        yield return ["groq", new GroqProvider().CreateErrorHandler()];
        yield return ["huggingface", new HuggingFaceProvider().CreateErrorHandler()];
        yield return ["mistral", new MistralProvider().CreateErrorHandler()];
        yield return ["moonshot", new MoonshotProvider().CreateErrorHandler()];
        yield return ["together", new TogetherProvider().CreateErrorHandler()];
#endif
    }

    [Theory]
    [MemberData(nameof(OpenAICompatibleProviders))]
    public void OpenAICompatibleProvider_MetadataAndValidation_ShouldPreserveProviderSpecificConstructionBehavior(ProviderCase providerCase)
    {
        var provider = providerCase.Create();
        var metadata = provider.GetMetadata();

        metadata.ProviderKey.Should().Be(providerCase.ProviderKey);
        metadata.Families.Should().ContainKey(ProviderClientFamily.Chat);
        metadata.Families[ProviderClientFamily.Chat].DefaultModelId.Should().NotBeNullOrWhiteSpace();

        var invalid = provider.ValidateConfiguration(
            new ProviderClientConfig
            {
                ProviderKey = providerCase.ProviderKey,
                Endpoint = "not-a-uri"
            },
            ProviderClientFamily.Embeddings);

        invalid.IsValid.Should().BeFalse();
        invalid.Errors.Should().Contain(error => error.Contains("chat provider family", StringComparison.OrdinalIgnoreCase));
        invalid.Errors.Should().Contain(error => error.Contains("Model name", StringComparison.OrdinalIgnoreCase));
        invalid.Errors.Should().Contain(error => error.Contains("Endpoint", StringComparison.OrdinalIgnoreCase));
        invalid.Errors.Should().NotContain(error => error.Contains("API key", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(OpenAICompatibleProviders))]
    public async Task OpenAICompatibleProvider_CreateChatClient_ShouldExposeMetadataAndDefaultModel(ProviderCase providerCase)
    {
        var provider = providerCase.Create();
        var config = new ProviderClientConfig
        {
            ProviderKey = providerCase.ProviderKey,
            ModelName = "test-model",
            ApiKey = providerCase.RequiresApiKey ? "test-key" : null
        };

        using var client = await provider.CreateChatClientAsync(config);
        var metadata = client.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;

        metadata.Should().NotBeNull();
        metadata!.ProviderName.Should().Be(providerCase.ProviderKey);
        metadata.DefaultModelId.Should().Be("test-model");
    }

    [Fact]
    public async Task OpenAICompatibleProvider_CreateChatClient_ShouldResolveRequiredApiKeyFromSecretResolver()
    {
        var provider = new DeepSeekProvider();
        var config = new ProviderClientConfig
        {
            ProviderKey = "deepseek",
            ModelName = "deepseek-v4-flash"
        };

        using var services = ServicesWithSecrets(new Dictionary<string, string>
        {
            ["deepseek:ApiKey"] = "test-key"
        });

        using var client = await provider.CreateChatClientAsync(config, services);

        client.Should().NotBeNull();
    }

    [Fact]
    public async Task OpenAICompatibleProvider_CreateChatClient_ShouldFailWhenRequiredApiKeyCannotBeResolved()
    {
        var provider = new DeepSeekProvider();
        var config = new ProviderClientConfig
        {
            ProviderKey = "deepseek",
            ModelName = "deepseek-v4-flash"
        };

        using var services = ServicesWithSecrets();

        var action = async () => await provider.CreateChatClientAsync(config, services);

        await action.Should().ThrowAsync<SecretNotFoundException>()
            .WithMessage("*DeepSeek*");
    }

    [Theory]
    [MemberData(nameof(ErrorHandlers))]
    public void ProviderErrorHandler_ShouldRetryRateLimitAndServerErrorsButNotClientErrors(
        string providerKey,
        IProviderErrorHandler handler)
    {
        var initialDelay = TimeSpan.FromMilliseconds(100);
        var maxDelay = TimeSpan.FromSeconds(5);

        handler.GetRetryDelay(
                new ProviderErrorDetails { Category = ErrorCategory.RateLimitRetryable, StatusCode = 429, Message = "rate limit" },
                attempt: 0,
                initialDelay,
                multiplier: 2,
                maxDelay)
            .Should()
            .NotBeNull($"{providerKey} should retry retryable rate limits");

        handler.GetRetryDelay(
                new ProviderErrorDetails { Category = ErrorCategory.ServerError, StatusCode = 500, Message = "server error" },
                attempt: 1,
                initialDelay,
                multiplier: 2,
                maxDelay)
            .Should()
            .NotBeNull($"{providerKey} should retry server errors");

        handler.GetRetryDelay(
                new ProviderErrorDetails { Category = ErrorCategory.ClientError, StatusCode = 400, Message = "bad request" },
                attempt: 0,
                initialDelay,
                multiplier: 2,
                maxDelay)
            .Should()
            .BeNull($"{providerKey} should not retry client errors");
    }

    [Theory]
    [MemberData(nameof(ErrorHandlers))]
    public void ProviderErrorHandler_ShouldRequireSpecialHandlingForAuthErrors(
        string providerKey,
        IProviderErrorHandler handler)
    {
        handler.RequiresSpecialHandling(
                new ProviderErrorDetails { Category = ErrorCategory.AuthError, StatusCode = 401, Message = "unauthorized" })
            .Should()
            .BeTrue($"{providerKey} auth errors need caller intervention");
    }

    [Fact]
    public void OllamaErrorHandler_ShouldRetryTransientErrorsWithoutSpecialAuthHandling()
    {
        var handler = new OllamaProvider().CreateErrorHandler();

        handler.GetRetryDelay(
                new ProviderErrorDetails { Category = ErrorCategory.Transient, Message = "connection refused" },
                attempt: 0,
                initialDelay: TimeSpan.FromMilliseconds(100),
                multiplier: 2,
                maxDelay: TimeSpan.FromSeconds(5))
            .Should()
            .NotBeNull();

        handler.GetRetryDelay(
                new ProviderErrorDetails { Category = ErrorCategory.ClientError, StatusCode = 404, Message = "missing model" },
                attempt: 0,
                initialDelay: TimeSpan.FromMilliseconds(100),
                multiplier: 2,
                maxDelay: TimeSpan.FromSeconds(5))
            .Should()
            .BeNull();

        handler.RequiresSpecialHandling(
                new ProviderErrorDetails { Category = ErrorCategory.AuthError, StatusCode = 401, Message = "unauthorized" })
            .Should()
            .BeFalse();
    }

    [Fact]
    public void AzureAI_ValidateConfiguration_ShouldKeepAuthModeAndSdkOptionGuards()
    {
        var provider = new AzureAIProvider();
        var config = new ProviderClientConfig
        {
            ProviderKey = "azure-ai",
            ModelName = "gpt-4o"
        };
        config.ProviderConfig = new AzureAIProviderConfig
        {
            AuthMode = (AzureAIAuthMode)999,
            NetworkTimeoutMs = 0
        };

        var result = provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("AuthMode", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(error => error.Contains("NetworkTimeoutMs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AzureAI_CreateChatClient_WithApiKeyAuthModeButNoKey_ShouldFailBeforeSdkClientConstruction()
    {
        var provider = new AzureAIProvider();
        var config = new ProviderClientConfig
        {
            ProviderKey = "azure-ai",
            ModelName = "gpt-4o",
            Endpoint = "https://example.openai.azure.com/"
        };
        config.ProviderConfig = new AzureAIProviderConfig
        {
            AuthMode = AzureAIAuthMode.ApiKey
        };

        var action = async () => await provider.CreateChatClientAsync(config, ServicesWithSecrets());

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*API key authentication was requested*");
    }

    [Fact]
    public async Task AzureAI_CreateChatClient_WithProjectEndpointAndApiKey_ShouldRejectKeyAuth()
    {
        var provider = new AzureAIProvider();
        var config = new ProviderClientConfig
        {
            ProviderKey = "azure-ai",
            ModelName = "gpt-4o",
            Endpoint = "https://hpd.services.ai.azure.com/api/projects/test",
            ApiKey = "test-key"
        };

        var action = async () => await provider.CreateChatClientAsync(config, ServicesWithSecrets());

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Projects endpoints require OAuth authentication*");
    }

    [Fact]
    public void GoogleAI_ValidateConfiguration_ShouldRequireModelAndSupportedPlatform()
    {
        var provider = new GoogleAIProvider();
        var config = new ProviderClientConfig
        {
            ProviderKey = "google-ai"
        };
        config.ProviderConfig = new GoogleAIProviderConfig
        {
            Platform = (GoogleAIPlatform)999
        };

        var result = provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("Model name", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(error => error.Contains("Unsupported Google AI platform", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task XaiChatClient_ShouldWriteReasoningEffortExtraField()
    {
        var handler = new CapturingHandler("""
            {"id":"chatcmpl-1","model":"grok","created":10,"choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}
            """);
        using var client = new XaiChatClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.x.ai/v1/") },
            new OpenAICompatibleChatClientOptions
            {
                ProviderKey = "xai",
                DisplayName = "xAI",
                ProviderUri = new Uri("https://api.x.ai/"),
                DefaultModelId = "grok"
            });

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "think")],
            new ChatOptions
            {
                Reasoning = new Microsoft.Extensions.AI.ReasoningOptions
                {
                    Effort = Microsoft.Extensions.AI.ReasoningEffort.High
                }
            });

        using var request = JsonDocument.Parse(handler.RequestBody);
        request.RootElement.GetProperty("reasoning_effort").GetString().Should().Be("high");
    }

#if NET10_0_OR_GREATER
    [Fact]
    public async Task MoonshotChatClient_ShouldWriteThinkingExtraField()
    {
        var handler = new CapturingHandler("""
            {"id":"chatcmpl-1","model":"kimi","created":10,"choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}
            """);
        using var client = new MoonshotChatClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.moonshot.ai/v1/") },
            new OpenAICompatibleChatClientOptions
            {
                ProviderKey = "moonshot",
                DisplayName = "Moonshot",
                ProviderUri = new Uri("https://api.moonshot.ai/"),
                DefaultModelId = "kimi"
            });

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "think")],
            new ChatOptions
            {
                Reasoning = new Microsoft.Extensions.AI.ReasoningOptions
                {
                    Effort = Microsoft.Extensions.AI.ReasoningEffort.High
                }
            }.UseMoonshotChatRequestOptions(new MoonshotChatRequestOptions
            {
                ThinkingKeep = MoonshotThinkingKeep.All
            }));

        using var request = JsonDocument.Parse(handler.RequestBody);
        var thinking = request.RootElement.GetProperty("thinking");
        thinking.GetProperty("type").GetString().Should().Be("enabled");
        thinking.GetProperty("keep").GetString().Should().Be("all");
    }

    [Fact]
    public void DashScopeChatRequestOptions_ShouldApplyTextRawParameters()
    {
        var options = new ChatOptions
        {
            ModelId = "qwen-plus",
            Temperature = 0.2f,
            MaxOutputTokens = 256,
            ResponseFormat = ChatResponseFormat.Json
        }.UseDashScopeChatRequestOptions(new DashScopeChatRequestOptions
        {
            EnableSearch = true,
            ThinkingBudget = 1024,
            EnableCodeInterpreter = true,
            SearchOptions = new DashScopeSearchRequestOptions
            {
                EnableCitation = true,
                SearchStrategy = DashScopeSearchStrategy.Turbo
            }
        });

        DashScopeChatRequestOptionKeys.ApplyRawParameters(options, "qwen-plus", defaultUseVl: false);

        options.AdditionalProperties.Should().NotBeNull();
        options.AdditionalProperties![DashScopeChatRequestOptionKeys.UseVl].Should().Be(false);
        var raw = options.AdditionalProperties[DashScopeChatRequestOptionKeys.Raw];
        raw.Should().NotBeNull();
        raw!.GetType().Name.Should().Be("TextGenerationParameters");
        raw.GetType().GetProperty("EnableSearch")!.GetValue(raw).Should().Be(true);
        raw.GetType().GetProperty("ThinkingBudget")!.GetValue(raw).Should().Be(1024);
        raw.GetType().GetProperty("EnableCodeInterpreter")!.GetValue(raw).Should().Be(true);
        raw.GetType().GetProperty("MaxTokens")!.GetValue(raw).Should().Be(256);
    }
#endif

    private static ServiceProvider ServicesWithSecrets(Dictionary<string, string>? secrets = null)
        => new ServiceCollection()
            .AddSingleton<ISecretResolver>(new DictionarySecretResolver(secrets ?? []))
            .BuildServiceProvider();

    public sealed record ProviderCase(
        string ProviderKey,
        Func<IChatClientProvider> Factory,
        bool RequiresApiKey)
    {
        public IChatClientProvider Create() => Factory();

        public override string ToString() => ProviderKey;
    }

    private sealed class DictionarySecretResolver(IReadOnlyDictionary<string, string> secrets) : ISecretResolver
    {
        public ValueTask<ResolvedSecret?> ResolveAsync(string key, CancellationToken cancellationToken = default)
            => secrets.TryGetValue(key, out var value)
                ? ValueTask.FromResult<ResolvedSecret?>(new ResolvedSecret { Value = value, Source = "test" })
                : ValueTask.FromResult<ResolvedSecret?>(null);
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
