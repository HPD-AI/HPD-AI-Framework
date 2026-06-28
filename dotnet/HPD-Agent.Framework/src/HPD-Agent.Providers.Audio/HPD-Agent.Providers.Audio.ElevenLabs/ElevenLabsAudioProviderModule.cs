// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Audio.ElevenLabs;

public static class ElevenLabsAudioProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        ProviderContributionRegistry.RegisterProviderFactory(() => new ElevenLabsAudioProvider());
        ProviderContributionRegistry.RegisterProviderConfigType<ElevenLabsTtsConfig>(
            ElevenLabsAudioProvider.Key,
            ProviderClientFamily.TextToSpeech,
            json => JsonSerializer.Deserialize(json, ElevenLabsTtsJsonContext.Default.ElevenLabsTtsConfig),
            config => JsonSerializer.Serialize(config, ElevenLabsTtsJsonContext.Default.ElevenLabsTtsConfig));
        ProviderContributionRegistry.RegisterProviderConfigType<ElevenLabsSttConfig>(
            ElevenLabsAudioProvider.Key,
            ProviderClientFamily.SpeechToText,
            json => JsonSerializer.Deserialize(json, ElevenLabsTtsJsonContext.Default.ElevenLabsSttConfig),
            config => JsonSerializer.Serialize(config, ElevenLabsTtsJsonContext.Default.ElevenLabsSttConfig));

        ProviderContributionRegistry.RegisterSecretAlias("elevenlabs:ApiKey", "ELEVENLABS_API_KEY");
    }
}
