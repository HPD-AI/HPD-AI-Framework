// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.ClientTools;

/// <summary>
/// Defines a ToolHarness container with multiple tools.
/// Similar to [Collapse] attribute for C# ToolHarnesses.
/// </summary>
/// <remarks>
/// <para><b>Important:</b> If <paramref name="StartCollapsed"/> is true, <paramref name="Description"/> is REQUIRED.</para>
/// <para>The description tells the LLM when to expand this container. Without a description, the LLM won't know what tools are inside.</para>
/// </remarks>
/// <param name="Name">Unique name for the ToolHarness container</param>
/// <param name="Description">Description shown to LLM (REQUIRED if StartCollapsed=true)</param>
/// <param name="Tools">Tools contained in this ToolHarness</param>
/// <param name="Skills">Optional skills that reference tools in this or other ToolHarnesses</param>
/// <param name="FunctionResult">Ephemeral instructions returned in function result when container is expanded</param>
/// <param name="SystemPrompt">Persistent instructions injected into system prompt after expansion</param>
/// <param name="StartCollapsed">Whether container starts collapsed (default: true)</param>
public record clientToolHarnessDefinition(
    string Name,
    string? Description,
    IReadOnlyList<ClientToolDefinition> Tools,
    IReadOnlyList<ClientSkillDefinition>? Skills = null,
    string? FunctionResult = null,
    string?SystemPrompt = null,
    bool StartCollapsed = true
)
{
    /// <summary>
    /// Validates the ToolHarness definition.
    /// Throws if StartCollapsed=true but no Description provided.
    /// </summary>
    /// <exception cref="ArgumentException">If validation fails</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("ToolHarness name is required", nameof(Name));

        if (Tools == null || Tools.Count == 0)
            throw new ArgumentException("ToolHarness must contain at least one tool", nameof(Tools));

        if (StartCollapsed && string.IsNullOrWhiteSpace(Description))
            throw new ArgumentException(
                $"ToolHarness '{Name}' has StartCollapsed=true but no Description. " +
                "A description is required for collapsed ToolHarnesses so the LLM knows when to expand them.",
                nameof(Description));

        // Validate each tool
        foreach (var tool in Tools)
        {
            tool.Validate();
        }

        // Validate skills if present
        if (Skills != null)
        {
            foreach (var skill in Skills)
            {
                skill.Validate();
            }
        }
    }

    /// <summary>
    /// Validates skill references against registered ToolHarnesses.
    /// Call this after all ToolHarnesses are registered to validate cross-ToolHarness references.
    /// </summary>
    /// <param name="RegisteredToolHarnesses">All registered ToolHarnesses by name</param>
    /// <exception cref="ArgumentException">If a skill references a non-existent tool</exception>
    public void ValidateSkillReferences(IReadOnlyDictionary<string, clientToolHarnessDefinition> RegisteredToolHarnesses)
    {
        if (Skills == null) return;

        foreach (var skill in Skills)
        {
            skill.ValidateReferences(Name, RegisteredToolHarnesses);
        }
    }
}
