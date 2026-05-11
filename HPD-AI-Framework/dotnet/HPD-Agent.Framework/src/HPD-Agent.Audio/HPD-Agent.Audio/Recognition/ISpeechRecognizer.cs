// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Recognition;

/// <summary>
/// Normalizes audio input into HPD speech recognition events.
/// </summary>
public interface ISpeechRecognizer : IAsyncDisposable
{
    /// <summary>Truthful capabilities for this recognizer.</summary>
    SpeechRecognitionCapabilities Capabilities { get; }

    /// <summary>
    /// Recognizes the supplied audio stream and emits normalized HPD recognition events.
    /// </summary>
    IAsyncEnumerable<SpeechRecognitionEvent> RecognizeAsync(
        IAsyncEnumerable<AudioInputFrame> audio,
        SpeechRecognitionOptions options,
        CancellationToken cancellationToken = default);
}
