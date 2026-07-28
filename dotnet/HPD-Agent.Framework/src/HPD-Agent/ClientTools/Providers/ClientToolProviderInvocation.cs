// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

namespace HPD.Agent.ClientTools;

/// <summary>
/// Binding metadata for a model-visible tool backed by a live client tool provider.
/// </summary>
public sealed record ClientToolProviderToolBinding
{
    /// <summary>Gets the exclusive lease id used to route provider invocations.</summary>
    public required string BindingId { get; init; }

    /// <summary>Gets HPD's runtime id for the connected provider.</summary>
    public required string ClientRuntimeId { get; init; }

    /// <summary>Gets HPD's active connection id for the provider.</summary>
    public required string ConnectionId { get; init; }

    /// <summary>Gets the logical app provider name.</summary>
    public required string AppProviderName { get; init; }

    /// <summary>Gets the provider harness name.</summary>
    public required string HarnessName { get; init; }

    /// <summary>Gets the original provider-side tool name.</summary>
    public required string ProviderToolName { get; init; }

    /// <summary>Gets the model-visible tool name exposed by HPD.</summary>
    public required string VisibleToolName { get; init; }
}

/// <summary>
/// Provider-backed invocation request created by the client tool middleware.
/// </summary>
public sealed record ClientToolProviderInvocationRequest
{
    /// <summary>Gets the provider binding used for this invocation.</summary>
    public required ClientToolProviderToolBinding Binding { get; init; }

    /// <summary>Gets the request id used to correlate the immediate outcome.</summary>
    public required string RequestId { get; init; }

    /// <summary>Gets the model function call id.</summary>
    public required string CallId { get; init; }

    /// <summary>Gets sanitized tool arguments.</summary>
    public required IReadOnlyDictionary<string, object?> Arguments { get; init; }

    /// <summary>Gets the resolved compound operation, if applicable.</summary>
    public ClientToolResolvedOperation? Operation { get; init; }

    /// <summary>Gets whether HPD must include its provider-context snapshot.</summary>
    public bool RequiresFreshContext { get; init; }

    /// <summary>Gets the requested invocation mode, if the model supplied one.</summary>
    public AgentInvocationMode? RequestedInvocationMode { get; init; }

    /// <summary>Gets the invocation mode resolved by HPD policy.</summary>
    public required AgentInvocationMode ResolvedInvocationMode { get; init; }

    /// <summary>Gets an optional tool description for diagnostics.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// Transport abstraction for a connected provider.
/// </summary>
public interface IClientToolProviderConnection
{
    /// <summary>
    /// Sends a provider tool invocation to the connected app.
    /// </summary>
    ValueTask SendInvocationAsync(
        ClientToolProviderInvokeToolMessage message,
        CancellationToken cancellationToken);

    /// <summary>
    /// Actively terminates the provider transport after server-side
    /// revocation. Implementations must be idempotent.
    /// </summary>
    ValueTask CloseAsync(
        string reason,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}

/// <summary>
/// Provider-owned background operation accepted by a provider-backed client tool.
/// </summary>
public sealed record ClientToolProviderBackgroundOperationDescriptor
{
    /// <summary>Gets the provider tool binding that owns the operation.</summary>
    public required ClientToolProviderToolBinding Binding { get; init; }

    /// <summary>Gets the provider-owned background operation id.</summary>
    public required string ClientOperationId { get; init; }

    /// <summary>Gets the model-visible tool name that launched the operation.</summary>
    public required string ToolName { get; init; }

    /// <summary>Gets the immediate request id that accepted the operation.</summary>
    public required string RequestId { get; init; }

    /// <summary>Gets the model function call id that launched the operation.</summary>
    public string? CallId { get; init; }

    /// <summary>Gets the session id that owns the operation.</summary>
    public string? SessionId { get; init; }

    /// <summary>Gets the thread id that owns the operation.</summary>
    public string? ThreadId { get; init; }
}

/// <summary>
/// Registration returned when provider-owned background work is tracked by the registry.
/// </summary>
public sealed record ClientToolProviderBackgroundOperationRegistration(
    string ClientOperationId,
    Task<ClientToolBackgroundOperationResult> Completion);
