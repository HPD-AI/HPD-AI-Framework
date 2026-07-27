namespace HPD.Environment.AppleVirtualization.Hosts;

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using HPD.Environment.AppleVirtualization.Activation;
using HPD.Environment.AppleVirtualization.GuestAgent;
using HPD.Environment.AppleVirtualization.Handles;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.Contracts;

internal sealed record AppleVirtualizationRuntimeHostFingerprintInput(
    RuntimeHostSpec Spec,
    int EffectiveCpuCount,
    long EffectiveMemoryBytes,
    long EffectiveDiskBytes,
    AppleVirtualizationGuestImageOptions GuestImage,
    AppleVirtualizationEngineBootstrapOptions EngineBootstrap,
    bool GuestControlExpected,
    uint GuestAgentVirtioSocketPort,
    bool RealVmBootEnabled);

public sealed class AppleVirtualizationRuntimeHostProvider : IRuntimeHostProvider
{
    private static readonly SchemaVersion SchemaVersion = new("v1");
    private static readonly ResourceKind RuntimeHostKind = new("runtime-host");
    private static readonly ContentType JsonContentType = new("application/json");
    private static readonly DiagnosticCode UnsupportedHostCode = new("AppleVirtualization.HostUnsupported");
    private static readonly DiagnosticCode HelperErrorCode = new("AppleVirtualization.HostHelperError");
    private static readonly DiagnosticCode BootInputMissingCode = new("AppleVirtualization.RuntimeHostBootInputMissing");
    private static readonly DiagnosticCode GuestAgentMissingCode = new("AppleVirtualization.GuestAgentConfigurationMissing");
    private static readonly DiagnosticCode GuestAgentReadinessMissingCode = new("AppleVirtualization.GuestAgentReadinessMissing");
    private static readonly DiagnosticCode GuestAgentVmStoppedDuringReadinessCode = new("AppleVirtualization.GuestAgentReadiness.VmStoppedDuringReadiness");
    private static readonly DiagnosticCode VirtiofsMissingCode = new("AppleVirtualization.VirtiofsConfigurationMissing");
    private static readonly DiagnosticCode EngineBootstrapConfigurationMissingCode = new("AppleVirtualization.EngineBootstrapConfigurationMissing");
    private static readonly DiagnosticCode EngineBootstrapAuthorityModeMissingCode = new("AppleVirtualization.EngineBootstrapAuthorityModeMissing");
    private static readonly DiagnosticCode EngineBootstrapStatusMissingCode = new("AppleVirtualization.EngineBootstrapStatusMissing");
    private static readonly DiagnosticCode EngineProvisioningStatusMissingCode = new("AppleVirtualization.EngineProvisioningStatusMissing");
    private static readonly DiagnosticCode ImmutableConfigurationConflictCode = new("AppleVirtualization.RuntimeHostImmutableConfigurationConflict");
    private static readonly DiagnosticCode StaleObservedHandleCode = new("AppleVirtualization.RuntimeHostStaleObservedHandle");
    private const int MaxReadinessDiagnosticMessageLength = 512;
    private const uint GuestAgentVirtioSocketPort = 7_777;

    private readonly AppleVirtualizationProviderStateLedger _ledger;
    private readonly IAppleVirtualizationHelperClient _helper;
    private readonly AppleVirtualizationProviderOptions _options;
    private readonly PlatformSpec _hostPlatform;
    private long _requestSequence;

    internal AppleVirtualizationRuntimeHostProvider(
        IAppleVirtualizationHelperClient helper,
        AppleVirtualizationProviderStateLedger ledger)
        : this(helper, ledger, AppleVirtualizationProviderDescriptor.CurrentPlatform())
    {
    }

    internal AppleVirtualizationRuntimeHostProvider(
        IAppleVirtualizationHelperClient helper,
        AppleVirtualizationProviderStateLedger ledger,
        PlatformSpec hostPlatform)
        : this(helper, ledger, hostPlatform, new AppleVirtualizationProviderOptions())
    {
    }

    internal AppleVirtualizationRuntimeHostProvider(
        IAppleVirtualizationHelperClient helper,
        AppleVirtualizationProviderStateLedger ledger,
        PlatformSpec hostPlatform,
        AppleVirtualizationProviderOptions options)
    {
        _helper = helper ?? throw new ArgumentNullException(nameof(helper));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _hostPlatform = hostPlatform;
    }

    public ProviderId ProviderId => AppleVirtualizationProviderDescriptor.ProviderId;

    public async ValueTask<RuntimeHostStatus> EnsureAsync(
        ResourceMetadata<RuntimeHost> metadata,
        RuntimeHostSpec spec,
        RuntimeHostStatus? observed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsSupportedHost(_hostPlatform))
        {
            return Store(metadata, FailureStatus(metadata, spec, UnsupportedHost(_hostPlatform)), spec);
        }

        string requestedFingerprint = ConfigurationFingerprint(spec);
        if (observed?.Handle is { } existingHandle &&
            observed.HostPhase is RuntimeHostPhase.Starting or RuntimeHostPhase.Running or RuntimeHostPhase.Ready or RuntimeHostPhase.Degraded)
        {
            if (existingHandle.ProviderGeneration != _ledger.ProviderGeneration)
            {
                return observed with
                {
                    Phase = ResourcePhase.Failed,
                    ReconciliationOutcome = ResourceReconciliationOutcome.ImmutableConflict,
                    HostPhase = RuntimeHostPhase.Failed,
                    LastTransitionAt = DateTimeOffset.UtcNow,
                    Diagnostics = AppendDiagnostic(observed.Diagnostics, new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = StaleObservedHandleCode,
                        Message = "The observed runtime-host handle belongs to a stale provider generation and cannot be reconciled.",
                        ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                        TargetPath = "runtimeHost.handle.providerGeneration",
                    }),
                };
            }

            string? activeFingerprint = _ledger.GetRuntimeHostConfigurationFingerprint(metadata.Id, metadata.Scope);
            if (!string.Equals(activeFingerprint, requestedFingerprint, StringComparison.Ordinal))
            {
                return observed with
                {
                    Phase = ResourcePhase.Failed,
                    HostPhase = RuntimeHostPhase.Failed,
                    LastTransitionAt = DateTimeOffset.UtcNow,
                    Diagnostics = AppendDiagnostic(observed.Diagnostics, new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = ImmutableConfigurationConflictCode,
                        Message = activeFingerprint is null
                            ? "The active VM has no persisted configuration fingerprint and must be replaced before reconciliation."
                            : "The requested VM-defining configuration differs from the active VM; stop/delete/recreate is required.",
                        ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                        TargetPath = "runtimeHost.spec",
                    }),
                };
            }

            AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
                HostLifecycleRequest(metadata, observed, AppleVirtualizationHelperOperation.HostStatus),
                cancellationToken).ConfigureAwait(false);
            RuntimeHostStatus existing = response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error
                ? observed with
                {
                    Phase = ResourcePhase.Degraded,
                    LastTransitionAt = DateTimeOffset.UtcNow,
                    Diagnostics = AppendDiagnostic(observed.Diagnostics, ToDiagnostic(response.Error, "host.status")),
                }
                : MapHostStatus(metadata, spec, observed, response.HostStatusResponse, response.GuestControlStatusResponse);

            existing = existing with { Handle = existingHandle };
            existing = await ProbeGuestAgentReadinessAsync(metadata, spec, existing, cancellationToken).ConfigureAwait(false);
            existing = await ApplyEngineBootstrapStatusAsync(metadata, spec, existing, cancellationToken).ConfigureAwait(false);
            return Store(metadata, existing, spec);
        }

        if (observed?.Handle is { } retainedHandle &&
            observed.HostPhase is RuntimeHostPhase.Stopping or RuntimeHostPhase.Stopped or RuntimeHostPhase.Failed)
        {
            if (retainedHandle.ProviderGeneration != _ledger.ProviderGeneration)
            {
                return observed with
                {
                    Phase = ResourcePhase.Failed,
                    ReconciliationOutcome = ResourceReconciliationOutcome.ImmutableConflict,
                    HostPhase = RuntimeHostPhase.Failed,
                    LastTransitionAt = DateTimeOffset.UtcNow,
                    Diagnostics = AppendDiagnostic(observed.Diagnostics, new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = StaleObservedHandleCode,
                        Message = "The observed runtime-host handle belongs to a stale provider generation and cannot be restarted.",
                        ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                        TargetPath = "runtimeHost.handle.providerGeneration",
                    }),
                };
            }

            string? retainedFingerprint =
                _ledger.GetRuntimeHostConfigurationFingerprint(metadata.Id, metadata.Scope);
            if (!string.Equals(retainedFingerprint, requestedFingerprint, StringComparison.Ordinal))
            {
                return observed with
                {
                    Phase = ResourcePhase.Failed,
                    ReconciliationOutcome = ResourceReconciliationOutcome.ImmutableConflict,
                    HostPhase = RuntimeHostPhase.Failed,
                    LastTransitionAt = DateTimeOffset.UtcNow,
                    Diagnostics = AppendDiagnostic(observed.Diagnostics, new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = ImmutableConfigurationConflictCode,
                        Message = "The retained VM configuration differs from the requested configuration; delete and recreate the host.",
                        ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                        TargetPath = "runtimeHost.spec",
                    }),
                };
            }

            return await EnsureRealVmLifecycleAsync(
                metadata,
                spec,
                observed,
                cancellationToken).ConfigureAwait(false);
        }

        RuntimeHostStatus status = Store(metadata, CreateStatus(metadata, spec, RuntimeHostPhase.Preparing, ResourcePhase.Reconciling), spec);

        if (ShouldValidateVmConfiguration())
        {
            RuntimeHostStatus validationStatus = await ValidateVmConfigurationAsync(metadata, spec, status, cancellationToken).ConfigureAwait(false);
            return Store(metadata, validationStatus, spec);
        }

        if (ShouldStartRealVm())
        {
            return await EnsureRealVmLifecycleAsync(metadata, spec, status, cancellationToken).ConfigureAwait(false);
        }

        AppleVirtualizationHelperEnvelope ensureResponse = await _helper.SendAsync(
            Request(AppleVirtualizationHelperOperation.HostEnsure, AppleVirtualizationHelperProtocol.HostRequestSchema) with
            {
                ResourceKind = metadata.Kind,
                ResourceId = metadata.Id.Value,
                ResourceScope = metadata.Scope,
                ResourceGeneration = metadata.Generation,
                ProviderHandle = status.ProviderHandle,
                ProviderGeneration = _ledger.ProviderGeneration,
                HostEnsureRequest = new AppleVirtualizationHostEnsureRequest
                {
                    HostId = metadata.Id.Value,
                    Platform = spec.Platform,
                    Capacity = spec.Capacity,
                    GuestImage = _options.GuestImage,
                    BootImagePath = _options.GuestImage.BundleRoot,
                    KernelPath = _options.GuestImage.KernelPath,
                    InitrdPath = _options.GuestImage.InitrdPath,
                    KernelCommandLine = _options.GuestImage.KernelCommandLine,
                    DiskImagePath = _options.GuestImage.DiskImagePath,
                    EfiVariableStorePath = _options.GuestImage.EfiVariableStorePath,
                    SerialLogPath = _options.GuestImage.SerialLogPath,
                    ExpectVirtiofsSupport = _options.GuestImage.ExpectVirtiofsSupport,
                    ExpectedGuestAgentVersion = _options.GuestImage.ExpectedGuestAgentVersion,
                },
            },
            cancellationToken).ConfigureAwait(false);

        status = ApplyResponse(metadata, spec, status, ensureResponse, "host.ensure");
        if (status.Phase == ResourcePhase.Failed)
        {
            return Store(metadata, status);
        }

        AppleVirtualizationHelperEnvelope startResponse = await _helper.SendAsync(
            Request(AppleVirtualizationHelperOperation.HostStart, AppleVirtualizationHelperProtocol.HostRequestSchema) with
            {
                ResourceKind = metadata.Kind,
                ResourceId = metadata.Id.Value,
                ResourceScope = metadata.Scope,
                ResourceGeneration = metadata.Generation,
                ProviderHandle = status.ProviderHandle,
                ProviderGeneration = _ledger.ProviderGeneration,
                HostLifecycleRequest = new AppleVirtualizationHostLifecycleRequest
                {
                    HostId = metadata.Id.Value,
                    HostStartGeneration = checked((ulong)NextHostStartGeneration(status).Value),
                    Reason = "ensure",
                },
            },
            cancellationToken).ConfigureAwait(false);

        status = ApplyResponse(metadata, spec, status, startResponse, "host.start");
        if (status.Phase == ResourcePhase.Failed)
        {
            return Store(metadata, status);
        }

        AppleVirtualizationHelperEnvelope statusResponse = await _helper.SendAsync(
            HostLifecycleRequest(metadata, status, AppleVirtualizationHelperOperation.HostStatus),
            cancellationToken).ConfigureAwait(false);

        status = ApplyResponse(metadata, spec, status, statusResponse, "host.status");
        if (status.Phase == ResourcePhase.Failed)
        {
            return Store(metadata, status);
        }

        if (ShouldProbeGuestAgentReadiness(spec, status))
        {
            AppleVirtualizationHelperEnvelope readyResponse = await _helper.SendAsync(
                Request(AppleVirtualizationHelperOperation.GuestAgentReadinessProbe, AppleVirtualizationHelperProtocol.GuestAgentReadinessRequestSchema) with
                {
                    ResourceKind = metadata.Kind,
                    ResourceId = metadata.Id.Value,
                    ResourceScope = metadata.Scope,
                    ResourceGeneration = metadata.Generation,
                    ProviderHandle = status.ProviderHandle,
                    ProviderGeneration = _ledger.ProviderGeneration,
                    GuestAgentReadinessProbeRequest = CreateGuestAgentReadinessProbeRequest(metadata.Id.Value, spec),
                },
                cancellationToken).ConfigureAwait(false);

            status = ApplyGuestAgentReadinessResponse(metadata, spec, status, readyResponse, "guestAgent.readinessProbe");
        }

        status = await ApplyEngineBootstrapStatusAsync(metadata, spec, status, cancellationToken).ConfigureAwait(false);
        return Store(metadata, status, spec);
    }

    private async ValueTask<RuntimeHostStatus> ProbeGuestAgentReadinessAsync(
        ResourceMetadata<RuntimeHost> metadata,
        RuntimeHostSpec? spec,
        RuntimeHostStatus status,
        CancellationToken cancellationToken)
    {
        if (!ShouldProbeGuestAgentReadiness(spec, status))
        {
            return status;
        }

        AppleVirtualizationHelperEnvelope readyResponse = await _helper.SendAsync(
            Request(AppleVirtualizationHelperOperation.GuestAgentReadinessProbe, AppleVirtualizationHelperProtocol.GuestAgentReadinessRequestSchema) with
            {
                ResourceKind = metadata.Kind,
                ResourceId = metadata.Id.Value,
                ResourceScope = metadata.Scope,
                ResourceGeneration = metadata.Generation,
                ProviderHandle = status.ProviderHandle,
                ProviderGeneration = _ledger.ProviderGeneration,
                GuestAgentReadinessProbeRequest = CreateGuestAgentReadinessProbeRequest(metadata.Id.Value, spec),
            },
            cancellationToken).ConfigureAwait(false);

        return ApplyGuestAgentReadinessResponse(metadata, spec, status, readyResponse, "guestAgent.readinessProbe");
    }

    private AppleVirtualizationGuestAgentReadinessProbeRequest CreateGuestAgentReadinessProbeRequest(
        string hostId,
        RuntimeHostSpec? spec) =>
        new()
        {
            HostId = hostId,
            ExplicitRealMode = _options.FeatureGates.EnableRealVmBoot,
            TimeoutMilliseconds = ToMilliseconds(ReadinessTimeout(spec) ?? TimeSpan.FromSeconds(1)),
            ExpectedAgentVersion = _options.GuestImage.ExpectedGuestAgentVersion,
            RequiredCapabilities = Array.Empty<string>(),
        };

    private RuntimeHostStatus ApplyGuestAgentReadinessResponse(
        ResourceMetadata<RuntimeHost> metadata,
        RuntimeHostSpec? spec,
        RuntimeHostStatus previous,
        AppleVirtualizationHelperEnvelope response,
        string operation)
    {
        bool guestControlExpected = GuestControlExpected(spec) || previous.GuestControl?.Expected == true;
        if (response.GuestAgentReadinessProbeResponse is not { } readiness)
        {
            Diagnostic diagnostic = response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error
                ? ToDiagnostic(response.Error, operation)
                : new Diagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Code = GuestAgentReadinessMissingCode,
                    Message = "The Apple Virtualization helper returned a guest-agent readiness response without a readiness payload.",
                    ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                    TargetPath = operation,
                };

            return previous with
            {
                ObservedGeneration = metadata.Generation,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics = AppendDiagnostic(previous.Diagnostics, diagnostic),
                GuestControl = GuestControl(guestControlExpected, installed: false, reachable: false, previous.GuestControl?.Conditions),
                Readiness = Readiness(spec, ready: false, DateTimeOffset.UtcNow, previous.Readiness),
                ControlPlane = ControlPlane(guestControlExpected, guestReachable: false),
            };
        }

        bool verifiedReady = readiness.VerifiedReady && readiness.State == AppleVirtualizationGuestAgentReadinessState.Ready;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<Diagnostic> diagnostics = previous.Diagnostics;
        if (!verifiedReady && ReadinessFailureDiagnostic(readiness, operation) is { } readinessDiagnostic)
        {
            diagnostics = AppendDiagnostic(diagnostics, readinessDiagnostic);
        }

        IReadOnlyList<Condition> conditions = readiness.Conditions.Count == 0
            ? ReadinessConditions(readiness, metadata.Generation, now)
            : readiness.Conditions;

        RuntimeHostPhase hostPhase = verifiedReady
            ? RuntimeHostPhase.Ready
            : previous.HostPhase == RuntimeHostPhase.Ready
                ? RuntimeHostPhase.Running
                : previous.HostPhase;

        return previous with
        {
            Phase = verifiedReady ? ResourcePhase.Ready : PhaseFor(hostPhase),
            HostPhase = hostPhase,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = now,
            Conditions = conditions,
            Diagnostics = diagnostics,
            GuestControlEndpoint = verifiedReady ? new ControlEndpoint("vsock", "hpd-guest-agent") : previous.GuestControlEndpoint,
            GuestControl = GuestControl(guestControlExpected, installed: GuestAgentEvidencePresent(readiness), reachable: verifiedReady, conditions),
            Readiness = Readiness(spec, verifiedReady, now, previous.Readiness, previous.Generations.HostStartGeneration),
            ControlPlane = ControlPlane(guestControlExpected, guestReachable: verifiedReady),
            Provisioning = previous.Provisioning is null
                ? null
                : previous.Provisioning with { Complete = verifiedReady },
            Generations = previous.Generations with
            {
                GuestBootGeneration = readiness.GuestBootGeneration == 0
                    ? previous.Generations.GuestBootGeneration
                    : new GuestBootGeneration(GuestBootGenerationValue(readiness)),
                GuestAgentGeneration = readiness.GuestAgentGeneration == 0
                    ? previous.Generations.GuestAgentGeneration
                    : new ResourceGeneration(checked(
                        (long)readiness.GuestAgentGeneration)),
            },
            Storage = previous.Storage is null
                ? null
                : previous.Storage with
                {
                    PrimaryDisk = previous.Storage.PrimaryDisk is null
                        ? null
                        : previous.Storage.PrimaryDisk with { Ready = hostPhase is RuntimeHostPhase.Running or RuntimeHostPhase.Ready },
                },
        };
    }

    private static bool GuestAgentEvidencePresent(AppleVirtualizationGuestAgentReadinessProbeResponse readiness) =>
        readiness.State is AppleVirtualizationGuestAgentReadinessState.Ready
            or AppleVirtualizationGuestAgentReadinessState.NotReady
            or AppleVirtualizationGuestAgentReadinessState.IncompatibleProtocol
            or AppleVirtualizationGuestAgentReadinessState.IncompatibleAgentVersion
            or AppleVirtualizationGuestAgentReadinessState.MissingCapability
            or AppleVirtualizationGuestAgentReadinessState.GuestAgentError ||
        !string.IsNullOrWhiteSpace(readiness.ProtocolVersion) ||
        !string.IsNullOrWhiteSpace(readiness.AgentVersion);

    private static IReadOnlyList<Condition> ReadinessConditions(
        AppleVirtualizationGuestAgentReadinessProbeResponse readiness,
        ResourceGeneration observedGeneration,
        DateTimeOffset now)
    {
        ConditionStatus status = readiness.VerifiedReady ? ConditionStatus.True : ConditionStatus.False;
        string reason = readiness.State.ToString();
        string message = readiness.VerifiedReady
            ? "Guest agent is compatible and ready."
            : BoundDiagnosticMessage(readiness.Message ?? "Waiting for verified guest-agent readiness.");

        int count = 1;
        if (readiness.GuestBootGeneration != 0)
        {
            count++;
        }

        if (readiness.GuestAgentGeneration != 0)
        {
            count++;
        }

        Condition[] conditions = new Condition[count];
        conditions[0] =
            new Condition(
                "AppleVirtualization.GuestAgentReadiness",
                status,
                reason,
                message,
                now,
                observedGeneration,
                readiness.VerifiedReady ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning);

        int index = 1;
        if (readiness.GuestBootGeneration != 0)
        {
            conditions[index++] = new Condition(
                "AppleVirtualization.GuestBootGeneration",
                ConditionStatus.True,
                "Observed",
                GuestBootGenerationValue(readiness),
                now,
                observedGeneration,
                DiagnosticSeverity.Info);
        }

        if (readiness.GuestAgentGeneration != 0)
        {
            conditions[index] = new Condition(
                "AppleVirtualization.GuestAgentGeneration",
                ConditionStatus.True,
                "Observed",
                readiness.GuestAgentGeneration.ToString(CultureInfo.InvariantCulture),
                now,
                observedGeneration,
                DiagnosticSeverity.Info);
        }

        return conditions;
    }

    private static Diagnostic? ReadinessFailureDiagnostic(
        AppleVirtualizationGuestAgentReadinessProbeResponse readiness,
        string operation)
    {
        if (readiness.State is AppleVirtualizationGuestAgentReadinessState.TransportNotConnected
            or AppleVirtualizationGuestAgentReadinessState.Handshaking
            or AppleVirtualizationGuestAgentReadinessState.NotReady
            or AppleVirtualizationGuestAgentReadinessState.NotAttempted)
        {
            return null;
        }

        if (readiness.Error is { } error)
        {
            return ToDiagnostic(error, operation);
        }

        if (readiness.State == AppleVirtualizationGuestAgentReadinessState.MissingCapability &&
            readiness.MissingCapabilities.Count > 0)
        {
            return new Diagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Code = new DiagnosticCode("AppleVirtualization.GuestAgentReadiness.MissingCapability"),
                Message = BoundDiagnosticMessage(
                    "Guest-agent readiness is missing required capabilities: " +
                    string.Join(", ", readiness.MissingCapabilities.Take(8)) +
                    (readiness.MissingCapabilities.Count > 8 ? ", ..." : ".")),
                ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                TargetPath = operation,
            };
        }

        return new Diagnostic
        {
            Severity = DiagnosticSeverity.Warning,
            Code = new DiagnosticCode("AppleVirtualization.GuestAgentReadiness." + readiness.State),
            Message = BoundDiagnosticMessage(readiness.Message ?? $"Guest-agent readiness probe returned {readiness.State}."),
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = operation,
        };
    }

    private static string GuestBootGenerationValue(AppleVirtualizationGuestAgentReadinessProbeResponse readiness)
    {
        string generation = readiness.GuestBootGeneration.ToString(CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(readiness.GuestBootId)
            ? generation
            : string.Concat(BoundDiagnosticMessage(readiness.GuestBootId, 128), ":", generation);
    }

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

    private async ValueTask<RuntimeHostStatus> ApplyEngineBootstrapStatusAsync(
        ResourceMetadata<RuntimeHost> metadata,
        RuntimeHostSpec spec,
        RuntimeHostStatus previous,
        CancellationToken cancellationToken)
    {
        EngineBootstrapIntent intent = EngineBootstrapIntent.FromSpec(spec);
        if (!intent.Requested)
        {
            return previous;
        }

        if (previous.Readiness?.Ready != true)
        {
            ProviderComponentStatus component = EngineComponent(
                intent.ComponentName,
                ProviderComponentPhase.Starting,
                "WaitingForGuestReadiness",
                "Engine bootstrap is waiting for verified guest-agent readiness.",
                metadata.Generation);
            return previous with
            {
                Bootstrap = Bootstrap(spec, ready: false, EngineCondition(
                    ConditionStatus.False,
                    "WaitingForGuestReadiness",
                    "Engine bootstrap is waiting for verified guest-agent readiness.",
                    DiagnosticSeverity.Info,
                    metadata.Generation)),
                Provisioning = EngineProvisioning(spec, intent, ResourcePhase.Pending, complete: false),
                ControlPlane = ControlPlane(GuestControlExpected(spec), guestReachable: false, component),
            };
        }

        if (!_options.FeatureGates.EnableEngineControlPlane || !_options.EngineBootstrap.Enabled)
        {
            Diagnostic diagnostic = new()
            {
                Severity = DiagnosticSeverity.Warning,
                Code = EngineBootstrapConfigurationMissingCode,
                Message = "Container runtime guest component bootstrap was requested, but Apple Virtualization engine bootstrap is not configured.",
                ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                TargetPath = "bootstrap.guestComponents.containerRuntime",
            };

            ProviderComponentStatus component = EngineComponent(
                intent.ComponentName,
                ProviderComponentPhase.Degraded,
                "RequiresConfiguration",
                "Engine bootstrap requires explicit provider configuration.",
                metadata.Generation);
            return previous with
            {
                Phase = ResourcePhase.Degraded,
                HostPhase = RuntimeHostPhase.Degraded,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics = AppendDiagnostic(previous.Diagnostics, diagnostic),
                Bootstrap = Bootstrap(spec, ready: false, EngineCondition(
                    ConditionStatus.False,
                    "RequiresConfiguration",
                    "Container runtime bootstrap is explicitly requested but not configured.",
                    DiagnosticSeverity.Warning,
                    metadata.Generation)),
                Provisioning = EngineProvisioning(spec, intent, ResourcePhase.Degraded, complete: false),
                ControlPlane = ControlPlane(GuestControlExpected(spec), guestReachable: true, component),
            };
        }

        if (!_options.EngineBootstrap.AuthorityModeConfigured)
        {
            Diagnostic diagnostic = new()
            {
                Severity = DiagnosticSeverity.Warning,
                Code = EngineBootstrapAuthorityModeMissingCode,
                Message = "Container runtime guest component bootstrap requires an explicit rootless or rootful authority mode.",
                ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                TargetPath = "engineBootstrap.authorityMode",
            };

            ProviderComponentStatus component = EngineComponent(
                intent.ComponentName,
                ProviderComponentPhase.Degraded,
                "AuthorityModeRequired",
                "Engine bootstrap requires explicit rootless or rootful authority mode selection.",
                metadata.Generation);
            return previous with
            {
                Phase = ResourcePhase.Degraded,
                HostPhase = RuntimeHostPhase.Degraded,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics = AppendDiagnostic(previous.Diagnostics, diagnostic),
                Bootstrap = Bootstrap(spec, ready: false, EngineCondition(
                    ConditionStatus.False,
                    "AuthorityModeRequired",
                    "Container runtime bootstrap requires explicit rootless or rootful authority mode selection.",
                    DiagnosticSeverity.Warning,
                    metadata.Generation)),
                Provisioning = EngineProvisioning(spec, intent, ResourcePhase.Degraded, complete: false),
                ControlPlane = ControlPlane(GuestControlExpected(spec), guestReachable: true, component),
            };
        }

        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(AppleVirtualizationHelperOperation.EngineStatus, AppleVirtualizationHelperProtocol.EngineStatusRequestSchema) with
            {
                ResourceKind = metadata.Kind,
                ResourceId = metadata.Id.Value,
                ResourceScope = metadata.Scope,
                ResourceGeneration = metadata.Generation,
                ProviderHandle = previous.ProviderHandle,
                ProviderGeneration = _ledger.ProviderGeneration,
                EngineStatusRequest = new AppleVirtualizationEngineStatusRequest
                {
                    HostId = metadata.Id.Value,
                    ProviderGeneration = _ledger.ProviderGeneration,
                    HostStartGeneration = (ulong)Math.Max(
                        0,
                        previous.Generations.HostStartGeneration?.Value ?? 0),
                    EngineId = intent.EngineId,
                    Kind = _options.EngineBootstrap.Kind,
                    Api = _options.EngineBootstrap.Api,
                    AuthorityMode = _options.EngineBootstrap.AuthorityMode,
                    ImageStore = _options.EngineBootstrap.ImageStore,
                    WorkloadAdoption = _options.EngineBootstrap.WorkloadAdoption,
                    ExplicitRealMode = _options.FeatureGates.EnableRealVmBoot,
                    ScriptedObservationState = _options.EngineBootstrap.ScriptedObservationState,
                },
            },
            cancellationToken).ConfigureAwait(false);

        if (response.EngineStatusResponse is not { } engine)
        {
            Diagnostic diagnostic = response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error
                ? ToDiagnostic(response.Error, "engine.status")
                : new Diagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Code = EngineBootstrapStatusMissingCode,
                    Message = "The Apple Virtualization helper returned an engine status response without an engine payload.",
                    ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                    TargetPath = "engine.status",
                };

            return previous with
            {
                Phase = ResourcePhase.Degraded,
                HostPhase = RuntimeHostPhase.Degraded,
                Diagnostics = AppendDiagnostic(previous.Diagnostics, diagnostic),
                Bootstrap = Bootstrap(spec, ready: false, EngineCondition(
                    ConditionStatus.False,
                    "StatusMissing",
                    "Engine bootstrap status could not be observed.",
                    DiagnosticSeverity.Warning,
                    metadata.Generation)),
                Provisioning = EngineProvisioning(spec, intent, ResourcePhase.Degraded, complete: false),
                ControlPlane = ControlPlane(GuestControlExpected(spec), guestReachable: true, EngineComponent(
                    intent.ComponentName,
                    ProviderComponentPhase.Degraded,
                    "StatusMissing",
                    "Engine bootstrap status could not be observed.",
                    metadata.Generation)),
            };
        }

        AppleVirtualizationGuestAgentEngineGenerationStamp? generation = engine.GuestEngineStatus?.Generation;
        ulong expectedHostStartGeneration = (ulong)Math.Max(
            0,
            previous.Generations.HostStartGeneration?.Value ?? 0);
        (string? ExpectedGuestBootId, ulong? ExpectedGuestBootGeneration) expectedGuestBoot =
            ParseGuestBootGeneration(previous.Generations.GuestBootGeneration);
        string generationFailure = string.Empty;
        bool engineIdentityMatches =
            string.Equals(engine.EngineId, intent.EngineId, StringComparison.Ordinal) &&
            string.Equals(engine.GuestEngineStatus?.EngineId, intent.EngineId, StringComparison.Ordinal);
        if (!engineIdentityMatches)
        {
            generationFailure = "Engine status was rejected because its engine identity did not match the requested engine.";
        }
        bool generationAccepted = engineIdentityMatches && generation is not null &&
            _ledger.TryAcceptRuntimeHostEngineGeneration(
                metadata.Id,
                metadata.Scope,
                intent.EngineId,
                generation,
                _ledger.ProviderGeneration,
                expectedHostStartGeneration,
                expectedGuestBoot.ExpectedGuestBootId,
                expectedGuestBoot.ExpectedGuestBootGeneration,
                requireEngineGeneration: engine.Ready,
                out generationFailure);
        if (generation is null || !generationAccepted)
        {
            Diagnostic diagnostic = new()
            {
                Severity = DiagnosticSeverity.Error,
                Code = new DiagnosticCode("AppleVirtualization.EngineStatusStaleGeneration"),
                Message = string.IsNullOrWhiteSpace(generationFailure)
                    ? "Engine status was rejected because its provider or host-start generation was missing or stale."
                    : generationFailure,
                ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                TargetPath = "engine.status.generation",
            };
            return previous with
            {
                Phase = ResourcePhase.Degraded,
                HostPhase = RuntimeHostPhase.Degraded,
                Diagnostics = AppendDiagnostic(previous.Diagnostics, diagnostic),
            };
        }

        AppleVirtualizationEngineProvisioningResponse? provisioning = null;
        if (!engine.Ready && _options.EngineBootstrap.Provisioning.Enabled)
        {
            AppleVirtualizationHelperEnvelope provisioningResponse = await _helper.SendAsync(
                Request(AppleVirtualizationHelperOperation.EngineProvision, AppleVirtualizationHelperProtocol.EngineProvisionRequestSchema) with
                {
                    ResourceKind = metadata.Kind,
                    ResourceId = metadata.Id.Value,
                    ResourceScope = metadata.Scope,
                    ResourceGeneration = metadata.Generation,
                    ProviderHandle = previous.ProviderHandle,
                    ProviderGeneration = _ledger.ProviderGeneration,
                    EngineProvisioningRequest = new AppleVirtualizationEngineProvisioningRequest
                    {
                        HostId = metadata.Id.Value,
                        EngineId = intent.EngineId,
                        Kind = _options.EngineBootstrap.Kind,
                        Api = _options.EngineBootstrap.Api,
                        AuthorityMode = _options.EngineBootstrap.AuthorityMode,
                        ImageStore = _options.EngineBootstrap.ImageStore,
                        WorkloadAdoption = _options.EngineBootstrap.WorkloadAdoption,
                        ExplicitRealMode = _options.FeatureGates.EnableRealVmBoot,
                        AllowPackageInstall = _options.EngineBootstrap.Provisioning.AllowPackageInstall,
                        AllowServiceEnablement = _options.EngineBootstrap.Provisioning.AllowServiceEnablement,
                        ProvisioningTimeoutMilliseconds = ToMilliseconds(_options.EngineBootstrap.Provisioning.ProvisioningTimeout),
                        MaxCapturedOutputBytes = _options.EngineBootstrap.Provisioning.MaxCapturedOutputBytes,
                        PackageManager = _options.EngineBootstrap.Provisioning.PackageManager,
                        ScriptedExecutionState = _options.EngineBootstrap.Provisioning.ScriptedExecutionState,
                        ScriptedPrerequisites = _options.EngineBootstrap.Provisioning.ScriptedPrerequisites,
                        ScriptedOutput = _options.EngineBootstrap.Provisioning.ScriptedOutput,
                        ScriptedStdout = _options.EngineBootstrap.Provisioning.ScriptedStdout,
                        ScriptedStderr = _options.EngineBootstrap.Provisioning.ScriptedStderr,
                    },
                },
                cancellationToken).ConfigureAwait(false);

            if (provisioningResponse.EngineProvisioningResponse is not { } provisioned)
            {
                Diagnostic diagnostic = provisioningResponse.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error
                    ? ToDiagnostic(provisioningResponse.Error, "engine.provision")
                    : new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Warning,
                        Code = EngineProvisioningStatusMissingCode,
                        Message = "The Apple Virtualization helper returned an engine provisioning response without a provisioning payload.",
                        ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                        TargetPath = "engine.provisioning",
                    };

                return previous with
                {
                    Phase = ResourcePhase.Degraded,
                    HostPhase = RuntimeHostPhase.Degraded,
                    Diagnostics = AppendDiagnostic(previous.Diagnostics, diagnostic),
                    Bootstrap = Bootstrap(spec, ready: false, EngineCondition(
                        ConditionStatus.False,
                        "ProvisioningStatusMissing",
                        "Engine provisioning status could not be observed.",
                        DiagnosticSeverity.Warning,
                        metadata.Generation)),
                    Provisioning = EngineProvisioning(spec, intent, ResourcePhase.Degraded, complete: false),
                    ControlPlane = ControlPlane(GuestControlExpected(spec), guestReachable: true, EngineComponent(
                        intent.ComponentName,
                        ProviderComponentPhase.Degraded,
                        "ProvisioningStatusMissing",
                        "Engine provisioning status could not be observed.",
                        metadata.Generation)),
                };
            }

            provisioning = provisioned;
        }

        ProviderComponentPhase componentPhase = EngineComponentPhaseFor(engine.ObservationState);
        bool ready = engine.Ready && engine.ObservationState == AppleVirtualizationEngineObservationState.Ready;
        bool provisioningDegraded = provisioning?.Phase is AppleVirtualizationEngineProvisioningPhase.Degraded or
            AppleVirtualizationEngineProvisioningPhase.Failed;
        ResourcePhase phase = ready
            ? previous.Phase
            : provisioningDegraded ? ResourcePhase.Degraded
            : engine.Phase == ResourcePhase.Degraded || engine.Phase == ResourcePhase.Failed
                ? engine.Phase
                : ResourcePhase.Reconciling;
        RuntimeHostPhase hostPhase = ready
            ? previous.HostPhase
            : phase == ResourcePhase.Degraded || phase == ResourcePhase.Failed
                ? RuntimeHostPhase.Degraded
                : RuntimeHostPhase.Running;
        string reason = provisioningDegraded ? "ProvisioningPrerequisitesMissing" : EngineBootstrapReason(engine);
        string message = provisioningDegraded
            ? BoundDiagnosticMessage(provisioning!.Conditions.Count > 0
                ? provisioning.Conditions[0].Message
                : "Engine provisioning is blocked by missing guest prerequisites.")
            : EngineBootstrapMessage(engine);
        IReadOnlyList<Diagnostic> diagnostics = provisioning is null
            ? engine.Diagnostics
            : AppendDiagnostics(engine.Diagnostics, provisioning.Diagnostics);

        return previous with
        {
            Phase = phase,
            HostPhase = hostPhase,
            LastTransitionAt = DateTimeOffset.UtcNow,
            Diagnostics = AppendDiagnostics(previous.Diagnostics, diagnostics),
            Bootstrap = Bootstrap(spec, ready, EngineCondition(
                ready ? ConditionStatus.True : ConditionStatus.False,
                reason,
                message,
                ready ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning,
                metadata.Generation)),
            Provisioning = EngineProvisioning(spec, intent, ready ? ResourcePhase.Ready : phase, ready),
            ControlPlane = ControlPlane(GuestControlExpected(spec), guestReachable: true, EngineComponent(
                intent.ComponentName,
                componentPhase,
                reason,
                message,
                metadata.Generation)),
        };
    }

    private async ValueTask<RuntimeHostStatus> EnsureRealVmLifecycleAsync(
        ResourceMetadata<RuntimeHost> metadata,
        RuntimeHostSpec spec,
        RuntimeHostStatus previous,
        CancellationToken cancellationToken)
    {
        AppleVirtualizationRealModePreconditionResult preconditions =
            AppleVirtualizationRealModePreconditions.Evaluate(_options);
        if (!preconditions.Passed)
        {
            return Store(metadata, previous with
            {
                Phase = ResourcePhase.Failed,
                HostPhase = RuntimeHostPhase.Failed,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Conditions = PreconditionsAsConditions(preconditions.Facts, metadata.Generation),
                Diagnostics = preconditions.Diagnostics,
                GuestControl = GuestControl(GuestControlExpected(spec), installed: false, reachable: false, Array.Empty<Condition>()),
                Readiness = Readiness(spec, ready: false, DateTimeOffset.UtcNow),
                ControlPlane = ControlPlane(GuestControlExpected(spec), guestReachable: false),
            });
        }

        AppleVirtualizationHelperEnvelope startResponse = await _helper.SendAsync(
            Request(AppleVirtualizationHelperOperation.HostStart, AppleVirtualizationHelperProtocol.HostRequestSchema) with
            {
                ResourceKind = metadata.Kind,
                ResourceId = metadata.Id.Value,
                ResourceScope = metadata.Scope,
                ResourceGeneration = metadata.Generation,
                ProviderHandle = previous.ProviderHandle,
                ProviderGeneration = _ledger.ProviderGeneration,
                HostLifecycleRequest = new AppleVirtualizationHostLifecycleRequest
                {
                    HostId = metadata.Id.Value,
                    HostStartGeneration = checked((ulong)NextHostStartGeneration(previous).Value),
                    ExplicitRealMode = true,
                    Reason = "ensure-real-vm",
                    VmConfigurationValidationRequest = CreateVmConfigurationValidationRequest(metadata.Id.Value, spec),
                },
            },
            cancellationToken).ConfigureAwait(false);

        RuntimeHostStatus status = ApplyResponse(metadata, spec, previous, startResponse, "host.start");
        if (status.Phase == ResourcePhase.Failed)
        {
            return Store(metadata, status);
        }

        AppleVirtualizationHelperEnvelope statusResponse = await _helper.SendAsync(
            HostLifecycleRequest(metadata, status, AppleVirtualizationHelperOperation.HostStatus),
            cancellationToken).ConfigureAwait(false);

        status = ApplyResponse(metadata, spec, status, statusResponse, "host.status");
        if (status.Phase == ResourcePhase.Failed)
        {
            return Store(metadata, status);
        }

        status = await ProbeGuestAgentReadinessAsync(metadata, spec, status, cancellationToken).ConfigureAwait(false);
        status = await ApplyEngineBootstrapStatusAsync(metadata, spec, status, cancellationToken).ConfigureAwait(false);
        return Store(metadata, status);
    }

    private async ValueTask<RuntimeHostStatus> ValidateVmConfigurationAsync(
        ResourceMetadata<RuntimeHost> metadata,
        RuntimeHostSpec spec,
        RuntimeHostStatus previous,
        CancellationToken cancellationToken)
    {
        (IReadOnlyList<Diagnostic> diagnostics, IReadOnlyList<Condition> conditions) =
            ValidateProviderInputs(spec, _options.GuestImage, metadata.Generation);
        if (diagnostics.Count > 0)
        {
            bool hasError = diagnostics.Any(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Error);
            return previous with
            {
                Phase = hasError ? ResourcePhase.Failed : ResourcePhase.Degraded,
                HostPhase = hasError ? RuntimeHostPhase.Failed : RuntimeHostPhase.Degraded,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Conditions = conditions,
                Diagnostics = diagnostics,
                GuestControl = GuestControl(GuestControlExpected(spec), installed: false, reachable: false, conditions),
                Readiness = Readiness(spec, ready: false, DateTimeOffset.UtcNow),
                ControlPlane = ControlPlane(GuestControlExpected(spec), guestReachable: false),
            };
        }

        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(AppleVirtualizationHelperOperation.VmConfigurationValidate, AppleVirtualizationHelperProtocol.VmConfigurationValidationRequestSchema) with
            {
                ResourceKind = metadata.Kind,
                ResourceId = metadata.Id.Value,
                ResourceScope = metadata.Scope,
                ResourceGeneration = metadata.Generation,
                ProviderHandle = previous.ProviderHandle,
                ProviderGeneration = _ledger.ProviderGeneration,
                VmConfigurationValidationRequest = CreateVmConfigurationValidationRequest(metadata.Id.Value, spec),
            },
            cancellationToken).ConfigureAwait(false);

        if (response.VmConfigurationValidationResponse is { } validation)
        {
            return ApplyValidationResponse(spec, previous, validation);
        }

        if (response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error)
        {
            return previous with
            {
                Phase = ResourcePhase.Failed,
                HostPhase = RuntimeHostPhase.Failed,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics = AppendDiagnostic(previous.Diagnostics, ToDiagnostic(response.Error, "vmConfiguration.validate")),
                Readiness = Readiness(spec, ready: false, DateTimeOffset.UtcNow),
            };
        }

        return previous with
        {
            Phase = ResourcePhase.Degraded,
            HostPhase = RuntimeHostPhase.Degraded,
            LastTransitionAt = DateTimeOffset.UtcNow,
            Diagnostics = AppendDiagnostic(previous.Diagnostics, new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = HelperErrorCode,
                Message = "The Apple Virtualization helper returned a VM configuration validation response without a validation payload.",
                ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                TargetPath = "vmConfiguration.validate",
            }),
            Readiness = Readiness(spec, ready: false, DateTimeOffset.UtcNow),
        };
    }

    public async ValueTask<RuntimeHostStatus> StopAsync(
        TargetHandle<RuntimeHost> host,
        StopPolicy policy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>> lookup =
            _ledger.TryGetRuntimeHost(host);
        if (!lookup.Succeeded)
        {
            return HandleFailureStatus(host, lookup.Diagnostic);
        }

        AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> entry = lookup.Entry!;
        AppleVirtualizationHelperOperation operation = policy.Kind == StopKind.Kill
            ? AppleVirtualizationHelperOperation.HostStop
            : AppleVirtualizationHelperOperation.HostRequestStop;

        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            HostLifecycleRequest(ToMetadata(entry), entry.Status, operation) with
            {
                HostLifecycleRequest = new AppleVirtualizationHostLifecycleRequest
                {
                    HostId = entry.Resource.Id.Value,
                    ExplicitRealMode = _options.FeatureGates.EnableRealVmBoot,
                    StopKind = policy.Kind,
                    GracePeriod = policy.GracePeriod,
                    GracePeriodMilliseconds = ToMilliseconds(policy.GracePeriod),
                    Reason = policy.ProviderSignal,
                },
            },
            cancellationToken).ConfigureAwait(false);

        RuntimeHostStatus status = response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error
            ? entry.Status with
            {
                Phase = ResourcePhase.Failed,
                HostPhase = RuntimeHostPhase.Failed,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics = AppendDiagnostic(entry.Status.Diagnostics, ToDiagnostic(response.Error, OperationName(operation))),
            }
            : MapHostStatus(ToMetadata(entry), null, entry.Status, response.HostStatusResponse, response.GuestControlStatusResponse);

        if (policy.Kind == StopKind.GracefulThenKill && status.HostPhase != RuntimeHostPhase.Stopped)
        {
            AppleVirtualizationHelperEnvelope forceResponse = await _helper.SendAsync(
                HostLifecycleRequest(ToMetadata(entry), status, AppleVirtualizationHelperOperation.HostStop) with
                {
                    HostLifecycleRequest = new AppleVirtualizationHostLifecycleRequest
                    {
                        HostId = entry.Resource.Id.Value,
                        ExplicitRealMode = _options.FeatureGates.EnableRealVmBoot,
                        StopKind = StopKind.Kill,
                        GracePeriod = TimeSpan.Zero,
                        GracePeriodMilliseconds = 0,
                        Reason = policy.ProviderSignal,
                    },
                },
                cancellationToken).ConfigureAwait(false);

            status = forceResponse.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error
                ? status with
                {
                    Phase = ResourcePhase.Failed,
                    HostPhase = RuntimeHostPhase.Failed,
                    LastTransitionAt = DateTimeOffset.UtcNow,
                    Diagnostics = AppendDiagnostic(status.Diagnostics, ToDiagnostic(forceResponse.Error, "host.stop")),
                }
                : MapHostStatus(ToMetadata(entry), null, status, forceResponse.HostStatusResponse, forceResponse.GuestControlStatusResponse);
        }

        RuntimeHostStatus stored = Store(ToMetadata(entry), status);
        if (stored.HostPhase is RuntimeHostPhase.Stopped or RuntimeHostPhase.Failed or RuntimeHostPhase.Deleted)
        {
            _ledger.InvalidateExecutionUnitsForRuntimeHost(
                entry.Resource,
                ResourcePhase.Degraded,
                ExecutionUnitPhase.Stopped,
                HostInvalidatedUnitsDiagnostic(entry.Resource.Id.Value, OperationName(operation)));
            _ledger.ReleaseMembershipsForRuntimeHost(
                entry.Resource,
                HostInvalidatedUnitsDiagnostic(entry.Resource.Id.Value, OperationName(operation)));
            AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>> refreshed =
                _ledger.TryGetRuntimeHost(entry.Resource);
            if (refreshed.Succeeded)
            {
                return refreshed.Entry!.Status;
            }
        }

        return stored;
    }

    public async ValueTask DeleteAsync(ResourceRef<RuntimeHost> host, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>> lookup =
            _ledger.TryGetRuntimeHost(host);
        if (!lookup.Succeeded)
        {
            return;
        }

        AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> entry = lookup.Entry!;
        TimeSpan timeout = _options.HostDeletionTimeout > TimeSpan.Zero
            ? _options.HostDeletionTimeout
            : TimeSpan.FromSeconds(30);
        using var deletionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deletionTimeout.CancelAfter(timeout);

        try
        {
            await DeleteHostCoreAsync(host, entry, deletionTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deletionTimeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out after {timeout} deleting Apple Virtualization host '{entry.Resource.Id.Value}'.");
        }
    }

    private async ValueTask DeleteHostCoreAsync(
        ResourceRef<RuntimeHost> host,
        AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> entry,
        CancellationToken deletionToken)
    {
        AppleVirtualizationHelperEnvelope deleteRequest =
            HostLifecycleRequest(ToMetadata(entry), entry.Status, AppleVirtualizationHelperOperation.HostDelete) with
            {
                HostLifecycleRequest = new AppleVirtualizationHostLifecycleRequest
                {
                    HostId = entry.Resource.Id.Value,
                    ExplicitRealMode = _options.FeatureGates.EnableRealVmBoot,
                    StopKind = StopKind.Kill,
                    Reason = "delete",
                },
            };

        AppleVirtualizationHelperEnvelope response =
            await _helper.SendAsync(deleteRequest, deletionToken).ConfigureAwait(false);
        ValidateDeletionResponse(entry, response, "host.delete");

        while (response.HostStatusResponse!.HostPhase == RuntimeHostPhase.Stopping)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), deletionToken).ConfigureAwait(false);
            response = await _helper.SendAsync(
                HostLifecycleRequest(ToMetadata(entry), entry.Status, AppleVirtualizationHelperOperation.HostStatus),
                deletionToken).ConfigureAwait(false);
            ValidateDeletionResponse(entry, response, "host.status");
        }

        if (response.HostStatusResponse!.HostPhase == RuntimeHostPhase.Stopped)
        {
            response = await _helper.SendAsync(deleteRequest, deletionToken).ConfigureAwait(false);
            ValidateDeletionResponse(entry, response, "host.delete");
        }

        if (response.HostStatusResponse!.HostPhase != RuntimeHostPhase.Deleted)
        {
            throw new InvalidOperationException(
                $"Apple Virtualization helper did not prove deletion of host '{entry.Resource.Id.Value}'; observed phase was '{response.HostStatusResponse.HostPhase}'.");
        }

        _ledger.InvalidateExecutionUnitsForRuntimeHost(
            entry.Resource,
            ResourcePhase.Deleted,
            ExecutionUnitPhase.Deleted,
            HostInvalidatedUnitsDiagnostic(entry.Resource.Id.Value, "host.delete"));
        _ledger.ReleaseMembershipsForRuntimeHost(
            entry.Resource,
            HostInvalidatedUnitsDiagnostic(entry.Resource.Id.Value, "host.delete"));
        _ledger.RemoveRuntimeHost(host);
    }

    private void ValidateDeletionResponse(
        AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> entry,
        AppleVirtualizationHelperEnvelope response,
        string operation)
    {
        if (response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error)
        {
            Diagnostic diagnostic = ToDiagnostic(response.Error, operation);
            throw new InvalidOperationException(
                $"Apple Virtualization helper rejected deletion of host '{entry.Resource.Id.Value}': {diagnostic.Code}: {diagnostic.Message}");
        }

        if (response.ProviderGeneration != _ledger.ProviderGeneration)
        {
            throw new InvalidOperationException(
                $"Apple Virtualization helper returned provider generation {response.ProviderGeneration} while deleting host '{entry.Resource.Id.Value}'; expected {_ledger.ProviderGeneration}.");
        }

        AppleVirtualizationHostStatusResponse? status = response.HostStatusResponse;
        if (status is null || !string.Equals(status.HostId, entry.Resource.Id.Value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Apple Virtualization helper returned a missing or mismatched host identity while deleting host '{entry.Resource.Id.Value}'.");
        }
    }

    public async ValueTask<RuntimeHostStatus> GetStatusAsync(
        TargetHandle<RuntimeHost> host,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>> lookup =
            _ledger.TryGetRuntimeHost(host);
        if (!lookup.Succeeded)
        {
            return HandleFailureStatus(host, lookup.Diagnostic);
        }

        AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> entry = lookup.Entry!;
        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            HostLifecycleRequest(ToMetadata(entry), entry.Status, AppleVirtualizationHelperOperation.HostStatus),
            cancellationToken).ConfigureAwait(false);

        RuntimeHostStatus status = response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error
            ? entry.Status with
            {
                Phase = ResourcePhase.Degraded,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics = AppendDiagnostic(entry.Status.Diagnostics, ToDiagnostic(response.Error, "host.status")),
            }
            : MapHostStatus(ToMetadata(entry), null, entry.Status, response.HostStatusResponse, response.GuestControlStatusResponse);

        if (entry.Status.GuestControl?.Expected == true && status.HostPhase is RuntimeHostPhase.Running or RuntimeHostPhase.Ready)
        {
            status = await ProbeGuestAgentReadinessAsync(ToMetadata(entry), null!, status, cancellationToken).ConfigureAwait(false);
        }

        return Store(ToMetadata(entry), status);
    }

    private AppleVirtualizationHelperEnvelope HostLifecycleRequest(
        ResourceMetadata<RuntimeHost> metadata,
        RuntimeHostStatus status,
        AppleVirtualizationHelperOperation operation) =>
        Request(operation, AppleVirtualizationHelperProtocol.HostRequestSchema) with
        {
            ResourceKind = metadata.Kind,
            ResourceId = metadata.Id.Value,
            ResourceScope = metadata.Scope,
            ResourceGeneration = metadata.Generation,
            ProviderHandle = status.ProviderHandle,
            ProviderGeneration = _ledger.ProviderGeneration,
            HostLifecycleRequest = new AppleVirtualizationHostLifecycleRequest
            {
                HostId = metadata.Id.Value,
                ExplicitRealMode = _options.FeatureGates.EnableRealVmBoot,
            },
        };

    private AppleVirtualizationHelperEnvelope Request(
        AppleVirtualizationHelperOperation operation,
        SchemaId schema)
    {
        long sequence = Interlocked.Increment(ref _requestSequence);
        return AppleVirtualizationHelperEnvelope.Request(
            operation,
            "apple-vz-host-" + sequence.ToString(CultureInfo.InvariantCulture),
            sequence,
            schema);
    }

    private RuntimeHostStatus ApplyResponse(
        ResourceMetadata<RuntimeHost> metadata,
        RuntimeHostSpec spec,
        RuntimeHostStatus previous,
        AppleVirtualizationHelperEnvelope response,
        string operation)
    {
        if (response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error)
        {
            return previous with
            {
                Phase = ResourcePhase.Failed,
                HostPhase = RuntimeHostPhase.Failed,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Diagnostics = AppendDiagnostic(previous.Diagnostics, ToDiagnostic(response.Error, operation)),
                Readiness = Readiness(spec, ready: false, DateTimeOffset.UtcNow),
            };
        }

        RuntimeHostStatus mapped = MapHostStatus(metadata, spec, previous, response.HostStatusResponse, response.GuestControlStatusResponse);
        if (operation != "host.start")
        {
            return mapped;
        }

        RuntimeHostStartGeneration hostStartGeneration = NextHostStartGeneration(previous);
        return mapped with
        {
            Generations = mapped.Generations with
            {
                HostStartGeneration = hostStartGeneration,
                StartedAt = DateTimeOffset.UtcNow,
            },
            Readiness = mapped.Readiness is null
                ? null
                : mapped.Readiness with { ObservedHostStartGeneration = hostStartGeneration },
        };
    }

    private static RuntimeHostStatus MapHostStatus(
        ResourceMetadata<RuntimeHost> metadata,
        RuntimeHostSpec? spec,
        RuntimeHostStatus previous,
        AppleVirtualizationHostStatusResponse? host,
        AppleVirtualizationGuestControlStatusResponse? guest)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        RuntimeHostPhase hostPhase = host?.HostPhase ?? previous.HostPhase;
        ResourcePhase phase = host?.Phase == ResourcePhase.Unknown || host?.Phase is null
            ? PhaseFor(hostPhase)
            : host.Phase;
        bool guestExpected = GuestControlExpected(spec) || guest?.Expected == true || previous.GuestControl?.Expected == true;
        bool guestInstalled = guest?.Installed ?? previous.GuestControl?.Installed ?? guestExpected;
        RuntimeHostPhase mappedHostPhase = hostPhase == RuntimeHostPhase.Ready
            ? RuntimeHostPhase.Running
            : hostPhase;
        if (phase == ResourcePhase.Ready && mappedHostPhase == RuntimeHostPhase.Running)
        {
            phase = ResourcePhase.Reconciling;
        }

        return previous with
        {
            Phase = phase,
            HostPhase = mappedHostPhase,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = now,
            Conditions = host?.Conditions ?? guest?.Conditions ?? previous.Conditions,
            Diagnostics = ReadinessStoppedDiagnostics(previous, mappedHostPhase, host?.Diagnostics ?? previous.Diagnostics),
            GuestControlEndpoint = previous.GuestControlEndpoint,
            GuestControl = GuestControl(guestExpected, guestInstalled, reachable: false, guest?.Conditions ?? previous.GuestControl?.Conditions),
            Readiness = Readiness(spec, ready: false, now, previous.Readiness),
            ControlPlane = ControlPlane(guestExpected, guestReachable: false),
            Provisioning = previous.Provisioning is null
                ? null
                : previous.Provisioning with { Complete = false },
            Storage = previous.Storage is null
                ? null
                : previous.Storage with
                {
                    PrimaryDisk = previous.Storage.PrimaryDisk is null
                        ? null
                        : previous.Storage.PrimaryDisk with { Ready = mappedHostPhase is RuntimeHostPhase.Running },
                },
        };
    }

    private RuntimeHostStatus ApplyValidationResponse(
        RuntimeHostSpec spec,
        RuntimeHostStatus previous,
        AppleVirtualizationVmConfigurationValidationResponse validation)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool passed = validation.Passed &&
            validation.State == AppleVirtualizationVmConfigurationValidationState.Passed &&
            !validation.HostRunning &&
            !validation.HpdReady;

        Condition validationCondition = new(
            "AppleVirtualization.VmConfigurationValidated",
            passed ? ConditionStatus.True : ConditionStatus.False,
            validation.State.ToString(),
            passed
                ? "Apple Virtualization VM configuration validation passed; the VM has not been started."
                : "Apple Virtualization VM configuration validation did not pass.",
            now,
            previous.ObservedGeneration,
            passed ? DiagnosticSeverity.Info : DiagnosticSeverity.Error);

        IReadOnlyList<Diagnostic> diagnostics = validation.Diagnostics.Count == 0 && !passed
            ? AppendDiagnostic(previous.Diagnostics, new Diagnostic
            {
                Severity = validation.State == AppleVirtualizationVmConfigurationValidationState.Unsupported
                    ? DiagnosticSeverity.Warning
                    : DiagnosticSeverity.Error,
                Code = HelperErrorCode,
                Message = "The Apple Virtualization helper did not pass VM configuration validation.",
                ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                TargetPath = "vmConfiguration.validate",
            })
            : validation.Diagnostics;

        return previous with
        {
            Phase = passed
                ? ResourcePhase.Reconciling
                : validation.State == AppleVirtualizationVmConfigurationValidationState.Unsupported
                    ? ResourcePhase.Degraded
                    : ResourcePhase.Failed,
            HostPhase = passed
                ? RuntimeHostPhase.Preparing
                : validation.State == AppleVirtualizationVmConfigurationValidationState.Unsupported
                    ? RuntimeHostPhase.Degraded
                    : RuntimeHostPhase.Failed,
            LastTransitionAt = now,
            Conditions = AppendCondition(previous.Conditions, validationCondition),
            Diagnostics = diagnostics,
            GuestControl = GuestControl(GuestControlExpected(spec), installed: false, reachable: false, previous.GuestControl?.Conditions),
            Readiness = Readiness(spec, ready: false, now),
            ControlPlane = ControlPlane(GuestControlExpected(spec), guestReachable: false),
            Provisioning = previous.Provisioning is null
                ? null
                : previous.Provisioning with { Complete = false },
        };
    }

    private static RuntimeHostStatus CreateStatus(
        ResourceMetadata<RuntimeHost> metadata,
        RuntimeHostSpec spec,
        RuntimeHostPhase hostPhase,
        ResourcePhase phase) =>
        new()
        {
            Phase = phase,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            HostPhase = hostPhase,
            ObservedCapacity = ObservedCapacity(spec),
            Generations = new RuntimeHostGenerationStatus
            {
                BootstrapGeneration = metadata.Generation,
            },
            Storage = Storage(spec, ready: false),
            Bootstrap = Bootstrap(spec, ready: false),
            Provisioning = spec.Bootstrap?.Provisioning is null ? null : new RuntimeHostProvisioningStatus(Complete: false),
            GuestControl = GuestControl(GuestControlExpected(spec), installed: GuestControlExpected(spec), reachable: false, Array.Empty<Condition>()),
            Readiness = Readiness(spec, ready: false, DateTimeOffset.UtcNow),
            ControlPlane = ControlPlane(GuestControlExpected(spec), guestReachable: false),
            Protection = new RuntimeHostProtectionStatus(false),
        };

    private RuntimeHostStatus Store(
        ResourceMetadata<RuntimeHost> metadata,
        RuntimeHostStatus status,
        RuntimeHostSpec? spec = null)
    {
        RuntimeHostStatus stored = _ledger.UpsertRuntimeHost(metadata, status, spec).Status;
        if (spec is not null)
        {
            _ledger.SetRuntimeHostConfigurationFingerprint(
                metadata.Id,
                metadata.Scope,
                ConfigurationFingerprint(spec));
        }
        return stored;
    }

    private string ConfigurationFingerprint(RuntimeHostSpec spec)
    {
        var input = new AppleVirtualizationRuntimeHostFingerprintInput(
            Spec: spec,
            EffectiveCpuCount: CpuCount(spec),
            EffectiveMemoryBytes: MemorySizeBytes(spec),
            EffectiveDiskBytes: spec.Capacity.StorageBytes.GetValueOrDefault(_options.DefaultDiskBytes),
            GuestImage: _options.GuestImage,
            EngineBootstrap: _options.EngineBootstrap,
            GuestControlExpected: GuestControlExpected(spec),
            GuestAgentVirtioSocketPort,
            RealVmBootEnabled: _options.FeatureGates.EnableRealVmBoot);
        byte[] canonical = JsonSerializer.SerializeToUtf8Bytes(
            input,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationRuntimeHostFingerprintInput);
        return Convert.ToHexString(SHA256.HashData(canonical));
    }

    private static ResourceMetadata<RuntimeHost> ToMetadata(
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

    private static RuntimeHostStatus FailureStatus(
        ResourceMetadata<RuntimeHost> metadata,
        RuntimeHostSpec spec,
        Diagnostic diagnostic) =>
        CreateStatus(metadata, spec, RuntimeHostPhase.Failed, ResourcePhase.Failed) with
        {
            Diagnostics = [diagnostic],
        };

    private static RuntimeHostStatus HandleFailureStatus(
        TargetHandle<RuntimeHost> handle,
        Diagnostic? diagnostic) =>
        new()
        {
            Phase = ResourcePhase.Failed,
            ObservedGeneration = default,
            LastTransitionAt = DateTimeOffset.UtcNow,
            HostPhase = RuntimeHostPhase.Failed,
            Diagnostics =
            [
                diagnostic ?? AppleVirtualizationHandleDiagnostics.Missing(
                    AppleVirtualizationProviderDescriptor.ProviderId,
                    "runtime-host/" + (handle.Route.BackingResourceId ?? "unknown")),
            ],
        };

    private static bool IsSupportedHost(PlatformSpec platform) =>
        string.Equals(platform.OperatingSystem, "macos", StringComparison.OrdinalIgnoreCase);

    private bool ShouldValidateVmConfiguration() =>
        _options.FeatureGates.EnableRealHelperActivation &&
        _options.FeatureGates.EnableVmConfigurationValidation &&
        !_options.FeatureGates.EnableRealVmBoot;

    private bool ShouldStartRealVm() =>
        _options.FeatureGates.EnableRealHelperActivation &&
        _options.FeatureGates.EnableRealVmBoot;

    private AppleVirtualizationVmConfigurationValidationRequest CreateVmConfigurationValidationRequest(
        string hostId,
        RuntimeHostSpec spec) =>
        new()
        {
            HostId = hostId,
            CpuCount = CpuCount(spec),
            MemorySizeBytes = MemorySizeBytes(spec),
            GuestImage = _options.GuestImage,
            IncludeSerialConsole = true,
            IncludeVirtioSocketPlaceholder = GuestControlExpected(spec),
        };

    private static int ToMilliseconds(TimeSpan gracePeriod)
    {
        if (gracePeriod <= TimeSpan.Zero)
        {
            return 0;
        }

        return gracePeriod.TotalMilliseconds >= int.MaxValue
            ? int.MaxValue
            : (int)Math.Ceiling(gracePeriod.TotalMilliseconds);
    }

    private static IReadOnlyList<Condition> PreconditionsAsConditions(
        IReadOnlyList<AppleVirtualizationPreflightFact> facts,
        ResourceGeneration observedGeneration)
    {
        if (facts.Count == 0)
        {
            return Array.Empty<Condition>();
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Condition[] conditions = new Condition[facts.Count];
        for (int i = 0; i < facts.Count; i++)
        {
            AppleVirtualizationPreflightFact fact = facts[i];
            conditions[i] = new Condition(
                "AppleVirtualization.RealModePrecondition." + fact.Name,
                fact.State == AppleVirtualizationPreflightFactState.Supported ? ConditionStatus.True : ConditionStatus.False,
                fact.Reason,
                fact.Message ?? fact.Reason,
                now,
                observedGeneration,
                fact.Severity);
        }

        return conditions;
    }

    private static int CpuCount(RuntimeHostSpec spec) =>
        Math.Max(1, (int)Math.Ceiling(spec.Capacity.CpuCores ?? 1));

    private long MemorySizeBytes(RuntimeHostSpec spec) =>
        spec.Capacity.MemoryBytes.GetValueOrDefault(_options.DefaultMemoryBytes);

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<Condition> Conditions) ValidateProviderInputs(
        RuntimeHostSpec spec,
        AppleVirtualizationGuestImageOptions guestImage,
        ResourceGeneration observedGeneration)
    {
        var diagnostics = new List<Diagnostic>(3);
        var conditions = new List<Condition>(3);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (guestImage.GetConfigurationState() == AppleVirtualizationGuestImageConfigurationState.MissingRequiredBootInputs ||
            string.IsNullOrWhiteSpace(guestImage.SerialLogPath))
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = BootInputMissingCode,
                Message = "Apple Virtualization runtime host validation requires configured Linux boot artifacts, disk image, and serial log path.",
                ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                TargetPath = "guestImage",
            });
            conditions.Add(ConfigurationCondition(
                "AppleVirtualization.BootInputsConfigured",
                "MissingRequiredBootInputs",
                "Linux boot artifacts, disk image, or serial log path are missing.",
                DiagnosticSeverity.Error,
                now,
                observedGeneration));
        }

        if (GuestControlExpected(spec) && string.IsNullOrWhiteSpace(guestImage.ExpectedGuestAgentVersion))
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Code = GuestAgentMissingCode,
                Message = "RuntimeHost guest-agent readiness requires an expected HPD guest-agent version before the host can become ready.",
                ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                TargetPath = "guestImage.expectedGuestAgentVersion",
            });
            conditions.Add(ConfigurationCondition(
                "AppleVirtualization.GuestAgentConfigured",
                "RequiresConfiguration",
                "Expected HPD guest-agent version is missing; helper health is not guest readiness.",
                DiagnosticSeverity.Warning,
                now,
                observedGeneration));
        }

        if (!guestImage.ExpectVirtiofsSupport)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Code = VirtiofsMissingCode,
                Message = "RuntimeHost validation requires an explicit virtiofs support expectation for future guest-verified projection.",
                ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                TargetPath = "guestImage.expectVirtiofsSupport",
            });
            conditions.Add(ConfigurationCondition(
                "AppleVirtualization.VirtiofsConfigured",
                "RequiresConfiguration",
                "Virtiofs support expectation is disabled; configured file sharing is not guest projection.",
                DiagnosticSeverity.Warning,
                now,
                observedGeneration));
        }

        return (diagnostics, conditions);
    }

    private static Condition ConfigurationCondition(
        string type,
        string reason,
        string message,
        DiagnosticSeverity severity,
        DateTimeOffset now,
        ResourceGeneration observedGeneration) =>
        new(
            type,
            ConditionStatus.False,
            reason,
            message,
            now,
            observedGeneration,
            severity);

    private static bool GuestControlExpected(RuntimeHostSpec? spec) =>
        spec?.Bootstrap?.GuestComponents.Any(component => component.Kind == GuestComponentKind.GuestAgent) == true ||
        spec?.Bootstrap?.ReadinessGates.Any(gate => gate.Scope == ReadinessGateScope.GuestControl) == true;

    private bool ShouldProbeGuestAgentReadiness(RuntimeHostSpec? spec, RuntimeHostStatus status) =>
        (GuestControlExpected(spec) || status.GuestControl?.Expected == true) &&
        status.HostPhase is RuntimeHostPhase.Running or RuntimeHostPhase.Ready &&
        status.Readiness?.Ready != true;

    private static TimeSpan? ReadinessTimeout(RuntimeHostSpec? spec)
    {
        TimeSpan timeout = TimeSpan.Zero;
        foreach (ReadinessGateSpec gate in spec?.Bootstrap?.ReadinessGates ?? Array.Empty<ReadinessGateSpec>())
        {
            if (gate.Scope == ReadinessGateScope.GuestControl && gate.Timeout is { } gateTimeout && gateTimeout > timeout)
            {
                timeout = gateTimeout;
            }
        }

        return timeout == TimeSpan.Zero ? null : timeout;
    }

    private static RuntimeHostStartGeneration NextHostStartGeneration(RuntimeHostStatus previous) =>
        new(previous.Generations.HostStartGeneration is { } generation ? generation.Value + 1 : 1);

    private static IReadOnlyList<Diagnostic> ReadinessStoppedDiagnostics(
        RuntimeHostStatus previous,
        RuntimeHostPhase mappedHostPhase,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        if (mappedHostPhase != RuntimeHostPhase.Stopped ||
            previous.HostPhase is not (RuntimeHostPhase.Running or RuntimeHostPhase.Ready) ||
            previous.Readiness?.Ready == true ||
            previous.GuestControl?.Expected != true)
        {
            return diagnostics;
        }

        return AppendDiagnostic(diagnostics, new Diagnostic
        {
            Severity = DiagnosticSeverity.Warning,
            Code = GuestAgentVmStoppedDuringReadinessCode,
            Message = "The VM stopped before verified guest-agent readiness was established.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = "guestAgent.readinessProbe",
        });
    }

    private static CapacityObservation ObservedCapacity(RuntimeHostSpec spec) =>
        new(
            spec.Capacity.CpuCores ?? 0,
            spec.Capacity.MemoryBytes ?? 0,
            spec.Capacity.StorageBytes ?? 0);

    private static RuntimeHostStorageStatus? Storage(RuntimeHostSpec spec, bool ready)
    {
        if (spec.Storage?.PrimaryDisk is null)
        {
            return null;
        }

        return new RuntimeHostStorageStatus(
            new RuntimeHostPrimaryDiskStatus(
                ready,
                spec.Storage.PrimaryDisk.Size,
                spec.Storage.PrimaryDisk.Format));
    }

    private static RuntimeHostBootstrapStatus? Bootstrap(
        RuntimeHostSpec spec,
        bool ready,
        Condition? engineCondition = null)
    {
        if (spec.Bootstrap is null)
        {
            return null;
        }

        RuntimeHostBootArtifactStatus[] artifacts = new RuntimeHostBootArtifactStatus[spec.Bootstrap.BootArtifacts.Count];
        for (int i = 0; i < artifacts.Length; i++)
        {
            artifacts[i] = new RuntimeHostBootArtifactStatus(spec.Bootstrap.BootArtifacts[i].Kind, ready);
        }

        return new RuntimeHostBootstrapStatus
        {
            Artifacts = artifacts,
            Conditions = engineCondition is { } condition ? [condition] : Array.Empty<Condition>(),
        };
    }

    private static RuntimeHostProvisioningStatus? EngineProvisioning(
        RuntimeHostSpec spec,
        EngineBootstrapIntent intent,
        ResourcePhase phase,
        bool complete)
    {
        if (spec.Bootstrap is null)
        {
            return null;
        }

        RuntimeHostProvisioningStepStatus engineStep = new(
            intent.ComponentName,
            RuntimeHostProvisioningStage.GuestControlInstall,
            phase,
            complete ? DateTimeOffset.UtcNow : null);

        if (spec.Bootstrap.Provisioning?.Steps is not { Count: > 0 } steps)
        {
            return new RuntimeHostProvisioningStatus([engineStep], complete);
        }

        RuntimeHostProvisioningStepStatus[] statuses = new RuntimeHostProvisioningStepStatus[steps.Count + 1];
        for (int i = 0; i < steps.Count; i++)
        {
            RuntimeHostProvisioningStepSpec step = steps[i];
            statuses[i] = new RuntimeHostProvisioningStepStatus(
                step.Name,
                step.Stage,
                complete ? ResourcePhase.Ready : ResourcePhase.Pending,
                complete ? DateTimeOffset.UtcNow : null);
        }

        statuses[^1] = engineStep;
        return new RuntimeHostProvisioningStatus(statuses, complete);
    }

    private static Condition EngineCondition(
        ConditionStatus status,
        string reason,
        string message,
        DiagnosticSeverity severity,
        ResourceGeneration observedGeneration) =>
        new(
            "AppleVirtualization.EngineBootstrap",
            status,
            reason,
            message,
            DateTimeOffset.UtcNow,
            observedGeneration,
            severity);

    private static GuestControlStatus GuestControl(
        bool expected,
        bool installed,
        bool reachable,
        IReadOnlyList<Condition>? conditions) =>
        new(
            expected,
            installed,
            reachable,
            reachable
                ? new ProviderNamedEndpoint(
                    "guest-control",
                    ProviderEndpointPurpose.GuestControl,
                    new ProviderEndpoint("vsock", "hpd-guest-agent"),
                    ProviderTransportKind.Vsock)
                : null,
            ProviderTransportKind.Vsock,
            conditions);

    private static RuntimeHostReadinessStatus Readiness(
        RuntimeHostSpec? spec,
        bool ready,
        DateTimeOffset now,
        RuntimeHostReadinessStatus? previous = null,
        RuntimeHostStartGeneration? observedHostStartGeneration = null)
    {
        IReadOnlyList<ReadinessGateSpec> gates = spec?.Bootstrap?.ReadinessGates ?? Array.Empty<ReadinessGateSpec>();
        RuntimeHostStartGeneration? hostStartGeneration =
            observedHostStartGeneration ?? previous?.ObservedHostStartGeneration;
        if (gates.Count == 0)
        {
            if (previous?.Gates is { Count: > 0 } previousGates)
            {
                ReadinessGateStatus[] preservedStatuses = new ReadinessGateStatus[previousGates.Count];
                ConditionStatus preservedGateStatus = ready ? ConditionStatus.True : ConditionStatus.False;
                for (int i = 0; i < previousGates.Count; i++)
                {
                    preservedStatuses[i] = previousGates[i] with
                    {
                        Status = preservedGateStatus,
                        LastCheckedAt = now,
                        Message = ready ? "Ready." : previousGates[i].Message ?? "Waiting for guest-control readiness.",
                    };
                }

                return new RuntimeHostReadinessStatus(ready, hostStartGeneration, preservedStatuses);
            }

            return previous is null || previous.Ready != ready || previous.ObservedHostStartGeneration != hostStartGeneration
                ? new RuntimeHostReadinessStatus(ready, hostStartGeneration)
                : previous;
        }

        ReadinessGateStatus[] statuses = new ReadinessGateStatus[gates.Count];
        ConditionStatus gateStatus = ready ? ConditionStatus.True : ConditionStatus.False;
        for (int i = 0; i < gates.Count; i++)
        {
            statuses[i] = new ReadinessGateStatus(
                gates[i].Name,
                gates[i].Kind,
                gateStatus,
                now,
                ready ? "Ready." : "Waiting for guest-control readiness.");
        }

        return new RuntimeHostReadinessStatus(ready, hostStartGeneration, statuses);
    }

    private static RuntimeHostControlPlaneStatus ControlPlane(
        bool guestExpected,
        bool guestReachable,
        ProviderComponentStatus? engineComponent = null)
    {
        ProviderNamedEndpoint? endpoint = guestReachable
            ? new ProviderNamedEndpoint(
                "guest-control",
                ProviderEndpointPurpose.GuestControl,
                new ProviderEndpoint("vsock", "hpd-guest-agent"),
                ProviderTransportKind.Vsock)
            : null;

        ProviderComponentStatus driver = new(
            ProviderComponentKind.Driver,
            "apple-virtualization-helper",
            guestReachable ? ProviderComponentPhase.Ready : ProviderComponentPhase.Starting);
        ProviderComponentStatus guest = new(
            ProviderComponentKind.GuestAgent,
            "hpd-guest-agent",
            guestExpected
                ? guestReachable ? ProviderComponentPhase.Ready : ProviderComponentPhase.Starting
                : ProviderComponentPhase.Stopped,
            Endpoint: endpoint);
        IReadOnlyList<ProviderComponentStatus> components = engineComponent is null
            ? [driver, guest]
            : [driver, guest, engineComponent];

        return new RuntimeHostControlPlaneStatus(
            Components: components,
            Endpoints: endpoint is null ? Array.Empty<ProviderNamedEndpoint>() : [endpoint]);
    }

    private static ResourcePhase PhaseFor(RuntimeHostPhase hostPhase) =>
        hostPhase switch
        {
            RuntimeHostPhase.Declared => ResourcePhase.Pending,
            RuntimeHostPhase.Preparing or RuntimeHostPhase.Provisioning or RuntimeHostPhase.Starting or RuntimeHostPhase.Running or RuntimeHostPhase.Stopping or RuntimeHostPhase.Resetting => ResourcePhase.Reconciling,
            RuntimeHostPhase.Ready or RuntimeHostPhase.Stopped => ResourcePhase.Ready,
            RuntimeHostPhase.Degraded => ResourcePhase.Degraded,
            RuntimeHostPhase.Deleting => ResourcePhase.Deleting,
            RuntimeHostPhase.Deleted => ResourcePhase.Deleted,
            RuntimeHostPhase.Failed => ResourcePhase.Failed,
            _ => ResourcePhase.Unknown,
        };

    private static ProviderComponentStatus EngineComponent(
        string name,
        ProviderComponentPhase phase,
        string reason,
        string message,
        ResourceGeneration observedGeneration) =>
        new(
            ProviderComponentKind.EngineDaemon,
            name,
            phase,
            Conditions:
            [
                new Condition(
                    "AppleVirtualization.EngineBootstrap",
                    phase == ProviderComponentPhase.Ready ? ConditionStatus.True : ConditionStatus.False,
                    reason,
                    message,
                    DateTimeOffset.UtcNow,
                    observedGeneration,
                    phase == ProviderComponentPhase.Ready ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning),
            ]);

    private static ProviderComponentPhase EngineComponentPhaseFor(AppleVirtualizationEngineObservationState state) =>
        state switch
        {
            AppleVirtualizationEngineObservationState.Ready => ProviderComponentPhase.Ready,
            AppleVirtualizationEngineObservationState.Degraded => ProviderComponentPhase.Degraded,
            AppleVirtualizationEngineObservationState.Failed => ProviderComponentPhase.Failed,
            AppleVirtualizationEngineObservationState.Installed or AppleVirtualizationEngineObservationState.Starting => ProviderComponentPhase.Starting,
            AppleVirtualizationEngineObservationState.RequiresConfiguration or AppleVirtualizationEngineObservationState.Unsupported => ProviderComponentPhase.Degraded,
            _ => ProviderComponentPhase.Stopped,
        };

    private static string EngineBootstrapReason(AppleVirtualizationEngineStatusResponse engine) =>
        engine.ObservationState.ToString();

    private static string EngineBootstrapMessage(AppleVirtualizationEngineStatusResponse engine) =>
        engine.Ready
            ? "Container runtime guest component is ready inside the VM; engine API access remains authority-bound."
            : BoundDiagnosticMessage(engine.Conditions.Count > 0
                ? engine.Conditions[0].Message
                : "Container runtime guest component is not ready.");

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

    private static IReadOnlyList<Diagnostic> AppendDiagnostics(
        IReadOnlyList<Diagnostic> existing,
        IReadOnlyList<Diagnostic> additional)
    {
        if (additional.Count == 0)
        {
            return existing;
        }

        Diagnostic[] diagnostics = new Diagnostic[existing.Count + additional.Count];
        for (int i = 0; i < existing.Count; i++)
        {
            diagnostics[i] = existing[i];
        }

        for (int i = 0; i < additional.Count; i++)
        {
            diagnostics[existing.Count + i] = additional[i];
        }

        return diagnostics;
    }

    private static IReadOnlyList<Condition> AppendCondition(IReadOnlyList<Condition> existing, Condition condition)
    {
        Condition[] conditions = new Condition[existing.Count + 1];
        for (int i = 0; i < existing.Count; i++)
        {
            conditions[i] = existing[i];
        }

        conditions[^1] = condition;
        return conditions;
    }

    private static Diagnostic ToDiagnostic(AppleVirtualizationHelperError? error, string operation)
    {
        if (error is null)
        {
            return new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = HelperErrorCode,
                Message = "The Apple Virtualization helper returned an error response without an error payload.",
                ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                TargetPath = operation,
            };
        }

        return new Diagnostic
        {
            Severity = error.Severity,
            Code = new DiagnosticCode(error.Code),
            Message = BoundDiagnosticMessage(error.Message),
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

    private static string BoundDiagnosticMessage(string value, int maxLength = MaxReadinessDiagnosticMessageLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, maxLength - 3), "...");
    }

    private static Diagnostic UnsupportedHost(PlatformSpec platform) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = UnsupportedHostCode,
            Message = "Apple Virtualization runtime hosts require a macOS host with the Virtualization.framework entitlement boundary.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = "host.platform/" + platform.OperatingSystem,
        };

    private static Diagnostic HostInvalidatedUnitsDiagnostic(string hostId, string operation) =>
        new()
        {
            Severity = DiagnosticSeverity.Warning,
            Code = new DiagnosticCode("AppleVirtualization.ExecutionUnitHostInvalidated"),
            Message = $"Runtime host '{hostId}' was stopped or deleted by {operation}; dependent execution units are no longer usable.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = operation,
        };

    private static string OperationName(AppleVirtualizationHelperOperation operation) =>
        AppleVirtualizationHelperOperationNames.ToWireName(operation);

    private readonly record struct EngineBootstrapIntent(bool Requested, string ComponentName, string EngineId)
    {
        public static EngineBootstrapIntent FromSpec(RuntimeHostSpec spec)
        {
            foreach (GuestComponentSpec component in spec.Bootstrap?.GuestComponents ?? Array.Empty<GuestComponentSpec>())
            {
                if (component.Kind != GuestComponentKind.ContainerRuntime)
                {
                    continue;
                }

                string name = string.IsNullOrWhiteSpace(component.Name)
                    ? "container-runtime"
                    : BoundDiagnosticMessage(component.Name, 128);
                return new EngineBootstrapIntent(true, name, "engine-" + name);
            }

            return default;
        }
    }
}
