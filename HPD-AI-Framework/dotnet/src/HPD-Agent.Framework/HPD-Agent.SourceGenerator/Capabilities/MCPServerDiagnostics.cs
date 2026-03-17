using Microsoft.CodeAnalysis;

namespace HPD.Agent.SourceGenerator.Capabilities;

/// <summary>
/// Diagnostic descriptors for [MCPServer] capability analysis.
/// </summary>
internal static class MCPServerDiagnostics
{
    /// <summary>
    /// HPDAG0301: [MCPServer] method has invalid return type.
    /// Must return MCPServerConfig or MCPServerConfig?.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidReturnType = new(
        id: "HPDAG0301",
        title: "Invalid MCPServer return type",
        messageFormat: "[MCPServer] method '{0}' must return MCPServerConfig or MCPServerConfig?, but returns '{1}'",
        category: "HPDAgent.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Methods marked with [MCPServer] must return MCPServerConfig (inline config) or MCPServerConfig? (manifest reference). " +
                    "The source generator uses the return type to determine the config mode.");

    /// <summary>
    /// HPDAG0302: [MCPServer] method has conflicting capability attributes.
    /// Cannot combine with [AIFunction], [Skill], [SubAgent], or [MultiAgent].
    /// </summary>
    public static readonly DiagnosticDescriptor ConflictingAttributes = new(
        id: "HPDAG0302",
        title: "Conflicting capability attributes on MCPServer",
        messageFormat: "[MCPServer] method '{0}' cannot be combined with [{1}]. Use only one capability attribute per method.",
        category: "HPDAgent.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A method can only have one capability attribute. [MCPServer] cannot be combined with [AIFunction], [Skill], [SubAgent], or [MultiAgent]. " +
                    "Each capability type has different execution semantics and return type requirements.");

    /// <summary>
    /// HPDAG0303: [MCPServer] uses FromManifest but manifest file not found.
    /// Warning since file existence can't always be validated at compile time.
    /// </summary>
    public static readonly DiagnosticDescriptor ManifestFileNotFound = new(
        id: "HPDAG0303",
        title: "MCP manifest file not found",
        messageFormat: "[MCPServer] method '{0}' uses FromManifest but manifest file '{1}' was not found",
        category: "HPDAgent.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The FromManifest property references a manifest file that could not be found at compile time. " +
                    "Ensure the file exists at the specified path relative to the project directory.");

    /// <summary>
    /// HPDAG0304: [MCPServer] references server not found in manifest.
    /// Warning since manifest contents may change.
    /// </summary>
    public static readonly DiagnosticDescriptor ServerNotFoundInManifest = new(
        id: "HPDAG0304",
        title: "MCP server not found in manifest",
        messageFormat: "[MCPServer] method '{0}' references server '{1}' which was not found in manifest '{2}'",
        category: "HPDAgent.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The ServerName parameter references a server that was not found in the specified manifest file. " +
                    "Ensure the server name matches an entry in the manifest's servers array.");
}
