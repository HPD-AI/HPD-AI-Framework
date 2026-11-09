// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

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
    /// </list>
    /// </para>
    /// </remarks>
    public string ProcessingStrategy { get; set; } = ImageProcessingStrategy.PassThrough;

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

}
