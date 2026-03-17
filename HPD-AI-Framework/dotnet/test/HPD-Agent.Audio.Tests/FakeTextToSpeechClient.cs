// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using System.Runtime.CompilerServices;
using HPD.Agent.Audio;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.Tests;

/// <summary>
/// Fake TTS client for testing purposes.
/// Records all synthesis requests and returns configurable responses.
/// </summary>
public sealed class FakeTextToSpeechClient : ITextToSpeechClient
{
    private readonly List<SynthesisRequest> _requests = new();
    private byte[] _audioData = [0x00, 0x01, 0x02, 0x03]; // Minimal fake audio
    private bool _disposed;

    /// <summary>
    /// Gets all recorded synthesis requests.
    /// </summary>
    public IReadOnlyList<SynthesisRequest> Requests => _requests.AsReadOnly();

    /// <summary>
    /// Configures the audio data to return in responses.
    /// </summary>
    public void SetAudioData(byte[] audioData)
    {
        _audioData = audioData;
    }

    /// <inheritdoc />
    public Task<TextToSpeechResponse> GetSpeechAsync(
        string text,
        TextToSpeechOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _requests.Add(new SynthesisRequest(text, options, false));

        return Task.FromResult(new TextToSpeechResponse
        {
            Audio = new DataContent(_audioData, "audio/mpeg"),
            Duration = TimeSpan.FromSeconds(text.Length * 0.1), // Rough estimate
            ModelId = options?.ModelId,
            Voice = options?.Voice
        });
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingSpeechAsync(
        IAsyncEnumerable<string> textChunks,
        TextToSpeechOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Collect all text chunks
        var fullText = new System.Text.StringBuilder();
        await foreach (var chunk in textChunks.WithCancellation(cancellationToken))
        {
            fullText.Append(chunk);
        }

        var text = fullText.ToString();
        _requests.Add(new SynthesisRequest(text, options, true));

        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        // Return single chunk
        yield return new TextToSpeechResponseUpdate
        {
            Audio = new DataContent(_audioData, "audio/mpeg"),
            Duration = TimeSpan.FromSeconds(text.Length * 0.1),
            IsLast = true,
            SequenceNumber = 0
        };
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
    }

    /// <summary>
    /// Clears recorded requests.
    /// </summary>
    public void Clear()
    {
        _requests.Clear();
    }

    /// <summary>
    /// Represents a recorded synthesis request.
    /// </summary>
    public record SynthesisRequest(
        string Text,
        TextToSpeechOptions? Options,
        bool IsStreaming);
}
