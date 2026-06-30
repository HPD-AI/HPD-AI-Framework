// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;

namespace HPD.Agent.Providers.Audio.OpenAI;

public static class OpenAIAudioProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        ProviderDiscovery.RegisterProviderFactory(() => new OpenAIAudioProvider());
        ProviderDiscovery.RegisterProviderConfigType<OpenAISttConfig>(
            OpenAIAudioProvider.Key,
            ProviderClientFamily.SpeechToText,
            json => JsonSerializer.Deserialize(json, OpenAISttJsonContext.Default.OpenAISttConfig),
            config => JsonSerializer.Serialize(config, OpenAISttJsonContext.Default.OpenAISttConfig));
        ProviderDiscovery.RegisterProviderConfigType<OpenAITtsConfig>(
            OpenAIAudioProvider.Key,
            ProviderClientFamily.TextToSpeech,
            json => JsonSerializer.Deserialize(json, OpenAITtsJsonContext.Default.OpenAITtsConfig),
            config => JsonSerializer.Serialize(config, OpenAITtsJsonContext.Default.OpenAITtsConfig));
        ProviderDiscovery.RegisterProviderConfigType<OpenAIRealtimeConfig>(
            OpenAIAudioProvider.Key,
            ProviderClientFamily.Realtime,
            json => JsonSerializer.Deserialize(json, OpenAIRealtimeJsonContext.Default.OpenAIRealtimeConfig),
            config => JsonSerializer.Serialize(config, OpenAIRealtimeJsonContext.Default.OpenAIRealtimeConfig));

        SecretAliasRegistry.Register("openai:ApiKey", "OPENAI_API_KEY");
        SecretAliasRegistry.Register("openai:Endpoint", "OPENAI_ENDPOINT");
    }
}
