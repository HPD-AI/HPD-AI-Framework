using System.ClientModel;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.Audio.OpenAI;
using HPD.Agent.Audio.Interaction;
using HPD.Agent.Audio.Media;
using HPD.Agent.Audio.Providers;
using Microsoft.Extensions.AI;
using System.Threading.Channels;

namespace HPD.Agent.Audio.V2.Tests;

#pragma warning disable EXTEXP0001

public sealed class OpenAIAudioProviderTests
{
    [Fact]
    public void BuilderExtensions_ConfigureAudioProviderFamilies()
    {
        var builder = new AgentBuilder()
            .WithOpenAISpeechToText(
                model: "gpt-4o-transcribe",
                apiKey: "sk-stt",
                configure: config => config.Prompt = "names may be product codenames")
            .WithOpenAITextToSpeech(
                model: "gpt-4o-mini-tts",
                apiKey: "sk-tts",
                voice: "nova",
                outputFormat: "wav",
                speed: 1.2f)
            .WithOpenAIRealtime(
                model: "gpt-realtime",
                apiKey: "sk-realtime",
                configure: config => config.OrganizationId = "org_123");

        var sttConfig = builder.Config.Clients?.SpeechToText;
        Assert.NotNull(sttConfig);
        Assert.Equal(OpenAIAudioProvider.Key, sttConfig.ProviderKey);
        Assert.Equal("gpt-4o-transcribe", sttConfig.ModelName);
        Assert.Equal("sk-stt", sttConfig.ApiKey);
        Assert.Null(sttConfig.ProviderConfig);
        var sttProviderOptions = Assert.IsType<OpenAISttOptions>(sttConfig.ProviderOptions);
        Assert.Equal("names may be product codenames", sttProviderOptions.Prompt);

        var ttsConfig = builder.Config.Clients.TextToSpeech;
        Assert.NotNull(ttsConfig);
        Assert.Equal(OpenAIAudioProvider.Key, ttsConfig.ProviderKey);
        Assert.Equal("gpt-4o-mini-tts", ttsConfig.ModelName);
        Assert.Equal("sk-tts", ttsConfig.ApiKey);
        Assert.Null(ttsConfig.ProviderConfig);
        Assert.Equal("nova", ttsConfig.VoiceId);
        Assert.Equal("wav", ttsConfig.AudioFormat);
        Assert.Equal(1.2f, ttsConfig.Speed);

        var realtimeConfig = builder.Config.Clients.Realtime;
        Assert.NotNull(realtimeConfig);
        Assert.Equal(OpenAIAudioProvider.Key, realtimeConfig.ProviderKey);
        Assert.Equal("gpt-realtime", realtimeConfig.ModelName);
        Assert.Equal("sk-realtime", realtimeConfig.ApiKey);
        var realtimeProviderConfig = realtimeConfig.ProviderConfig as OpenAIRealtimeConfig;
        Assert.NotNull(realtimeProviderConfig);
        Assert.Equal("org_123", realtimeProviderConfig.OrganizationId);
    }

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
        Assert.True((bool)Assert.Contains("SupportsRetainedStreamingTranscription", family.Capabilities)!);
    }

    [Fact]
    public async Task RetainedParticipant_UsesManualRealtimeTranscriptionProtocol()
    {
        var socket = new ScriptedOpenAISocket();
        socket.Enqueue("""{"type":"session.created","session":{"id":"sess_1"}}""");
        socket.Enqueue(ReadySession("gpt-live-transcribe",
            "\"languages\":[\"en\"],\"prompt\":\"HPD voice agent\",\"keywords\":[\"HPD\"],\"delay\":\"low\""));
        var participant = new OpenAIRealtimeSpeechToTextParticipant("sk-test",
            new Uri("https://api.openai.com/v1"), new OpenAISttOptions
            { Prompt = "HPD voice agent", Keywords = ["HPD"], RealtimeDelay = "low" },
            socketFactory: () => socket);
        await using (participant.ConfigureAwait(false))
        {
            var ready = await participant.ConnectAsync(new()
            {
                ModelId = "gpt-live-transcribe",
                AudioFormat = new() { SampleRateHz = 48_000, ChannelCount = 1, BitsPerSample = 16 },
                CommitStrategy = StreamingSpeechToTextCommitStrategy.Manual,
                LanguageCode = "en",
                Keyterms = ["HPD"]
            });
            Assert.Equal("sess_1", ready.ProviderSessionId);
            await participant.WriteAudioAsync(new(1, new byte[] { 1, 0, 2, 0 }));
            var dispatch = await participant.CommitAsync(new() { OperationId = "commit-1" });
            Assert.Equal((ulong)1, dispatch.DispatchSequence);

            socket.Enqueue("""{"type":"input_audio_buffer.committed","item_id":"item_1"}""");
            socket.Enqueue("""{"type":"conversation.item.input_audio_transcription.delta","item_id":"item_1","content_index":0,"delta":"Hello"}""");
            socket.Enqueue("""{"type":"conversation.item.input_audio_transcription.delta","item_id":"item_1","content_index":0,"delta":" HPD"}""");
            socket.Enqueue("""{"type":"conversation.item.input_audio_transcription.completed","item_id":"item_1","content_index":0,"transcript":"Hello HPD"}""");
            await using var observations = participant.ReadObservationsAsync().GetAsyncEnumerator();
            Assert.True(await observations.MoveNextAsync());
            Assert.Equal(StreamingSpeechToTextObservationKind.PartialTranscript, observations.Current.Kind);
            Assert.True(await observations.MoveNextAsync());
            Assert.Equal("Hello HPD", observations.Current.Text);
            Assert.True(await observations.MoveNextAsync());
            Assert.Equal(StreamingSpeechToTextObservationKind.CommittedTranscript, observations.Current.Kind);
            Assert.Equal("Hello HPD", observations.Current.Text);

            var sent = socket.Sent.Select(static value => JsonDocument.Parse(value)).ToArray();
            try
            {
                Assert.Equal("session.update", sent[0].RootElement.GetProperty("type").GetString());
                Assert.Equal("transcription", sent[0].RootElement.GetProperty("session").GetProperty("type").GetString());
                Assert.Equal(24_000, sent[0].RootElement.GetProperty("session").GetProperty("audio")
                    .GetProperty("input").GetProperty("format").GetProperty("rate").GetInt32());
                Assert.Equal("input_audio_buffer.append", sent[1].RootElement.GetProperty("type").GetString());
                Assert.Equal(2, sent[1].RootElement.GetProperty("audio").GetBytesFromBase64().Length);
                Assert.Equal("input_audio_buffer.commit", sent[2].RootElement.GetProperty("type").GetString());
            }
            finally { foreach (var document in sent) document.Dispose(); }
            await participant.StopAsync();
        }
    }

    [Fact]
    public async Task RetainedParticipant_RejectsUnsupportedChannelLayoutBeforeProviderEffect()
    {
        var socket = new ScriptedOpenAISocket();
        await using var participant = new OpenAIRealtimeSpeechToTextParticipant("sk-test",
            new Uri("https://api.openai.com/v1"), new OpenAISttOptions(), socketFactory: () => socket);
        await Assert.ThrowsAsync<NotSupportedException>(async () => await participant.ConnectAsync(new()
        {
            ModelId = "gpt-live-transcribe",
            AudioFormat = new() { SampleRateHz = 48_000, ChannelCount = 2, BitsPerSample = 16 },
            CommitStrategy = StreamingSpeechToTextCommitStrategy.Manual
        }));
        Assert.Empty(socket.Sent);
    }

    [Theory]
    [InlineData("gpt-live-transcribe", "\"languages\":[\"en\"]", "languages")]
    [InlineData("gpt-transcribe", "\"language\":\"en\"", "language")]
    public async Task RetainedParticipant_UsesModelSpecificLanguageSchema(
        string model, string echoedLanguage, string expectedProperty)
    {
        var socket = new ScriptedOpenAISocket();
        socket.Enqueue("""{"type":"session.created","session":{"id":"sess_1"}}""");
        socket.Enqueue(ReadySession(model, echoedLanguage));
        await using var participant = new OpenAIRealtimeSpeechToTextParticipant("sk-test",
            new Uri("https://api.openai.com/v1"), new OpenAISttOptions(), socketFactory: () => socket);
        await participant.ConnectAsync(ConnectRequest(model));

        using var sent = JsonDocument.Parse(Assert.Single(socket.Sent));
        var transcription = sent.RootElement.GetProperty("session").GetProperty("audio")
            .GetProperty("input").GetProperty("transcription");
        Assert.True(transcription.TryGetProperty(expectedProperty, out _));
        Assert.False(transcription.TryGetProperty(expectedProperty == "language" ? "languages" : "language", out _));
    }

    [Fact]
    public async Task RetainedParticipant_FailsClosedForMismatchedCommitItem()
    {
        var socket = new ScriptedOpenAISocket();
        socket.Enqueue("""{"type":"session.created","session":{"id":"sess_1"}}""");
        socket.Enqueue(ReadySession("gpt-live-transcribe", "\"languages\":[\"en\"]"));
        await using var participant = new OpenAIRealtimeSpeechToTextParticipant("sk-test",
            new Uri("https://api.openai.com/v1"), new OpenAISttOptions(), socketFactory: () => socket);
        await participant.ConnectAsync(ConnectRequest("gpt-live-transcribe"));
        await participant.CommitAsync(new() { OperationId = "commit-1" });
        socket.Enqueue("""{"type":"input_audio_buffer.committed","item_id":"item_1"}""");
        socket.Enqueue("""{"type":"conversation.item.input_audio_transcription.completed","item_id":"stale_item","content_index":0,"transcript":"stale"}""");

        await using var observations = participant.ReadObservationsAsync().GetAsyncEnumerator();
        await Assert.ThrowsAsync<InvalidDataException>(async () => await observations.MoveNextAsync().AsTask());
    }

    [Theory]
    [InlineData("item_1", 1)]
    [InlineData("stale_item", 0)]
    public async Task RetainedParticipant_FailsClosedForInvalidCompletion(string itemId, int contentIndex)
    {
        var socket = new ScriptedOpenAISocket();
        socket.Enqueue("""{"type":"session.created","session":{"id":"sess_1"}}""");
        socket.Enqueue(ReadySession("gpt-live-transcribe", "\"languages\":[\"en\"]"));
        await using var participant = new OpenAIRealtimeSpeechToTextParticipant("sk-test",
            new Uri("https://api.openai.com/v1"), new OpenAISttOptions(), socketFactory: () => socket);
        await participant.ConnectAsync(ConnectRequest("gpt-live-transcribe"));
        await participant.CommitAsync(new() { OperationId = "commit-1" });
        socket.Enqueue("""{"type":"input_audio_buffer.committed","item_id":"item_1"}""");
        socket.Enqueue($$"""{"type":"conversation.item.input_audio_transcription.completed","item_id":"{{itemId}}","content_index":{{contentIndex}},"transcript":"text"}""");
        await using var observations = participant.ReadObservationsAsync().GetAsyncEnumerator();
        await Assert.ThrowsAsync<InvalidDataException>(async () => await observations.MoveNextAsync().AsTask());
    }

    [Fact]
    public async Task RetainedParticipant_RejectsReadinessMismatch()
    {
        var socket = new ScriptedOpenAISocket();
        socket.Enqueue("""{"type":"session.created","session":{"id":"sess_1"}}""");
        socket.Enqueue(ReadySession("wrong-model", "\"languages\":[\"en\"]"));
        await using var participant = new OpenAIRealtimeSpeechToTextParticipant("sk-test",
            new Uri("https://api.openai.com/v1"), new OpenAISttOptions(), socketFactory: () => socket);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await participant.ConnectAsync(ConnectRequest("gpt-live-transcribe")));
    }

    [Fact]
    public async Task RetainedParticipant_RejectsDuplicateCompletion()
    {
        var socket = new ScriptedOpenAISocket();
        socket.Enqueue("""{"type":"session.created","session":{"id":"sess_1"}}""");
        socket.Enqueue(ReadySession("gpt-live-transcribe", "\"languages\":[\"en\"]"));
        await using var participant = new OpenAIRealtimeSpeechToTextParticipant("sk-test",
            new Uri("https://api.openai.com/v1"), new OpenAISttOptions(), socketFactory: () => socket);
        await participant.ConnectAsync(ConnectRequest("gpt-live-transcribe"));
        await participant.CommitAsync(new() { OperationId = "commit-1" });
        socket.Enqueue("""{"type":"input_audio_buffer.committed","item_id":"item_1"}""");
        socket.Enqueue("""{"type":"conversation.item.input_audio_transcription.completed","item_id":"item_1","content_index":0,"transcript":"first"}""");
        socket.Enqueue("""{"type":"conversation.item.input_audio_transcription.completed","item_id":"item_1","content_index":0,"transcript":"duplicate"}""");
        await using var observations = participant.ReadObservationsAsync().GetAsyncEnumerator();
        Assert.True(await observations.MoveNextAsync());
        Assert.Equal("first", observations.Current.Text);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await observations.MoveNextAsync().AsTask());
    }

    [Fact]
    public async Task RetainedParticipant_ReportsMalformedAndOversizedEventsWithoutText()
    {
        var socket = new ScriptedOpenAISocket();
        socket.Enqueue("""{"type":"session.created","session":{"id":"sess_1"}}""");
        socket.Enqueue(ReadySession("gpt-live-transcribe", "\"languages\":[\"en\"]"));
        await using var participant = new OpenAIRealtimeSpeechToTextParticipant("sk-test",
            new Uri("https://api.openai.com/v1"), new OpenAISttOptions(), socketFactory: () => socket);
        await participant.ConnectAsync(ConnectRequest("gpt-live-transcribe"));
        socket.Enqueue("{");
        socket.EnqueueCapacityExceeded("digest");
        await using var observations = participant.ReadObservationsAsync().GetAsyncEnumerator();
        Assert.True(await observations.MoveNextAsync());
        Assert.Equal("malformed-provider-message", observations.Current.SafeCode);
        Assert.Null(observations.Current.Text);
        Assert.True(await observations.MoveNextAsync());
        Assert.Equal("provider-message-capacity-exceeded", observations.Current.SafeCode);
        Assert.Null(observations.Current.Text);
    }

    [Fact]
    public async Task RetainedParticipant_StopCancelsReaderAndClosesSession()
    {
        var socket = new ScriptedOpenAISocket();
        socket.Enqueue("""{"type":"session.created","session":{"id":"sess_1"}}""");
        socket.Enqueue(ReadySession("gpt-live-transcribe", "\"languages\":[\"en\"]"));
        await using var participant = new OpenAIRealtimeSpeechToTextParticipant("sk-test",
            new Uri("https://api.openai.com/v1"), new OpenAISttOptions(), socketFactory: () => socket);
        await participant.ConnectAsync(ConnectRequest("gpt-live-transcribe"));
        await using var observations = participant.ReadObservationsAsync().GetAsyncEnumerator();
        var next = observations.MoveNextAsync().AsTask();
        await participant.StopAsync();
        Assert.True(await next);
        Assert.Equal(StreamingSpeechToTextObservationKind.SessionClosed, observations.Current.Kind);
        Assert.Equal(StreamingSpeechToTextParticipantState.Stopped, participant.State);
    }

    private static StreamingSpeechToTextConnectRequest ConnectRequest(string model) => new()
    {
        ModelId = model,
        AudioFormat = new() { SampleRateHz = 48_000, ChannelCount = 1, BitsPerSample = 16 },
        CommitStrategy = StreamingSpeechToTextCommitStrategy.Manual,
        LanguageCode = "en"
    };

    private static string ReadySession(string model, string language) =>
        "{\"type\":\"session.updated\",\"session\":{\"id\":\"sess_1\",\"type\":\"transcription\"," +
        "\"audio\":{\"input\":{\"format\":{\"type\":\"audio/pcm\",\"rate\":24000}," +
        $"\"transcription\":{{\"model\":\"{model}\",{language}}},\"turn_detection\":null}}}}}}}}";

    [Fact]
    public void ValidateConfiguration_MissingApiKeyFails()
    {
        var provider = new OpenAIAudioProvider();

        var result = provider.ValidateConfiguration(
            new ProviderClientConfig
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
            new ProviderClientConfig
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
    public void SpeechToTextClient_ExposesRetainedParticipantFactoryWithoutSecondClient()
    {
        var provider = new OpenAIAudioProvider();
        using var client = provider.CreateSpeechToTextClient(new SpeechToTextClientConfig
        {
            ProviderKey = "openai",
            ApiKey = "sk-test",
            ModelName = "gpt-live-transcribe",
            SpeechLanguage = "en",
            ProviderOptions = new OpenAISttOptions { Keywords = ["HPD"] }
        });

        var factory = Assert.IsAssignableFrom<IStreamingSpeechToTextParticipantFactory>(
            client.GetService(typeof(IStreamingSpeechToTextParticipantFactory)));
        Assert.Equal("gpt-live-transcribe", factory.Configuration.ModelId);
        Assert.Equal("en", factory.Configuration.LanguageCode);
        Assert.Equal(["HPD"], factory.Configuration.Keyterms);
        Assert.False(factory.Configuration.IncludeTimestamps);
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
                new ProviderClientConfig
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
            new ProviderClientConfig
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

        var normalizer = Assert.IsAssignableFrom<ISpeechToTextEvidenceNormalizer>(
            client.GetService(typeof(ISpeechToTextEvidenceNormalizer)));
        Assert.True(normalizer.Capabilities.Supports(
            SpeechToTextEvidenceCapability.TranscriptText));
        Assert.True(normalizer.Capabilities.Supports(
            SpeechToTextEvidenceCapability.WordTiming));
        Assert.True(normalizer.Capabilities.Supports(
            SpeechToTextEvidenceCapability.SegmentTiming));
        Assert.False(normalizer.Capabilities.Supports(
            SpeechToTextEvidenceCapability.ExplicitSpeechClassification));

        var evidence = normalizer.Normalize(
            new SpeechToTextResponse("ordinary transcript"),
            new InputContentId("openai-batch"));
        Assert.Equal(SpeechContentEvidenceKind.Unobservable, evidence.Kind);
        Assert.Equal(OpenAIAudioProvider.Key, evidence.ProviderKey);
    }

    [Fact]
    public void ProviderConfigRegistration_RoundTripsSttConfig()
    {
        var config = new OpenAISttOptions
        {
            Prompt = "Names may include Jeff.",
            Temperature = 0.1f,
            ResponseFormat = "verbose_json",
            TimestampGranularities = ["word", "segment"],
            IncludeLogprobs = true
        };

        var json = JsonSerializer.Serialize(config, OpenAISttJsonContext.Default.OpenAISttOptions);
        var deserialized = JsonSerializer.Deserialize(json, OpenAISttJsonContext.Default.OpenAISttOptions);

        var roundTripped = Assert.IsType<OpenAISttOptions>(deserialized);
        Assert.Equal("Names may include Jeff.", roundTripped.Prompt);
        Assert.Equal(0.1f, roundTripped.Temperature);
        Assert.Equal("verbose_json", roundTripped.ResponseFormat);
        Assert.Equal(["word", "segment"], roundTripped.TimestampGranularities);
        Assert.True(roundTripped.IncludeLogprobs);
    }

    [SkippableFact]
    public async Task LiveTextToSpeech_WithConfiguredApiKey_ReturnsAudio()
    {
        Skip.IfNot(
            string.Equals(
                System.Environment.GetEnvironmentVariable("HPD_AUDIO_LIVE_SMOKE"),
                "1",
                StringComparison.Ordinal),
            "Set HPD_AUDIO_LIVE_SMOKE=1 and OPENAI_API_KEY to run the credentialed OpenAI audio smoke test.");
        var apiKey = System.Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Skip.If(
            string.IsNullOrWhiteSpace(apiKey),
            "Set OPENAI_API_KEY to run the credentialed OpenAI audio smoke test.");

        var provider = new OpenAIAudioProvider();
        using var client = provider.CreateTextToSpeechClient(
            new ProviderClientConfig
            {
                ProviderKey = "openai",
                ApiKey = apiKey,
                ModelName = System.Environment.GetEnvironmentVariable("OPENAI_TTS_MODEL") ??
                    OpenAIAudioProvider.DefaultTextToSpeechModel
            });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var response = await client.GetAudioAsync(
            System.Environment.GetEnvironmentVariable("OPENAI_TTS_SMOKE_TEXT") ?? "HPD audio smoke.",
            new TextToSpeechOptions
            {
                VoiceId = System.Environment.GetEnvironmentVariable("OPENAI_TTS_VOICE") ??
                    OpenAIAudioProvider.DefaultTextToSpeechVoice,
                AudioFormat = "mp3"
            },
            cts.Token);

        var audio = Assert.Single(response.Contents.OfType<DataContent>());
        Assert.Equal("audio/mpeg", audio.MediaType);
        Assert.NotEmpty(audio.Data.ToArray());
    }

    [SkippableFact]
    public async Task LiveRealtimeAgentTurn_WithConfiguredApiKey_ReturnsText()
    {
        Skip.IfNot(
            string.Equals(
                System.Environment.GetEnvironmentVariable("HPD_REALTIME_LIVE_SMOKE"),
                "1",
                StringComparison.Ordinal),
            "Set HPD_REALTIME_LIVE_SMOKE=1 and OPENAI_API_KEY to run the credentialed OpenAI realtime smoke test.");
        var apiKey = System.Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Skip.If(
            string.IsNullOrWhiteSpace(apiKey),
            "Set OPENAI_API_KEY to run the credentialed OpenAI realtime smoke test.");

        var provider = new OpenAIAudioProvider();
        using var realtimeClient = provider.CreateRealtimeClient(
            new ProviderClientConfig
            {
                ProviderKey = "openai",
                ApiKey = apiKey,
                ModelName = System.Environment.GetEnvironmentVariable("OPENAI_REALTIME_MODEL") ??
                    OpenAIAudioProvider.DefaultRealtimeModel
            });
        var agent = await AgentBuilder
            .Create()
            .BuildAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        try
        {
            var result = await agent.RunAsync(
                System.Environment.GetEnvironmentVariable("OPENAI_REALTIME_SMOKE_PROMPT") ??
                    "Reply with exactly three words.",
                runConfig: new AgentRunConfig
                {
                    Clients = new AgentClientsConfig
                    {
                        Transport = AgentModelTransportMode.Realtime,
                        Realtime = new RealtimeClientConfig
                        {
                            Override = new ClientOverride<IRealtimeClient> { Client = realtimeClient }
                        }
                    }
                },
                cancellationToken: cts.Token);

            Assert.NotNull(result);
            Assert.False(string.IsNullOrWhiteSpace(result.Text));
        }
        finally
        {
            agent.Dispose();
        }
    }

    private sealed class ScriptedOpenAISocket : IOpenAIRealtimeSttSocket
    {
        private readonly Channel<OpenAIRealtimeSttSocketMessage> _messages = Channel.CreateUnbounded<OpenAIRealtimeSttSocketMessage>();
        internal List<byte[]> Sent { get; } = [];
        public bool IsOpen { get; private set; }
        internal void Enqueue(string json) => _messages.Writer.TryWrite(new(false,
            System.Text.Encoding.UTF8.GetBytes(json)));
        internal void EnqueueCapacityExceeded(string digest) =>
            _messages.Writer.TryWrite(new(false, ReadOnlyMemory<byte>.Empty, true, digest));
        public ValueTask ConnectAsync(Uri uri, string apiKey, IReadOnlyDictionary<string, string>? headers,
            CancellationToken cancellationToken)
        { IsOpen = true; return ValueTask.CompletedTask; }
        public ValueTask SendTextAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        { Sent.Add(payload.ToArray()); return ValueTask.CompletedTask; }
        public async ValueTask<OpenAIRealtimeSttSocketMessage> ReceiveAsync(CancellationToken cancellationToken) =>
            await _messages.Reader.ReadAsync(cancellationToken);
        public ValueTask CloseAsync(CancellationToken cancellationToken)
        { IsOpen = false; _messages.Writer.TryComplete(); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

#pragma warning restore EXTEXP0001
