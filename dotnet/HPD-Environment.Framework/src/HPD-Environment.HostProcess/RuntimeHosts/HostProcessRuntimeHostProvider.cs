namespace HPD.Environment.HostProcess;

using System.Runtime.InteropServices;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

/// <summary>
/// Represents the current host as an Environment runtime host without starting
/// an external daemon or VM.
/// </summary>
internal sealed class HostProcessRuntimeHostProvider(
    HostProcessProviderState state)
    : IRuntimeHostProvider
{
    private static readonly ProviderResourceShape Shape = new(
        new TargetKind("runtime-host"),
        TargetRouteSegmentKind.RuntimeHost,
        TargetHandleLifetime.Lease,
        TargetHandleAuthority.Observe | TargetHandleAuthority.Control,
        new SchemaId("hpd.execution.host-process.runtime-host.handle.v1"));

    public ProviderId ProviderId =>
        HostProcessProviderDescriptor.ProviderId;

    public ValueTask<RuntimeHostStatus> EnsureAsync(
        ResourceMetadata<RuntimeHost> metadata,
        RuntimeHostSpec spec,
        RuntimeHostStatus? observed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PlatformSpec current = HostProcessProviderDescriptor.CurrentPlatform();
        if (!string.Equals(
                spec.Platform.OperatingSystem,
                current.OperatingSystem,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                spec.Platform.Architecture,
                current.Architecture,
                StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(Failed(
                metadata,
                "hpd.environment.host-process.platform-mismatch",
                $"Requested platform '{spec.Platform.OperatingSystem}/{spec.Platform.Architecture}' does not match host " +
                $"'{current.OperatingSystem}/{current.Architecture}'."));
        }

        if (OperatingSystem.IsWindows())
        {
            return ValueTask.FromResult(Failed(
                metadata,
                "hpd.environment.host-process.windows-unsupported",
                "Host Process sandboxing is not available on Windows until an accepted enforcement mechanism is implemented."));
        }

        ProviderResourceEntry<
            RuntimeHost,
            RuntimeHostSpec,
            RuntimeHostStatus> entry =
            state.Ledger.Upsert(
                metadata,
                spec,
                new RuntimeHostStatus
                {
                    Phase = ResourcePhase.Ready,
                    HostPhase = RuntimeHostPhase.Ready,
                    ObservedGeneration = metadata.Generation,
                    LastTransitionAt = DateTimeOffset.UtcNow,
                    Generations = new RuntimeHostGenerationStatus
                    {
                        HostStartGeneration =
                            new RuntimeHostStartGeneration(
                                (observed?.Generations.HostStartGeneration?.Value ?? 0) + 1),
                        StartedAt =
                            observed?.Generations.StartedAt ??
                            DateTimeOffset.UtcNow,
                    },
                    GuestControl = new GuestControlStatus(
                        Expected: false,
                        Installed: false,
                        Reachable: false),
                    Readiness =
                        new RuntimeHostReadinessStatus(Ready: true),
                },
                Shape);
        RuntimeHostStatus status = entry.Status with
        {
            Handle = entry.TargetHandle,
            ProviderHandle = entry.ProviderHandle,
        };
        state.Ledger.Upsert(metadata, spec, status, Shape);
        return ValueTask.FromResult(status);
    }

    public ValueTask<RuntimeHostStatus> StopAsync(
        TargetHandle<RuntimeHost> host,
        StopPolicy policy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderResourceEntry<
            RuntimeHost,
            RuntimeHostSpec,
            RuntimeHostStatus> entry = Require(host);
        RuntimeHostStatus stopped = entry.Status with
        {
            HostPhase = RuntimeHostPhase.Stopped,
            LastTransitionAt = DateTimeOffset.UtcNow,
            Readiness = new RuntimeHostReadinessStatus(Ready: false),
        };
        state.Ledger.Upsert(Metadata(entry.Resource), entry.Spec, stopped, Shape);
        return ValueTask.FromResult(stopped);
    }

    public ValueTask DeleteAsync(
        ResourceRef<RuntimeHost> host,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.Ledger.Remove<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus>(host);
        return ValueTask.CompletedTask;
    }

    public ValueTask<RuntimeHostStatus> GetStatusAsync(
        TargetHandle<RuntimeHost> host,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Require(host).Status);
    }

    private ProviderResourceEntry<
        RuntimeHost,
        RuntimeHostSpec,
        RuntimeHostStatus> Require(TargetHandle<RuntimeHost> host)
    {
        ProviderLedgerLookup<
            ProviderResourceEntry<
                RuntimeHost,
                RuntimeHostSpec,
                RuntimeHostStatus>> lookup = state.Ledger.TryGet<
                    RuntimeHost,
                    RuntimeHostSpec,
                    RuntimeHostStatus>(host);
        return lookup.Succeeded
            ? lookup.Entry!
            : throw new InvalidOperationException(
                $"{lookup.Diagnostic!.Code.Value}: {lookup.Diagnostic.Message}");
    }

    private RuntimeHostStatus Failed(
        ResourceMetadata<RuntimeHost> metadata,
        string code,
        string message) =>
        new()
        {
            Phase = ResourcePhase.Failed,
            HostPhase = RuntimeHostPhase.Failed,
            ReconciliationOutcome =
                ResourceReconciliationOutcome.Rejected,
            ObservedGeneration = metadata.Generation,
            Diagnostics =
            [
                Diagnostic(code, message),
            ],
        };

    private Diagnostic Diagnostic(string code, string message) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = new DiagnosticCode(code),
            Message = message,
            ProviderId = ProviderId,
        };

    private static ResourceMetadata<RuntimeHost> Metadata(
        ResourceRef<RuntimeHost> resource) =>
        new()
        {
            Id = resource.Id,
            Kind = new ResourceKind("RuntimeHost"),
            Scope = resource.Scope,
            Generation =
                resource.Generation ?? new ResourceGeneration(1),
            SchemaVersion = new SchemaVersion("1"),
        };
}
