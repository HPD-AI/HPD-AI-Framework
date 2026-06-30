// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Collections.Immutable;
using System.Text.Json;
using HPD.Agent.ClientTools;

namespace HPD.Agent;

/// <summary>
/// State for Client tool middleware. Tracks registered ToolHarnesses, visibility,
/// and pending augmentations during the current message turn.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b></para>
/// <para>
/// This state is immutable and flows through the context.
/// It is NOT stored in middleware instance fields, preserving thread safety
/// for concurrent RunAsync() calls.
/// </para>
///
/// <para><b>Usage:</b></para>
/// <code>
/// // Read state
/// var ftState = context.State.MiddlewareState.ClientTool ?? new();
/// var isExpanded = ftState.ExpandedToolHarnesses.Contains("ECommerceToolHarness");
///
/// // Update state
/// context.UpdateState(s => s with
/// {
///     MiddlewareState = s.MiddlewareState.WithClientTool(
///         ftState.WithExpandedToolHarness("ECommerceToolHarness"))
/// });
/// </code>
///
/// <para><b>Lifecycle:</b></para>
/// <para>
/// - RegisteredToolHarnesses persist across message turns (unless ResetClientState=true)
/// - ExpandedToolHarnesses and HiddenTools can be modified via augmentation
/// - PendingAugmentation is applied at the start of each iteration
/// </para>
/// </remarks>
[MiddlewareState]
public sealed record ClientToolStateData
{
    /// <summary>
    /// Registered ToolHarnesses (source of truth for tools).
    /// Key is ToolHarness name, value is the ToolHarness definition.
    /// </summary>
    public ImmutableDictionary<string, clientToolHarnessDefinition> RegisteredToolHarnesses { get; init; }
        = ImmutableDictionary<string, clientToolHarnessDefinition>.Empty;

    /// <summary>
    /// ToolHarnesses that are currently expanded (showing their tools).
    /// </summary>
    public ImmutableHashSet<string> ExpandedToolHarnesses { get; init; }
        = ImmutableHashSet<string>.Empty;

    /// <summary>
    /// Tools that are currently hidden (not visible to LLM but still registered).
    /// </summary>
    public ImmutableHashSet<string> HiddenTools { get; init; }
        = ImmutableHashSet<string>.Empty;

    /// <summary>
    /// Context items provided by Client.
    /// Key is the effective key (Key property or Description if Key is null).
    /// </summary>
    public ImmutableDictionary<string, ContextItem> Context { get; init; }
        = ImmutableDictionary<string, ContextItem>.Empty;

    /// <summary>
    /// Application state from the Client.
    /// Opaque to the agent but available to tools.
    /// </summary>
    public JsonElement? State { get; init; }

    /// <summary>
    /// Pending augmentation to apply at the start of next iteration.
    /// Set when a Client tool response includes augmentation data.
    /// </summary>
    public ClientToolAugmentation? PendingAugmentation { get; init; }

    // ========== ToolHarness METHODS ==========

    /// <summary>
    /// Registers a new ToolHarness.
    /// </summary>
    public ClientToolStateData WithRegisteredToolHarness(clientToolHarnessDefinition ToolHarness)
    {
        return this with
        {
            RegisteredToolHarnesses = RegisteredToolHarnesses.SetItem(ToolHarness.Name, ToolHarness)
        };
    }

    /// <summary>
    /// Removes a registered ToolHarness.
    /// </summary>
    public ClientToolStateData WithoutRegisteredToolHarness(string toolName)
    {
        return this with
        {
            RegisteredToolHarnesses = RegisteredToolHarnesses.Remove(toolName),
            ExpandedToolHarnesses = ExpandedToolHarnesses.Remove(toolName)
        };
    }

    /// <summary>
    /// Marks a ToolHarness as expanded.
    /// </summary>
    public ClientToolStateData WithExpandedToolHarness(string toolName)
    {
        return this with
        {
            ExpandedToolHarnesses = ExpandedToolHarnesses.Add(toolName)
        };
    }

    /// <summary>
    /// Marks a ToolHarness as collapsed.
    /// </summary>
    public ClientToolStateData WithCollapsedToolHarness(string toolName)
    {
        return this with
        {
            ExpandedToolHarnesses = ExpandedToolHarnesses.Remove(toolName)
        };
    }

    // ========== TOOL VISIBILITY METHODS ==========

    /// <summary>
    /// Hides a tool.
    /// </summary>
    public ClientToolStateData WithHiddenTool(string toolName)
    {
        return this with
        {
            HiddenTools = HiddenTools.Add(toolName)
        };
    }

    /// <summary>
    /// Shows a previously hidden tool.
    /// </summary>
    public ClientToolStateData WithVisibleTool(string toolName)
    {
        return this with
        {
            HiddenTools = HiddenTools.Remove(toolName)
        };
    }

    // ========== CONTEXT METHODS ==========

    /// <summary>
    /// Adds or updates a context item.
    /// </summary>
    public ClientToolStateData WithContextItem(ContextItem item)
    {
        return this with
        {
            Context = Context.SetItem(item.EffectiveKey, item)
        };
    }

    /// <summary>
    /// Adds multiple context items.
    /// </summary>
    public ClientToolStateData WithContext(IEnumerable<ContextItem> items)
    {
        var builder = Context.ToBuilder();
        foreach (var item in items)
        {
            builder[item.EffectiveKey] = item;
        }
        return this with { Context = builder.ToImmutable() };
    }

    /// <summary>
    /// Removes a context item.
    /// </summary>
    public ClientToolStateData WithouTMetadata(string key)
    {
        return this with
        {
            Context = Context.Remove(key)
        };
    }

    /// <summary>
    /// Clears all context items.
    /// </summary>
    public ClientToolStateData ClearContext()
    {
        return this with
        {
            Context = ImmutableDictionary<string, ContextItem>.Empty
        };
    }

    // ========== STATE METHODS ==========

    /// <summary>
    /// Updates the application state.
    /// </summary>
    public ClientToolStateData WithState(JsonElement? state)
    {
        return this with { State = state };
    }

    // ========== AUGMENTATION METHODS ==========

    /// <summary>
    /// Sets the pending augmentation to apply at next iteration.
    /// </summary>
    public ClientToolStateData WithPendingAugmentation(ClientToolAugmentation? augmentation)
    {
        return this with { PendingAugmentation = augmentation };
    }

    /// <summary>
    /// Clears the pending augmentation.
    /// </summary>
    public ClientToolStateData ClearPendingAugmentation()
    {
        return this with { PendingAugmentation = null };
    }
}
