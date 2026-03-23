// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;

namespace HPD.Agent.ClientTools;

/// <summary>
/// Input configuration for agent execution with Client tool support.
/// Tools are always registered via Toolkits (containers), matching HPD's C# Toolkit model.
/// For "standalone" tools without grouping, use a default Toolkit with StartCollapsed=false.
/// </summary>
public record AgentClientInput
{
    /// <summary>
    /// Client Toolkit containers to register for this message turn.
    /// Each Toolkit contains tools that can be collapsed/expanded together.
    /// This is the ONLY way to register Client tools - matching HPD's Toolkit-centric model.
    /// </summary>
    public IReadOnlyList<clientToolKitDefinition>? clientToolKits { get; init; }

    /// <summary>
    /// Containers that should start in expanded state.
    /// By default, Toolkits with StartCollapsed=true are collapsed.
    /// </summary>
    public IReadOnlySet<string>? ExpandedContainers { get; init; }

    /// <summary>
    /// Tools that should be hidden (not visible to LLM).
    /// </summary>
    public IReadOnlySet<string>? HiddenTools { get; init; }

    /// <summary>
    /// Runtime context to inject into the conversation.
    /// Each context item has a description (explaining what it is to the LLM)
    /// and a value (the actual data). This is richer than simple key-value pairs.
    /// </summary>
    /// <example>
    /// { Description: "Current user's timezone", Value: "America/New_York" }
    /// The description helps the LLM understand when/how to use this context.
    /// </example>
    public IReadOnlyList<ContextItem>? Context { get; init; }

    /// <summary>
    /// Application state from the Client.
    /// This is opaque to the agent but can be used by tools or passed back.
    /// Useful for page state, form data, UI state, etc.
    /// </summary>
    public JsonElement? State { get; init; }

    /// <summary>
    /// Custom metadata for application-specific use.
    /// Unlike State (which is Client app state), Metadata is for
    /// integration-level concerns (tracing, routing, feature flags, etc).
    /// </summary>
    public object? Metadata { get; init; }

    /// <summary>
    /// If true, clears all Client tool state from previous turns.
    /// Default: false (state persists across message turns).
    /// </summary>
    /// <remarks>
    /// <para><b>Use cases for persistence (default):</b></para>
    /// <list type="bullet">
    /// <item>Navigate to settings page in turn 1, settings tools available in turn 2</item>
    /// <item>User login in turn 1, admin tools remain available in turn 2</item>
    /// </list>
    /// <para><b>Use cases for reset:</b></para>
    /// <list type="bullet">
    /// <item>Starting a completely new workflow</item>
    /// <item>User explicitly requests "start fresh"</item>
    /// </list>
    /// </remarks>
    public bool ResetClientState { get; init; } = false;
}

/// <summary>
/// A context item with semantic description for the LLM.
/// Unlike simple key-value pairs, the description tells the LLM what this context means.
/// </summary>
/// <param name="Description">Human-readable description of what this context represents</param>
/// <param name="Value">The actual context value</param>
/// <param name="Key">Optional key for programmatic access (if not provided, uses description)</param>
public record ContextItem(
    string Description,
    string Value,
    string? Key = null
)
{
    /// <summary>
    /// Gets the effective key for this context item.
    /// Uses Key if provided, otherwise uses Description.
    /// </summary>
    public string EffectiveKey => Key ?? Description;
}
