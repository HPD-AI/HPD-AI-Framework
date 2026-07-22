// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

namespace HPD.Agent.ClientTools;

/// <summary>
/// Defines a skill that guides the agent through a complex workflow.
/// Skills are semantic groupings of tools with workflow instructions.
/// </summary>
/// <param name="Name">Skill name (becomes AIFunction name, shown to agent)</param>
/// <param name="Description">Shown BEFORE activation - helps agent decide whether to use skill</param>
/// <param name="Instructions">Authoritative instructions returned when the skill activates.</param>
/// <param name="Reinforcement">Optional higher-priority reinforcement while the skill remains active.</param>
/// <param name="References">Tool references - these become visible when skill is activated</param>
public record ClientSkillDefinition(
    string Name,
    string Description,
    string Instructions,
    string? Reinforcement = null,
    IReadOnlyList<ClientSkillReference>? References = null
)
{
    /// <summary>
    /// Validates the skill definition.
    /// </summary>
    /// <exception cref="ArgumentException">If validation fails</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Skill name is required", nameof(Name));

        if (string.IsNullOrWhiteSpace(Description))
            throw new ArgumentException("Skill description is required", nameof(Description));

        if (string.IsNullOrWhiteSpace(Instructions))
            throw new ArgumentException("Skill instructions are required.", nameof(Instructions));

    }

    /// <summary>
    /// Validates skill references against registered ToolHarnesses.
    /// </summary>
    /// <param name="parentToolHarnessName">Name of the ToolHarness containing this skill</param>
    /// <param name="RegisteredToolHarnesses">All registered ToolHarnesses by name</param>
    /// <exception cref="ArgumentException">If a reference is invalid</exception>
    public void ValidateReferences(
        string parentToolHarnessName,
        IReadOnlyDictionary<string, clientToolHarnessDefinition> RegisteredToolHarnesses)
    {
        if (References == null) return;

        // Get tools from parent ToolHarness
        if (!RegisteredToolHarnesses.TryGetValue(parentToolHarnessName, out var parentToolHarness))
        {
            throw new ArgumentException(
                $"Skill '{Name}' belongs to ToolHarness '{parentToolHarnessName}' which is not registered.");
        }

        var localToolNames = parentToolHarness.Tools.Select(t => t.Name).ToHashSet();

        foreach (var reference in References)
        {
            if (string.IsNullOrEmpty(reference.ToolsetName))
            {
                // Local reference - tool must be in parent ToolHarness
                if (!localToolNames.Contains(reference.ToolName))
                {
                    throw new ArgumentException(
                        $"Skill '{Name}' references tool '{reference.ToolName}' " +
                        $"which does not exist in ToolHarness '{parentToolHarnessName}'");
                }
            }
            else
            {
                // Cross-ToolHarness reference - verify target ToolHarness and tool exist
                if (!RegisteredToolHarnesses.TryGetValue(reference.ToolsetName, out var targetToolHarness))
                {
                    throw new ArgumentException(
                        $"Skill '{Name}' in ToolHarness '{parentToolHarnessName}' references " +
                        $"ToolHarness '{reference.ToolsetName}' which is not registered.");
                }

                var toolExists = targetToolHarness.Tools.Any(t => t.Name == reference.ToolName);
                if (!toolExists)
                {
                    throw new ArgumentException(
                        $"Skill '{Name}' in ToolHarness '{parentToolHarnessName}' references " +
                        $"tool '{reference.ToolName}' in ToolHarness '{reference.ToolsetName}', " +
                        $"but that tool does not exist.");
                }
            }
        }
    }
}

/// <summary>
/// Reference to a tool that becomes visible when the skill is activated.
/// </summary>
/// <param name="ToolName">Name of the tool to reference</param>
/// <param name="ToolsetName">ToolHarness containing the tool. If null, uses the skill's parent ToolHarness</param>
public record ClientSkillReference(
    string ToolName,
    string? ToolsetName = null
);
