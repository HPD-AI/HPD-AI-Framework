// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;

namespace HPD.Agent.ClientTools;

/// <summary>
/// Defines a tool that executes on the Client.
/// Mirrors the structure Clients provide: name, description, parameters (JSON Schema).
/// Tools are always registered inside a <see cref="clientToolKitDefinition"/> (container).
/// </summary>
/// <param name="Name">Unique name for the tool (used in function calls)</param>
/// <param name="Description">Human-readable description shown to the LLM</param>
/// <param name="ParametersSchema">JSON Schema defining the tool's parameters</param>
/// <param name="RequiresPermission">Whether this tool requires permission before execution (uses existing PermissionMiddleware)</param>
public record ClientToolDefinition(
    string Name,
    string Description,
    JsonElement ParametersSchema,
    bool RequiresPermission = false
)
{
    /// <summary>
    /// Validates the tool definition.
    /// </summary>
    /// <exception cref="ArgumentException">If name or description is empty</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Tool name is required", nameof(Name));

        if (string.IsNullOrWhiteSpace(Description))
            throw new ArgumentException("Tool description is required", nameof(Description));
    }
}
