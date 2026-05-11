// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.Recognition;

/// <summary>
/// Selects an HPD recognizer adapter for a Microsoft.Extensions.AI speech client.
/// </summary>
public static class MeaiSpeechRecognizerFactory
{
    /// <summary>
    /// Creates either a batch or streaming-shaped recognizer from truthful provider capabilities.
    /// </summary>
    public static ISpeechRecognizer Create(
        ISpeechToTextClient client,
        SpeechRecognitionCapabilities capabilities,
        bool useStreamingRecognition,
        string? provider = null,
        string? model = null,
        bool disposeClient = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(capabilities);

        if (useStreamingRecognition && CanUseStreamingShape(capabilities))
        {
            return new MeaiStreamingSpeechRecognizer(
                client,
                capabilities,
                provider,
                model,
                disposeClient);
        }

        return new MeaiBatchSpeechRecognizer(
            client,
            provider,
            model,
            disposeClient);
    }

    private static bool CanUseStreamingShape(SpeechRecognitionCapabilities capabilities) =>
        capabilities.StreamingInput ||
        capabilities.InterimResults ||
        capabilities.PreflightResults ||
        capabilities.ServerSpeechEvents;
}
