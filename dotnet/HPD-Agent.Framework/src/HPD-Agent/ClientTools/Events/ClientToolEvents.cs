// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.Middleware;
using HPD.Events;

namespace HPD.Agent.ClientTools;

/// <summary>
/// Emitted by agent when a Client tool needs to be invoked.
/// The middleware detects Client tools and emits this event.
/// Client must respond with <see cref="ClientToolInvokeOutcomeEvent"/>.
/// </summary>
/// <param name="RequestId">Unique identifier for this request (used to correlate response)</param>
/// <param name="ToolName">Name of the tool to invoke</param>
/// <param name="CallId">The function call ID from the LLM</param>
/// <param name="Arguments">Arguments to pass to the tool</param>
/// <param name="Description">Optional description of the tool (for debugging)</param>
[HPD.Agent.Serialization.EventType("CLIENT_TOOL_INVOKE_REQUEST", Durability = HPD.Agent.Serialization.AgentEventDurability.Durable)]
public record ClientToolInvokeRequestEvent(
    string RequestId,
    string ToolName,
    string CallId,
    IReadOnlyDictionary<string, object?> Arguments,
    string? Description = null
) : AgentEvent, IAgentRequestEvent<ClientToolInvokeOutcomeEvent>
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override EventKind Kind { get; init; } = EventKind.Control;
    public string SourceName => "HPD.Agent.ClientTools";
    public ResponsePolicy ResponsePolicy => ResponsePolicy.TargetedResponder;
    public ResponderTarget? Target { get; init; }
    public RequestVisibility Visibility { get; init; } = RequestVisibility.AllObservers;
}

/// <summary>
/// Defines the immediate outcome of a client tool invocation request.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ClientToolInvokeOutcomeKind>))]
public enum ClientToolInvokeOutcomeKind
{
    /// <summary>The client completed the tool call during the initial request.</summary>
    Completed,

    /// <summary>The client accepted the request as background work that will complete later.</summary>
    AcceptedBackground,

    /// <summary>The client declined to perform the request.</summary>
    Rejected,

    /// <summary>The client failed while handling the request.</summary>
    Failed
}

/// <summary>
/// Immediate outcome from a client after it receives a client tool invocation request.
/// </summary>
[HPD.Agent.Serialization.EventType("CLIENT_TOOL_INVOKE_OUTCOME", Durability = HPD.Agent.Serialization.AgentEventDurability.Durable)]
public record ClientToolInvokeOutcomeEvent : AgentEvent, IAgentResponseEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override EventKind Kind { get; init; } = EventKind.Control;
    public override EventDirection Direction { get; init; } = EventDirection.Upstream;

    /// <summary>
    /// Gets the request id from the corresponding <see cref="ClientToolInvokeRequestEvent"/>.
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// Gets the immediate outcome kind.
    /// </summary>
    public required ClientToolInvokeOutcomeKind Outcome { get; init; }

    /// <summary>
    /// Gets the final content for completed outcomes or optional launch content for background outcomes.
    /// </summary>
    public IReadOnlyList<IToolResultContent>? Content { get; init; }

    /// <summary>
    /// Gets the error or rejection message when the outcome did not complete successfully.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Gets structured provider error information when available.</summary>
    public ClientToolError? Error { get; init; }

    /// <summary>
    /// Gets the client-owned id for accepted background work.
    /// </summary>
    public string? ClientOperationId { get; init; }

    /// <summary>
    /// Gets the provider operation kind when the accepted background operation is controllable.
    /// </summary>
    public AgentOperationKind? OperationKind { get; init; }

    /// <summary>
    /// Gets the controls supported by the provider operation.
    /// </summary>
    public AgentOperationCapabilities OperationCapabilities { get; init; } =
        AgentOperationCapabilities.None;

    /// <summary>
    /// Gets optional client tool state changes to apply before the next iteration.
    /// </summary>
    public ClientToolAugmentation? Augmentation { get; init; }

    public string SourceName => "HPD.Agent.ClientTools";
    public string? ResponderId { get; init; }
    public string? ResponderGroup { get; init; }
    [JsonConverter(typeof(ReadOnlyStringSetJsonConverter))]
    public IReadOnlySet<string> Capabilities { get; init; } = new HashSet<string>();
}

internal sealed class ReadOnlyStringSetJsonConverter : JsonConverter<IReadOnlySet<string>>
{
    public override IReadOnlySet<string> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("A responder capability set must be a JSON array.");

        var values = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String || reader.GetString() is not { } value)
                throw new JsonException("Responder capabilities must be strings.");
            values.Add(value);
        }
        return values;
    }

    public override void Write(
        Utf8JsonWriter writer,
        IReadOnlySet<string> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (string capability in value.Order(StringComparer.Ordinal))
            writer.WriteStringValue(capability);
        writer.WriteEndArray();
    }
}

/// <summary>
/// Terminal state reported by a client-owned background tool operation.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ClientToolOperationOutcomeState>))]
public enum ClientToolOperationOutcomeState
{
    /// <summary>The operation completed successfully.</summary>
    Completed,

    /// <summary>The operation faulted.</summary>
    Faulted,

    /// <summary>The operation was cancelled.</summary>
    Cancelled,

    /// <summary>
    /// The provider connection was lost after accepting the operation and
    /// completion cannot be proven. The operation must not be replayed.
    /// </summary>
    Unknown
}

/// <summary>
/// Input sent by a client when accepted background client-tool work reaches a terminal state.
/// </summary>
public sealed record ClientToolOperationOutcomeEvent : AgentInputEvent
{
    /// <summary>
    /// Gets the client-owned background operation id.
    /// </summary>
    public required string ClientOperationId { get; init; }

    /// <summary>
    /// Gets the terminal state reported by the client.
    /// </summary>
    public required ClientToolOperationOutcomeState State { get; init; }

    /// <summary>
    /// Gets the final content produced by the client operation when <see cref="State"/> is <see cref="ClientToolOperationOutcomeState.Completed"/>.
    /// </summary>
    public IReadOnlyList<IToolResultContent>? Content { get; init; }

    /// <summary>
    /// Gets optional client tool state changes to apply after completion.
    /// </summary>
    public ClientToolAugmentation? Augmentation { get; init; }

    /// <summary>
    /// Gets the error message when <see cref="State"/> is
    /// <see cref="ClientToolOperationOutcomeState.Faulted"/> or
    /// <see cref="ClientToolOperationOutcomeState.Unknown"/>.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the optional error type when <see cref="State"/> is
    /// <see cref="ClientToolOperationOutcomeState.Faulted"/> or
    /// <see cref="ClientToolOperationOutcomeState.Unknown"/>.
    /// </summary>
    public string? ErrorType { get; init; }

    /// <summary>
    /// Gets the optional cancellation reason when <see cref="State"/> is <see cref="ClientToolOperationOutcomeState.Cancelled"/>.
    /// </summary>
    public string? CancellationReason { get; init; }

    /// <summary>
    /// Gets optional terminal metadata.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
