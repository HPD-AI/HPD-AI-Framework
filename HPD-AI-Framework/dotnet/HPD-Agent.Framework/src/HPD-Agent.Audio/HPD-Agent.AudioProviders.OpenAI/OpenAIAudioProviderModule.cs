// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using HPD.Agent.Providers;
using HPD.Agent.AudioProviders.OpenAI.Tts;
using HPD.Agent.AudioProviders.OpenAI.Stt;

namespace HPD.Agent.AudioProviders.OpenAI;

/// <summary>
/// Auto-registers OpenAI audio provider on assembly load.
/// OpenAI supports both TTS and STT (but not VAD).
/// </summary>
public static class OpenAIAudioProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    internal static void Initialize()
    {
        ProviderDiscovery.RegisterProviderFactory(() => new OpenAIAudioProvider());
        ProviderDiscovery.RegisterProviderConfigType<OpenAITtsConfig>(
            "openai",
            ProviderClientFamily.TextToSpeech,
            json => System.Text.Json.JsonSerializer.Deserialize(json, OpenAITtsJsonContext.Default.OpenAITtsConfig),
            config => System.Text.Json.JsonSerializer.Serialize(config, OpenAITtsJsonContext.Default.OpenAITtsConfig));
        ProviderDiscovery.RegisterProviderConfigType<OpenAISttConfig>(
            "openai",
            ProviderClientFamily.SpeechToText,
            json => System.Text.Json.JsonSerializer.Deserialize(json, OpenAISttJsonContext.Default.OpenAISttConfig),
            config => System.Text.Json.JsonSerializer.Serialize(config, OpenAISttJsonContext.Default.OpenAISttConfig));
    }
}
