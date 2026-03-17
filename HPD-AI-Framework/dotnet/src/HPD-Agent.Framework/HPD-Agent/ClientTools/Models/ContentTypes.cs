// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;

namespace HPD.Agent.ClientTools;

/// <summary>
/// Base interface for tool result content.
/// Supports text, binary (images, files), and structured data.
/// </summary>
public interface IToolResultContent
{
    /// <summary>
    /// The content type discriminator.
    /// </summary>
    string Type { get; }
}

/// <summary>
/// Plain text content.
/// </summary>
public record TextContent(string Text) : IToolResultContent
{
    /// <inheritdoc />
    public string Type => "text";
}

/// <summary>
/// Binary content (images, PDFs, files, etc).
/// At least one of Data, Url, or Id must be provided.
/// </summary>
/// <param name="MimeType">MIME type (e.g., "image/png", "application/pdf")</param>
/// <param name="Data">Base64-encoded binary data</param>
/// <param name="Url">URL to fetch the content from</param>
/// <param name="Id">Reference ID for content stored elsewhere</param>
/// <param name="Filename">Optional filename for downloads</param>
public record BinaryContent(
    string MimeType,
    string? Data = null,
    string? Url = null,
    string? Id = null,
    string? Filename = null
) : IToolResultContent
{
    /// <inheritdoc />
    public string Type => "binary";

    /// <summary>
    /// Validates the binary content.
    /// </summary>
    /// <exception cref="ArgumentException">If no data source is provided</exception>
    public void Validate()
    {
        if (string.IsNullOrEmpty(MimeType))
            throw new ArgumentException("MimeType is required", nameof(MimeType));

        if (string.IsNullOrEmpty(Data) && string.IsNullOrEmpty(Url) && string.IsNullOrEmpty(Id))
            throw new ArgumentException("BinaryContent requires at least one of Data, Url, or Id");
    }
}

/// <summary>
/// Structured JSON content for complex data.
/// Useful when the result needs to be parsed programmatically.
/// </summary>
public record JsonContent(JsonElement Value) : IToolResultContent
{
    /// <inheritdoc />
    public string Type => "json";
}
