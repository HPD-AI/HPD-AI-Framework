namespace HPD.Environment.AppleVirtualization.Engines;

using System.Globalization;
using HPD.Environment.AppleVirtualization.Handles;
using HPD.Environment.AppleVirtualization.GuestAgent;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.Contracts;

public sealed class AppleVirtualizationEngineControlPlaneProvider : IEngineControlPlaneProvider
{
    private static readonly DiagnosticCode BootstrapAbsentCode = new("AppleVirtualization.EngineBootstrapAbsent");
    private static readonly DiagnosticCode HostRequiredCode = new("AppleVirtualization.EngineHostRequired");
    private static readonly DiagnosticCode HostNotReadyCode = new("AppleVirtualization.EngineHostNotReady");
    private static readonly DiagnosticCode HelperPayloadMissingCode = new("AppleVirtualization.EngineStatusMissingPayload");
    private static readonly DiagnosticCode AuthorityModeUnsupportedCode = new("AppleVirtualization.EngineAuthorityModeUnsupported");
    private static readonly DiagnosticCode WorkloadAdoptionUnsupportedCode = new("AppleVirtualization.EngineWorkloadAdoptionUnsupported");
    private static readonly DiagnosticCode ImageStoreUnsupportedCode = new("AppleVirtualization.EngineImageStoreUnsupported");
    private static readonly DiagnosticCode KubernetesUnsupportedCode = new("AppleVirtualization.EngineKubernetesUnsupported");

    private readonly AppleVirtualizationProviderStateLedger _ledger;
    private readonly IAppleVirtualizationHelperClient _helper;
    private readonly AppleVirtualizationProviderOptions _options;
    private long _requestSequence;

    internal AppleVirtualizationEngineControlPlaneProvider(
        AppleVirtualizationProviderStateLedger ledger,
        IAppleVirtualizationHelperClient helper,
        AppleVirtualizationProviderOptions options)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _helper = helper ?? throw new ArgumentNullException(nameof(helper));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ProviderId ProviderId => AppleVirtualizationProviderDescriptor.ProviderId;

    public ValueTask<EngineAuthorityBindingPlan> PlanAuthorityBindingAsync(
        EngineControlPlaneStatus engine,
        EngineAuthorityBindingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        bool accepted = AppleVirtualizationEngineEndpointAuthority.TryCreateBindingSpec(
            engine,
            request.Api,
            request.TargetUnit,
            request.TargetSocketPath,
            request.Provenance,
            out AuthorityBindingSpec? spec,
            out Diagnostic? diagnostic);
        return ValueTask.FromResult(new EngineAuthorityBindingPlan
        {
            Accepted = accepted,
            SourceEngine = request.Engine,
            Spec = spec,
            Diagnostics = diagnostic is null ? Array.Empty<Diagnostic>() : [diagnostic],
        });
    }

    public async ValueTask<EngineControlPlaneStatus> EnsureEngineControlPlaneAsync(
        ResourceMetadata<EngineControlPlane> metadata,
        EngineControlPlaneSpec spec,
        EngineControlPlaneStatus? observed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        Diagnostic[] diagnostics = ValidateSpec(spec);
        if (FindFatal(diagnostics) is { } fatal)
        {
            return Store(metadata, spec, Status(
                metadata,
                ResourcePhase.Failed,
                EngineControlPlanePhase.Failed,
                diagnostics,
                endpoints: Array.Empty<EngineApiEndpointStatus>()));
        }

        if (spec.Host is not { } host)
        {
            Diagnostic diagnostic = Diagnostic(
                DiagnosticSeverity.Error,
                HostRequiredCode,
                "Apple Virtualization EngineControlPlane resources must be associated with a RuntimeHost.",
                "engine.host");
            return Store(metadata, spec, Status(
                metadata,
                ResourcePhase.Failed,
                EngineControlPlanePhase.Failed,
                Append(diagnostics, diagnostic),
                endpoints: Array.Empty<EngineApiEndpointStatus>()));
        }

        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>> hostLookup =
            _ledger.TryGetRuntimeHost(host);
        if (!hostLookup.Succeeded || hostLookup.Entry is null)
        {
            return Store(metadata, spec, Status(
                metadata,
                ResourcePhase.Failed,
                EngineControlPlanePhase.Failed,
                Append(diagnostics, hostLookup.Diagnostic ?? AppleVirtualizationHandleDiagnostics.Missing(ProviderId, "runtime-host/" + host.Id.Value)),
                endpoints: Array.Empty<EngineApiEndpointStatus>()));
        }

        AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> hostEntry = hostLookup.Entry;
        if (hostEntry.Status.Readiness?.Ready != true)
        {
            Diagnostic diagnostic = Diagnostic(
                DiagnosticSeverity.Info,
                HostNotReadyCode,
                "Engine status is pending because the associated RuntimeHost is not HPD-ready.",
                "engine.host.readiness");
            return Store(metadata, spec, Status(
                metadata,
                ResourcePhase.Pending,
                EngineControlPlanePhase.Pending,
                Append(diagnostics, diagnostic),
                endpoints: Array.Empty<EngineApiEndpointStatus>()));
        }

        if (!_options.FeatureGates.EnableEngineControlPlane || !_options.EngineBootstrap.Enabled)
        {
            Diagnostic diagnostic = Diagnostic(
                DiagnosticSeverity.Warning,
                BootstrapAbsentCode,
                "EngineControlPlane was requested, but Apple Virtualization engine bootstrap is not enabled for this provider instance.",
                "engine.bootstrap");
            return Store(metadata, spec, Status(
                metadata,
                ResourcePhase.Degraded,
                EngineControlPlanePhase.Pending,
                Append(diagnostics, diagnostic),
                endpoints: Array.Empty<EngineApiEndpointStatus>()));
        }

        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(AppleVirtualizationHelperOperation.EngineStatus, AppleVirtualizationHelperProtocol.EngineStatusRequestSchema) with
            {
                ResourceKind = metadata.Kind,
                ResourceId = metadata.Id.Value,
                ResourceScope = metadata.Scope,
                ResourceGeneration = metadata.Generation,
                ProviderHandle = hostEntry.ProviderHandle,
                ProviderGeneration = _ledger.ProviderGeneration,
                EngineStatusRequest = new AppleVirtualizationEngineStatusRequest
                {
                    HostId = host.Id.Value,
                    ProviderGeneration = _ledger.ProviderGeneration,
                    HostStartGeneration = (ulong)Math.Max(
                        0,
                        hostEntry.Status.Generations.HostStartGeneration?.Value ?? 0),
                    EngineId = metadata.Id.Value,
                    Kind = spec.Kind,
                    Api = spec.Api,
                    AuthorityMode = spec.AuthorityMode,
                    ImageStore = spec.ImageStore,
                    WorkloadAdoption = spec.WorkloadAdoption,
                    ExplicitRealMode = _options.FeatureGates.EnableRealVmBoot,
                    ScriptedObservationState = _options.EngineBootstrap.ScriptedObservationState,
                },
            },
            cancellationToken).ConfigureAwait(false);

        if (response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error)
        {
            return Store(metadata, spec, Status(
                metadata,
                ResourcePhase.Failed,
                EngineControlPlanePhase.Failed,
                Append(diagnostics, ToDiagnostic(response.Error, "engine.status")),
                endpoints: Array.Empty<EngineApiEndpointStatus>()));
        }

        if (response.EngineStatusResponse is not { } engine)
        {
            Diagnostic diagnostic = Diagnostic(
                DiagnosticSeverity.Error,
                HelperPayloadMissingCode,
                "The Apple Virtualization helper returned an engine status response without an engine payload.",
                "engine.status");
            return Store(metadata, spec, Status(
                metadata,
                ResourcePhase.Failed,
                EngineControlPlanePhase.Failed,
                Append(diagnostics, diagnostic),
                endpoints: Array.Empty<EngineApiEndpointStatus>()));
        }

        ulong expectedHostStartGeneration = (ulong)Math.Max(
            0,
            hostEntry.Status.Generations.HostStartGeneration?.Value ?? 0);
        AppleVirtualizationGuestAgentEngineGenerationStamp? generation = engine.GuestEngineStatus?.Generation;
        (string? ExpectedGuestBootId, ulong? ExpectedGuestBootGeneration) expectedGuestBoot =
            ParseGuestBootGeneration(hostEntry.Status.Generations.GuestBootGeneration);
        string generationFailure = string.Empty;
        bool engineIdentityMatches =
            string.Equals(engine.EngineId, metadata.Id.Value, StringComparison.Ordinal) &&
            string.Equals(engine.GuestEngineStatus?.EngineId, metadata.Id.Value, StringComparison.Ordinal);
        if (!engineIdentityMatches)
        {
            generationFailure = "Engine status was rejected because its engine identity did not match the requested engine.";
        }
        bool generationAccepted = engineIdentityMatches && generation is not null &&
            _ledger.TryAcceptRuntimeHostEngineGeneration(
                hostEntry.Resource.Id,
                hostEntry.Resource.Scope,
                metadata.Id.Value,
                generation,
                _ledger.ProviderGeneration,
                expectedHostStartGeneration,
                expectedGuestBoot.ExpectedGuestBootId,
                expectedGuestBoot.ExpectedGuestBootGeneration,
                requireEngineGeneration: engine.Ready,
                out generationFailure);
        if (generation is null ||
            generation.EngineGeneration > long.MaxValue ||
            !generationAccepted)
        {
            Diagnostic diagnostic = Diagnostic(
                DiagnosticSeverity.Error,
                new DiagnosticCode("AppleVirtualization.EngineStatusStaleGeneration"),
                string.IsNullOrWhiteSpace(generationFailure)
                    ? "Engine status was rejected because its provider or host-start generation was missing or stale."
                    : generationFailure,
                "engine.status.generation");
            return Store(metadata, spec, Status(
                metadata,
                ResourcePhase.Degraded,
                EngineControlPlanePhase.Degraded,
                Append(diagnostics, diagnostic),
                endpoints: Array.Empty<EngineApiEndpointStatus>()));
        }

        Diagnostic[] combinedDiagnostics = Append(diagnostics, engine.Diagnostics);
        EngineApiEndpointStatus[] endpoints = MapEndpoints(engine.Endpoints);
        return Store(metadata, spec, new EngineControlPlaneStatus
        {
            Phase = engine.Phase,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            EnginePhase = engine.EnginePhase,
            EngineGeneration = new EngineIncarnationGeneration(
                checked((long)generation.EngineGeneration)),
            Endpoints = endpoints,
            ExternalMutationPossible = spec.WorkloadAdoption != EngineWorkloadAdoptionMode.None ||
                spec.ImageStore is EngineImageStoreMode.EngineLocal or EngineImageStoreMode.Remote,
            Diagnostics = combinedDiagnostics,
            Conditions = Conditions(metadata.Generation, engine.EnginePhase, engine.Ready, combinedDiagnostics),
            ProviderHandle = observed?.ProviderHandle,
        });
    }

    public ValueTask<EngineControlPlaneStatus> GetStatusAsync(
        ResourceRef<EngineControlPlane> engine,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<EngineControlPlane, EngineControlPlaneStatus>> lookup =
            _ledger.TryGetEngineControlPlane(engine);
        return ValueTask.FromResult(lookup.Succeeded
            ? lookup.Entry!.Status
            : Status(
                new ResourceMetadata<EngineControlPlane>
                {
                    Id = engine.Id,
                    Kind = new ResourceKind("engine-control-plane"),
                    Scope = engine.Scope,
                    SchemaVersion = new SchemaVersion("v1"),
                    Generation = engine.Generation ?? default,
                },
                ResourcePhase.Failed,
                EngineControlPlanePhase.Failed,
                [lookup.Diagnostic ?? AppleVirtualizationHandleDiagnostics.Missing(ProviderId, "engine-control-plane/" + engine.Id.Value)],
                endpoints: Array.Empty<EngineApiEndpointStatus>()));
    }

    public ValueTask<EngineControlPlaneStatus> StopAsync(
        TargetHandle<EngineControlPlane> engine,
        StopPolicy policy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<EngineControlPlane, EngineControlPlaneStatus>> lookup =
            _ledger.TryGetEngineControlPlane(engine);
        if (!lookup.Succeeded || lookup.Entry is null)
        {
            return ValueTask.FromResult(new EngineControlPlaneStatus
            {
                Phase = ResourcePhase.Failed,
                EnginePhase = EngineControlPlanePhase.Failed,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics = [lookup.Diagnostic ?? AppleVirtualizationHandleDiagnostics.Missing(ProviderId, "engine-control-plane")],
            });
        }

        EngineControlPlaneStatus status = lookup.Entry.Status with
        {
            Phase = ResourcePhase.Ready,
            EnginePhase = EngineControlPlanePhase.Stopped,
            LastTransitionAt = DateTimeOffset.UtcNow,
            Endpoints = Array.Empty<EngineApiEndpointStatus>(),
        };
        EngineControlPlaneSpec? spec = _ledger.TryGetEngineControlPlaneSpec(lookup.Entry.Resource);
        return ValueTask.FromResult(_ledger.UpsertEngineControlPlane(
            Metadata(lookup.Entry.Resource),
            status,
            spec).Status);
    }

    public ValueTask DeleteAsync(ResourceRef<EngineControlPlane> engine, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ledger.RemoveEngineControlPlane(engine);
        return ValueTask.CompletedTask;
    }

    private EngineControlPlaneStatus Store(
        ResourceMetadata<EngineControlPlane> metadata,
        EngineControlPlaneSpec spec,
        EngineControlPlaneStatus status) =>
        _ledger.UpsertEngineControlPlane(metadata, status, spec).Status;

    private AppleVirtualizationHelperEnvelope Request(AppleVirtualizationHelperOperation operation, SchemaId schema) =>
        AppleVirtualizationHelperEnvelope.Request(
            operation,
            "apple-vz-engine-" + Interlocked.Increment(ref _requestSequence).ToString(CultureInfo.InvariantCulture),
            Interlocked.Read(ref _requestSequence),
            schema);

    private Diagnostic[] ValidateSpec(EngineControlPlaneSpec spec)
    {
        var diagnostics = new List<Diagnostic>(4);
        if (spec.Kind == EngineControlPlaneKind.Kubernetes || spec.Api == EngineApiKind.KubernetesApi)
        {
            diagnostics.Add(Diagnostic(
                DiagnosticSeverity.Error,
                KubernetesUnsupportedCode,
                "Kubernetes EngineControlPlane is not implemented by the Apple Virtualization L14 provider.",
                "engine.kind"));
        }

        if (spec.AuthorityMode is EngineAuthorityMode.Mixed or EngineAuthorityMode.ProviderDefined)
        {
            diagnostics.Add(Diagnostic(
                spec.AuthorityMode == EngineAuthorityMode.ProviderDefined ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                AuthorityModeUnsupportedCode,
                "Apple Virtualization engine authority must be explicit rootless or rootful before endpoint binding. Mixed mode is observable but conservative.",
                "engine.authorityMode"));
        }

        if (spec.WorkloadAdoption != EngineWorkloadAdoptionMode.None)
        {
            diagnostics.Add(Diagnostic(
                DiagnosticSeverity.Warning,
                WorkloadAdoptionUnsupportedCode,
                "Apple Virtualization L14 observes workload adoption intent but does not adopt externally-created workloads.",
                "engine.workloadAdoption"));
        }

        if (spec.ImageStore is EngineImageStoreMode.SharedWithRootfsProvider or EngineImageStoreMode.Remote or EngineImageStoreMode.ProviderDefined)
        {
            diagnostics.Add(Diagnostic(
                spec.ImageStore == EngineImageStoreMode.ProviderDefined ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                ImageStoreUnsupportedCode,
                "Apple Virtualization L14 observes image-store mode but does not claim artifact/rootfs/image-store ownership.",
                "engine.imageStore"));
        }

        return diagnostics.Count == 0 ? [] : diagnostics.ToArray();
    }

    private static EngineApiEndpointStatus[] MapEndpoints(IReadOnlyList<AppleVirtualizationGuestAgentEngineApiEndpoint> endpoints)
    {
        if (endpoints.Count == 0)
        {
            return [];
        }

        var result = new EngineApiEndpointStatus[endpoints.Count];
        for (int i = 0; i < endpoints.Count; i++)
        {
            AppleVirtualizationGuestAgentEngineApiEndpoint endpoint = endpoints[i];
            var named = new ProviderNamedEndpoint(
                endpoint.Name,
                ProviderEndpointPurpose.EngineApi,
                new ProviderEndpoint(
                    Scheme: endpoint.Transport == NetworkTransport.UnixStream ? "unix" : endpoint.Transport.ToString().ToLowerInvariant(),
                    Address: endpoint.GuestVisibleOnly ? "guest" : "provider",
                    Port: endpoint.Port?.Value,
                    Path: endpoint.SocketPath?.Value),
                ProviderTransportKind.UnixSocket,
                EndpointSensitivity.Sensitive);
            result[i] = new EngineApiEndpointStatus(endpoint.Api, named, endpoint.SensitivePolicy);
        }

        return result;
    }

    private static EngineControlPlaneStatus Status(
        ResourceMetadata<EngineControlPlane> metadata,
        ResourcePhase phase,
        EngineControlPlanePhase enginePhase,
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<EngineApiEndpointStatus> endpoints) =>
        new()
        {
            Phase = phase,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            EnginePhase = enginePhase,
            Endpoints = endpoints,
            Diagnostics = diagnostics,
            Conditions = Conditions(metadata.Generation, enginePhase, ready: phase == ResourcePhase.Ready, diagnostics),
        };

    private static IReadOnlyList<Condition> Conditions(
        ResourceGeneration generation,
        EngineControlPlanePhase phase,
        bool ready,
        IReadOnlyList<Diagnostic> diagnostics) =>
    [
        new Condition(
            "AppleVirtualizationEngineReady",
            ready ? ConditionStatus.True : ConditionStatus.False,
            phase.ToString(),
            ready
                ? "The in-guest engine control plane is ready."
                : "The in-guest engine control plane is not ready.",
            DateTimeOffset.UtcNow,
            generation,
            diagnostics.Any(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Error)
                ? DiagnosticSeverity.Error
                : diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning)
                    ? DiagnosticSeverity.Warning
                    : DiagnosticSeverity.Info),
    ];

    private static Diagnostic? FindFatal(IReadOnlyList<Diagnostic> diagnostics)
    {
        for (int i = 0; i < diagnostics.Count; i++)
        {
            if (diagnostics[i].Severity >= DiagnosticSeverity.Error)
            {
                return diagnostics[i];
            }
        }

        return null;
    }

    private static Diagnostic[] Append(IReadOnlyList<Diagnostic> existing, Diagnostic diagnostic)
    {
        var result = new Diagnostic[existing.Count + 1];
        for (int i = 0; i < existing.Count; i++)
        {
            result[i] = existing[i];
        }

        result[^1] = diagnostic;
        return result;
    }

    private static Diagnostic[] Append(IReadOnlyList<Diagnostic> existing, IReadOnlyList<Diagnostic> additional)
    {
        if (additional.Count == 0)
        {
            return existing.Count == 0 ? [] : existing.ToArray();
        }

        var result = new Diagnostic[existing.Count + additional.Count];
        for (int i = 0; i < existing.Count; i++)
        {
            result[i] = existing[i];
        }

        for (int i = 0; i < additional.Count; i++)
        {
            result[existing.Count + i] = additional[i];
        }

        return result;
    }

    private static Diagnostic Diagnostic(
        DiagnosticSeverity severity,
        DiagnosticCode code,
        string message,
        string targetPath) =>
        new()
        {
            Severity = severity,
            Code = code,
            Message = message,
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    private static Diagnostic ToDiagnostic(AppleVirtualizationHelperError? error, string targetPath) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = new DiagnosticCode(error?.Code ?? "AppleVirtualization.HelperError"),
            Message = error?.Message ?? "The Apple Virtualization helper returned an error.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    private static (string? GuestBootId, ulong? Generation) ParseGuestBootGeneration(
        GuestBootGeneration? generation)
    {
        if (generation is null || string.IsNullOrWhiteSpace(generation.Value.Value))
        {
            return (null, null);
        }

        string value = generation.Value.Value;
        int separator = value.LastIndexOf(':');
        string number = separator >= 0 ? value[(separator + 1)..] : value;
        return ulong.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed)
            ? (separator > 0 ? value[..separator] : null, parsed)
            : (null, null);
    }

    private static ResourceMetadata<EngineControlPlane> Metadata(ResourceRef<EngineControlPlane> resource) =>
        new()
        {
            Id = resource.Id,
            Kind = new ResourceKind("engine-control-plane"),
            Scope = resource.Scope,
            SchemaVersion = new SchemaVersion("v1"),
            Generation = resource.Generation ?? default,
        };
}
