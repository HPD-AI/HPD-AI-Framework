// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Audio.OpenAI;

public static class OpenAIAudioProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        ProviderContributionRegistry.RegisterProviderFactory(() => new OpenAIAudioProvider());
        ProviderContributionRegistry.RegisterProviderConfigType<OpenAISttConfig>(
            OpenAIAudioProvider.Key,
            ProviderClientFamily.SpeechToText,
            json => JsonSerializer.Deserialize(json, OpenAISttJsonContext.Default.OpenAISttConfig),
            config => JsonSerializer.Serialize(config, OpenAISttJsonContext.Default.OpenAISttConfig));
        ProviderContributionRegistry.RegisterProviderConfigType<OpenAITtsConfig>(
            OpenAIAudioProvider.Key,
            ProviderClientFamily.TextToSpeech,
            json => JsonSerializer.Deserialize(json, OpenAITtsJsonContext.Default.OpenAITtsConfig),
            config => JsonSerializer.Serialize(config, OpenAITtsJsonContext.Default.OpenAITtsConfig));
        ProviderContributionRegistry.RegisterProviderConfigType<OpenAIRealtimeConfig>(
            OpenAIAudioProvider.Key,
            ProviderClientFamily.Realtime,
            json => JsonSerializer.Deserialize(json, OpenAIRealtimeJsonContext.Default.OpenAIRealtimeConfig),
            config => JsonSerializer.Serialize(config, OpenAIRealtimeJsonContext.Default.OpenAIRealtimeConfig));

        ProviderContributionRegistry.RegisterSecretAlias("openai:ApiKey", "OPENAI_API_KEY");
        ProviderContributionRegistry.RegisterSecretAlias("openai:Endpoint", "OPENAI_ENDPOINT");
    }
}
