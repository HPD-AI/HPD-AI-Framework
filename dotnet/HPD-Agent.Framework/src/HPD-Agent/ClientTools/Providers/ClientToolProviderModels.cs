// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json.Serialization;

namespace HPD.Agent.ClientTools;

/// <summary>
/// Logical client app integration advertised by a connected provider.
/// </summary>
public sealed record ClientAppProviderDescriptor
{
    /// <summary>Gets the stable app provider name, such as <c>penpot</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets a display name for UI/debugging surfaces.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Gets a description of the app provider.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the provider implementation version.</summary>
    public string? Version { get; init; }

    /// <summary>Gets tags used by provider selection policy.</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>Gets implementation-specific metadata.</summary>
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
}

/// <summary>
/// Stable identity for one connected client tool provider instance.
/// </summary>
public sealed record ClientToolProviderIdentity
{
    /// <summary>Gets the provider implementation name.</summary>
    public required string ProviderName { get; init; }

    /// <summary>Gets the app kind, such as <c>penpot</c> or <c>code-server</c>.</summary>
    public required string AppKind { get; init; }

    /// <summary>Gets an app-instance id supplied by the provider.</summary>
    public string? InstanceId { get; init; }

    /// <summary>Gets a stable installation id when available.</summary>
    public string? InstallationId { get; init; }

    /// <summary>Gets an optional user hint for diagnostics and selection.</summary>
    public string? UserHint { get; init; }

    /// <summary>Gets the origin that opened the connection, when known.</summary>
    public string? Origin { get; init; }

    /// <summary>Gets the provider implementation version.</summary>
    public string? Version { get; init; }
}

/// <summary>
/// Current provider-side app context used for selection and stale-context checks.
/// </summary>
public sealed record ClientToolProviderContext
{
    /// <summary>Gets the current workspace id.</summary>
    public string? WorkspaceId { get; init; }

    /// <summary>Gets the current document id.</summary>
    public string? DocumentId { get; init; }

    /// <summary>Gets the current document name.</summary>
    public string? DocumentName { get; init; }

    /// <summary>Gets the current page id.</summary>
    public string? PageId { get; init; }

    /// <summary>Gets the current file id.</summary>
    public string? FileId { get; init; }

    /// <summary>Gets the current project id.</summary>
    public string? ProjectId { get; init; }

    /// <summary>Gets the current scene id.</summary>
    public string? SceneId { get; init; }

    /// <summary>Gets the current active view name.</summary>
    public string? ActiveView { get; init; }

    /// <summary>Gets a human-readable selection summary.</summary>
    public string? SelectionSummary { get; init; }

    /// <summary>Gets a provider-defined app state version.</summary>
    public string? AppStateVersion { get; init; }

    /// <summary>Gets implementation-specific metadata.</summary>
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
}

/// <summary>
/// Provider readiness independent of websocket connectivity.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ClientToolProviderReadiness>))]
public enum ClientToolProviderReadiness
{
    /// <summary>The provider is initializing and should not be invoked yet.</summary>
    Initializing,

    /// <summary>The provider is ready to accept tool invocations.</summary>
    Ready,

    /// <summary>The provider is connected but some tools or bridges are degraded.</summary>
    Degraded,

    /// <summary>The provider has been revoked and should not be invoked.</summary>
    Revoked
}

/// <summary>
/// Current connection state for one provider instance.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ClientToolProviderConnectionState>))]
public enum ClientToolProviderConnectionState
{
    /// <summary>The provider connection exists but has not sent a manifest yet.</summary>
    Connected,

    /// <summary>The provider sent a manifest and is registered.</summary>
    Registered,

    /// <summary>The provider is ready to be bound.</summary>
    Ready,

    /// <summary>The provider is bound to a runtime scope.</summary>
    Bound,

    /// <summary>The provider disconnected.</summary>
    Disconnected,

    /// <summary>The provider was revoked by the server.</summary>
    Revoked
}

/// <summary>
/// State of an exclusive lease between one HPD runtime scope and one connected provider.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ClientToolProviderBindingLeaseStatus>))]
public enum ClientToolProviderBindingLeaseStatus
{
    /// <summary>The lease is active and can route provider invocations.</summary>
    Active,

    /// <summary>The runtime or provider released the lease intentionally.</summary>
    Released,

    /// <summary>The lease expired before it was released.</summary>
    Expired,

    /// <summary>The provider disconnected while the lease was active.</summary>
    Disconnected,

    /// <summary>The server revoked the lease.</summary>
    Revoked,

    /// <summary>The lease is no longer safe to use because the binding broke.</summary>
    Broken
}

/// <summary>
/// Manifest advertised by a client tool provider connection.
/// </summary>
public sealed record ClientToolProviderManifest
{
    /// <summary>Gets the provider protocol version.</summary>
    public string ProtocolVersion { get; init; } = "1";

    /// <summary>Gets the provider instance identity.</summary>
    public required ClientToolProviderIdentity Identity { get; init; }

    /// <summary>Gets the logical app provider identity.</summary>
    public required ClientAppProviderDescriptor AppProvider { get; init; }

    /// <summary>Gets the current provider app context.</summary>
    public ClientToolProviderContext? Context { get; init; }

    /// <summary>Gets the provider readiness.</summary>
    public ClientToolProviderReadiness Readiness { get; init; } = ClientToolProviderReadiness.Initializing;

    /// <summary>Gets the client tool harnesses advertised by this provider instance.</summary>
    public IReadOnlyList<clientToolHarnessDefinition> ClientToolHarnesses { get; init; } =
        Array.Empty<clientToolHarnessDefinition>();

    /// <summary>Gets provider-wide metadata.</summary>
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
}

/// <summary>
/// Immutable snapshot of a connected provider.
/// </summary>
public sealed record ClientToolProviderSnapshot
{
    /// <summary>Gets HPD's runtime id for this connected provider.</summary>
    public required string ClientRuntimeId { get; init; }

    /// <summary>Gets HPD's connection id for this websocket connection.</summary>
    public required string ConnectionId { get; init; }

    /// <summary>Gets the latest manifest, if the provider registered one.</summary>
    public ClientToolProviderManifest? Manifest { get; init; }

    /// <summary>Gets the current connection state.</summary>
    public ClientToolProviderConnectionState State { get; init; } = ClientToolProviderConnectionState.Connected;

    /// <summary>Gets when the provider connected.</summary>
    public DateTimeOffset ConnectedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the last heartbeat timestamp.</summary>
    public DateTimeOffset? LastHeartbeatAt { get; init; }

    /// <summary>Gets when the provider disconnected.</summary>
    public DateTimeOffset? DisconnectedAt { get; init; }

    /// <summary>Gets the active or most recent binding lease for this provider.</summary>
    public ClientToolProviderBindingLease? BindingLease { get; init; }
}

/// <summary>
/// Result returned when a provider connection is registered.
/// </summary>
public sealed record ClientToolProviderConnectionRegistration(
    string ClientRuntimeId,
    string ConnectionId,
    TimeSpan HeartbeatInterval);

/// <summary>
/// Runtime scope requesting an exclusive binding to a connected provider.
/// </summary>
public sealed record ClientToolProviderBindingScope
{
    /// <summary>Gets the HPD runtime id when the binding is runtime-scoped.</summary>
    public string? OwnerRuntimeId { get; init; }

    /// <summary>Gets the agent id or name that owns the binding.</summary>
    public string? AgentId { get; init; }

    /// <summary>Gets the session id that owns the binding.</summary>
    public string? SessionId { get; init; }

    /// <summary>Gets the thread id that owns the binding.</summary>
    public string? ThreadId { get; init; }

    /// <summary>Gets the runtime run id when the binding is run-scoped.</summary>
    public string? RuntimeRunId { get; init; }

    /// <summary>Gets the requested lease duration.</summary>
    public TimeSpan? LeaseDuration { get; init; }
}

/// <summary>
/// Exclusive lease allowing one runtime scope to invoke tools on one provider connection.
/// </summary>
public sealed record ClientToolProviderBindingLease
{
    /// <summary>Gets the stable id that must accompany provider-backed invocations.</summary>
    public required string BindingId { get; init; }

    /// <summary>Gets HPD's runtime id for the bound provider.</summary>
    public required string ClientRuntimeId { get; init; }

    /// <summary>Gets HPD's active connection id for the bound provider.</summary>
    public required string ConnectionId { get; init; }

    /// <summary>Gets the HPD runtime id that owns the lease, when known.</summary>
    public string? OwnerRuntimeId { get; init; }

    /// <summary>Gets the agent id or name that owns the lease.</summary>
    public string? AgentId { get; init; }

    /// <summary>Gets the session id that owns the lease.</summary>
    public string? SessionId { get; init; }

    /// <summary>Gets the thread id that owns the lease.</summary>
    public string? ThreadId { get; init; }

    /// <summary>Gets the runtime run id that owns the lease, when known.</summary>
    public string? RuntimeRunId { get; init; }

    /// <summary>Gets when the lease was created.</summary>
    public DateTimeOffset BoundAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets when the lease expires.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Gets the heartbeat interval expected from the provider.</summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Gets the current lease status.</summary>
    public ClientToolProviderBindingLeaseStatus Status { get; init; } =
        ClientToolProviderBindingLeaseStatus.Active;

    /// <summary>Gets when the lease moved out of the active state.</summary>
    public DateTimeOffset? ReleasedAt { get; init; }

    /// <summary>Gets a human-readable reason for the current terminal lease state.</summary>
    public string? ReleaseReason { get; init; }
}

/// <summary>
/// Result of acquiring an exclusive provider binding lease.
/// </summary>
public sealed record ClientToolProviderBindingResult
{
    /// <summary>Gets the provider snapshot selected by the registry.</summary>
    public required ClientToolProviderSnapshot Provider { get; init; }

    /// <summary>Gets the acquired exclusive lease.</summary>
    public required ClientToolProviderBindingLease Lease { get; init; }
}

/// <summary>
/// Query used to list provider snapshots.
/// </summary>
public sealed record ClientToolProviderQuery
{
    /// <summary>Gets the app provider name to match.</summary>
    public string? AppProviderName { get; init; }

    /// <summary>Gets the app kind to match.</summary>
    public string? AppKind { get; init; }

    /// <summary>Gets whether disconnected providers should be included.</summary>
    public bool IncludeDisconnected { get; init; }
}
