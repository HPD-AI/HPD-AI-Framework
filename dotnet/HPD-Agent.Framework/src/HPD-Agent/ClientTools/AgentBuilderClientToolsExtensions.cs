// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

namespace HPD.Agent.ClientTools;

/// <summary>
/// Extension methods for configuring Client tools on AgentBuilder.
/// </summary>
public static class AgentBuilderClientToolsExtensions
{
    /// <summary>
    /// Adds Client tool support to the agent with default configuration.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <returns>The builder for chaining</returns>
    /// <example>
    /// <code>
    /// await using var agent = await new AgentBuilder()
    ///     .WithClientTools()
    ///     .BuildAsync();
    /// </code>
    /// </example>
    public static AgentBuilder WithClientTools(this AgentBuilder builder)
    {
        return builder.WithClientTools(new ClientToolConfig());
    }

    /// <summary>
    /// Adds Client tool support to the agent with custom configuration.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="config">Client tool configuration</param>
    /// <returns>The builder for chaining</returns>
    /// <example>
    /// <code>
    /// await using var agent = await new AgentBuilder()
    ///     .WithClientTools(new ClientToolConfig
    ///     {
    ///         InvokeTimeout = TimeSpan.FromSeconds(60),
    ///         ValidateSchemaOnRegistration = true
    ///     })
    ///     .BuildAsync();
    /// </code>
    /// </example>
    public static AgentBuilder WithClientTools(this AgentBuilder builder, ClientToolConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        var middleware = new ClientToolMiddleware(config);
        builder.WithMiddleware(middleware);
        return builder;
    }

    /// <summary>
    /// Adds Client tool support to the agent with configuration action.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="configure">Action to configure Client tools</param>
    /// <returns>The builder for chaining</returns>
    /// <example>
    /// <code>
    /// await using var agent = await new AgentBuilder()
    ///     .WithClientTools(config =>
    ///     {
    ///         config.InvokeTimeout = TimeSpan.FromSeconds(60);
    ///         config.DisconnectionStrategy = ClientDisconnectionStrategy.RetryWithBackoff;
    ///     })
    ///     .BuildAsync();
    /// </code>
    /// </example>
    public static AgentBuilder WithClientTools(
        this AgentBuilder builder,
        Action<ClientToolConfig> configure)
    {
        if (configure == null)
            throw new ArgumentNullException(nameof(configure));

        var config = new ClientToolConfig();
        configure(config);
        return builder.WithClientTools(config);
    }
}
