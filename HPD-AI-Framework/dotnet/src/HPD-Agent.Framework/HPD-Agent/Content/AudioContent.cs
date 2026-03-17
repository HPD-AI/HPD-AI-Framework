// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Represents audio content for speech-to-text and audio models.
/// Extends DataContent with audio-specific conveniences.
/// </summary>
/// <remarks>
/// <para>
/// AudioContent flows through the AudioPipelineMiddleware for automatic
/// transcription via ISpeechToTextClient.
/// </para>
/// <para>
/// <b>Supported Formats:</b> MP3, WAV, OGG, FLAC, WebM, M4A
/// </para>
/// <para>
/// <b>Middleware Behavior:</b>
/// <list type="bullet">
/// <item>AssetUploadMiddleware: Uploads to IContentStore with /uploads folder tag</item>
/// <item>AudioPipelineMiddleware: Transcribes via STT, replaces with TextContent</item>
/// </list>
/// </para>
/// </remarks>
public class AudioContent : DataContent
{
    /// <summary>
    /// Creates audio content from bytes.
    /// </summary>
    /// <param name="data">Audio bytes.</param>
    /// <param name="mediaType">MIME type. Defaults to "audio/mpeg" (MP3).</param>
    public AudioContent(ReadOnlyMemory<byte> data, string? mediaType = null)
        : base(data, mediaType ?? MimeTypeRegistry.AudioMpeg)
    {
    }

    /// <summary>
    /// Creates audio content from a data URI.
    /// </summary>
    /// <param name="uri">Data URI containing audio data.</param>
    [JsonConstructor]
    public AudioContent(string uri)
        : base(uri)
    {
        if (!HasTopLevelMediaType("audio"))
            throw new ArgumentException("Data URI must contain audio content.", nameof(uri));
    }

    /// <summary>
    /// Creates audio content from a file path.
    /// </summary>
    /// <param name="filePath">Path to audio file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>AudioContent with file data.</returns>
    public static async Task<AudioContent> FromFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var mediaType = GetMediaTypeFromExtension(filePath);
        return new AudioContent(bytes, mediaType) { Name = Path.GetFileName(filePath) };
    }

    /// <summary>
    /// Creates audio content for WAV format.
    /// </summary>
    /// <param name="data">Audio bytes in WAV format.</param>
    /// <returns>AudioContent with WAV MIME type.</returns>
    public static AudioContent Wav(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.AudioWav);

    /// <summary>
    /// Creates audio content for MP3 format.
    /// </summary>
    /// <param name="data">Audio bytes in MP3 format.</param>
    /// <returns>AudioContent with MP3 MIME type.</returns>
    public static AudioContent Mp3(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.AudioMpeg);

    /// <summary>
    /// Creates audio content for OGG format.
    /// </summary>
    /// <param name="data">Audio bytes in OGG format.</param>
    /// <returns>AudioContent with OGG MIME type.</returns>
    public static AudioContent Ogg(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.AudioOgg);

    /// <summary>
    /// Creates audio content for FLAC format.
    /// </summary>
    /// <param name="data">Audio bytes in FLAC format.</param>
    /// <returns>AudioContent with FLAC MIME type.</returns>
    public static AudioContent Flac(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.AudioFlac);

    /// <summary>
    /// Creates audio content for WebM format.
    /// </summary>
    /// <param name="data">Audio bytes in WebM format.</param>
    /// <returns>AudioContent with WebM MIME type.</returns>
    public static AudioContent WebM(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.AudioWebM);

    /// <summary>
    /// Creates audio content for M4A format.
    /// </summary>
    /// <param name="data">Audio bytes in M4A format.</param>
    /// <returns>AudioContent with M4A MIME type.</returns>
    public static AudioContent M4a(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.AudioMp4);

    private static string GetMediaTypeFromExtension(string filePath)
    {
        return MimeTypeRegistry.GetMimeTypeFromPath(filePath)
            ?? MimeTypeRegistry.AudioMpeg; // Default to MP3 if unknown
    }
}
