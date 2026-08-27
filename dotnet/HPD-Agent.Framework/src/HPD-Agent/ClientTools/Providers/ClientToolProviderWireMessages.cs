// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.Middleware;

namespace HPD.Agent.ClientTools;

/// <summary>
/// Initial message sent by a client tool provider after opening the provider websocket.
/// </summary>
public sealed record ClientToolProviderHelloMessage
{
    /// <summary>Gets the wire message type.</summary>
    public string Type { get; init; } = "provider.hello";

    /// <summary>Gets the provider protocol version.</summary>
    public string ProtocolVersion { get; init; } = "2";

    /// <summary>Gets the provider identity.</summary>
    public required ClientToolProviderIdentity Identity { get; init; }
}

/// <summary>
/// Welcome message returned by HPD after accepting a provider connection.
/// </summary>
public sealed record ClientToolProviderWelcomeMessage
{
    /// <summary>Gets the wire message type.</summary>
    public string Type { get; init; } = "provider.welcome";

    /// <summary>Gets HPD's runtime id for the connected provider.</summary>
    public required string ClientRuntimeId { get; init; }

    /// <summary>Gets HPD's connection id for this websocket.</summary>
    public required string ConnectionId { get; init; }

    /// <summary>Gets the heartbeat interval in milliseconds.</summary>
    public required int HeartbeatIntervalMs { get; init; }
}

/// <summary>
/// Manifest update sent by a connected client tool provider.
/// </summary>
public sealed record ClientToolProviderManifestMessage
{
    /// <summary>Gets the wire message type.</summary>
    public string Type { get; init; } = "provider.manifest";

    /// <summary>Gets the provider protocol version.</summary>
    public string ProtocolVersion { get; init; } = "2";

    /// <summary>Gets the logical app provider identity.</summary>
    public required ClientAppProviderDescriptor AppProvider { get; init; }

    /// <summary>Gets the current app context.</summary>
    public ClientToolProviderContext? Context { get; init; }

    /// <summary>Gets provider readiness.</summary>
    public ClientToolProviderReadiness Readiness { get; init; } = ClientToolProviderReadiness.Initializing;

    /// <summary>Gets tool harnesses advertised by this provider.</summary>
    public IReadOnlyList<clientToolHarnessDefinition> ClientToolHarnesses { get; init; } =
        Array.Empty<clientToolHarnessDefinition>();

    /// <summary>Gets provider metadata.</summary>
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
}

/// <summary>
/// Heartbeat sent by a connected provider.
/// </summary>
public sealed record ClientToolProviderHeartbeatMessage
{
    /// <summary>Gets the wire message type.</summary>
    public string Type { get; init; } = "provider.heartbeat";
}

/// <summary>
/// Message sent by a provider when it wants to release its current binding or close cleanly.
/// </summary>
public sealed record ClientToolProviderReleaseMessage
{
    /// <summary>Gets the wire message type.</summary>
    public string Type { get; init; } = "provider.release";

    /// <summary>Gets the release reason.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets the binding lease id the provider wants to release, if known.</summary>
    public string? BindingId { get; init; }
}

/// <summary>
/// Tool invocation sent by HPD to a bound provider connection.
/// </summary>
public sealed record ClientToolProviderInvokeToolMessage
{
    /// <summary>Gets the wire message type.</summary>
    public string Type { get; init; } = "provider.invoke";

    /// <summary>Gets the provider protocol version.</summary>
    public string ProtocolVersion { get; init; } = "2";

    /// <summary>Gets HPD's provider runtime id.</summary>
    public required string ClientRuntimeId { get; init; }

    /// <summary>Gets HPD's active connection id.</summary>
    public required string ConnectionId { get; init; }

    /// <summary>Gets the active binding lease id authorizing this invocation.</summary>
    public required string BindingId { get; init; }

    /// <summary>Gets the provider invocation id.</summary>
    public required string InvocationId { get; init; }

    /// <summary>Gets the client-tool request id.</summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// Gets the HPD-assigned stable operation id for a background invocation.
    /// Providers must return this exact id when accepting the operation.
    /// </summary>
    public string? ClientOperationId { get; init; }

    /// <summary>Gets the original provider-side tool name.</summary>
    public required string ToolName { get; init; }

    /// <summary>Gets the model-visible tool name exposed by HPD.</summary>
    public required string VisibleToolName { get; init; }

    /// <summary>Gets the model function call id.</summary>
    public required string CallId { get; init; }

    /// <summary>Gets the invocation arguments.</summary>
    public required IReadOnlyDictionary<string, object?> Arguments { get; init; }

    /// <summary>Gets the resolved compound operation.</summary>
    public ClientToolResolvedOperation? Operation { get; init; }

    /// <summary>Gets the provider context HPD expects for a fresh operation.</summary>
    public ClientToolProviderContext? ExpectedContext { get; init; }

    /// <summary>Gets the requested invocation mode.</summary>
    public AgentInvocationMode? RequestedInvocationMode { get; init; }

    /// <summary>Gets the invocation mode resolved by HPD policy.</summary>
    public required AgentInvocationMode ResolvedInvocationMode { get; init; }

    /// <summary>Gets the invocation deadline.</summary>
    public DateTimeOffset? Deadline { get; init; }
}

/// <summary>
/// Immediate outcome sent by a provider after an invocation request.
/// </summary>
public sealed record ClientToolProviderInvokeOutcomeMessage
{
    /// <summary>Gets the wire message type.</summary>
    public string Type { get; init; } = "provider.invokeOutcome";

    /// <summary>Gets the provider invocation id.</summary>
    public required string InvocationId { get; init; }

    /// <summary>Gets the active binding lease id that produced this outcome.</summary>
    public required string BindingId { get; init; }

    /// <summary>Gets the request id from the invocation.</summary>
    public required string RequestId { get; init; }

    /// <summary>Gets the immediate outcome kind.</summary>
    public required ClientToolInvokeOutcomeKind Outcome { get; init; }

    /// <summary>Gets final or launch content.</summary>
    public IReadOnlyList<IToolResultContent>? Content { get; init; }

    /// <summary>Gets the structured rejection or failure.</summary>
    public ClientToolError? Error { get; init; }

    /// <summary>Gets the HPD-assigned id for background work.</summary>
    public string? ClientOperationId { get; init; }

    /// <summary>Gets optional handle kind for background work.</summary>
    public AgentOperationKind? OperationKind { get; init; }

    /// <summary>Gets supported handle operations for background work.</summary>
    public AgentOperationCapabilities OperationCapabilities { get; init; } =
        AgentOperationCapabilities.None;

    /// <summary>Gets optional client tool augmentation.</summary>
    public ClientToolAugmentation? Augmentation { get; init; }
}

/// <summary>
/// Terminal outcome sent by a provider for background work it previously accepted.
/// </summary>
public sealed record ClientToolProviderOperationOutcomeMessage
{
    /// <summary>Gets the wire message type.</summary>
    public string Type { get; init; } = "provider.backgroundOperationOutcome";

    /// <summary>Gets the active binding lease id that owns this background operation.</summary>
    public required string BindingId { get; init; }

    /// <summary>Gets the provider-owned background operation id.</summary>
    public required string ClientOperationId { get; init; }

    /// <summary>Gets the terminal operation state.</summary>
    public required ClientToolOperationOutcomeState State { get; init; }

    /// <summary>Gets final content for completed operations.</summary>
    public IReadOnlyList<IToolResultContent>? Content { get; init; }

    /// <summary>Gets optional client tool augmentation.</summary>
    public ClientToolAugmentation? Augmentation { get; init; }

    /// <summary>Gets the structured fault information.</summary>
    public ClientToolError? Error { get; init; }

    /// <summary>Gets the cancellation reason for cancelled operations.</summary>
    public string? CancellationReason { get; init; }

    /// <summary>Gets provider-supplied terminal metadata.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>Structured provider rejection or failure.</summary>
public sealed record ClientToolError
{
    public required string Kind { get; init; }
    public required string Message { get; init; }
    public bool? Retryable { get; init; }
    public IReadOnlyList<IToolResultContent>? Details { get; init; }
    public ClientToolProviderContext? CurrentContext { get; init; }
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
}

/// <summary>
/// Error message sent by HPD on the provider websocket.
/// </summary>
public sealed record ClientToolProviderErrorMessage
{
    /// <summary>Gets the wire message type.</summary>
    public string Type { get; init; } = "provider.error";

    /// <summary>Gets a stable error code.</summary>
    public required string Code { get; init; }

    /// <summary>Gets a human-readable message.</summary>
    public required string Message { get; init; }
}
