using System.Text.Json;
using System.Net.WebSockets;
using HPD.Agent;
using HPD.Agent.Audio.Output;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.Audio.ElevenLabs;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.V2.Tests;

#pragma warning disable EXTEXP0001

public sealed class ElevenLabsAudioProviderTests
{
    [Fact]
    public void Metadata_ExposesTextToSpeechFamily()
    {
        var provider = new ElevenLabsAudioProvider();
        var metadata = provider.GetMetadata();

        Assert.Equal("elevenlabs", provider.ProviderKey);
        Assert.Equal("elevenlabs", metadata.ProviderKey);
        var family = Assert.Contains(ProviderClientFamily.TextToSpeech, metadata.Families);
        Assert.Equal(ProviderClientFamily.TextToSpeech, family.Family);
        Assert.Equal(ElevenLabsAudioProvider.DefaultTextToSpeechModel, family.DefaultModelId);
        Assert.True((bool)Assert.Contains("SupportsAudio", family.Capabilities!)!);
    }

    [Fact]
    public void ValidateConfiguration_MissingApiKeyFails()
    {
        using var env = new EnvironmentVariableScope("ELEVENLABS_API_KEY", null);
        var provider = new ElevenLabsAudioProvider();

        var result = provider.ValidateConfiguration(
            new ClientProviderConfig
            {
                ProviderKey = "elevenlabs"
            },
            ProviderClientFamily.TextToSpeech);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("API key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateConfiguration_AcceptsApiKeyFromProviderOptionsJson()
    {
        var provider = new ElevenLabsAudioProvider();

        var result = provider.ValidateConfiguration(
            new ClientProviderConfig
            {
                ProviderKey = "elevenlabs",
                ProviderOptionsJson = "{\"apiKey\":\"el-test\",\"stability\":0.4}"
            },
            ProviderClientFamily.TextToSpeech);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateConfiguration_WrongFamilyFails()
    {
        var provider = new ElevenLabsAudioProvider();

        var result = provider.ValidateConfiguration(
            new ClientProviderConfig
            {
                ProviderKey = "elevenlabs",
                ApiKey = "el-test"
            },
            ProviderClientFamily.SpeechToText);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("SpeechToText", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateConfiguration_InvalidProviderOptionsJsonFails()
    {
        var provider = new ElevenLabsAudioProvider();

        var result = provider.ValidateConfiguration(
            new ClientProviderConfig
            {
                ProviderKey = "elevenlabs",
                ApiKey = "el-test",
                ProviderOptionsJson = "{"
            },
            ProviderClientFamily.TextToSpeech);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("ProviderOptionsJson", StringComparison.Ordinal));
    }

    [Fact]
    public void ProviderRegistry_ResolvesTextToSpeechProvider()
    {
        var registry = new ProviderRegistry();
        var provider = new ElevenLabsAudioProvider();

        registry.Register(provider);

        Assert.Same(provider, registry.GetProvider<ITextToSpeechClientProvider>("elevenlabs"));
    }

    [Fact]
    public void CreateErrorHandler_ReturnsElevenLabsErrorHandler()
    {
        var provider = new ElevenLabsAudioProvider();

        var handler = provider.CreateErrorHandler();

        Assert.NotNull(handler);
        Assert.Equal("ElevenLabsErrorHandler", handler.GetType().Name);
    }

    [Fact]
    public void ErrorHandler_ParsesInvalidApiKeyAsAuthError()
    {
        var handler = CreateErrorHandler();
        var exception = new HttpRequestException(
            """
            ElevenLabs API call failed: HTTP 401. Body: {"detail":{"type":"authentication_error","code":"invalid_api_key","message":"Invalid API key","request_id":"req_123"}}
            """,
            inner: null,
            statusCode: System.Net.HttpStatusCode.Unauthorized);

        var details = handler.ParseError(exception);

        Assert.NotNull(details);
        Assert.Equal(ErrorCategory.AuthError, details.Category);
        Assert.Equal("invalid_api_key", details.ErrorCode);
        Assert.Equal("authentication_error", details.ErrorType);
        Assert.Equal("req_123", details.RequestId);
        Assert.Null(handler.GetRetryDelay(details, 0, TimeSpan.FromSeconds(1), 2, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void ErrorHandler_ParsesQuotaExceededAsTerminalRateLimit()
    {
        var handler = CreateErrorHandler();
        var exception = new HttpRequestException(
            """
            ElevenLabs API call failed: HTTP 402. Body: {"detail":{"status":"quota_exceeded","message":"You have insufficient quota to complete the request."}}
            """,
            inner: null,
            statusCode: System.Net.HttpStatusCode.PaymentRequired);

        var details = handler.ParseError(exception);

        Assert.NotNull(details);
        Assert.Equal(ErrorCategory.RateLimitTerminal, details.Category);
        Assert.Equal("quota_exceeded", details.ErrorCode);
        Assert.Null(handler.GetRetryDelay(details, 0, TimeSpan.FromSeconds(1), 2, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void ErrorHandler_ParsesConcurrentLimitAsRetryableRateLimit()
    {
        var handler = CreateErrorHandler();
        var exception = new HttpRequestException(
            """
            ElevenLabs API call failed: HTTP 429. Body: {"detail":{"type":"rate_limit_error","code":"concurrent_limit_exceeded","message":"Too many concurrent requests."}}
            """,
            inner: null,
            statusCode: System.Net.HttpStatusCode.TooManyRequests);

        var details = handler.ParseError(exception);

        Assert.NotNull(details);
        Assert.Equal(ErrorCategory.RateLimitRetryable, details.Category);
        Assert.Equal("concurrent_limit_exceeded", details.ErrorCode);
        Assert.NotNull(handler.GetRetryDelay(details, 0, TimeSpan.FromSeconds(1), 2, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void ErrorHandler_ParsesTextTooLongAsContextWindow()
    {
        var handler = CreateErrorHandler();
        var exception = new HttpRequestException(
            """
            ElevenLabs API call failed: HTTP 400. Body: {"detail":{"type":"validation_error","code":"text_too_long","message":"The provided text exceeds the maximum allowed length.","param":"text"}}
            """,
            inner: null,
            statusCode: System.Net.HttpStatusCode.BadRequest);

        var details = handler.ParseError(exception);

        Assert.NotNull(details);
        Assert.Equal(ErrorCategory.ContextWindow, details.Category);
        Assert.Equal("text_too_long", details.ErrorCode);
        Assert.Equal("text", Assert.IsType<string>(details.RawDetails!["param"]));
    }

    [Fact]
    public void ErrorHandler_ParsesHttpStatusMessageWithoutStatusCodeAsRetryableRateLimit()
    {
        var handler = CreateErrorHandler();
        var exception = new HttpRequestException(
            """
            ElevenLabs API call failed: HTTP 429. Body: {"detail":{"type":"rate_limit_error","code":"rate_limit_exceeded","message":"Rate limit reached."}}
            """);

        var details = handler.ParseError(exception);

        Assert.NotNull(details);
        Assert.Equal(429, details.StatusCode);
        Assert.Equal(ErrorCategory.RateLimitRetryable, details.Category);
        Assert.Equal("rate_limit_exceeded", details.ErrorCode);
        Assert.NotNull(handler.GetRetryDelay(details, 0, TimeSpan.FromSeconds(1), 2, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void ErrorHandler_ParsesServerErrorAsRetryableServerError()
    {
        var handler = CreateErrorHandler();
        var exception = new HttpRequestException(
            """
            ElevenLabs API call failed: HTTP 500. Body: {"detail":{"type":"internal_error","code":"internal_error","message":"Internal server error."}}
            """);

        var details = handler.ParseError(exception);

        Assert.NotNull(details);
        Assert.Equal(500, details.StatusCode);
        Assert.Equal(ErrorCategory.ServerError, details.Category);
        Assert.NotNull(handler.GetRetryDelay(details, 0, TimeSpan.FromSeconds(1), 2, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void ErrorHandler_ParsesTaskCanceledAsTransient()
    {
        var handler = CreateErrorHandler();
        var exception = new TaskCanceledException("The ElevenLabs request timed out.");

        var details = handler.ParseError(exception);

        Assert.NotNull(details);
        Assert.Equal(408, details.StatusCode);
        Assert.Equal(ErrorCategory.Transient, details.Category);
        Assert.NotNull(handler.GetRetryDelay(details, 0, TimeSpan.FromSeconds(1), 2, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void ErrorHandler_ParsesWebSocketFailureAsTransient()
    {
        var handler = CreateErrorHandler();
        var exception = new WebSocketException(WebSocketError.ConnectionClosedPrematurely);

        var details = handler.ParseError(exception);

        Assert.NotNull(details);
        Assert.Equal(ErrorCategory.Transient, details.Category);
        Assert.NotNull(handler.GetRetryDelay(details, 0, TimeSpan.FromSeconds(1), 2, TimeSpan.FromSeconds(30)));
    }

    private static IProviderErrorHandler CreateErrorHandler()
        => new ElevenLabsAudioProvider().CreateErrorHandler();

    [Fact]
    public void CreateTextToSpeechClient_MissingApiKeyThrowsUsefulError()
    {
        using var env = new EnvironmentVariableScope("ELEVENLABS_API_KEY", null);
        var provider = new ElevenLabsAudioProvider();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            provider.CreateTextToSpeechClient(
                new ClientProviderConfig
                {
                    ProviderKey = "elevenlabs"
                }));

        Assert.Contains("ElevenLabs API key is required", exception.Message);
        Assert.Contains("ELEVENLABS_API_KEY", exception.Message);
    }

    [Fact]
    public void CreateTextToSpeechClient_WithFakeApiKeyReturnsMeaiClient()
    {
        var provider = new ElevenLabsAudioProvider();

        using var client = provider.CreateTextToSpeechClient(
            new ClientProviderConfig
            {
                ProviderKey = "elevenlabs",
                ModelName = "eleven_flash_v2_5",
                ApiKey = "el-test"
            });

        Assert.NotNull(client);
        var metadata = Assert.IsType<TextToSpeechClientMetadata>(
            client.GetService(typeof(TextToSpeechClientMetadata)));
        Assert.Equal("elevenlabs", metadata.ProviderName);
        Assert.Equal("eleven_flash_v2_5", metadata.DefaultModelId);
    }

    [Fact]
    public void CreateTextToSpeechClient_AdvertisesHonestBatchOnlyTtsCapabilities()
    {
        var provider = new ElevenLabsAudioProvider();

        using var client = provider.CreateTextToSpeechClient(
            new ClientProviderConfig
            {
                ProviderKey = "elevenlabs",
                ApiKey = "el-test"
            });

        var profile = Assert.IsType<TextToSpeechCapabilityProfile>(
            client.GetService(typeof(TextToSpeechCapabilityProfile)));
        Assert.True(profile.SupportsCompletedTextSynthesis);
        Assert.False(profile.SupportsCompletedTextAudioStreaming);
        Assert.False(profile.SupportsPushTextAudioStreaming);
        Assert.False(profile.SupportsAlignment);
        Assert.True(profile.SupportsCancellationBeforeAudio);
        Assert.False(profile.SupportsCancellationAfterAudio);
        Assert.Empty(profile.PreferredStreamingFormats);
    }

    [Fact]
    public void CreateTextToSpeechClient_WhenPushTextEnabled_ExposesFactoryAndCapabilities()
    {
        var provider = new ElevenLabsAudioProvider();

        using var client = provider.CreateTextToSpeechClient(
            new ClientProviderConfig
            {
                ProviderKey = "elevenlabs",
                ApiKey = "el-test",
                ProviderOptionsJson = """
                {
                  "enablePushTextStreaming": true,
                  "webSocketBaseUrl": "wss://example.test/v1",
                  "defaultVoiceId": "voice-test"
                }
                """
            });

        var profile = Assert.IsType<TextToSpeechCapabilityProfile>(
            client.GetService(typeof(TextToSpeechCapabilityProfile)));
        Assert.True(profile.SupportsCompletedTextSynthesis);
        Assert.True(profile.SupportsPushTextAudioStreaming);
        Assert.True(profile.SupportsCancellationBeforeAudio);
        Assert.True(profile.SupportsCancellationAfterAudio);
        Assert.Equal(["pcm_16000", "mp3_44100_128"], profile.PreferredStreamingFormats);
        Assert.IsAssignableFrom<IPushTextToSpeechStreamFactory>(
            client.GetService(typeof(IPushTextToSpeechStreamFactory)));
    }

    [Fact]
    public void PushTextStreamFactory_BuildsExpectedUriAndMessages()
    {
        var config = new ElevenLabsTtsConfig
        {
            WebSocketBaseUrl = "wss://example.test/v1",
            Stability = 0.25,
            SimilarityBoost = 0.5,
            ApplyTextNormalization = "auto",
            AutoMode = true,
            SyncAlignment = true,
            InactivityTimeout = 30
        };
        var factory = new ElevenLabsPushTextToSpeechStreamFactory(
            "el-test",
            config,
            "eleven_flash_v2_5",
            "voice-test",
            "mp3_44100_128");

        var uri = factory.BuildStreamInputUri(
            "voice-test",
            "eleven_flash_v2_5",
            languageCode: "en",
            outputFormat: "mp3_44100_128");

        Assert.Equal("/v1/text-to-speech/voice-test/stream-input", uri.AbsolutePath);
        Assert.Contains("model_id=eleven_flash_v2_5", uri.Query);
        Assert.Contains("output_format=mp3_44100_128", uri.Query);
        Assert.Contains("language_code=en", uri.Query);
        Assert.Contains("apply_text_normalization=auto", uri.Query);
        Assert.Contains("auto_mode=true", uri.Query);
        Assert.Contains("sync_alignment=true", uri.Query);
        Assert.Contains("inactivity_timeout=30", uri.Query);

        var initJson = JsonSerializer.Serialize(
            new ElevenLabsWebSocketInitializeMessage
            {
                Text = " ",
                XiApiKey = "el-test",
                VoiceSettings = new ElevenLabsWebSocketVoiceSettings
                {
                    Stability = 0.25,
                    SimilarityBoost = 0.5
                }
            },
            ElevenLabsTtsJsonContext.Default.ElevenLabsWebSocketInitializeMessage);
        using var init = JsonDocument.Parse(initJson);
        Assert.Equal(" ", init.RootElement.GetProperty("text").GetString());
        Assert.Equal("el-test", init.RootElement.GetProperty("xi_api_key").GetString());
        Assert.Equal(0.25, init.RootElement.GetProperty("voice_settings").GetProperty("stability").GetDouble());
        Assert.Equal(0.5, init.RootElement.GetProperty("voice_settings").GetProperty("similarity_boost").GetDouble());

        var textJson = JsonSerializer.Serialize(
            new ElevenLabsWebSocketTextMessage
            {
                Text = "Hello there.",
                TryTriggerGeneration = true
            },
            ElevenLabsTtsJsonContext.Default.ElevenLabsWebSocketTextMessage);
        using var text = JsonDocument.Parse(textJson);
        Assert.Equal("Hello there.", text.RootElement.GetProperty("text").GetString());
        Assert.True(text.RootElement.GetProperty("try_trigger_generation").GetBoolean());

        var finalJson = JsonSerializer.Serialize(
            new ElevenLabsWebSocketTextMessage
            {
                Text = string.Empty
            },
            ElevenLabsTtsJsonContext.Default.ElevenLabsWebSocketTextMessage);
        using var final = JsonDocument.Parse(finalJson);
        Assert.Equal(string.Empty, final.RootElement.GetProperty("text").GetString());

        var audio = JsonSerializer.Deserialize(
            """{"audio":"AQIDBA==","isFinal":true}""",
            ElevenLabsTtsJsonContext.Default.ElevenLabsWebSocketAudioMessage);
        Assert.NotNull(audio);
        Assert.Equal("AQIDBA==", audio.Audio);
        Assert.True(audio.IsFinal);
    }

    [Fact]
    public void CreateTextToSpeechClient_ResolvesApiKeyFromSecretResolver()
    {
        var provider = new ElevenLabsAudioProvider();
        var services = new SecretServiceProvider(
            new ExplicitSecretResolver(new Dictionary<string, string>
            {
                ["elevenlabs:ApiKey"] = "el-secret"
            }));

        using var client = provider.CreateTextToSpeechClient(
            new ClientProviderConfig
            {
                ProviderKey = "elevenlabs"
            },
            services);

        Assert.NotNull(client);
    }

    [Fact]
    public void CreateTextToSpeechClient_ResolvesApiKeyFromEnvironment()
    {
        using var env = new EnvironmentVariableScope("ELEVENLABS_API_KEY", "el-env");
        var provider = new ElevenLabsAudioProvider();

        using var client = provider.CreateTextToSpeechClient(
            new ClientProviderConfig
            {
                ProviderKey = "elevenlabs"
            });

        Assert.NotNull(client);
    }

    [Fact]
    public void ProviderConfigRegistration_RoundTripsTtsConfig()
    {
        var config = new ElevenLabsTtsConfig
        {
            ApiKey = "el-test",
            BaseUrl = "https://example.test/v1",
            WebSocketBaseUrl = "wss://example.test/v1",
            DefaultModelId = "eleven_flash_v2_5",
            DefaultVoiceId = "voice-test",
            Stability = 0.25,
            EnablePushTextStreaming = true,
            PushTextAggregationMode = PushTextInputAggregationMode.Sentence
        };

        var json = ProviderDiscovery.SerializeProviderConfig(
            "elevenlabs",
            ProviderClientFamily.TextToSpeech,
            config);
        var deserialized = ProviderDiscovery.DeserializeProviderConfig(
            "elevenlabs",
            ProviderClientFamily.TextToSpeech,
            json);

        var roundTripped = Assert.IsType<ElevenLabsTtsConfig>(deserialized);
        Assert.Equal("el-test", roundTripped.ApiKey);
        Assert.Equal("https://example.test/v1", roundTripped.BaseUrl);
        Assert.Equal("wss://example.test/v1", roundTripped.WebSocketBaseUrl);
        Assert.Equal("eleven_flash_v2_5", roundTripped.DefaultModelId);
        Assert.Equal("voice-test", roundTripped.DefaultVoiceId);
        Assert.Equal(0.25, roundTripped.Stability);
        Assert.True(roundTripped.EnablePushTextStreaming);
        Assert.Equal(PushTextInputAggregationMode.Sentence, roundTripped.PushTextAggregationMode);
    }

    private sealed class SecretServiceProvider(ISecretResolver resolver) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(ISecretResolver) ? resolver : null;
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() =>
            Environment.SetEnvironmentVariable(_name, _previousValue);
    }
}

#pragma warning restore EXTEXP0001
