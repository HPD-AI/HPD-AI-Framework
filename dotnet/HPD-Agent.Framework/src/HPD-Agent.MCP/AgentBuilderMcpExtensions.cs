using HPD.Agent;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Agent.MCP;

/// <summary>
/// Extension methods for configuring Model Context Protocol (MCP) capabilities for the AgentBuilder.
/// </summary>
public static class AgentBuilderMcpExtensions
{
    /// <summary>
    /// Configures MCP options without registering a manifest.
    /// Use this for toolharness-owned [MCPServer] declarations or before calling WithMCP.
    /// </summary>
    /// <param name="configure">Configuration action for MCP options</param>
    public static AgentBuilder WithMCPOptions(this AgentBuilder builder, Action<MCPOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = builder.Config.Mcp?.Options as MCPOptions ?? new MCPOptions();
        configure(options);

        builder.Config.Mcp ??= new McpConfig();
        builder.Config.Mcp.Options = options;

        return builder;
    }

    /// <summary>
    /// Enables MCP support with the specified manifest file
    /// </summary>
    /// <param name="manifestPath">Path to the MCP manifest JSON file</param>
    /// <param name="options">Optional MCP configuration options</param>
    public static AgentBuilder WithMCP(this AgentBuilder builder, string manifestPath, MCPOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new ArgumentException("Manifest path cannot be null or empty", nameof(manifestPath));

        options ??= builder.Config.Mcp?.Options as MCPOptions;
        builder.Config.Mcp = new McpConfig
        {
            ManifestPath = manifestPath,
            Options = options
        };
        var manager = new MCPClientManager(
            builder.Logger?.CreateLogger("HPD.Agent.MCP.MCPClientManager") ?? NullLogger.Instance, 
            options);
        builder.McpClientManager = manager;
        builder.WithEventSubscription(coordinator => manager.AttachLiveUpdates(coordinator));

        return builder;
    }

    /// <summary>
    /// Enables MCP support with fluent configuration
    /// </summary>
    /// <param name="manifestPath">Path to the MCP manifest JSON file</param>
    /// <param name="configure">Configuration action for MCP options</param>
    public static AgentBuilder WithMCP(this AgentBuilder builder, string manifestPath, Action<MCPOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = builder.Config.Mcp?.Options as MCPOptions ?? new MCPOptions();
        configure(options);
        return builder.WithMCP(manifestPath, options);
    }

    /// <summary>
    /// Enables MCP support with manifest content directly
    /// </summary>
    /// <param name="manifestContent">JSON content of the MCP manifest</param>
    /// <param name="options">Optional MCP configuration options</param>
    public static AgentBuilder WithMCPContent(this AgentBuilder builder, string manifestContent, MCPOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (string.IsNullOrWhiteSpace(manifestContent))
            throw new ArgumentException("Manifest content cannot be null or empty", nameof(manifestContent));

        options ??= builder.Config.Mcp?.Options as MCPOptions;
        builder.Config.Mcp = new McpConfig
        {
            ManifestContent = manifestContent,
            Options = options
        };
        var manager = new MCPClientManager(
            builder.Logger?.CreateLogger("HPD.Agent.MCP.MCPClientManager") ?? NullLogger.Instance,
            options);
        builder.McpClientManager = manager;
        builder.WithEventSubscription(coordinator => manager.AttachLiveUpdates(coordinator));

        return builder;
    }
}
