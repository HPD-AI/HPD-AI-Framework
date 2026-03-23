using System.Collections.Generic;
using System.Text;

namespace HPD.Agent.SourceGenerator.Capabilities;

/// <summary>
/// Represents an OpenAPI spec capability — a method that returns OpenApiConfig
/// to register an OpenAPI spec source when the toolkit is loaded.
/// Decorated with [OpenApi] attribute.
///
/// Unlike MCPServer, OpenAPI capabilities are collected via a generated
/// CollectOpenApiSources static method (not a static property) because
/// the config is returned by calling the method on the toolkit instance.
/// </summary>
internal class OpenApiCapability : BaseCapability
{
    public override CapabilityType Type => CapabilityType.OpenApi;
    public override bool IsContainer => false; // Container created at runtime if CollapseWithinToolkit=true
    public override bool EmitsIntoCreateTools => false;  // OpenAPI sources collected via CollectOpenApiSources
    public override bool RequiresInstance => !IsStatic;

    // ========== OpenApi-Specific Properties ==========

    /// <summary>
    /// Method name (e.g., "PetStore", "GitHub")
    /// </summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>
    /// Whether this OpenAPI method is static.
    /// </summary>
    public bool IsStatic { get; set; }

    /// <summary>
    /// Optional prefix for generated function names from the [OpenApi(Prefix = "...")] argument.
    /// If null, the method name is used as the prefix.
    /// Functions are named: {Prefix}_{OperationId}
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    /// Whether [RequiresPermission] attribute is present on the method.
    /// When true, AgentBuilder sets config.RequiresPermission = true at build time.
    /// </summary>
    public bool RequiresPermission { get; set; }

    // ========== Code Generation ==========

    /// <summary>
    /// Generates the collector invocation for this OpenAPI source.
    /// Unlike MCPServer (which emits a static property), OpenAPI sources are collected
    /// via a delegate passed to CollectOpenApiSources().
    ///
    /// The generated code calls the [OpenApi] method on the toolkit instance
    /// and passes the result to the collector action.
    /// Signature: collector(name, config, parentContainer)
    /// </summary>
    public override string GenerateRegistrationCode(object parent)
    {
        var toolkit = (ToolkitInfo)parent;

        // Effective name: Prefix ?? MethodName
        var effectiveName = Prefix ?? MethodName;

        if (IsStatic)
        {
            return $"__openApiCollector(\"{EscapeString(effectiveName)}\", {toolkit.Name}.{MethodName}(), \"{EscapeString(toolkit.Name)}\");";
        }
        else
        {
            return $"__openApiCollector(\"{EscapeString(effectiveName)}\", (({toolkit.Name})__instance).{MethodName}(), \"{EscapeString(toolkit.Name)}\");";
        }
    }

    /// <summary>
    /// OpenAPI specs are NOT containers at source-gen time.
    /// </summary>
    public override string? GenerateContainerCode() => null;

    /// <summary>
    /// Gets additional metadata properties for this OpenAPI source.
    /// </summary>
    public override Dictionary<string, object> GetAdditionalProperties()
    {
        var props = base.GetAdditionalProperties();
        props["IsOpenApi"] = true;
        props["IsContainer"] = false;
        props["ParentToolkit"] = ParentToolkitName;
        if (Prefix != null)
            props["Prefix"] = Prefix;
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
