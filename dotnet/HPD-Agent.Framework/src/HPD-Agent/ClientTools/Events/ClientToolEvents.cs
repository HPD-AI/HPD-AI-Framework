// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json.Serialization;
using HPD.Events;

namespace HPD.Agent.ClientTools;

/// <summary>
/// Emitted by agent when a Client tool needs to be invoked.
/// The middleware detects Client tools and emits this event.
/// Client must respond with <see cref="ClientToolInvokeResponseEvent"/>.
/// </summary>
/// <param name="RequestId">Unique identifier for this request (used to correlate response)</param>
/// <param name="ToolName">Name of the tool to invoke</param>
/// <param name="CallId">The function call ID from the LLM</param>
/// <param name="Arguments">Arguments to pass to the tool</param>
/// <param name="Description">Optional description of the tool (for debugging)</param>
public record ClientToolInvokeRequestEvent(
    string RequestId,
    string ToolName,
    string CallId,
    IReadOnlyDictionary<string, object?> Arguments,
    string? Description = null
) : AgentEvent, IAgentRequestEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override EventKind Kind { get; init; } = EventKind.Control;
    public string SourceName => "HPD.Agent.ClientTools";
    public ResponsePolicy ResponsePolicy => ResponsePolicy.TargetedResponder;
    public ResponderTarget? Target { get; init; }
    public RequestVisibility Visibility { get; init; } = RequestVisibility.AllObservers;
}

/// <summary>
/// Response from Client after executing a tool.
/// Supports rich content types: text, binary (images/files), JSON.
/// </summary>
/// <param name="RequestId">Must match the RequestId from the corresponding request</param>
/// <param name="Content">The tool result content (text, binary, or JSON)</param>
/// <param name="Success">Whether the tool execution succeeded</param>
/// <param name="ErrorMessage">Error message if Success is false</param>
/// <param name="Augmentation">Optional state changes to apply before next iteration</param>
[method: JsonConstructor]
public record ClientToolInvokeResponseEvent(
    string RequestId,
    IReadOnlyList<IToolResultContent> Content,
    bool Success = true,
    string? ErrorMessage = null,
    ClientToolAugmentation? Augmentation = null
) : AgentEvent, IAgentResponseEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override EventKind Kind { get; init; } = EventKind.Control;
    public override EventDirection Direction { get; init; } = EventDirection.Upstream;
    public string SourceName => "HPD.Agent.ClientTools";
    public string? ResponderId { get; init; }
    public string? ResponderGroup { get; init; }
    public HashSet<string> Capabilities { get; init; } = [];
    IReadOnlySet<string> IResponseEvent.Capabilities => Capabilities;

    /// <summary>
    /// Convenience constructor for simple text results.
    /// </summary>
    public ClientToolInvokeResponseEvent(
        string requestId,
        string textResult,
        bool success = true,
        string? errorMessage = null,
        ClientToolAugmentation? augmentation = null)
        : this(requestId, new IToolResultContent[] { new TextContent(textResult) }, success, errorMessage, augmentation)
    { }

    /// <summary>
    /// Convenience constructor for single content item.
    /// </summary>
    public ClientToolInvokeResponseEvent(
        string requestId,
        IToolResultContent content,
        bool success = true,
        string? errorMessage = null,
        ClientToolAugmentation? augmentation = null)
        : this(requestId, new[] { content }, success, errorMessage, augmentation)
    { }
}

/// <summary>
/// Emitted after Client ToolHarnesses are successfully registered.
/// Useful for debugging and observability.
/// </summary>
/// <param name="RegisteredToolHarnesses">Names of all registered tool groups</param>
/// <param name="TotalTools">Total number of tools across all tool groups</param>
/// <param name="Timestamp">When registration completed</param>
public record clientToolHarnessesRegisteredEvent(
    IReadOnlyList<string>RegisteredToolHarnesses,
    int TotalTools,
    DateTimeOffset Timestamp
) : AgentEvent
{
    public override EventKind Kind { get; init; } = EventKind.Diagnostic;
}
