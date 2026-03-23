// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Middleware.Image;

/// <summary>
/// Configuration options for ImageMiddleware.
/// </summary>
public class ImageMiddlewareOptions
{
    /// <summary>
    /// Gets or sets the processing strategy for images.
    /// Default: PassThrough (send images directly to vision models).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Available strategies:
    /// <list type="bullet">
    /// <item><b>PassThrough</b>: Default. Send images to vision-capable models without processing.</item>
    /// <item><b>OCR (future)</b>: Extract text via OCR, replace ImageContent with TextContent.</item>
    /// <item><b>Description (future)</b>: Generate description via vision API, add as TextContent.</item>
    /// </list>
    /// </para>
    /// </remarks>
    public string ProcessingStrategy { get; set; } = ImageProcessingStrategy.PassThrough;

    // Future: OCR-specific options
    // public string? OcrLanguage { get; set; }
    // public int? OcrDpi { get; set; }

    // Future: Description-specific options
    // public string? VisionModel { get; set; }
    // public int? MaxDescriptionLength { get; set; }
}

/// <summary>
/// Constants for image processing strategies.
/// </summary>
public static class ImageProcessingStrategy
{
    /// <summary>
    /// Pass images directly to vision-capable models without processing.
    /// This is the default strategy for modern vision models.
    /// </summary>
    public const string PassThrough = nameof(PassThrough);

    /// <summary>
    /// Extract text via OCR, replace ImageContent with TextContent.
    /// Useful for non-vision models or when text extraction is needed.
    /// (Future implementation)
    /// </summary>
    public const string OCR = nameof(OCR);

    /// <summary>
    /// Generate description via vision API, add as TextContent alongside image.
    /// Useful for enhancing context or creating searchable metadata.
    /// (Future implementation)
    /// </summary>
    public const string Description = nameof(Description);
}
