// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.Providers;
using HPD.Agent.StructuredOutput;

namespace HPD.Agent;

/// <summary>Captures independent per-invocation configuration before asynchronous execution begins.</summary>
internal static class AgentRunConfigSnapshot
{
    internal static AgentRunConfig? Capture(AgentRunConfig? source, ProviderComposition? composition)
    {
        if (source is null)
            return null;

        var snapshot = AgentRunConfigInheritance.CreateSnapshot(source, SubAgentRunConfigFields.All);
        SnapshotProviderPayloads(snapshot.Clients, composition);
        snapshot.Audio = CloneAudio(source.Audio);
        snapshot.Compaction = CloneCompaction(source.Compaction, composition);
        snapshot.StructuredOutput = CloneStructuredOutput(source.StructuredOutput);
        snapshot.Evaluations = SnapshotEvaluations(source.Evaluations);
        return snapshot;
    }

    private static void SnapshotProviderPayloads(AgentClientsConfig clients, ProviderComposition? composition)
    {
        foreach (var family in Enum.GetValues<ProviderClientFamily>())
        {
            var config = clients.GetFamilyConfig(family);
            if (config is null)
                continue;

            config.ProviderConfig = SnapshotPayload(
                composition,
                config.ProviderKey,
                family,
                ProviderPayloadKind.Configuration,
                config.ProviderConfig,
                $"Clients.{family}.ProviderConfig") as IProviderConfig;

            switch (config)
            {
                case ChatClientConfig chat:
                    chat.ProviderOptions = SnapshotPayload(composition, chat.ProviderKey, family,
                        ProviderPayloadKind.OperationOptions, chat.ProviderOptions,
                        $"Clients.{family}.ProviderOptions") as IChatRequestOptions;
                    break;
                case RealtimeClientConfig realtime:
                    realtime.ProviderOptions = SnapshotPayload(composition, realtime.ProviderKey, family,
                        ProviderPayloadKind.OperationOptions, realtime.ProviderOptions,
                        $"Clients.{family}.ProviderOptions") as IRealtimeSessionProviderOptions;
                    break;
                case ImageGenerationClientConfig image:
                    image.ProviderOptions = SnapshotPayload(composition, image.ProviderKey, family,
                        ProviderPayloadKind.OperationOptions, image.ProviderOptions,
                        $"Clients.{family}.ProviderOptions") as IImageGenerationProviderOptions;
                    break;
                case EmbeddingsClientConfig embeddings:
                    embeddings.ProviderOptions = SnapshotPayload(composition, embeddings.ProviderKey, family,
                        ProviderPayloadKind.OperationOptions, embeddings.ProviderOptions,
                        $"Clients.{family}.ProviderOptions") as IEmbeddingGenerationProviderOptions;
                    break;
                case TextToSpeechClientConfig tts:
                    tts.ProviderOptions = SnapshotPayload(composition, tts.ProviderKey, family,
                        ProviderPayloadKind.OperationOptions, tts.ProviderOptions,
                        $"Clients.{family}.ProviderOptions") as ITextToSpeechProviderOptions;
                    break;
                case SpeechToTextClientConfig stt:
                    stt.ProviderOptions = SnapshotPayload(composition, stt.ProviderKey, family,
                        ProviderPayloadKind.OperationOptions, stt.ProviderOptions,
                        $"Clients.{family}.ProviderOptions") as ISpeechToTextProviderOptions;
                    break;
                case HostedFilesClientConfig files:
                    files.ProviderOptions = SnapshotPayload(composition, files.ProviderKey, family,
                        ProviderPayloadKind.OperationOptions, files.ProviderOptions,
                        $"Clients.{family}.ProviderOptions") as IHostedFileProviderOptions;
                    break;
            }
        }
    }

    private static object? SnapshotPayload(
        ProviderComposition? composition,
        string? providerKey,
        ProviderClientFamily family,
        ProviderPayloadKind kind,
        object? payload,
        string path)
    {
        if (payload is null)
            return null;
        if (composition is null)
            throw new AgentRunConfigurationException(
                "ProviderCompositionNotInstalled",
                path,
                $"A generated provider composition is required to capture '{path}'.",
                providerKey);

        composition.ValidatePayload(providerKey, family, kind, payload, path);
        var canonical = composition.Descriptors.Canonicalize(providerKey!);
        composition.Serialization.TryGet(canonical, family, kind, out var contract);
        return contract!.Snapshot(payload);
    }

    private static IAgentRunEvaluationConfig? SnapshotEvaluations(IAgentRunEvaluationConfig? evaluations)
    {
        if (evaluations is null)
            return null;
        try
        {
            return evaluations.Snapshot() ?? throw new InvalidOperationException("Snapshot returned null.");
        }
        catch (Exception exception) when (exception is not AgentRunConfigurationException)
        {
            throw new AgentRunConfigurationException(
                "EvaluationSnapshotFailed",
                nameof(AgentRunConfig.Evaluations),
                $"The evaluation configuration could not be captured: {exception.Message}");
        }
    }

    private static AudioRunConfig? CloneAudio(AudioRunConfig? source) => source is null ? null : new AudioRunConfig
    {
        Enabled = source.Enabled,
        InputMode = source.InputMode,
        OutputMode = source.OutputMode,
        AssistantOutputMode = source.AssistantOutputMode,
        Pacing = source.Pacing is null ? null : source.Pacing with
        {
            First = source.Pacing.First with { },
            Continuation = source.Pacing.Continuation with { },
            Boundaries = source.Pacing.Boundaries with { },
            Filtering = source.Pacing.Filtering with { }
        },
        ProgressiveRouteMode = source.ProgressiveRouteMode,
        PushTextAggregationMode = source.PushTextAggregationMode,
        ArtifactCapturePolicy = source.ArtifactCapturePolicy,
        VoiceId = source.VoiceId,
        Language = source.Language,
        OutputFormat = source.OutputFormat,
        ContentType = source.ContentType,
        Speed = source.Speed,
        EnablePlayback = source.EnablePlayback
    };

    private static CompactionRunPolicy? CloneCompaction(
        CompactionRunPolicy? source,
        ProviderComposition? composition)
    {
        if (source is null)
            return null;
        return source with
        {
            Automatic = source.Automatic is null ? null : source.Automatic with
            {
                Compaction = CloneSpecification(source.Automatic.Compaction, composition)
            }
        };
    }

    private static CompactionSpecification CloneSpecification(
        CompactionSpecification source,
        ProviderComposition? composition) => source with
    {
        Strategy = source.Strategy switch
        {
            SummarizingCompaction summarizing => summarizing with
            {
                Summarizer = CloneSummarizer(summarizing.Summarizer, composition)
            },
            RemovalCompaction removal => removal with { },
            _ => throw new AgentRunConfigurationException(
                "UnsupportedCompactionStrategy",
                "Compaction",
                $"Compaction strategy '{source.Strategy.GetType().Name}' cannot be captured.")
        }
    };

    private static ChatClientConfig? CloneSummarizer(ChatClientConfig? source, ProviderComposition? composition)
    {
        if (source is null)
            return null;
        var clients = new AgentClientsConfig { Chat = (ChatClientConfig)ProviderClientConfigResolver.Clone(source) };
        SnapshotProviderPayloads(clients, composition);
        return clients.Chat;
    }

    private static StructuredOutputOptions? CloneStructuredOutput(StructuredOutputOptions? source) =>
        source is null ? null : new StructuredOutputOptions
        {
            Mode = source.Mode,
            Schema = source.Schema?.Clone(),
            SchemaName = source.SchemaName,
            SchemaDescription = source.SchemaDescription,
            ToolName = source.ToolName,
            StreamPartials = source.StreamPartials,
            PartialDebounceMs = source.PartialDebounceMs,
            SerializerOptions = source.SerializerOptions,
            UnionTypes = source.UnionTypes?.ToArray()
        };
}
