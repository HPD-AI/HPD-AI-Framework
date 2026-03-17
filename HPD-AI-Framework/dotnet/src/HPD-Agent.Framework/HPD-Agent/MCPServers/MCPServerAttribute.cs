namespace HPD.Agent;

/// <summary>
/// Marks a method as an MCP server provider.
/// The method must return MCPServerConfig (from HPD.Agent.MCP namespace).
/// </summary>
/// <remarks>
/// Two modes of operation:
/// 1. **Inline config**: Method returns a fully configured MCPServerConfig
/// 2. **Manifest reference**: Set FromManifest to load config from mcp.json by server name
///
/// Description is auto-fetched from the MCP server's ServerInfo metadata at connection time.
/// You can optionally override it via the Description property for collapsing purposes.
///
/// Two collapsing modes (controlled via CollapseWithinToolkit):
/// 1. **Flat** (default): MCP tools appear directly under the parent toolkit when expanded
/// 2. **Nested** (CollapseWithinToolkit = true): MCP tools sit behind their own container
///    inside the parent toolkit — two-level expand required
///
/// To require user permission for MCP tools, add [RequiresPermission] to the method
/// (same as [AIFunction] and [Skill]).
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public class MCPServerAttribute : Attribute
{
    /// <summary>
    /// Server name to look up when using FromManifest mode.
    /// Not used in inline config mode.
    /// </summary>
    public string? ServerName { get; }

    /// <summary>
    /// Custom name for the MCP server. Defaults to method name.
    /// Overrides the name from manifest if both are specified.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Optional description override for collapsing.
    /// If not set, description is auto-fetched from the MCP server's ServerInfo metadata.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Path to mcp.json manifest file to load config from.
    /// When set, the method can return null and config is loaded from manifest.
    /// The ServerName parameter specifies which server to look up.
    /// </summary>
    /// <example>
    /// [MCPServer("filesystem", FromManifest = "mcp.json")]
    /// public MCPServerConfig? FileSystem() => null;
    /// </example>
    public string? FromManifest { get; set; }

    /// <summary>
    /// When true, MCP tools are grouped behind their own container (e.g., MCP_wolfram)
    /// nested inside the parent toolkit container. Two expansions required: first the
    /// toolkit, then the MCP server container.
    ///
    /// When false (default), MCP tools appear directly under the parent toolkit
    /// when it is expanded — single expansion required.
    /// </summary>
    public bool CollapseWithinToolkit { get; set; } = false;

    public MCPServerAttribute() { }
    public MCPServerAttribute(string serverName) => ServerName = serverName;
}

/// <summary>
/// Generic version for typed metadata support (conditional MCP servers).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class MCPServerAttribute<TMetadata> : Attribute where TMetadata : IToolMetadata
{
    public Type ContextType => typeof(TMetadata);
    public string? ServerName { get; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? FromManifest { get; set; }
    public bool CollapseWithinToolkit { get; set; } = false;

    public MCPServerAttribute() { }
    public MCPServerAttribute(string serverName) => ServerName = serverName;
}
