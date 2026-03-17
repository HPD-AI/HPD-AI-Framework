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
/// Represents video content for video-capable models.
/// Extends DataContent with video-specific conveniences.
/// </summary>
/// <remarks>
/// <para>
/// VideoContent is prepared for future video model integrations (Sora, world models, etc.).
/// Currently supported by AssetUploadMiddleware for storage.
/// </para>
/// <para>
/// <b>Supported Formats:</b> MP4, WebM, MOV, AVI
/// </para>
/// <para>
/// <b>Future Capabilities:</b>
/// <list type="bullet">
/// <item>Video generation via future IVideoGenerator integration</item>
/// <item>Frame extraction for analysis</item>
/// <item>Audio track transcription</item>
/// </list>
/// </para>
/// </remarks>
public class VideoContent : DataContent
{
    /// <summary>
    /// Creates video content from bytes.
    /// </summary>
    /// <param name="data">Video bytes.</param>
    /// <param name="mediaType">MIME type. Defaults to "video/mp4".</param>
    public VideoContent(ReadOnlyMemory<byte> data, string? mediaType = null)
        : base(data, mediaType ?? MimeTypeRegistry.VideoMp4)
    {
    }

    /// <summary>
    /// Creates video content from a data URI.
    /// </summary>
    /// <param name="uri">Data URI containing video data.</param>
    [JsonConstructor]
    public VideoContent(string uri)
        : base(uri)
    {
        if (!HasTopLevelMediaType("video"))
            throw new ArgumentException("Data URI must contain video content.", nameof(uri));
    }

    /// <summary>
    /// Creates video content from a file path.
    /// </summary>
    /// <param name="filePath">Path to video file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>VideoContent with file data.</returns>
    public static async Task<VideoContent> FromFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var mediaType = GetMediaTypeFromExtension(filePath);
        return new VideoContent(bytes, mediaType) { Name = Path.GetFileName(filePath) };
    }

    /// <summary>
    /// Creates video content from a remote URI.
    /// </summary>
    /// <param name="uri">URI to remote video.</param>
    /// <param name="download">
    /// If true, downloads the video and converts to base64 (default for compatibility).
    /// If false, returns UriContent for direct URL passthrough (better performance for supported providers).
    /// </param>
    /// <param name="httpClient">Optional HttpClient for download (only used when download=true). If null, uses shared default client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// If download=true: VideoContent with downloaded data.
    /// If download=false: UriContent with direct URL reference.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Passthrough mode (download=false)</b>: Creates UriContent with the URL directly.
    /// Use this for better performance when the provider supports direct video URLs.
    /// <b>WARNING</b>: Video URL support varies by provider. For example:
    /// <list type="bullet">
    /// <item>Google Gemini (AI Studio): Only YouTube links supported</item>
    /// <item>Google Gemini (Vertex AI): Direct URLs not supported, use base64</item>
    /// <item>Other providers: Check model documentation</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Download mode (download=true)</b>: Downloads the video and converts to base64 DataContent.
    /// Use this for universal compatibility or when you need to process the video data locally.
    /// </para>
    /// <para>
    /// For production scenarios with DI, inject IHttpClientFactory-created client:
    /// <code>
    /// var factory = services.GetRequiredService&lt;IHttpClientFactory&gt;();
    /// var content = await VideoContent.FromUriAsync(uri, download: true, factory.CreateClient());
    /// </code>
    /// </para>
    /// </remarks>
    public static async Task<AIContent> FromUriAsync(
        Uri uri,
        bool download = false,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        if (!download)
        {
            // Passthrough mode: return UriContent directly
            // Use video/* wildcard since we don't know exact format without downloading
            return new UriContent(uri, MimeTypeRegistry.VideoAny);
        }

        // Download mode: fetch and convert to VideoContent
        httpClient ??= DefaultSharedHttpClient;
        var bytes = await httpClient.GetByteArrayAsync(uri, cancellationToken);
        return new VideoContent(bytes); // Auto-detect format from magic bytes
    }

    private static readonly HttpClient DefaultSharedHttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(100) // Azure SDK default
    };

    /// <summary>
    /// Creates video content for MP4 format.
    /// </summary>
    /// <param name="data">Video bytes in MP4 format.</param>
    /// <returns>VideoContent with MP4 MIME type.</returns>
    public static VideoContent Mp4(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.VideoMp4);

    /// <summary>
    /// Creates video content for WebM format.
    /// </summary>
    /// <param name="data">Video bytes in WebM format.</param>
    /// <returns>VideoContent with WebM MIME type.</returns>
    public static VideoContent WebM(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.VideoWebM);

    /// <summary>
    /// Creates video content for MOV format.
    /// </summary>
    /// <param name="data">Video bytes in MOV format.</param>
    /// <returns>VideoContent with MOV MIME type.</returns>
    public static VideoContent Mov(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.VideoQuickTime);

    /// <summary>
    /// Creates video content for AVI format.
    /// </summary>
    /// <param name="data">Video bytes in AVI format.</param>
    /// <returns>VideoContent with AVI MIME type.</returns>
    public static VideoContent Avi(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.VideoAvi);

    private static string GetMediaTypeFromExtension(string filePath)
    {
        return MimeTypeRegistry.GetMimeTypeFromPath(filePath)
            ?? MimeTypeRegistry.VideoMp4; // Default to MP4 if unknown
    }
}
