// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Represents image content for vision models.
/// Extends DataContent with image-specific conveniences.
/// </summary>
/// <remarks>
/// <para>
/// ImageContent is a thin wrapper around <see cref="DataContent"/> that ensures
/// correct MIME types for images and provides a cleaner API.
/// </para>
/// <para>
/// <b>Supported Formats:</b> PNG, JPEG, GIF, WebP, BMP, TIFF, HEIC, AVIF, SVG
/// </para>
/// <para>
/// <b>Middleware Behavior:</b>
/// <list type="bullet">
/// <item>AssetUploadMiddleware: Uploads to IContentStore with /uploads folder tag, replaces with UriContent</item>
/// <item>ImageMiddleware: Handles upload and future image generation</item>
/// <item>Vision models: Sent as-is for native image understanding</item>
/// </list>
/// </para>
/// </remarks>
public class ImageContent : DataContent
{
    /// <summary>
    /// Creates image content from bytes with auto-detected format.
    /// </summary>
    /// <param name="data">Image bytes (PNG, JPEG, etc.)</param>
    /// <param name="mediaType">MIME type. Defaults to auto-detection, fallback to "image/png".</param>
    public ImageContent(ReadOnlyMemory<byte> data, string? mediaType = null)
        : base(data, mediaType ?? DetectImageType(data) ?? "image/png")
    {
    }

    /// <summary>
    /// Creates image content from a data URI.
    /// </summary>
    /// <param name="uri">Data URI containing image data.</param>
    [JsonConstructor]
    public ImageContent(string uri)
        : base(uri)
    {
        if (!HasTopLevelMediaType("image"))
            throw new ArgumentException("Data URI must contain image content.", nameof(uri));
    }

    /// <summary>
    /// Creates image content from a file path.
    /// </summary>
    /// <param name="filePath">Path to image file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>ImageContent with file data.</returns>
    public static async Task<ImageContent> FromFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var mediaType = GetMediaTypeFromExtension(filePath);
        return new ImageContent(bytes, mediaType) { Name = Path.GetFileName(filePath) };
    }

    /// <summary>
    /// Creates image content from a remote URI.
    /// </summary>
    /// <param name="uri">URI to remote image.</param>
    /// <param name="download">
    /// If true, downloads the image and converts to base64 (default for compatibility).
    /// If false, returns UriContent for direct URL passthrough (better performance for supported providers).
    /// </param>
    /// <param name="httpClient">Optional HttpClient for download (only used when download=true). If null, uses shared default client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// If download=true: ImageContent with downloaded data.
    /// If download=false: UriContent with direct URL reference.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Passthrough mode (download=false)</b>: Creates UriContent with the URL directly.
    /// Use this for better performance when the provider supports direct image URLs (e.g., OpenRouter, OpenAI).
    /// The image is NOT downloaded, saving bandwidth and time.
    /// </para>
    /// <para>
    /// <b>Download mode (download=true)</b>: Downloads the image and converts to base64 DataContent.
    /// Use this for compatibility with providers that don't support direct URLs, or when you need
    /// to process the image data locally.
    /// </para>
    /// <para>
    /// For production scenarios with DI, inject IHttpClientFactory-created client:
    /// <code>
    /// var factory = services.GetRequiredService&lt;IHttpClientFactory&gt;();
    /// var content = await ImageContent.FromUriAsync(uri, download: true, factory.CreateClient());
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
            // Use image/* wildcard since we don't know exact format without downloading
            return new UriContent(uri, MimeTypeRegistry.ImageAny);
        }

        // Download mode: fetch and convert to ImageContent
        httpClient ??= DefaultSharedHttpClient;
        var bytes = await httpClient.GetByteArrayAsync(uri, cancellationToken);
        return new ImageContent(bytes); // Auto-detect format from magic bytes
    }

    /// <summary>
    /// Verifies that the image data signature matches the declared MIME type.
    /// Does NOT validate image integrity, dimensions, or decoding.
    /// </summary>
    /// <returns>True if magic bytes match MIME type, false otherwise.</returns>
    /// <remarks>
    /// This method only checks file format signatures (magic bytes).
    /// It does NOT verify:
    /// - Image can be decoded successfully
    /// - Image dimensions are valid
    /// - Image data is not corrupted
    /// - Color space is correct
    /// For comprehensive validation, use a proper image library.
    /// </remarks>
    public bool HasValidFormat()
    {
        if (Data.IsEmpty)
            return false;

        var detectedType = DetectFormat();
        if (detectedType == null)
            return false; // Unrecognized format

        // Check if detected type matches declared MediaType
        return string.Equals(detectedType, MediaType, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Detects the actual image format from magic bytes.
    /// Returns null if format is unrecognized.
    /// </summary>
    /// <returns>Detected MIME type (e.g., "image/png"), or null if unrecognized.</returns>
    /// <remarks>
    /// Useful for diagnosing format mismatches or detecting actual format when MIME type is unknown.
    /// </remarks>
    public string? DetectFormat() => DetectImageType(Data);

    private static readonly HttpClient DefaultSharedHttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(100) // Azure SDK default
    };

    /// <summary>
    /// Detects image type from magic bytes.
    /// Supports: PNG, JPEG, GIF, WebP, BMP, HEIC, AVIF, SVG.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Detection Limitations:</b>
    /// - HEIC/AVIF: Checks ftyp box, may have false positives with other ISO formats
    /// - SVG: Simple heuristic (checks for "&lt;svg" tag in first 512 bytes), not foolproof
    /// - For production SVG validation, use an XML parser
    /// </para>
    /// </remarks>
    private static string? DetectImageType(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 4) return null;
        var span = data.Span;

        // PNG: 89 50 4E 47
        if (span[0] == 0x89 && span[1] == 0x50 && span[2] == 0x4E && span[3] == 0x47)
            return MimeTypeRegistry.ImagePng;

        // JPEG: FF D8 FF
        if (span[0] == 0xFF && span[1] == 0xD8 && span[2] == 0xFF)
            return MimeTypeRegistry.ImageJpeg;

        // GIF: 47 49 46 38
        if (span[0] == 0x47 && span[1] == 0x49 && span[2] == 0x46 && span[3] == 0x38)
            return MimeTypeRegistry.ImageGif;

        // WebP: 52 49 46 46 ... 57 45 42 50
        if (span.Length >= 12 && span[0] == 0x52 && span[1] == 0x49 && span[2] == 0x46 && span[3] == 0x46
            && span[8] == 0x57 && span[9] == 0x45 && span[10] == 0x42 && span[11] == 0x50)
            return MimeTypeRegistry.ImageWebP;

        // BMP: 42 4D
        if (span[0] == 0x42 && span[1] == 0x4D)
            return MimeTypeRegistry.ImageBmp;

        // HEIC/HEIF: Checks for ftyp box with heic/heix/hevc/heim/heis/mif1
        // Format: [size] 66 74 79 70 [brand]
        if (span.Length >= 12 && span[4] == 0x66 && span[5] == 0x74 &&
            span[6] == 0x79 && span[7] == 0x70)
        {
            // Check ftyp brand (bytes 8-11)
            var brand = Encoding.ASCII.GetString(span.Slice(8, 4).ToArray());
            if (brand.StartsWith("heic") || brand.StartsWith("heix") ||
                brand.StartsWith("hevc") || brand.StartsWith("heim") ||
                brand.StartsWith("heis") || brand.StartsWith("mif1"))
                return MimeTypeRegistry.ImageHeic;
        }

        // AVIF: Similar to HEIC but with avif brand
        if (span.Length >= 12 && span[4] == 0x66 && span[5] == 0x74 &&
            span[6] == 0x79 && span[7] == 0x70)
        {
            var brand = Encoding.ASCII.GetString(span.Slice(8, 4).ToArray());
            if (brand.StartsWith("avif") || brand.StartsWith("avis"))
                return MimeTypeRegistry.ImageAvif;
        }

        // SVG: Check for "<svg" tag in first 512 bytes (simple heuristic)
        if (data.Length > 5)
        {
            var checkLength = Math.Min(512, data.Length);
            var text = Encoding.UTF8.GetString(span.Slice(0, checkLength).ToArray());
            if (text.Contains("<svg", StringComparison.OrdinalIgnoreCase))
                return MimeTypeRegistry.ImageSvg;
        }

        return null;
    }

    private static string GetMediaTypeFromExtension(string filePath)
    {
        return MimeTypeRegistry.GetMimeTypeFromPath(filePath)
            ?? MimeTypeRegistry.ImagePng; // Default to PNG if unknown
    }
}
