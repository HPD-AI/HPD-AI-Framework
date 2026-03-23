using System.Collections.Generic;
using System.Text;

namespace HPD.Agent.SourceGenerator.Capabilities;

/// <summary>
/// Represents an MCP server capability — a method that returns MCPServerConfig
/// to register an MCP server connection when the toolkit is loaded.
/// Decorated with [MCPServer] attribute.
/// </summary>
internal class MCPServerCapability : BaseCapability
{
    public override CapabilityType Type => CapabilityType.MCPServer;
    public override bool IsContainer => false; // Container is created at runtime by WrapMCPServerTools
    public override bool EmitsIntoCreateTools => false;  // MCPServers registered via static MCPServers property
    public override bool RequiresInstance => !IsStatic;

    // ========== MCPServer-Specific Properties ==========

    /// <summary>
    /// Method name (e.g., "WolframServer")
    /// </summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>
    /// Whether this MCP server method is static.
    /// </summary>
    public bool IsStatic { get; set; }

    /// <summary>
    /// Path to mcp.json manifest (if using FromManifest mode).
    /// Null means inline config mode.
    /// </summary>
    public string? FromManifest { get; set; }

    /// <summary>
    /// Server name to look up in manifest (FromManifest mode only).
    /// </summary>
    public string? ManifestServerName { get; set; }

    /// <summary>
    /// When true, MCP tools sit behind their own container nested inside the parent toolkit.
    /// When false (default), MCP tools appear directly under the parent toolkit on expansion.
    /// </summary>
    public bool CollapseWithinToolkit { get; set; }

    /// <summary>
    /// Whether [RequiresPermission] attribute is present on the method.
    /// When true, emits RequiresPermissionOverride = true in registration code.
    /// When false, no override is emitted (config default applies).
    /// </summary>
    public bool RequiresPermission { get; set; }

    // ========== Code Generation ==========

    /// <summary>
    /// Generates the registration code for this MCP server.
    /// Unlike other capabilities, this emits an MCPServerRegistration object
    /// (not an HPDAIFunctionFactory.Create call) because MCP tools are loaded at runtime.
    /// </summary>
    public override string GenerateRegistrationCode(object parent)
    {
        var toolkit = (ToolkitInfo)parent;
        var sb = new StringBuilder();

        sb.AppendLine("new HPD.Agent.MCP.MCPServerRegistration");
        sb.AppendLine("{");
        sb.AppendLine($"    Name = \"{EscapeString(Name)}\",");
        sb.AppendLine($"    Description = \"{EscapeString(Description)}\",");
        sb.AppendLine($"    ParentToolkit = \"{toolkit.Name}\",");
        sb.AppendLine($"    CollapseWithinToolkit = {CollapseWithinToolkit.ToString().ToLower()},");

        if (FromManifest != null)
        {
            sb.AppendLine($"    FromManifest = \"{EscapeString(FromManifest)}\",");
            sb.AppendLine($"    ManifestServerName = \"{EscapeString(ManifestServerName ?? Name)}\",");
        }

        if (RequiresPermission)
            sb.AppendLine($"    RequiresPermissionOverride = true,");

        if (IsStatic)
        {
            sb.AppendLine($"    StaticConfigProvider = () => {toolkit.Name}.{MethodName}()");
        }
        else
        {
            sb.AppendLine($"    InstanceConfigProvider = (instance) => (({toolkit.Name})instance).{MethodName}()");
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// MCPServers are NOT containers at source-gen time.
    /// </summary>
    public override string? GenerateContainerCode() => null;

    /// <summary>
    /// Gets additional metadata properties for this MCP server.
    /// </summary>
    public override Dictionary<string, object> GetAdditionalProperties()
    {
        var props = base.GetAdditionalProperties();
        props["IsMCPServer"] = true;
        props["IsContainer"] = false;
        props["ParentToolkit"] = ParentToolkitName;
        props["CollapseWithinToolkit"] = CollapseWithinToolkit;

        if (FromManifest != null)
        {
            props["FromManifest"] = FromManifest;
            props["ManifestServerName"] = ManifestServerName ?? Name;
        }

        return props;
    }

    // ========== Helper Methods ==========

    private static string EscapeString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        return input.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
    }
}
