using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Shared helper for generating toolkit container descriptions and metadata.
/// Used by both HPDToolSourceGenerator and SkillCodeGenerator to avoid code duplication.
/// </summary>
internal static class ToolkitContainerHelper
{
    /// <summary>
    /// Generates a plain text description of the container's available functions.
    /// </summary>
    public static string GenerateMermaidFlow(string toolName, List<string> capabilities)
    {
        var functionList = string.Join(", ", capabilities);
        return $"Container {toolName} provides access to: {functionList}";
    }

    /// <summary>
    /// Generates the full container description including user description and Mermaid flow.
    /// </summary>
    public static string GenerateContainerDescription(string? userDescription, string toolName, List<string> capabilities)
    {
        var description = userDescription ?? string.Empty;
        var mermaidFlow = GenerateMermaidFlow(toolName, capabilities);
        return $"{mermaidFlow}. {description}";
    }

    /// <summary>
    /// Generates the return message shown after container expansion.
    /// </summary>
    public static string GenerateReturnMessage(string toolName, List<string> capabilities, string? postExpansionInstructions)
    {
        var capabilitiesList = string.Join(", ", capabilities);
        var returnMessage = $"{toolName} expanded. Available functions: {capabilitiesList}";

        if (!string.IsNullOrEmpty(postExpansionInstructions))
        {
            returnMessage += $"\n\n{postExpansionInstructions}";
        }

        return returnMessage;
    }
}
