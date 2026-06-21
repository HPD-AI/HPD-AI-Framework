using System.ComponentModel;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Extensions.AI;

namespace HPD.MultiAgent.Internal;

/// <summary>
/// Generates dynamic handoff tools based on configured targets.
/// Each handoff target gets a handoff_to_{targetId}() tool.
/// </summary>
internal static class HandoffToolGenerator
{
    /// <summary>
    /// Creates AIFunction tools for each handoff target.
    /// </summary>
    /// <param name="handoffTargets">Dictionary of target ID to description.</param>
    /// <returns>List of AIFunction tools for handoffs.</returns>
    public static IReadOnlyList<AIFunction> CreateHandoffTools(Dictionary<string, string> handoffTargets)
    {
        if (handoffTargets == null || handoffTargets.Count == 0)
            return Array.Empty<AIFunction>();

        var tools = new List<AIFunction>();

        foreach (var (targetId, description) in handoffTargets)
        {
            var toolName = $"handoff_to_{targetId}";
            var toolDescription = $"Hand off the conversation to {targetId}. {description}";

            // Create a simple AIFunction that returns the target ID
            var tool = AIFunctionFactory.Create(
                method: () => targetId,
                name: toolName,
                description: toolDescription
            );

            tools.Add(tool);
        }

        return tools;
    }

    /// <summary>
    /// Creates a handoff context with routing information.
    /// </summary>
    public static string CreateHandoffSystemPrompt(Dictionary<string, string> handoffTargets)
    {
        if (handoffTargets == null || handoffTargets.Count == 0)
            return string.Empty;

        var lines = new List<string>
        {
            "",
            "## Available Handoffs",
            "You MUST use one of the following handoff functions to route this request to the appropriate handler:",
            ""
        };

        foreach (var (targetId, description) in handoffTargets)
        {
            lines.Add($"- **handoff_to_{targetId}()**: {description}");
        }

        lines.Add("");
        lines.Add("Analyze the user's request and call the appropriate handoff function.");

        return string.Join("\n", lines);
    }
}

/// <summary>
/// Result of a handoff operation.
/// </summary>
internal record HandoffResult(string TargetId, string? Message = null);
