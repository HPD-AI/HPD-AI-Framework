using HPD.Agent;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Agent.MCP;

/// <summary>
/// Extension methods for configuring Model Context Protocol (MCP) capabilities for the AgentBuilder.
/// </summary>
public static class AgentBuilderMcpExtensions
{
    /// <summary>
    /// Enables MCP support with the specified manifest file
    /// </summary>
    /// <param name="manifestPath">Path to the MCP manifest JSON file</param>
    /// <param name="options">Optional MCP configuration options</param>
    public static AgentBuilder WithMCP(this AgentBuilder builder, string manifestPath, MCPOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new ArgumentException("Manifest path cannot be null or empty", nameof(manifestPath));

        builder.Config.Mcp = new McpConfig
        {
            ManifestPath = manifestPath,
            Options = options
        };
        builder.McpClientManager = new MCPClientManager(
            builder.Logger?.CreateLogger("HPD.Agent.MCP.MCPClientManager") ?? NullLogger.Instance, 
            options);

        return builder;
    }

    /// <summary>
    /// Enables MCP support with fluent configuration
    /// </summary>
    /// <param name="manifestPath">Path to the MCP manifest JSON file</param>
    /// <param name="configure">Configuration action for MCP options</param>
    public static AgentBuilder WithMCP(this AgentBuilder builder, string manifestPath, Action<MCPOptions> configure)
    {
        var options = new MCPOptions();
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
        if (string.IsNullOrWhiteSpace(manifestContent))
            throw new ArgumentException("Manifest content cannot be null or empty", nameof(manifestContent));

        // Store content in ManifestPath for now - we might need a separate property for content
        builder.Config.Mcp = new McpConfig
        {
            ManifestPath = manifestContent, // This represents content, not path
            Options = options
        };
        builder.McpClientManager = new MCPClientManager(
            builder.Logger?.CreateLogger("HPD.Agent.MCP.MCPClientManager") ?? NullLogger.Instance,
            options);

        return builder;
    }
}
