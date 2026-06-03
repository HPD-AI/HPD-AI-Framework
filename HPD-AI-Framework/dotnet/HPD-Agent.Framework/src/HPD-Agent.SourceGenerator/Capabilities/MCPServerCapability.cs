using System.Collections.Generic;
using System.Text;

namespace HPD.Agent.SourceGenerator.Capabilities;

/// <summary>
/// Represents an MCP server capability — a method that returns MCPServerConfig
/// to register an MCP server connection when the toolharness is loaded.
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
    /// When true, MCP tools sit behind their own container nested inside the parent toolharness.
    /// When false (default), MCP tools appear directly under the parent toolharness on expansion.
    /// </summary>
    public bool CollapseWithinToolHarness { get; set; }

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
        var toolharness = (ToolHarnessInfo)parent;
        var sb = new StringBuilder();

        sb.AppendLine("new HPD.Agent.MCP.MCPServerRegistration");
        sb.AppendLine("{");
        sb.AppendLine($"    Name = \"{EscapeString(Name)}\",");
        sb.AppendLine($"    Description = \"{EscapeString(Description)}\",");
        sb.AppendLine($"    ParentToolHarness = \"{toolharness.Name}\",");
        sb.AppendLine($"    CollapseWithinToolHarness = {CollapseWithinToolHarness.ToString().ToLower()},");

        if (FromManifest != null)
        {
            sb.AppendLine($"    FromManifest = \"{EscapeString(FromManifest)}\",");
            sb.AppendLine($"    ManifestServerName = \"{EscapeString(ManifestServerName ?? Name)}\",");
        }

        if (RequiresPermission)
            sb.AppendLine($"    RequiresPermissionOverride = true,");

        if (IsStatic)
        {
            sb.AppendLine($"    StaticConfigProvider = () => {toolharness.Name}.{MethodName}()");
        }
        else
        {
            sb.AppendLine($"    InstanceConfigProvider = (instance) => (({toolharness.Name})instance).{MethodName}()");
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Generates reflection-free core MCP source registration code.
    /// </summary>
    public string GenerateSourceCode(object parent)
    {
        var toolharness = (ToolHarnessInfo)parent;
        var provider = IsStatic
            ? $"static _ => {toolharness.Name}.{MethodName}()"
            : $"static instance => (({toolharness.Name})instance!).{MethodName}()";

        return
            "            __mcpCollector(new HPD.Agent.McpServerSource(\n" +
            $"                Name: \"{EscapeString(Name)}\",\n" +
            $"                Description: \"{EscapeString(Description)}\",\n" +
            $"                ParentToolHarness: \"{toolharness.Name}\",\n" +
            $"                CollapseWithinToolHarness: {CollapseWithinToolHarness.ToString().ToLower()},\n" +
            $"                FromManifest: {(FromManifest is null ? "null" : $"\"{EscapeString(FromManifest)}\"")},\n" +
            $"                ManifestServerName: {(FromManifest is null ? "null" : $"\"{EscapeString(ManifestServerName ?? Name)}\"")},\n" +
            $"                RequiresPermissionOverride: {(RequiresPermission ? "true" : "null")},\n" +
            $"                ConfigProvider: {provider}));";
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
        props["ParentToolHarness"] = ParentToolHarnessName;
        props["CollapseWithinToolHarness"] = CollapseWithinToolHarness;

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
