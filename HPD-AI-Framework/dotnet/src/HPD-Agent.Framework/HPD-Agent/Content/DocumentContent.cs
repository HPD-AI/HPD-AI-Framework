// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.IO;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Represents document content for text extraction.
/// Extends DataContent with document-specific conveniences.
/// </summary>
/// <remarks>
/// <para>
/// DocumentContent flows through DocumentHandlingMiddleware for automatic
/// text extraction via TextExtractionUtility.
/// </para>
/// <para>
/// <b>Supported Formats:</b> PDF, Word (.doc/.docx), Excel (.xls/.xlsx),
/// PowerPoint (.ppt/.pptx), HTML, Markdown, plain text
/// </para>
/// <para>
/// <b>Middleware Behavior:</b>
/// <list type="bullet">
/// <item>AssetUploadMiddleware: Uploads to IContentStore with /uploads folder tag</item>
/// <item>DocumentHandlingMiddleware: Extracts text, replaces with TextContent</item>
/// </list>
/// </para>
/// </remarks>
public class DocumentContent : DataContent
{
    /// <summary>
    /// Creates document content from bytes.
    /// </summary>
    /// <param name="data">Document bytes.</param>
    /// <param name="mediaType">MIME type. Required for documents.</param>
    public DocumentContent(ReadOnlyMemory<byte> data, string mediaType)
        : base(data, mediaType)
    {
    }

    /// <summary>
    /// Creates document content from a data URI.
    /// </summary>
    /// <param name="uri">Data URI containing document data.</param>
    [JsonConstructor]
    public DocumentContent(string uri)
        : base(uri)
    {
    }

    /// <summary>
    /// Creates document content from a file path.
    /// </summary>
    /// <param name="filePath">Path to document file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>DocumentContent with file data.</returns>
    public static async Task<DocumentContent> FromFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var mediaType = GetMediaTypeFromExtension(filePath);
        return new DocumentContent(bytes, mediaType) { Name = Path.GetFileName(filePath) };
    }

    /// <summary>
    /// Creates document content from a remote URI.
    /// </summary>
    /// <param name="uri">URI to remote document.</param>
    /// <param name="download">
    /// If true, downloads the document and converts to base64 (default for compatibility).
    /// If false, returns UriContent for direct URL passthrough (better performance for supported providers like OpenRouter for PDFs).
    /// </param>
    /// <param name="httpClient">Optional HttpClient for download (only used when download=true). If null, uses shared default client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// If download=true: DocumentContent with downloaded data.
    /// If download=false: UriContent with direct URL reference.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Passthrough mode (download=false)</b>: Creates UriContent with the URL directly.
    /// Use this for better performance when the provider supports direct document URLs (e.g., OpenRouter for PDFs).
    /// The document is NOT downloaded, saving bandwidth and time.
    /// </para>
    /// <para>
    /// <b>Download mode (download=true)</b>: Downloads the document and converts to base64 DataContent.
    /// Use this for compatibility with providers that don't support direct URLs, or when you need
    /// to process the document data locally.
    /// </para>
    /// <para>
    /// For production scenarios with DI, inject IHttpClientFactory-created client:
    /// <code>
    /// var factory = services.GetRequiredService&lt;IHttpClientFactory&gt;();
    /// var content = await DocumentContent.FromUriAsync(uri, download: true, factory.CreateClient());
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
            // Use application/pdf as default since it's the most common document type
            return new UriContent(uri, MimeTypeRegistry.ApplicationPdf);
        }

        // Download mode: fetch and convert to DocumentContent
        httpClient ??= DefaultSharedHttpClient;
        var response = await httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType
            ?? GetMediaTypeFromExtension(uri.ToString());

        var fileName = Path.GetFileName(uri.LocalPath);

        return new DocumentContent(bytes, contentType) { Name = fileName };
    }

    /// <summary>
    /// Creates document content from a web URL.
    /// Downloads and auto-detects format from Content-Type header or URL extension.
    /// </summary>
    /// <param name="url">URL to remote document.</param>
    /// <param name="httpClient">Optional HttpClient for download. If null, uses shared default client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>DocumentContent with downloaded data.</returns>
    /// <remarks>
    /// <para>
    /// This method always downloads the document. For URL passthrough support, use <see cref="FromUriAsync"/> instead.
    /// </para>
    /// <para>
    /// For production scenarios with DI, inject IHttpClientFactory-created client:
    /// <code>
    /// var factory = services.GetRequiredService&lt;IHttpClientFactory&gt;();
    /// var content = await DocumentContent.FromUrlAsync(url, factory.CreateClient());
    /// </code>
    /// </para>
    /// <para>
    /// If httpClient is null, uses a shared HttpClient instance (safe for most scenarios).
    /// </para>
    /// </remarks>
    public static async Task<DocumentContent> FromUrlAsync(
        string url,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        var uri = new Uri(url);
        var result = await FromUriAsync(uri, download: true, httpClient, cancellationToken);
        return (DocumentContent)result; // Safe cast since download=true returns DocumentContent
    }

    /// <summary>
    /// Verifies that the document data matches the declared MIME type by checking magic bytes.
    /// </summary>
    /// <returns>True if document format is valid, false otherwise.</returns>
    /// <remarks>
    /// This method only checks file format signatures (magic bytes).
    /// It does NOT verify document can be parsed or content is valid.
    /// </remarks>
    public bool HasValidFormat()
    {
        if (Data.IsEmpty)
            return false;

        // Normalize MIME type first (handles variants)
        var normalizedType = MimeTypeRegistry.NormalizeMimeType(MediaType);

        // Validate based on MediaType
        return normalizedType switch
        {
            var type when type == MimeTypeRegistry.ApplicationPdf => ValidatePdf(Data),
            var type when type == MimeTypeRegistry.ApplicationWordOpenXml => ValidateZip(Data), // .docx
            var type when type == MimeTypeRegistry.ApplicationExcelOpenXml => ValidateZip(Data), // .xlsx
            var type when type == MimeTypeRegistry.ApplicationPowerPointOpenXml => ValidateZip(Data), // .pptx
            var type when type == MimeTypeRegistry.ApplicationZip => ValidateZip(Data),
            _ => true // For text formats, assume valid
        };
    }

    /// <summary>
    /// Detects the actual document format from magic bytes.
    /// Returns null if format cannot be determined.
    /// </summary>
    /// <returns>Detected MIME type, or null if unrecognized.</returns>
    public string? DetectFormat()
    {
        if (Data.IsEmpty)
            return null;

        if (ValidatePdf(Data))
            return MimeTypeRegistry.ApplicationPdf;

        if (ValidateZip(Data))
            return MimeTypeRegistry.ApplicationZip; // Could be .docx, .xlsx, .pptx

        return null; // Text formats don't have magic bytes
    }

    private static bool ValidatePdf(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 5) return false;
        var span = data.Span;
        // PDF: %PDF-
        return span[0] == 0x25 && span[1] == 0x50 && span[2] == 0x44 && span[3] == 0x46 && span[4] == 0x2D;
    }

    private static bool ValidateZip(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 4) return false;
        var span = data.Span;
        // ZIP: PK\x03\x04 or PK\x05\x06
        return span[0] == 0x50 && span[1] == 0x4B &&
               ((span[2] == 0x03 && span[3] == 0x04) || (span[2] == 0x05 && span[3] == 0x06));
    }

    private static readonly HttpClient DefaultSharedHttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(100) // Azure SDK default
    };

    /// <summary>
    /// Creates PDF document content.
    /// </summary>
    /// <param name="data">PDF document bytes.</param>
    /// <returns>DocumentContent with PDF MIME type.</returns>
    public static DocumentContent Pdf(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.ApplicationPdf);

    /// <summary>
    /// Creates Word document content (.docx).
    /// </summary>
    /// <param name="data">Word document bytes.</param>
    /// <returns>DocumentContent with Word MIME type.</returns>
    public static DocumentContent Word(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.ApplicationWordOpenXml);

    /// <summary>
    /// Creates Excel document content (.xlsx).
    /// </summary>
    /// <param name="data">Excel document bytes.</param>
    /// <returns>DocumentContent with Excel MIME type.</returns>
    public static DocumentContent Excel(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.ApplicationExcelOpenXml);

    /// <summary>
    /// Creates PowerPoint document content (.pptx).
    /// </summary>
    /// <param name="data">PowerPoint document bytes.</param>
    /// <returns>DocumentContent with PowerPoint MIME type.</returns>
    public static DocumentContent PowerPoint(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.ApplicationPowerPointOpenXml);

    /// <summary>
    /// Creates HTML document content.
    /// </summary>
    /// <param name="data">HTML document bytes.</param>
    /// <returns>DocumentContent with HTML MIME type.</returns>
    public static DocumentContent Html(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.TextHtml);

    /// <summary>
    /// Creates Markdown document content.
    /// </summary>
    /// <param name="data">Markdown document bytes.</param>
    /// <returns>DocumentContent with Markdown MIME type.</returns>
    public static DocumentContent Markdown(ReadOnlyMemory<byte> data)
        => new(data, MimeTypeRegistry.TextMarkdown);

    private static string GetMediaTypeFromExtension(string filePath)
    {
        return MimeTypeRegistry.GetMimeTypeFromPath(filePath)
            ?? MimeTypeRegistry.ApplicationOctetStream; // Default to octet-stream if unknown
    }
}
