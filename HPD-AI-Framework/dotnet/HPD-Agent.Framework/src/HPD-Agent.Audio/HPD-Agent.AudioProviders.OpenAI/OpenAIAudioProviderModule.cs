// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using HPD.Agent.Audio.Tts;
using HPD.Agent.Audio.Stt;
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
        // Register TTS factory
        TtsProviderDiscovery.RegisterFactory("openai-audio", () => new OpenAITtsProviderFactory());
        TtsProviderDiscovery.RegisterConfigType<OpenAITtsConfig>("openai-audio");

        // Register STT factory
        SttProviderDiscovery.RegisterFactory("openai-audio", () => new OpenAISttProviderFactory());
        SttProviderDiscovery.RegisterConfigType<OpenAISttConfig>("openai-audio");

        // Note: OpenAI does not provide VAD, so no VadProviderDiscovery registration
    }
}
