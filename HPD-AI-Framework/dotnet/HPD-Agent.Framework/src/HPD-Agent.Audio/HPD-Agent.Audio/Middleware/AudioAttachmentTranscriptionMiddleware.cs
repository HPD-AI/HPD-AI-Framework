// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using HPD.Agent.Audio.Recognition;
using HPD.Agent.Audio.Stt;
using HPD.Agent.Middleware;
using HPD.Agent.Providers;
using HPD.Events;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio;

/// <summary>
/// Transcribes audio attachments in semantic chat turns.
/// </summary>
/// <remarks>
/// This middleware is intentionally separate from <see cref="AudioPipelineMiddleware"/>.
/// Live microphone input must enter the runtime as <see cref="AudioInputFrame"/>.
/// Chat content such as <see cref="AudioContent"/>, <see cref="DataContent"/> with
/// an audio media type, or stored <see cref="UriContent"/> assets are attachment
/// inputs and are handled here.
/// </remarks>
public sealed class AudioAttachmentTranscriptionMiddleware : IAgentMiddleware
{
    private AudioConfig _config = new();

    /// <summary>
    /// Creates a middleware instance with default audio configuration.
    /// </summary>
    public AudioAttachmentTranscriptionMiddleware()
    {
        ProviderRegistry = new AgentBuilder().ProviderRegistry;
    }

    /// <summary>
    /// Creates a middleware instance with the supplied audio configuration.
    /// </summary>
    public AudioAttachmentTranscriptionMiddleware(AudioConfig config)
    {
        ProviderRegistry = new AgentBuilder().ProviderRegistry;
        Configure(config);
    }

    /// <summary>
    /// Replaces middleware-level default audio configuration.
    /// </summary>
    public void Configure(AudioConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        _config = config.Clone();
    }

    /// <summary>HPD speech recognizer used for attachment transcription.</summary>
    public ISpeechRecognizer? SpeechRecognizer { get; set; }

    /// <summary>Unified provider registry used to resolve configured STT clients.</summary>
    public IProviderRegistry? ProviderRegistry { get; set; }

    /// <summary>What input/output modalities to use. Default: AudioToAudioAndText.</summary>
    public AudioIOMode IOMode
    {
        get => _config.IOMode;
        set => _config.IOMode = value;
    }

    /// <inheritdoc />
    public async Task BeforeIterationAsync(BeforeIterationContext context, CancellationToken cancellationToken)
    {
        var effectiveConfig = GetEffectiveConfig(context.RunConfig?.Audio);
        ApplyClientFamilyConfigs(effectiveConfig, context.Config, context.RunConfig);
        effectiveConfig.Validate();
        ApplyGlobalLanguage(effectiveConfig);

        if (effectiveConfig.Disabled == true || !HasAudioInputMode(effectiveConfig.IOMode))
        {
            return;
        }

        EnsureSpeechRecognizer(effectiveConfig, context.Services, context.ClientSet);

        if (SpeechRecognizer == null || context.Messages == null || context.Messages.Count == 0)
            return;

        var lastMessage = context.Messages[^1];
        if (lastMessage.Role != ChatRole.User)
            return;

        var audioItems = await CollectAudioAttachmentsAsync(
                context,
                lastMessage,
                cancellationToken)
            .ConfigureAwait(false);

        if (audioItems.Count == 0)
            return;

        var transcriptionId = Guid.NewGuid().ToString("N")[..8];
        var sttStartTime = DateTime.UtcNow;

        context.TryEmit(new TranscriptionDeltaEvent(transcriptionId, "", false, null)
        {
            Channel = EventChannel.Streaming
        });

        var transcriptions = new List<string>();
        foreach (var (_, resolved) in audioItems)
        {
            if (resolved == null)
                continue;

            try
            {
                var transcription = await TranscribeAudioAsync(
                        resolved,
                        effectiveConfig.Stt,
                        context.Session?.Id,
                        context.BranchId,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(transcription))
                {
                    transcriptions.Add(transcription);
                    context.TryEmit(new TranscriptionDeltaEvent(transcriptionId, transcription, false, null)
                    {
                        Channel = EventChannel.Streaming
                    });
                }
            }
            catch
            {
                context.TryEmit(new AudioPipelineMetricsEvent("error", "attachment_stt_error", 1, "count")
                {
                    Channel = EventChannel.Streaming
                });
            }
        }

        if (transcriptions.Count == 0)
            return;

        var sttDuration = DateTime.UtcNow - sttStartTime;
        var fullTranscription = string.Join(" ", transcriptions);

        context.TryEmit(new TranscriptionCompletedEvent(transcriptionId, fullTranscription, sttDuration)
        {
            Channel = EventChannel.Synchronous
        });

        var audioOriginals = audioItems.Select(a => a.Original).ToHashSet(ReferenceEqualityComparer.Instance);
        var newContents = lastMessage.Contents!
            .Where(c => !audioOriginals.Contains(c))
            .ToList();

        newContents.Insert(0, new TextContent(fullTranscription));

        context.Messages[^1] = new ChatMessage(lastMessage.Role, newContents)
        {
            AuthorName = lastMessage.AuthorName,
            RawRepresentation = lastMessage.RawRepresentation
        };
    }

    private static async Task<List<(AIContent Original, DataContent? Resolved)>> CollectAudioAttachmentsAsync(
        BeforeIterationContext context,
        ChatMessage lastMessage,
        CancellationToken cancellationToken)
    {
        var audioItems = new List<(AIContent Original, DataContent? Resolved)>();
        var contentStore = context.ContentStore;

        foreach (var content in lastMessage.Contents ?? [])
        {
            if (content is AudioContent ac)
            {
                audioItems.Add((content, ac));
            }
            else if (content is DataContent dc &&
                     dc.MediaType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true)
            {
                audioItems.Add((content, dc));
            }
            else if (content is UriContent uc &&
                     uc.MediaType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true &&
                     ContentReferenceResolverMiddleware.IsContentReference(uc) &&
                     contentStore != null)
            {
                var contentId = uc.Uri.Host;
                var stored = await contentStore.StatAsync(context.Session!.Id, contentId, cancellationToken)
                    .ConfigureAwait(false);
                if (stored != null)
                {
                    var data = await contentStore.ReadBytesAsync(context.Session!.Id, contentId, cancellationToken)
                        .ConfigureAwait(false);
                    if (data != null)
                        audioItems.Add((content, new DataContent(data, stored.ContentType)));
                }
            }
        }

        return audioItems;
    }

    private async Task<string?> TranscribeAudioAsync(
        DataContent audioContent,
        SttConfig? sttConfig,
        string? sessionId,
        string? branchId,
        CancellationToken cancellationToken)
    {
        await foreach (var recognitionEvent in RecognizeAudioAsync(
                audioContent,
                sttConfig,
                sessionId,
                branchId,
                cancellationToken)
            .ConfigureAwait(false))
        {
            if (recognitionEvent is SpeechRecognitionFinalEvent final &&
                !string.IsNullOrWhiteSpace(final.Transcript.Text))
            {
                return final.Transcript.Text;
            }
        }

        return null;
    }

    private async IAsyncEnumerable<SpeechRecognitionEvent> RecognizeAudioAsync(
        DataContent audioContent,
        SttConfig? sttConfig,
        string? sessionId,
        string? branchId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (SpeechRecognizer == null)
            yield break;

        var audioData = audioContent.Data;
        if (audioData.IsEmpty)
            yield break;

        var options = new SpeechRecognitionOptions
        {
            Provider = sttConfig?.Provider,
            Model = sttConfig?.ModelId,
            Language = sttConfig?.Language,
            SessionId = sessionId,
            BranchId = branchId,
            AudioMimeType = audioContent.MediaType,
            SampleRate = sttConfig?.SpeechSampleRate
        };

        await foreach (var recognitionEvent in SpeechRecognizer
            .RecognizeAsync(
                EnumerateSingleAudioFrame(audioData, audioContent.MediaType, sessionId, branchId, cancellationToken),
                options,
                cancellationToken)
            .ConfigureAwait(false))
        {
            yield return recognitionEvent;
        }
    }

    private static async IAsyncEnumerable<AudioInputFrame> EnumerateSingleAudioFrame(
        ReadOnlyMemory<byte> audioData,
        string? mediaType,
        string? sessionId,
        string? branchId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new AudioInputFrame(
            SessionId: sessionId,
            BranchId: branchId,
            Audio: audioData,
            MimeType: string.IsNullOrWhiteSpace(mediaType) ? "audio/pcm" : mediaType,
            TimestampNs: 0,
            IsFinal: true);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private AudioConfig GetEffectiveConfig(object? audioOptions) =>
        audioOptions switch
        {
            AudioRunConfig runOptions => GetEffectiveConfig(runOptions),
            AudioConfig config => _config.MergeWith(config),
            _ => _config.Clone()
        };

    private AudioConfig GetEffectiveConfig(AudioRunConfig runOptions)
    {
        runOptions.Validate();

        var effective = _config.Clone();
        if (runOptions.IOMode.HasValue)
            effective.IOMode = runOptions.IOMode.Value;

        effective.Language = runOptions.Language ?? effective.Language;
        effective.Disabled = runOptions.Disabled ?? effective.Disabled;
        effective.Stt = MergeStt(effective.Stt, runOptions.Stt);
        return effective;
    }

    private static SttConfig? MergeStt(SttConfig? defaults, SttConfig? overrides)
    {
        if (overrides == null)
            return defaults;

        defaults = AudioConfig.CloneStt(defaults) ?? new SttConfig();
        defaults.Language = overrides.Language ?? defaults.Language;
        defaults.SpeechSampleRate = overrides.SpeechSampleRate ?? defaults.SpeechSampleRate;
        defaults.TextLanguage = overrides.TextLanguage ?? defaults.TextLanguage;
        defaults.ModelId = overrides.ModelId ?? defaults.ModelId;
        defaults.Temperature = overrides.Temperature ?? defaults.Temperature;
        defaults.ResponseFormat = overrides.ResponseFormat ?? defaults.ResponseFormat;
        defaults.Provider = string.IsNullOrWhiteSpace(overrides.Provider) ? defaults.Provider : overrides.Provider;
        defaults.ProviderOptionsJson = overrides.ProviderOptionsJson ?? defaults.ProviderOptionsJson;
        defaults.OverrideClient = overrides.OverrideClient ?? defaults.OverrideClient;
        defaults.AdditionalProperties = MergeAdditionalProperties(defaults.AdditionalProperties, overrides.AdditionalProperties);
        return defaults;
    }

    private static Dictionary<string, object>? MergeAdditionalProperties(
        Dictionary<string, object>? defaults,
        Dictionary<string, object>? overrides)
    {
        if (defaults == null && overrides == null)
            return null;

        var merged = defaults == null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object>(defaults);

        if (overrides != null)
        {
            foreach (var entry in overrides)
                merged[entry.Key] = entry.Value;
        }

        return merged;
    }

    private static bool HasAudioInputMode(AudioIOMode mode) =>
        mode is AudioIOMode.AudioToText
            or AudioIOMode.AudioToAudio
            or AudioIOMode.AudioToAudioAndText;

    private static void ApplyGlobalLanguage(AudioConfig effectiveConfig)
    {
        if (!string.IsNullOrWhiteSpace(effectiveConfig.Language) && effectiveConfig.Stt != null)
            effectiveConfig.Stt.Language ??= effectiveConfig.Language;
    }

    private static void ApplyClientFamilyConfigs(
        AudioConfig effectiveConfig,
        AgentConfig? agentConfig,
        AgentRunConfig? runConfig)
    {
        if (agentConfig == null)
            return;

        ApplySttClientConfig(
            effectiveConfig,
            agentConfig.ResolveClientConfig(ProviderClientFamily.SpeechToText),
            overwrite: false);

        if (runConfig?.Clients?.GetFamilyConfig(ProviderClientFamily.SpeechToText) != null)
        {
            ApplySttClientConfig(
                effectiveConfig,
                agentConfig.ResolveClientConfig(ProviderClientFamily.SpeechToText, runConfig.Clients),
                overwrite: true);
        }
    }

    private static void ApplySttClientConfig(AudioConfig config, ClientProviderConfig? clientConfig, bool overwrite)
    {
        if (clientConfig == null)
            return;

        config.Stt ??= new SttConfig();
        if (overwrite || string.IsNullOrWhiteSpace(config.Stt.Provider))
            config.Stt.Provider = clientConfig.ProviderKey ?? config.Stt.Provider;
        if (overwrite || string.IsNullOrWhiteSpace(config.Stt.ModelId))
            config.Stt.ModelId = clientConfig.ModelName ?? config.Stt.ModelId;
        if (overwrite || string.IsNullOrWhiteSpace(config.Stt.ProviderOptionsJson))
            config.Stt.ProviderOptionsJson = clientConfig.ProviderOptionsJson ?? config.Stt.ProviderOptionsJson;
        config.Stt.AdditionalProperties = MergeAdditionalProperties(config.Stt.AdditionalProperties, clientConfig.AdditionalProperties);
    }

    private void EnsureSpeechRecognizer(
        AudioConfig effectiveConfig,
        IServiceProvider? services,
        AgentClientSet? clientSet)
    {
        var configuredSttClient = effectiveConfig.Stt?.OverrideClient ?? clientSet?.SpeechToText;
        if (SpeechRecognizer != null || (configuredSttClient == null && effectiveConfig.Stt == null))
            return;

        var sttClient = configuredSttClient;
        var disposeClient = false;
        if (sttClient == null)
        {
            if (effectiveConfig.Stt == null || string.IsNullOrWhiteSpace(effectiveConfig.Stt.Provider))
                throw new InvalidOperationException("Audio attachment transcription requires either an explicit speech-to-text client override or a configured provider.");

            if (ProviderRegistry == null)
                throw new InvalidOperationException($"Audio provider '{effectiveConfig.Stt.Provider}' requires a unified provider registry.");

            var provider = ProviderRegistry.GetRequiredProvider<ISpeechToTextClientProvider>(effectiveConfig.Stt.Provider);
            sttClient = provider.CreateSpeechToTextClient(ToClientProviderConfig(effectiveConfig.Stt), services);
            disposeClient = true;
        }

        SpeechRecognizer = MeaiSpeechRecognizerFactory.Create(
            sttClient,
            new SpeechRecognitionCapabilities
            {
                StreamingInput = false,
                InterimResults = false,
                PreflightResults = false,
                FinalResults = true,
                OfflineRecognize = true
            },
            effectiveConfig.Stt?.UseStreamingRecognition == true,
            effectiveConfig.Stt?.Provider,
            effectiveConfig.Stt?.ModelId,
            disposeClient);
    }

    private static ClientProviderConfig ToClientProviderConfig(SttConfig config) => new()
    {
        ProviderKey = config.Provider,
        ModelName = config.ModelId ?? string.Empty,
        ProviderOptionsJson = config.ProviderOptionsJson,
        AdditionalProperties = config.AdditionalProperties
    };
}
