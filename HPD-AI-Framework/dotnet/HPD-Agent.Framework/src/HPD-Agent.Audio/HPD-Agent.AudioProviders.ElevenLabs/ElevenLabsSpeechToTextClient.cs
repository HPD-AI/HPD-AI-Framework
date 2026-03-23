// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace HPD.Agent.Audio.ElevenLabs;

/// <summary>
/// ElevenLabs speech-to-text client implementing Microsoft.Extensions.AI ISpeechToTextClient.
/// Uses ElevenLabs Scribe model for audio transcription.
/// Implements the pattern from ElevenLabs SDK's Extensions.AI support.
/// </summary>
public sealed class ElevenLabsSpeechToTextClient : ISpeechToTextClient
{
    private const string DefaultModelId = "scribe_v1";

    private readonly ElevenLabsAudioConfig _config;
    private readonly global::ElevenLabs.SpeechToTextClient _client;
    private bool _disposed;

    /// <summary>
    /// Creates a new ElevenLabs STT client.
    /// </summary>
    /// <param name="config">ElevenLabs configuration</param>
    /// <param name="httpClient">Optional HttpClient for connection pooling</param>
    public ElevenLabsSpeechToTextClient(
        ElevenLabsAudioConfig config,
        HttpClient? httpClient = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _config.Validate();

        _client = new global::ElevenLabs.SpeechToTextClient(
            httpClient: httpClient,
            authorizations: [
                new global::ElevenLabs.EndPointAuthorization
                {
                    Type = "ApiKey",
                    Location = "Header",
                    Name = "xi-api-key",
                    Value = _config.ApiKey!
                }
            ]
        );
    }

    /// <summary>
    /// Transcribe audio to text (non-streaming).
    /// Adapted from ElevenLabs SDK's SpeechToTextClient.ExtensionsAI.cs pattern.
    /// </summary>
    public async Task<SpeechToTextResponse> GetTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioSpeechStream);

        // Prepare the request body
        var request = new global::ElevenLabs.BodySpeechToTextV1SpeechToTextPost
        {
            ModelId = options?.ModelId ?? _config.SttModelId ?? DefaultModelId,
            LanguageCode = options?.SpeechLanguage
        };

        // Convert stream to byte array
        MemoryStream? bytes = audioSpeechStream as MemoryStream;
        if (bytes is null || bytes.Position != 0)
        {
            bytes = new MemoryStream();
            await audioSpeechStream.CopyToAsync(bytes, 81920, cancellationToken).ConfigureAwait(false);
        }

        request.File = bytes.TryGetBuffer(out ArraySegment<byte> buffer) &&
                       buffer.Array is not null &&
                       buffer.Offset == 0 &&
                       buffer.Count == bytes.Length
            ? buffer.Array
            : bytes.ToArray();

        request.Filename = audioSpeechStream is FileStream fileStream
            ? fileStream.Name
            : "audio_input";

        // Call SDK's CreateSpeechToTextAsync
        var result = await _client.CreateSpeechToTextAsync(request, cancellationToken: cancellationToken);

        // The result is a SpeechToTextChunkResponseModel directly (not wrapped in AnyOf in v0.9.0)
        TimeSpan? startTime = null;
        TimeSpan? endTime = null;

        if (result.Words?.Count > 0)
        {
            startTime = result.Words.Min(w => w.Start) is double st
                ? TimeSpan.FromSeconds(st)
                : null;
            endTime = result.Words.Max(w => w.End) is double et
                ? TimeSpan.FromSeconds(et)
                : null;
        }

        return new SpeechToTextResponse(result.Text)
        {
            StartTime = startTime,
            EndTime = endTime,
            ModelId = request.ModelId,
            RawRepresentation = result
        };
    }

    /// <summary>
    /// Transcribe streaming audio to text.
    /// ElevenLabs doesn't support true streaming STT, so we delegate to non-streaming.
    /// </summary>
    public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        IAsyncEnumerable<ReadOnlyMemory<byte>> audioSpeechChunks,
        SpeechToTextOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioSpeechChunks);

        // Collect all chunks into a stream
        var memoryStream = new MemoryStream();
        await foreach (var chunk in audioSpeechChunks.WithCancellation(cancellationToken))
        {
            await memoryStream.WriteAsync(chunk, cancellationToken);
        }

        memoryStream.Position = 0;

        // Process as non-streaming
        var response = await GetTextAsync(memoryStream, options, cancellationToken);

        // Convert to streaming updates
        foreach (var update in response.ToSpeechToTextResponseUpdates())
        {
            yield return update;
        }
    }

    /// <summary>
    /// Transcribe streaming audio to text (Stream overload).
    /// </summary>
    public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioSpeechStream);

        var response = await GetTextAsync(audioSpeechStream, options, cancellationToken);

        foreach (var update in response.ToSpeechToTextResponseUpdates())
        {
            yield return update;
        }
    }

    /// <summary>
    /// Gets a service of the specified type.
    /// </summary>
    public object? GetService(Type serviceType, object? key = null)
    {
        if (serviceType is null)
        {
            throw new ArgumentNullException(nameof(serviceType));
        }

        if (key is not null)
        {
            return null;
        }

        if (serviceType == typeof(ElevenLabsAudioConfig))
        {
            return _config;
        }

        if (serviceType == typeof(global::ElevenLabs.SpeechToTextClient))
        {
            return _client;
        }

        if (serviceType.IsInstanceOfType(this))
        {
            return this;
        }

        return null;
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client?.Dispose();
    }
}
