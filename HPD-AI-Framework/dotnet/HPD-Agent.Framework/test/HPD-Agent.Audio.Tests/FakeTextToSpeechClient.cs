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
    public Task<TextToSpeechResponse> GetAudioAsync(
        string text,
        TextToSpeechOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _requests.Add(new SynthesisRequest(text, options, false));

        return Task.FromResult(new TextToSpeechResponse([new DataContent(_audioData, "audio/mpeg")])
        {
            ModelId = options?.ModelId,
            AdditionalProperties = options?.VoiceId is null
                ? null
                : new AdditionalPropertiesDictionary { ["voiceId"] = options.VoiceId }
        });
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
        string text,
        TextToSpeechOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _requests.Add(new SynthesisRequest(text, options, true));

        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        // Return single chunk
        yield return new TextToSpeechResponseUpdate([new DataContent(_audioData, "audio/mpeg")])
        {
            Kind = TextToSpeechResponseUpdateKind.AudioUpdated,
            ModelId = options?.ModelId
        };

        await Task.CompletedTask;
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
