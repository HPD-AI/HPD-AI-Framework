namespace HPD.Environment.HostProcess;

using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

/// <summary>
/// Owns the logical namespace used to associate sandboxed processes with one
/// Host Process execution unit.
/// </summary>
internal sealed class HostProcessExecutionUnitProvider(
    HostProcessProviderState state)
    : IExecutionUnitProvider
{
    private static readonly ProviderResourceShape Shape = new(
        new TargetKind("execution-unit"),
        TargetRouteSegmentKind.ExecutionUnit,
        TargetHandleLifetime.Lease,
        TargetHandleAuthority.Observe |
        TargetHandleAuthority.Control |
        TargetHandleAuthority.Invoke,
        new SchemaId("hpd.execution.host-process.execution-unit.handle.v1"));

    public ProviderId ProviderId =>
        HostProcessProviderDescriptor.ProviderId;

    public ValueTask<ExecutionUnitStatus> EnsureAsync(
        ResourceMetadata<ExecutionUnit> metadata,
        ExecutionUnitSpec spec,
        ExecutionUnitStatus? observed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (spec.PreferredHost is not { } host)
        {
            return ValueTask.FromResult(Failed(
                metadata,
                "hpd.environment.host-process.execution-unit-host-required",
                "Host Process execution units require a Host Process runtime host."));
        }

        ProviderLedgerLookup<
            ProviderResourceEntry<
                RuntimeHost,
                RuntimeHostSpec,
                RuntimeHostStatus>> hostLookup = state.Ledger.TryGet<
                    RuntimeHost,
                    RuntimeHostSpec,
                    RuntimeHostStatus>(host);
        if (!hostLookup.Succeeded ||
            hostLookup.Entry!.Status.HostPhase != RuntimeHostPhase.Ready)
        {
            return ValueTask.FromResult(Failed(
                metadata,
                "hpd.environment.host-process.execution-unit-host-not-ready",
                hostLookup.Diagnostic?.Message ??
                "The Host Process runtime host is not ready."));
        }

        ProviderResourceEntry<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus> entry =
            state.Ledger.Upsert(
                metadata,
                spec,
                new ExecutionUnitStatus
                {
                    Phase = ResourcePhase.Ready,
                    UnitPhase = ExecutionUnitPhase.Ready,
                    AssignedHost = host,
                    ObservedGeneration = metadata.Generation,
                    LastTransitionAt = DateTimeOffset.UtcNow,
                },
                Shape);
        ExecutionUnitStatus status = entry.Status with
        {
            Handle = entry.TargetHandle,
            NamespaceHandle = entry.ProviderHandle,
        };
        state.Ledger.Upsert(metadata, spec, status, Shape);
        return ValueTask.FromResult(status);
    }

    public ValueTask<ExecutionUnitStatus> StopAsync(
        TargetHandle<ExecutionUnit> unit,
        StopPolicy policy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderResourceEntry<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus> entry = Require(unit);
        ExecutionUnitStatus stopped = entry.Status with
        {
            UnitPhase = ExecutionUnitPhase.Stopped,
            LastTransitionAt = DateTimeOffset.UtcNow,
        };
        state.Ledger.Upsert(Metadata(entry.Resource), entry.Spec, stopped, Shape);
        return ValueTask.FromResult(stopped);
    }

    public ValueTask DeleteAsync(
        ResourceRef<ExecutionUnit> unit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.Ledger.Remove<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus>(unit);
        return ValueTask.CompletedTask;
    }

    public ValueTask<ExecutionUnitStatus> GetStatusAsync(
        TargetHandle<ExecutionUnit> unit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Require(unit).Status);
    }

    internal static ProviderLedgerLookup<
        ProviderResourceEntry<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus>> Lookup(
                HostProcessProviderState state,
                TargetHandle<ExecutionUnit> unit) =>
        state.Ledger.TryGet<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus>(unit);

    private ProviderResourceEntry<
        ExecutionUnit,
        ExecutionUnitSpec,
        ExecutionUnitStatus> Require(
            TargetHandle<ExecutionUnit> unit)
    {
        ProviderLedgerLookup<
            ProviderResourceEntry<
                ExecutionUnit,
                ExecutionUnitSpec,
                ExecutionUnitStatus>> lookup = Lookup(state, unit);
        return lookup.Succeeded
            ? lookup.Entry!
            : throw new InvalidOperationException(
                $"{lookup.Diagnostic!.Code.Value}: {lookup.Diagnostic.Message}");
    }

    private ExecutionUnitStatus Failed(
        ResourceMetadata<ExecutionUnit> metadata,
        string code,
        string message) =>
        new()
        {
            Phase = ResourcePhase.Failed,
            UnitPhase = ExecutionUnitPhase.Failed,
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

    private static ResourceMetadata<ExecutionUnit> Metadata(
        ResourceRef<ExecutionUnit> resource) =>
        new()
        {
            Id = resource.Id,
            Kind = new ResourceKind("ExecutionUnit"),
            Scope = resource.Scope,
            Generation =
                resource.Generation ?? new ResourceGeneration(1),
            SchemaVersion = new SchemaVersion("1"),
        };
}
