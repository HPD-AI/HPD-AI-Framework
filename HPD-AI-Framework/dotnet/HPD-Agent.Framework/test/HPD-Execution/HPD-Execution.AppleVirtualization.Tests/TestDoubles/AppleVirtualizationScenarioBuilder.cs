namespace HPD.Execution.AppleVirtualization.Tests.TestDoubles;

using System.Text;
using HPD.Execution.AppleVirtualization.GuestAgent;
using HPD.Execution.AppleVirtualization.Protocol;
using HPD.Execution.Contracts;

public sealed class AppleVirtualizationScenarioBuilder
{
    private readonly List<AppleVirtualizationHelperEnvelope> _responses = [];
    private readonly List<AppleVirtualizationHelperEnvelope> _events = [];
    private DateTimeOffset _observedAt = new(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);
    private long _sequence;
    private ulong _providerGeneration = 1;

    public static AppleVirtualizationScenarioBuilder FirstSliceSuccess() =>
        new AppleVirtualizationScenarioBuilder()
            .WithHelloSuccess()
            .WithHostStatus("host-1", RuntimeHostPhase.Running, guestControlReachable: false)
            .WithGuestControlReady("host-1")
            .WithProjectionMounted("projection-1", "/workspace")
            .WithUnitReady("unit-1", "/workspace")
            .WithProcessStarted("process-1", "guest-pid-1")
            .WithProcessOutput("process-1", ProcessOutputStream.Stdout, "Linux hpd-vm\n"u8.ToArray(), final: false)
            .WithProcessOutput("process-1", ProcessOutputStream.Stderr, ReadOnlyMemory<byte>.Empty, final: true)
            .WithProcessExited("process-1", exitCode: 0);

    public AppleVirtualizationScenarioBuilder WithProviderGeneration(ulong generation)
    {
        _providerGeneration = generation;
        return this;
    }

    public AppleVirtualizationScenarioBuilder WithHelloSuccess(
        bool protocolCompatible = true,
        bool frameworkAvailable = true,
        bool entitlementVerified = true)
    {
        _responses.Add(Response(AppleVirtualizationHelperOperation.Hello) with
        {
            PayloadSchema = AppleVirtualizationHelperProtocol.HelloResponseSchema,
            HelloResponse = new AppleVirtualizationHelperHelloResponse
            {
                HelperVersion = "0.1.0-test",
                ProtocolVersion = AppleVirtualizationHelperProtocol.CurrentVersion,
                ProviderGeneration = _providerGeneration,
                ProtocolCompatible = protocolCompatible,
                VirtualizationFrameworkAvailable = frameworkAvailable,
                VirtualizationEntitlementVerified = entitlementVerified,
            },
        });

        return this;
    }

    public AppleVirtualizationScenarioBuilder WithHostStatus(
        string hostId,
        RuntimeHostPhase hostPhase,
        bool guestControlReachable,
        IReadOnlyList<Diagnostic>? diagnostics = null)
    {
        _responses.Add(Response(AppleVirtualizationHelperOperation.HostStatus) with
        {
            ResourceKind = new ResourceKind("runtime-host"),
            ResourceId = hostId,
            ResourceScope = new ResourceScope("acceptance-runtime"),
            ProviderHandle = ProviderHandle("host", hostId),
            ProviderGeneration = _providerGeneration,
            HostStatusResponse = new AppleVirtualizationHostStatusResponse
            {
                HostId = hostId,
                HostPhase = hostPhase,
                Phase = hostPhase is RuntimeHostPhase.Ready ? ResourcePhase.Ready : ResourcePhase.Reconciling,
                ProviderHandle = ProviderHandle("host", hostId),
                GuestControlReachable = guestControlReachable,
                Diagnostics = diagnostics ?? Array.Empty<Diagnostic>(),
            },
        });

        return this;
    }

    public AppleVirtualizationScenarioBuilder WithGuestControlReady(string hostId)
    {
        _events.Add(Event(AppleVirtualizationHelperOperation.GuestControlWaitReady) with
        {
            ResourceKind = new ResourceKind("runtime-host"),
            ResourceId = hostId,
            ResourceScope = new ResourceScope("acceptance-runtime"),
            ProviderHandle = ProviderHandle("host", hostId),
            ProviderGeneration = _providerGeneration,
            HostStatusResponse = new AppleVirtualizationHostStatusResponse
            {
                HostId = hostId,
                HostPhase = RuntimeHostPhase.Ready,
                Phase = ResourcePhase.Ready,
                ProviderHandle = ProviderHandle("host", hostId),
                GuestControlReachable = true,
                Conditions =
                [
                    Condition("GuestControlReady", ConditionStatus.True, "GuestAgentHandshakeComplete"),
                ],
            },
        });

        return this;
    }

    public AppleVirtualizationScenarioBuilder WithProjectionMounted(string projectionId, string guestPath)
    {
        _responses.Add(Response(AppleVirtualizationHelperOperation.ProjectionStatus) with
        {
            ResourceKind = new ResourceKind("content-projection"),
            ResourceId = projectionId,
            ResourceScope = new ResourceScope("acceptance-runtime"),
            ProviderHandle = ProviderHandle("projection", projectionId),
            ProviderGeneration = _providerGeneration,
            ProjectionStatusResponse = new AppleVirtualizationProjectionStatusResponse
            {
                ProjectionId = projectionId,
                ProjectionPhase = ContentProjectionPhase.Projected,
                EffectiveRealization = ProjectionRealizationKind.LiveProjection,
                EffectiveWriteEffect = ProjectionWriteEffect.NoWrites,
                EffectiveCoherence = CoherenceClass.CloseToOpen,
                GuestAgentReady = true,
                HostShareConfigured = true,
                FrameworkShareAccepted = true,
                VerifiedByGuestAgent = true,
                GuestProjectionStatus = new AppleVirtualizationGuestAgentProjectionStatus
                {
                    ProjectionId = projectionId,
                    GuestPath = guestPath,
                    Tag = "hpdprojection",
                    Mounted = true,
                    GuestMountVerified = true,
                    HostShareState = AppleVirtualizationGuestAgentProjectionHostShareState.HostShareConfigured,
                    FrameworkShareState = AppleVirtualizationGuestAgentProjectionFrameworkShareState.Accepted,
                    VerificationState = AppleVirtualizationGuestAgentProjectionVerificationState.ReadyForHpdUse,
                    ExpectedGuestPath = guestPath,
                    ActualGuestPath = guestPath,
                    RequestedAccessMode = AccessMode.ReadOnly,
                    EffectiveAccessMode = AccessMode.ReadOnly,
                    ProjectionPhase = ContentProjectionPhase.Projected,
                    EffectiveRealization = ProjectionRealizationKind.LiveProjection,
                    EffectiveWriteEffect = ProjectionWriteEffect.NoWrites,
                    EffectiveCoherence = CoherenceClass.CloseToOpen,
                    EffectiveCache = CacheBehavior.ReadCache,
                    Conditions =
                    [
                        Condition("GuestMountVerified", ConditionStatus.True, $"Mounted:{guestPath}"),
                    ],
                },
                Conditions =
                [
                    Condition("GuestMountVerified", ConditionStatus.True, $"Mounted:{guestPath}"),
                ],
            },
        });

        return this;
    }

    public AppleVirtualizationScenarioBuilder WithProjectionFailure(string projectionId, string code)
    {
        _responses.Add(Response(AppleVirtualizationHelperOperation.ProjectionStatus) with
        {
            ResourceKind = new ResourceKind("content-projection"),
            ResourceId = projectionId,
            ResourceScope = new ResourceScope("acceptance-runtime"),
            Error = Error(code, "Projection failed before guest mount verification.", retryable: false, failedPhase: nameof(ContentProjectionPhase.Projecting)),
        });

        return this;
    }

    public AppleVirtualizationScenarioBuilder WithUnitReady(string unitId, string workingDirectory)
    {
        _responses.Add(Response(AppleVirtualizationHelperOperation.UnitStatus) with
        {
            ResourceKind = new ResourceKind("execution-unit"),
            ResourceId = unitId,
            ResourceScope = new ResourceScope("acceptance-runtime"),
            ProviderHandle = ProviderHandle("unit", unitId),
            ProviderGeneration = _providerGeneration,
            UnitStatusResponse = new AppleVirtualizationUnitStatusResponse
            {
                UnitId = unitId,
                UnitPhase = ExecutionUnitPhase.Ready,
                ProviderHandle = ProviderHandle("unit", unitId),
                WorkingDirectory = workingDirectory,
            },
        });

        return this;
    }

    public AppleVirtualizationScenarioBuilder WithProcessStarted(string processId, string providerProcessId)
    {
        _events.Add(Event(AppleVirtualizationHelperOperation.ProcessStart) with
        {
            ResourceKind = new ResourceKind("process-invocation"),
            ResourceId = processId,
            ResourceScope = new ResourceScope("acceptance-runtime"),
            ProviderGeneration = _providerGeneration,
            ProcessStatusResponse = new AppleVirtualizationProcessStatusResponse
            {
                ProcessId = processId,
                ProcessPhase = ProcessInvocationPhase.Running,
                IoState = ProcessIoState.Open,
                ProviderProcessId = providerProcessId,
            },
        });

        return this;
    }

    public AppleVirtualizationScenarioBuilder WithProcessOutput(
        string processId,
        ProcessOutputStream stream,
        ReadOnlyMemory<byte> bytes,
        bool final = false,
        bool truncated = false)
    {
        _events.Add(Event(AppleVirtualizationHelperOperation.ProcessReadOutput) with
        {
            ResourceKind = new ResourceKind("process-invocation"),
            ResourceId = processId,
            ResourceScope = new ResourceScope("acceptance-runtime"),
            PayloadSchema = AppleVirtualizationHelperProtocol.ProcessOutputEventSchema,
            ProviderGeneration = _providerGeneration,
            ProcessOutputEvent = new AppleVirtualizationProcessOutputEvent
            {
                ProcessId = processId,
                Stream = stream,
                Sequence = NextSequence(),
                ObservedAt = _observedAt,
                Bytes = bytes,
                Flags = (final ? ProcessOutputChunkFlags.Final : ProcessOutputChunkFlags.None) |
                    (truncated ? ProcessOutputChunkFlags.Truncated : ProcessOutputChunkFlags.None),
            },
        });

        return this;
    }

    public AppleVirtualizationScenarioBuilder WithProcessOutput(
        string processId,
        ProcessOutputStream stream,
        string utf8Text,
        bool final = false,
        bool truncated = false) =>
        WithProcessOutput(processId, stream, Encoding.UTF8.GetBytes(utf8Text), final, truncated);

    public AppleVirtualizationScenarioBuilder WithProcessExited(string processId, int exitCode)
    {
        _responses.Add(Response(AppleVirtualizationHelperOperation.ProcessWait) with
        {
            ResourceKind = new ResourceKind("process-invocation"),
            ResourceId = processId,
            ResourceScope = new ResourceScope("acceptance-runtime"),
            ProviderGeneration = _providerGeneration,
            ProcessStatusResponse = new AppleVirtualizationProcessStatusResponse
            {
                ProcessId = processId,
                ProcessPhase = ProcessInvocationPhase.Exited,
                IoState = ProcessIoState.Closed,
                Result = new ProcessInvocationResult
                {
                    ProviderProcessId = $"guest-{processId}",
                    ExitCode = exitCode,
                    CompletionKind = ProcessCompletionKind.Exited,
                    StartedAt = _observedAt,
                    ExitedAt = _observedAt.AddSeconds(1),
                    Duration = TimeSpan.FromSeconds(1),
                    Output = new ProcessCapturedOutput
                    {
                        Stdout = new ProcessStreamOutput(),
                        Stderr = new ProcessStreamOutput(),
                        OutputDrainTimeout = ProcessInvocationPolicy.Default.OutputDrainTimeout,
                    },
                },
            },
        });

        return this;
    }

    public AppleVirtualizationScenarioBuilder WithHelperFailure(
        AppleVirtualizationHelperOperation operation,
        string code,
        string message,
        bool retryable = false)
    {
        _responses.Add(Response(operation) with
        {
            Error = Error(code, message, retryable, failedPhase: null),
        });

        return this;
    }

    public AppleVirtualizationScenarioBuilder WithStaleHandle(
        AppleVirtualizationHelperOperation operation,
        string resourceId,
        ulong staleGeneration)
    {
        _responses.Add(Response(operation) with
        {
            ResourceId = resourceId,
            ProviderGeneration = _providerGeneration,
            Error = Error(
                "AppleVirtualization.StaleHandle",
                $"Handle generation {staleGeneration} is stale for provider generation {_providerGeneration}.",
                retryable: false,
                failedPhase: "lookup"),
        });

        return this;
    }

    public AppleVirtualizationScenario Build()
    {
        var client = new FakeAppleVirtualizationHelperClient();
        foreach (AppleVirtualizationHelperEnvelope response in _responses)
        {
            client.EnqueueResponse(response);
        }

        foreach (AppleVirtualizationHelperEnvelope helperEvent in _events)
        {
            client.EnqueueEvent(helperEvent);
        }

        return new AppleVirtualizationScenario(client, _responses.ToArray(), _events.ToArray());
    }

    private AppleVirtualizationHelperEnvelope Response(AppleVirtualizationHelperOperation operation) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            RequestId = $"request-{NextSequence()}",
            SequenceNumber = NextSequence(),
            Timestamp = Tick(),
            ProviderGeneration = _providerGeneration,
        };

    private AppleVirtualizationHelperEnvelope Event(AppleVirtualizationHelperOperation operation) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Event,
            Operation = operation,
            EventId = $"event-{NextSequence()}",
            SequenceNumber = NextSequence(),
            Timestamp = Tick(),
            ProviderGeneration = _providerGeneration,
        };

    private long NextSequence() => ++_sequence;

    private DateTimeOffset Tick()
    {
        _observedAt = _observedAt.AddMilliseconds(10);
        return _observedAt;
    }

    private static ProviderOpaqueHandle ProviderHandle(string kind, string id) =>
        new(AppleVirtualizationProviderDescriptor.ProviderId, $"{kind}:{id}", Generation: 1);

    private Condition Condition(string type, ConditionStatus status, string reason) =>
        new(type, status, reason, reason, _observedAt, new ResourceGeneration(_sequence));

    private static AppleVirtualizationHelperError Error(string code, string message, bool retryable, string? failedPhase) =>
        new()
        {
            Code = code,
            Message = message,
            Retryable = retryable,
            FailedPhase = failedPhase,
            Severity = DiagnosticSeverity.Error,
        };
}

public sealed record AppleVirtualizationScenario(
    FakeAppleVirtualizationHelperClient Client,
    IReadOnlyList<AppleVirtualizationHelperEnvelope> Responses,
    IReadOnlyList<AppleVirtualizationHelperEnvelope> Events);
