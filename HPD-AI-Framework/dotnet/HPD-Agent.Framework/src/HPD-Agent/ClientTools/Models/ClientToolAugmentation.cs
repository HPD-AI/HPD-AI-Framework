// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;

namespace HPD.Agent.ClientTools;

/// <summary>
/// Augmentation data that modifies tool state for the next iteration.
/// All tool changes happen at the Toolkit level - tools are always inside Toolkits.
/// </summary>
/// <remarks>
/// <para>This enables dynamic scenarios like page navigation:</para>
/// <code>
/// Iteration 1: Agent has [PageA Toolkit with tools]
///     -> Agent calls navigate_to_page("settings")
///     -> Client responds with:
///        {
///          result: "Navigated to settings",
///          augmentation: {
///            removeToolkits: ["PageA"],
///            injectToolkits: [{ name: "Settings", tools: [...] }]
///          }
///        }
///
/// Iteration 2: Agent now has [Settings Toolkit with tools]
///     -> Toolkits changed mid-turn!
/// </code>
/// </remarks>
public record ClientToolAugmentation
{
    // ========== Toolkit CHANGES ==========

    /// <summary>
    /// New Toolkits to inject (with their tools).
    /// This is the ONLY way to add new tools - they must be inside a Toolkit.
    /// </summary>
    public IReadOnlyList<clientToolKitDefinition>? InjectToolkits { get; init; }

    /// <summary>
    /// Toolkit names to remove entirely (removes Toolkit and all its tools).
    /// </summary>
    public IReadOnlySet<string>? RemoveToolkits { get; init; }

    /// <summary>
    /// Toolkit names to expand (show their tools).
    /// </summary>
    public IReadOnlySet<string>? ExpandToolkits { get; init; }

    /// <summary>
    /// Toolkit names to collapse (hide their tools, show container function).
    /// </summary>
    public IReadOnlySet<string>? CollapseToolkits { get; init; }

    // ========== TOOL VISIBILITY ==========

    /// <summary>
    /// Tool names to hide (not visible to LLM but still registered).
    /// </summary>
    public IReadOnlySet<string>? HideTools { get; init; }

    /// <summary>
    /// Tool names to show (unhide previously hidden tools).
    /// </summary>
    public IReadOnlySet<string>? ShowTools { get; init; }

    // ========== CONTEXT ==========

    /// <summary>
    /// Context items to add/update.
    /// Each item has a description (for LLM) and value (the data).
    /// </summary>
    public IReadOnlyList<ContextItem>? AddContext { get; init; }

    /// <summary>
    /// Context keys to remove.
    /// </summary>
    public IReadOnlySet<string>? RemoveContext { get; init; }

    // ========== STATE ==========

    /// <summary>
    /// Updated application state from the Client.
    /// If provided, replaces the current state entirely.
    /// </summary>
    public JsonElement? UpdateState { get; init; }

    /// <summary>
    /// Partial state update (merged with existing state).
    /// Use this for incremental updates instead of full replacement.
    /// </summary>
    public JsonElement? PatchState { get; init; }

    /// <summary>
    /// Returns true if this augmentation contains any changes.
    /// </summary>
    public bool HasChanges =>
        (InjectToolkits?.Count ?? 0) > 0 ||
        (RemoveToolkits?.Count ?? 0) > 0 ||
        (ExpandToolkits?.Count ?? 0) > 0 ||
        (CollapseToolkits?.Count ?? 0) > 0 ||
        (HideTools?.Count ?? 0) > 0 ||
        (ShowTools?.Count ?? 0) > 0 ||
        (AddContext?.Count ?? 0) > 0 ||
        (RemoveContext?.Count ?? 0) > 0 ||
        UpdateState.HasValue ||
        PatchState.HasValue;
}
