// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Collections.Concurrent;

namespace HPD.Agent.ClientTools;

/// <summary>
/// In-memory implementation of <see cref="IClientToolProviderRegistry"/>.
/// </summary>
/// <remarks>
/// This registry is intentionally host-local. Durable provider persistence can be added later
/// without changing the provider protocol contracts.
/// </remarks>
public sealed class InMemoryClientToolProviderRegistry : IClientToolProviderRegistry
{
    private static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<string, ProviderEntry> _providers =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, ClientToolProviderBindingLease> _leases =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, PendingProviderBackgroundOperation> _backgroundOperations =
        new(StringComparer.Ordinal);

    private readonly object _leaseLock = new();

    /// <inheritdoc />
    public ValueTask<ClientToolProviderConnectionRegistration> RegisterConnectionAsync(
        ClientToolProviderIdentity identity,
        IClientToolProviderConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();

        var clientRuntimeId = CreateClientRuntimeId(identity);
        var connectionId = $"cpc_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var snapshot = new ClientToolProviderSnapshot
        {
            ClientRuntimeId = clientRuntimeId,
            ConnectionId = connectionId,
            State = ClientToolProviderConnectionState.Connected,
            ConnectedAt = now,
            LastHeartbeatAt = now
        };

        _providers.AddOrUpdate(clientRuntimeId, new ProviderEntry(snapshot, connection), (_, existing) =>
        {
            BreakProviderLeases(
                existing.Snapshot.ClientRuntimeId,
                existing.Snapshot.ConnectionId,
                ClientToolProviderBindingLeaseStatus.Disconnected,
                "Provider connection was replaced.");
            existing.FailPendingInvocations("Provider connection was replaced.");
            return existing with
            {
                Snapshot = existing.Snapshot with
                {
                    ConnectionId = connectionId,
                    State = ClientToolProviderConnectionState.Connected,
                    ConnectedAt = now,
                    LastHeartbeatAt = now,
                    DisconnectedAt = null,
                    BindingLease = null
                },
                Connection = connection
            };
        });

        return ValueTask.FromResult(new ClientToolProviderConnectionRegistration(
            clientRuntimeId,
            connectionId,
            DefaultHeartbeatInterval));
    }

    /// <inheritdoc />
    public ValueTask UpdateManifestAsync(
        string clientRuntimeId,
        string connectionId,
        ClientToolProviderManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientRuntimeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(manifest);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(manifest.ProtocolVersion, "2", StringComparison.Ordinal))
            throw new ArgumentException(
                $"Unsupported client tool provider protocol '{manifest.ProtocolVersion}'. Expected '2'.",
                nameof(manifest));

        foreach (var harness in manifest.ClientToolHarnesses)
        {
            foreach (var tool in harness.Tools)
                tool.Validate();
        }

        _providers.AddOrUpdate(
            clientRuntimeId,
            _ => new ProviderEntry(new ClientToolProviderSnapshot
            {
                ClientRuntimeId = clientRuntimeId,
                ConnectionId = connectionId,
                Manifest = manifest,
                State = StateFromReadiness(manifest.Readiness),
                ConnectedAt = DateTimeOffset.UtcNow,
                LastHeartbeatAt = DateTimeOffset.UtcNow
            }, NoopClientToolProviderConnection.Instance),
            (_, existing) =>
            {
                if (!string.Equals(existing.Snapshot.ConnectionId, connectionId, StringComparison.Ordinal))
                    return existing;

                var bindingLease = GetActiveLease(existing.Snapshot.BindingLease, DateTimeOffset.UtcNow);

                return existing with
                {
                    Snapshot = existing.Snapshot with
                    {
                        Manifest = manifest,
                        State = bindingLease is not null
                            ? ClientToolProviderConnectionState.Bound
                            : StateFromReadiness(manifest.Readiness),
                        LastHeartbeatAt = DateTimeOffset.UtcNow
                    }
                };
            });

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask RecordHeartbeatAsync(
        string clientRuntimeId,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientRuntimeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_providers.TryGetValue(clientRuntimeId, out var existing) &&
            string.Equals(existing.Snapshot.ConnectionId, connectionId, StringComparison.Ordinal))
        {
            _providers[clientRuntimeId] = existing with
            {
                Snapshot = existing.Snapshot with
                {
                    LastHeartbeatAt = DateTimeOffset.UtcNow
                }
            };
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisconnectAsync(
        string clientRuntimeId,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientRuntimeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_providers.TryGetValue(clientRuntimeId, out var existing) &&
            string.Equals(existing.Snapshot.ConnectionId, connectionId, StringComparison.Ordinal))
        {
            existing.FailPendingInvocations("Provider disconnected.");
            FailProviderBackgroundOperations(clientRuntimeId, connectionId, "Provider disconnected.");
            BreakProviderLeases(clientRuntimeId, connectionId, ClientToolProviderBindingLeaseStatus.Disconnected, "Provider disconnected.");
            _providers[clientRuntimeId] = existing with
            {
                Snapshot = existing.Snapshot with
                {
                    State = ClientToolProviderConnectionState.Disconnected,
                    DisconnectedAt = DateTimeOffset.UtcNow,
                    BindingLease = existing.Snapshot.BindingLease is null
                        ? null
                        : CompleteLease(
                            existing.Snapshot.BindingLease,
                            ClientToolProviderBindingLeaseStatus.Disconnected,
                            "Provider disconnected.")
                }
            };
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public bool TryGet(string clientRuntimeId, out ClientToolProviderSnapshot snapshot)
    {
        if (_providers.TryGetValue(clientRuntimeId, out var entry))
        {
            snapshot = entry.Snapshot;
            return true;
        }

        snapshot = null!;
        return false;
    }

    /// <inheritdoc />
    public ValueTask<ClientToolProviderBindingResult?> TryAcquireBindingAsync(
        ClientAppProviderReference reference,
        ClientToolProviderBindingScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_leaseLock)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var entry in _providers.Values
                .Where(provider => IsBindable(provider.Snapshot, reference, scope, now))
                .OrderBy(provider => provider.Snapshot.ConnectedAt))
            {
                var snapshot = entry.Snapshot;
                var manifest = snapshot.Manifest;
                if (manifest is null)
                    continue;

                var existingLease =
                    GetActiveLease(snapshot.BindingLease, now) ??
                    GetActiveProviderLease(snapshot.ClientRuntimeId, now);
                if (existingLease is not null)
                {
                    if (!LeaseMatchesScope(existingLease, scope))
                        return ValueTask.FromResult<ClientToolProviderBindingResult?>(null);

                    return ValueTask.FromResult<ClientToolProviderBindingResult?>(new ClientToolProviderBindingResult
                    {
                        Provider = snapshot,
                        Lease = existingLease
                    });
                }

                var lease = new ClientToolProviderBindingLease
                {
                    BindingId = $"bind_{Guid.NewGuid():N}",
                    ClientRuntimeId = snapshot.ClientRuntimeId,
                    ConnectionId = snapshot.ConnectionId,
                    OwnerRuntimeId = scope.OwnerRuntimeId,
                    AgentId = scope.AgentId,
                    SessionId = scope.SessionId,
                    ThreadId = scope.ThreadId,
                    ThreadExecutionId = scope.ThreadExecutionId,
                    BoundAt = now,
                    ExpiresAt = scope.LeaseDuration is null ? null : now.Add(scope.LeaseDuration.Value),
                    HeartbeatInterval = DefaultHeartbeatInterval,
                    Status = ClientToolProviderBindingLeaseStatus.Active
                };

                _leases[lease.BindingId] = lease;
                var boundSnapshot = snapshot with
                {
                    State = ClientToolProviderConnectionState.Bound,
                    BindingLease = lease
                };
                _providers[snapshot.ClientRuntimeId] = entry with { Snapshot = boundSnapshot };

                return ValueTask.FromResult<ClientToolProviderBindingResult?>(new ClientToolProviderBindingResult
                {
                    Provider = boundSnapshot,
                    Lease = lease
                });
            }
        }

        return ValueTask.FromResult<ClientToolProviderBindingResult?>(null);
    }

    /// <inheritdoc />
    public ValueTask ReleaseBindingAsync(
        string bindingId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_leaseLock)
        {
            CompleteLease(bindingId, ClientToolProviderBindingLeaseStatus.Released, reason ?? "Binding released.");
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public bool TryGetBinding(string bindingId, out ClientToolProviderBindingLease lease)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        if (_leases.TryGetValue(bindingId, out var existing))
        {
            if (!IsLeaseActive(existing, DateTimeOffset.UtcNow))
                existing = CompleteLease(bindingId, ClientToolProviderBindingLeaseStatus.Expired, "Binding lease expired.") ?? existing;

            lease = existing;
            return true;
        }

        lease = null!;
        return false;
    }

    /// <inheritdoc />
    public async ValueTask<ClientToolInvokeOutcomeEvent> InvokeToolAsync(
        ClientToolProviderInvocationRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTimeOffset.UtcNow;
        if (!_leases.TryGetValue(request.Binding.BindingId, out var lease))
            return CreateProviderFailure(request.RequestId, "Client app provider binding is missing.");

        if (!IsLeaseActive(lease, now))
        {
            CompleteLease(request.Binding.BindingId, ClientToolProviderBindingLeaseStatus.Expired, "Binding lease expired.");
            return CreateProviderFailure(request.RequestId, "Client app provider binding is expired.");
        }

        if (!_providers.TryGetValue(request.Binding.ClientRuntimeId, out var entry))
            return CreateProviderFailure(request.RequestId, "Provider is unavailable.");

        if (!string.Equals(lease.ClientRuntimeId, request.Binding.ClientRuntimeId, StringComparison.Ordinal) ||
            !string.Equals(lease.ConnectionId, request.Binding.ConnectionId, StringComparison.Ordinal) ||
            !string.Equals(entry.Snapshot.ConnectionId, request.Binding.ConnectionId, StringComparison.Ordinal))
        {
            return CreateProviderFailure(request.RequestId, "Provider binding is no longer active.");
        }

        if (entry.Snapshot.State is ClientToolProviderConnectionState.Disconnected or ClientToolProviderConnectionState.Revoked)
            return CreateProviderFailure(request.RequestId, "Provider is disconnected.");

        if (entry.Snapshot.Manifest is null ||
            entry.Snapshot.Manifest.Readiness is not ClientToolProviderReadiness.Ready)
            return CreateProviderFailure(request.RequestId, "Provider is not ready.");

        if (!ContainsTool(entry.Snapshot.Manifest, request.Binding.HarnessName, request.Binding.ProviderToolName))
            return CreateProviderFailure(request.RequestId, "Provider tool is unavailable.");

        var invocationId = $"inv_{Guid.NewGuid():N}";
        var pending = entry.RegisterPendingInvocation(invocationId);
        try
        {
            await entry.Connection.SendInvocationAsync(
                new ClientToolProviderInvokeToolMessage
                {
                    ClientRuntimeId = request.Binding.ClientRuntimeId,
                    ConnectionId = request.Binding.ConnectionId,
                    BindingId = request.Binding.BindingId,
                    InvocationId = invocationId,
                    RequestId = request.RequestId,
                    ToolName = request.Binding.ProviderToolName,
                    VisibleToolName = request.Binding.VisibleToolName,
                    CallId = request.CallId,
                    Arguments = request.Arguments,
                    Operation = request.Operation,
                    ExpectedContext = request.RequiresFreshContext
                        ? entry.Snapshot.Manifest.Context
                        : null,
                    RequestedInvocationMode = request.RequestedInvocationMode,
                    Deadline = DateTimeOffset.UtcNow.Add(timeout)
                },
                cancellationToken).ConfigureAwait(false);

            return await pending.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            entry.RemovePendingInvocation(invocationId);
            return CreateProviderFailure(request.RequestId, "Provider invocation timed out.");
        }
        catch (OperationCanceledException)
        {
            entry.RemovePendingInvocation(invocationId);
            return CreateProviderFailure(request.RequestId, "Provider invocation was cancelled.");
        }
        catch (Exception ex)
        {
            entry.RemovePendingInvocation(invocationId);
            return CreateProviderFailure(request.RequestId, $"Provider invocation failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public bool TryResolveInvocationOutcome(
        string clientRuntimeId,
        string connectionId,
        ClientToolProviderInvokeOutcomeMessage outcome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientRuntimeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(outcome);

        if (!_providers.TryGetValue(clientRuntimeId, out var entry) ||
            !string.Equals(entry.Snapshot.ConnectionId, connectionId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!_leases.TryGetValue(outcome.BindingId, out var lease) ||
            !string.Equals(lease.ClientRuntimeId, clientRuntimeId, StringComparison.Ordinal) ||
            !string.Equals(lease.ConnectionId, connectionId, StringComparison.Ordinal))
        {
            return false;
        }

        return entry.TryResolveInvocation(outcome);
    }

    /// <inheritdoc />
    public ClientToolProviderBackgroundOperationRegistration RegisterBackgroundOperation(
        ClientToolProviderBackgroundOperationDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(descriptor.Binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ClientOperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ToolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.RequestId);

        if (!TryGetActiveLease(
                descriptor.Binding.BindingId,
                descriptor.Binding.ClientRuntimeId,
                descriptor.Binding.ConnectionId,
                out _))
        {
            throw new InvalidOperationException(
                $"Provider binding '{descriptor.Binding.BindingId}' is not active.");
        }

        var pending = new PendingProviderBackgroundOperation(descriptor);
        if (!_backgroundOperations.TryAdd(descriptor.ClientOperationId, pending))
        {
            throw new InvalidOperationException(
                $"A provider background operation with id '{descriptor.ClientOperationId}' is already registered.");
        }

        return new ClientToolProviderBackgroundOperationRegistration(
            descriptor.ClientOperationId,
            pending.Completion);
    }

    /// <inheritdoc />
    public bool TryResolveBackgroundOperationOutcome(
        string clientRuntimeId,
        string connectionId,
        ClientToolProviderBackgroundOperationOutcomeMessage outcome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientRuntimeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome.ClientOperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome.BindingId);

        if (!TryGetActiveLease(outcome.BindingId, clientRuntimeId, connectionId, out _))
            return false;

        if (!_backgroundOperations.TryRemove(outcome.ClientOperationId, out var pending))
            return false;

        if (!string.Equals(pending.Descriptor.Binding.BindingId, outcome.BindingId, StringComparison.Ordinal) ||
            !string.Equals(pending.Descriptor.Binding.ClientRuntimeId, clientRuntimeId, StringComparison.Ordinal) ||
            !string.Equals(pending.Descriptor.Binding.ConnectionId, connectionId, StringComparison.Ordinal))
        {
            return false;
        }

        return pending.TrySetResult(new ClientToolBackgroundOperationResult
        {
            State = outcome.State,
            Content = outcome.Content,
            Augmentation = outcome.Augmentation,
            ErrorMessage = outcome.Error?.Message,
            ErrorType = outcome.Error?.Kind,
            CancellationReason = outcome.CancellationReason,
            Metadata = outcome.Metadata
        });
    }

    /// <inheritdoc />
    public IReadOnlyList<ClientToolProviderSnapshot> List(ClientToolProviderQuery? query = null)
    {
        query ??= new ClientToolProviderQuery();
        return _providers.Values
            .Select(static entry => entry.Snapshot)
            .Where(snapshot => query.IncludeDisconnected ||
                snapshot.State is not ClientToolProviderConnectionState.Disconnected)
            .Where(snapshot => Matches(snapshot, query))
            .OrderBy(snapshot => snapshot.ConnectedAt)
            .ToArray();
    }

    private static bool Matches(ClientToolProviderSnapshot snapshot, ClientToolProviderQuery query)
    {
        var manifest = snapshot.Manifest;
        if (!string.IsNullOrWhiteSpace(query.AppProviderName) &&
            !string.Equals(manifest?.AppProvider.Name, query.AppProviderName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.AppKind) &&
            !string.Equals(manifest?.Identity.AppKind, query.AppKind, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private bool IsBindable(
        ClientToolProviderSnapshot snapshot,
        ClientAppProviderReference reference,
        ClientToolProviderBindingScope scope,
        DateTimeOffset now)
    {
        var manifest = snapshot.Manifest;
        if (manifest is null)
            return false;

        if (snapshot.State is ClientToolProviderConnectionState.Disconnected or ClientToolProviderConnectionState.Revoked)
            return false;

        if (manifest.Readiness is not ClientToolProviderReadiness.Ready)
            return false;

        if (!string.Equals(manifest.AppProvider.Name, reference.Name, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!MatchesSelector(snapshot, reference.ProviderSelector))
            return false;

        var activeLease = GetActiveLease(snapshot.BindingLease, now);
        var activeProviderLease =
            activeLease ??
            GetActiveProviderLease(snapshot.ClientRuntimeId, now);
        if (activeProviderLease is not null && !LeaseMatchesScope(activeProviderLease, scope))
            return false;

        if (snapshot.BindingLease is { Status: ClientToolProviderBindingLeaseStatus.Active } expiredLease &&
            !IsLeaseActive(expiredLease, now))
        {
            CompleteLease(expiredLease.BindingId, ClientToolProviderBindingLeaseStatus.Expired, "Binding lease expired.");
        }

        return true;
    }

    private static bool LeaseMatchesScope(
        ClientToolProviderBindingLease lease,
        ClientToolProviderBindingScope scope)
        => string.Equals(lease.OwnerRuntimeId, scope.OwnerRuntimeId, StringComparison.Ordinal) &&
            string.Equals(lease.AgentId, scope.AgentId, StringComparison.Ordinal) &&
            string.Equals(lease.SessionId, scope.SessionId, StringComparison.Ordinal) &&
            string.Equals(lease.ThreadId, scope.ThreadId, StringComparison.Ordinal) &&
            string.Equals(lease.ThreadExecutionId, scope.ThreadExecutionId, StringComparison.Ordinal);

    private static bool MatchesSelector(ClientToolProviderSnapshot snapshot, ClientProviderSelector? selector)
    {
        if (selector is null)
            return true;

        var manifest = snapshot.Manifest;
        if (!string.IsNullOrWhiteSpace(selector.ClientRuntimeId) &&
            !string.Equals(snapshot.ClientRuntimeId, selector.ClientRuntimeId, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrWhiteSpace(selector.AppKind) &&
            !string.Equals(manifest?.Identity.AppKind, selector.AppKind, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(selector.WorkspaceId) &&
            !string.Equals(manifest?.Context?.WorkspaceId, selector.WorkspaceId, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(selector.DocumentId) &&
            !string.Equals(manifest?.Context?.DocumentId, selector.DocumentId, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(selector.ProjectId) &&
            !string.Equals(manifest?.Context?.ProjectId, selector.ProjectId, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(selector.UserId) &&
            !string.Equals(manifest?.Identity.UserHint, selector.UserId, StringComparison.OrdinalIgnoreCase))
            return false;

        if (selector.Tags is { Count: > 0 })
        {
            var tags = manifest?.AppProvider.Tags ?? Array.Empty<string>();
            if (!selector.Tags.All(tag => tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
                return false;
        }

        return true;
    }

    private static bool ContainsTool(
        ClientToolProviderManifest manifest,
        string harnessName,
        string toolName)
        => manifest.ClientToolHarnesses.Any(harness =>
            string.Equals(harness.Name, harnessName, StringComparison.OrdinalIgnoreCase) &&
            harness.Tools.Any(tool => string.Equals(tool.Name, toolName, StringComparison.OrdinalIgnoreCase)));

    private bool TryGetActiveLease(
        string bindingId,
        string clientRuntimeId,
        string connectionId,
        out ClientToolProviderBindingLease lease)
    {
        lease = null!;
        if (!_leases.TryGetValue(bindingId, out var existing))
            return false;

        if (!IsLeaseActive(existing, DateTimeOffset.UtcNow))
        {
            CompleteLease(bindingId, ClientToolProviderBindingLeaseStatus.Expired, "Binding lease expired.");
            return false;
        }

        if (!string.Equals(existing.ClientRuntimeId, clientRuntimeId, StringComparison.Ordinal) ||
            !string.Equals(existing.ConnectionId, connectionId, StringComparison.Ordinal))
        {
            return false;
        }

        lease = existing;
        return true;
    }

    private ClientToolProviderBindingLease? CompleteLease(
        string bindingId,
        ClientToolProviderBindingLeaseStatus status,
        string reason)
    {
        if (!_leases.TryGetValue(bindingId, out var lease))
            return null;

        var completed = CompleteLease(lease, status, reason);
        _leases[bindingId] = completed;

        if (_providers.TryGetValue(completed.ClientRuntimeId, out var entry) &&
            string.Equals(entry.Snapshot.BindingLease?.BindingId, bindingId, StringComparison.Ordinal))
        {
            var nextState = entry.Snapshot.State is ClientToolProviderConnectionState.Disconnected
                ? ClientToolProviderConnectionState.Disconnected
                : entry.Snapshot.Manifest is null
                    ? ClientToolProviderConnectionState.Connected
                    : StateFromReadiness(entry.Snapshot.Manifest.Readiness);

            _providers[completed.ClientRuntimeId] = entry with
            {
                Snapshot = entry.Snapshot with
                {
                    State = nextState,
                    BindingLease = completed
                }
            };
        }

        return completed;
    }

    private void BreakProviderLeases(
        string clientRuntimeId,
        string connectionId,
        ClientToolProviderBindingLeaseStatus status,
        string reason)
    {
        foreach (var lease in _leases.Values.Where(lease =>
            string.Equals(lease.ClientRuntimeId, clientRuntimeId, StringComparison.Ordinal) &&
            string.Equals(lease.ConnectionId, connectionId, StringComparison.Ordinal) &&
            lease.Status is ClientToolProviderBindingLeaseStatus.Active))
        {
            _leases[lease.BindingId] = CompleteLease(lease, status, reason);
        }
    }

    private void FailProviderBackgroundOperations(
        string clientRuntimeId,
        string connectionId,
        string message)
    {
        foreach (var (clientOperationId, pending) in _backgroundOperations)
        {
            var binding = pending.Descriptor.Binding;
            if (!string.Equals(binding.ClientRuntimeId, clientRuntimeId, StringComparison.Ordinal) ||
                !string.Equals(binding.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                continue;
            }

            if (_backgroundOperations.TryRemove(clientOperationId, out _))
            {
                pending.TrySetResult(new ClientToolBackgroundOperationResult
                {
                    State = ClientToolBackgroundOperationOutcomeState.Faulted,
                    ErrorMessage = message,
                    ErrorType = "provider_disconnected"
                });
            }
        }
    }

    private static ClientToolProviderBindingLease? GetActiveLease(
        ClientToolProviderBindingLease? lease,
        DateTimeOffset now)
        => lease is not null && IsLeaseActive(lease, now) ? lease : null;

    private ClientToolProviderBindingLease? GetActiveProviderLease(
        string clientRuntimeId,
        DateTimeOffset now)
        => _leases.Values.FirstOrDefault(lease =>
            string.Equals(lease.ClientRuntimeId, clientRuntimeId, StringComparison.Ordinal) &&
            IsLeaseActive(lease, now));

    private static bool IsLeaseActive(ClientToolProviderBindingLease lease, DateTimeOffset now)
        => lease.Status is ClientToolProviderBindingLeaseStatus.Active &&
            (lease.ExpiresAt is null || lease.ExpiresAt > now);

    private static ClientToolProviderBindingLease CompleteLease(
        ClientToolProviderBindingLease lease,
        ClientToolProviderBindingLeaseStatus status,
        string reason)
        => lease with
        {
            Status = status,
            ReleasedAt = DateTimeOffset.UtcNow,
            ReleaseReason = reason
        };

    private static ClientToolProviderConnectionState StateFromReadiness(ClientToolProviderReadiness readiness)
        => readiness switch
        {
            ClientToolProviderReadiness.Ready => ClientToolProviderConnectionState.Ready,
            ClientToolProviderReadiness.Revoked => ClientToolProviderConnectionState.Revoked,
            _ => ClientToolProviderConnectionState.Registered
        };

    private static string CreateClientRuntimeId(ClientToolProviderIdentity identity)
    {
        var stablePart = identity.InstallationId ?? identity.InstanceId;
        if (!string.IsNullOrWhiteSpace(stablePart))
        {
            var sanitized = new string(stablePart
                .Where(static c => char.IsLetterOrDigit(c) || c is '-' or '_')
                .ToArray());

            if (!string.IsNullOrWhiteSpace(sanitized))
                return $"crt_{identity.AppKind}_{sanitized}";
        }

        return $"crt_{identity.AppKind}_{Guid.NewGuid():N}";
    }

    private static ClientToolInvokeOutcomeEvent CreateProviderFailure(string requestId, string message)
        => new()
        {
            RequestId = requestId,
            Outcome = ClientToolInvokeOutcomeKind.Failed,
            ErrorMessage = message
        };

    private sealed record ProviderEntry(
        ClientToolProviderSnapshot Snapshot,
        IClientToolProviderConnection Connection)
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<ClientToolInvokeOutcomeEvent>> _pendingInvocations =
            new(StringComparer.Ordinal);

        public TaskCompletionSource<ClientToolInvokeOutcomeEvent> RegisterPendingInvocation(string invocationId)
        {
            var completion = new TaskCompletionSource<ClientToolInvokeOutcomeEvent>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingInvocations[invocationId] = completion;
            return completion;
        }

        public void RemovePendingInvocation(string invocationId)
            => _pendingInvocations.TryRemove(invocationId, out _);

        public bool TryResolveInvocation(ClientToolProviderInvokeOutcomeMessage outcome)
        {
            if (!_pendingInvocations.TryRemove(outcome.InvocationId, out var completion))
                return false;

            return completion.TrySetResult(new ClientToolInvokeOutcomeEvent
            {
                RequestId = outcome.RequestId,
                Outcome = outcome.Outcome,
                Content = outcome.Content,
                ErrorMessage = outcome.Error?.Message,
                ClientOperationId = outcome.ClientOperationId,
                HandleKind = outcome.HandleKind,
                SupportedOperations = outcome.SupportedOperations,
                Augmentation = outcome.Augmentation,
                ResponderId = Snapshot.ClientRuntimeId,
                ResponderGroup = Snapshot.Manifest?.AppProvider.Name
            });
        }

        public void FailPendingInvocations(string message)
        {
            foreach (var (invocationId, completion) in _pendingInvocations)
            {
                if (_pendingInvocations.TryRemove(invocationId, out _))
                {
                    completion.TrySetResult(CreateProviderFailure(invocationId, message));
                }
            }
        }
    }

    private sealed class NoopClientToolProviderConnection : IClientToolProviderConnection
    {
        public static readonly NoopClientToolProviderConnection Instance = new();

        public ValueTask SendInvocationAsync(
            ClientToolProviderInvokeToolMessage message,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("Provider connection is not available.");
    }

    private sealed class PendingProviderBackgroundOperation
    {
        private readonly TaskCompletionSource<ClientToolBackgroundOperationResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PendingProviderBackgroundOperation(ClientToolProviderBackgroundOperationDescriptor descriptor)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        }

        public ClientToolProviderBackgroundOperationDescriptor Descriptor { get; }

        public Task<ClientToolBackgroundOperationResult> Completion => _completion.Task;

        public bool TrySetResult(ClientToolBackgroundOperationResult result)
            => _completion.TrySetResult(result);
    }
}
