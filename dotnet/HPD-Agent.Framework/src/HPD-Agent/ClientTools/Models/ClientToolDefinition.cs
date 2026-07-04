// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json;

namespace HPD.Agent.ClientTools;

/// <summary>
/// Defines a tool that executes on the Client.
/// Mirrors the structure Clients provide: name, description, parameters (JSON Schema).
/// Tools are always registered inside a <see cref="clientToolHarnessDefinition"/> (container).
/// </summary>
public sealed record ClientToolDefinition
{
    /// <summary>
    /// Gets the unique tool name used in function calls.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the human-readable description shown to the model.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets the JSON schema defining the tool parameters.
    /// </summary>
    public required JsonElement ParametersSchema { get; init; }

    /// <summary>
    /// Gets whether this tool requires permission before execution.
    /// </summary>
    public bool RequiresPermission { get; init; }

    /// <summary>
    /// Gets the invocation modes supported by this client tool.
    /// </summary>
    public AgentInvocationModePolicy InvocationModePolicy { get; init; } =
        AgentInvocationModePolicy.SynchronousOnly;

    /// <summary>
    /// Gets the notification rule used when this client tool accepts background work.
    /// </summary>
    public BackgroundTaskNotificationRule BackgroundNotification { get; init; } =
        new BackgroundTaskNotificationRule.OnFinalStateRule(
            Completed: true,
            Faulted: true);

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
