// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Events;

namespace HPD.Agent.ClientTools;

/// <summary>
/// Emitted by agent when a Client tool needs to be invoked.
/// The middleware detects Client tools and emits this event.
/// Client must respond with <see cref="ClientToolInvokeResponseEvent"/>.
/// </summary>
/// <param name="RequestId">Unique identifier for this request (used to correlate response)</param>
/// <param name="SourceName">Name of the component emitting this event</param>
/// <param name="ToolName">Name of the tool to invoke</param>
/// <param name="CallId">The function call ID from the LLM</param>
/// <param name="Arguments">Arguments to pass to the tool</param>
/// <param name="Description">Optional description of the tool (for debugging)</param>
public record ClientToolInvokeRequestEvent(
    string RequestId,
    string SourceName,
    string ToolName,
    string CallId,
    IReadOnlyDictionary<string, object?> Arguments,
    string? Description = null
) : AgentEvent, IBidirectionalEvent
{
    public new HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;
}

/// <summary>
/// Response from Client after executing a tool.
/// Supports rich content types: text, binary (images/files), JSON.
/// </summary>
/// <param name="RequestId">Must match the RequestId from the corresponding request</param>
/// <param name="SourceName">Name of the component that processed this response</param>
/// <param name="Content">The tool result content (text, binary, or JSON)</param>
/// <param name="Success">Whether the tool execution succeeded</param>
/// <param name="ErrorMessage">Error message if Success is false</param>
/// <param name="Augmentation">Optional state changes to apply before next iteration</param>
public record ClientToolInvokeResponseEvent(
    string RequestId,
    string SourceName,
    IReadOnlyList<IToolResultContent> Content,
    bool Success = true,
    string? ErrorMessage = null,
    ClientToolAugmentation? Augmentation = null
) : AgentEvent, IBidirectionalEvent
{
    public new HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;
}

/// <summary>
/// Emitted after Client Toolkits are successfully registered.
/// Useful for debugging and observability.
/// </summary>
/// <param name="RegisteredToolKits">Names of all registered tool groups</param>
/// <param name="TotalTools">Total number of tools across all tool groups</param>
/// <param name="Timestamp">When registration completed</param>
public record clientToolKitsRegisteredEvent(
    IReadOnlyList<string> RegisteredToolKits,
    int TotalTools,
    DateTimeOffset Timestamp
) : AgentEvent;
