// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Recognition;

/// <summary>
/// Push-based lower-level primitive for realtime recognition lifecycles.
/// </summary>
public interface ISpeechRecognitionSession : IAsyncDisposable
{
    /// <summary>Truthful capabilities for this recognition session.</summary>
    SpeechRecognitionCapabilities Capabilities { get; }

    /// <summary>Pushes one audio frame into the recognition session.</summary>
    ValueTask PushAsync(
        AudioInputFrame frame,
        CancellationToken cancellationToken = default);

    /// <summary>Flushes pending recognizer input without ending the session.</summary>
    ValueTask FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>Signals that no more audio frames will be pushed.</summary>
    ValueTask EndAsync(CancellationToken cancellationToken = default);

    /// <summary>Recognition events emitted by the session.</summary>
    IAsyncEnumerable<SpeechRecognitionEvent> Events { get; }
}
