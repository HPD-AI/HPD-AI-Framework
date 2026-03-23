// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio;

/// <summary>
/// Represents a text-to-speech client that converts text to audio.
/// Mirrors Microsoft.Extensions.AI's ISpeechToTextClient pattern.
/// </summary>
public interface ITextToSpeechClient : IDisposable
{
    /// <summary>
    /// Converts text to speech audio.
    /// </summary>
    /// <param name="text">The text to convert to speech.</param>
    /// <param name="options">Optional TTS configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The synthesized audio response.</returns>
    Task<TextToSpeechResponse> GetSpeechAsync(
        string text,
        TextToSpeechOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts streaming text chunks to streaming audio.
    /// Enables low-latency synthesis as LLM tokens arrive.
    /// </summary>
    /// <param name="textChunks">Async enumerable of text chunks to synthesize.</param>
    /// <param name="options">Optional TTS configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of audio chunks.</returns>
    IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingSpeechAsync(
        IAsyncEnumerable<string> textChunks,
        TextToSpeechOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a service of the specified type.
    /// </summary>
    /// <param name="serviceType">The type of service to retrieve.</param>
    /// <param name="serviceKey">An optional key for the service.</param>
    /// <returns>The service instance, or null if not available.</returns>
    object? GetService(Type serviceType, object? serviceKey = null);
}

/// <summary>
/// Options for text-to-speech synthesis.
/// </summary>
public class TextToSpeechOptions
{
    /// <summary>
    /// The model ID to use for synthesis (provider-specific).
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// The voice to use for synthesis.
    /// </summary>
    public string? Voice { get; set; }

    /// <summary>
    /// The language code for synthesis (e.g., "en-US").
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Speech speed multiplier. Default: 1.0 (normal speed).
    /// </summary>
    public float? Speed { get; set; }

    /// <summary>
    /// Pitch adjustment. Provider-specific interpretation.
    /// </summary>
    public float? Pitch { get; set; }

    /// <summary>
    /// Output audio format (e.g., "mp3", "wav", "pcm", "opus").
    /// </summary>
    public string? OutputFormat { get; set; }

    /// <summary>
    /// Output sample rate in Hz (e.g., 24000, 44100).
    /// </summary>
    public int? SampleRate { get; set; }

    /// <summary>
    /// Additional provider-specific properties.
    /// </summary>
    public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }

    /// <summary>
    /// Creates a default TextToSpeechOptions instance.
    /// </summary>
    public TextToSpeechOptions() { }

    /// <summary>
    /// Creates a clone of this options instance.
    /// </summary>
    public virtual TextToSpeechOptions Clone() => new(this);

    /// <summary>
    /// Copy constructor for cloning.
    /// </summary>
    protected TextToSpeechOptions(TextToSpeechOptions? other)
    {
        if (other is null) return;

        ModelId = other.ModelId;
        Voice = other.Voice;
        Language = other.Language;
        Speed = other.Speed;
        Pitch = other.Pitch;
        OutputFormat = other.OutputFormat;
        SampleRate = other.SampleRate;

        if (other.AdditionalProperties is not null)
        {
            AdditionalProperties = new AdditionalPropertiesDictionary(other.AdditionalProperties);
        }
    }
}

/// <summary>
/// Response from a text-to-speech synthesis request.
/// </summary>
public class TextToSpeechResponse
{
    /// <summary>
    /// The synthesized audio data.
    /// </summary>
    public DataContent? Audio { get; set; }

    /// <summary>
    /// The duration of the synthesized audio.
    /// </summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// The model ID that was used for synthesis.
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// The voice that was used for synthesis.
    /// </summary>
    public string? Voice { get; set; }

    /// <summary>
    /// Additional provider-specific properties.
    /// </summary>
    public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }
}

/// <summary>
/// A streaming update from text-to-speech synthesis.
/// </summary>
public record TextToSpeechResponseUpdate
{
    /// <summary>
    /// The audio chunk data.
    /// </summary>
    public DataContent? Audio { get; init; }

    /// <summary>
    /// The duration of this audio chunk.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Whether this is the last chunk in the stream.
    /// </summary>
    public bool IsLast { get; init; }

    /// <summary>
    /// Sequence number for ordering chunks.
    /// </summary>
    public int SequenceNumber { get; init; }
}
