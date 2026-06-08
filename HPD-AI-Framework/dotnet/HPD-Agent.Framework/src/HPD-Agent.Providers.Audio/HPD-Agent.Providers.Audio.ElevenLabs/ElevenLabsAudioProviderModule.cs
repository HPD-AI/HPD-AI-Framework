// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;

namespace HPD.Agent.Providers.Audio.ElevenLabs;

public static class ElevenLabsAudioProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        ProviderDiscovery.RegisterProviderFactory(() => new ElevenLabsAudioProvider());
        ProviderDiscovery.RegisterProviderConfigType<ElevenLabsTtsConfig>(
            ElevenLabsAudioProvider.Key,
            ProviderClientFamily.TextToSpeech,
            json => JsonSerializer.Deserialize(json, ElevenLabsTtsJsonContext.Default.ElevenLabsTtsConfig),
            config => JsonSerializer.Serialize(config, ElevenLabsTtsJsonContext.Default.ElevenLabsTtsConfig));
        ProviderDiscovery.RegisterProviderConfigType<ElevenLabsSttConfig>(
            ElevenLabsAudioProvider.Key,
            ProviderClientFamily.SpeechToText,
            json => JsonSerializer.Deserialize(json, ElevenLabsTtsJsonContext.Default.ElevenLabsSttConfig),
            config => JsonSerializer.Serialize(config, ElevenLabsTtsJsonContext.Default.ElevenLabsSttConfig));

        SecretAliasRegistry.Register("elevenlabs:ApiKey", "ELEVENLABS_API_KEY");
    }
}
