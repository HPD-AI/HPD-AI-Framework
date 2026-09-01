// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

namespace HPD.Agent.ClientTools;

/// <summary>
/// Registry for live client tool providers connected to the HPD host.
/// </summary>
public interface IClientToolProviderRegistry
{
    /// <summary>
    /// Registers a provider connection and assigns runtime ids.
    /// </summary>
    /// <param name="identity">Provider-supplied identity.</param>
    /// <param name="cancellationToken">Cancellation token for the registration.</param>
    /// <returns>Connection registration details.</returns>
    ValueTask<ClientToolProviderConnectionRegistration> RegisterConnectionAsync(
        ClientToolProviderIdentity identity,
        ClientToolProviderRuntimeIdentity? runtimeIdentity,
        IClientToolProviderConnection connection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the manifest for a connected provider.
    /// </summary>
    /// <param name="clientRuntimeId">The HPD provider runtime id.</param>
    /// <param name="connectionId">The active connection id.</param>
    /// <param name="manifest">The latest provider manifest.</param>
    /// <param name="cancellationToken">Cancellation token for the update.</param>
    ValueTask UpdateManifestAsync(
        string clientRuntimeId,
        string connectionId,
        ClientToolProviderManifest manifest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a heartbeat for a provider connection.
    /// </summary>
    /// <param name="clientRuntimeId">The HPD provider runtime id.</param>
    /// <param name="connectionId">The active connection id.</param>
    /// <param name="cancellationToken">Cancellation token for the update.</param>
    ValueTask RecordHeartbeatAsync(
        string clientRuntimeId,
        string connectionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a provider connection as disconnected.
    /// </summary>
    /// <param name="clientRuntimeId">The HPD provider runtime id.</param>
    /// <param name="connectionId">The active connection id.</param>
    /// <param name="cancellationToken">Cancellation token for the update.</param>
    ValueTask DisconnectAsync(
        string clientRuntimeId,
        string connectionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every provider connection matching a server-authoritative
    /// selector and breaks its leases and in-flight operations.
    /// </summary>
    ValueTask<int> RevokeAsync(
        ClientProviderSelector selector,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically prevents registration of an already-authorized connection
    /// at or below the supplied App workload generation.
    /// </summary>
    ValueTask AdvanceRevocationFenceAsync(
        ClientToolProviderRevocationFence fence,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to acquire an exclusive binding lease for a provider selected by a reference.
    /// </summary>
    /// <param name="reference">The requested client app provider and selection policy.</param>
    /// <param name="scope">Runtime scope that will own the lease.</param>
    /// <param name="cancellationToken">Cancellation token for the acquisition.</param>
    /// <returns>A binding result, or <see langword="null"/> when no provider can be leased.</returns>
    ValueTask<ClientToolProviderBindingResult?> TryAcquireBindingAsync(
        ClientAppProviderReference reference,
        ClientToolProviderBindingScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases an active binding lease.
    /// </summary>
    /// <param name="bindingId">The binding lease id.</param>
    /// <param name="reason">Human-readable release reason.</param>
    /// <param name="cancellationToken">Cancellation token for the release.</param>
    ValueTask ReleaseBindingAsync(
        string bindingId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to get a binding lease by id.
    /// </summary>
    /// <param name="bindingId">The binding lease id.</param>
    /// <param name="lease">The lease when found.</param>
    /// <returns>True when a lease exists.</returns>
    bool TryGetBinding(string bindingId, out ClientToolProviderBindingLease lease);

    /// <summary>
    /// Invokes one tool on a connected provider and waits for its immediate outcome.
    /// </summary>
    /// <param name="request">Provider invocation request.</param>
    /// <param name="timeout">Maximum time to wait for the immediate outcome.</param>
    /// <param name="cancellationToken">Cancellation token for the wait.</param>
    /// <returns>The client tool immediate outcome.</returns>
    ValueTask<ClientToolInvokeOutcomeEvent> InvokeToolAsync(
        ClientToolProviderInvocationRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a provider invocation with the outcome sent by the connected provider.
    /// </summary>
    /// <param name="clientRuntimeId">The provider runtime id.</param>
    /// <param name="connectionId">The active connection id.</param>
    /// <param name="outcome">The provider invocation outcome.</param>
    /// <returns>True when a pending invocation was resolved.</returns>
    bool TryResolveInvocationOutcome(
        string clientRuntimeId,
        string connectionId,
        ClientToolProviderInvokeOutcomeMessage outcome);

    /// <summary>
    /// Tracks provider-owned background work accepted by a provider-backed client tool.
    /// </summary>
    /// <param name="descriptor">Accepted provider background operation descriptor.</param>
    /// <returns>A registration that completes when the provider sends a terminal outcome.</returns>
    ClientToolProviderOperationRegistration RegisterOperation(
        ClientToolProviderOperationDescriptor descriptor);

    /// <summary>
    /// Resolves provider-owned background work with the terminal outcome sent by the provider.
    /// </summary>
    /// <param name="clientRuntimeId">The provider runtime id.</param>
    /// <param name="connectionId">The active connection id.</param>
    /// <param name="outcome">The provider background operation outcome.</param>
    /// <returns>True when a pending provider background operation was resolved.</returns>
    bool TryResolveOperationOutcome(
        string clientRuntimeId,
        string connectionId,
        ClientToolProviderOperationOutcomeMessage outcome);

    /// <summary>
    /// Attempts to get one provider snapshot.
    /// </summary>
    /// <param name="clientRuntimeId">The HPD provider runtime id.</param>
    /// <param name="snapshot">The provider snapshot when found.</param>
    /// <returns>True when a provider exists.</returns>
    bool TryGet(string clientRuntimeId, out ClientToolProviderSnapshot snapshot);

    /// <summary>
    /// Lists providers matching the supplied query.
    /// </summary>
    /// <param name="query">Provider query.</param>
    /// <returns>Matching provider snapshots.</returns>
    IReadOnlyList<ClientToolProviderSnapshot> List(ClientToolProviderQuery? query = null);
}

public sealed record ClientToolProviderRevocationFence(
    string AppInstallationId,
    long MaximumWorkloadGeneration);
