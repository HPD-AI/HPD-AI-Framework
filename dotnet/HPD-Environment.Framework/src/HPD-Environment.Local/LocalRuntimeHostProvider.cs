namespace HPD.Environment.Local;

using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

internal sealed class LocalRuntimeHostProvider(LocalProviderState state)
    : IRuntimeHostProvider
{
    private static readonly ProviderResourceShape Shape = new(
        new TargetKind("runtime-host"),
        TargetRouteSegmentKind.RuntimeHost,
        TargetHandleLifetime.Lease,
        TargetHandleAuthority.Observe | TargetHandleAuthority.Control,
        new SchemaId("hpd.execution.local.runtime-host.handle.v1"));

    public ProviderId ProviderId =>
        LocalEnvironmentProviderDescriptor.ProviderId;

    public ValueTask<RuntimeHostStatus> EnsureAsync(
        ResourceMetadata<RuntimeHost> metadata,
        RuntimeHostSpec spec,
        RuntimeHostStatus? observed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PlatformSpec current =
            LocalEnvironmentProviderDescriptor.CurrentPlatform();
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
                "LocalEnvironment.PlatformMismatch",
                $"Requested host platform '{spec.Platform.OperatingSystem}/{spec.Platform.Architecture}' does not match '{current.OperatingSystem}/{current.Architecture}'."));
        }

        RuntimeHostStartGeneration startGeneration =
            NextStartGeneration(observed);
        bool restarted = observed?.HostPhase is
            RuntimeHostPhase.Stopped or
            RuntimeHostPhase.Failed or
            RuntimeHostPhase.Deleted;
        var status = new RuntimeHostStatus
        {
            Phase = ResourcePhase.Ready,
            HostPhase = RuntimeHostPhase.Ready,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            Generations = new RuntimeHostGenerationStatus
            {
                HostStartGeneration = startGeneration,
                StartedAt = restarted ||
                    observed?.Generations.StartedAt is null
                        ? DateTimeOffset.UtcNow
                        : observed.Generations.StartedAt,
            },
            GuestControl = new GuestControlStatus(
                Expected: false,
                Installed: false,
                Reachable: false),
            Readiness = new RuntimeHostReadinessStatus(
                Ready: true,
                ObservedHostStartGeneration:
                    startGeneration),
        };
        ProviderResourceEntry<
            RuntimeHost,
            RuntimeHostSpec,
            RuntimeHostStatus> entry =
            state.Ledger.Upsert(metadata, spec, status, Shape);
        status = status with
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
        ProviderLedgerLookup<
            ProviderResourceEntry<
                RuntimeHost,
                RuntimeHostSpec,
                RuntimeHostStatus>> lookup = state.Ledger.TryGet<
                    RuntimeHost,
                    RuntimeHostSpec,
                    RuntimeHostStatus>(host);
        if (!lookup.Succeeded)
            throw Error(lookup.Diagnostic!);
        ProviderResourceEntry<
            RuntimeHost,
            RuntimeHostSpec,
            RuntimeHostStatus> entry = lookup.Entry!;
        RuntimeHostStatus stopped = entry.Status with
        {
            Phase = ResourcePhase.Ready,
            HostPhase = RuntimeHostPhase.Stopped,
            LastTransitionAt = DateTimeOffset.UtcNow,
            Readiness = new RuntimeHostReadinessStatus(false),
        };
        state.Ledger.Upsert(
            Metadata(entry.Resource),
            entry.Spec,
            stopped,
            Shape);
        return ValueTask.FromResult(stopped);
    }

    public ValueTask DeleteAsync(
        ResourceRef<RuntimeHost> host,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.Ledger.Remove<
            RuntimeHost,
            RuntimeHostSpec,
            RuntimeHostStatus>(host);
        return ValueTask.CompletedTask;
    }

    public ValueTask<RuntimeHostStatus> GetStatusAsync(
        TargetHandle<RuntimeHost> host,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderLedgerLookup<
            ProviderResourceEntry<
                RuntimeHost,
                RuntimeHostSpec,
                RuntimeHostStatus>> lookup = state.Ledger.TryGet<
                    RuntimeHost,
                    RuntimeHostSpec,
                    RuntimeHostStatus>(host);
        return lookup.Succeeded
            ? ValueTask.FromResult(lookup.Entry!.Status)
            : ValueTask.FromException<RuntimeHostStatus>(
                Error(lookup.Diagnostic!));
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
                new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Code = new DiagnosticCode(code),
                    Message = message,
                    ProviderId = ProviderId,
                },
            ],
        };

    private static RuntimeHostStartGeneration NextStartGeneration(
        RuntimeHostStatus? observed)
    {
        long previous =
            observed?.Generations.HostStartGeneration?.Value ?? 0;
        return observed?.HostPhase is
            RuntimeHostPhase.Stopped or
            RuntimeHostPhase.Failed or
            RuntimeHostPhase.Deleted
                ? new RuntimeHostStartGeneration(
                    checked(Math.Max(1, previous + 1)))
                : new RuntimeHostStartGeneration(
                    Math.Max(1, previous));
    }

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

    private static InvalidOperationException Error(Diagnostic diagnostic) =>
        new($"{diagnostic.Code.Value}: {diagnostic.Message}");
}
