using System.ClientModel;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.Audio.OpenAI;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.V2.Tests;

#pragma warning disable EXTEXP0001

public sealed class OpenAIAudioProviderTests
{
    [Fact]
    public void Metadata_ExposesSpeechToTextFamily()
    {
        var provider = new OpenAIAudioProvider();
        var metadata = provider.GetMetadata();

        Assert.Equal("openai", provider.ProviderKey);
        Assert.Equal("openai", metadata.ProviderKey);
        var family = Assert.Contains(ProviderClientFamily.SpeechToText, metadata.Families);
        Assert.Equal(ProviderClientFamily.SpeechToText, family.Family);
        Assert.Equal(OpenAIAudioProvider.DefaultSpeechToTextModel, family.DefaultModelId);
        Assert.True((bool)Assert.Contains("SupportsAudio", family.Capabilities!)!);
    }

    [Fact]
    public void ValidateConfiguration_MissingApiKeyFails()
    {
        var provider = new OpenAIAudioProvider();

        var result = provider.ValidateConfiguration(
            new ClientProviderConfig
            {
                ProviderKey = "openai"
            },
            ProviderClientFamily.SpeechToText);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("API key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateConfiguration_WrongFamilyFails()
    {
        var provider = new OpenAIAudioProvider();

        var result = provider.ValidateConfiguration(
            new ClientProviderConfig
            {
                ProviderKey = "openai",
                ApiKey = "sk-test"
            },
            ProviderClientFamily.Chat);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Chat", StringComparison.Ordinal));
    }

    [Fact]
    public void ProviderRegistry_ResolvesSpeechToTextProvider()
    {
        var registry = new ProviderRegistry();
        var provider = new OpenAIAudioProvider();

        registry.Register(provider);

        Assert.Same(provider, registry.GetProvider<ISpeechToTextClientProvider>("openai"));
    }

    [Fact]
    public void CreateErrorHandler_ReturnsOpenAIAudioErrorHandler()
    {
        var provider = new OpenAIAudioProvider();

        var handler = provider.CreateErrorHandler();

        Assert.NotNull(handler);
        Assert.Equal("OpenAIAudioErrorHandler", handler.GetType().Name);
    }

    [Fact]
    public void ErrorHandler_ParsesInvalidApiKeyAsAuthError()
    {
        var handler = CreateErrorHandler();
        var exception = new ClientResultException(
            """
            HTTP 401 (invalid_request_error)
            {"error":{"type":"authentication_error","code":"invalid_api_key","message":"Invalid API key"}}
            Request-Id: req_openai_123
            """,
            response: null,
            innerException: null);

        var details = handler.ParseError(exception);

        Assert.NotNull(details);
        Assert.Equal(ErrorCategory.AuthError, details.Category);
        Assert.Equal("invalid_api_key", details.ErrorCode);
        Assert.Equal("authentication_error", details.ErrorType);
        Assert.Equal("req_openai_123", details.RequestId);
        Assert.Null(handler.GetRetryDelay(details, 0, TimeSpan.FromSeconds(1), 2, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void ErrorHandler_ParsesInsufficientQuotaAsTerminalRateLimit()
    {
        var handler = CreateErrorHandler();
        var exception = new ClientResultException(
            """
            HTTP 429 (rate_limit_exceeded)
            {"error":{"type":"insufficient_quota","code":"insufficient_quota","message":"You exceeded your current quota."}}
            """,
            response: null,
            innerException: null);

        var details = handler.ParseError(exception);

        Assert.NotNull(details);
        Assert.Equal(ErrorCategory.RateLimitTerminal, details.Category);
        Assert.Equal("insufficient_quota", details.ErrorCode);
        Assert.Null(handler.GetRetryDelay(details, 0, TimeSpan.FromSeconds(1), 2, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void ErrorHandler_ParsesRateLimitAsRetryable()
    {
        var handler = CreateErrorHandler();
        var exception = new ClientResultException(
            """
            HTTP 429 (rate_limit_exceeded)
            {"error":{"type":"rate_limit_error","code":"rate_limit_exceeded","message":"Rate limit reached. Please try again in 1.5s"}}
            """,
            response: null,
            innerException: null);

        var details = handler.ParseError(exception);

        Assert.NotNull(details);
        Assert.Equal(ErrorCategory.RateLimitRetryable, details.Category);
        Assert.Equal("rate_limit_exceeded", details.ErrorCode);
        Assert.Equal(TimeSpan.FromSeconds(1.5), handler.GetRetryDelay(details, 0, TimeSpan.FromSeconds(1), 2, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void ErrorHandler_ParsesContextLengthAsContextWindow()
    {
        var handler = CreateErrorHandler();
        var exception = new ClientResultException(
            """
            HTTP 400 (invalid_request_error)
            {"error":{"type":"invalid_request_error","code":"context_length_exceeded","message":"Maximum context length exceeded."}}
            """,
            response: null,
            innerException: null);

        var details = handler.ParseError(exception);

        Assert.NotNull(details);
        Assert.Equal(ErrorCategory.ContextWindow, details.Category);
        Assert.Equal("context_length_exceeded", details.ErrorCode);
    }

    [Fact]
    public void ErrorHandler_PreservesRestErrorParamInRawDetails()
    {
        var handler = CreateErrorHandler();
        var exception = new ClientResultException(
            """
            HTTP 400 (invalid_request_error)
            {"error":{"type":"invalid_request_error","code":"invalid_type","message":"Invalid type for 'messages[0].content'.","param":"messages[0].content"}}
            """,
            response: null,
            innerException: null);

        var details = handler.ParseError(exception);

        Assert.NotNull(details);
        Assert.Equal(ErrorCategory.ClientError, details.Category);
        Assert.Equal("invalid_type", details.ErrorCode);
        Assert.Equal("invalid_request_error", details.ErrorType);
        Assert.Equal("messages[0].content", Assert.IsType<string>(details.RawDetails!["param"]));
    }

    [Fact]
    public void ErrorHandler_PreservesRealtimeEventIdsInRawDetails()
    {
        var handler = CreateErrorHandler();
        var exception = new ClientResultException(
            """
            HTTP 400 (invalid_request_error)
            {"event_id":"event_server_890","type":"error","error":{"type":"invalid_request_error","code":"invalid_event","message":"The 'type' field is missing.","param":"type","event_id":"event_client_123"}}
            """,
            response: null,
            innerException: null);

        var details = handler.ParseError(exception);

        Assert.NotNull(details);
        Assert.Equal(ErrorCategory.ClientError, details.Category);
        Assert.Equal("invalid_event", details.ErrorCode);
        Assert.Equal("invalid_request_error", details.ErrorType);
        Assert.Equal("type", Assert.IsType<string>(details.RawDetails!["param"]));
        Assert.Equal(["event_server_890", "event_client_123"], Assert.IsType<string[]>(details.RawDetails["eventIds"]));
    }

    private static IProviderErrorHandler CreateErrorHandler()
        => new OpenAIAudioProvider().CreateErrorHandler();

    [Fact]
    public void CreateSpeechToTextClient_MissingApiKeyThrowsUsefulError()
    {
        var provider = new OpenAIAudioProvider();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            provider.CreateSpeechToTextClient(
                new ClientProviderConfig
                {
                    ProviderKey = "openai"
                }));

        Assert.Contains("OpenAI API key is required", exception.Message);
    }

    [Fact]
    public void CreateSpeechToTextClient_WithFakeApiKeyReturnsMeaiWrapper()
    {
        var provider = new OpenAIAudioProvider();

        using var client = provider.CreateSpeechToTextClient(
            new ClientProviderConfig
            {
                ProviderKey = "openai",
                ModelName = "whisper-1",
                ApiKey = "sk-test"
            });

        Assert.NotNull(client);
        var metadata = Assert.IsType<SpeechToTextClientMetadata>(
            client.GetService(typeof(SpeechToTextClientMetadata)));
        Assert.Equal("openai", metadata.ProviderName);
        Assert.Equal("whisper-1", metadata.DefaultModelId);
    }

    [Fact]
    public void ProviderConfigRegistration_RoundTripsSttConfig()
    {
        var config = new OpenAISttConfig
        {
            ApiKey = "sk-test",
            BaseUrl = "https://example.test/v1",
            DefaultModelId = "gpt-4o-mini-transcribe",
            Prompt = "Names may include Jeff.",
            Temperature = 0.1f,
            ResponseFormat = "verbose_json",
            TimestampGranularities = ["word", "segment"],
            IncludeLogprobs = true
        };

        var json = ProviderDiscovery.SerializeProviderConfig(
            "openai",
            ProviderClientFamily.SpeechToText,
            config);
        var deserialized = ProviderDiscovery.DeserializeProviderConfig(
            "openai",
            ProviderClientFamily.SpeechToText,
            json);

        var roundTripped = Assert.IsType<OpenAISttConfig>(deserialized);
        Assert.Equal("sk-test", roundTripped.ApiKey);
        Assert.Equal("https://example.test/v1", roundTripped.BaseUrl);
        Assert.Equal("gpt-4o-mini-transcribe", roundTripped.DefaultModelId);
        Assert.Equal("Names may include Jeff.", roundTripped.Prompt);
        Assert.Equal(0.1f, roundTripped.Temperature);
        Assert.Equal("verbose_json", roundTripped.ResponseFormat);
        Assert.Equal(["word", "segment"], roundTripped.TimestampGranularities);
        Assert.True(roundTripped.IncludeLogprobs);
    }
}

#pragma warning restore EXTEXP0001
