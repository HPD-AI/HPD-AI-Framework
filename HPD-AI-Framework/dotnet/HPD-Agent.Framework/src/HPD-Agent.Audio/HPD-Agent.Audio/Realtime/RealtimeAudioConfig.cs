// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json.Serialization;
using HPD.Agent;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.Realtime;

/// <summary>
/// Runtime configuration for a true bidirectional realtime model session.
/// </summary>
public sealed class RealtimeAudioConfig
{
    /// <summary>Input audio format expected by the realtime provider.</summary>
    public RealtimeAudioFormat? InputAudioFormat { get; set; }

    /// <summary>Output audio format requested from the realtime provider.</summary>
    public RealtimeAudioFormat? OutputAudioFormat { get; set; }

    /// <summary>Realtime output voice.</summary>
    public string? Voice { get; set; }

    /// <summary>Requested response modalities, such as "text" and "audio".</summary>
    public IReadOnlyList<string>? OutputModalities { get; set; }

    /// <summary>Server-side voice activity detection options.</summary>
    public VoiceActivityDetectionOptions? VoiceActivityDetection { get; set; }

    /// <summary>Send a response creation message after explicit audio commits.</summary>
    public bool CreateResponseOnCommit { get; set; } = true;

    /// <summary>Use provider-owned VAD and turn management when available.</summary>
    public bool UseServerVad { get; set; } = true;

    /// <summary>
    /// Realtime client configuration override for this audio runtime.
    /// Merged over agent-level realtime defaults when the runtime starts.
    /// </summary>
    public ClientProviderConfig? Client { get; set; }

    /// <summary>
    /// Direct realtime client override for this audio runtime.
    /// Highest precedence for realtime session binding at runtime startup.
    /// </summary>
    [JsonIgnore]
    public IRealtimeClient? OverrideClient { get; set; }

    /// <summary>Provider-specific raw options factory.</summary>
    [JsonIgnore]
    public Func<object?>? RawRepresentationFactory { get; set; }

    /// <summary>Create MEAI realtime session options.</summary>
    public RealtimeSessionOptions ToSessionOptions(string? model = null, string? instructions = null) =>
        new()
        {
            Model = model,
            InputAudioFormat = InputAudioFormat,
            OutputAudioFormat = OutputAudioFormat,
            Voice = Voice,
            Instructions = instructions,
            OutputModalities = OutputModalities,
            VoiceActivityDetection = VoiceActivityDetection ?? new VoiceActivityDetectionOptions
            {
                Enabled = UseServerVad
            },
            RawRepresentationFactory = RawRepresentationFactory
        };

    /// <summary>Create a copy of the configuration.</summary>
    public RealtimeAudioConfig Clone() => new()
    {
        InputAudioFormat = InputAudioFormat,
        OutputAudioFormat = OutputAudioFormat,
        Voice = Voice,
        OutputModalities = OutputModalities is null ? null : [.. OutputModalities],
        VoiceActivityDetection = VoiceActivityDetection is null
            ? null
            : new VoiceActivityDetectionOptions
            {
                Enabled = VoiceActivityDetection.Enabled,
                AllowInterruption = VoiceActivityDetection.AllowInterruption
            },
        CreateResponseOnCommit = CreateResponseOnCommit,
        UseServerVad = UseServerVad,
        Client = CloneClient(Client),
        OverrideClient = OverrideClient,
        RawRepresentationFactory = RawRepresentationFactory
    };

    private static ClientProviderConfig? CloneClient(ClientProviderConfig? config)
    {
        if (config == null)
            return null;

        return new ClientProviderConfig
        {
            ProviderKey = config.ProviderKey,
            ModelName = config.ModelName,
            ApiKey = config.ApiKey,
            Endpoint = config.Endpoint,
            DefaultChatOptions = config.DefaultChatOptions,
            CustomHeaders = config.CustomHeaders is null
                ? null
                : new Dictionary<string, string>(config.CustomHeaders, StringComparer.OrdinalIgnoreCase),
            AdditionalProperties = config.AdditionalProperties is null
                ? null
                : new Dictionary<string, object>(config.AdditionalProperties),
            ProviderOptionsJson = config.ProviderOptionsJson,
            HttpReferer = config.HttpReferer,
            AppName = config.AppName,
            PromptFormatter = config.PromptFormatter
        };
    }
}
