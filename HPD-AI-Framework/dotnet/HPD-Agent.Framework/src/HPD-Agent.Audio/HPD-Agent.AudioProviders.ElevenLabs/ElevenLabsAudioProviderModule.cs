// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using HPD.Agent.Providers;
using HPD.Agent.AudioProviders.ElevenLabs.Tts;

namespace HPD.Agent.AudioProviders.ElevenLabs;

/// <summary>
/// Auto-registers ElevenLabs audio provider on assembly load.
/// ElevenLabs supports TTS only (no STT or VAD).
/// </summary>
public static class ElevenLabsProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    internal static void Initialize()
    {
        ProviderDiscovery.RegisterProviderFactory(() => new ElevenLabsProvider());
        ProviderDiscovery.RegisterProviderConfigType<ElevenLabsTtsConfig>(
            "elevenlabs",
            ProviderClientFamily.TextToSpeech,
            json => System.Text.Json.JsonSerializer.Deserialize(json, ElevenLabsTtsJsonContext.Default.ElevenLabsTtsConfig),
            config => System.Text.Json.JsonSerializer.Serialize(config, ElevenLabsTtsJsonContext.Default.ElevenLabsTtsConfig));
    }
}
