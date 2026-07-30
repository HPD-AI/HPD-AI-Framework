namespace HPD.Environment.AppleVirtualization.ExecutionUnits;

using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Environment.AppleVirtualization.Authority;
using HPD.Environment.AppleVirtualization.Handles;
using HPD.Environment.AppleVirtualization.Projections;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.Contracts;

public sealed class AppleVirtualizationExecutionUnitProvider : IExecutionUnitProvider
{
    private static readonly SchemaVersion SchemaVersion = new("v1");
    private static readonly ResourceKind ExecutionUnitKind = new("execution-unit");
    private static readonly ResourceKind RuntimeHostKind = new("runtime-host");
    private static readonly ResourceKind ProcessKind = new("process-invocation");
    private static readonly ResourceKind ContentProjectionKind = new("content-projection");
    private static readonly SchemaId ContextExtensionSchema = new("hpd.execution.apple-virtualization.execution-unit.context.v1");
    private static readonly ContentType JsonContentType = new("application/json");

    private readonly AppleVirtualizationProviderStateLedger _ledger;
    private readonly IAppleVirtualizationHelperClient _helper;
    private readonly AppleVirtualizationContentProjectionProvider? _projectionProvider;
    private readonly AppleVirtualizationAuthorityBindingProvider? _authorityProvider;
    private long _requestSequence;

    internal AppleVirtualizationExecutionUnitProvider(
        AppleVirtualizationProviderStateLedger ledger,
        IAppleVirtualizationHelperClient helper,
        AppleVirtualizationContentProjectionProvider? projectionProvider = null,
        AppleVirtualizationAuthorityBindingProvider? authorityProvider = null)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _helper = helper ?? throw new ArgumentNullException(nameof(helper));
        _projectionProvider = projectionProvider;
        _authorityProvider = authorityProvider;
    }

    public ProviderId ProviderId => AppleVirtualizationProviderDescriptor.ProviderId;

    public async ValueTask<ExecutionUnitStatus> EnsureAsync(
        ResourceMetadata<ExecutionUnit> metadata,
        ExecutionUnitSpec spec,
        ExecutionUnitStatus? observed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        if (observed is not null &&
            !observed.ObservedGeneration.Equals(metadata.Generation) &&
            HasActiveDependents(observed))
        {
            return observed with
            {
                ReconciliationOutcome = ResourceReconciliationOutcome.ImmutableConflict,
                Diagnostics =
                [
                    .. observed.Diagnostics,
                    new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = new DiagnosticCode(
                            "AppleVirtualization.ExecutionUnitReplacementDependentsActive"),
                        Message =
                            "The execution unit cannot be materially reconfigured while guest processes, " +
                            "authority bindings, projections, network memberships, or published endpoints remain active.",
                    },
                ],
            };
        }

        ResourceRef<RuntimeHost>? assignedHost = spec.PreferredHost ?? observed?.AssignedHost;
        if (assignedHost is null)
        {
            return Store(metadata, FailureStatus(
                metadata,
                assignedHost,
                UnitDiagnostics.UnsupportedPlacement("execution-unit/" + metadata.Id.Value)));
        }

        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>> hostLookup =
            _ledger.TryGetRuntimeHost(assignedHost.Value);
        if (!hostLookup.Succeeded)
        {
            Diagnostic diagnostic = hostLookup.Diagnostic ?? UnitDiagnostics.MissingHost("runtime-host/" + assignedHost.Value.Id.Value);
            return Store(metadata, FailureStatus(metadata, assignedHost, diagnostic));
        }

        Diagnostic? terminalHostDiagnostic = TerminalHostDiagnostic(hostLookup.Entry!, "unit.ensure");
        if (terminalHostDiagnostic is not null)
        {
            return Store(metadata, FailureStatus(metadata, assignedHost, terminalHostDiagnostic));
        }

        Diagnostic? hostReadinessDiagnostic = ValidateHostReady(hostLookup.Entry!);
        if (hostReadinessDiagnostic is not null)
        {
            return Store(metadata, WaitingStatus(
                metadata,
                assignedHost,
                ExecutionUnitPhase.Declared,
                UnitDiagnostics.HostReadyCondition(metadata.Generation, ready: false),
                hostReadinessDiagnostic));
        }

        ProjectionReadiness projectionReadiness = ValidateRequiredProjections(spec);
        if (!projectionReadiness.Ready)
        {
            return Store(metadata, WaitingStatus(
                metadata,
                assignedHost,
                ExecutionUnitPhase.ProjectingContent,
                UnitDiagnostics.ProjectionsReadyCondition(metadata.Generation, ready: false),
                projectionReadiness.Diagnostic!));
        }

        string workingDirectory = WorkingDirectoryFor(metadata);
        IReadOnlyDictionary<string, string> environment = EnvironmentFor(metadata, spec);
        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(
                AppleVirtualizationHelperOperation.UnitEnsure,
                AppleVirtualizationHelperProtocol.UnitRequestSchema) with
            {
                ResourceKind = metadata.Kind,
                ResourceId = metadata.Id.Value,
                ResourceScope = metadata.Scope,
                ResourceGeneration = metadata.Generation,
                ProviderGeneration = _ledger.ProviderGeneration,
                UnitEnsureRequest = new AppleVirtualizationUnitEnsureRequest
                {
                    UnitId = metadata.Id.Value,
                    HostId = assignedHost.Value.Id.Value,
                    WorkingDirectory = workingDirectory,
                    Environment = environment,
                },
            },
            cancellationToken).ConfigureAwait(false);

        if (response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error)
        {
            return Store(metadata, FailureStatus(metadata, assignedHost, ToDiagnostic(response.Error, "unit.ensure")));
        }

        AppleVirtualizationUnitStatusResponse? unit = response.UnitStatusResponse;
        ExecutionUnitPhase unitPhase = unit?.UnitPhase ?? ExecutionUnitPhase.Ready;
        ResourcePhase phase = ResourcePhaseFor(unitPhase);
        IReadOnlyList<Condition> conditions = MergeReadinessConditions(
            unit?.Conditions,
            metadata.Generation,
            hostReady: true,
            projectionsReady: true);
        bool sameIncarnation =
            observed is not null &&
            observed.ObservedGeneration.Equals(metadata.Generation);
        ExecutionUnitStatus? storedStatus = sameIncarnation
            ? _ledger.TryGetExecutionUnit(
                    new ResourceRef<ExecutionUnit>(
                        metadata.Id,
                        metadata.Scope,
                        metadata.Generation))
                .Entry?.Status
            : null;

        ExecutionUnitStatus status = new()
        {
            Phase = phase,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            Conditions = conditions,
            UnitPhase = unitPhase,
            AssignedHost = assignedHost,
            RealizedContentProjections = projectionReadiness.ProjectedRefs,
            NetworkMemberships = spec.Network.Memberships,
            AuthorityBindings = sameIncarnation
                ? storedStatus?.AuthorityBindings ??
                    observed!.AuthorityBindings
                : Array.Empty<ResourceRef<AuthorityBinding>>(),
            ActiveProcesses = sameIncarnation
                ? storedStatus?.ActiveProcesses ??
                    observed!.ActiveProcesses
                : Array.Empty<ResourceRef<ProcessInvocation>>(),
            Extensions =
            [
                CreateContextExtension(
                    metadata,
                    assignedHost.Value,
                    unit?.WorkingDirectory ?? workingDirectory,
                    spec,
                    environment),
            ],
        };
        if (spec.WorkloadStorage is { } storage)
        {
            if (string.IsNullOrWhiteSpace(storage.LogicalId) ||
                storage.LogicalId.Length > 128 ||
                storage.LogicalId.Any(character =>
                    !(char.IsAsciiLetterOrDigit(character) ||
                      character is '.' or '_' or '-')) ||
                !Enum.IsDefined(storage.PersistenceClass))
                return Store(metadata, FailureStatus(
                    metadata,
                    assignedHost,
                    new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = new DiagnosticCode(
                            "AppleVirtualization.WorkloadStorageLogicalIdInvalid"),
                        Message =
                            "The workload storage request is malformed.",
                        ProviderId = ProviderId,
                    }));
            status = status with
            {
                WorkloadStorage = new WorkloadStorageAllocation
                {
                    LogicalId = storage.LogicalId,
                    ProviderHandle = new ProviderOpaqueHandle(
                        ProviderId,
                        $"storage:{metadata.Scope.Value}:{metadata.Id.Value}",
                        Generation: _ledger.ProviderGeneration),
                    EffectiveRuntimePath = workingDirectory,
                    PersistenceClass = storage.PersistenceClass,
                    Generation = metadata.Generation,
                },
            };
        }

        return Store(metadata, status, spec);
    }

    public async ValueTask<ExecutionUnitStatus> StopAsync(
        TargetHandle<ExecutionUnit> unit,
        StopPolicy policy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>> lookup =
            _ledger.TryGetExecutionUnit(unit);
        if (!lookup.Succeeded)
        {
            return HandleFailureStatus(unit, lookup.Diagnostic);
        }

        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> entry = lookup.Entry!;
        ExecutionUnitSpec? spec = _ledger.TryGetExecutionUnitSpec(entry.Resource);
        CleanupResult cleanup = await CleanupOwnedResourcesAsync(
            entry,
            spec?.LifecyclePolicy.Cleanup ?? CleanupPolicy.Default,
            policy,
            "unit.stop",
            removeProcesses: false,
            cancellationToken).ConfigureAwait(false);
        if (!cleanup.Succeeded)
        {
            return Store(ToMetadata(entry), entry.Status with
            {
                Phase = ResourcePhase.Degraded,
                UnitPhase = ExecutionUnitPhase.Stopping,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics = AppendDiagnostics(entry.Status.Diagnostics, cleanup.Diagnostics),
            });
        }

        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(
                AppleVirtualizationHelperOperation.UnitStop,
                AppleVirtualizationHelperProtocol.UnitRequestSchema) with
            {
                ResourceKind = ExecutionUnitKind,
                ResourceId = entry.Resource.Id.Value,
                ResourceScope = entry.Resource.Scope,
                ResourceGeneration = entry.Resource.Generation,
                ProviderHandle = entry.ProviderHandle,
                ProviderGeneration = _ledger.ProviderGeneration,
                UnitLifecycleRequest = new AppleVirtualizationUnitLifecycleRequest
                {
                    UnitId = entry.Resource.Id.Value,
                    StopKind = policy.Kind,
                    Reason = policy.ProviderSignal,
                },
            },
            cancellationToken).ConfigureAwait(false);

        ExecutionUnitStatus status = response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error
            ? entry.Status with
            {
                Phase = ResourcePhase.Failed,
                UnitPhase = ExecutionUnitPhase.Failed,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics = AppendDiagnostic(entry.Status.Diagnostics, ToDiagnostic(response.Error, "unit.stop")),
            }
            : entry.Status with
            {
                Phase = ResourcePhaseFor(response.UnitStatusResponse?.UnitPhase ?? ExecutionUnitPhase.Stopped),
                UnitPhase = response.UnitStatusResponse?.UnitPhase ?? ExecutionUnitPhase.Stopped,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Conditions = response.UnitStatusResponse?.Conditions ?? entry.Status.Conditions,
                ActiveProcesses = Array.Empty<ResourceRef<ProcessInvocation>>(),
                RealizedContentProjections = Array.Empty<ResourceRef<ContentProjection>>(),
                AuthorityBindings = Array.Empty<ResourceRef<AuthorityBinding>>(),
            };

        ExecutionUnitStatus stored = Store(ToMetadata(entry), status);
        if (stored.UnitPhase is ExecutionUnitPhase.Stopped or ExecutionUnitPhase.Deleted)
        {
            await ApplyHostEmptyPolicyAsync(stored.AssignedHost, cancellationToken).ConfigureAwait(false);
        }

        return stored;
    }

    public async ValueTask DeleteAsync(ResourceRef<ExecutionUnit> unit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>> lookup =
            _ledger.TryGetExecutionUnit(unit);
        if (!lookup.Succeeded)
        {
            return;
        }

        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> entry = lookup.Entry!;
        ExecutionUnitSpec? spec = _ledger.TryGetExecutionUnitSpec(entry.Resource);
        CleanupResult cleanup = await CleanupOwnedResourcesAsync(
            entry,
            spec?.LifecyclePolicy.Cleanup ?? CleanupPolicy.Default,
            StopPolicy.Default with { Kind = StopKind.Kill, ProviderSignal = "delete" },
            "unit.delete",
            removeProcesses: true,
            cancellationToken).ConfigureAwait(false);
        if (!cleanup.Succeeded)
        {
            Store(ToMetadata(entry), entry.Status with
            {
                Phase = ResourcePhase.Degraded,
                UnitPhase = ExecutionUnitPhase.Deleting,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics = AppendDiagnostics(entry.Status.Diagnostics, cleanup.Diagnostics),
            });
            return;
        }

        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(
                AppleVirtualizationHelperOperation.UnitDelete,
                AppleVirtualizationHelperProtocol.UnitRequestSchema) with
            {
                ResourceKind = ExecutionUnitKind,
                ResourceId = entry.Resource.Id.Value,
                ResourceScope = entry.Resource.Scope,
                ResourceGeneration = entry.Resource.Generation,
                ProviderHandle = entry.ProviderHandle,
                ProviderGeneration = _ledger.ProviderGeneration,
                UnitLifecycleRequest = new AppleVirtualizationUnitLifecycleRequest
                {
                    UnitId = entry.Resource.Id.Value,
                    StopKind = StopKind.Kill,
                    Reason = "delete",
                },
            },
            cancellationToken).ConfigureAwait(false);

        if (response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error)
        {
            Store(ToMetadata(entry), entry.Status with
            {
                Phase = ResourcePhase.Failed,
                UnitPhase = ExecutionUnitPhase.Deleting,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics = AppendDiagnostic(entry.Status.Diagnostics, ToDiagnostic(response.Error, "unit.delete")),
            });
            return;
        }

        ResourceRef<RuntimeHost>? assignedHost = entry.Status.AssignedHost;
        _ledger.RemoveExecutionUnit(unit);
        await ApplyHostEmptyPolicyAsync(assignedHost, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ExecutionUnitStatus> GetStatusAsync(
        TargetHandle<ExecutionUnit> unit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>> lookup =
            _ledger.TryGetExecutionUnit(unit);
        if (!lookup.Succeeded)
        {
            return HandleFailureStatus(unit, lookup.Diagnostic);
        }

        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> entry = lookup.Entry!;
        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(
                AppleVirtualizationHelperOperation.UnitStatus,
                AppleVirtualizationHelperProtocol.UnitRequestSchema) with
            {
                ResourceKind = ExecutionUnitKind,
                ResourceId = entry.Resource.Id.Value,
                ResourceScope = entry.Resource.Scope,
                ResourceGeneration = entry.Resource.Generation,
                ProviderHandle = entry.ProviderHandle,
                ProviderGeneration = _ledger.ProviderGeneration,
                UnitLifecycleRequest = new AppleVirtualizationUnitLifecycleRequest
                {
                    UnitId = entry.Resource.Id.Value,
                },
            },
            cancellationToken).ConfigureAwait(false);

        if (response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error)
        {
            ExecutionUnitStatus failed = entry.Status with
            {
                Phase = ResourcePhase.Degraded,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics = AppendDiagnostic(entry.Status.Diagnostics, ToDiagnostic(response.Error, "unit.status")),
            };
            return Store(ToMetadata(entry), failed);
        }

        if (response.UnitStatusResponse is null)
        {
            return entry.Status;
        }

        ExecutionUnitStatus status = entry.Status with
        {
            Phase = ResourcePhaseFor(response.UnitStatusResponse.UnitPhase),
            UnitPhase = response.UnitStatusResponse.UnitPhase,
            LastTransitionAt = DateTimeOffset.UtcNow,
            Conditions = response.UnitStatusResponse.Conditions,
        };

        return Store(ToMetadata(entry), status);
    }

    private AppleVirtualizationHelperEnvelope Request(
        AppleVirtualizationHelperOperation operation,
        SchemaId schema) =>
        AppleVirtualizationHelperEnvelope.Request(
            operation,
            "apple-vz-unit-" + Interlocked.Increment(ref _requestSequence).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Interlocked.Read(ref _requestSequence),
            schema);

    private async ValueTask<CleanupResult> CleanupOwnedResourcesAsync(
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> entry,
        CleanupPolicy cleanupPolicy,
        StopPolicy policy,
        string reason,
        bool removeProcesses,
        CancellationToken cancellationToken)
    {
        Diagnostic[]? diagnostics = null;

        if (cleanupPolicy.RevokeAuthorityBindingsFirst)
        {
            ResourceRef<AuthorityBinding>[] authorityBindings = _ledger.GetAuthorityBindingsForExecutionUnit(entry.Resource);
            for (int i = 0; i < authorityBindings.Length; i++)
            {
                Diagnostic? diagnostic = await RevokeOwnedAuthorityBindingAsync(
                    entry,
                    authorityBindings[i],
                    reason,
                    cancellationToken).ConfigureAwait(false);
                if (diagnostic is not null)
                {
                    diagnostics = AddDiagnostic(diagnostics, diagnostic);
                }
            }
        }

        if (diagnostics is not null)
        {
            return CleanupResult.Failed(diagnostics);
        }

        for (int i = 0; i < entry.Status.ActiveProcesses.Count; i++)
        {
            ResourceRef<ProcessInvocation> process = entry.Status.ActiveProcesses[i];
            Diagnostic? diagnostic = await StopOwnedProcessAsync(
                entry,
                process,
                policy,
                reason,
                removeProcesses,
                cancellationToken).ConfigureAwait(false);
            if (diagnostic is not null)
            {
                diagnostics = AddDiagnostic(diagnostics, diagnostic);
            }
        }

        if (diagnostics is not null)
        {
            return CleanupResult.Failed(diagnostics);
        }

        for (int i = 0; i < entry.Status.RealizedContentProjections.Count; i++)
        {
            ResourceRef<ContentProjection> projection = entry.Status.RealizedContentProjections[i];
            ProjectionCleanupResult projectionCleanup = await CleanupOwnedProjectionAsync(
                entry,
                projection,
                cleanupPolicy,
                reason,
                cancellationToken).ConfigureAwait(false);
            if (!projectionCleanup.Succeeded)
            {
                diagnostics = AddDiagnostics(diagnostics, projectionCleanup.Diagnostics);
            }
        }

        return diagnostics is null ? CleanupResult.Success : CleanupResult.Failed(diagnostics);
    }

    private async ValueTask<Diagnostic?> RevokeOwnedAuthorityBindingAsync(
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit,
        ResourceRef<AuthorityBinding> binding,
        string reason,
        CancellationToken cancellationToken)
    {
        _ = reason;
        if (!_ledger.AuthorityBindingTargetsExecutionUnit(binding, unit.Resource))
        {
            return UnitDiagnostics.CleanupFailed(
                "authority-binding/" + binding.Id.Value,
                "Authority binding is attached to the execution unit status but does not target this execution unit.");
        }

        if (_authorityProvider is null)
        {
            return UnitDiagnostics.CleanupFailed(
                "authority-binding/" + binding.Id.Value,
                "Execution-unit cleanup could not revoke the authority binding because no authority provider was available.");
        }

        try
        {
            await _authorityProvider.RevokeAuthorityBindingAsync(binding, cancellationToken).ConfigureAwait(false);
            _ledger.DetachAuthorityBindingFromExecutionUnit(unit.Resource, binding);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return UnitDiagnostics.CleanupFailed(
                "authority-binding/" + binding.Id.Value,
                "Execution-unit cleanup failed to revoke an authority binding: " + ex.Message);
        }
    }

    private async ValueTask ApplyHostEmptyPolicyAsync(
        ResourceRef<RuntimeHost>? assignedHost,
        CancellationToken cancellationToken)
    {
        if (assignedHost is null)
        {
            return;
        }

        AppleVirtualizationHostEmptyPolicyEvaluation evaluation = _ledger.RefreshRuntimeHostEmptyPolicy(assignedHost.Value);
        if (evaluation.Host is null ||
            evaluation.Action != AppleVirtualizationHostEmptyPolicyAction.StopNow)
        {
            return;
        }

        AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> host = evaluation.Host;
        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(
                AppleVirtualizationHelperOperation.HostRequestStop,
                AppleVirtualizationHelperProtocol.HostRequestSchema) with
            {
                ResourceKind = RuntimeHostKind,
                ResourceId = host.Resource.Id.Value,
                ResourceScope = host.Resource.Scope,
                ResourceGeneration = host.Resource.Generation,
                ProviderHandle = host.ProviderHandle,
                ProviderGeneration = _ledger.ProviderGeneration,
                HostLifecycleRequest = new AppleVirtualizationHostLifecycleRequest
                {
                    HostId = host.Resource.Id.Value,
                    StopKind = StopKind.Graceful,
                    Reason = "empty-host",
                },
            },
            cancellationToken).ConfigureAwait(false);

        RuntimeHostStatus status = response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error
            ? host.Status with
            {
                Phase = ResourcePhase.Degraded,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics = AppendDiagnostic(
                    host.Status.Diagnostics,
                    RuntimeHostIdlePolicyDiagnostics.HelperError(response.Error, "host.requestStop")),
            }
            : host.Status with
            {
                Phase = response.HostStatusResponse?.Phase ?? ResourcePhase.Ready,
                HostPhase = response.HostStatusResponse?.HostPhase ?? RuntimeHostPhase.Stopped,
                LastTransitionAt = DateTimeOffset.UtcNow,
                ExecutionUnits = Array.Empty<ResourceRef<ExecutionUnit>>(),
                Diagnostics = evaluation.Diagnostic is null
                    ? host.Status.Diagnostics
                    : AppendDiagnosticIfMissing(host.Status.Diagnostics, evaluation.Diagnostic),
            };

        _ledger.UpsertRuntimeHost(ToHostMetadata(host), status);
    }

    private async ValueTask<Diagnostic?> StopOwnedProcessAsync(
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit,
        ResourceRef<ProcessInvocation> process,
        StopPolicy policy,
        string reason,
        bool removeProcess,
        CancellationToken cancellationToken)
    {
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus>> lookup =
            _ledger.TryGetProcessInvocation(process);
        if (!lookup.Succeeded)
        {
            return UnitDiagnostics.CleanupFailed(
                "process-invocation/" + process.Id.Value,
                lookup.Diagnostic?.Message ?? "Active process could not be resolved during execution-unit cleanup.");
        }

        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry = lookup.Entry!;
        if (IsTerminal(entry.Status))
        {
            if (removeProcess)
            {
                _ledger.RemoveProcessInvocation(entry.Resource);
            }

            return null;
        }

        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(
                AppleVirtualizationHelperOperation.ProcessStop,
                AppleVirtualizationHelperProtocol.ProcessRequestSchema) with
            {
                ResourceKind = ProcessKind,
                ResourceId = entry.Resource.Id.Value,
                ResourceScope = entry.Resource.Scope,
                ResourceGeneration = entry.Resource.Generation,
                ProviderHandle = entry.ProviderHandle,
                ProviderGeneration = _ledger.ProviderGeneration,
                ProcessStopRequest = new AppleVirtualizationProcessStopRequest(
                    entry.Resource.Id.Value,
                    policy.Kind,
                    policy.GracePeriod,
                    reason),
            },
            cancellationToken).ConfigureAwait(false);

        if (response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error)
        {
            Diagnostic diagnostic = ToDiagnostic(response.Error, "process.stop");
            StoreProcess(entry, entry.Status with
            {
                Diagnostics = AppendDiagnostic(entry.Status.Diagnostics, diagnostic),
                LastTransitionAt = DateTimeOffset.UtcNow,
            });
            return UnitDiagnostics.CleanupFailed("process-invocation/" + process.Id.Value, diagnostic.Message);
        }

        ProcessInvocationResult? result = response.ProcessStatusResponse?.Result;
        StoreProcess(entry, entry.Status with
        {
            Phase = ResourcePhase.Ready,
            ProcessPhase = response.ProcessStatusResponse?.ProcessPhase ?? ProcessInvocationPhase.Stopped,
            IoState = response.ProcessStatusResponse?.IoState ?? ProcessIoState.Closed,
            Result = result ?? entry.Status.Result,
            ExitedAt = result?.ExitedAt ?? DateTimeOffset.UtcNow,
            LastTransitionAt = DateTimeOffset.UtcNow,
        });

        if (removeProcess)
        {
            _ledger.RemoveProcessInvocation(entry.Resource);
        }

        return null;
    }

    private async ValueTask<ProjectionCleanupResult> CleanupOwnedProjectionAsync(
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit,
        ResourceRef<ContentProjection> projection,
        CleanupPolicy cleanupPolicy,
        string reason,
        CancellationToken cancellationToken)
    {
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus>> lookup =
            _ledger.TryGetContentProjection(projection);
        if (!lookup.Succeeded)
        {
            return ProjectionCleanupResult.Failed(UnitDiagnostics.CleanupFailed(
                "content-projection/" + projection.Id.Value,
                lookup.Diagnostic?.Message ?? "Realized projection could not be resolved during execution-unit cleanup."));
        }

        if (_ledger.IsContentProjectionReferencedByOtherUnit(projection, unit.Resource))
        {
            return ProjectionCleanupResult.Success;
        }

        AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> entry = lookup.Entry!;
        ContentProjectionSpec? spec = _ledger.TryGetContentProjectionSpec(entry.Resource);
        ProjectionCleanupDecision decision = ProjectionCleanupDecisionFor(spec);
        if (decision == ProjectionCleanupDecision.RetainForRuntimeFinalization)
        {
            StoreProjection(entry, entry.Status with
            {
                Diagnostics = AppendDiagnosticIfMissing(
                    entry.Status.Diagnostics,
                    ProjectionRetainedDiagnostic(entry, "OnRuntimeEnd", "Projection is retained for runtime-end finalization instead of being released during execution-unit cleanup.")),
                LastTransitionAt = DateTimeOffset.UtcNow,
            });
            return ProjectionCleanupResult.Success;
        }

        if (decision == ProjectionCleanupDecision.RetainForExplicitPromotion)
        {
            Diagnostic diagnostic = ProjectionRetainedDiagnostic(
                entry,
                "PromoteExplicitly",
                "Projection requires explicit promotion and is retained instead of being auto-promoted or released during cleanup.");
            StoreProjection(entry, entry.Status with
            {
                Phase = ResourcePhase.Degraded,
                ProjectionPhase = ContentProjectionPhase.Degraded,
                Diagnostics = AppendDiagnosticIfMissing(entry.Status.Diagnostics, diagnostic),
                LastTransitionAt = DateTimeOffset.UtcNow,
            });
            return cleanupPolicy.FailureMode == CleanupFailureMode.FailOperation
                ? ProjectionCleanupResult.Failed(UnitDiagnostics.CleanupFailed("content-projection/" + projection.Id.Value, diagnostic.Message))
                : ProjectionCleanupResult.Success;
        }

        if (decision == ProjectionCleanupDecision.FinalizeBeforeRelease)
        {
            FinalizationResult finalization = await FinalizeOwnedProjectionAsync(
                entry,
                reason,
                cancellationToken).ConfigureAwait(false);
            if (!AppleVirtualizationContentProjectionProvider.FinalizationSucceeded(finalization))
            {
                ProjectionCleanupResult failure = await HandleProjectionFinalizationFailureAsync(
                    entry,
                    cleanupPolicy,
                    reason,
                    finalization,
                    cancellationToken).ConfigureAwait(false);
                if (!failure.Succeeded ||
                    cleanupPolicy.FailureMode != CleanupFailureMode.BestEffortRelease)
                {
                    return failure;
                }
            }
        }

        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(
                AppleVirtualizationHelperOperation.ProjectionRelease,
                AppleVirtualizationHelperProtocol.ProjectionRequestSchema) with
            {
                ResourceKind = ContentProjectionKind,
                ResourceId = entry.Resource.Id.Value,
                ResourceScope = entry.Resource.Scope,
                ResourceGeneration = entry.Resource.Generation,
                ProviderHandle = entry.ProviderHandle,
                ProviderGeneration = _ledger.ProviderGeneration,
                ProjectionLifecycleRequest = new AppleVirtualizationProjectionLifecycleRequest
                {
                    ProjectionId = entry.Resource.Id.Value,
                    FinalizeBeforeRelease = false,
                    Reason = reason,
                },
            },
            cancellationToken).ConfigureAwait(false);

        if (response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error)
        {
            Diagnostic diagnostic = ToDiagnostic(response.Error, "projection.release");
            StoreProjection(entry, entry.Status with
            {
                Phase = ResourcePhase.Degraded,
                ProjectionPhase = ContentProjectionPhase.Degraded,
                Diagnostics = AppendDiagnostic(entry.Status.Diagnostics, diagnostic),
                LastTransitionAt = DateTimeOffset.UtcNow,
            });
            return ProjectionCleanupResult.Failed(UnitDiagnostics.CleanupFailed("content-projection/" + projection.Id.Value, diagnostic.Message));
        }

        _ledger.RemoveContentProjection(entry.Resource);
        return ProjectionCleanupResult.Success;
    }

    private async ValueTask<FinalizationResult> FinalizeOwnedProjectionAsync(
        AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> entry,
        string reason,
        CancellationToken cancellationToken)
    {
        if (_projectionProvider is null)
        {
            return new FinalizationResult
            {
                CompletedAt = DateTimeOffset.UnixEpoch,
                Conditions =
                [
                    new Condition(
                        AppleVirtualizationContentProjectionProvider.FinalizationFailedCondition,
                        ConditionStatus.False,
                        "ProjectionProviderUnavailable",
                        "Execution-unit cleanup could not finalize the content projection because no projection provider was available.",
                        DateTimeOffset.UtcNow,
                        entry.Resource.Generation ?? default,
                        DiagnosticSeverity.Error),
                ],
            };
        }

        return await _projectionProvider.FinalizeAsync(
            entry.TargetHandle,
            new FinalizationRequest
            {
                Kind = FinalizationKind.ManifestAndChangedContent,
                IncludeDeletedEntries = true,
                IncludeProvenance = true,
                ProducerId = reason,
            },
            events: null,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ProjectionCleanupResult> HandleProjectionFinalizationFailureAsync(
        AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> entry,
        CleanupPolicy cleanupPolicy,
        string reason,
        FinalizationResult finalization,
        CancellationToken cancellationToken)
    {
        Diagnostic diagnostic = ProjectionFinalizationFailedDiagnostic(entry, finalization);
        switch (cleanupPolicy.FailureMode)
        {
            case CleanupFailureMode.BestEffortRelease:
                StoreProjection(entry, entry.Status with
                {
                    Diagnostics = AppendDiagnosticIfMissing(entry.Status.Diagnostics, diagnostic),
                    LastTransitionAt = DateTimeOffset.UtcNow,
                });
                return ProjectionCleanupResult.Success;
            case CleanupFailureMode.MarkDegradedAndRetain:
                StoreProjection(entry, entry.Status with
                {
                    Phase = ResourcePhase.Degraded,
                    ProjectionPhase = ContentProjectionPhase.Degraded,
                    Diagnostics = AppendDiagnosticIfMissing(entry.Status.Diagnostics, diagnostic),
                    LastTransitionAt = DateTimeOffset.UtcNow,
                });
                return ProjectionCleanupResult.Success;
            default:
                StoreProjection(entry, entry.Status with
                {
                    Phase = ResourcePhase.Degraded,
                    ProjectionPhase = ContentProjectionPhase.Degraded,
                    Diagnostics = AppendDiagnosticIfMissing(entry.Status.Diagnostics, diagnostic),
                    LastTransitionAt = DateTimeOffset.UtcNow,
                });
                return ProjectionCleanupResult.Failed(UnitDiagnostics.CleanupFailed("content-projection/" + entry.Resource.Id.Value, diagnostic.Message));
        }
    }

    private AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> StoreProcess(
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry,
        ProcessInvocationStatus status)
    {
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> stored = _ledger.UpsertProcessInvocation(
            new ResourceMetadata<ProcessInvocation>
            {
                Id = entry.Resource.Id,
                Kind = ProcessKind,
                Scope = entry.Resource.Scope,
                Generation = entry.Resource.Generation ?? new ResourceGeneration(1),
                SchemaVersion = SchemaVersion,
                CreatedAt = entry.CreatedAt,
                UpdatedAt = entry.UpdatedAt,
            },
            status);
        if (IsTerminal(status))
        {
            _ledger.DetachProcessFromAllExecutionUnits(stored.Resource);
        }

        return stored;
    }

    private AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> StoreProjection(
        AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> entry,
        ContentProjectionStatus status) =>
        _ledger.UpsertContentProjection(
            new ResourceMetadata<ContentProjection>
            {
                Id = entry.Resource.Id,
                Kind = ContentProjectionKind,
                Scope = entry.Resource.Scope,
                Generation = entry.Resource.Generation ?? new ResourceGeneration(1),
                SchemaVersion = SchemaVersion,
                CreatedAt = entry.CreatedAt,
                UpdatedAt = entry.UpdatedAt,
            },
            status);

    private ExecutionUnitStatus Store(ResourceMetadata<ExecutionUnit> metadata, ExecutionUnitStatus status, ExecutionUnitSpec? spec = null) =>
        _ledger.UpsertExecutionUnit(metadata, status, spec).Status;

    private static bool HasActiveDependents(ExecutionUnitStatus status) =>
        status.UnitPhase is ExecutionUnitPhase.Starting or ExecutionUnitPhase.Running or
            ExecutionUnitPhase.Stopping or ExecutionUnitPhase.Deleting ||
        status.ActiveProcesses.Count > 0 ||
        status.AuthorityBindings.Count > 0 ||
        status.RealizedContentProjections.Count > 0 ||
        status.NetworkMemberships.Count > 0 ||
        status.PublishedEndpoints.Count > 0;

    private static ResourceMetadata<ExecutionUnit> ToMetadata(
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> entry) =>
        new()
        {
            Id = entry.Resource.Id,
            Kind = ExecutionUnitKind,
            Scope = entry.Resource.Scope,
            Generation = entry.Resource.Generation ?? default,
            SchemaVersion = SchemaVersion,
            CreatedAt = entry.CreatedAt,
            UpdatedAt = entry.UpdatedAt,
        };

    private static ResourceMetadata<RuntimeHost> ToHostMetadata(
        AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> entry) =>
        new()
        {
            Id = entry.Resource.Id,
            Kind = RuntimeHostKind,
            Scope = entry.Resource.Scope,
            Generation = entry.Resource.Generation ?? default,
            SchemaVersion = SchemaVersion,
            CreatedAt = entry.CreatedAt,
            UpdatedAt = entry.UpdatedAt,
        };

    private static ExecutionUnitStatus FailureStatus(
        ResourceMetadata<ExecutionUnit> metadata,
        ResourceRef<RuntimeHost>? assignedHost,
        Diagnostic diagnostic) =>
        new()
        {
            Phase = ResourcePhase.Failed,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            UnitPhase = ExecutionUnitPhase.Failed,
            AssignedHost = assignedHost,
            Diagnostics = [diagnostic],
        };

    private static ExecutionUnitStatus WaitingStatus(
        ResourceMetadata<ExecutionUnit> metadata,
        ResourceRef<RuntimeHost>? assignedHost,
        ExecutionUnitPhase unitPhase,
        Condition condition,
        Diagnostic diagnostic) =>
        new()
        {
            Phase = ResourcePhase.Reconciling,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            UnitPhase = unitPhase,
            AssignedHost = assignedHost,
            Conditions = [condition],
            Diagnostics = [diagnostic],
            ActiveProcesses = Array.Empty<ResourceRef<ProcessInvocation>>(),
            RealizedContentProjections = Array.Empty<ResourceRef<ContentProjection>>(),
            NetworkMemberships = Array.Empty<ResourceRef<NetworkMembership>>(),
            AuthorityBindings = Array.Empty<ResourceRef<AuthorityBinding>>(),
        };

    private static ExecutionUnitStatus HandleFailureStatus(
        TargetHandle<ExecutionUnit> handle,
        Diagnostic? diagnostic) =>
        new()
        {
            Phase = ResourcePhase.Failed,
            ObservedGeneration = default,
            LastTransitionAt = DateTimeOffset.UtcNow,
            UnitPhase = ExecutionUnitPhase.Failed,
            Diagnostics =
            [
                diagnostic ?? UnitDiagnostics.HandleLookupFailed(handle.Route.BackingResourceId ?? "unknown"),
            ],
        };

    private static IReadOnlyList<Diagnostic> AppendDiagnostic(IReadOnlyList<Diagnostic> existing, Diagnostic diagnostic)
    {
        Diagnostic[] diagnostics = new Diagnostic[existing.Count + 1];
        for (int i = 0; i < existing.Count; i++)
        {
            diagnostics[i] = existing[i];
        }

        diagnostics[^1] = diagnostic;
        return diagnostics;
    }

    private static IReadOnlyList<Diagnostic> AppendDiagnosticIfMissing(IReadOnlyList<Diagnostic> existing, Diagnostic diagnostic)
    {
        for (int i = 0; i < existing.Count; i++)
        {
            if (existing[i].Code == diagnostic.Code &&
                string.Equals(existing[i].TargetPath, diagnostic.TargetPath, StringComparison.Ordinal))
            {
                return existing;
            }
        }

        return AppendDiagnostic(existing, diagnostic);
    }

    private static IReadOnlyList<Diagnostic> AppendDiagnostics(IReadOnlyList<Diagnostic> existing, IReadOnlyList<Diagnostic> additions)
    {
        if (additions.Count == 0)
        {
            return existing;
        }

        Diagnostic[] diagnostics = new Diagnostic[existing.Count + additions.Count];
        for (int i = 0; i < existing.Count; i++)
        {
            diagnostics[i] = existing[i];
        }

        for (int i = 0; i < additions.Count; i++)
        {
            diagnostics[existing.Count + i] = additions[i];
        }

        return diagnostics;
    }

    private static Diagnostic[] AddDiagnostic(Diagnostic[]? existing, Diagnostic diagnostic)
    {
        if (existing is null)
        {
            return [diagnostic];
        }

        Diagnostic[] diagnostics = new Diagnostic[existing.Length + 1];
        Array.Copy(existing, diagnostics, existing.Length);
        diagnostics[^1] = diagnostic;
        return diagnostics;
    }

    private static Diagnostic[] AddDiagnostics(Diagnostic[]? existing, IReadOnlyList<Diagnostic> additions)
    {
        if (additions.Count == 0)
        {
            return existing ?? [];
        }

        if (existing is null || existing.Length == 0)
        {
            var diagnostics = new Diagnostic[additions.Count];
            for (int i = 0; i < additions.Count; i++)
            {
                diagnostics[i] = additions[i];
            }

            return diagnostics;
        }

        Diagnostic[] combined = new Diagnostic[existing.Length + additions.Count];
        Array.Copy(existing, combined, existing.Length);
        for (int i = 0; i < additions.Count; i++)
        {
            combined[existing.Length + i] = additions[i];
        }

        return combined;
    }

    private static ProjectionCleanupDecision ProjectionCleanupDecisionFor(ContentProjectionSpec? spec)
    {
        if (spec is null)
        {
            return ProjectionCleanupDecision.ReleaseOnly;
        }

        return spec.FinalizationPolicy switch
        {
            FinalizationPolicy.Required or FinalizationPolicy.OnExecutionUnitStop => ProjectionCleanupDecision.FinalizeBeforeRelease,
            FinalizationPolicy.OnRuntimeEnd => ProjectionCleanupDecision.RetainForRuntimeFinalization,
            FinalizationPolicy.PromoteExplicitly => ProjectionCleanupDecision.RetainForExplicitPromotion,
            _ => ProjectionCleanupDecision.ReleaseOnly,
        };
    }

    private static Diagnostic ProjectionRetainedDiagnostic(
        AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> entry,
        string reason,
        string message) =>
        new()
        {
            Severity = DiagnosticSeverity.Warning,
            Code = new DiagnosticCode("AppleVirtualization.ProjectionRetainedDuringCleanup"),
            Message = message,
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = "content-projection/" + entry.Resource.Id.Value,
        };

    private static Diagnostic ProjectionFinalizationFailedDiagnostic(
        AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> entry,
        FinalizationResult finalization) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = new DiagnosticCode("AppleVirtualization.ProjectionFinalizationRequiredFailed"),
            Message = finalization.Conditions.Count == 0
                ? "Required projection finalization failed during execution-unit cleanup."
                : finalization.Conditions[0].Message,
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = "content-projection/" + entry.Resource.Id.Value,
        };

    private static bool IsTerminal(ProcessInvocationStatus status) =>
        status.Result is not null ||
        status.ProcessPhase is ProcessInvocationPhase.Exited or ProcessInvocationPhase.Failed or ProcessInvocationPhase.Stopped;

    private static Diagnostic ToDiagnostic(AppleVirtualizationHelperError? error, string operation)
    {
        if (error is null)
        {
            return UnitDiagnostics.HelperError(operation, "The Apple Virtualization helper returned an error response without an error payload.");
        }

        return new Diagnostic
        {
            Severity = error.Severity,
            Code = new DiagnosticCode(error.Code),
            Message = error.Message,
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = error.Operation ?? operation,
            Detail = error.Detail.IsEmpty || error.DetailSchema is null
                ? null
                : new ProviderExtensionData(
                    AppleVirtualizationProviderDescriptor.ProviderId,
                    error.DetailSchema.Value,
                    JsonContentType,
                    error.Detail),
        };
    }

    private static Diagnostic? ValidateHostReady(AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> host)
    {
        RuntimeHostStatus status = host.Status;
        if (status.Phase == ResourcePhase.Ready &&
            status.HostPhase == RuntimeHostPhase.Ready &&
            status.Readiness?.Ready == true &&
            status.GuestControl?.Reachable == true)
        {
            return null;
        }

        return UnitDiagnostics.HostNotReady("runtime-host/" + host.Resource.Id.Value, status.HostPhase, status.Phase);
    }

    private static Diagnostic? TerminalHostDiagnostic(
        AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> host,
        string targetPath)
    {
        RuntimeHostStatus status = host.Status;
        if (status.Phase != ResourcePhase.Failed && status.HostPhase != RuntimeHostPhase.Failed)
        {
            return null;
        }

        if (status.Diagnostics.Count == 0)
        {
            return UnitDiagnostics.HostNotReady("runtime-host/" + host.Resource.Id.Value, status.HostPhase, status.Phase);
        }

        Diagnostic diagnostic = status.Diagnostics[0];
        return diagnostic with { TargetPath = targetPath };
    }

    private ProjectionReadiness ValidateRequiredProjections(ExecutionUnitSpec spec)
    {
        if (spec.ContentProjections.Count == 0)
        {
            return ProjectionReadiness.ReadyState(Array.Empty<ResourceRef<ContentProjection>>());
        }

        ResourceRef<ContentProjection>[] projected = new ResourceRef<ContentProjection>[spec.ContentProjections.Count];
        for (int i = 0; i < spec.ContentProjections.Count; i++)
        {
            ResourceRef<ContentProjection> projection = spec.ContentProjections[i];
            AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus>> lookup =
                _ledger.TryGetContentProjection(projection);
            if (!lookup.Succeeded)
            {
                return ProjectionReadiness.Waiting(
                    lookup.Diagnostic ?? UnitDiagnostics.ProjectionMissing("content-projection/" + projection.Id.Value));
            }

            ContentProjectionStatus status = lookup.Entry!.Status;
            if (status.Phase != ResourcePhase.Ready || status.ProjectionPhase != ContentProjectionPhase.Projected)
            {
                return ProjectionReadiness.Waiting(
                    UnitDiagnostics.ProjectionNotProjected(
                        "content-projection/" + projection.Id.Value,
                        status.ProjectionPhase,
                        status.Phase));
            }

            projected[i] = projection;
        }

        return ProjectionReadiness.ReadyState(projected);
    }

    private static ResourcePhase ResourcePhaseFor(ExecutionUnitPhase unitPhase) =>
        unitPhase switch
        {
            ExecutionUnitPhase.Unknown or ExecutionUnitPhase.Declared or ExecutionUnitPhase.ProjectingContent => ResourcePhase.Reconciling,
            ExecutionUnitPhase.Ready or ExecutionUnitPhase.Running => ResourcePhase.Ready,
            ExecutionUnitPhase.Stopping => ResourcePhase.Deleting,
            ExecutionUnitPhase.Stopped => ResourcePhase.Ready,
            ExecutionUnitPhase.Deleting => ResourcePhase.Deleting,
            ExecutionUnitPhase.Deleted => ResourcePhase.Deleted,
            ExecutionUnitPhase.Failed => ResourcePhase.Failed,
            _ => ResourcePhase.Unknown,
        };

    private static IReadOnlyList<Condition> MergeReadinessConditions(
        IReadOnlyList<Condition>? helperConditions,
        ResourceGeneration generation,
        bool hostReady,
        bool projectionsReady)
    {
        int helperCount = helperConditions?.Count ?? 0;
        Condition[] conditions = new Condition[helperCount + 2];
        if (helperConditions is not null)
        {
            for (int i = 0; i < helperConditions.Count; i++)
            {
                conditions[i] = helperConditions[i];
            }
        }

        conditions[helperCount] = UnitDiagnostics.HostReadyCondition(generation, hostReady);
        conditions[helperCount + 1] = UnitDiagnostics.ProjectionsReadyCondition(generation, projectionsReady);
        return conditions;
    }

    private static string WorkingDirectoryFor(ResourceMetadata<ExecutionUnit> metadata) =>
        "/hpd/units/" + metadata.Id.Value;

    private static IReadOnlyDictionary<string, string> EnvironmentFor(
        ResourceMetadata<ExecutionUnit> metadata,
        ExecutionUnitSpec spec)
    {
        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            ["HPD_EXECUTION_UNIT_ID"] = metadata.Id.Value,
            ["HPD_RESOURCE_SCOPE"] = metadata.Scope.Value,
        };

        if (!string.IsNullOrWhiteSpace(spec.Identity.User))
        {
            environment["HPD_EXECUTION_USER"] = spec.Identity.User!;
        }

        if (!string.IsNullOrWhiteSpace(spec.Identity.Group))
        {
            environment["HPD_EXECUTION_GROUP"] = spec.Identity.Group!;
        }

        return environment;
    }

    private static ProviderExtensionData CreateContextExtension(
        ResourceMetadata<ExecutionUnit> metadata,
        ResourceRef<RuntimeHost> assignedHost,
        string workingDirectory,
        ExecutionUnitSpec spec,
        IReadOnlyDictionary<string, string> environment)
    {
        string[] projections = ResourceIds(spec.ContentProjections);
        string[] memberships = ResourceIds(spec.Network.Memberships);
        var payload = new AppleVirtualizationExecutionUnitContextExtension
        {
            UnitId = metadata.Id.Value,
            HostId = assignedHost.Id.Value,
            WorkingDirectory = workingDirectory,
            IdentityUser = spec.Identity.User,
            IdentityGroup = spec.Identity.Group,
            Environment = environment,
            ContentProjectionIds = projections,
            NetworkMembershipIds = memberships,
        };

        return new ProviderExtensionData(
            AppleVirtualizationProviderDescriptor.ProviderId,
            ContextExtensionSchema,
            JsonContentType,
            JsonSerializer.SerializeToUtf8Bytes(
                payload,
                AppleVirtualizationExecutionUnitJsonContext.Default.AppleVirtualizationExecutionUnitContextExtension));
    }

    private static string[] ResourceIds<TResource>(IReadOnlyList<ResourceRef<TResource>> refs)
        where TResource : IExecutionResourceMarker
    {
        if (refs.Count == 0)
        {
            return [];
        }

        string[] ids = new string[refs.Count];
        for (int i = 0; i < refs.Count; i++)
        {
            ids[i] = refs[i].Id.Value;
        }

        return ids;
    }

    private readonly record struct ProjectionReadiness(
        bool Ready,
        IReadOnlyList<ResourceRef<ContentProjection>> ProjectedRefs,
        Diagnostic? Diagnostic)
    {
        public static ProjectionReadiness ReadyState(IReadOnlyList<ResourceRef<ContentProjection>> refs) =>
            new(true, refs, Diagnostic: null);

        public static ProjectionReadiness Waiting(Diagnostic diagnostic) =>
            new(false, Array.Empty<ResourceRef<ContentProjection>>(), diagnostic);
    }

    private readonly record struct CleanupResult(bool Succeeded, IReadOnlyList<Diagnostic> Diagnostics)
    {
        public static CleanupResult Success { get; } = new(true, Array.Empty<Diagnostic>());

        public static CleanupResult Failed(IReadOnlyList<Diagnostic> diagnostics) => new(false, diagnostics);
    }
}

internal enum ProjectionCleanupDecision
{
    ReleaseOnly,
    FinalizeBeforeRelease,
    RetainForRuntimeFinalization,
    RetainForExplicitPromotion,
}

internal readonly record struct ProjectionCleanupResult(bool Succeeded, IReadOnlyList<Diagnostic> Diagnostics)
{
    public static ProjectionCleanupResult Success { get; } = new(true, Array.Empty<Diagnostic>());

    public static ProjectionCleanupResult Failed(Diagnostic diagnostic) => new(false, [diagnostic]);
}

internal sealed record AppleVirtualizationExecutionUnitContextExtension
{
    public required string UnitId { get; init; }
    public required string HostId { get; init; }
    public required string WorkingDirectory { get; init; }
    public string? IdentityUser { get; init; }
    public string? IdentityGroup { get; init; }
    public IReadOnlyDictionary<string, string> Environment { get; init; } = EmptyStringDictionary.Value;
    public IReadOnlyList<string> ContentProjectionIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> NetworkMembershipIds { get; init; } = Array.Empty<string>();
}

internal static class UnitDiagnostics
{
    public static readonly DiagnosticCode MissingHostCode = new("AppleVirtualization.ExecutionUnitMissingHost");
    public static readonly DiagnosticCode HostNotReadyCode = new("AppleVirtualization.ExecutionUnitHostNotReady");
    public static readonly DiagnosticCode ProjectionMissingCode = new("AppleVirtualization.ExecutionUnitProjectionMissing");
    public static readonly DiagnosticCode ProjectionNotProjectedCode = new("AppleVirtualization.ExecutionUnitProjectionNotProjected");
    public static readonly DiagnosticCode UnsupportedPlacementCode = new("AppleVirtualization.ExecutionUnitUnsupportedPlacement");
    public static readonly DiagnosticCode HelperErrorCode = new("AppleVirtualization.ExecutionUnitHelperError");
    public static readonly DiagnosticCode CleanupFailedCode = new("AppleVirtualization.ExecutionUnitCleanupFailed");

    public static Diagnostic MissingHost(string targetPath) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = MissingHostCode,
            Message = "The Apple Virtualization execution unit could not resolve its assigned runtime host.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    public static Diagnostic HostNotReady(string targetPath, RuntimeHostPhase hostPhase, ResourcePhase phase) =>
        new()
        {
            Severity = DiagnosticSeverity.Warning,
            Code = HostNotReadyCode,
            Message = $"The Apple Virtualization execution unit is waiting for its assigned runtime host to become HPD-ready. Host phase: {hostPhase}; resource phase: {phase}.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    public static Diagnostic ProjectionMissing(string targetPath) =>
        new()
        {
            Severity = DiagnosticSeverity.Warning,
            Code = ProjectionMissingCode,
            Message = "The Apple Virtualization execution unit is waiting for a required content projection ledger entry.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    public static Diagnostic ProjectionNotProjected(string targetPath, ContentProjectionPhase projectionPhase, ResourcePhase phase) =>
        new()
        {
            Severity = DiagnosticSeverity.Warning,
            Code = ProjectionNotProjectedCode,
            Message = $"The Apple Virtualization execution unit is waiting for a required projection to be guest-verified. Projection phase: {projectionPhase}; resource phase: {phase}.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    public static Diagnostic UnsupportedPlacement(string targetPath) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = UnsupportedPlacementCode,
            Message = "The Apple Virtualization execution unit requires a preferred host or previously assigned host for first-slice placement.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    public static Diagnostic HelperError(string targetPath, string message) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = HelperErrorCode,
            Message = message,
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    public static Diagnostic CleanupFailed(string targetPath, string message) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = CleanupFailedCode,
            Message = message,
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    public static Diagnostic HandleLookupFailed(string targetPath) =>
        AppleVirtualizationHandleDiagnostics.Missing(
            AppleVirtualizationProviderDescriptor.ProviderId,
            "execution-unit/" + targetPath);

    public static Condition HostReadyCondition(ResourceGeneration generation, bool ready) =>
        new(
            "AppleVirtualization.ExecutionUnitHostReady",
            ready ? ConditionStatus.True : ConditionStatus.False,
            ready ? "HostReady" : "HostNotReady",
            ready
                ? "The assigned runtime host is HPD-ready for execution-unit realization."
                : "The execution unit is waiting for the assigned runtime host to become HPD-ready.",
            DateTimeOffset.UtcNow,
            generation,
            ready ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning);

    public static Condition ProjectionsReadyCondition(ResourceGeneration generation, bool ready) =>
        new(
            "AppleVirtualization.ExecutionUnitProjectionsReady",
            ready ? ConditionStatus.True : ConditionStatus.False,
            ready ? "ProjectionsReady" : "ProjectionsNotReady",
            ready
                ? "All required execution-unit content projections are guest-verified."
                : "The execution unit is waiting for required content projections to become guest-verified.",
            DateTimeOffset.UtcNow,
            generation,
            ready ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning);
}

internal static class RuntimeHostIdlePolicyDiagnostics
{
    public static Diagnostic HelperError(AppleVirtualizationHelperError? error, string operation)
    {
        if (error is null)
        {
            return new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = new DiagnosticCode("AppleVirtualization.RuntimeHostIdlePolicyHelperError"),
                Message = "The Apple Virtualization helper returned an empty host stop error without an error payload.",
                ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                TargetPath = operation,
            };
        }

        return new Diagnostic
        {
            Severity = error.Severity,
            Code = new DiagnosticCode(error.Code),
            Message = error.Message,
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = error.Operation ?? operation,
        };
    }
}

[JsonSerializable(typeof(AppleVirtualizationExecutionUnitContextExtension))]
internal sealed partial class AppleVirtualizationExecutionUnitJsonContext : JsonSerializerContext;
