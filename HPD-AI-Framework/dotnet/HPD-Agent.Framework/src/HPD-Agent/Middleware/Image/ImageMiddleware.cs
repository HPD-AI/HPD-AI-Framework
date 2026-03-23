// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.AI;

namespace HPD.Agent.Middleware.Image;

/// <summary>
/// Middleware for processing ImageContent using configurable strategies.
/// Default strategy: PassThrough (send images directly to vision models).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two-Layer Architecture:</b>
/// <list type="bullet">
/// <item><b>AssetUploadMiddleware (Layer 1)</b>: Universal storage - uploads ALL binary content automatically</item>
/// <item><b>ImageMiddleware (Layer 2)</b>: Domain processing - transforms images based on strategy</item>
/// </list>
/// </para>
/// <para>
/// <b>Default Behavior (PassThrough):</b>
/// <list type="number">
/// <item>User sends ImageContent</item>
/// <item>AssetUploadMiddleware uploads bytes → transforms to UriContent</item>
/// <item>ImageMiddleware (PassThrough): No-op, image passes through</item>
/// <item>Vision model receives image for native processing</item>
/// </list>
/// </para>
/// <para>
/// <b>Usage:</b>
/// <code>
/// var agent = new AgentBuilder()
///     .WithChatClient(client)
///     .WithImageHandling()  // Default: PassThrough strategy
///     .Build();
/// </code>
/// </para>
/// </remarks>
public class ImageMiddleware : IAgentMiddleware
{
    private readonly IImageStrategy _strategy;
    private readonly ImageMiddlewareOptions _options;

    /// <summary>
    /// Creates ImageMiddleware with the specified strategy and options.
    /// </summary>
    /// <param name="strategy">Image processing strategy.</param>
    /// <param name="options">Image processing options (optional).</param>
    public ImageMiddleware(
        IImageStrategy strategy,
        ImageMiddlewareOptions? options = null)
    {
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _options = options ?? new ImageMiddlewareOptions();
    }

    /// <summary>
    /// Process images in the user message before sending to LLM.
    /// NOTE: AssetUploadMiddleware handles storage automatically (runs before this).
    /// </summary>
    public async Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        // Extract ImageContent from user message
        var images = ExtractImageContents(context);
        if (!images.Any())
            return;

        // Process images using strategy
        await _strategy.ProcessImagesAsync(
            context,
            images,
            _options,
            cancellationToken);
    }

    /// <summary>
    /// Extract ImageContent from user message.
    /// </summary>
    private IEnumerable<ImageContent> ExtractImageContents(BeforeMessageTurnContext context)
    {
        if (context.UserMessage == null)
            return Enumerable.Empty<ImageContent>();

        return context.UserMessage.Contents
            .OfType<ImageContent>();
    }
}
