// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.ClientTools;

/// <summary>
/// Defines a skill that guides the agent through a complex workflow.
/// Skills are semantic groupings of tools with workflow instructions.
/// </summary>
/// <param name="Name">Skill name (becomes AIFunction name, shown to agent)</param>
/// <param name="Description">Shown BEFORE activation - helps agent decide whether to use skill</param>
/// <param name="FunctionResult">Ephemeral instructions returned in function result when skill is activated (one-time)</param>
/// <param name="SystemPrompt">Persistent instructions injected into system prompt after activation (every iteration)</param>
/// <param name="References">Tool references - these become visible when skill is activated</param>
public record ClientSkillDefinition(
    string Name,
    string Description,
    string? FunctionResult = null,
    string? SystemPrompt = null,
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

        if (string.IsNullOrWhiteSpace(FunctionResult) && string.IsNullOrWhiteSpace(SystemPrompt))
            throw new ArgumentException(
                "At least one of FunctionResult or SystemPrompt must be provided. " +
                "FunctionResult is shown once when skill is activated. " +
                "SystemPrompt is injected into the system prompt persistently.",
                nameof(FunctionResult));

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
