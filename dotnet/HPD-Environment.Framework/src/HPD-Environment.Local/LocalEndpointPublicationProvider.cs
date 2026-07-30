namespace HPD.Environment.Local;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

internal sealed class LocalEndpointPublicationProvider(
    LocalProviderState state)
    : IEndpointPublicationProvider
{
    private static readonly ProviderResourceShape Shape = new(
        new TargetKind("published-endpoint"),
        TargetRouteSegmentKind.Endpoint,
        TargetHandleLifetime.LiveCapability,
        TargetHandleAuthority.Observe |
        TargetHandleAuthority.Control,
        new SchemaId("hpd.execution.local.endpoint.handle.v1"));

    private readonly ConcurrentDictionary<string, LocalTcpPublication>
        _publications = new(StringComparer.Ordinal);

    public ProviderId ProviderId =>
        LocalEnvironmentProviderDescriptor.ProviderId;

    public ValueTask<PublishedEndpointStatus>
        EnsurePublishedEndpointAsync(
            ResourceMetadata<PublishedEndpoint> metadata,
            PublishedEndpointSpec spec,
            PublishedEndpointStatus? observed,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resource = new ResourceRef<PublishedEndpoint>(
            metadata.Id,
            metadata.Scope,
            metadata.Generation);
        ProviderLedgerLookup<
            ProviderResourceEntry<
                PublishedEndpoint,
                PublishedEndpointSpec,
                PublishedEndpointStatus>> existing =
            state.Ledger.TryGet<
                PublishedEndpoint,
                PublishedEndpointSpec,
                PublishedEndpointStatus>(resource);
        if (existing.Succeeded)
        {
            if (existing.Entry!.Spec != spec)
                return ValueTask.FromResult(Failed(
                    metadata,
                    Error(
                        "LocalEnvironment.EndpointSpecConflict",
                        "A published endpoint with the same identity already exists with a different immutable specification.")));
            if (_publications.ContainsKey(metadata.Id.Value) &&
                state.IsEndpointBoundToCurrentEngine(
                    metadata.Id.Value))
                return ValueTask.FromResult(
                    existing.Entry.Status);
        }
        else if (existing.Diagnostic?.Code.Value !=
                 "hpd.environment.provider-ledger.resource-unknown")
        {
            return ValueTask.FromResult(Failed(
                metadata,
                existing.Diagnostic!));
        }
        Diagnostic? invalid = Validate(spec);
        if (invalid is not null)
            return ValueTask.FromResult(Failed(metadata, invalid));

        if (_publications.TryRemove(
                metadata.Id.Value,
                out LocalTcpPublication? previous))
        {
            previous.Dispose();
        }

        IPAddress targetAddress = ToIPAddress(spec.Target.Address!.Value);
        int targetPort = spec.Target.Port!.Value.Value;
        IPAddress listenAddress = IPAddress.Loopback;
        int requestedPort =
            spec.Listener.Ports?.Start.Value ?? 0;
        var publication = new LocalTcpPublication(
            listenAddress,
            requestedPort,
            targetAddress,
            targetPort);
        try
        {
            publication.Start();
            if (!_publications.TryAdd(metadata.Id.Value, publication))
                throw new InvalidOperationException(
                    "The Local endpoint identity is already active.");
            state.BindEndpointToCurrentEngine(metadata.Id.Value);
        }
        catch
        {
            _publications.TryRemove(metadata.Id.Value, out _);
            state.ReleaseEndpoint(metadata.Id.Value);
            publication.Dispose();
            throw;
        }

        ushort port = checked((ushort)publication.LocalPort);
        var status = new PublishedEndpointStatus
        {
            Phase = ResourcePhase.Ready,
            EndpointPhase = PublishedEndpointPhase.Bound,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            BoundListener = new BoundEndpoint(
                EndpointListenerKind.HostAddress,
                NetworkTransport.Tcp,
                new IpAddressValue(
                    NetworkAddressFamily.IPv4,
                    0,
                    0x7f000001),
                new PortRange(new NetworkPort(port), 1),
                Socket: null),
            Route = new EndpointRouteStatus(
                spec.Target,
                new NetworkEndpointHandle(
                    $"local-loopback:{port}"),
                spec.Target.Address,
                spec.Target.Port,
                ResolvedSocketPath: null),
            PublicationOrigin =
                EndpointPublicationOrigin.Explicit,
            Conditions =
            [
                new Condition(
                    "LocalEnvironment.EndpointLoopbackOnly",
                    ConditionStatus.True,
                    "LoopbackProxyBound",
                    "The endpoint is bound to an HPD-owned loopback listener.",
                    DateTimeOffset.UtcNow,
                    metadata.Generation),
            ],
        };
        ProviderResourceEntry<
            PublishedEndpoint,
            PublishedEndpointSpec,
            PublishedEndpointStatus> entry =
            state.Ledger.Upsert(metadata, spec, status, Shape);
        status = status with { RouterHandle = entry.TargetHandle };
        state.Ledger.Upsert(metadata, spec, status, Shape);
        return ValueTask.FromResult(status);
    }

    public ValueTask<PublishedEndpointStatus> GetStatusAsync(
        ResourceRef<PublishedEndpoint> endpoint,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderLedgerLookup<
            ProviderResourceEntry<
                PublishedEndpoint,
                PublishedEndpointSpec,
                    PublishedEndpointStatus>> lookup = state.Ledger.TryGet<
                    PublishedEndpoint,
                    PublishedEndpointSpec,
                    PublishedEndpointStatus>(endpoint);
        if (lookup.Succeeded &&
            !state.IsEndpointBoundToCurrentEngine(endpoint.Id.Value))
        {
            if (_publications.TryRemove(
                    endpoint.Id.Value,
                    out LocalTcpPublication? stale))
                stale.Dispose();
            PublishedEndpointStatus failed = lookup.Entry!.Status with
            {
                Phase = ResourcePhase.Failed,
                EndpointPhase = PublishedEndpointPhase.Failed,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics =
                [
                    Error(
                        "LocalEnvironment.EndpointEngineIncarnationStale",
                        "The endpoint belongs to a prior or unavailable engine incarnation and was revoked."),
                ],
            };
            return ValueTask.FromResult(failed);
        }
        return lookup.Succeeded
            ? ValueTask.FromResult(lookup.Entry!.Status)
            : ValueTask.FromException<PublishedEndpointStatus>(
                new InvalidOperationException(
                    $"{lookup.Diagnostic!.Code.Value}: {lookup.Diagnostic.Message}"));
    }

    public ValueTask ReleasePublishedEndpointAsync(
        ResourceRef<PublishedEndpoint> endpoint,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_publications.TryRemove(
                endpoint.Id.Value,
                out LocalTcpPublication? publication))
        {
            publication.Dispose();
        }
        state.ReleaseEndpoint(endpoint.Id.Value);
        state.Ledger.Remove<
            PublishedEndpoint,
            PublishedEndpointSpec,
            PublishedEndpointStatus>(endpoint);
        return ValueTask.CompletedTask;
    }

    private Diagnostic? Validate(PublishedEndpointSpec spec)
    {
        if (spec.ExposurePolicy.Scope !=
            EndpointExposureScope.HostLocal ||
            !spec.AuthorizationPolicy.RequireLoopbackClient)
        {
            return Error(
                "LocalEnvironment.EndpointExposureRejected",
                "Local endpoint publication requires host-local scope and loopback-client authorization.");
        }
        if (spec.Listener.Kind != EndpointListenerKind.HostAddress ||
            spec.Listener.Transport != NetworkTransport.Tcp ||
            spec.Listener.Address is not { } listener ||
            !ToIPAddress(listener).Equals(IPAddress.Loopback))
        {
            return Error(
                "LocalEnvironment.EndpointListenerRejected",
                "The first Local endpoint lane accepts only IPv4 loopback TCP listeners.");
        }
        if (spec.Listener.Ports is { } requested &&
            requested.Count != 1)
        {
            return Error(
                "LocalEnvironment.EndpointPortRangeRejected",
                "Local endpoint publication accepts a single port or an ephemeral listener.");
        }
        if (spec.Listener.Ports is null &&
            !spec.ExposurePolicy.AllowEphemeralPort)
        {
            return Error(
                "LocalEnvironment.EndpointEphemeralPortRequired",
                "An omitted listener port requires explicit ephemeral-port permission.");
        }
        if (spec.Target.Kind != EndpointTargetKind.NetworkAddress ||
            spec.Target.Transport != NetworkTransport.Tcp ||
            spec.Target.Address is null ||
            spec.Target.Port is null ||
            !ToIPAddress(spec.Target.Address.Value).Equals(
                IPAddress.Loopback))
        {
            return Error(
                "LocalEnvironment.EndpointTargetUnsupported",
                "The first Local endpoint lane requires an exact provider-owned IPv4 loopback TCP target.");
        }
        return null;
    }

    private PublishedEndpointStatus Failed(
        ResourceMetadata<PublishedEndpoint> metadata,
        Diagnostic diagnostic) =>
        new()
        {
            Phase = ResourcePhase.Failed,
            EndpointPhase = PublishedEndpointPhase.Failed,
            ReconciliationOutcome =
                ResourceReconciliationOutcome.Rejected,
            ObservedGeneration = metadata.Generation,
            Diagnostics = [diagnostic],
        };

    private Diagnostic Error(string code, string message) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = new DiagnosticCode(code),
            Message = message,
            ProviderId = ProviderId,
        };

    private static IPAddress ToIPAddress(IpAddressValue value)
    {
        if (value.Family == NetworkAddressFamily.IPv4 &&
            value.HighBits == 0 &&
            value.LowBits <= uint.MaxValue)
        {
            uint address = checked((uint)value.LowBits);
            return new IPAddress(
            [
                (byte)(address >> 24),
                (byte)(address >> 16),
                (byte)(address >> 8),
                (byte)address,
            ]);
        }
        throw new InvalidOperationException(
            "The first Local endpoint lane supports only bounded IPv4 addresses.");
    }
}

internal sealed class LocalTcpPublication : IDisposable
{
    private readonly TcpListener _listener;
    private readonly IPAddress _targetAddress;
    private readonly int _targetPort;
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _acceptLoop;
    private int _disposed;

    public LocalTcpPublication(
        IPAddress listenAddress,
        int listenPort,
        IPAddress targetAddress,
        int targetPort)
    {
        _listener = new TcpListener(listenAddress, listenPort);
        _targetAddress = targetAddress;
        _targetPort = targetPort;
    }

    public int LocalPort =>
        ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Start()
    {
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_lifetime.Token);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _lifetime.Cancel();
        _listener.Stop();
        _lifetime.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient inbound;
            try
            {
                inbound = await _listener.AcceptTcpClientAsync(
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            _ = ProxyAsync(inbound, cancellationToken);
        }
    }

    private async Task ProxyAsync(
        TcpClient inbound,
        CancellationToken cancellationToken)
    {
        using (inbound)
        using (var outbound = new TcpClient())
        {
            try
            {
                await outbound.ConnectAsync(
                        _targetAddress,
                        _targetPort,
                        cancellationToken)
                    .ConfigureAwait(false);
                using NetworkStream left = inbound.GetStream();
                using NetworkStream right = outbound.GetStream();
                await Task.WhenAny(
                        left.CopyToAsync(right, cancellationToken),
                        right.CopyToAsync(left, cancellationToken))
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException)
            {
            }
            catch (IOException)
            {
            }
        }
    }
}
