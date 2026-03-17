// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio;

/// <summary>
/// Extension methods for configuring audio options on AgentRunConfig.
/// Uses the slim AudioRunConfig API for common runtime customizations.
/// </summary>
public static class AgentRunConfigAudioExtensions
{
    /// <summary>
    /// Configures audio options for this run using AudioRunConfig (slim API).
    /// </summary>
    /// <param name="options">The agent run options.</param>
    /// <param name="audio">The audio runtime options.</param>
    /// <returns>The agent run options for chaining.</returns>
    public static AgentRunConfig WithAudio(this AgentRunConfig options, AudioRunConfig audio)
    {
        options.Audio = audio;
        return options;
    }

    /// <summary>
    /// Configures audio options for this run using a builder action.
    /// </summary>
    /// <param name="options">The agent run options.</param>
    /// <param name="configure">Action to configure the audio options.</param>
    /// <returns>The agent run options for chaining.</returns>
    public static AgentRunConfig WithAudio(this AgentRunConfig options, Action<AudioRunConfig> configure)
    {
        var audio = GetOrCreateAudioRunConfig(options);
        configure(audio);
        return options;
    }

    /// <summary>Voice conversation with captions (most common).</summary>
    /// <param name="options">The agent run options.</param>
    /// <returns>The agent run options for chaining.</returns>
    public static AgentRunConfig WithVoiceConversation(this AgentRunConfig options)
        => options.WithAudio(new AudioRunConfig { IOMode = AudioIOMode.AudioToAudioAndText });

    /// <summary>Voice input only, text output.</summary>
    /// <param name="options">The agent run options.</param>
    /// <returns>The agent run options for chaining.</returns>
    public static AgentRunConfig WithVoiceInput(this AgentRunConfig options)
        => options.WithAudio(new AudioRunConfig { IOMode = AudioIOMode.AudioToText });

    /// <summary>Text input, voice + text output.</summary>
    /// <param name="options">The agent run options.</param>
    /// <returns>The agent run options for chaining.</returns>
    public static AgentRunConfig WithVoiceOutput(this AgentRunConfig options)
        => options.WithAudio(new AudioRunConfig { IOMode = AudioIOMode.TextToAudioAndText });

    /// <summary>Disable audio for this request.</summary>
    /// <param name="options">The agent run options.</param>
    /// <returns>The agent run options for chaining.</returns>
    public static AgentRunConfig WithTextOnly(this AgentRunConfig options)
        => options.WithAudio(new AudioRunConfig { Disabled = true });

    /// <summary>Full voice conversation without captions.</summary>
    /// <param name="options">The agent run options.</param>
    /// <returns>The agent run options for chaining.</returns>
    public static AgentRunConfig WithFullVoice(this AgentRunConfig options)
        => options.WithAudio(new AudioRunConfig { IOMode = AudioIOMode.AudioToAudio });

    /// <summary>Native processing with captions (GPT-4o Realtime, Gemini Live).</summary>
    /// <param name="options">The agent run options.</param>
    /// <returns>The agent run options for chaining.</returns>
    public static AgentRunConfig WithNativeAudio(this AgentRunConfig options)
        => options.WithAudio(new AudioRunConfig { ProcessingMode = AudioProcessingMode.Native, IOMode = AudioIOMode.AudioToAudioAndText });

    /// <summary>
    /// Sets the TTS voice for this request.
    /// </summary>
    /// <param name="options">The agent run options.</param>
    /// <param name="voice">The voice to use (e.g., "nova", "alloy", "shimmer").</param>
    /// <returns>The agent run options for chaining.</returns>
    public static AgentRunConfig WithVoice(this AgentRunConfig options, string voice)
    {
        var audio = GetOrCreateAudioRunConfig(options);
        audio.Voice = voice;
        return options;
    }

    /// <summary>
    /// Sets the TTS model for this request.
    /// </summary>
    /// <param name="options">The agent run options.</param>
    /// <param name="model">The model to use (e.g., "tts-1", "tts-1-hd").</param>
    /// <returns>The agent run options for chaining.</returns>
    public static AgentRunConfig WithTtsModel(this AgentRunConfig options, string model)
    {
        var audio = GetOrCreateAudioRunConfig(options);
        audio.TtsModel = model;
        return options;
    }

    /// <summary>
    /// Sets the TTS speed for this request.
    /// </summary>
    /// <param name="options">The agent run options.</param>
    /// <param name="speed">The speed multiplier (0.25 to 4.0).</param>
    /// <returns>The agent run options for chaining.</returns>
    public static AgentRunConfig WithTtsSpeed(this AgentRunConfig options, float speed)
    {
        var audio = GetOrCreateAudioRunConfig(options);
        audio.TtsSpeed = speed;
        return options;
    }

    /// <summary>
    /// Gets the audio runtime options from the run options, or null if not set.
    /// </summary>
    /// <param name="options">The agent run options.</param>
    /// <returns>The audio runtime options, or null.</returns>
    public static AudioRunConfig? GetAudioRunConfig(this AgentRunConfig options)
        => options.Audio as AudioRunConfig;

    private static AudioRunConfig GetOrCreateAudioRunConfig(AgentRunConfig options)
    {
        if (options.Audio is AudioRunConfig existing)
            return existing;

        var audio = new AudioRunConfig();
        options.Audio = audio;
        return audio;
    }
}
