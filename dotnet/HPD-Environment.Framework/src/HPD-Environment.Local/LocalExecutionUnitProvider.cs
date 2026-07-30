namespace HPD.Environment.Local;

using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

internal sealed class LocalExecutionUnitProvider(LocalProviderState state)
    : IExecutionUnitProvider
{
    private static readonly ProviderResourceShape Shape = new(
        new TargetKind("execution-unit"),
        TargetRouteSegmentKind.ExecutionUnit,
        TargetHandleLifetime.Lease,
        TargetHandleAuthority.Observe |
        TargetHandleAuthority.Control |
        TargetHandleAuthority.Invoke,
        new SchemaId("hpd.execution.local.execution-unit.handle.v1"));

    public ProviderId ProviderId =>
        LocalEnvironmentProviderDescriptor.ProviderId;

    public ValueTask<ExecutionUnitStatus> EnsureAsync(
        ResourceMetadata<ExecutionUnit> metadata,
        ExecutionUnitSpec spec,
        ExecutionUnitStatus? observed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (spec.PreferredHost is not { } host)
            return ValueTask.FromResult(Failed(
                metadata,
                "LocalEnvironment.ExecutionUnitHostRequired",
                "Local execution units require the Local runtime host."));
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
                "LocalEnvironment.ExecutionUnitHostNotReady",
                hostLookup.Diagnostic?.Message ??
                "The Local runtime host is not ready."));
        }

        var status = new ExecutionUnitStatus
        {
            Phase = ResourcePhase.Ready,
            UnitPhase = ExecutionUnitPhase.Ready,
            ObservedGeneration = metadata.Generation,
            AssignedHost = host,
            LastTransitionAt = DateTimeOffset.UtcNow,
        };
        ProviderResourceEntry<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus> entry =
            state.Ledger.Upsert(metadata, spec, status, Shape);
        status = status with
        {
            Handle = entry.TargetHandle,
            NamespaceHandle = entry.ProviderHandle,
            WorkloadStorage = ResolveStorage(
                metadata,
                spec,
                entry.ProviderHandle),
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
            Phase = ResourcePhase.Ready,
            UnitPhase = ExecutionUnitPhase.Stopped,
            LastTransitionAt = DateTimeOffset.UtcNow,
        };
        state.Ledger.Upsert(
            Metadata(entry.Resource),
            entry.Spec,
            stopped,
            Shape);
        return ValueTask.FromResult(stopped);
    }

    public ValueTask DeleteAsync(
        ResourceRef<ExecutionUnit> unit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderLedgerLookup<
            ProviderResourceEntry<
                ExecutionUnit,
                ExecutionUnitSpec,
                ExecutionUnitStatus>> lookup = state.Ledger.TryGet<
                    ExecutionUnit,
                    ExecutionUnitSpec,
                    ExecutionUnitStatus>(unit);
        if (lookup.Succeeded &&
            lookup.Entry!.Status.WorkloadStorage is { } allocation &&
            allocation.PersistenceClass is not
                WorkloadStoragePersistenceClass.Installation)
            DeleteOwnedStorage(allocation.EffectiveRuntimePath);
        state.Ledger.Remove<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus>(unit);
        return ValueTask.CompletedTask;
    }

    private void DeleteOwnedStorage(string effectivePath)
    {
        string allocationsRoot = Path.GetFullPath(
            Path.Combine(state.WorkloadStateRoot, "allocations"));
        string candidate = Path.GetFullPath(effectivePath);
        if (!candidate.StartsWith(
                allocationsRoot + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) ||
            Path.GetDirectoryName(candidate) != allocationsRoot)
            throw new InvalidOperationException(
                "LocalEnvironment.WorkloadStorageOwnershipInvalid: refusing to delete storage outside the provider allocation root.");
        if (Directory.Exists(candidate))
            Directory.Delete(candidate, recursive: true);
    }

    public ValueTask<ExecutionUnitStatus> GetStatusAsync(
        TargetHandle<ExecutionUnit> unit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Require(unit).Status);
    }

    private ProviderResourceEntry<
        ExecutionUnit,
        ExecutionUnitSpec,
        ExecutionUnitStatus> Require(TargetHandle<ExecutionUnit> handle)
    {
        ProviderLedgerLookup<
            ProviderResourceEntry<
                ExecutionUnit,
                ExecutionUnitSpec,
                ExecutionUnitStatus>> lookup = state.Ledger.TryGet<
                    ExecutionUnit,
                    ExecutionUnitSpec,
                    ExecutionUnitStatus>(handle);
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

    private WorkloadStorageAllocation? ResolveStorage(
        ResourceMetadata<ExecutionUnit> metadata,
        ExecutionUnitSpec spec,
        ProviderOpaqueHandle providerHandle)
    {
        if (spec.WorkloadStorage is not { } request)
            return null;
        if (string.IsNullOrWhiteSpace(request.LogicalId) ||
            request.LogicalId.Length > 128 ||
            request.LogicalId.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) ||
                  character is '.' or '_' or '-')) ||
            !Enum.IsDefined(request.PersistenceClass))
            throw new InvalidOperationException(
                "LocalEnvironment.WorkloadStorageRequestInvalid: the workload storage request is malformed.");

        string path = Path.Combine(
            state.WorkloadStateRoot,
            "allocations",
            Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(
                        $"{metadata.Scope.Value}\n{request.LogicalId}")))
                .ToLowerInvariant()[..32]);
        Directory.CreateDirectory(path);
        return new WorkloadStorageAllocation
        {
            LogicalId = request.LogicalId,
            ProviderHandle = providerHandle,
            EffectiveRuntimePath = path,
            PersistenceClass = request.PersistenceClass,
            Generation = metadata.Generation,
        };
    }

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
