// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.ClientTools;

/// <summary>
/// Configuration for Client tool middleware behavior.
/// </summary>
public record ClientToolConfig
{
    /// <summary>
    /// Timeout for waiting for Client tool response.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan InvokeTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Strategy for handling Client disconnection mid-invocation.
    /// Default: FallbackMessage (returns error message to agent).
    /// </summary>
    public ClientDisconnectionStrategy DisconnectionStrategy { get; init; }
        = ClientDisconnectionStrategy.FallbackMessage;

    /// <summary>
    /// Maximum retry attempts for RetryWithBackoff strategy.
    /// Default: 3.
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Message template for FallbackMessage strategy.
    /// {0} is replaced with tool name.
    /// </summary>
    public string FallbackMessageTemplate { get; init; }
        = "Client disconnected. Tool '{0}' is unavailable.";

    /// <summary>
    /// Maximum size for inline document content in skills.
    /// Documents exceeding this should use URL instead.
    /// Default: 50KB.
    /// </summary>
    public int MaxInlineDocumentSizeBytes { get; init; } = 50 * 1024;

    /// <summary>
    /// Whether to validate JSON Schema on tool registration.
    /// Recommended: true (catch errors early).
    /// Default: true.
    /// </summary>
    public bool ValidateSchemaOnRegistration { get; init; } = true;
}

/// <summary>
/// Strategy for handling Client disconnection during tool invocation.
/// </summary>
public enum ClientDisconnectionStrategy
{
    /// <summary>
    /// Throw exception immediately. Use when Client availability is critical.
    /// </summary>
    FailFast,

    /// <summary>
    /// Retry with exponential backoff up to MaxRetries times.
    /// Use for transient network issues.
    /// </summary>
    RetryWithBackoff,

    /// <summary>
    /// Return error message to agent (default).
    /// Agent can decide how to proceed (e.g., inform user, try alternative).
    /// </summary>
    FallbackMessage
}
