// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.Middleware;

namespace HPD.Agent.ClientTools;

/// <summary>
/// Runtime-scoped registry for client-owned background tool operations.
/// </summary>
public interface IClientToolBackgroundOperationRegistry
{
    /// <summary>
    /// Registers a client-owned background operation.
    /// </summary>
    /// <param name="descriptor">Operation descriptor.</param>
    /// <returns>The registered operation.</returns>
    ClientToolBackgroundOperationRegistration RegisterClientToolBackgroundOperation(
        ClientToolBackgroundOperationDescriptor descriptor);

    /// <summary>
    /// Attempts to resolve a registered client-owned background operation.
    /// </summary>
    /// <param name="input">Terminal outcome input from the client.</param>
    /// <returns><see langword="true"/> when a matching operation was resolved.</returns>
    bool TryResolveClientToolBackgroundOperation(ClientToolBackgroundOperationOutcomeEvent input);
}

/// <summary>
/// Describes a client-owned background tool operation accepted by a responder.
/// </summary>
public sealed record ClientToolBackgroundOperationDescriptor
{
    /// <summary>Gets the client-owned operation id.</summary>
    public required string ClientOperationId { get; init; }

    /// <summary>Gets the client tool name.</summary>
    public required string ToolName { get; init; }

    /// <summary>Gets the initial request id.</summary>
    public required string RequestId { get; init; }

    /// <summary>Gets the model function call id.</summary>
    public required string CallId { get; init; }

    /// <summary>Gets the associated runtime task id.</summary>
    public required string TaskId { get; init; }

    /// <summary>Gets the associated runtime handle id, if one exists.</summary>
    public string? HandleId { get; init; }

    /// <summary>Gets the session id that owns the operation.</summary>
    public string? SessionId { get; init; }

    /// <summary>Gets the thread id that owns the operation.</summary>
    public string? ThreadId { get; init; }

    /// <summary>Gets the invocation that created the operation.</summary>
    public FunctionInvocationSnapshot? Invocation { get; init; }
}

/// <summary>
/// Registration returned for a client-owned background tool operation.
/// </summary>
public sealed record ClientToolBackgroundOperationRegistration(
    string ClientOperationId,
    string TaskId,
    Task<ClientToolBackgroundOperationResult> Completion);

/// <summary>
/// Terminal result for a client-owned background tool operation.
/// </summary>
public sealed record ClientToolBackgroundOperationResult
{
    /// <summary>Gets the terminal state.</summary>
    public required ClientToolBackgroundOperationOutcomeState State { get; init; }

    /// <summary>Gets content for completed operations.</summary>
    public IReadOnlyList<IToolResultContent>? Content { get; init; }

    /// <summary>Gets optional client tool augmentation.</summary>
    public ClientToolAugmentation? Augmentation { get; init; }

    /// <summary>Gets an error message for faulted operations.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Gets an error type for faulted operations.</summary>
    public string? ErrorType { get; init; }

    /// <summary>Gets a cancellation reason for cancelled operations.</summary>
    public string? CancellationReason { get; init; }

    /// <summary>Gets terminal metadata.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
