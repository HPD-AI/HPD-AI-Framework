// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Middleware.Image;

/// <summary>
/// Default strategy: sends ImageContent directly to vision-capable models without processing.
/// </summary>
/// <remarks>
/// <para>
/// This strategy is designed for modern vision models (GPT-4V, Claude 3.5, Gemini)
/// that can natively understand images. No transformation is performed.
/// </para>
/// <para>
/// <b>What happens:</b>
/// <list type="bullet">
/// <item>AssetUploadMiddleware: Uploads image bytes → transforms to UriContent (automatic)</item>
/// <item>PassThroughImageStrategy: No-op (images pass through unchanged)</item>
/// <item>LLM: Receives image for native vision processing</item>
/// </list>
/// </para>
/// </remarks>
public class PassThroughImageStrategy : IImageStrategy
{
    /// <summary>
    /// No-op implementation. Images are sent directly to vision models.
    /// </summary>
    public Task ProcessImagesAsync(
        BeforeMessageTurnContext context,
        IEnumerable<ImageContent> images,
        ImageMiddlewareOptions options,
        CancellationToken cancellationToken)
    {
        // PassThrough: No processing needed
        // AssetUploadMiddleware handles storage automatically
        // Vision models receive images as-is
        return Task.CompletedTask;
    }
}
