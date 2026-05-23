namespace HPD.Execution.AppleVirtualization.Engines;

using HPD.Execution.AppleVirtualization.State;
using HPD.Execution.Contracts;

internal sealed class AppleVirtualizationContainerSmokeWorkflow
{
    private const int DefaultMaxCapturedBytesPerStream = 64 * 1024;

    private static readonly DiagnosticCode EngineNotReadyCode = new("AppleVirtualization.ContainerSmokeEngineNotReady");
    private static readonly DiagnosticCode EndpointMissingCode = new("AppleVirtualization.ContainerSmokeEngineEndpointMissing");
    private static readonly DiagnosticCode AuthorityRequiredCode = new("AppleVirtualization.ContainerSmokeEngineAuthorityRequired");
    private static readonly DiagnosticCode HostPassthroughRejectedCode = new("AppleVirtualization.ContainerSmokeHostEngineSocketPassthroughRejected");
    private static readonly DiagnosticCode AuthorityTargetMismatchCode = new("AppleVirtualization.ContainerSmokeAuthorityTargetMismatch");
    private static readonly DiagnosticCode AuthorityRevokedCode = new("AppleVirtualization.ContainerSmokeEngineAuthorityRevoked");
    private static readonly DiagnosticCode NonZeroExitCode = new("AppleVirtualization.ContainerSmokeNonZeroExit");

    private readonly AppleVirtualizationProviderStateLedger _ledger;
    private readonly IEngineControlPlaneProvider _engineProvider;
    private readonly IProcessProvider _processProvider;

    public AppleVirtualizationContainerSmokeWorkflow(
        AppleVirtualizationProviderStateLedger ledger,
        IEngineControlPlaneProvider engineProvider,
        IProcessProvider processProvider)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _engineProvider = engineProvider ?? throw new ArgumentNullException(nameof(engineProvider));
        _processProvider = processProvider ?? throw new ArgumentNullException(nameof(processProvider));
    }

    public async ValueTask<ProcessInvocationResult> RunAsync(
        AppleVirtualizationContainerSmokeWorkflowRequest request,
        IProcessOutputSink? output = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        EngineControlPlaneStatus engine = await _engineProvider.EnsureEngineControlPlaneAsync(
            request.EngineMetadata,
            request.EngineSpec,
            observed: null,
            cancellationToken).ConfigureAwait(false);

        if (engine.Phase != ResourcePhase.Ready || engine.EnginePhase != EngineControlPlanePhase.Ready)
        {
            return FailedResult(request, Diagnostic(
                DiagnosticSeverity.Error,
                EngineNotReadyCode,
                "Container smoke workflow requires a ready in-guest EngineControlPlane before dispatching workload process accounting.",
                "containerSmoke.engine"));
        }

        if (!TryGetEndpoint(engine, request.Api, out EngineApiEndpointStatus endpoint))
        {
            return FailedResult(request, Diagnostic(
                DiagnosticSeverity.Error,
                EndpointMissingCode,
                "Container smoke workflow requires the requested engine API endpoint metadata before dispatch.",
                "containerSmoke.engine.endpoint"));
        }

        if (ValidateAuthorityBinding(request, endpoint) is { } authorityDiagnostic)
        {
            return FailedResult(request, authorityDiagnostic);
        }

        ProcessInvocationSpec processSpec = new()
        {
            Target = request.TargetUnit,
            Role = request.Role,
            Command = request.Command,
            Identity = request.Identity,
            Limits = request.Limits,
            Io = BoundIo(request.Io, request.MaxCapturedBytesPerStream),
            Policy = request.Policy,
            Isolation = request.Isolation with
            {
                AuthorityBindings = [request.EngineAuthorityBinding],
            },
            PersistResource = request.PersistProcessResource,
            ObservationRetention = request.ObservationRetention,
            ProviderExtensions = request.ProviderExtensions,
        };

        ProcessInvocationResult result = await _processProvider.RunAsync(processSpec, output, cancellationToken).ConfigureAwait(false);
        if (!request.PersistProcessResource && result.ProcessId is { } processId)
        {
            _ledger.RemoveProcessInvocation(new ResourceRef<ProcessInvocation>(
                processId,
                request.TargetUnit.Route.Scope,
                new ResourceGeneration(1)));
        }

        return AnnotateNonZeroExit(result);
    }

    private Diagnostic? ValidateAuthorityBinding(
        AppleVirtualizationContainerSmokeWorkflowRequest request,
        EngineApiEndpointStatus endpoint)
    {
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<AuthorityBinding, AuthorityBindingStatus>> lookup =
            _ledger.TryGetAuthorityBinding(request.EngineAuthorityBinding);
        if (!lookup.Succeeded || lookup.Entry is null)
        {
            return Diagnostic(
                DiagnosticSeverity.Error,
                AuthorityRequiredCode,
                lookup.Diagnostic?.Message ??
                    "Container smoke workflow requires a projected AuthorityBinding for the engine API socket.",
                "containerSmoke.authorityBinding");
        }

        AuthorityBindingStatus status = lookup.Entry.Status;
        if (status.Phase != ResourcePhase.Ready ||
            status.BindingPhase != AuthorityBindingPhase.Projected ||
            status.BoundAuthority is null)
        {
            return Diagnostic(
                DiagnosticSeverity.Error,
                AuthorityRequiredCode,
                "Container smoke workflow requires the engine API AuthorityBinding to be projected before process dispatch.",
                "containerSmoke.authorityBinding");
        }

        AuthorityBindingSpec? spec = _ledger.TryGetAuthorityBindingSpec(request.EngineAuthorityBinding);
        if (spec is null)
        {
            return Diagnostic(
                DiagnosticSeverity.Error,
                AuthorityRequiredCode,
                "Container smoke workflow could not resolve the engine API AuthorityBinding spec.",
                "containerSmoke.authorityBinding");
        }

        if (spec.Source.Locus == BoundaryLocus.Host)
        {
            return Diagnostic(
                DiagnosticSeverity.Error,
                HostPassthroughRejectedCode,
                "Container smoke workflow rejects host-locus Docker, Podman, containerd, and BuildKit sockets.",
                "containerSmoke.authorityBinding.source");
        }

        BoundAuthority boundAuthority = status.BoundAuthority;
        if (boundAuthority.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            return Diagnostic(
                DiagnosticSeverity.Error,
                AuthorityRevokedCode,
                "Container smoke workflow rejects expired engine API authority before process dispatch.",
                "containerSmoke.authorityBinding.lease");
        }

        if (boundAuthority.RevocationStatus != RevocationVerificationStatus.Pending)
        {
            return Diagnostic(
                DiagnosticSeverity.Error,
                AuthorityRevokedCode,
                "Container smoke workflow rejects revoked or no-longer-active engine API authority before process dispatch.",
                "containerSmoke.authorityBinding.revocation");
        }

        string? endpointSocket = endpoint.Endpoint.Endpoint.Path;
        string? authoritySocket = spec.Source.SocketPath?.Value;
        if (string.IsNullOrWhiteSpace(endpointSocket) ||
            !string.Equals(endpointSocket, authoritySocket, StringComparison.Ordinal))
        {
            return Diagnostic(
                DiagnosticSeverity.Error,
                AuthorityRequiredCode,
                "Container smoke workflow AuthorityBinding must bind the ready engine endpoint socket reported by the EngineControlPlane.",
                "containerSmoke.authorityBinding.source");
        }

        if (spec.Target.Kind != AuthorityTargetKind.ExecutionUnit ||
            spec.Target.Unit is not { } targetUnit ||
            !HandleTargetsExecutionUnit(targetUnit, request.TargetUnit))
        {
            return Diagnostic(
                DiagnosticSeverity.Error,
                AuthorityTargetMismatchCode,
                "Container smoke workflow AuthorityBinding must target the execution unit that will run the workload process.",
                "containerSmoke.authorityBinding.target");
        }

        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>> unitLookup =
            _ledger.TryGetExecutionUnit(request.TargetUnit);
        if (!unitLookup.Succeeded || unitLookup.Entry is null || !Contains(unitLookup.Entry.Status.AuthorityBindings, request.EngineAuthorityBinding))
        {
            return Diagnostic(
                DiagnosticSeverity.Error,
                AuthorityRequiredCode,
                "Container smoke workflow AuthorityBinding must be attached to the target execution unit before process dispatch.",
                "containerSmoke.authorityBinding.target");
        }

        return null;
    }

    private static ProcessInvocationResult AnnotateNonZeroExit(ProcessInvocationResult result)
    {
        if (result.CompletionKind != ProcessCompletionKind.Exited ||
            result.ExitCode is null or 0 ||
            HasDiagnostic(result.Diagnostics, NonZeroExitCode))
        {
            return result;
        }

        return result with
        {
            Diagnostics = Append(result.Diagnostics, new Condition(
                "AppleVirtualizationContainerSmoke",
                ConditionStatus.False,
                NonZeroExitCode.Value,
                "Container smoke command exited with nonzero status " + result.ExitCode.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".",
                DateTimeOffset.UtcNow,
                default,
                DiagnosticSeverity.Error)),
        };
    }

    private static bool TryGetEndpoint(
        EngineControlPlaneStatus engine,
        EngineApiKind api,
        out EngineApiEndpointStatus endpoint)
    {
        IReadOnlyList<EngineApiEndpointStatus> endpoints = engine.Endpoints;
        for (int i = 0; i < endpoints.Count; i++)
        {
            if (endpoints[i].Api == api)
            {
                endpoint = endpoints[i];
                return true;
            }
        }

        endpoint = default!;
        return false;
    }

    private static ProcessIoSpec BoundIo(ProcessIoSpec io, int maxCapturedBytesPerStream)
    {
        int max = Math.Max(0, maxCapturedBytesPerStream);
        return io with
        {
            StandardOutput = BoundOutput(io.StandardOutput, max),
            StandardError = BoundOutput(io.StandardError, max),
        };
    }

    private static ProcessOutputSpec BoundOutput(ProcessOutputSpec output, int maxCapturedBytes)
    {
        int capped = output.MaxCapturedBytes is { } requested
            ? Math.Min(Math.Max(0, requested), maxCapturedBytes)
            : maxCapturedBytes;
        return output with { MaxCapturedBytes = capped };
    }

    private static ProcessInvocationResult FailedResult(
        AppleVirtualizationContainerSmokeWorkflowRequest request,
        Diagnostic diagnostic) =>
        new()
        {
            CompletionKind = ProcessCompletionKind.FailedToStart,
            StartedAt = DateTimeOffset.UtcNow,
            ExitedAt = DateTimeOffset.UtcNow,
            Output = new ProcessCapturedOutput
            {
                Stdout = new ProcessStreamOutput(),
                Stderr = new ProcessStreamOutput(),
                OutputDrainTimeout = request.Policy.OutputDrainTimeout,
            },
            Diagnostics =
            [
                new Condition(
                    "AppleVirtualizationContainerSmoke",
                    ConditionStatus.False,
                    diagnostic.Code.Value,
                    diagnostic.Message,
                    DateTimeOffset.UtcNow,
                    default,
                    diagnostic.Severity),
            ],
        };

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

    private static bool HandleTargetsExecutionUnit(
        TargetHandle<ExecutionUnit> handle,
        TargetHandle<ExecutionUnit> target) =>
        string.Equals(handle.Route.BackingResourceId, target.Route.BackingResourceId, StringComparison.Ordinal) &&
        string.Equals(handle.Route.Scope.Value, target.Route.Scope.Value, StringComparison.Ordinal);

    private static bool Contains<TResource>(
        IReadOnlyList<ResourceRef<TResource>> values,
        ResourceRef<TResource> value)
        where TResource : IExecutionResourceMarker
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i].Id.Value, value.Id.Value, StringComparison.Ordinal) &&
                string.Equals(values[i].Scope.Value, value.Scope.Value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDiagnostic(IReadOnlyList<Condition> diagnostics, DiagnosticCode code)
    {
        for (int i = 0; i < diagnostics.Count; i++)
        {
            if (string.Equals(diagnostics[i].Reason, code.Value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<Condition> Append(IReadOnlyList<Condition> existing, Condition condition)
    {
        var result = new Condition[existing.Count + 1];
        for (int i = 0; i < existing.Count; i++)
        {
            result[i] = existing[i];
        }

        result[^1] = condition;
        return result;
    }
}

internal sealed record AppleVirtualizationContainerSmokeWorkflowRequest
{
    public required ResourceMetadata<EngineControlPlane> EngineMetadata { get; init; }
    public required EngineControlPlaneSpec EngineSpec { get; init; }
    public required EngineApiKind Api { get; init; }
    public required TargetHandle<ExecutionUnit> TargetUnit { get; init; }
    public required ResourceRef<AuthorityBinding> EngineAuthorityBinding { get; init; }
    public required ProcessCommandSpec Command { get; init; }
    public ProcessRole Role { get; init; } = ProcessRole.Exec;
    public ProcessIdentitySpec? Identity { get; init; }
    public ProcessLimitSpec? Limits { get; init; }
    public ProcessIoSpec Io { get; init; } = ProcessIoSpec.Default;
    public ProcessInvocationPolicy Policy { get; init; } = ProcessInvocationPolicy.Default;
    public ProcessIsolationPolicy Isolation { get; init; } = ProcessIsolationPolicy.Default;
    public bool PersistProcessResource { get; init; }
    public ObservationRetentionPolicy ObservationRetention { get; init; } = ObservationRetentionPolicy.ResultAndDiagnostics;
    public int MaxCapturedBytesPerStream { get; init; } = 64 * 1024;
    public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = Array.Empty<ProviderExtensionData>();
}
