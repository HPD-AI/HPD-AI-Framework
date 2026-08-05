using System.Text.Json;
using System.Net.WebSockets;
using HPD.Agent;
using HPD.Agent.Audio;
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
    public void BuilderExtension_ConfiguresTextToSpeechProviderFamily()
    {
        var builder = new AgentBuilder()
            .WithElevenLabsTextToSpeech(
                model: "eleven_multilingual_v2",
                apiKey: "el-test",
                voice: "voice_123",
                outputFormat: "mp3_44100_128",
                configureClient: config => config.EnablePushTextStreaming = true,
                configureOptions: options => options.Stability = 0.4);

        var ttsConfig = builder.Config.Clients?.TextToSpeech;
        Assert.NotNull(ttsConfig);
        Assert.Equal(ElevenLabsAudioProvider.Key, ttsConfig.ProviderKey);
        Assert.Equal("eleven_multilingual_v2", ttsConfig.ModelName);
        Assert.Equal("el-test", ttsConfig.ApiKey);

        var providerConfig = ttsConfig.ProviderConfig as ElevenLabsTtsConfig;
        Assert.NotNull(providerConfig);
        Assert.True(providerConfig.EnablePushTextStreaming);
        Assert.Equal("voice_123", ttsConfig.VoiceId);
        Assert.Equal("mp3_44100_128", ttsConfig.AudioFormat);
        Assert.Equal(0.4, Assert.IsType<ElevenLabsTtsOptions>(ttsConfig.ProviderOptions).Stability);
    }

    [Fact]
    public void BuilderExtension_ConfiguresSpeechToTextProviderFamily()
    {
        var builder = new AgentBuilder()
            .WithElevenLabsSpeechToText(
                model: "scribe_v2",
                apiKey: "el-test",
                language: "en",
                configureOptions: options => options.Diarize = true);

        var sttConfig = builder.Config.Clients?.SpeechToText;
        Assert.NotNull(sttConfig);
        Assert.Equal(ElevenLabsAudioProvider.Key, sttConfig.ProviderKey);
        Assert.Equal("scribe_v2", sttConfig.ModelName);
        Assert.Equal("el-test", sttConfig.ApiKey);

        var providerConfig = sttConfig.ProviderConfig as ElevenLabsSttConfig;
        Assert.NotNull(providerConfig);
        Assert.Equal("en", sttConfig.SpeechLanguage);
        Assert.True(Assert.IsType<ElevenLabsSttOptions>(sttConfig.ProviderOptions).Diarize);
    }

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
    public void Metadata_ExposesSpeechToTextFamily()
    {
        var provider = new ElevenLabsAudioProvider();
        var metadata = provider.GetMetadata();

        var family = Assert.Contains(ProviderClientFamily.SpeechToText, metadata.Families);
        Assert.Equal(ProviderClientFamily.SpeechToText, family.Family);
        Assert.Equal(ElevenLabsAudioProvider.DefaultSpeechToTextModel, family.DefaultModelId);
        Assert.Contains("scribe_v2", family.SupportedModels);
        Assert.Contains(ElevenLabsAudioProvider.DefaultRealtimeSpeechToTextModel, family.SupportedModels);
        Assert.True((bool)Assert.Contains("SupportsAudio", family.Capabilities!)!);
        Assert.True((bool)Assert.Contains("SupportsStreamingSpeechToText", family.Capabilities!)!);
        Assert.True((bool)Assert.Contains("SupportsRealtimeSpeechToText", family.Capabilities!)!);
    }

    [Fact]
    public void StreamingTextUpdate_MapsSessionStarted()
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            message_type = "session_started",
            session_id = "session_123"
        });

        var update = ElevenLabsSpeechToTextClient.ToStreamingTextUpdate(
            payload,
            ElevenLabsAudioProvider.DefaultRealtimeSpeechToTextModel);

        Assert.NotNull(update);
        Assert.Equal(SpeechToTextResponseUpdateKind.SessionOpen, update.Kind);
        Assert.Equal("session_123", update.ResponseId);
        Assert.Equal(ElevenLabsAudioProvider.DefaultRealtimeSpeechToTextModel, update.ModelId);
    }

    [Fact]
    public void StreamingTextUpdate_MapsPartialTranscript()
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            message_type = "partial_transcript",
            text = "hello wor"
        });

        var update = ElevenLabsSpeechToTextClient.ToStreamingTextUpdate(
            payload,
            ElevenLabsAudioProvider.DefaultRealtimeSpeechToTextModel);

        Assert.NotNull(update);
        Assert.Equal(SpeechToTextResponseUpdateKind.TextUpdating, update.Kind);
        Assert.Equal("hello wor", update.Text);
    }

    [Fact]
    public void StreamingTextUpdate_MapsCommittedTranscriptWithTimestamps()
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            message_type = "committed_transcript_with_timestamps",
            text = "hello world",
            language_code = "en",
            words = new[]
            {
                new { text = "hello", start = 0.1, end = 0.4, type = "word" },
                new { text = "world", start = 0.5, end = 0.9, type = "word" }
            }
        });

        var update = ElevenLabsSpeechToTextClient.ToStreamingTextUpdate(
            payload,
            ElevenLabsAudioProvider.DefaultRealtimeSpeechToTextModel);

        Assert.NotNull(update);
        Assert.Equal(SpeechToTextResponseUpdateKind.TextUpdated, update.Kind);
        Assert.Equal("hello world", update.Text);
        Assert.Equal(TimeSpan.FromSeconds(0.1), update.StartTime);
        Assert.Equal(TimeSpan.FromSeconds(0.9), update.EndTime);
        Assert.Equal("en", update.AdditionalProperties?["languageCode"]);
    }

    [Fact]
    public void StreamingTextUpdate_MapsError()
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            message_type = "scribe_error",
            error = "bad audio"
        });

        var update = ElevenLabsSpeechToTextClient.ToStreamingTextUpdate(
            payload,
            ElevenLabsAudioProvider.DefaultRealtimeSpeechToTextModel);

        Assert.NotNull(update);
        Assert.Equal(SpeechToTextResponseUpdateKind.Error, update.Kind);
        Assert.Equal("scribe_error", update.AdditionalProperties?["messageType"]);
        Assert.Equal("bad audio", update.AdditionalProperties?["error"]);
    }

    [Fact]
    public void ValidateConfiguration_MissingApiKeyFails()
    {
        using var env = new EnvironmentVariableScope("ELEVENLABS_API_KEY", null);
        var provider = new ElevenLabsAudioProvider();

        var result = provider.ValidateConfiguration(
            new TextToSpeechClientConfig
            {
                ProviderKey = "elevenlabs"
            },
            ProviderClientFamily.TextToSpeech);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("API key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateConfiguration_AcceptsApiKeyFromFamilyConfig()
    {
        var provider = new ElevenLabsAudioProvider();

        var result = provider.ValidateConfiguration(
            new TextToSpeechClientConfig
            {
                ProviderKey = "elevenlabs",
                ApiKey = "el-test",
                ProviderOptions = new ElevenLabsTtsOptions { Stability = 0.4f }
            },
            ProviderClientFamily.TextToSpeech);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateConfiguration_AcceptsSpeechToTextApiKeyFromFamilyConfig()
    {
        var provider = new ElevenLabsAudioProvider();

        var result = provider.ValidateConfiguration(
            new SpeechToTextClientConfig
            {
                ProviderKey = "elevenlabs",
                ApiKey = "el-test",
                ModelName = "scribe_v2"
            },
            ProviderClientFamily.SpeechToText);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateConfiguration_WrongFamilyFails()
    {
        var provider = new ElevenLabsAudioProvider();

        var result = provider.ValidateConfiguration(
            new ProviderClientConfig
            {
                ProviderKey = "elevenlabs",
                ApiKey = "el-test"
            },
            ProviderClientFamily.Realtime);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Realtime", StringComparison.Ordinal));
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
    public void ProviderRegistry_ResolvesSpeechToTextProvider()
    {
        var registry = new ProviderRegistry();
        var provider = new ElevenLabsAudioProvider();

        registry.Register(provider);

        Assert.Same(provider, registry.GetProvider<ISpeechToTextClientProvider>("elevenlabs"));
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
                new ProviderClientConfig
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
            new ProviderClientConfig
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
    public void CreateSpeechToTextClient_MissingApiKeyThrowsUsefulError()
    {
        using var env = new EnvironmentVariableScope("ELEVENLABS_API_KEY", null);
        var provider = new ElevenLabsAudioProvider();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            provider.CreateSpeechToTextClient(
                new ProviderClientConfig
                {
                    ProviderKey = "elevenlabs"
                }));

        Assert.Contains("ElevenLabs API key is required", exception.Message);
        Assert.Contains("speech-to-text", exception.Message);
        Assert.Contains("ELEVENLABS_API_KEY", exception.Message);
    }

    [Fact]
    public void CreateSpeechToTextClient_WithFakeApiKeyReturnsMeaiClient()
    {
        var provider = new ElevenLabsAudioProvider();

        using var client = provider.CreateSpeechToTextClient(
            new ProviderClientConfig
            {
                ProviderKey = "elevenlabs",
                ModelName = "scribe_v2",
                ApiKey = "el-test"
            });

        Assert.NotNull(client);
        var metadata = Assert.IsType<SpeechToTextClientMetadata>(
            client.GetService(typeof(SpeechToTextClientMetadata)));
        Assert.Equal("elevenlabs", metadata.ProviderName);
        Assert.Equal("scribe_v2", metadata.DefaultModelId);
    }

    [Fact]
    public async Task SpeechToTextClient_PostsMultipartRequestAndMapsResponse()
    {
        var handler = new CapturingMessageHandler(
            """
            {
              "language_code": "en",
              "language_probability": 0.98,
              "text": "hello world",
              "words": [
                { "text": "hello", "start": 0.1, "end": 0.4, "type": "word" },
                { "text": "world", "start": 0.5, "end": 0.9, "type": "word" }
              ]
            }
            """);
        using var httpClient = new HttpClient(handler);
        using var client = new ElevenLabsSpeechToTextClient(
            "el-test",
            new ElevenLabsSttRuntimeSettings
            {
                BaseUrl = "https://api.example.test/v1",
                DefaultModelId = "scribe_v1",
                Diarize = true,
                TagAudioEvents = false,
                TimestampsGranularity = "word"
            },
            httpClient);

        var response = await client.GetTextAsync(
            new MemoryStream([1, 2, 3, 4]),
            new SpeechToTextOptions
            {
                ModelId = "scribe_v2",
                SpeechLanguage = "en",
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["fileName"] = "clip.wav",
                    ["contentType"] = "audio/wav"
                }
            });

        Assert.Equal("hello world", response.Text);
        Assert.Equal("scribe_v2", response.ModelId);
        Assert.Equal(TimeSpan.FromSeconds(0.1), response.StartTime);
        Assert.Equal(TimeSpan.FromSeconds(0.9), response.EndTime);
        Assert.Equal("en", response.AdditionalProperties!["languageCode"]);
        Assert.Equal(0.98, response.AdditionalProperties!["languageProbability"]);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://api.example.test/v1/speech-to-text", handler.RequestUri?.ToString());
        Assert.Equal("el-test", handler.ApiKey);
        Assert.Equal("scribe_v2", handler.FormValues["model_id"]);
        Assert.Equal("en", handler.FormValues["language_code"]);
        Assert.Equal("true", handler.FormValues["diarize"]);
        Assert.Equal("false", handler.FormValues["tag_audio_events"]);
        Assert.Equal("word", handler.FormValues["timestamps_granularity"]);
        Assert.Equal("clip.wav", handler.FileName);
        Assert.Equal("audio/wav", handler.FileContentType);
        Assert.Equal([1, 2, 3, 4], handler.FileBytes);
    }

    [Fact]
    public void CreateTextToSpeechClient_AdvertisesHonestBatchOnlyTtsCapabilities()
    {
        var provider = new ElevenLabsAudioProvider();

        using var client = provider.CreateTextToSpeechClient(
            new ProviderClientConfig
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
            new TextToSpeechClientConfig
            {
                ProviderKey = "elevenlabs",
                ApiKey = "el-test",
                ProviderConfig = new ElevenLabsTtsConfig
                {
                    EnablePushTextStreaming = true,
                    WebSocketBaseUrl = "wss://example.test/v1"
                },
                VoiceId = "voice-test"
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
        var config = new ElevenLabsTtsRuntimeSettings
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
            new ProviderClientConfig
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
            new ProviderClientConfig
            {
                ProviderKey = "elevenlabs"
            });

        Assert.NotNull(client);
    }

    [Fact]
    public void ProviderConfigRegistration_RoundTripsSttConfig()
    {
        var config = new ElevenLabsSttConfig
        {
            WebSocketBaseUrl = "wss://example.test/v1"
        };
        var options = new ElevenLabsSttOptions
        {
            RealtimeModelId = "scribe_v2_realtime",
            Diarize = true,
            TagAudioEvents = false,
            TimestampsGranularity = "word",
            AudioFormat = "pcm_16000",
            CommitStrategy = "manual",
            IncludeTimestamps = true,
            IncludeLanguageDetection = true,
            Keyterms = ["hpd", "agent"],
            NoVerbatim = true,
            VadSilenceThresholdSeconds = 1.2,
            VadThreshold = 0.5,
            MinSpeechDurationMilliseconds = 120,
            MinSilenceDurationMilliseconds = 150,
            EnableLogging = false,
            StreamingChunkSizeBytes = 4096
        };

        var json = JsonSerializer.Serialize(config, ElevenLabsTtsJsonContext.Default.ElevenLabsSttConfig);
        var deserialized = JsonSerializer.Deserialize(json, ElevenLabsTtsJsonContext.Default.ElevenLabsSttConfig);
        var optionsJson = JsonSerializer.Serialize(options, ElevenLabsTtsJsonContext.Default.ElevenLabsSttOptions);
        var deserializedOptions = JsonSerializer.Deserialize(optionsJson, ElevenLabsTtsJsonContext.Default.ElevenLabsSttOptions);

        var roundTripped = Assert.IsType<ElevenLabsSttConfig>(deserialized);
        Assert.Equal("wss://example.test/v1", roundTripped.WebSocketBaseUrl);
        var roundTrippedOptions = Assert.IsType<ElevenLabsSttOptions>(deserializedOptions);
        Assert.Equal("scribe_v2_realtime", roundTrippedOptions.RealtimeModelId);
        Assert.True(roundTrippedOptions.Diarize);
        Assert.False(roundTrippedOptions.TagAudioEvents);
        Assert.Equal("word", roundTrippedOptions.TimestampsGranularity);
        Assert.Equal("pcm_16000", roundTrippedOptions.AudioFormat);
        Assert.Equal("manual", roundTrippedOptions.CommitStrategy);
        Assert.True(roundTrippedOptions.IncludeTimestamps);
        Assert.True(roundTrippedOptions.IncludeLanguageDetection);
        Assert.Equal(["hpd", "agent"], roundTrippedOptions.Keyterms);
        Assert.True(roundTrippedOptions.NoVerbatim);
        Assert.Equal(1.2, roundTrippedOptions.VadSilenceThresholdSeconds);
        Assert.Equal(0.5, roundTrippedOptions.VadThreshold);
        Assert.Equal(120, roundTrippedOptions.MinSpeechDurationMilliseconds);
        Assert.Equal(150, roundTrippedOptions.MinSilenceDurationMilliseconds);
        Assert.False(roundTrippedOptions.EnableLogging);
        Assert.Equal(4096, roundTrippedOptions.StreamingChunkSizeBytes);
    }

    [Fact]
    public void ProviderConfigRegistration_RoundTripsTtsConfig()
    {
        var config = new ElevenLabsTtsConfig
        {
            WebSocketBaseUrl = "wss://example.test/v1",
            EnablePushTextStreaming = true
        };
        var options = new ElevenLabsTtsOptions { Stability = 0.25 };

        var json = JsonSerializer.Serialize(config, ElevenLabsTtsJsonContext.Default.ElevenLabsTtsConfig);
        var deserialized = JsonSerializer.Deserialize(json, ElevenLabsTtsJsonContext.Default.ElevenLabsTtsConfig);
        var optionsJson = JsonSerializer.Serialize(options, ElevenLabsTtsJsonContext.Default.ElevenLabsTtsOptions);
        var deserializedOptions = JsonSerializer.Deserialize(optionsJson, ElevenLabsTtsJsonContext.Default.ElevenLabsTtsOptions);

        var roundTripped = Assert.IsType<ElevenLabsTtsConfig>(deserialized);
        Assert.Equal("wss://example.test/v1", roundTripped.WebSocketBaseUrl);
        Assert.True(roundTripped.EnablePushTextStreaming);
        Assert.Equal(0.25, Assert.IsType<ElevenLabsTtsOptions>(deserializedOptions).Stability);
    }

    [SkippableFact]
    public async Task LiveTextToSpeech_WithConfiguredApiKey_ReturnsAudio()
    {
        Skip.IfNot(
            string.Equals(
                System.Environment.GetEnvironmentVariable("HPD_AUDIO_LIVE_SMOKE"),
                "1",
                StringComparison.Ordinal),
            "Set HPD_AUDIO_LIVE_SMOKE=1 and ELEVENLABS_API_KEY to run the credentialed ElevenLabs audio smoke test.");
        var apiKey = System.Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
        Skip.If(
            string.IsNullOrWhiteSpace(apiKey),
            "Set ELEVENLABS_API_KEY to run the credentialed ElevenLabs audio smoke test.");

        var provider = new ElevenLabsAudioProvider();
        using var client = provider.CreateTextToSpeechClient(
            new TextToSpeechClientConfig
            {
                ProviderKey = "elevenlabs",
                ApiKey = apiKey,
                ModelName = System.Environment.GetEnvironmentVariable("ELEVENLABS_TTS_MODEL") ??
                    ElevenLabsAudioProvider.DefaultTextToSpeechModel,
                VoiceId = System.Environment.GetEnvironmentVariable("ELEVENLABS_VOICE_ID") ??
                    ElevenLabsAudioProvider.DefaultVoiceId,
                AudioFormat = "mp3"
            });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var response = await client.GetAudioAsync(
            System.Environment.GetEnvironmentVariable("ELEVENLABS_TTS_SMOKE_TEXT") ?? "HPD audio smoke.",
            new TextToSpeechOptions
            {
                AudioFormat = "mp3"
            },
            cts.Token);

        var audio = Assert.Single(response.Contents.OfType<DataContent>());
        Assert.Equal("audio/mpeg", audio.MediaType);
        Assert.NotEmpty(audio.Data.ToArray());
    }

    private sealed class SecretServiceProvider(ISecretResolver resolver) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(ISecretResolver) ? resolver : null;
    }

    private sealed class CapturingMessageHandler(string responseJson) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? ApiKey { get; private set; }
        public Dictionary<string, string> FormValues { get; } = new(StringComparer.Ordinal);
        public string? FileName { get; private set; }
        public string? FileContentType { get; private set; }
        public byte[]? FileBytes { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            ApiKey = request.Headers.TryGetValues("xi-api-key", out var apiKeyValues)
                ? apiKeyValues.SingleOrDefault()
                : null;

            var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
            foreach (var part in multipart)
            {
                var name = part.Headers.ContentDisposition?.Name?.Trim('"');
                if (string.Equals(name, "file", StringComparison.Ordinal))
                {
                    FileName = part.Headers.ContentDisposition?.FileName?.Trim('"');
                    FileContentType = part.Headers.ContentType?.MediaType;
                    FileBytes = await part.ReadAsByteArrayAsync(cancellationToken);
                }
                else if (!string.IsNullOrWhiteSpace(name))
                {
                    FormValues[name] = await part.ReadAsStringAsync(cancellationToken);
                }
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _previousValue = System.Environment.GetEnvironmentVariable(name);
            System.Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() =>
            System.Environment.SetEnvironmentVariable(_name, _previousValue);
    }
}

#pragma warning restore EXTEXP0001
