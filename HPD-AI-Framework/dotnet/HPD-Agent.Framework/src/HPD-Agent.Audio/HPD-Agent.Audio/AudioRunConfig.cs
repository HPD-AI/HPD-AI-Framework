// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio.Stt;
using HPD.Agent.Audio.Tts;
using HPD.Agent.Audio.Vad;

namespace HPD.Agent.Audio;

/// <summary>
/// Per-request audio configuration overrides.
/// Slim runtime API exposing only commonly-changed settings.
/// </summary>
/// <remarks>
/// <para><b>Design Philosophy:</b></para>
/// <para>
/// AudioRunConfig provides per-request audio customization for common scenarios:
/// - Voice switching (per-user preferences, multi-voice narratives)
/// - Provider switching (multi-tenant SaaS with different TTS budgets)
/// - Language switching (multilingual conversations)
/// - I/O mode switching (voice-only vs. voice+captions)
/// </para>
/// <para>
/// Pipeline tuning settings (turn detection, filler audio, interruption behavior)
/// are intentionally NOT exposed here - they should be configured at agent-level
/// via AudioPipelineMiddleware or AgentBuilder for UX consistency.
/// </para>
/// <para><b>Common Use Cases:</b></para>
/// <code>
/// // Voice switching for different users
/// var options = new AgentRunConfig
/// {
///     Audio = new AudioRunConfig { Voice = "alloy" }
/// };
///
/// // Provider switching for multilingual
/// var options = new AgentRunConfig
/// {
///     Audio = new AudioRunConfig
///     {
///         Tts = new TtsConfig { Provider = "elevenlabs", Voice = "Rachel" }
///     }
/// };
///
/// // Disable audio for specific request
/// var options = new AgentRunConfig
/// {
///     Audio = new AudioRunConfig { Disabled = true }
/// };
/// </code>
/// </remarks>
public class AudioRunConfig
{
    //
    // ROLE-BASED PROVIDER SWITCHING (most common)
    //

    /// <summary>
    /// TTS configuration override (provider, voice, model).
    /// Null = use middleware defaults.
    /// </summary>
    public TtsConfig? Tts { get; set; }

    /// <summary>
    /// STT configuration override (provider, language, model).
    /// Null = use middleware defaults.
    /// </summary>
    public SttConfig? Stt { get; set; }

    /// <summary>
    /// VAD configuration override (provider, sensitivity).
    /// Null = use middleware defaults.
    /// </summary>
    public VadConfig? Vad { get; set; }

    //
    // I/O CONTROL (common for multi-modal switching)
    //

    /// <summary>
    /// Audio processing mode override.
    /// - Pipeline: STT → LLM → TTS (default)
    /// - Native: Single model (GPT-4o Realtime, Gemini Live)
    /// Null = use middleware defaults.
    /// </summary>
    public AudioProcessingMode? ProcessingMode { get; set; }

    /// <summary>
    /// Audio I/O mode override.
    /// - AudioToText: Voice input, text output
    /// - TextToAudio: Text input, voice output
    /// - AudioToAudio: Full voice (no captions)
    /// - AudioToAudioAndText: Full voice with captions (default)
    /// - TextToAudioAndText: Text input, voice + text output
    /// Null = use middleware defaults.
    /// </summary>
    public AudioIOMode? IOMode { get; set; }

    /// <summary>
    /// Language override (ISO 639-1 code: "en", "es", "fr", "de", etc.).
    /// Overrides language settings in Tts.Language and Stt.Language.
    /// Null = use middleware defaults.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Disable audio processing for this request.
    /// Useful for text-only requests in audio-enabled agents.
    /// Null = use middleware defaults.
    /// </summary>
    public bool? Disabled { get; set; }

    //
    // CONVENIENCE SHORTCUTS (for common overrides)
    //

    /// <summary>
    /// Voice override (shortcut for Tts.Voice).
    /// Examples: "nova", "alloy", "shimmer" (OpenAI), "21m00Tcm4TlvDq8ikWAM" (ElevenLabs).
    /// Null = use middleware defaults.
    /// </summary>
    public string? Voice { get; set; }

    /// <summary>
    /// TTS model override (shortcut for Tts.ModelId).
    /// Examples: "tts-1", "tts-1-hd" (OpenAI), "eleven_turbo_v2_5" (ElevenLabs).
    /// Null = use middleware defaults.
    /// </summary>
    public string? TtsModel { get; set; }

    /// <summary>
    /// TTS speed override (shortcut for Tts.Speed).
    /// Range: 0.25 to 4.0 (1.0 = normal speed).
    /// Null = use middleware defaults.
    /// </summary>
    public float? TtsSpeed { get; set; }

    //
    // CONVERSION TO FULL CONFIG
    //

    /// <summary>
    /// Converts runtime options to full AudioConfig for middleware consumption.
    /// Merges convenience shortcuts (Voice, TtsModel, TtsSpeed) into Tts config.
    /// </summary>
    /// <returns>AudioConfig with runtime overrides applied</returns>
    internal AudioConfig ToFullConfig()
    {
        // Fail fast — catch Native+Stt/Tts/Vad and shortcut conflicts before building the config.
        Validate();

        var config = new AudioConfig
        {
            Tts = Tts,
            Stt = Stt,
            Vad = Vad,
            Language = Language,
            Disabled = Disabled
        };

        // Apply processing/IO mode if specified
        if (ProcessingMode.HasValue)
            config.ProcessingMode = ProcessingMode.Value;

        if (IOMode.HasValue)
            config.IOMode = IOMode.Value;

        // Apply convenience shortcuts to Tts config
        if (Voice != null || TtsModel != null || TtsSpeed != null)
        {
            config.Tts ??= new TtsConfig();
            config.Tts.Voice ??= Voice;
            config.Tts.ModelId ??= TtsModel;
            if (TtsSpeed.HasValue)
                config.Tts.Speed ??= TtsSpeed.Value;
        }

        return config;
    }

    /// <summary>
    /// Validates runtime options.
    /// </summary>
    public void Validate()
    {
        // Native mode: STT/TTS/VAD are owned by the model — setting them is a configuration error.
        if (ProcessingMode == AudioProcessingMode.Native)
        {
            if (Stt != null)
                throw new InvalidOperationException(
                    "AudioRunConfig.Stt cannot be set when ProcessingMode is Native. " +
                    "In Native mode the model handles speech-to-text directly; " +
                    "remove the Stt configuration or switch to ProcessingMode.Pipeline.");

            if (Tts != null)
                throw new InvalidOperationException(
                    "AudioRunConfig.Tts cannot be set when ProcessingMode is Native. " +
                    "In Native mode the model produces audio output directly; " +
                    "remove the Tts configuration or switch to ProcessingMode.Pipeline.");

            if (Vad != null)
                throw new InvalidOperationException(
                    "AudioRunConfig.Vad cannot be set when ProcessingMode is Native. " +
                    "In Native mode turn detection is handled by the model itself; " +
                    "remove the Vad configuration or switch to ProcessingMode.Pipeline.");

            if (Voice != null || TtsModel != null || TtsSpeed != null)
                throw new InvalidOperationException(
                    "AudioRunConfig convenience shortcuts (Voice, TtsModel, TtsSpeed) cannot be used " +
                    "when ProcessingMode is Native. In Native mode the model controls its own voice; " +
                    "remove these overrides or switch to ProcessingMode.Pipeline.");
        }

        // Validate role configs
        Tts?.Validate();
        Stt?.Validate();
        Vad?.Validate();

        // Validate TTS speed
        if (TtsSpeed is < 0.25f or > 4.0f)
            throw new ArgumentException("TtsSpeed must be between 0.25 and 4.0");
    }
}
