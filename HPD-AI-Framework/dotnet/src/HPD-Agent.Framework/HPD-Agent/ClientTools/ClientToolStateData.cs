// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Collections.Immutable;
using System.Text.Json;
using HPD.Agent.ClientTools;

namespace HPD.Agent;

/// <summary>
/// State for Client tool middleware. Tracks registered Toolkits, visibility,
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
/// var isExpanded = ftState.ExpandedToolkits.Contains("ECommerceToolkit");
///
/// // Update state
/// context.UpdateState(s => s with
/// {
///     MiddlewareState = s.MiddlewareState.WithClientTool(
///         ftState.WithExpandedToolkit("ECommerceToolkit"))
/// });
/// </code>
///
/// <para><b>Lifecycle:</b></para>
/// <para>
/// - RegisteredToolKits persist across message turns (unless ResetClientState=true)
/// - ExpandedToolkits and HiddenTools can be modified via augmentation
/// - PendingAugmentation is applied at the start of each iteration
/// </para>
/// </remarks>
[MiddlewareState]
public sealed record ClientToolStateData
{
    /// <summary>
    /// Registered Toolkits (source of truth for tools).
    /// Key is Toolkit name, value is the Toolkit definition.
    /// </summary>
    public ImmutableDictionary<string, clientToolKitDefinition> RegisteredToolKits { get; init; }
        = ImmutableDictionary<string, clientToolKitDefinition>.Empty;

    /// <summary>
    /// Toolkits that are currently expanded (showing their tools).
    /// </summary>
    public ImmutableHashSet<string> ExpandedToolkits { get; init; }
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

    // ========== Toolkit METHODS ==========

    /// <summary>
    /// Registers a new Toolkit.
    /// </summary>
    public ClientToolStateData WithRegisteredToolkit(clientToolKitDefinition Toolkit)
    {
        return this with
        {
            RegisteredToolKits = RegisteredToolKits.SetItem(Toolkit.Name, Toolkit)
        };
    }

    /// <summary>
    /// Removes a registered Toolkit.
    /// </summary>
    public ClientToolStateData WithoutRegisteredToolkit(string toolName)
    {
        return this with
        {
            RegisteredToolKits = RegisteredToolKits.Remove(toolName),
            ExpandedToolkits = ExpandedToolkits.Remove(toolName)
        };
    }

    /// <summary>
    /// Marks a Toolkit as expanded.
    /// </summary>
    public ClientToolStateData WithExpandedToolkit(string toolName)
    {
        return this with
        {
            ExpandedToolkits = ExpandedToolkits.Add(toolName)
        };
    }

    /// <summary>
    /// Marks a Toolkit as collapsed.
    /// </summary>
    public ClientToolStateData WithCollapsedToolkit(string toolName)
    {
        return this with
        {
            ExpandedToolkits = ExpandedToolkits.Remove(toolName)
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
