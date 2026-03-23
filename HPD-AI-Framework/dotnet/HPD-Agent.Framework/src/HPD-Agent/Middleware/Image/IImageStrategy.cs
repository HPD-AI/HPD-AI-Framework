// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Middleware.Image;

/// <summary>
/// Strategy interface for processing ImageContent in messages.
/// Implementations define how images are handled before being sent to the LLM.
/// </summary>
/// <remarks>
/// <para>
/// Current strategies:
/// <list type="bullet">
/// <item><b>PassThrough</b>: Send images directly to vision-capable models (default)</item>
/// <item><b>OCR (future)</b>: Extract text, replace with TextContent</item>
/// <item><b>Description (future)</b>: Use vision API to describe, add as TextContent</item>
/// </list>
/// </para>
/// <para>
/// <b>Note:</b> Image storage is handled by AssetUploadMiddleware (universal storage layer).
/// Strategies focus on content transformation only.
/// </para>
/// </remarks>
public interface IImageStrategy
{
    /// <summary>
    /// Process images in the user message before sending to LLM.
    /// </summary>
    /// <param name="context">Message turn context containing the user message.</param>
    /// <param name="images">ImageContent instances to process.</param>
    /// <param name="options">Image processing options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ProcessImagesAsync(
        BeforeMessageTurnContext context,
        IEnumerable<ImageContent> images,
        ImageMiddlewareOptions options,
        CancellationToken cancellationToken);
}
