// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.Providers;
using HPD.Agent.StructuredOutput;

namespace HPD.Agent;

/// <summary>Captures independent per-invocation configuration before asynchronous execution begins.</summary>
internal static class AgentRunConfigSnapshot
{
    internal static IReadOnlySet<string> CapturedPropertyNames { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(AgentRunConfig.Security),
        nameof(AgentRunConfig.Clients),
        nameof(AgentRunConfig.SystemInstructions),
        nameof(AgentRunConfig.Tools),
        nameof(AgentRunConfig.Context),
        nameof(AgentRunConfig.BackgroundResponses),
        nameof(AgentRunConfig.Streaming),
        nameof(AgentRunConfig.RuntimeMiddleware),
        nameof(AgentRunConfig.UploadStrategy),
        nameof(AgentRunConfig.Audio),
        nameof(AgentRunConfig.Compaction),
        nameof(AgentRunConfig.Goals),
        nameof(AgentRunConfig.Collapsing),
        nameof(AgentRunConfig.StructuredOutput),
        nameof(AgentRunConfig.RuntimeTools),
        nameof(AgentRunConfig.RuntimeToolMode),
        nameof(AgentRunConfig.Evaluations)
    };

    internal static AgentRunConfig? Capture(AgentRunConfig? source, ProviderComposition? composition)
    {
        if (source is null)
            return null;

        var snapshot = CloneCore(source, composition);
        snapshot.Clients = CloneClients(source.Clients);
        SnapshotProviderPayloads(snapshot.Clients, composition);
        snapshot.Evaluations = SnapshotEvaluations(source.Evaluations);
        return snapshot;
    }

    internal static SubAgentRunConfig? Capture(SubAgentRunConfig? source, ProviderComposition? composition)
    {
        if (source is null) return null;
        var core = Capture((AgentRunConfig)source, composition)!;
        var result = Promote(core, source.ClientPropagation);
        result.DescendantDefaults = source.DescendantDefaults?.Snapshot();
        result.HandoffCompaction = source.HandoffCompaction is null ? null : CloneSpecification(source.HandoffCompaction, composition);
        return result;
    }

    internal static SubAgentRunConfig Promote(
        AgentRunConfig core,
        SubAgentClientPropagation? propagation = null)
    {
        ArgumentNullException.ThrowIfNull(core);
        return new SubAgentRunConfig
        {
            Security = core.Security,
            Clients = core.Clients,
            SystemInstructions = core.SystemInstructions,
            Tools = core.Tools,
            Context = core.Context,
            BackgroundResponses = core.BackgroundResponses,
            Streaming = core.Streaming,
            RuntimeMiddleware = core.RuntimeMiddleware,
            UploadStrategy = core.UploadStrategy,
            Audio = core.Audio,
            Compaction = core.Compaction,
            Goals = core.Goals,
            Collapsing = core.Collapsing,
            StructuredOutput = core.StructuredOutput,
            Evaluations = core.Evaluations,
            ClientPropagation = propagation ?? SubAgentClientPropagation.DirectChildren
        };
    }

    private static AgentRunConfig CloneCore(AgentRunConfig source, ProviderComposition? composition) => new()
    {
        Security = source.Security with
        {
            PermissionOverrides = source.Security.PermissionOverrides?.Select(static value => value with
            {
                Selector = value.Selector with { }
            }).ToArray(),
            Sandbox = source.Security.Sandbox with
            {
                Capabilities = source.Security.Sandbox.Capabilities with
                {
                    Filesystem = source.Security.Sandbox.Capabilities.Filesystem
                        .Select(static grant => grant with { }).ToArray()
                }
            }
        },
        SystemInstructions = source.SystemInstructions is null ? null : new SystemInstructionsRunConfig
        {
            Override = source.SystemInstructions.Override,
            Append = source.SystemInstructions.Append
        },
        Tools = source.Tools is null ? null : new AgentToolsRunConfig
        {
            ClientInput = source.Tools.ClientInput,
            ClientAppProviders = source.Tools.ClientAppProviders?.ToArray(),
            Additional = source.Tools.Additional?.ToArray(),
            Mode = source.Tools.Mode
        },
        Context = source.Context is null ? null : new AgentContextRunConfig
        {
            Properties = source.Context.Properties is null ? null : new Dictionary<string, object>(source.Context.Properties),
            ToolInstances = source.Context.ToolInstances is null ? null : new Dictionary<string, IToolMetadata>(source.Context.ToolInstances)
        },
        BackgroundResponses = source.BackgroundResponses is null ? null : new BackgroundResponsesRunConfig
        {
            Allow = source.BackgroundResponses.Allow,
            ContinuationToken = source.BackgroundResponses.ContinuationToken,
            PollingInterval = source.BackgroundResponses.PollingInterval,
            Timeout = source.BackgroundResponses.Timeout
        },
        Streaming = source.Streaming is null ? null : new StreamingRunConfig
        {
            CoalesceDeltas = source.Streaming.CoalesceDeltas,
            Callback = source.Streaming.Callback
        },
        RuntimeMiddleware = source.RuntimeMiddleware?.ToArray(),
        UploadStrategy = source.UploadStrategy,
        Audio = CloneAudio(source.Audio),
        Compaction = CloneCompaction(source.Compaction, composition),
        Goals = source.Goals?.Snapshot(),
        Collapsing = CloneCollapsing(source.Collapsing),
        StructuredOutput = CloneStructuredOutput(source.StructuredOutput),
        RuntimeTools = source.RuntimeTools is null ? null : new(source.RuntimeTools),
        RuntimeToolMode = source.RuntimeToolMode
    };

    private static AgentClientsConfig CloneClients(AgentClientsConfig source) => new()
    {
        Transport = source.Transport,
        Chat = CloneClient<ChatClientConfig>(source.Chat),
        Realtime = CloneClient<RealtimeClientConfig>(source.Realtime),
        ImageGeneration = CloneClient<ImageGenerationClientConfig>(source.ImageGeneration),
        Embeddings = CloneClient<EmbeddingsClientConfig>(source.Embeddings),
        TextToSpeech = CloneClient<TextToSpeechClientConfig>(source.TextToSpeech),
        SpeechToText = CloneClient<SpeechToTextClientConfig>(source.SpeechToText),
        HostedFiles = CloneClient<HostedFilesClientConfig>(source.HostedFiles),
        VoiceActivity = CloneClient<VoiceActivityClientConfig>(source.VoiceActivity),
        EndOfTurn = CloneClient<EndOfTurnClientConfig>(source.EndOfTurn)
    };

    private static TConfig? CloneClient<TConfig>(TConfig? source)
        where TConfig : ProviderClientConfig
        => source is null ? null : (TConfig)ProviderClientConfigSnapshot.Clone(source);

    private static void SnapshotProviderPayloads(AgentClientsConfig clients, ProviderComposition? composition)
    {
        foreach (var family in Enum.GetValues<ProviderClientFamily>())
        {
            var config = clients.GetFamilyConfig(family);
            if (config is null)
                continue;

            config.ProviderConfig = SnapshotPayload(
                composition,
                config.Provider?.Key,
                family,
                ProviderPayloadKind.Configuration,
                config.ProviderConfig,
                $"Clients.{family}.ProviderConfig") as IProviderConfig;

            switch (config)
            {
                case ChatClientConfig chat:
                    chat.ProviderOptions = SnapshotPayload(composition, chat.Provider?.Key, family,
                        ProviderPayloadKind.OperationOptions, chat.ProviderOptions,
                        $"Clients.{family}.ProviderOptions") as IChatRequestOptions;
                    break;
                case RealtimeClientConfig realtime:
                    realtime.ProviderOptions = SnapshotPayload(composition, realtime.Provider?.Key, family,
                        ProviderPayloadKind.OperationOptions, realtime.ProviderOptions,
                        $"Clients.{family}.ProviderOptions") as IRealtimeSessionProviderOptions;
                    break;
                case ImageGenerationClientConfig image:
                    image.ProviderOptions = SnapshotPayload(composition, image.Provider?.Key, family,
                        ProviderPayloadKind.OperationOptions, image.ProviderOptions,
                        $"Clients.{family}.ProviderOptions") as IImageGenerationProviderOptions;
                    break;
                case EmbeddingsClientConfig embeddings:
                    embeddings.ProviderOptions = SnapshotPayload(composition, embeddings.Provider?.Key, family,
                        ProviderPayloadKind.OperationOptions, embeddings.ProviderOptions,
                        $"Clients.{family}.ProviderOptions") as IEmbeddingGenerationProviderOptions;
                    break;
                case TextToSpeechClientConfig tts:
                    tts.ProviderOptions = SnapshotPayload(composition, tts.Provider?.Key, family,
                        ProviderPayloadKind.OperationOptions, tts.ProviderOptions,
                        $"Clients.{family}.ProviderOptions") as ITextToSpeechProviderOptions;
                    break;
                case SpeechToTextClientConfig stt:
                    stt.ProviderOptions = SnapshotPayload(composition, stt.Provider?.Key, family,
                        ProviderPayloadKind.OperationOptions, stt.ProviderOptions,
                        $"Clients.{family}.ProviderOptions") as ISpeechToTextProviderOptions;
                    break;
                case HostedFilesClientConfig files:
                    files.ProviderOptions = SnapshotPayload(composition, files.Provider?.Key, family,
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

    internal static AudioRunConfig? CloneAudio(AudioRunConfig? source) => source is null ? null : new AudioRunConfig
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
        ContentType = source.ContentType,
        EnablePlayback = source.EnablePlayback
    };

    internal static CompactionRunPolicy? CloneCompaction(
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

    internal static CollapsingRunPolicy? CloneCollapsing(CollapsingRunPolicy? source)
        => source is null ? null : source with { };

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
        var clients = new AgentClientsConfig { Chat = (ChatClientConfig)ProviderClientConfigSnapshot.Clone(source) };
        SnapshotProviderPayloads(clients, composition);
        return clients.Chat;
    }

    internal static StructuredOutputOptions? CloneStructuredOutput(StructuredOutputOptions? source) =>
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
