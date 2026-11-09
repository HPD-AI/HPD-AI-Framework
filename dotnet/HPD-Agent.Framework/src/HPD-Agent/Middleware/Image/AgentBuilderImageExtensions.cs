// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

namespace HPD.Agent.Middleware.Image;

/// <summary>
/// Extension methods for adding ImageMiddleware to AgentBuilder.
/// </summary>
public static class AgentBuilderImageExtensions
{
    /// <summary>
    /// Replaces auto-registered ImageMiddleware with default PassThrough strategy.
    /// (Redundant since PassThrough is already the default - useful for clarity only)
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <returns>The agent builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Note:</b> ImageMiddleware with PassThrough strategy is already auto-registered.
    /// You typically don't need to call this unless you want to be explicit about the configuration.
    /// </para>
    /// <para>
    /// <b>Default behavior:</b> Images are passed through to vision models unchanged.
    /// ContentLifecycleMiddleware (auto-registered) handles storage automatically.
    /// </para>
    /// <para>
    /// <b>Example (unnecessary but explicit):</b>
    /// <code>
    /// await using var agent = await new AgentBuilder()
    ///     .WithChatClient(visionClient)
    ///     .WithImageHandling()  // Redundant - already auto-registered
    ///     .BuildAsync();
    /// </code>
    /// </para>
    /// </remarks>
    public static AgentBuilder WithImageHandling(this AgentBuilder builder)
    {
        return builder.WithImageHandling(new ImageMiddlewareOptions());
    }

    /// <summary>
    /// Replaces auto-registered ImageMiddleware with custom options.
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="options">Image processing options.</param>
    /// <returns>The agent builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Note:</b> ImageMiddleware is auto-registered with PassThrough strategy by default.
    /// This method removes the auto-registered instance and replaces it with your custom configuration.
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// await using var agent = await new AgentBuilder()
    ///     .WithChatClient(client)
    ///     .WithImageHandling(new ImageMiddlewareOptions
    ///     {
    ///         ProcessingStrategy = ImageProcessingStrategy.PassThrough
    ///     })
    ///     .BuildAsync();
    /// </code>
    /// </para>
    /// </remarks>
    public static AgentBuilder WithImageHandling(
        this AgentBuilder builder,
        ImageMiddlewareOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        // Create strategy based on options
        IImageStrategy strategy = options.ProcessingStrategy switch
        {
            ImageProcessingStrategy.PassThrough => new PassThroughImageStrategy(),
            _ => throw new ArgumentException(
                $"Unsupported image processing strategy: {options.ProcessingStrategy}. " +
                $"Use {ImageProcessingStrategy.PassThrough} or supply a custom IImageStrategy.",
                nameof(options))
        };

        // Replace auto-registered ImageMiddleware only after the replacement is valid.
        var middleware = new ImageMiddleware(strategy, options);
        return builder.ReplaceMiddleware<ImageMiddleware>(middleware);
    }

    /// <summary>
    /// Replaces auto-registered ImageMiddleware with a custom strategy instance.
    /// Advanced usage for custom image processing logic.
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="strategy">Custom image processing strategy.</param>
    /// <param name="options">Image processing options (optional).</param>
    /// <returns>The agent builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Note:</b> ImageMiddleware is auto-registered with PassThrough strategy by default.
    /// This method removes the auto-registered instance and replaces it with your custom strategy.
    /// </para>
    /// <para>
    /// Use this overload to implement custom image processing strategies
    /// beyond the built-in PassThrough option.
    /// </para>
    /// </remarks>
    public static AgentBuilder WithImageHandling(
        this AgentBuilder builder,
        IImageStrategy strategy,
        ImageMiddlewareOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(strategy);

        // Replace auto-registered ImageMiddleware only after the replacement is valid.
        var middleware = new ImageMiddleware(strategy, options);
        return builder.ReplaceMiddleware<ImageMiddleware>(middleware);
    }
}
