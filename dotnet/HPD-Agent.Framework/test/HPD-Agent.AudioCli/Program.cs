using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.AgentIntegration.Thread;
using HPD.Agent.Audio.AgentIntegration.Middleware;
using HPD.Agent.Audio.AgentIntegration.Output;
using HPD.Agent.Audio.Interaction;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio.Runtime.Output;
using HPD.Agent.Audio.Trace;
using HPD.Agent.Providers;
using HPD.Agent.Providers.Audio.ElevenLabs;
using HPD.Agent.Providers.Audio.Meai;
using HPD.Agent.Providers.Audio.OpenAI;
using Microsoft.Extensions.AI;

#pragma warning disable MEAI001

AudioCliOptions options;
try
{
    options = AudioCliOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    PrintUsage();
    return 2;
}

if (options.ShowHelp)
{
    PrintUsage();
    return 0;
}

using var appsettings = LoadAppSettings(options.AppSettingsPath);

var audioPaths = options.AudioPaths.Count > 0
    ? options.AudioPaths
    : SplitList(GetConfigString(appsettings, "AudioCli", "AudioPath"));
var sttProvider = FirstNonWhiteSpace(
    options.SttProvider,
    GetConfigString(appsettings, "AudioCli", "SttProvider"),
    OpenAIAudioProvider.Key)!;
var sttModel = FirstNonWhiteSpace(
    options.SttModel,
    GetConfigString(appsettings, "AudioCli", "SttModel"),
    string.Equals(sttProvider, ElevenLabsAudioProvider.Key, StringComparison.OrdinalIgnoreCase)
        ? ElevenLabsAudioProvider.DefaultSpeechToTextModel
        : OpenAIAudioProvider.DefaultSpeechToTextModel)!;
var chatModel = FirstNonWhiteSpace(
    options.ChatModel,
    GetConfigString(appsettings, "AudioCli", "ChatModel"),
    "gpt-5.5")!;
var realtimeEnabledByConfig = GetConfigBool(appsettings, "AudioCli", "RealtimeEnabled") ?? false;
var realtimeRequested = options.RealtimeEnabled || realtimeEnabledByConfig;
var realtimeModel = FirstNonWhiteSpace(
    options.RealtimeModel,
    GetConfigString(appsettings, "AudioCli", "RealtimeModel"),
    OpenAIAudioProvider.DefaultRealtimeModel)!;
var realtimeVoice = FirstNonWhiteSpace(
    options.RealtimeVoice,
    GetConfigString(appsettings, "AudioCli", "RealtimeVoice"));
var realtimeInstructions = FirstNonWhiteSpace(
    options.RealtimeInstructions,
    GetConfigString(appsettings, "AudioCli", "RealtimeInstructions"));
var realtimeMathToolsRequested = options.RealtimeMathTools ||
    (GetConfigBool(appsettings, "AudioCli", "RealtimeMathTools") ?? false);
if (realtimeMathToolsRequested)
{
    realtimeRequested = true;
    realtimeInstructions = FirstNonWhiteSpace(
        realtimeInstructions,
        "You are validating HPD realtime function calling. Use the available math tools for arithmetic requests. For multi-step arithmetic, call the needed tools in sequence, use prior tool results as inputs when needed, and answer with the final number.");
}
var language = FirstNonWhiteSpace(
    options.Language,
    GetConfigString(appsettings, "AudioCli", "Language"));
var textLanguage = FirstNonWhiteSpace(
    options.TextLanguage,
    GetConfigString(appsettings, "AudioCli", "TextLanguage"));
var sttPrompt = FirstNonWhiteSpace(
    options.SttPrompt,
    GetConfigString(appsettings, "AudioCli", "SttPrompt"));
var realtimeTranscriptionEnabledByConfig = GetConfigBool(appsettings, "AudioCli", "RealtimeTranscriptionEnabled");
var realtimeTranscriptionRequested = options.RealtimeTranscriptionEnabled
    ?? realtimeTranscriptionEnabledByConfig
    ?? realtimeRequested;
var realtimeTranscriptionModel = FirstNonWhiteSpace(
    options.RealtimeTranscriptionModel,
    GetConfigString(appsettings, "AudioCli", "RealtimeTranscriptionModel"),
    sttModel);
var realtimeTranscriptionLanguage = FirstNonWhiteSpace(
    options.RealtimeTranscriptionLanguage,
    GetConfigString(appsettings, "AudioCli", "RealtimeTranscriptionLanguage"),
    language);
var realtimeTranscriptionPrompt = FirstNonWhiteSpace(
    options.RealtimeTranscriptionPrompt,
    GetConfigString(appsettings, "AudioCli", "RealtimeTranscriptionPrompt"),
    sttPrompt);
var sttResponseFormat = FirstNonWhiteSpace(
    options.SttResponseFormat,
    GetConfigString(appsettings, "AudioCli", "SttResponseFormat"));
var sttTimestampGranularities = options.SttTimestampGranularities.Count > 0
    ? options.SttTimestampGranularities
    : SplitList(GetConfigString(appsettings, "AudioCli", "SttTimestampGranularities"));
var sttTemperature = options.SttTemperature
    ?? GetConfigSingle(appsettings, "AudioCli", "SttTemperature");
var realtimeTranscriptionOptions = realtimeRequested && realtimeTranscriptionRequested
    ? new TranscriptionOptions
    {
        ModelId = realtimeTranscriptionModel,
        SpeechLanguage = realtimeTranscriptionLanguage,
        Prompt = realtimeTranscriptionPrompt
    }
    : null;
var includeLogprobs = options.IncludeLogprobs
    ?? GetConfigBool(appsettings, "AudioCli", "IncludeLogprobs");
var mediaType = FirstNonWhiteSpace(
    options.MediaType,
    GetConfigString(appsettings, "AudioCli", "MediaType"));
var sessionId = FirstNonWhiteSpace(
    options.SessionId,
    GetConfigString(appsettings, "AudioCli", "SessionId"))
    ?? $"audio-cli-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
var threadId = FirstNonWhiteSpace(
    options.ThreadId,
    GetConfigString(appsettings, "AudioCli", "ThreadId"))
    ?? "main";
var text = FirstNonWhiteSpace(
    options.Text,
    GetConfigString(appsettings, "AudioCli", "Text"));
var followUpTexts = options.TurnTexts;
var openAIEndpoint = FirstNonWhiteSpace(
    options.OpenAIEndpoint,
    GetConfigString(appsettings, "openai", "Endpoint"),
    GetConfigString(appsettings, "OpenAI", "Endpoint"),
    GetConfigString(appsettings, "Providers", "OpenAI", "Endpoint"));
var apiKey = FirstNonWhiteSpace(
    options.OpenAIApiKey,
    GetConfigString(appsettings, "openai", "ApiKey"),
    GetConfigString(appsettings, "OpenAI", "ApiKey"),
    GetConfigString(appsettings, "Providers", "OpenAI", "ApiKey"),
    System.Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
if (!options.SttStreamingSmoke && string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("OpenAI API key is required. Pass --openai-key, set OPENAI_API_KEY, or add openai:ApiKey to appsettings.json.");
    return 2;
}

var ttsRouteMode = options.TtsRouteMode
    ?? TryParseRouteMode(GetConfigString(appsettings, "AudioCli", "TtsRoute"))
    ?? TryParseRouteMode(GetConfigString(appsettings, "AudioCli", "TtsRouteMode"))
    ?? ProgressiveTextToSpeechRouteMode.Auto;
var ttsPushAggregationMode = options.TtsPushAggregationMode
    ?? TryParsePushAggregationMode(GetConfigString(appsettings, "AudioCli", "TtsPushAggregation"))
    ?? TryParsePushAggregationMode(GetConfigString(appsettings, "AudioCli", "TtsPushAggregationMode"))
    ?? TryParsePushAggregationMode(GetConfigString(appsettings, "elevenlabs", "PushTextAggregationMode"))
    ?? TryParsePushAggregationMode(GetConfigString(appsettings, "ElevenLabs", "PushTextAggregationMode"))
    ?? PushTextInputAggregationMode.ProviderDefault;
var artifactCapturePolicy = options.TtsArtifactCapturePolicy
    ?? TryParseArtifactCapturePolicy(GetConfigString(appsettings, "AudioCli", "TtsArtifactCapture"))
    ?? TryParseArtifactCapturePolicy(GetConfigString(appsettings, "AudioCli", "TtsArtifactCapturePolicy"))
    ?? AssistantAudioArtifactCapturePolicy.ContentStoreArtifact;
var ttsEnabledByConfig = GetConfigBool(appsettings, "AudioCli", "TtsEnabled") ?? false;
var ttsPushTextEnabledByConfig =
    GetConfigBool(appsettings, "AudioCli", "TtsPushTextEnabled") ??
    GetConfigBool(appsettings, "AudioCli", "EnablePushTextStreaming") ??
    GetConfigBool(appsettings, "elevenlabs", "EnablePushTextStreaming") ??
    GetConfigBool(appsettings, "ElevenLabs", "EnablePushTextStreaming") ??
    false;
var ttsPushTextRequested = options.TtsPushTextEnabled ||
    ttsPushTextEnabledByConfig ||
    ttsRouteMode == ProgressiveTextToSpeechRouteMode.ForcePushText;
var ttsRequested = (options.TtsEnabled ?? false) ||
    options.TtsProgressive ||
    options.TtsProviderExplicit ||
    options.TtsRouteMode.HasValue ||
    options.TtsArtifactCapturePolicy.HasValue ||
    ttsPushTextRequested ||
    (ttsEnabledByConfig && options.TtsDisabled != true);
if (options.TtsDisabled)
{
    ttsRequested = false;
}

var ttsProvider = FirstNonWhiteSpace(
    options.TtsProvider,
    GetConfigString(appsettings, "AudioCli", "TtsProvider"),
    ttsRequested ? ElevenLabsAudioProvider.Key : null);
var ttsModel = FirstNonWhiteSpace(
    options.TtsModel,
    GetConfigString(appsettings, "AudioCli", "TtsModel"),
    GetConfigString(appsettings, "elevenlabs", "DefaultModelId"),
    GetConfigString(appsettings, "ElevenLabs", "DefaultModelId"));
var ttsVoice = FirstNonWhiteSpace(
    options.TtsVoice,
    GetConfigString(appsettings, "AudioCli", "TtsVoice"),
    GetConfigString(appsettings, "elevenlabs", "DefaultVoiceId"),
    GetConfigString(appsettings, "ElevenLabs", "DefaultVoiceId"));
var ttsFormat = FirstNonWhiteSpace(
    options.TtsFormat,
    GetConfigString(appsettings, "AudioCli", "TtsFormat"),
    GetConfigString(appsettings, "elevenlabs", "OutputFormat"),
    GetConfigString(appsettings, "ElevenLabs", "OutputFormat"));
var elevenLabsWebSocketBaseUrl = FirstNonWhiteSpace(
    options.ElevenLabsWebSocketBaseUrl,
    GetConfigString(appsettings, "AudioCli", "ElevenLabsWebSocketBaseUrl"),
    GetConfigString(appsettings, "AudioCli", "TtsWebSocketBaseUrl"),
    GetConfigString(appsettings, "elevenlabs", "WebSocketBaseUrl"),
    GetConfigString(appsettings, "ElevenLabs", "WebSocketBaseUrl"));
var ttsLanguage = FirstNonWhiteSpace(
    options.TtsLanguage,
    GetConfigString(appsettings, "AudioCli", "TtsLanguage"));
var ttsExportDir = FirstNonWhiteSpace(
    options.TtsOutputDirectory,
    GetConfigString(appsettings, "AudioCli", "TtsOutputDir"),
    GetConfigString(appsettings, "AudioCli", "TtsOutputDirectory"));
var ttsSpeed = options.TtsSpeed
    ?? GetConfigSingle(appsettings, "AudioCli", "TtsSpeed")
    ?? GetConfigSingle(appsettings, "elevenlabs", "Speed")
    ?? GetConfigSingle(appsettings, "ElevenLabs", "Speed");
var elevenLabsApiKey = FirstNonWhiteSpace(
    options.ElevenLabsApiKey,
    GetConfigString(appsettings, "elevenlabs", "ApiKey"),
    GetConfigString(appsettings, "ElevenLabs", "ApiKey"),
    GetConfigString(appsettings, "Providers", "ElevenLabs", "ApiKey"),
    System.Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY"));
var benchmarkEnabled = options.Benchmark ||
    !string.IsNullOrWhiteSpace(options.BenchmarkOutputPath) ||
    (GetConfigBool(appsettings, "AudioCli", "Benchmark") ?? false);
var benchmarkOutputPath = FirstNonWhiteSpace(
    options.BenchmarkOutputPath,
    GetConfigString(appsettings, "AudioCli", "BenchmarkOutputPath"),
    GetConfigString(appsettings, "AudioCli", "BenchmarkOutput"));
var playbackSinkName = FirstNonWhiteSpace(
    options.PlaybackSink,
    GetConfigString(appsettings, "AudioCli", "PlaybackSink"));
var useManualPlaybackSink = string.Equals(playbackSinkName, "manual-clock", StringComparison.OrdinalIgnoreCase);
var ttsProgressive = options.TtsProgressive ||
    useManualPlaybackSink ||
    options.TtsRouteMode.HasValue ||
    ttsPushTextRequested;

if (!string.IsNullOrWhiteSpace(playbackSinkName) && !useManualPlaybackSink)
{
    Console.Error.WriteLine($"Unsupported playback sink '{playbackSinkName}'. Supported playback sink: manual-clock.");
    return 2;
}

if (useManualPlaybackSink)
{
    ttsRequested = true;
    ttsProvider = FirstNonWhiteSpace(ttsProvider, ElevenLabsAudioProvider.Key);
}

if (ttsRequested && string.Equals(ttsProvider, ElevenLabsAudioProvider.Key, StringComparison.OrdinalIgnoreCase))
{
    ttsModel = FirstNonWhiteSpace(ttsModel, ElevenLabsAudioProvider.DefaultTextToSpeechModel);
    ttsVoice = FirstNonWhiteSpace(ttsVoice, ElevenLabsAudioProvider.DefaultVoiceId);
    ttsFormat = FirstNonWhiteSpace(ttsFormat, ElevenLabsAudioProvider.DefaultOutputFormat);
}

var sttUsesOpenAI = string.Equals(sttProvider, OpenAIAudioProvider.Key, StringComparison.OrdinalIgnoreCase);
var sttUsesElevenLabs = string.Equals(sttProvider, ElevenLabsAudioProvider.Key, StringComparison.OrdinalIgnoreCase);
if (!sttUsesOpenAI && !sttUsesElevenLabs)
{
    Console.Error.WriteLine($"Unsupported STT provider '{sttProvider}'. Supported providers: {OpenAIAudioProvider.Key}, {ElevenLabsAudioProvider.Key}.");
    return 2;
}

if (options.SttStreamingSmoke && sttUsesOpenAI && string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("OpenAI API key is required for --stt-streaming-smoke with --stt-provider openai. Pass --openai-key, set OPENAI_API_KEY, or add openai:ApiKey to appsettings.json.");
    return 2;
}

if (sttUsesElevenLabs && string.IsNullOrWhiteSpace(elevenLabsApiKey))
{
    Console.Error.WriteLine("ElevenLabs API key is required for --stt-provider elevenlabs. Pass --elevenlabs-key, set ELEVENLABS_API_KEY, or add elevenlabs:ApiKey to appsettings.json.");
    return 2;
}

if (options.SttStreamingSmoke &&
    sttUsesElevenLabs &&
    string.IsNullOrWhiteSpace(options.SttModel) &&
    string.IsNullOrWhiteSpace(GetConfigString(appsettings, "AudioCli", "SttModel")))
{
    sttModel = ElevenLabsAudioProvider.DefaultRealtimeSpeechToTextModel;
}

var textOnlyTurns = new List<string>();
if (audioPaths.Count == 0 && !string.IsNullOrWhiteSpace(text))
{
    textOnlyTurns.Add(text);
}

textOnlyTurns.AddRange(followUpTexts);

if (audioPaths.Count == 0 && textOnlyTurns.Count == 0)
{
    Console.Error.WriteLine("Audio file path or text input is required.");
    PrintUsage();
    return 2;
}

foreach (var audioPath in audioPaths)
{
    if (!File.Exists(audioPath))
    {
        Console.Error.WriteLine($"Audio file not found: {audioPath}");
        return 2;
    }
}

var sessionStore = new InMemorySessionStore();
var contentStore = new InMemoryContentStore();
var builder = AgentBuilder.Create();
builder.ProviderRegistry.Register(new OpenAIAudioProvider());
if (sttUsesElevenLabs || ttsRequested)
{
    builder.ProviderRegistry.Register(new ElevenLabsAudioProvider());
}

var realtimeMathTools = realtimeMathToolsRequested
    ? CreateRealtimeMathTools()
    : [];
foreach (var tool in realtimeMathTools)
{
    builder.WithNativeFunction(tool);
}

builder.Config.SetChatClientConfig(new ChatClientConfig
{
    ProviderKey = OpenAIAudioProvider.Key,
    ModelName = chatModel,
    ApiKey = apiKey,
    Endpoint = openAIEndpoint
});

if (realtimeRequested)
{
    builder.Config.SetClientConfig(
        ProviderClientFamily.Realtime,
        new RealtimeClientConfig
        {
            ProviderKey = OpenAIAudioProvider.Key,
            ModelName = realtimeModel,
            ApiKey = apiKey,
            Endpoint = openAIEndpoint
        });
}

if (ttsRequested)
{
    if (!string.Equals(ttsProvider, ElevenLabsAudioProvider.Key, StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine($"Unsupported TTS provider '{ttsProvider}'. Supported provider: {ElevenLabsAudioProvider.Key}.");
        return 2;
    }

    if (string.IsNullOrWhiteSpace(elevenLabsApiKey))
    {
        Console.Error.WriteLine("ElevenLabs API key is required for --tts. Pass --elevenlabs-key, set ELEVENLABS_API_KEY, or add elevenlabs:ApiKey to appsettings.json.");
        return 2;
    }

    var ttsProviderConfig = new TextToSpeechClientConfig
    {
        ProviderKey = ElevenLabsAudioProvider.Key,
        ModelName = ttsModel!,
        ApiKey = elevenLabsApiKey,
        VoiceId = ttsVoice,
        AudioFormat = ttsFormat,
        Speed = ttsSpeed.HasValue ? (float)ttsSpeed.Value : null
    };
    ttsProviderConfig.ProviderConfig = new ElevenLabsTtsConfig
    {
        WebSocketBaseUrl = elevenLabsWebSocketBaseUrl,
        EnablePushTextStreaming = ttsPushTextRequested
    };
    builder.Config.SetClientConfig(ProviderClientFamily.TextToSpeech, ttsProviderConfig);
}

var sttProviderConfig = sttUsesElevenLabs
    ? new SpeechToTextClientConfig
    {
        ProviderKey = ElevenLabsAudioProvider.Key,
        ModelName = sttModel,
        ApiKey = elevenLabsApiKey,
        SpeechLanguage = language
    }
    : new SpeechToTextClientConfig
    {
        ProviderKey = OpenAIAudioProvider.Key,
        ModelName = sttModel,
        ApiKey = apiKey,
        Endpoint = openAIEndpoint
    };
if (sttUsesElevenLabs)
{
    sttProviderConfig.ProviderConfig = new ElevenLabsSttConfig
    {
        WebSocketBaseUrl = elevenLabsWebSocketBaseUrl
    };
    sttProviderConfig.ProviderOptions = new ElevenLabsSttOptions
    {
        RealtimeModelId = options.SttStreamingSmoke ? sttModel : null,
        AudioFormat = options.SttStreamingSmoke ? FirstNonWhiteSpace(mediaType, "pcm_16000") : null,
        CommitStrategy = options.SttStreamingSmoke ? "manual" : null,
        IncludeTimestamps = options.SttStreamingSmoke,
        IncludeLanguageDetection = options.SttStreamingSmoke,
        TimestampsGranularity = sttTimestampGranularities.Count > 0
            ? sttTimestampGranularities[0]
            : null
    };
}
else
{
    sttProviderConfig.ProviderOptions = new OpenAISttOptions
    {
        Prompt = sttPrompt,
        Temperature = sttTemperature,
        ResponseFormat = sttResponseFormat,
        TimestampGranularities = sttTimestampGranularities.Count > 0 ? [.. sttTimestampGranularities] : null,
        IncludeLogprobs = includeLogprobs
    };
}
builder.Config.SetClientConfig(ProviderClientFamily.SpeechToText, sttProviderConfig);

if (options.SttStreamingSmoke)
{
    await RunStreamingSttSmokeAsync(
        builder.ProviderRegistry,
        sttProviderConfig,
        audioPaths,
        options.SttSampleRate,
        language);
    return 0;
}

var benchmark = new AudioCliBenchmarkCollector(benchmarkEnabled);
IAudioOutputSink? assistantOutputSink = null;
if (useManualPlaybackSink)
{
    assistantOutputSink = new AutoAdvancingManualClockAudioOutputSink();
    if (benchmark.Enabled)
    {
        assistantOutputSink = new BenchmarkingAudioOutputSink(assistantOutputSink, benchmark);
    }
}

var audioOptions = new AudioRuntimeAttachmentOptions
{
    ThreadProjectionSink = new SessionThreadProjectionSink(sessionStore),
    RunAudioInteractionRuntime = true,
    EnableAssistantOutputPlayback = useManualPlaybackSink,
    AssistantAudioOutputSink = assistantOutputSink,
    AssistantOutputSynthesisMode = ttsProgressive
        ? AssistantOutputSynthesisMode.ProgressiveWithFinalFallback
        : ttsRequested
            ? AssistantOutputSynthesisMode.FinalText
            : AssistantOutputSynthesisMode.Disabled,
    AssistantOutputPacingOptions = new TextToSpeechPacingOptions
    {
        Continuation = new TextToSpeechContinuationOptions
        {
            MaxCharacters = options.TtsMaxSegmentChars ?? 320
        }
    },
    AssistantOutputProgressiveRouteMode = ttsRouteMode,
    AssistantOutputPushTextAggregationMode = ttsPushAggregationMode,
    AssistantOutputArtifactCapturePolicy = artifactCapturePolicy,
    AssistantOutputProviderKey = ttsRequested ? ttsProvider : null,
    AssistantOutputModelId = ttsModel,
    AssistantOutputVoiceId = ttsVoice,
    AssistantOutputLanguage = ttsLanguage,
    AssistantOutputFormat = ttsFormat,
    AssistantOutputSpeed = ttsSpeed
};
audioOptions.UseSpeechToTextProvider(
    builder.ProviderRegistry,
    new InputMediaSpeechToTextProviderOptions
    {
        ProviderKey = sttProviderConfig.ProviderKey,
        ModelId = sttModel,
        SpeechLanguage = language,
        TextLanguage = textLanguage,
        ProviderConfig = sttProviderConfig
    });
var audioAttachment = new AudioRuntimeAttachment(audioOptions);

builder
    .WithName("HPD Audio CLI")
    .WithSessionStore(sessionStore)
    .WithContentStore(contentStore)
    .WithMiddleware(audioAttachment);

var agent = await builder.BuildAsync();
await EnsureSessionAsync(agent, sessionId);

using var textSubscription = agent.Subscribe<TextDeltaEvent>(evt =>
{
    benchmark.RecordTextDelta(evt);
    Console.ForegroundColor = ConsoleColor.Gray;
    Console.Write(evt.Text);
    Console.ResetColor();
});
using var turnStartedSubscription = agent.Subscribe<MessageTurnStartedEvent>(evt =>
{
    benchmark.RecordTurnStarted(evt);
    Console.ForegroundColor = ConsoleColor.DarkCyan;
    Console.Error.WriteLine($"[turn:start] {evt.AgentName} {evt.MessageTurnId}");
    Console.ResetColor();
});
using var turnFinishedSubscription = agent.Subscribe<MessageTurnFinishedEvent>(evt =>
{
    benchmark.RecordTurnFinished(evt);
    Console.ForegroundColor = ConsoleColor.DarkCyan;
    Console.Error.WriteLine($"[turn:end] {evt.AgentName} {evt.MessageTurnId} duration={evt.Duration.TotalMilliseconds:0}ms");
    Console.ResetColor();
});
using var realtimeTranscriptCompletedSubscription = agent.Subscribe<UserAudioTranscriptCompletedEvent>(evt =>
{
    Console.ForegroundColor = ConsoleColor.DarkGreen;
    Console.Error.WriteLine($"[realtime-transcript] {evt.Text}");
    Console.ResetColor();
});
using var realtimeTranscriptFailedSubscription = agent.Subscribe<UserAudioTranscriptFailedEvent>(evt =>
{
    benchmark.RecordError(evt.ErrorMessage);
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Error.WriteLine($"[realtime-transcript:error] {evt.ErrorMessage}");
    Console.ResetColor();
});
using var streamStartedSubscription = agent.Subscribe<AssistantAudioOutputStreamStartedEvent>(benchmark.RecordOutputStreamStarted);
using var chunkReadySubscription = agent.Subscribe<AssistantAudioOutputChunkReadyEvent>(benchmark.RecordOutputChunkReady);
using var pushStreamOpeningSubscription = agent.Subscribe<AssistantAudioPushTextStreamOpeningEvent>(benchmark.RecordPushTextStreamOpening);
using var pushStreamOpenedSubscription = agent.Subscribe<AssistantAudioPushTextStreamOpenedEvent>(benchmark.RecordPushTextStreamOpened);
using var pushTextSentSubscription = agent.Subscribe<AssistantAudioPushTextInputSentEvent>(benchmark.RecordPushTextInputSent);
using var artifactCapturedSubscription = agent.Subscribe<AssistantAudioOutputArtifactCapturedEvent>(benchmark.RecordArtifactCaptured);
using var outputCompletedSubscription = agent.Subscribe<AssistantAudioOutputCompletedEvent>(benchmark.RecordOutputCompleted);
using var failedSubscription = agent.Subscribe<AssistantAudioOutputFailedEvent>(evt =>
{
    benchmark.RecordError(evt.Error?.Code ?? evt.Disposition);
    return ValueTask.CompletedTask;
});
using var segmentFailedSubscription = agent.Subscribe<AssistantAudioOutputSegmentFailedEvent>(evt =>
{
    benchmark.RecordError(evt.Error?.Code ?? evt.Disposition);
    return ValueTask.CompletedTask;
});
using var anySubscription = agent.SubscribeAny(evt =>
{
    if (options.PrintEvents)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Error.WriteLine($"[event] {evt.GetType().Name}");
        Console.ResetColor();
    }

    if (evt is IErrorEvent error)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[error] {evt.GetType().Name}: {error.ErrorMessage}");
        Console.ResetColor();
    }
});

Console.WriteLine($"[audio-cli] stt={sttProvider}/{sttModel}");
Console.WriteLine($"[audio-cli] chat=openai/{chatModel}");
if (realtimeRequested)
{
    Console.WriteLine($"[audio-cli] realtime=openai/{realtimeModel}{(string.IsNullOrWhiteSpace(realtimeVoice) ? string.Empty : $" voice={realtimeVoice}")}");
    Console.WriteLine(realtimeTranscriptionOptions is null
        ? "[audio-cli] realtime-transcription=disabled"
        : $"[audio-cli] realtime-transcription=model={realtimeTranscriptionOptions.ModelId ?? "<provider-default>"} language={realtimeTranscriptionOptions.SpeechLanguage ?? "<auto>"}");
}
if (realtimeMathToolsRequested)
{
    Console.WriteLine("[audio-cli] realtime-tools=math Add,Multiply,Subtract runtime=started");
}
if (ttsRequested)
{
    var exportSuffix = string.IsNullOrWhiteSpace(ttsExportDir) ? string.Empty : $" exportDir={ttsExportDir}";
    var sinkSuffix = useManualPlaybackSink ? " playbackSink=manual-clock" : string.Empty;
    var pushSuffix = ttsPushTextRequested
        ? $" pushText=true route={ttsRouteMode} pushAggregation={ttsPushAggregationMode}"
        : $" pushText=false route={ttsRouteMode}";
    var benchmarkSuffix = benchmarkEnabled
        ? $" benchmark=true{(string.IsNullOrWhiteSpace(benchmarkOutputPath) ? string.Empty : $" benchmarkOutput={benchmarkOutputPath}")}"
        : string.Empty;
    Console.WriteLine($"[audio-cli] tts={ttsProvider}/{FirstNonWhiteSpace(ttsModel, ElevenLabsAudioProvider.DefaultTextToSpeechModel)} voice={FirstNonWhiteSpace(ttsVoice, ElevenLabsAudioProvider.DefaultVoiceId)} format={FirstNonWhiteSpace(ttsFormat, ElevenLabsAudioProvider.DefaultOutputFormat)} mode={(ttsProgressive ? "progressive" : "final")} maxSegmentChars={options.TtsMaxSegmentChars ?? 320} artifactCapture={artifactCapturePolicy} store=hpd-content{exportSuffix}{sinkSuffix}{pushSuffix}{benchmarkSuffix}");
}
else
{
    Console.WriteLine("[audio-cli] tts=disabled textOnly=true");
}

var totalTurns = audioPaths.Count + textOnlyTurns.Count;
Console.WriteLine($"[audio-cli] session={sessionId} thread={threadId} audioCount={audioPaths.Count} textTurnCount={textOnlyTurns.Count}");

for (var turnIndex = 0; turnIndex < audioPaths.Count; turnIndex++)
{
    var audioPath = audioPaths[turnIndex];
    var audio = await AudioContent.FromFileAsync(audioPath);
    if (!string.IsNullOrWhiteSpace(mediaType))
    {
        audio = new AudioContent(await File.ReadAllBytesAsync(audioPath), mediaType)
        {
            Name = Path.GetFileName(audioPath)
        };
    }

    var contents = new List<AIContent>();
    if (!string.IsNullOrWhiteSpace(text))
    {
        contents.Add(new TextContent(text));
    }

    contents.Add(audio);
    Console.WriteLine();
    Console.WriteLine($"[audio-cli] turn={turnIndex + 1}/{totalTurns} file={audio.Name} mediaType={audio.MediaType}");

    benchmark.BeginTurn(
        turnIndex + 1,
        totalTurns,
        audio.Name ?? Path.GetFileName(audioPath),
        audio.MediaType,
        ttsRequested,
        ttsProgressive,
        ttsRouteMode,
        ttsPushTextRequested,
        ttsPushAggregationMode,
        artifactCapturePolicy,
        ttsProvider,
        FirstNonWhiteSpace(ttsModel, ElevenLabsAudioProvider.DefaultTextToSpeechModel),
        FirstNonWhiteSpace(ttsVoice, ElevenLabsAudioProvider.DefaultVoiceId),
        FirstNonWhiteSpace(ttsFormat, ElevenLabsAudioProvider.DefaultOutputFormat),
        options.TtsMaxSegmentChars ?? 320);

    try
    {
        var input = new UserMessagesInputEvent { Messages = [
            new ChatMessage(ChatRole.User, contents)
        ],
            SessionId = sessionId,
            ThreadId = threadId,
            RunConfig = new AgentRunConfig
            {
                Clients = new AgentClientsConfig
                {
                    Transport = realtimeRequested
                        ? AgentModelTransportMode.Realtime
                        : AgentModelTransportMode.Auto,
                    Realtime = realtimeTranscriptionOptions is null
                        ? null
                        : new RealtimeClientConfig
                        {
                            Transcription = new RealtimeTranscriptionRunConfig
                            {
                                ModelName = realtimeTranscriptionOptions.ModelId,
                                SpeechLanguage = realtimeTranscriptionOptions.SpeechLanguage,
                                Prompt = realtimeTranscriptionOptions.Prompt
                            }
                        }
                },
                SystemInstructions = realtimeRequested
                    ? new SystemInstructionsRunConfig { Append = realtimeInstructions }
                    : null,
                Tools = realtimeMathToolsRequested
                    ? new AgentToolsRunConfig
                    {
                        Additional = realtimeMathTools,
                        Mode = ChatToolMode.Auto
                    }
                    : null
            }
        };
        await RunCliTurnAsync(agent, input, waitForThreadExecution: false);
    }
    catch (Exception ex)
    {
        benchmark.RecordError(ex.GetType().Name);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[run:error] {ex.Message}");
        if (ex.InnerException is not null)
        {
            Console.Error.WriteLine($"{ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
        }

        Console.ResetColor();
        return 1;
    }
    finally
    {
        benchmark.CompleteRun();
    }

    Console.WriteLine();
    await PrintAudioInteractionRuntimeAsync(audioAttachment);
    var playoutResults = SummarizePlayout(audioAttachment.LastOutputLedger);
    await PrintAssistantOutputResultsAsync(audioAttachment, contentStore, sessionId, ttsExportDir, playoutResults);
    await benchmark.WriteAsync(audioAttachment, benchmarkOutputPath);
}

for (var textTurnIndex = 0; textTurnIndex < textOnlyTurns.Count; textTurnIndex++)
{
    var turnText = textOnlyTurns[textTurnIndex];
    var turnNumber = audioPaths.Count + textTurnIndex + 1;

    Console.WriteLine();
    Console.WriteLine($"[audio-cli] turn={turnNumber}/{totalTurns} text={Preview(turnText)}");

    benchmark.BeginTurn(
        turnNumber,
        totalTurns,
        $"text-turn-{textTurnIndex + 1}",
        "text/plain",
        ttsRequested,
        ttsProgressive,
        ttsRouteMode,
        ttsPushTextRequested,
        ttsPushAggregationMode,
        artifactCapturePolicy,
        ttsProvider,
        FirstNonWhiteSpace(ttsModel, ElevenLabsAudioProvider.DefaultTextToSpeechModel),
        FirstNonWhiteSpace(ttsVoice, ElevenLabsAudioProvider.DefaultVoiceId),
        FirstNonWhiteSpace(ttsFormat, ElevenLabsAudioProvider.DefaultOutputFormat),
        options.TtsMaxSegmentChars ?? 320);

    try
    {
        var input = new UserMessagesInputEvent { Messages = [
            new ChatMessage(ChatRole.User, turnText)
        ],
            SessionId = sessionId,
            ThreadId = threadId,
            RunConfig = new AgentRunConfig
            {
                Clients = new AgentClientsConfig
                {
                    Transport = realtimeRequested
                        ? AgentModelTransportMode.Realtime
                        : AgentModelTransportMode.Auto,
                    Realtime = realtimeTranscriptionOptions is null
                        ? null
                        : new RealtimeClientConfig
                        {
                            Transcription = new RealtimeTranscriptionRunConfig
                            {
                                ModelName = realtimeTranscriptionOptions.ModelId,
                                SpeechLanguage = realtimeTranscriptionOptions.SpeechLanguage,
                                Prompt = realtimeTranscriptionOptions.Prompt
                            }
                        }
                },
                SystemInstructions = realtimeRequested
                    ? new SystemInstructionsRunConfig { Append = realtimeInstructions }
                    : null,
                Tools = realtimeMathToolsRequested
                    ? new AgentToolsRunConfig
                    {
                        Additional = realtimeMathTools,
                        Mode = ChatToolMode.Auto
                    }
                    : null
            }
        };
        await RunCliTurnAsync(agent, input, waitForThreadExecution: false);
    }
    catch (Exception ex)
    {
        benchmark.RecordError(ex.GetType().Name);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[run:error] {ex.Message}");
        if (ex.InnerException is not null)
        {
            Console.Error.WriteLine($"{ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
        }

        Console.ResetColor();
        return 1;
    }
    finally
    {
        benchmark.CompleteRun();
    }

    Console.WriteLine();
    var playoutResults = SummarizePlayout(audioAttachment.LastOutputLedger);
    await PrintAssistantOutputResultsAsync(audioAttachment, contentStore, sessionId, ttsExportDir, playoutResults);
    await benchmark.WriteAsync(audioAttachment, benchmarkOutputPath);
}

await PrintThreadAsync(sessionStore, sessionId, threadId);
return 0;

static async Task EnsureSessionAsync(Agent agent, string sessionId)
{
    try
    {
        await agent.CreateSessionAsync(sessionId);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
    {
        // Reuse the existing CLI session when backed by a persistent session store.
    }
}

static async Task RunCliTurnAsync(
    Agent agent,
    UserMessagesInputEvent input,
    bool waitForThreadExecution)
{
    if (!waitForThreadExecution)
    {
        await agent.RunAsync(input);
        return;
    }

    if (string.IsNullOrWhiteSpace(input.ThreadExecutionId))
    {
        throw new InvalidOperationException("ThreadExecutionId is required when waiting for a started thread execution.");
    }

    var completion = new TaskCompletionSource<ThreadExecutionFinishedEvent>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    using var completionSubscription = agent.Subscribe<ThreadExecutionFinishedEvent>(evt =>
    {
        if (string.Equals(evt.ThreadExecutionId, input.ThreadExecutionId, StringComparison.Ordinal))
        {
            completion.TrySetResult(evt);
        }

        return ValueTask.CompletedTask;
    });

    await agent.RunAsync(input);
    var completed = await completion.Task.WaitAsync(TimeSpan.FromMinutes(3));
    if (completed.Outcome == ThreadExecutionOutcome.Failed)
    {
        throw new InvalidOperationException(
            $"Thread execution {completed.ThreadExecutionId} failed: {completed.Error?.Type}: {completed.Error?.Message}");
    }
}

static async Task PrintAudioInteractionRuntimeAsync(AudioRuntimeAttachment audioAttachment)
{
    for (var i = 0; i < audioAttachment.LastResults.Count; i++)
    {
        var result = audioAttachment.LastResults[i];
        var snapshot = await result.Ledger.SnapshotAsync();
        var traceRecords = new List<RealtimeAudioTraceRecord>();
        await foreach (var traceRecord in result.Trace.ReadAsync())
        {
            traceRecords.Add(traceRecord);
        }

        var transcript = snapshot.Records
            .OfType<TranscriptLedgerRecord>()
            .LastOrDefault()?.Text;
        var committed = result.TurnDecision?.Commit?.Text;

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine($"[audio-runtime:{i}] envelopes={result.Envelopes.Count} route={result.RouteDecision?.Plan?.Topology.ToString() ?? result.RouteDecision?.Kind.ToString() ?? "-"} ledger={snapshot.Records.Count}");
        Console.ResetColor();

        foreach (var record in snapshot.Records)
        {
            Console.ForegroundColor = record switch
            {
                TranscriptLedgerRecord => ConsoleColor.Green,
                UserTurnLedgerRecord => ConsoleColor.Green,
                ThreadProjectionLedgerRecord => ConsoleColor.Cyan,
                _ => ConsoleColor.DarkGray
            };

            Console.WriteLine(record switch
            {
                InputContentLedgerRecord input =>
                    $"  - {record.Family}: {input.Content.Name ?? input.Content.Id.Value} {input.Disposition}",
                TranscriptLedgerRecord transcriptRecord =>
                    $"  - {record.Family}: \"{transcriptRecord.Text}\"",
                UserTurnLedgerRecord turn =>
                    $"  - {record.Family}: \"{turn.Text}\" reason={turn.CommitReason}",
                ThreadProjectionLedgerRecord projection =>
                    $"  - {record.Family}: \"{projection.Projection.Text}\" event={projection.ProjectedEvent?.EventId ?? "-"}",
                _ => $"  - {record.Family}: {record.GetType().Name}"
            });
            Console.ResetColor();
        }

        Console.ForegroundColor = string.IsNullOrWhiteSpace(committed ?? transcript)
            ? ConsoleColor.Yellow
            : ConsoleColor.Green;
        Console.WriteLine($"[audio-transcript] {Preview(committed ?? transcript)}");
        Console.ResetColor();

        foreach (var toolCall in traceRecords
            .OfType<AudioInteractionUpdateTraceRecord>()
            .Select(record => record.Update)
            .OfType<ToolCallUpdate>()
            .Where(update => update.IsFinal))
        {
            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            Console.WriteLine($"[audio-runtime:tool-call] id={toolCall.ToolCallId} name={toolCall.Name} args={Preview(toolCall.ArgumentsDelta)}");
            Console.ResetColor();
        }
    }
}

static IReadOnlyList<AIFunction> CreateRealtimeMathTools() =>
[
    AIFunctionFactory.Create((Func<int, int, int>)Add, name: "Add", description: "Adds two integers."),
    AIFunctionFactory.Create((Func<int, int, int>)Multiply, name: "Multiply", description: "Multiplies two integers."),
    AIFunctionFactory.Create((Func<int, int, int>)Subtract, name: "Subtract", description: "Subtracts right from left.")
];

static int Add(int left, int right) => left + right;

static int Multiply(int left, int right) => left * right;

static int Subtract(int left, int right) => left - right;

static async Task PrintAssistantOutputResultsAsync(
    AudioRuntimeAttachment audioAttachment,
    IContentStore contentStore,
    string sessionId,
    string? exportDir,
    IReadOnlyDictionary<(string OutputFlowId, int SegmentIndex), AssistantAudioPlayoutResult> playoutResults)
{
    for (var i = 0; i < audioAttachment.LastOutputResults.Count; i++)
    {
        var result = audioAttachment.LastOutputResults[i];
        var artifact = result.Ledger.OfType<OutputArtifactLedgerRecord>().LastOrDefault();
        var ttsResult = result.Ledger.OfType<TtsSynthesisResultLedgerRecord>().LastOrDefault();
        var assistantOutput = result.Ledger.OfType<AssistantOutputLedgerRecord>().LastOrDefault();
        var key = (result.OutputFlowId.Value, result.SegmentIndex ?? 0);
        playoutResults.TryGetValue(key, out var playout);
        var played = result.Commit?.Disposition is OutputCommitDisposition.PlayedComplete or OutputCommitDisposition.PlayedPartial ||
            playout?.Played == true;
        var heard = result.Commit?.Disposition is OutputCommitDisposition.PlayedComplete or OutputCommitDisposition.PlayedPartial ||
            playout?.HeardByUser == true;
        var playback = playout?.Disposition.ToString() ?? result.Commit?.Disposition.ToString() ?? "SynthesizedNotPlayed";
        var segmentPlayback = playout?.Disposition.ToString() ?? "-";
        var mediaType = artifact?.Artifact.MediaType ?? result.MediaType ?? ttsResult?.MediaType ?? "-";
        var bytes = artifact?.Artifact.SizeBytes ?? ttsResult?.SizeBytes;
        var sha256 = artifact?.Artifact.Sha256;
        var store = artifact?.Artifact.Store ?? "-";
        var artifactId = artifact?.Artifact.ArtifactId ?? "-";

        Console.WriteLine($"[assistant-output:{i}] synthesis={result.Status} playback={playback} segmentPlayback={segmentPlayback} played={played.ToString().ToLowerInvariant()} heard={heard.ToString().ToLowerInvariant()} commit={result.Commit?.Disposition.ToString() ?? assistantOutput?.Disposition.ToString() ?? "-"} provider={result.Ledger.OfType<TtsSynthesisRequestedLedgerRecord>().LastOrDefault()?.ProviderKey ?? "-"} model={result.Ledger.OfType<TtsSynthesisRequestedLedgerRecord>().LastOrDefault()?.ModelId ?? "-"} voice={result.Ledger.OfType<TtsSynthesisRequestedLedgerRecord>().LastOrDefault()?.VoiceId ?? "-"}");
        Console.WriteLine($"[assistant-audio] store={store} id={artifactId} mediaType={mediaType} bytes={bytes?.ToString() ?? "-"} sha256={sha256 ?? "-"} playback={playback} heard={heard.ToString().ToLowerInvariant()}");
        Console.WriteLine($"[assistant-audio:segment] index={result.SegmentIndex ?? 0} outputFlow={result.OutputFlowId.Value} response={result.ResponseId.Value} artifact={store}/{artifactId}");

        if (!string.IsNullOrWhiteSpace(exportDir) && artifact is not null)
        {
            var bytesToExport = await contentStore.ReadBytesAsync(new ContentAddress(
                ContentScope.Create(sessionId), artifact.Artifact.ArtifactId));
            if (bytesToExport is not null)
            {
                Directory.CreateDirectory(exportDir);
                var path = Path.Combine(exportDir, $"assistant-output-{artifact.Artifact.ArtifactId}{ExtensionFor(mediaType)}");
                await File.WriteAllBytesAsync(path, bytesToExport);
                Console.WriteLine($"[assistant-audio:export] path={path}");
            }
        }

        if (result.Error is not null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[assistant-audio:error] code={result.Error.Code} message={result.Error.Message}");
            Console.ResetColor();
        }
    }
}

static IReadOnlyDictionary<(string OutputFlowId, int SegmentIndex), AssistantAudioPlayoutResult> SummarizePlayout(
    IReadOnlyList<RealtimeLedgerRecord> ledger)
{
    var results = new Dictionary<(string OutputFlowId, int SegmentIndex), AssistantAudioPlayoutResult>();
    foreach (var record in ledger.OfType<OutputPlaybackLedgerRecord>())
    {
        var key = (record.OutputFlowId.Value, record.SegmentIndex);
        results[key] = new AssistantAudioPlayoutResult(
            record.Disposition is OutputPlaybackDisposition.PlayedComplete or OutputPlaybackDisposition.Progress,
            record.Disposition is OutputPlaybackDisposition.PlayedComplete or OutputPlaybackDisposition.Progress,
            record.Disposition,
            record.PlayedDuration,
            record.PlayedTextLength);
    }

    return results;
}

static async Task PrintThreadAsync(InMemorySessionStore sessionStore, string sessionId, string threadId)
{
    var thread = await sessionStore.ProjectThreadAsync(sessionId, threadId, ThreadProjectionPurpose.ThreadHistory);
    Console.WriteLine();
    if (thread is null)
    {
        Console.WriteLine("[thread] messages=0");
        return;
    }

    Console.WriteLine($"[thread] messages={thread.Messages.Count}");
    for (var i = 0; i < thread.Messages.Count; i++)
    {
        var message = thread.Messages[i];
        Console.WriteLine($"[{i:00}] {message.Role}: {Preview(ExtractText(message))}");
    }
}

static string ExtractText(ChatMessage message)
{
    var parts = message.Contents
        .OfType<TextContent>()
        .Select(content => content.Text)
        .Where(value => !string.IsNullOrWhiteSpace(value));
    return string.Join(" ", parts);
}

static string Preview(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return "<no text>";
    }

    return value.Length <= 500 ? value : value[..500] + "...";
}

static string ExtensionFor(string mediaType) =>
    mediaType.ToLowerInvariant() switch
    {
        "audio/mpeg" => ".mp3",
        "audio/wav" or "audio/x-wav" => ".wav",
        "audio/opus" => ".opus",
        "audio/aac" => ".aac",
        "audio/flac" => ".flac",
        "audio/pcm" => ".pcm",
        _ => ".bin"
    };

static async Task RunStreamingSttSmokeAsync(
    IProviderRegistry providerRegistry,
    ProviderClientConfig providerConfig,
    IReadOnlyList<string> audioPaths,
    int? sampleRate,
    string? language)
{
    if (audioPaths.Count == 0)
    {
        Console.Error.WriteLine("--stt-streaming-smoke requires at least one --audio path.");
        return;
    }

    var provider = providerRegistry.GetRequiredProvider<ISpeechToTextClientProvider>(
        providerConfig.ProviderKey ?? throw new InvalidOperationException("STT provider key is required."));
    using var client = provider.CreateSpeechToTextClient(providerConfig);

    Console.WriteLine($"[stt-streaming-smoke] provider={providerConfig.ProviderKey} model={providerConfig.ModelName} sampleRate={sampleRate ?? 16000}");

    for (var index = 0; index < audioPaths.Count; index++)
    {
        var audioPath = audioPaths[index];
        Console.WriteLine($"[stt-streaming-smoke] input={index + 1}/{audioPaths.Count} path={audioPath}");
        await using var stream = File.OpenRead(audioPath);
        await foreach (var update in client.GetStreamingTextAsync(
            stream,
            new SpeechToTextOptions
            {
                ModelId = providerConfig.ModelName,
                SpeechLanguage = language,
                SpeechSampleRate = sampleRate ?? 16000
            }))
        {
            Console.WriteLine($"[stt-streaming-smoke] {update.Kind}: {Preview(update.Text)}");
        }
    }
}

static JsonDocument? LoadAppSettings(string? explicitPath)
{
    var path = FirstNonWhiteSpace(explicitPath, FindUpward("appsettings.json"));
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
    {
        return null;
    }

    using var stream = File.OpenRead(path);
    return JsonDocument.Parse(stream);
}

static string? FindUpward(string fileName)
{
    var dir = Directory.GetCurrentDirectory();
    while (!string.IsNullOrWhiteSpace(dir))
    {
        var candidate = Path.Combine(dir, fileName);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        dir = Directory.GetParent(dir)?.FullName;
    }

    return null;
}

static string? GetConfigString(JsonDocument? document, params string[] path)
{
    if (!TryGetConfigElement(document, path, out var element))
    {
        return null;
    }

    return element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null
    };
}

static float? GetConfigSingle(JsonDocument? document, params string[] path)
{
    if (!TryGetConfigElement(document, path, out var element))
    {
        return null;
    }

    return element.ValueKind switch
    {
        JsonValueKind.Number when element.TryGetSingle(out var value) => value,
        JsonValueKind.String when float.TryParse(element.GetString(), out var value) => value,
        _ => null
    };
}

static bool? GetConfigBool(JsonDocument? document, params string[] path)
{
    if (!TryGetConfigElement(document, path, out var element))
    {
        return null;
    }

    return element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String when bool.TryParse(element.GetString(), out var value) => value,
        _ => null
    };
}

static bool TryGetConfigElement(JsonDocument? document, string[] path, out JsonElement element)
{
    element = default;
    if (document is null || path.Length == 0)
    {
        return false;
    }

    element = document.RootElement;
    foreach (var part in path)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(part, out element))
        {
            return false;
        }
    }

    return true;
}

static string? FirstNonWhiteSpace(params string?[] values)
{
    foreach (var value in values)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    return null;
}

static IReadOnlyList<string> SplitList(string? value) =>
    string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

static ProgressiveTextToSpeechRouteMode? TryParseRouteMode(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    return NormalizeMode(value) switch
    {
        "auto" => ProgressiveTextToSpeechRouteMode.Auto,
        "segment" or "forcesegment" => ProgressiveTextToSpeechRouteMode.ForceSegment,
        "pushtext" or "push" or "forcepushtext" => ProgressiveTextToSpeechRouteMode.ForcePushText,
        _ => null
    };
}

static PushTextInputAggregationMode? TryParsePushAggregationMode(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    return NormalizeMode(value) switch
    {
        "providerdefault" or "default" => PushTextInputAggregationMode.ProviderDefault,
        "rawdelta" or "raw" => PushTextInputAggregationMode.RawDelta,
        "sentence" => PushTextInputAggregationMode.Sentence,
        "token" => PushTextInputAggregationMode.Token,
        "manualflush" or "manual" => PushTextInputAggregationMode.ManualFlush,
        _ => null
    };
}

static AssistantAudioArtifactCapturePolicy? TryParseArtifactCapturePolicy(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    return NormalizeMode(value) switch
    {
        "contentstore" or "contentstoreartifact" or "artifact" => AssistantAudioArtifactCapturePolicy.ContentStoreArtifact,
        "disabled" or "none" or "off" => AssistantAudioArtifactCapturePolicy.Disabled,
        "metadataonly" or "metadata" => AssistantAudioArtifactCapturePolicy.MetadataOnly,
        "digestonly" or "digest" => AssistantAudioArtifactCapturePolicy.DigestOnly,
        _ => null
    };
}

static string NormalizeMode(string value) =>
    value.Replace("-", string.Empty, StringComparison.Ordinal)
        .Replace("_", string.Empty, StringComparison.Ordinal)
        .Trim()
        .ToLowerInvariant();

static void PrintUsage()
{
    Console.WriteLine("""
    HPD Agent Audio CLI

    Usage:
      dotnet run --project test/HPD-Agent.AudioCli -- (--audio <path> | --text <text> | --turn-text <text>) [options]

    Options:
      --audio <path>          Audio file to submit. Repeat for multi-turn audio.
      --text <text>           Text input. With audio, sends alongside audio; without audio, runs as the first text-only turn.
      --turn-text <text>      Text-only follow-up turn. Repeat for multi-turn text after audio.
      --openai-key <key>      OpenAI API key. Defaults to OPENAI_API_KEY.
      --openai-endpoint <url> Optional OpenAI-compatible endpoint.
      --appsettings <path>    Optional appsettings.json path. Defaults to nearest file upward.
      --stt-provider <key>    STT provider: openai or elevenlabs. Default: openai.
      --stt-model <model>     STT model. Defaults: whisper-1 for OpenAI, scribe_v1 for ElevenLabs.
      --chat-model <model>    Chat model. Default: appsettings AudioCli:ChatModel or gpt-5.5.
      --language <locale>     STT language/locale hint, e.g. en-US.
      --text-language <locale> Target text language for translation-capable STT clients.
      --stt-prompt <text>     STT prompt/context hint.
      --stt-temperature <n>   STT temperature.
      --stt-response-format <f> OpenAI STT format: text, json, verbose_json, srt, vtt.
      --stt-timestamps <list> OpenAI timestamp granularities: word,segment.
      --stt-logprobs          Include OpenAI STT logprobs when supported.
      --stt-streaming-smoke   Call ISpeechToTextClient.GetStreamingTextAsync directly and print updates.
      --stt-sample-rate <hz>  Speech sample rate for STT streaming smoke. Default: 16000.
      --realtime              Use realtime model transport for the model turn.
      --realtime-model <m>    Realtime model. Default: gpt-realtime.
      --realtime-voice <id>   Optional realtime output voice.
      --realtime-instructions <text> Optional realtime session/response instructions.
      --realtime-transcription Enable realtime user input transcription. Default with --realtime: enabled.
      --no-realtime-transcription Disable realtime user input transcription.
      --realtime-transcription-model <m> Realtime transcription model. Default: STT model.
      --realtime-transcription-language <locale> Realtime transcription language hint.
      --realtime-transcription-prompt <text> Realtime transcription prompt/context hint.
      --realtime-math-tools   Diagnostic: register Add/Multiply/Subtract and run realtime through started HPD runtime.
      --media-type <mime>     Override detected media type.
      --session <id>          Session id. Default: generated.
      --thread <id>           Thread id. Default: main.
      --tts                   Opt in to assistant TTS.
      --tts-progressive       Synthesize assistant output progressively from streamed text.
      --tts-route <mode>      Progressive TTS route: auto, segment, push-text. Default: auto.
      --tts-push-text         Enable ElevenLabs provider-native push-text WebSocket TTS.
      --tts-push-aggregation <mode> provider-default, raw-delta, sentence, token, manual-flush.
      --tts-artifact-capture <mode> content-store, disabled, metadata-only, digest-only.
      --tts-max-segment-chars <n> Maximum progressive segment size before soft flush. Default: 320.
      --no-tts                Force text-only output even when config has TTS defaults.
      --tts-provider <key>    TTS provider. Default with --tts: elevenlabs.
      --tts-model <model>     TTS model. Default: provider/config default.
      --tts-voice <voice>     TTS voice id.
      --tts-format <format>   TTS output format, e.g. mp3_44100_128, mp3, wav, pcm.
      --tts-language <locale> TTS language hint.
      --tts-speed <n>         TTS speed.
      --tts-output-dir <dir>  Optional export directory for synthesized assistant audio artifacts.
      --elevenlabs-ws-url <url> ElevenLabs WebSocket base URL.
      --elevenlabs-key <key>  ElevenLabs API key. Defaults to ELEVENLABS_API_KEY.
      --benchmark             Print one JSON benchmark record per turn.
      --benchmark-output <path> Append benchmark records as JSONL to a file.
      --playback-sink <name>  Diagnostic output sink. Supported: manual-clock.
      --events                Print every AgentEvent type observed by the CLI.
      --help                  Show help.
    """);
}

file sealed record AudioCliOptions(
    IReadOnlyList<string> AudioPaths,
    string? Text,
    IReadOnlyList<string> TurnTexts,
    string? OpenAIApiKey,
    string? OpenAIEndpoint,
    string? AppSettingsPath,
    string? SttProvider,
    string? SttModel,
    string? ChatModel,
    string? Language,
    string? TextLanguage,
    string? SttPrompt,
    float? SttTemperature,
    string? SttResponseFormat,
    IReadOnlyList<string> SttTimestampGranularities,
    bool? IncludeLogprobs,
    bool SttStreamingSmoke,
    int? SttSampleRate,
    bool RealtimeEnabled,
    string? RealtimeModel,
    string? RealtimeVoice,
    string? RealtimeInstructions,
    bool? RealtimeTranscriptionEnabled,
    string? RealtimeTranscriptionModel,
    string? RealtimeTranscriptionLanguage,
    string? RealtimeTranscriptionPrompt,
    bool RealtimeMathTools,
    string? MediaType,
    string? SessionId,
    string? ThreadId,
    bool? TtsEnabled,
    bool TtsProgressive,
    ProgressiveTextToSpeechRouteMode? TtsRouteMode,
    bool TtsPushTextEnabled,
    PushTextInputAggregationMode? TtsPushAggregationMode,
    AssistantAudioArtifactCapturePolicy? TtsArtifactCapturePolicy,
    int? TtsMaxSegmentChars,
    bool TtsDisabled,
    bool TtsProviderExplicit,
    string? TtsProvider,
    string? TtsModel,
    string? TtsVoice,
    string? TtsFormat,
    string? TtsLanguage,
    float? TtsSpeed,
    string? TtsOutputDirectory,
    string? ElevenLabsWebSocketBaseUrl,
    string? ElevenLabsApiKey,
    bool Benchmark,
    string? BenchmarkOutputPath,
    string? PlaybackSink,
    bool PrintEvents,
    bool ShowHelp)
{
    public static AudioCliOptions Parse(string[] args)
    {
        var audioPaths = new List<string>();
        var turnTexts = new List<string>();
        string? text = null;
        string? openAIApiKey = null;
        string? openAIEndpoint = null;
        string? appSettingsPath = null;
        string? sttProvider = null;
        string? sttModel = null;
        string? chatModel = null;
        string? language = null;
        string? textLanguage = null;
        string? sttPrompt = null;
        float? sttTemperature = null;
        string? sttResponseFormat = null;
        var sttTimestampGranularities = new List<string>();
        bool? includeLogprobs = null;
        var sttStreamingSmoke = false;
        int? sttSampleRate = null;
        var realtimeEnabled = false;
        string? realtimeModel = null;
        string? realtimeVoice = null;
        string? realtimeInstructions = null;
        bool? realtimeTranscriptionEnabled = null;
        string? realtimeTranscriptionModel = null;
        string? realtimeTranscriptionLanguage = null;
        string? realtimeTranscriptionPrompt = null;
        var realtimeMathTools = false;
        string? mediaType = null;
        string? sessionId = null;
        string? threadId = null;
        bool? ttsEnabled = null;
        var ttsProgressive = false;
        ProgressiveTextToSpeechRouteMode? ttsRouteMode = null;
        var ttsPushTextEnabled = false;
        PushTextInputAggregationMode? ttsPushAggregationMode = null;
        AssistantAudioArtifactCapturePolicy? ttsArtifactCapturePolicy = null;
        int? ttsMaxSegmentChars = null;
        var ttsDisabled = false;
        var ttsProviderExplicit = false;
        string? ttsProvider = null;
        string? ttsModel = null;
        string? ttsVoice = null;
        string? ttsFormat = null;
        string? ttsLanguage = null;
        float? ttsSpeed = null;
        string? ttsOutputDirectory = null;
        string? elevenLabsWebSocketBaseUrl = null;
        string? elevenLabsApiKey = null;
        var benchmark = false;
        string? benchmarkOutputPath = null;
        string? playbackSink = null;
        var printEvents = false;
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                case "--audio":
                case "-a":
                    audioPaths.Add(RequireValue(args, ref i, arg));
                    break;
                case "--text":
                case "-t":
                    text = RequireValue(args, ref i, arg);
                    break;
                case "--turn-text":
                    turnTexts.Add(RequireValue(args, ref i, arg));
                    break;
                case "--openai-key":
                    openAIApiKey = RequireValue(args, ref i, arg);
                    break;
                case "--openai-endpoint":
                    openAIEndpoint = RequireValue(args, ref i, arg);
                    break;
                case "--appsettings":
                    appSettingsPath = RequireValue(args, ref i, arg);
                    break;
                case "--stt-provider":
                    sttProvider = RequireValue(args, ref i, arg);
                    break;
                case "--stt-model":
                    sttModel = RequireValue(args, ref i, arg);
                    break;
                case "--chat-model":
                    chatModel = RequireValue(args, ref i, arg);
                    break;
                case "--language":
                    language = RequireValue(args, ref i, arg);
                    break;
                case "--text-language":
                    textLanguage = RequireValue(args, ref i, arg);
                    break;
                case "--stt-prompt":
                    sttPrompt = RequireValue(args, ref i, arg);
                    break;
                case "--stt-temperature":
                    sttTemperature = float.Parse(RequireValue(args, ref i, arg));
                    break;
                case "--stt-response-format":
                    sttResponseFormat = RequireValue(args, ref i, arg);
                    break;
                case "--stt-timestamps":
                    sttTimestampGranularities.AddRange(SplitList(RequireValue(args, ref i, arg)));
                    break;
                case "--stt-logprobs":
                    includeLogprobs = true;
                    break;
                case "--stt-streaming-smoke":
                    sttStreamingSmoke = true;
                    break;
                case "--stt-sample-rate":
                    sttSampleRate = int.Parse(RequireValue(args, ref i, arg));
                    break;
                case "--realtime":
                    realtimeEnabled = true;
                    break;
                case "--realtime-model":
                    realtimeEnabled = true;
                    realtimeModel = RequireValue(args, ref i, arg);
                    break;
                case "--realtime-voice":
                    realtimeEnabled = true;
                    realtimeVoice = RequireValue(args, ref i, arg);
                    break;
                case "--realtime-instructions":
                    realtimeEnabled = true;
                    realtimeInstructions = RequireValue(args, ref i, arg);
                    break;
                case "--realtime-transcription":
                    realtimeEnabled = true;
                    realtimeTranscriptionEnabled = true;
                    break;
                case "--no-realtime-transcription":
                    realtimeTranscriptionEnabled = false;
                    break;
                case "--realtime-transcription-model":
                    realtimeEnabled = true;
                    realtimeTranscriptionEnabled = true;
                    realtimeTranscriptionModel = RequireValue(args, ref i, arg);
                    break;
                case "--realtime-transcription-language":
                    realtimeEnabled = true;
                    realtimeTranscriptionEnabled = true;
                    realtimeTranscriptionLanguage = RequireValue(args, ref i, arg);
                    break;
                case "--realtime-transcription-prompt":
                    realtimeEnabled = true;
                    realtimeTranscriptionEnabled = true;
                    realtimeTranscriptionPrompt = RequireValue(args, ref i, arg);
                    break;
                case "--realtime-math-tools":
                    realtimeEnabled = true;
                    realtimeMathTools = true;
                    break;
                case "--media-type":
                    mediaType = RequireValue(args, ref i, arg);
                    break;
                case "--session":
                    sessionId = RequireValue(args, ref i, arg);
                    break;
                case "--thread":
                    threadId = RequireValue(args, ref i, arg);
                    break;
                case "--tts":
                    ttsEnabled = true;
                    break;
                case "--tts-progressive":
                    ttsEnabled = true;
                    ttsProgressive = true;
                    break;
                case "--tts-route":
                    ttsEnabled = true;
                    ttsProgressive = true;
                    ttsRouteMode = TryParseRouteMode(RequireValue(args, ref i, arg)) ??
                        throw new ArgumentException($"Unknown {arg} value.");
                    break;
                case "--tts-push-text":
                    ttsEnabled = true;
                    ttsProgressive = true;
                    ttsPushTextEnabled = true;
                    break;
                case "--tts-push-aggregation":
                    ttsPushAggregationMode = TryParsePushAggregationMode(RequireValue(args, ref i, arg)) ??
                        throw new ArgumentException($"Unknown {arg} value.");
                    break;
                case "--tts-artifact-capture":
                    ttsArtifactCapturePolicy = TryParseArtifactCapturePolicy(RequireValue(args, ref i, arg)) ??
                        throw new ArgumentException($"Unknown {arg} value.");
                    break;
                case "--tts-max-segment-chars":
                    ttsMaxSegmentChars = int.Parse(RequireValue(args, ref i, arg));
                    break;
                case "--no-tts":
                    ttsDisabled = true;
                    ttsEnabled = false;
                    break;
                case "--tts-provider":
                    ttsProvider = RequireValue(args, ref i, arg);
                    ttsProviderExplicit = true;
                    break;
                case "--tts-model":
                    ttsModel = RequireValue(args, ref i, arg);
                    break;
                case "--tts-voice":
                    ttsVoice = RequireValue(args, ref i, arg);
                    break;
                case "--tts-format":
                    ttsFormat = RequireValue(args, ref i, arg);
                    break;
                case "--tts-language":
                    ttsLanguage = RequireValue(args, ref i, arg);
                    break;
                case "--tts-speed":
                    ttsSpeed = float.Parse(RequireValue(args, ref i, arg));
                    break;
                case "--tts-output-dir":
                    ttsOutputDirectory = RequireValue(args, ref i, arg);
                    break;
                case "--elevenlabs-ws-url":
                    elevenLabsWebSocketBaseUrl = RequireValue(args, ref i, arg);
                    break;
                case "--elevenlabs-key":
                    elevenLabsApiKey = RequireValue(args, ref i, arg);
                    break;
                case "--benchmark":
                    benchmark = true;
                    break;
                case "--benchmark-output":
                    benchmarkOutputPath = RequireValue(args, ref i, arg);
                    benchmark = true;
                    break;
                case "--playback-sink":
                    playbackSink = RequireValue(args, ref i, arg);
                    ttsEnabled = true;
                    break;
                case "--events":
                    printEvents = true;
                    break;
                default:
                    if (!arg.StartsWith("-", StringComparison.Ordinal))
                    {
                        audioPaths.Add(arg);
                    }
                    else
                    {
                        throw new ArgumentException($"Unknown argument: {arg}");
                    }

                    break;
            }
        }

        return new AudioCliOptions(
            audioPaths,
            text,
            turnTexts,
            openAIApiKey,
            openAIEndpoint,
            appSettingsPath,
            sttProvider,
            sttModel,
            chatModel,
            language,
            textLanguage,
            sttPrompt,
            sttTemperature,
            sttResponseFormat,
            sttTimestampGranularities,
            includeLogprobs,
            sttStreamingSmoke,
            sttSampleRate,
            realtimeEnabled,
            realtimeModel,
            realtimeVoice,
            realtimeInstructions,
            realtimeTranscriptionEnabled,
            realtimeTranscriptionModel,
            realtimeTranscriptionLanguage,
            realtimeTranscriptionPrompt,
            realtimeMathTools,
            mediaType,
            sessionId,
            threadId,
            ttsEnabled,
            ttsProgressive,
            ttsRouteMode,
            ttsPushTextEnabled,
            ttsPushAggregationMode,
            ttsArtifactCapturePolicy,
            ttsMaxSegmentChars,
            ttsDisabled,
            ttsProviderExplicit,
            ttsProvider,
            ttsModel,
            ttsVoice,
            ttsFormat,
            ttsLanguage,
            ttsSpeed,
            ttsOutputDirectory,
            elevenLabsWebSocketBaseUrl,
            elevenLabsApiKey,
            benchmark,
            benchmarkOutputPath,
            playbackSink,
            printEvents,
            showHelp);
    }

    private static string RequireValue(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{name} requires a value.");
        }

        return args[++index];
    }

    private static IReadOnlyList<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static ProgressiveTextToSpeechRouteMode? TryParseRouteMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return NormalizeMode(value) switch
        {
            "auto" => ProgressiveTextToSpeechRouteMode.Auto,
            "segment" or "forcesegment" => ProgressiveTextToSpeechRouteMode.ForceSegment,
            "pushtext" or "push" or "forcepushtext" => ProgressiveTextToSpeechRouteMode.ForcePushText,
            _ => null
        };
    }

    private static PushTextInputAggregationMode? TryParsePushAggregationMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return NormalizeMode(value) switch
        {
            "providerdefault" or "default" => PushTextInputAggregationMode.ProviderDefault,
            "rawdelta" or "raw" => PushTextInputAggregationMode.RawDelta,
            "sentence" => PushTextInputAggregationMode.Sentence,
            "token" => PushTextInputAggregationMode.Token,
            "manualflush" or "manual" => PushTextInputAggregationMode.ManualFlush,
            _ => null
        };
    }

    private static AssistantAudioArtifactCapturePolicy? TryParseArtifactCapturePolicy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return NormalizeMode(value) switch
        {
            "contentstore" or "contentstoreartifact" or "artifact" => AssistantAudioArtifactCapturePolicy.ContentStoreArtifact,
            "disabled" or "none" or "off" => AssistantAudioArtifactCapturePolicy.Disabled,
            "metadataonly" or "metadata" => AssistantAudioArtifactCapturePolicy.MetadataOnly,
            "digestonly" or "digest" => AssistantAudioArtifactCapturePolicy.DigestOnly,
            _ => null
        };
    }

    private static string NormalizeMode(string value) =>
        value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
}

file sealed class AutoAdvancingManualClockAudioOutputSink : IAudioOutputSink
{
    private readonly ManualClockAudioOutputSink _sink = new(
        defaultDuration: TimeSpan.FromSeconds(1),
        acceptEncodedAudio: true);

    public ValueTask<OutputSinkStartResult> StartAsync(OutputAudioStream stream, CancellationToken cancellationToken = default) =>
        _sink.StartAsync(stream, cancellationToken);

    public ValueTask WriteAsync(OutputAudioChunk chunk, CancellationToken cancellationToken = default) =>
        _sink.WriteAsync(chunk, cancellationToken);

    public async ValueTask CompleteAsync(OutputAudioStreamCompletion completion, CancellationToken cancellationToken = default)
    {
        await _sink.CompleteAsync(completion, cancellationToken).ConfigureAwait(false);
        var duration = completion.Duration ?? TimeSpan.FromSeconds(1);
        if (duration <= TimeSpan.Zero)
        {
            duration = TimeSpan.FromSeconds(1);
        }

        await _sink.AdvanceAsync(completion.OutputFlowId, duration, cancellationToken).ConfigureAwait(false);
    }

    public IAsyncEnumerable<OutputPlaybackEvent> ReadPlaybackEventsAsync(OutputFlowId outputFlowId, CancellationToken cancellationToken = default) =>
        _sink.ReadPlaybackEventsAsync(outputFlowId, cancellationToken);

    public ValueTask<OutputPlaybackBoundary> InterruptAsync(OutputFlowId outputFlowId, CancellationToken cancellationToken = default) =>
        _sink.InterruptAsync(outputFlowId, cancellationToken);

    public ValueTask FlushAsync(OutputFlowId outputFlowId, CancellationToken cancellationToken = default) =>
        _sink.FlushAsync(outputFlowId, cancellationToken);
}

file sealed class BenchmarkingAudioOutputSink(
    IAudioOutputSink inner,
    AudioCliBenchmarkCollector benchmark) : IAudioOutputSink
{
    public async ValueTask<OutputSinkStartResult> StartAsync(OutputAudioStream stream, CancellationToken cancellationToken = default)
    {
        benchmark.RecordSinkStart(stream);
        var result = await inner.StartAsync(stream, cancellationToken).ConfigureAwait(false);
        benchmark.RecordSinkStartResult(result);
        return result;
    }

    public async ValueTask WriteAsync(OutputAudioChunk chunk, CancellationToken cancellationToken = default)
    {
        benchmark.RecordSinkWrite(chunk);
        await inner.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask CompleteAsync(OutputAudioStreamCompletion completion, CancellationToken cancellationToken = default)
    {
        benchmark.RecordSinkComplete(completion);
        await inner.CompleteAsync(completion, cancellationToken).ConfigureAwait(false);
    }

    public IAsyncEnumerable<OutputPlaybackEvent> ReadPlaybackEventsAsync(OutputFlowId outputFlowId, CancellationToken cancellationToken = default) =>
        inner.ReadPlaybackEventsAsync(outputFlowId, cancellationToken);

    public ValueTask<OutputPlaybackBoundary> InterruptAsync(OutputFlowId outputFlowId, CancellationToken cancellationToken = default) =>
        inner.InterruptAsync(outputFlowId, cancellationToken);

    public ValueTask FlushAsync(OutputFlowId outputFlowId, CancellationToken cancellationToken = default) =>
        inner.FlushAsync(outputFlowId, cancellationToken);
}

file sealed record AssistantAudioPlayoutResult(
    bool Played,
    bool HeardByUser,
    OutputPlaybackDisposition Disposition,
    TimeSpan PlayedDuration,
    int PlayedTextLength);

file sealed class AudioCliBenchmarkCollector(bool enabled)
{
    private readonly object _gate = new();
    private BenchmarkTurnState? _turn;

    public bool Enabled { get; } = enabled;

    public void BeginTurn(
        int turnIndex,
        int turnCount,
        string audioName,
        string? mediaType,
        bool ttsRequested,
        bool ttsProgressive,
        ProgressiveTextToSpeechRouteMode configuredRoute,
        bool pushTextRequested,
        PushTextInputAggregationMode pushAggregationMode,
        AssistantAudioArtifactCapturePolicy artifactCapturePolicy,
        string? provider,
        string? model,
        string? voice,
        string? outputFormat,
        int maxSegmentChars)
    {
        if (!Enabled)
        {
            return;
        }

        lock (_gate)
        {
            _turn = new BenchmarkTurnState
            {
                TurnIndex = turnIndex,
                TurnCount = turnCount,
                AudioName = audioName,
                MediaType = mediaType,
                TtsRequested = ttsRequested,
                TtsProgressive = ttsProgressive,
                ConfiguredRoute = configuredRoute.ToString(),
                PushTextRequested = pushTextRequested,
                PushAggregationMode = pushAggregationMode.ToString(),
                ArtifactCapturePolicy = artifactCapturePolicy.ToString(),
                Provider = provider,
                Model = model,
                Voice = voice,
                OutputFormat = outputFormat,
                MaxSegmentChars = maxSegmentChars,
                RunStartedAt = DateTimeOffset.UtcNow
            };
        }
    }

    public ValueTask RecordOutputStreamStarted(AssistantAudioOutputStreamStartedEvent evt)
    {
        if (!Enabled) return ValueTask.CompletedTask;
        lock (_gate)
        {
            if (_turn is { } turn)
            {
                turn.FirstOutputStartedAt ??= DateTimeOffset.UtcNow;
                turn.Provider ??= evt.ProviderKey;
                turn.Model ??= evt.ModelId;
                turn.Voice ??= evt.VoiceId;
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RecordOutputChunkReady(AssistantAudioOutputChunkReadyEvent evt)
    {
        if (!Enabled) return ValueTask.CompletedTask;
        lock (_gate)
        {
            if (_turn is { } turn)
            {
                var now = DateTimeOffset.UtcNow;
                turn.FirstOutputChunkAt ??= now;
                turn.LastOutputChunkAt = now;
                turn.OutputChunkCount++;
                turn.StreamedAudioBytes += evt.SizeBytes;
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RecordPushTextStreamOpening(AssistantAudioPushTextStreamOpeningEvent evt)
    {
        if (!Enabled) return ValueTask.CompletedTask;
        lock (_gate)
        {
            if (_turn is { } turn)
            {
                var now = DateTimeOffset.UtcNow;
                turn.FirstPushTextStreamOpeningAt ??= now;
                turn.PushTextAggregationMode = evt.InputAggregationMode;
                turn.Provider ??= evt.ProviderKey;
                turn.Model ??= evt.ModelId;
                turn.Voice ??= evt.VoiceId;
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RecordPushTextStreamOpened(AssistantAudioPushTextStreamOpenedEvent evt)
    {
        if (!Enabled) return ValueTask.CompletedTask;
        lock (_gate)
        {
            if (_turn is { } turn)
            {
                var now = DateTimeOffset.UtcNow;
                turn.FirstPushTextStreamOpenedAt ??= now;
                turn.PushTextAggregationMode = evt.InputAggregationMode;
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RecordPushTextInputSent(AssistantAudioPushTextInputSentEvent evt)
    {
        if (!Enabled) return ValueTask.CompletedTask;
        lock (_gate)
        {
            if (_turn is { } turn)
            {
                var now = DateTimeOffset.UtcNow;
                if (!evt.IsFinalInput)
                {
                    turn.FirstPushTextInputSentAt ??= now;
                    turn.LastPushTextInputSentAt = now;
                    turn.PushTextInputSentCount++;
                    turn.PushTextInputChars += evt.SourceTextLength;
                }
                else
                {
                    turn.FinalPushTextInputSentAt ??= now;
                }

                turn.PushTextAggregationMode = evt.InputAggregationMode;
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RecordArtifactCaptured(AssistantAudioOutputArtifactCapturedEvent evt)
    {
        if (!Enabled) return ValueTask.CompletedTask;
        lock (_gate)
        {
            if (_turn is { } turn)
            {
                var now = DateTimeOffset.UtcNow;
                turn.FirstArtifactCapturedAt ??= now;
                turn.LastArtifactCapturedAt = now;
                turn.TotalArtifactBytes += evt.SizeBytes ?? 0;
                turn.Segments.Add(new BenchmarkSegmentState
                {
                    Index = evt.SegmentSequence,
                    SegmentId = evt.SegmentId,
                    OutputFlowId = evt.OutputFlowId,
                    ResponseId = evt.ResponseId,
                    ArtifactId = evt.Artifact.ArtifactId,
                    MediaType = evt.MediaType,
                    ArtifactBytes = evt.SizeBytes,
                    ArtifactCapturedAt = now
                });
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RecordOutputCompleted(AssistantAudioOutputCompletedEvent evt)
    {
        if (!Enabled) return ValueTask.CompletedTask;
        lock (_gate)
        {
            if (_turn is { } turn)
            {
                turn.OutputCompletedAt = DateTimeOffset.UtcNow;
                turn.OutputDisposition = evt.Disposition;
                turn.OutputCompletedSegmentCount = evt.SegmentCount;
            }
        }

        return ValueTask.CompletedTask;
    }

    public void RecordTextDelta(TextDeltaEvent evt)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            if (_turn is { } turn)
            {
                var now = DateTimeOffset.UtcNow;
                turn.FirstTextDeltaAt ??= now;
                turn.LastTextDeltaAt = now;
                turn.TextDeltaCount++;
                turn.AssistantTextChars += evt.Text.Length;
            }
        }
    }

    public void RecordTurnStarted(MessageTurnStartedEvent evt)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            if (_turn is { } turn)
            {
                turn.TurnStartedAt = DateTimeOffset.UtcNow;
                turn.MessageTurnId = evt.MessageTurnId;
                turn.AgentName = evt.AgentName;
            }
        }
    }

    public void RecordTurnFinished(MessageTurnFinishedEvent evt)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            if (_turn is { } turn)
            {
                turn.TurnFinishedAt = DateTimeOffset.UtcNow;
                turn.MessageTurnDurationMs = evt.Duration.TotalMilliseconds;
            }
        }
    }

    public void RecordSinkStart(OutputAudioStream stream)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            if (_turn is { } turn)
            {
                turn.FirstSinkStartAt ??= DateTimeOffset.UtcNow;
                turn.LastSinkStartAt = DateTimeOffset.UtcNow;
                turn.SinkStartCount++;
            }
        }
    }

    public void RecordSinkStartResult(OutputSinkStartResult result)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            if (_turn is { } turn)
            {
                if (result.Disposition == OutputSinkStartDisposition.Accepted)
                {
                    turn.SinkQueuedCount++;
                }
                else
                {
                    turn.SinkRejectedCount++;
                    turn.SinkError ??= result.Error?.Code;
                }
            }
        }
    }

    public void RecordSinkWrite(OutputAudioChunk chunk)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            if (_turn is { } turn)
            {
                turn.FirstSinkWriteAt ??= DateTimeOffset.UtcNow;
                turn.LastSinkWriteAt = DateTimeOffset.UtcNow;
                turn.SinkWriteCount++;
                turn.SinkWrittenBytes += chunk.SizeBytes;
            }
        }
    }

    public void RecordSinkComplete(OutputAudioStreamCompletion completion)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            if (_turn is { } turn)
            {
                turn.FirstSinkCompleteAt ??= DateTimeOffset.UtcNow;
                turn.LastSinkCompleteAt = DateTimeOffset.UtcNow;
                turn.SinkCompleteCount++;
            }
        }
    }

    public void RecordError(string? error)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            if (_turn is { } turn)
            {
                turn.Error ??= error;
            }
        }
    }

    public void CompleteRun()
    {
        if (!Enabled) return;
        lock (_gate)
        {
            if (_turn is { } turn)
            {
                turn.RunCompletedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    public async Task WriteAsync(
        AudioRuntimeAttachment attachment,
        string? outputPath)
    {
        if (!Enabled)
        {
            return;
        }

        BenchmarkTurnState? state;
        lock (_gate)
        {
            state = _turn?.Clone();
        }

        if (state is null)
        {
            return;
        }

        var record = CreateRecord(state, attachment);
        var json = JsonSerializer.Serialize(record);
        Console.WriteLine($"[benchmark] {json}");
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.AppendAllTextAsync(outputPath, json + System.Environment.NewLine);
        }
    }

    private static Dictionary<string, object?> CreateRecord(
        BenchmarkTurnState state,
        AudioRuntimeAttachment attachment)
    {
        var aggregateTtsRequests = attachment.LastOutputLedger
            .OfType<TtsSynthesisRequestedLedgerRecord>()
            .ToArray();
        var resultTtsRequests = attachment.LastOutputResults
            .SelectMany(result => result.Ledger)
            .OfType<TtsSynthesisRequestedLedgerRecord>()
            .ToArray();
        var ttsRequests = aggregateTtsRequests.Length > 0
            ? aggregateTtsRequests
            : resultTtsRequests;
        var sourceTextChars = ttsRequests.Sum(record => record.SourceTextLength > 0
            ? record.SourceTextLength
            : record.Text.Length);
        var firstTtsRequestedAt = ttsRequests
            .Select(record => (DateTimeOffset?)record.RecordedAt)
            .FirstOrDefault();
        var providerFirstAudioAt = attachment.LastOutputResults
            .SelectMany(result => result.Trace)
            .OfType<AudioTtsSynthesisTraceRecord>()
            .Select(trace => trace.ProviderFirstAudioAt)
            .FirstOrDefault(value => value is not null);
        var firstTtsInputAt = state.FirstPushTextInputSentAt ?? firstTtsRequestedAt ?? state.FirstOutputStartedAt;
        var firstTtsInputSource = state.FirstPushTextInputSentAt is not null
            ? "PushTextInputSent"
            : firstTtsRequestedAt is not null
                ? "SynthesisLedgerRequest"
                : state.FirstOutputStartedAt is not null
                    ? "OutputStarted"
                    : null;

        return new Dictionary<string, object?>
        {
            ["turn_index"] = state.TurnIndex,
            ["turn_count"] = state.TurnCount,
            ["audio_name"] = state.AudioName,
            ["media_type"] = state.MediaType,
            ["message_turn_id"] = state.MessageTurnId,
            ["agent"] = state.AgentName,
            ["configured_route"] = state.ConfiguredRoute,
            ["push_text_requested"] = state.PushTextRequested,
            ["push_aggregation"] = state.PushAggregationMode,
            ["push_text_effective_aggregation"] = state.PushTextAggregationMode,
            ["artifact_capture"] = state.ArtifactCapturePolicy,
            ["tts_requested"] = state.TtsRequested,
            ["tts_progressive"] = state.TtsProgressive,
            ["provider"] = state.Provider,
            ["model"] = state.Model,
            ["voice"] = state.Voice,
            ["format"] = state.OutputFormat,
            ["max_segment_chars"] = state.MaxSegmentChars,
            ["transcript_chars"] = attachment.LastResults
                .Select(result => result.TurnDecision?.Commit?.Text)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text))?.Length,
            ["assistant_text_chars"] = state.AssistantTextChars,
            ["tts_source_text_chars"] = sourceTextChars,
            ["text_delta_count"] = state.TextDeltaCount,
            ["push_text_input_sent_count"] = state.PushTextInputSentCount,
            ["push_text_input_chars"] = state.PushTextInputChars,
            ["segment_count"] = state.Segments.Count,
            ["chunk_count"] = state.OutputChunkCount,
            ["output_result_count"] = attachment.LastOutputResults.Count,
            ["streamed_audio_bytes"] = state.StreamedAudioBytes,
            ["sink_start_count"] = state.SinkStartCount,
            ["sink_queued_count"] = state.SinkQueuedCount,
            ["sink_rejected_count"] = state.SinkRejectedCount,
            ["sink_failed_count"] = state.SinkFailedCount,
            ["sink_write_count"] = state.SinkWriteCount,
            ["sink_complete_count"] = state.SinkCompleteCount,
            ["sink_written_bytes"] = state.SinkWrittenBytes,
            ["artifact_bytes"] = state.TotalArtifactBytes > 0 ? state.TotalArtifactBytes : null,
            ["message_turn_duration_ms"] = state.MessageTurnDurationMs,
            ["run_duration_ms"] = ElapsedRunMs(state, state.RunCompletedAt),
            ["turn_start_ms"] = ElapsedRunMs(state, state.TurnStartedAt),
            ["llm_ttft_ms"] = ElapsedMs(state.TurnStartedAt, state.FirstTextDeltaAt),
            ["first_text_delta_ms"] = ElapsedRunMs(state, state.FirstTextDeltaAt),
            ["last_text_delta_ms"] = ElapsedRunMs(state, state.LastTextDeltaAt),
            ["first_push_stream_opening_ms"] = ElapsedRunMs(state, state.FirstPushTextStreamOpeningAt),
            ["first_push_stream_opened_ms"] = ElapsedRunMs(state, state.FirstPushTextStreamOpenedAt),
            ["push_stream_open_latency_ms"] = ElapsedMs(state.FirstPushTextStreamOpeningAt, state.FirstPushTextStreamOpenedAt),
            ["first_push_text_sent_ms"] = ElapsedRunMs(state, state.FirstPushTextInputSentAt),
            ["last_push_text_sent_ms"] = ElapsedRunMs(state, state.LastPushTextInputSentAt),
            ["final_push_text_sent_ms"] = ElapsedRunMs(state, state.FinalPushTextInputSentAt),
            ["text_to_first_push_text_sent_ms"] = ElapsedMs(state.FirstTextDeltaAt, state.FirstPushTextInputSentAt),
            ["push_text_sent_to_first_output_chunk_ms"] = ElapsedMs(state.FirstPushTextInputSentAt, state.FirstOutputChunkAt),
            ["output_started_ms"] = ElapsedRunMs(state, state.FirstOutputStartedAt),
            ["text_to_output_start_ms"] = ElapsedMs(state.FirstTextDeltaAt, state.FirstOutputStartedAt),
            ["first_tts_input_ms"] = ElapsedRunMs(state, firstTtsInputAt),
            ["first_tts_input_source"] = firstTtsInputSource,
            ["first_synthesis_ledger_request_ms"] = ElapsedRunMs(state, firstTtsRequestedAt),
            ["aggregation_ms"] = ElapsedMs(state.FirstTextDeltaAt, state.FirstOutputStartedAt),
            ["first_output_chunk_ms"] = ElapsedRunMs(state, state.FirstOutputChunkAt),
            ["provider_first_audio_ms"] = ElapsedMs(firstTtsInputAt, providerFirstAudioAt),
            ["provider_first_audio_turn_ms"] = ElapsedMs(state.TurnStartedAt, providerFirstAudioAt),
            ["first_sink_start_ms"] = ElapsedRunMs(state, state.FirstSinkStartAt),
            ["first_sink_write_ms"] = ElapsedRunMs(state, state.FirstSinkWriteAt),
            ["first_output_chunk_to_sink_write_ms"] = ElapsedMs(state.FirstOutputChunkAt, state.FirstSinkWriteAt),
            ["first_sink_complete_ms"] = ElapsedRunMs(state, state.FirstSinkCompleteAt),
            ["first_artifact_captured_ms"] = ElapsedRunMs(state, state.FirstArtifactCapturedAt),
            ["artifact_capture_total_ms"] = ElapsedMs(state.FirstOutputStartedAt, state.FirstArtifactCapturedAt),
            ["artifact_capture_lag_ms"] = ElapsedMs(state.FirstOutputChunkAt, state.FirstArtifactCapturedAt),
            ["turn_to_first_output_chunk_ms"] = ElapsedMs(state.TurnStartedAt, state.FirstOutputChunkAt),
            ["turn_to_first_sink_write_ms"] = ElapsedMs(state.TurnStartedAt, state.FirstSinkWriteAt),
            ["output_completed_ms"] = ElapsedRunMs(state, state.OutputCompletedAt),
            ["output_disposition"] = state.OutputDisposition,
            ["output_completed_segment_count"] = state.OutputCompletedSegmentCount,
            ["sink_error"] = state.SinkError,
            ["error"] = state.Error,
            ["segments"] = state.Segments.Select(segment => new Dictionary<string, object?>
            {
                ["index"] = segment.Index,
                ["segment_id"] = segment.SegmentId,
                ["output_flow"] = segment.OutputFlowId,
                ["response"] = segment.ResponseId,
                ["artifact_id"] = segment.ArtifactId,
                ["media_type"] = segment.MediaType,
                ["artifact_bytes"] = segment.ArtifactBytes,
                ["artifact_captured_ms"] = ElapsedRunMs(state, segment.ArtifactCapturedAt)
            }).ToArray()
        };
    }

    private static double? ElapsedRunMs(BenchmarkTurnState state, DateTimeOffset? instant) =>
        ElapsedMs(state.RunStartedAt, instant);

    private static double? ElapsedMs(DateTimeOffset? start, DateTimeOffset? end) =>
        start is null || end is null
            ? null
            : Math.Round((end.Value - start.Value).TotalMilliseconds, 3);
}

file sealed class BenchmarkTurnState
{
    public int TurnIndex { get; init; }
    public int TurnCount { get; init; }
    public string? AudioName { get; init; }
    public string? MediaType { get; init; }
    public bool TtsRequested { get; init; }
    public bool TtsProgressive { get; init; }
    public string? ConfiguredRoute { get; init; }
    public bool PushTextRequested { get; init; }
    public string? PushAggregationMode { get; init; }
    public string? PushTextAggregationMode { get; set; }
    public string? ArtifactCapturePolicy { get; init; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? Voice { get; set; }
    public string? OutputFormat { get; set; }
    public int MaxSegmentChars { get; init; }
    public DateTimeOffset RunStartedAt { get; init; }
    public DateTimeOffset? RunCompletedAt { get; set; }
    public DateTimeOffset? TurnStartedAt { get; set; }
    public DateTimeOffset? TurnFinishedAt { get; set; }
    public DateTimeOffset? FirstTextDeltaAt { get; set; }
    public DateTimeOffset? LastTextDeltaAt { get; set; }
    public DateTimeOffset? FirstPushTextStreamOpeningAt { get; set; }
    public DateTimeOffset? FirstPushTextStreamOpenedAt { get; set; }
    public DateTimeOffset? FirstPushTextInputSentAt { get; set; }
    public DateTimeOffset? LastPushTextInputSentAt { get; set; }
    public DateTimeOffset? FinalPushTextInputSentAt { get; set; }
    public DateTimeOffset? FirstOutputStartedAt { get; set; }
    public DateTimeOffset? FirstOutputChunkAt { get; set; }
    public DateTimeOffset? LastOutputChunkAt { get; set; }
    public DateTimeOffset? FirstSinkStartAt { get; set; }
    public DateTimeOffset? LastSinkStartAt { get; set; }
    public DateTimeOffset? FirstSinkWriteAt { get; set; }
    public DateTimeOffset? LastSinkWriteAt { get; set; }
    public DateTimeOffset? FirstSinkCompleteAt { get; set; }
    public DateTimeOffset? LastSinkCompleteAt { get; set; }
    public DateTimeOffset? FirstArtifactCapturedAt { get; set; }
    public DateTimeOffset? LastArtifactCapturedAt { get; set; }
    public DateTimeOffset? OutputCompletedAt { get; set; }
    public string? MessageTurnId { get; set; }
    public string? AgentName { get; set; }
    public string? OutputDisposition { get; set; }
    public string? Error { get; set; }
    public string? SinkError { get; set; }
    public double? MessageTurnDurationMs { get; set; }
    public int TextDeltaCount { get; set; }
    public int AssistantTextChars { get; set; }
    public int PushTextInputSentCount { get; set; }
    public int PushTextInputChars { get; set; }
    public int OutputChunkCount { get; set; }
    public int? OutputCompletedSegmentCount { get; set; }
    public long StreamedAudioBytes { get; set; }
    public long TotalArtifactBytes { get; set; }
    public int SinkStartCount { get; set; }
    public int SinkQueuedCount { get; set; }
    public int SinkRejectedCount { get; set; }
    public int SinkFailedCount { get; set; }
    public int SinkWriteCount { get; set; }
    public int SinkCompleteCount { get; set; }
    public long SinkWrittenBytes { get; set; }
    public List<BenchmarkSegmentState> Segments { get; } = [];

    public BenchmarkTurnState Clone()
    {
        var clone = new BenchmarkTurnState
        {
            TurnIndex = TurnIndex,
            TurnCount = TurnCount,
            AudioName = AudioName,
            MediaType = MediaType,
            TtsRequested = TtsRequested,
            TtsProgressive = TtsProgressive,
            ConfiguredRoute = ConfiguredRoute,
            PushTextRequested = PushTextRequested,
            PushAggregationMode = PushAggregationMode,
            PushTextAggregationMode = PushTextAggregationMode,
            ArtifactCapturePolicy = ArtifactCapturePolicy,
            Provider = Provider,
            Model = Model,
            Voice = Voice,
            OutputFormat = OutputFormat,
            MaxSegmentChars = MaxSegmentChars,
            RunStartedAt = RunStartedAt,
            RunCompletedAt = RunCompletedAt,
            TurnStartedAt = TurnStartedAt,
            TurnFinishedAt = TurnFinishedAt,
            FirstTextDeltaAt = FirstTextDeltaAt,
            LastTextDeltaAt = LastTextDeltaAt,
            FirstPushTextStreamOpeningAt = FirstPushTextStreamOpeningAt,
            FirstPushTextStreamOpenedAt = FirstPushTextStreamOpenedAt,
            FirstPushTextInputSentAt = FirstPushTextInputSentAt,
            LastPushTextInputSentAt = LastPushTextInputSentAt,
            FinalPushTextInputSentAt = FinalPushTextInputSentAt,
            FirstOutputStartedAt = FirstOutputStartedAt,
            FirstOutputChunkAt = FirstOutputChunkAt,
            LastOutputChunkAt = LastOutputChunkAt,
            FirstSinkStartAt = FirstSinkStartAt,
            LastSinkStartAt = LastSinkStartAt,
            FirstSinkWriteAt = FirstSinkWriteAt,
            LastSinkWriteAt = LastSinkWriteAt,
            FirstSinkCompleteAt = FirstSinkCompleteAt,
            LastSinkCompleteAt = LastSinkCompleteAt,
            FirstArtifactCapturedAt = FirstArtifactCapturedAt,
            LastArtifactCapturedAt = LastArtifactCapturedAt,
            OutputCompletedAt = OutputCompletedAt,
            MessageTurnId = MessageTurnId,
            AgentName = AgentName,
            OutputDisposition = OutputDisposition,
            Error = Error,
            SinkError = SinkError,
            MessageTurnDurationMs = MessageTurnDurationMs,
            TextDeltaCount = TextDeltaCount,
            AssistantTextChars = AssistantTextChars,
            PushTextInputSentCount = PushTextInputSentCount,
            PushTextInputChars = PushTextInputChars,
            OutputChunkCount = OutputChunkCount,
            OutputCompletedSegmentCount = OutputCompletedSegmentCount,
            StreamedAudioBytes = StreamedAudioBytes,
            TotalArtifactBytes = TotalArtifactBytes,
            SinkStartCount = SinkStartCount,
            SinkQueuedCount = SinkQueuedCount,
            SinkRejectedCount = SinkRejectedCount,
            SinkFailedCount = SinkFailedCount,
            SinkWriteCount = SinkWriteCount,
            SinkCompleteCount = SinkCompleteCount,
            SinkWrittenBytes = SinkWrittenBytes
        };
        clone.Segments.AddRange(Segments.Select(segment => segment with { }));
        return clone;
    }
}

file sealed record BenchmarkSegmentState
{
    public int Index { get; init; }
    public string? SegmentId { get; init; }
    public string? OutputFlowId { get; init; }
    public string? ResponseId { get; init; }
    public string? ArtifactId { get; init; }
    public string? MediaType { get; init; }
    public long? ArtifactBytes { get; init; }
    public DateTimeOffset? ArtifactCapturedAt { get; init; }
}
