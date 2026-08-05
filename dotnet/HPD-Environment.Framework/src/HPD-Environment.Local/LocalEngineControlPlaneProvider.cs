namespace HPD.Environment.Local;

using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

internal sealed class LocalEngineControlPlaneProvider(
    LocalProviderState state,
    ILocalEngineProbe probe)
    : IEngineControlPlaneProvider
{
    private static readonly ProviderResourceShape Shape = new(
        new TargetKind("engine-control-plane"),
        TargetRouteSegmentKind.ProviderOpaque,
        TargetHandleLifetime.Lease,
        TargetHandleAuthority.Observe | TargetHandleAuthority.Control,
        new SchemaId("hpd.execution.local.engine.handle.v1"));

    public ProviderId ProviderId =>
        LocalEnvironmentProviderDescriptor.ProviderId;

    public async ValueTask<EngineControlPlaneStatus>
        EnsureEngineControlPlaneAsync(
            ResourceMetadata<EngineControlPlane> metadata,
            EngineControlPlaneSpec spec,
            EngineControlPlaneStatus? observed,
            CancellationToken cancellationToken = default)
    {
        if (spec.Host is not { } host ||
            !state.Ledger.TryGet<
                RuntimeHost,
                RuntimeHostSpec,
                RuntimeHostStatus>(host).Succeeded)
        {
            return Failed(
                metadata,
                "LocalEnvironment.EngineHostInvalid",
                "The engine must belong to the current Local runtime host.");
        }
        if (spec.Kind != state.Options.EngineKind ||
            spec.Api != state.Options.EngineApi)
        {
            return Failed(
                metadata,
                "LocalEnvironment.EngineKindUnsupported",
                $"The configured Local provider supports '{state.Options.EngineKind}/{state.Options.EngineApi}'.");
        }

        LocalEngineObservation observation;
        try
        {
            observation = await probe.ProbeAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            state.MarkEngineUnavailable();
            return Failed(
                metadata,
                "LocalEnvironment.EngineProbeFailed",
                exception.GetBaseException().Message);
        }

        EngineAuthorityMode authorityMode = observation.IsRootless
            ? EngineAuthorityMode.Rootless
            : EngineAuthorityMode.Rootful;
        if (spec.AuthorityMode is not
                EngineAuthorityMode.ProviderDefined &&
            spec.AuthorityMode != authorityMode)
        {
            return Failed(
                metadata,
                "LocalEnvironment.EngineAuthorityModeMismatch",
                $"The engine was requested as '{spec.AuthorityMode}' but was observed as '{authorityMode}'.");
        }
        if (authorityMode == EngineAuthorityMode.Rootful &&
            !state.Options.AllowRootfulEngine)
        {
            return Failed(
                metadata,
                "LocalEnvironment.RootfulEngineForbidden",
                "The observed engine is rootful and Local policy forbids rootful engine authority.");
        }
        long generation = state.AcceptEngineObservation(
            observation,
            authorityMode);
        var status = new EngineControlPlaneStatus
        {
            Phase = ResourcePhase.Ready,
            EnginePhase = EngineControlPlanePhase.Ready,
            ReconciliationOutcome =
                ResourceReconciliationOutcome.Accepted,
            EngineGeneration =
                new EngineIncarnationGeneration(generation),
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            ExternalMutationPossible = true,
            Endpoints =
            [
                new EngineApiEndpointStatus(
                    spec.Api,
                    new ProviderNamedEndpoint(
                        "local-engine",
                        ProviderEndpointPurpose.EngineApi,
                        new ProviderEndpoint(
                            "unix",
                            "local-engine"),
                        ProviderTransportKind.UnixSocket,
                        EndpointSensitivity.PrivilegedControl),
                    spec.EndpointPolicy),
            ],
            Conditions =
            [
                new Condition(
                    "LocalEnvironment.EngineReady",
                    ConditionStatus.True,
                    "ProbeSucceeded",
                    $"Docker-compatible engine {observation.ServerVersion} API {observation.ApiVersion} is ready.",
                    DateTimeOffset.UtcNow,
                    metadata.Generation),
            ],
        };
        ProviderResourceEntry<
            EngineControlPlane,
            EngineControlPlaneSpec,
            EngineControlPlaneStatus> entry =
            state.Ledger.Upsert(metadata, spec, status, Shape);
        status = status with { ProviderHandle = entry.ProviderHandle };
        state.Ledger.Upsert(metadata, spec, status, Shape);
        return status;
    }

    public ValueTask<EngineAuthorityBindingPlan> PlanAuthorityBindingAsync(
        EngineControlPlaneStatus engine,
        EngineAuthorityBindingRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (engine.EnginePhase != EngineControlPlanePhase.Ready ||
            engine.ProviderHandle is not { } engineHandle ||
            engineHandle.ProviderId != ProviderId ||
            engineHandle.Generation != state.Ledger.ProviderGeneration)
        {
            return ValueTask.FromResult(Rejected(
                "LocalEnvironment.EngineAuthoritySourceStale",
                "The Local engine observation is not current."));
        }
        ProviderLedgerLookup<
            ProviderResourceEntry<
                ExecutionUnit,
                ExecutionUnitSpec,
                ExecutionUnitStatus>> unit = state.Ledger.TryGet<
                    ExecutionUnit,
                    ExecutionUnitSpec,
                    ExecutionUnitStatus>(request.TargetUnit);
        if (!unit.Succeeded)
        {
            return ValueTask.FromResult(Rejected(
                "LocalEnvironment.EngineAuthorityTargetInvalid",
                unit.Diagnostic!.Message));
        }

        EngineAuthorityMode mode =
            state.CurrentEngineAuthorityMode;
        if (mode is not (
            EngineAuthorityMode.Rootful or
            EngineAuthorityMode.Rootless))
        {
            return ValueTask.FromResult(Rejected(
                "LocalEnvironment.EngineAuthorityModeUnknown",
                "The current engine authority mode is unknown."));
        }
        SensitiveAuthorityClass authorityClass =
            mode == EngineAuthorityMode.Rootless
                ? SensitiveAuthorityClass.RootlessEngineControl
                : SensitiveAuthorityClass.RootfulEngineControl;
        return ValueTask.FromResult(new EngineAuthorityBindingPlan
        {
            Accepted = true,
            PlanId = new EngineAuthorityBindingPlanId(
                $"local-engine-plan-{Guid.NewGuid():N}"),
            SourceEngine = request.Engine,
            Spec = new AuthorityBindingSpec
            {
                Kind = AuthorityBindingKind.HostService,
                Source = new AuthorityBindingSource
                {
                    Kind = AuthoritySourceKind.HostService,
                    Locus = BoundaryLocus.Host,
                    HostService = state.Options.EngineKind ==
                        EngineControlPlaneKind.Podman
                            ? HostServiceKind.PodmanDaemon
                            : HostServiceKind.DockerDaemon,
                },
                Target = new AuthorityBindingTarget(
                    AuthorityTargetKind.ExecutionUnit,
                    Unit: request.TargetUnit),
                Projection = new AuthorityBindingProjection
                {
                    Kind = AuthorityProjectionKind.ProviderDefined,
                    TargetSocketPath = null,
                    ReadOnly = false,
                },
                Policy = new AuthorityBindingPolicy
                {
                    Direction = AuthorityBindingDirection.ProviderToHost,
                    AuthorityClass = authorityClass,
                    EffectiveAuthorityClass = authorityClass,
                    Lease = new SensitiveLeasePolicy
                    {
                        Lifetime = BindingLifetime.Operation,
                        RevokeOnTargetStop = true,
                    },
                    Redaction =
                        SensitiveRedactionLevel.RedactIdentifiers,
                    RequireAudit = true,
                    AllowProviderSideProxy = true,
                    Provenance = request.Provenance,
                },
                AuditLabel = "local-engine-operation",
            },
        });
    }

    public ValueTask<EngineControlPlaneStatus> GetStatusAsync(
        ResourceRef<EngineControlPlane> engine,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderLedgerLookup<
            ProviderResourceEntry<
                EngineControlPlane,
                EngineControlPlaneSpec,
                EngineControlPlaneStatus>> lookup = state.Ledger.TryGet<
                    EngineControlPlane,
                    EngineControlPlaneSpec,
                    EngineControlPlaneStatus>(engine);
        return lookup.Succeeded
            ? ValueTask.FromResult(lookup.Entry!.Status)
            : ValueTask.FromException<EngineControlPlaneStatus>(
                new InvalidOperationException(
                    $"{lookup.Diagnostic!.Code.Value}: {lookup.Diagnostic.Message}"));
    }

    public ValueTask<EngineControlPlaneStatus> StopAsync(
        TargetHandle<EngineControlPlane> engine,
        StopPolicy policy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderLedgerLookup<
            ProviderResourceEntry<
                EngineControlPlane,
                EngineControlPlaneSpec,
                EngineControlPlaneStatus>> lookup = state.Ledger.TryGet<
                    EngineControlPlane,
                    EngineControlPlaneSpec,
                    EngineControlPlaneStatus>(engine);
        if (!lookup.Succeeded)
            throw new InvalidOperationException(lookup.Diagnostic!.Message);
        ProviderResourceEntry<
            EngineControlPlane,
            EngineControlPlaneSpec,
            EngineControlPlaneStatus> entry = lookup.Entry!;
        EngineControlPlaneStatus stopped = entry.Status with
        {
            Phase = ResourcePhase.Ready,
            EnginePhase = EngineControlPlanePhase.Stopped,
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
        ResourceRef<EngineControlPlane> engine,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.Ledger.Remove<
            EngineControlPlane,
            EngineControlPlaneSpec,
            EngineControlPlaneStatus>(engine);
        return ValueTask.CompletedTask;
    }

    private EngineControlPlaneStatus Failed(
        ResourceMetadata<EngineControlPlane> metadata,
        string code,
        string message) =>
        new()
        {
            Phase = ResourcePhase.Failed,
            EnginePhase = EngineControlPlanePhase.Failed,
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

    private EngineAuthorityBindingPlan Rejected(
        string code,
        string message) =>
        new()
        {
            Accepted = false,
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

    private static ResourceMetadata<EngineControlPlane> Metadata(
        ResourceRef<EngineControlPlane> resource) =>
        new()
        {
            Id = resource.Id,
            Kind = new ResourceKind("EngineControlPlane"),
            Scope = resource.Scope,
            Generation =
                resource.Generation ?? new ResourceGeneration(1),
            SchemaVersion = new SchemaVersion("1"),
        };
}
