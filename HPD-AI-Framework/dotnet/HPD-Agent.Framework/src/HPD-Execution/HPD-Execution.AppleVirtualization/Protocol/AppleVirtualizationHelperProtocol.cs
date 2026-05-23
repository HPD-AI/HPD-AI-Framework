namespace HPD.Execution.AppleVirtualization.Protocol;

using System.Collections.Concurrent;
using System.Text.Json;
using System.Runtime.CompilerServices;
using HPD.Execution.AppleVirtualization;
using HPD.Execution.AppleVirtualization.GuestAgent;
using HPD.Execution.Contracts;

public static class AppleVirtualizationHelperProtocol
{
    public const string CurrentVersion = "1.0";

    public static readonly SchemaVersion SchemaVersion = new("v1");
    public static readonly ContentType JsonContentType = new("application/json");

    public static readonly SchemaId HelloRequestSchema = new("hpd.execution.apple-virtualization.helper.hello.request.v1");
    public static readonly SchemaId HelloResponseSchema = new("hpd.execution.apple-virtualization.helper.hello.response.v1");
    public static readonly SchemaId HealthResponseSchema = new("hpd.execution.apple-virtualization.helper.health.response.v1");
    public static readonly SchemaId CapabilitiesResponseSchema = new("hpd.execution.apple-virtualization.helper.capabilities.response.v1");
    public static readonly SchemaId PreflightResponseSchema = new("hpd.execution.apple-virtualization.helper.preflight.response.v1");
    public static readonly SchemaId ActivationStatusResponseSchema = new("hpd.execution.apple-virtualization.helper.activation.status.response.v1");
    public static readonly SchemaId HostRequestSchema = new("hpd.execution.apple-virtualization.helper.host.request.v1");
    public static readonly SchemaId HostResponseSchema = new("hpd.execution.apple-virtualization.helper.host.response.v1");
    public static readonly SchemaId GuestControlResponseSchema = new("hpd.execution.apple-virtualization.helper.guest-control.response.v1");
    public static readonly SchemaId GuestAgentTransportRequestSchema = new("hpd.execution.apple-virtualization.helper.guest-agent.transport.request.v1");
    public static readonly SchemaId GuestAgentTransportResponseSchema = new("hpd.execution.apple-virtualization.helper.guest-agent.transport.response.v1");
    public static readonly SchemaId GuestAgentReadinessRequestSchema = new("hpd.execution.apple-virtualization.helper.guest-agent.readiness.request.v1");
    public static readonly SchemaId GuestAgentReadinessResponseSchema = new("hpd.execution.apple-virtualization.helper.guest-agent.readiness.response.v1");
    public static readonly SchemaId ProjectionRequestSchema = new("hpd.execution.apple-virtualization.helper.projection.request.v1");
    public static readonly SchemaId ProjectionResponseSchema = new("hpd.execution.apple-virtualization.helper.projection.response.v1");
    public static readonly SchemaId ProjectionSyncRequestSchema = new("hpd.execution.apple-virtualization.helper.projection.sync.request.v1");
    public static readonly SchemaId ProjectionSyncResponseSchema = new("hpd.execution.apple-virtualization.helper.projection.sync.response.v1");
    public static readonly SchemaId ProjectionFinalizationRequestSchema = new("hpd.execution.apple-virtualization.helper.projection.finalization.request.v1");
    public static readonly SchemaId ProjectionFinalizationResponseSchema = new("hpd.execution.apple-virtualization.helper.projection.finalization.response.v1");
    public static readonly SchemaId NetworkStatusRequestSchema = new("hpd.execution.apple-virtualization.helper.network.status.request.v1");
    public static readonly SchemaId NetworkStatusResponseSchema = new("hpd.execution.apple-virtualization.helper.network.status.response.v1");
    public static readonly SchemaId EndpointPublicationRequestSchema = new("hpd.execution.apple-virtualization.helper.endpoint.publication.request.v1");
    public static readonly SchemaId EndpointPublicationResponseSchema = new("hpd.execution.apple-virtualization.helper.endpoint.publication.response.v1");
    public static readonly SchemaId AuthorityBindingRequestSchema = new("hpd.execution.apple-virtualization.helper.authority.binding.request.v1");
    public static readonly SchemaId AuthorityBindingResponseSchema = new("hpd.execution.apple-virtualization.helper.authority.binding.response.v1");
    public static readonly SchemaId EngineStatusRequestSchema = new("hpd.execution.apple-virtualization.helper.engine.status.request.v1");
    public static readonly SchemaId EngineStatusResponseSchema = new("hpd.execution.apple-virtualization.helper.engine.status.response.v1");
    public static readonly SchemaId EngineProvisionRequestSchema = new("hpd.execution.apple-virtualization.helper.engine.provision.request.v1");
    public static readonly SchemaId EngineProvisionResponseSchema = new("hpd.execution.apple-virtualization.helper.engine.provision.response.v1");
    public static readonly SchemaId UnitRequestSchema = new("hpd.execution.apple-virtualization.helper.unit.request.v1");
    public static readonly SchemaId UnitResponseSchema = new("hpd.execution.apple-virtualization.helper.unit.response.v1");
    public static readonly SchemaId ProcessRequestSchema = new("hpd.execution.apple-virtualization.helper.process.request.v1");
    public static readonly SchemaId ProcessResponseSchema = new("hpd.execution.apple-virtualization.helper.process.response.v1");
    public static readonly SchemaId ProcessOutputEventSchema = new("hpd.execution.apple-virtualization.helper.process.output.v1");
    public static readonly SchemaId VmConfigurationValidationRequestSchema = new("hpd.execution.apple-virtualization.helper.vm-configuration.validation.request.v1");
    public static readonly SchemaId VmConfigurationValidationResponseSchema = new("hpd.execution.apple-virtualization.helper.vm-configuration.validation.response.v1");
    public static readonly SchemaId ErrorSchema = new("hpd.execution.apple-virtualization.helper.error.v1");
}

public enum AppleVirtualizationHelperMessageType { Request, Response, Event }
public enum AppleVirtualizationHelperResponseStatus { Ok, Accepted, Error }

public enum AppleVirtualizationHelperOperation
{
    Hello,
    CapabilitiesGet,
    PreflightRun,
    ActivationStatus,
    HealthProbe,
    Shutdown,
    HostEnsure,
    HostStart,
    HostStatus,
    HostRequestStop,
    HostStop,
    HostDelete,
    GuestControlWaitReady,
    GuestControlStatus,
    ProjectionConfigure,
    ProjectionMount,
    ProjectionStatus,
    ProjectionRelease,
    UnitEnsure,
    UnitStatus,
    UnitStop,
    UnitDelete,
    ProcessStart,
    ProcessStdin,
    ProcessCloseStdin,
    ProcessSignal,
    ProcessStop,
    ProcessWait,
    ProcessReadOutput,
    EndpointStatus,
    EndpointUnsupported,
    ProcessResize,
    VmConfigurationValidate,
    GuestAgentTransportProbe,
    GuestAgentReadinessProbe,
    ProjectionUnmount,
    ProjectionObserve,
    ProjectionSync,
    ProjectionFinalize,
    ProjectionEnumerateChanges,
    ProjectionPromote,
    NetworkStatus,
    EndpointPublish,
    EndpointRelease,
    AuthorityBind,
    AuthorityStatus,
    AuthorityRevoke,
    EngineStatus,
    EngineProvision,
}

public enum AppleVirtualizationHelperEventKind
{
    HelperStarted,
    HelperReady,
    HelperDegraded,
    HelperStopping,
    HelperExiting,
    PreflightFact,
    PermissionStatus,
    SigningStatus,
    VmStateChanged,
    VmValidationFailed,
    VmStarting,
    VmRunning,
    VmGuestStopped,
    VmStoppedWithError,
    VmFailed,
    GuestControlConnecting,
    GuestControlReady,
    GuestControlUnreachable,
    GuestControlLost,
    ProjectionProjecting,
    ProjectionMounted,
    ProjectionDegraded,
    ProjectionFailed,
    UnitReady,
    UnitStopped,
    UnitFailed,
    ProcessStarted,
    ProcessOutput,
    ProcessExited,
    ProcessFailed,
    Diagnostic,
    Log,
    Metric,
    VmValidationSucceeded,
    GuestAgentTransportConnecting,
    GuestAgentTransportConnected,
    GuestAgentTransportUnavailable,
    GuestAgentReadinessVerified,
    GuestAgentReadinessUnavailable,
    NetworkObserved,
    NetworkAttachmentDisconnected,
    EndpointPublished,
    EndpointReleased,
    EndpointRouteDegraded,
    AuthorityBound,
    AuthorityRevoked,
    AuthorityBindingFailed,
    EngineObserved,
    EngineDegraded,
    EngineProvisioned,
}

public enum AppleVirtualizationPreflightFactState
{
    Unknown,
    Supported,
    Unsupported,
    RequiresConfiguration,
    RequiresRemediation,
}

public sealed record AppleVirtualizationHelperEnvelope
{
    public string ProtocolVersion { get; init; } = AppleVirtualizationHelperProtocol.CurrentVersion;
    public required AppleVirtualizationHelperMessageType MessageType { get; init; }
    public required AppleVirtualizationHelperOperation Operation { get; init; }
    public string? RequestId { get; init; }
    public string? EventId { get; init; }
    public long SequenceNumber { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public AppleVirtualizationHelperResponseStatus? ResponseStatus { get; init; }
    public AppleVirtualizationHelperEventKind? EventKind { get; init; }
    public ResourceKind? ResourceKind { get; init; }
    public string? ResourceId { get; init; }
    public ResourceScope? ResourceScope { get; init; }
    public ResourceGeneration? ResourceGeneration { get; init; }
    public ProviderOpaqueHandle? ProviderHandle { get; init; }
    public ulong ProviderGeneration { get; init; }
    public SchemaId? PayloadSchema { get; init; }
    public AppleVirtualizationHelperError? Error { get; init; }
    public AppleVirtualizationHelperHelloRequest? HelloRequest { get; init; }
    public AppleVirtualizationHelperHelloResponse? HelloResponse { get; init; }
    public AppleVirtualizationCapabilitiesGetRequest? CapabilitiesGetRequest { get; init; }
    public AppleVirtualizationCapabilitiesGetResponse? CapabilitiesGetResponse { get; init; }
    public AppleVirtualizationPreflightRunRequest? PreflightRunRequest { get; init; }
    public AppleVirtualizationPreflightRunResponse? PreflightRunResponse { get; init; }
    public AppleVirtualizationActivationStatusResponse? ActivationStatusResponse { get; init; }
    public AppleVirtualizationHealthProbeRequest? HealthProbeRequest { get; init; }
    public AppleVirtualizationHealthProbeResponse? HealthProbeResponse { get; init; }
    public AppleVirtualizationShutdownRequest? ShutdownRequest { get; init; }
    public AppleVirtualizationShutdownResponse? ShutdownResponse { get; init; }
    public AppleVirtualizationHostEnsureRequest? HostEnsureRequest { get; init; }
    public AppleVirtualizationHostLifecycleRequest? HostLifecycleRequest { get; init; }
    public AppleVirtualizationHostStatusResponse? HostStatusResponse { get; init; }
    public AppleVirtualizationGuestControlWaitReadyRequest? GuestControlWaitReadyRequest { get; init; }
    public AppleVirtualizationGuestControlStatusResponse? GuestControlStatusResponse { get; init; }
    public AppleVirtualizationGuestAgentTransportProbeRequest? GuestAgentTransportProbeRequest { get; init; }
    public AppleVirtualizationGuestAgentTransportProbeResponse? GuestAgentTransportProbeResponse { get; init; }
    public AppleVirtualizationGuestAgentReadinessProbeRequest? GuestAgentReadinessProbeRequest { get; init; }
    public AppleVirtualizationGuestAgentReadinessProbeResponse? GuestAgentReadinessProbeResponse { get; init; }
    public AppleVirtualizationProjectionConfigureRequest? ProjectionConfigureRequest { get; init; }
    public AppleVirtualizationProjectionMountRequest? ProjectionMountRequest { get; init; }
    public AppleVirtualizationProjectionStatusRequest? ProjectionStatusRequest { get; init; }
    public AppleVirtualizationProjectionUnmountRequest? ProjectionUnmountRequest { get; init; }
    public AppleVirtualizationProjectionObserveRequest? ProjectionObserveRequest { get; init; }
    public AppleVirtualizationProjectionSyncRequest? ProjectionSyncRequest { get; init; }
    public AppleVirtualizationGuestAgentProjectionSyncResult? ProjectionSyncResult { get; init; }
    public AppleVirtualizationProjectionFinalizationRequest? ProjectionFinalizationRequest { get; init; }
    public AppleVirtualizationGuestAgentProjectionFinalizationResult? ProjectionFinalizationResult { get; init; }
    public AppleVirtualizationProjectionChangeEnumerationRequest? ProjectionChangeEnumerationRequest { get; init; }
    public AppleVirtualizationGuestAgentProjectionChangeEnumerationResult? ProjectionChangeEnumerationResult { get; init; }
    public AppleVirtualizationProjectionPromotionRequest? ProjectionPromotionRequest { get; init; }
    public AppleVirtualizationGuestAgentProjectionPromotionResult? ProjectionPromotionResult { get; init; }
    public AppleVirtualizationProjectionLifecycleRequest? ProjectionLifecycleRequest { get; init; }
    public AppleVirtualizationProjectionStatusResponse? ProjectionStatusResponse { get; init; }
    public AppleVirtualizationUnitEnsureRequest? UnitEnsureRequest { get; init; }
    public AppleVirtualizationUnitLifecycleRequest? UnitLifecycleRequest { get; init; }
    public AppleVirtualizationUnitStatusResponse? UnitStatusResponse { get; init; }
    public AppleVirtualizationProcessStartRequest? ProcessStartRequest { get; init; }
    public AppleVirtualizationProcessStdinRequest? ProcessStdinRequest { get; init; }
    public AppleVirtualizationProcessSignalRequest? ProcessSignalRequest { get; init; }
    public AppleVirtualizationProcessStopRequest? ProcessStopRequest { get; init; }
    public AppleVirtualizationProcessResizeRequest? ProcessResizeRequest { get; init; }
    public AppleVirtualizationProcessLifecycleRequest? ProcessLifecycleRequest { get; init; }
    public AppleVirtualizationProcessStatusResponse? ProcessStatusResponse { get; init; }
    public AppleVirtualizationProcessOutputEvent? ProcessOutputEvent { get; init; }
    public AppleVirtualizationVmConfigurationValidationRequest? VmConfigurationValidationRequest { get; init; }
    public AppleVirtualizationVmConfigurationValidationResponse? VmConfigurationValidationResponse { get; init; }
    public AppleVirtualizationNetworkStatusRequest? NetworkStatusRequest { get; init; }
    public AppleVirtualizationNetworkStatusResponse? NetworkStatusResponse { get; init; }
    public AppleVirtualizationEndpointPublicationRequest? EndpointPublicationRequest { get; init; }
    public AppleVirtualizationEndpointPublicationResponse? EndpointPublicationResponse { get; init; }
    public AppleVirtualizationAuthorityBindingRequest? AuthorityBindingRequest { get; init; }
    public AppleVirtualizationAuthorityBindingResponse? AuthorityBindingResponse { get; init; }
    public AppleVirtualizationEngineStatusRequest? EngineStatusRequest { get; init; }
    public AppleVirtualizationEngineStatusResponse? EngineStatusResponse { get; init; }
    public AppleVirtualizationEngineProvisioningRequest? EngineProvisioningRequest { get; init; }
    public AppleVirtualizationEngineProvisioningResponse? EngineProvisioningResponse { get; init; }

    public static AppleVirtualizationHelperEnvelope Request(
        AppleVirtualizationHelperOperation operation,
        string requestId,
        long sequenceNumber,
        SchemaId? payloadSchema = null) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Request,
            Operation = operation,
            RequestId = requestId,
            SequenceNumber = sequenceNumber,
            PayloadSchema = payloadSchema,
        };

    public AppleVirtualizationHelperEnvelope ToResponse(long sequenceNumber) =>
        this with
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            ResponseStatus = Error is null ? AppleVirtualizationHelperResponseStatus.Ok : AppleVirtualizationHelperResponseStatus.Error,
            SequenceNumber = sequenceNumber,
            Timestamp = DateTimeOffset.UtcNow,
            CausationId = RequestId,
        };

    public AppleVirtualizationHelperEnvelope ToErrorResponse(
        long sequenceNumber,
        AppleVirtualizationHelperError error) =>
        ToResponse(sequenceNumber) with
        {
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Error,
            PayloadSchema = AppleVirtualizationHelperProtocol.ErrorSchema,
            Error = error,
        };
}

public sealed record AppleVirtualizationHelperError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Operation { get; init; }
    public bool Retryable { get; init; }
    public string? FailedPhase { get; init; }
    public DiagnosticSeverity Severity { get; init; } = DiagnosticSeverity.Error;
    public SchemaId? DetailSchema { get; init; }
    public ReadOnlyMemory<byte> Detail { get; init; }
}

public sealed record AppleVirtualizationHelperHelloRequest
{
    public string ClientName { get; init; } = "HPD-Execution.AppleVirtualization";
    public string MinimumProtocolVersion { get; init; } = AppleVirtualizationHelperProtocol.CurrentVersion;
    public string RequestedProtocolVersion { get; init; } = AppleVirtualizationHelperProtocol.CurrentVersion;
}

public sealed record AppleVirtualizationHelperHelloResponse
{
    public string HelperName { get; init; } = AppleVirtualizationProviderDescriptor.HelperExecutableName;
    public required string HelperVersion { get; init; }
    public required string ProtocolVersion { get; init; }
    public ulong ProviderGeneration { get; init; }
    public bool ProtocolCompatible { get; init; }
    public bool VirtualizationFrameworkAvailable { get; init; }
    public bool VirtualizationEntitlementVerified { get; init; }
    public IReadOnlyList<AppleVirtualizationPreflightFact> PreflightFacts { get; init; } = Array.Empty<AppleVirtualizationPreflightFact>();
    public IReadOnlyList<ProviderPreflightCheck> PreflightChecks { get; init; } = Array.Empty<ProviderPreflightCheck>();
}

public sealed record AppleVirtualizationPreflightFact
{
    public required string Name { get; init; }
    public AppleVirtualizationPreflightFactState State { get; init; } = AppleVirtualizationPreflightFactState.Unknown;
    public string Reason { get; init; } = "Unknown";
    public string? Message { get; init; }
    public string? ObservedValue { get; init; }
    public DiagnosticSeverity Severity { get; init; } = DiagnosticSeverity.Info;
}

public sealed record AppleVirtualizationCapabilitiesGetRequest
{
    public CapabilityRequirementSet Requirements { get; init; } = CapabilityRequirementSet.Empty;
    public PlatformSpec? HostPlatform { get; init; }
    public PlatformSpec? GuestPlatform { get; init; }
    public GuestAbiSpec? GuestAbi { get; init; }
}

public sealed record AppleVirtualizationCapabilitiesGetResponse
{
    public IReadOnlyList<CapabilityFact> Capabilities { get; init; } = Array.Empty<CapabilityFact>();
    public IReadOnlyList<ProviderLimit> Limits { get; init; } = Array.Empty<ProviderLimit>();
    public IReadOnlyList<ProviderPreflightCheck> PreflightChecks { get; init; } = Array.Empty<ProviderPreflightCheck>();
}

public sealed record AppleVirtualizationPreflightRunRequest
{
    public bool IncludeSigning { get; init; } = true;
    public bool IncludeEntitlement { get; init; } = true;
    public bool IncludeVmValidation { get; init; } = true;
}

public sealed record AppleVirtualizationPreflightRunResponse
{
    public IReadOnlyList<AppleVirtualizationPreflightFact> Facts { get; init; } = Array.Empty<AppleVirtualizationPreflightFact>();
    public IReadOnlyList<ProviderPreflightCheck> Checks { get; init; } = Array.Empty<ProviderPreflightCheck>();
    public IReadOnlyList<ProviderPermissionRequirement> Permissions { get; init; } = Array.Empty<ProviderPermissionRequirement>();
}

public sealed record AppleVirtualizationActivationStatusResponse
{
    public ProviderActivationPhase Phase { get; init; } = ProviderActivationPhase.Unknown;
    public string? ActivationId { get; init; }
    public ulong ProviderGeneration { get; init; }
    public IReadOnlyList<ProviderComponentStatus> Components { get; init; } = Array.Empty<ProviderComponentStatus>();
    public IReadOnlyList<ProviderNamedEndpoint> Endpoints { get; init; } = Array.Empty<ProviderNamedEndpoint>();
}

public sealed record AppleVirtualizationHealthProbeRequest(bool IncludeGuestControl = false);
public sealed record AppleVirtualizationHealthProbeResponse(bool Ready, string? Detail = null);
public sealed record AppleVirtualizationShutdownRequest(string? Reason = null, TimeSpan? GracePeriod = null);
public sealed record AppleVirtualizationShutdownResponse(bool Accepted, string? Detail = null);

public sealed record AppleVirtualizationHostEnsureRequest
{
    public required string HostId { get; init; }
    public required PlatformSpec Platform { get; init; }
    public ResourceQuotaPolicy Capacity { get; init; } = ResourceQuotaPolicy.Default;
    public AppleVirtualizationGuestImageOptions? GuestImage { get; init; }
    public string? BootImagePath { get; init; }
    public string? KernelPath { get; init; }
    public string? InitrdPath { get; init; }
    public string? KernelCommandLine { get; init; }
    public string? DiskImagePath { get; init; }
    public string? EfiVariableStorePath { get; init; }
    public string? SerialLogPath { get; init; }
    public bool ExpectVirtiofsSupport { get; init; } = true;
    public string? ExpectedGuestAgentVersion { get; init; }
}

public sealed record AppleVirtualizationHostLifecycleRequest
{
    public required string HostId { get; init; }
    public bool ExplicitRealMode { get; init; }
    public AppleVirtualizationVmConfigurationValidationRequest? VmConfigurationValidationRequest { get; init; }
    public StopKind? StopKind { get; init; }
    public TimeSpan? GracePeriod { get; init; }
    public int? GracePeriodMilliseconds { get; init; }
    public string? Reason { get; init; }
}

public sealed record AppleVirtualizationHostStatusResponse
{
    public required string HostId { get; init; }
    public RuntimeHostPhase HostPhase { get; init; }
    public ResourcePhase Phase { get; init; }
    public ProviderOpaqueHandle? ProviderHandle { get; init; }
    public bool GuestControlReachable { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
}

public sealed record AppleVirtualizationGuestControlWaitReadyRequest
{
    public required string HostId { get; init; }
    public TimeSpan? Timeout { get; init; }
    public string? ExpectedAgentVersion { get; init; }
}

public sealed record AppleVirtualizationGuestControlStatusResponse
{
    public required string HostId { get; init; }
    public bool Expected { get; init; } = true;
    public bool Installed { get; init; }
    public bool Reachable { get; init; }
    public ProviderTransportKind Transport { get; init; } = ProviderTransportKind.Vsock;
    public string? AgentVersion { get; init; }
    public string? GuestBootId { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
}

public enum AppleVirtualizationGuestAgentTransportState
{
    NotAttempted,
    WaitingForVmRunning,
    Connecting,
    Connected,
    Refused,
    Timeout,
    Unsupported,
    Failed,
}

public enum AppleVirtualizationGuestAgentTransportKind
{
    VirtioSocket,
    SerialPort,
    Network,
    ProviderDefined,
}

public sealed record AppleVirtualizationGuestAgentTransportEndpoint
{
    public AppleVirtualizationGuestAgentTransportKind Kind { get; init; } = AppleVirtualizationGuestAgentTransportKind.VirtioSocket;
    public uint? Port { get; init; }
    public string? Address { get; init; }
    public string? Name { get; init; }
}

public sealed record AppleVirtualizationGuestAgentTransportProbeRequest
{
    public required string HostId { get; init; }
    public AppleVirtualizationGuestAgentTransportEndpoint Endpoint { get; init; } = new();
    public int TimeoutMilliseconds { get; init; } = 1_000;
    public bool ExplicitRealMode { get; init; }
    public bool RequireVmRunning { get; init; } = true;
    public AppleVirtualizationGuestAgentTransportState? ScriptedStatus { get; init; }
}

public sealed record AppleVirtualizationGuestAgentTransportProbeResponse
{
    public required string HostId { get; init; }
    public AppleVirtualizationGuestAgentTransportState State { get; init; } = AppleVirtualizationGuestAgentTransportState.NotAttempted;
    public AppleVirtualizationGuestAgentTransportEndpoint Endpoint { get; init; } = new();
    public bool VmRunning { get; init; }
    public bool Connected => State == AppleVirtualizationGuestAgentTransportState.Connected;
    public bool GuestReady { get; init; }
    public string? Reason { get; init; }
    public string? Message { get; init; }
    public AppleVirtualizationHelperError? Error { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
}

public enum AppleVirtualizationGuestAgentReadinessState
{
    NotAttempted,
    TransportNotConnected,
    Handshaking,
    Ready,
    NotReady,
    IncompatibleProtocol,
    IncompatibleAgentVersion,
    MissingCapability,
    MalformedFrame,
    GuestAgentError,
    Disconnected,
    Timeout,
    Failed,
}

public sealed record AppleVirtualizationGuestAgentReadinessProbeRequest
{
    public required string HostId { get; init; }
    public AppleVirtualizationGuestAgentTransportEndpoint Endpoint { get; init; } = new();
    public int TimeoutMilliseconds { get; init; } = 1_000;
    public bool ExplicitRealMode { get; init; }
    public string ExpectedProtocolVersion { get; init; } = AppleVirtualizationGuestAgentProtocol.CurrentVersion;
    public string? ExpectedAgentVersion { get; init; }
    public IReadOnlyList<string> RequiredCapabilities { get; init; } = Array.Empty<string>();
    public AppleVirtualizationGuestAgentReadinessState? ScriptedState { get; init; }
}

public sealed record AppleVirtualizationGuestAgentReadinessProbeResponse
{
    public required string HostId { get; init; }
    public AppleVirtualizationGuestAgentReadinessState State { get; init; } = AppleVirtualizationGuestAgentReadinessState.NotAttempted;
    public AppleVirtualizationGuestAgentTransportState TransportState { get; init; } = AppleVirtualizationGuestAgentTransportState.NotAttempted;
    public AppleVirtualizationGuestAgentTransportEndpoint Endpoint { get; init; } = new();
    public bool VmRunning { get; init; }
    public bool TransportConnected { get; init; }
    public bool VerifiedReady { get; init; }
    public string? ProtocolVersion { get; init; }
    public string? AgentVersion { get; init; }
    public string? GuestBootId { get; init; }
    public ulong GuestBootGeneration { get; init; }
    public ulong GuestAgentGeneration { get; init; }
    public AppleVirtualizationGuestAgentCapabilities? Capabilities { get; init; }
    public IReadOnlyList<string> MissingCapabilities { get; init; } = Array.Empty<string>();
    public string? Reason { get; init; }
    public string? Message { get; init; }
    public AppleVirtualizationHelperError? Error { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
}

public enum AppleVirtualizationNetworkAttachmentKind
{
    Nat,
    Bridged,
    Vmnet,
    FileHandle,
    VirtioSocket,
    ProviderDefined,
}

public enum AppleVirtualizationNetworkObservationState
{
    Unknown,
    Ready,
    RequiresConfiguration,
    RequiresPermission,
    Unsupported,
    TemporarilyUnavailable,
    Failed,
}

public sealed record AppleVirtualizationNetworkStatusRequest
{
    public required string HostId { get; init; }
    public AppleVirtualizationNetworkAttachmentKind RequestedAttachment { get; init; } = AppleVirtualizationNetworkAttachmentKind.Nat;
    public bool IncludeGuestObservation { get; init; } = true;
    public bool IncludeSocketObservation { get; init; } = true;
    public bool ExplicitRealMode { get; init; }
    public int MaxInterfaces { get; init; } = AppleVirtualizationGuestAgentNetworkResultBounds.DefaultMaxInterfaces;
    public int MaxRoutes { get; init; } = AppleVirtualizationGuestAgentNetworkResultBounds.DefaultMaxRoutes;
    public int MaxListeners { get; init; } = AppleVirtualizationGuestAgentNetworkResultBounds.DefaultMaxListeners;
    public AppleVirtualizationGuestAgentReadinessState? ScriptedReadinessState { get; init; }
}

public sealed record AppleVirtualizationNetworkStatusResponse
{
    public required string HostId { get; init; }
    public AppleVirtualizationNetworkObservationState State { get; init; } = AppleVirtualizationNetworkObservationState.Unknown;
    public AppleVirtualizationNetworkAttachmentKind DefaultAttachment { get; init; } = AppleVirtualizationNetworkAttachmentKind.Nat;
    public AppleVirtualizationNetworkAttachmentKind RequestedAttachment { get; init; } = AppleVirtualizationNetworkAttachmentKind.Nat;
    public NetworkCapabilitySet RealizedCapabilities { get; init; }
    public DiscoveryCapabilitySet DiscoveryCapabilities { get; init; }
    public bool VmRunning { get; init; }
    public bool GuestAgentReady { get; init; }
    public bool VirtioSocketConfigured { get; init; }
    public IReadOnlyList<AppleVirtualizationNetworkAttachmentCapabilityFact> AttachmentCapabilities { get; init; } = Array.Empty<AppleVirtualizationNetworkAttachmentCapabilityFact>();
    public AppleVirtualizationGuestAgentNetworkStatus? GuestNetworkStatus { get; init; }
    public IReadOnlyList<NetworkLimitation> Limitations { get; init; } = Array.Empty<NetworkLimitation>();
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
}

public sealed record AppleVirtualizationNetworkAttachmentCapabilityFact
{
    public AppleVirtualizationNetworkAttachmentKind AttachmentKind { get; init; }
    public CapabilityState State { get; init; } = CapabilityState.Unsupported;
    public NetworkCapabilitySet Capabilities { get; init; }
    public bool ConfiguredAtVmConfigurationTime { get; init; } = true;
    public bool RuntimeAttachmentMutable { get; init; }
    public bool RequiresEntitlement { get; init; }
    public string Detail { get; init; } = string.Empty;
    public IReadOnlyList<NetworkLimitation> Limitations { get; init; } = Array.Empty<NetworkLimitation>();
}

public enum AppleVirtualizationEndpointPublicationAction
{
    Publish,
    Status,
    Release,
}

public sealed record AppleVirtualizationEndpointPublicationRequest
{
    public required string EndpointId { get; init; }
    public AppleVirtualizationEndpointPublicationAction Action { get; init; } = AppleVirtualizationEndpointPublicationAction.Publish;
    public EndpointListenerKind ListenerKind { get; init; } = EndpointListenerKind.HostAddress;
    public NetworkTransport Transport { get; init; } = NetworkTransport.Tcp;
    public EndpointExposureScope ExposureScope { get; init; } = EndpointExposureScope.HostLocal;
    public string? ListenerAddress { get; init; } = "127.0.0.1";
    public ushort? RequestedPort { get; init; }
    public bool AllowEphemeralPort { get; init; }
    public bool RequireStableListener { get; init; }
    public EndpointTargetKind TargetKind { get; init; } = EndpointTargetKind.NetworkMembership;
    public string? TargetResourceId { get; init; }
    public string? TargetAddress { get; init; }
    public ushort? TargetPort { get; init; }
    public string? TargetSocketPath { get; init; }
    public bool ReconcileRouteOnTargetRestart { get; init; } = true;
    public bool RequireRouteHealth { get; init; } = true;
    public bool ScriptedRouteHealthy { get; init; } = true;
}

public sealed record AppleVirtualizationEndpointPublicationResponse
{
    public required string EndpointId { get; init; }
    public PublishedEndpointPhase EndpointPhase { get; init; } = PublishedEndpointPhase.Pending;
    public EndpointListenerKind ListenerKind { get; init; } = EndpointListenerKind.HostAddress;
    public NetworkTransport Transport { get; init; } = NetworkTransport.Tcp;
    public EndpointExposureScope ExposureScope { get; init; } = EndpointExposureScope.HostLocal;
    public string? BoundAddress { get; init; }
    public ushort? BoundPort { get; init; }
    public bool HpdOwned { get; init; }
    public bool RouteHealthy { get; init; }
    public string? ResolvedAddress { get; init; }
    public ushort? ResolvedPort { get; init; }
    public string? ResolvedSocketPath { get; init; }
    public IReadOnlyList<NetworkLimitation> Limitations { get; init; } = Array.Empty<NetworkLimitation>();
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
}

public enum AppleVirtualizationAuthorityBindingAction
{
    Bind,
    Status,
    Revoke,
}

public sealed record AppleVirtualizationAuthoritySourceDescriptor
{
    public AuthoritySourceKind Kind { get; init; } = AuthoritySourceKind.ProviderDefined;
    public BoundaryLocus Locus { get; init; } = BoundaryLocus.Host;
    public HostServiceKind? HostService { get; init; }
    public UnixSocketPath? SocketPath { get; init; }
    public CredentialRef? Credential { get; init; }
    public string? ProviderCapabilityName { get; init; }
    public SensitiveEndpointKind? SensitiveEndpointKind { get; init; }
    public SensitiveAuthorityClass AuthorityClass { get; init; } = SensitiveAuthorityClass.ProviderDefined;
    public string? RedactedDisplayName { get; init; }
}

public sealed record AppleVirtualizationAuthorityTargetDescriptor
{
    public AuthorityTargetKind Kind { get; init; } = AuthorityTargetKind.ProviderDefined;
    public string? UnitId { get; init; }
    public string? ProcessId { get; init; }
    public string? ServiceName { get; init; }
    public BoundaryLocus Locus { get; init; } = BoundaryLocus.ExecutionUnit;
}

public sealed record AppleVirtualizationAuthorityProjectionDescriptor
{
    public AuthorityProjectionKind Kind { get; init; } = AuthorityProjectionKind.ProviderDefined;
    public UnixSocketPath? TargetSocketPath { get; init; }
    public string? EnvironmentVariableName { get; init; }
    public UnixSocketPermissions? SocketPermissions { get; init; }
    public bool ReadOnly { get; init; } = true;
}

public sealed record AppleVirtualizationAuthorityLeaseDescriptor
{
    public BindingLifetime Lifetime { get; init; } = BindingLifetime.ExecutionUnit;
    public DateTimeOffset BoundAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool RevokeOnTargetStop { get; init; } = true;
    public bool SurviveTargetRestart { get; init; }
    public int? RevocationGracePeriodMilliseconds { get; init; }
}

public sealed record AppleVirtualizationAuthorityBindingRequest
{
    public required string BindingId { get; init; }
    public AppleVirtualizationAuthorityBindingAction Action { get; init; } = AppleVirtualizationAuthorityBindingAction.Bind;
    public AppleVirtualizationAuthoritySourceDescriptor Source { get; init; } = new();
    public AppleVirtualizationAuthorityTargetDescriptor Target { get; init; } = new();
    public AppleVirtualizationAuthorityProjectionDescriptor Projection { get; init; } = new();
    public AuthorityBindingDirection Direction { get; init; } = AuthorityBindingDirection.HostToGuest;
    public SensitiveAuthorityClass RequestedAuthorityClass { get; init; } = SensitiveAuthorityClass.ProviderDefined;
    public SensitiveAuthorityClass EffectiveAuthorityClass { get; init; } = SensitiveAuthorityClass.ProviderDefined;
    public SensitiveRedactionLevel Redaction { get; init; } = SensitiveRedactionLevel.RedactSecretValues;
    public bool RequireAudit { get; init; } = true;
    public bool AllowProviderSideProxy { get; init; } = true;
    public string? AuditLabel { get; init; }
    public string? AuditCorrelationId { get; init; }
    public uint? VirtioSocketPort { get; init; }
    public int? ObservedFileDescriptor { get; init; }
    public bool? GuestSocketPresent { get; init; }
    public AppleVirtualizationAuthorityLeaseDescriptor Lease { get; init; } = new();
    public AppleVirtualizationGuestAgentAuthorityGenerationStamp Generation { get; init; } = new();
}

public sealed record AppleVirtualizationAuthorityBindingResponse
{
    public required string BindingId { get; init; }
    public AuthorityBindingPhase BindingPhase { get; init; } = AuthorityBindingPhase.Pending;
    public AppleVirtualizationGuestAgentBoundAuthority? BoundAuthority { get; init; }
    public RevocationVerificationStatus RevocationStatus { get; init; } = RevocationVerificationStatus.Unknown;
    public IReadOnlyList<AppleVirtualizationAuthorityRevocationEvidence> RevocationEvidence { get; init; } =
        Array.Empty<AppleVirtualizationAuthorityRevocationEvidence>();
    public IReadOnlyList<AuthorityAuditEvent> AuditEvents { get; init; } = Array.Empty<AuthorityAuditEvent>();
    public bool AuditEventsTruncated { get; init; }
    public IReadOnlyList<NetworkLimitation> Limitations { get; init; } = Array.Empty<NetworkLimitation>();
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
}

public enum AppleVirtualizationAuthorityRevocationEvidenceKind
{
    None,
    ListenerRemoved,
    ConnectionFileDescriptorClosed,
    GuestSocketAbsent,
    ListenerStillRegistered,
    ConnectionFileDescriptorOpen,
    GuestSocketPresent,
    Unsupported,
}

public sealed record AppleVirtualizationAuthorityRevocationEvidence
{
    public string EvidenceProtocolVersion { get; init; } = "v1";
    public AppleVirtualizationAuthorityRevocationEvidenceKind Kind { get; init; } =
        AppleVirtualizationAuthorityRevocationEvidenceKind.None;
    public bool Observed { get; init; }
    public uint? VirtioSocketPort { get; init; }
    public int? FileDescriptor { get; init; }
    public UnixSocketPath? GuestSocketPath { get; init; }
    public string? Detail { get; init; }
    public DateTimeOffset? ObservedAt { get; init; }
}

public enum AppleVirtualizationEngineObservationState
{
    Unknown,
    NotInstalled,
    Installed,
    Starting,
    Ready,
    Degraded,
    Failed,
    Unsupported,
    RequiresConfiguration,
}

public enum AppleVirtualizationEngineProvisioningPackageManager
{
    Auto,
    Apt,
    Dnf,
    Apk,
    Pacman,
    Unsupported,
}

public enum AppleVirtualizationEngineProvisioningAction
{
    ObserveExisting,
    ValidatePrerequisites,
    InstallPackages,
    ConfigureService,
    EnableService,
    StartService,
}

public enum AppleVirtualizationEngineProvisioningPhase
{
    NotRequested,
    Planned,
    Ready,
    Degraded,
    Failed,
}

public enum AppleVirtualizationEngineProvisioningExecutionState
{
    NotRequested,
    Succeeded,
    TimedOut,
    Failed,
}

public sealed record AppleVirtualizationEngineProvisioningPrerequisiteStatus
{
    public static AppleVirtualizationEngineProvisioningPrerequisiteStatus Supported { get; } = new();

    public bool PackageManagerAvailable { get; init; } = true;
    public bool SystemdAvailable { get; init; } = true;
    public bool UserSystemdAvailable { get; init; } = true;
    public bool GuestAgentAvailable { get; init; } = true;
    public bool RootlessSupported { get; init; } = true;
    public bool RootfulSupported { get; init; } = true;
    public bool ImageStoreSupported { get; init; } = true;
    public bool NetworkAvailable { get; init; } = true;
    public bool WritableGuestStorageAvailable { get; init; } = true;
    public bool GuestOsSupported { get; init; } = true;
}

public sealed record AppleVirtualizationEngineProvisioningPlanStep
{
    public required string Name { get; init; }
    public AppleVirtualizationEngineProvisioningAction Action { get; init; }
    public bool Required { get; init; } = true;
    public string? Detail { get; init; }
}

public sealed record AppleVirtualizationEngineProvisioningOutputCapture
{
    public int MaxCapturedBytes { get; init; }
    public int CapturedBytes { get; init; }
    public bool Truncated { get; init; }
    public string? Text { get; init; }
    public int StdoutCapturedBytes { get; init; }
    public int StderrCapturedBytes { get; init; }
    public bool StdoutTruncated { get; init; }
    public bool StderrTruncated { get; init; }
    public string? StdoutText { get; init; }
    public string? StderrText { get; init; }
}

public sealed record AppleVirtualizationEngineProvisioningEvidence
{
    public bool HelperMediated { get; init; } = true;
    public bool GuestAgentMediated { get; init; } = true;
    public bool HostShellInvoked { get; init; }
    public bool HostDockerInvoked { get; init; }
    public AppleVirtualizationEngineProvisioningPackageManager PackageManager { get; init; } =
        AppleVirtualizationEngineProvisioningPackageManager.Auto;
    public bool PackageManagerAvailable { get; init; }
    public bool NetworkAvailable { get; init; }
    public bool WritableGuestStorageAvailable { get; init; }
    public bool SystemdAvailable { get; init; }
    public bool UserSystemdAvailable { get; init; }
    public bool ExistingEngineObserved { get; init; }
    public bool PackageInstallAllowed { get; init; }
    public bool ServiceEnablementAllowed { get; init; }
    public bool InstallAttempted { get; init; }
    public int TimeoutMilliseconds { get; init; }
    public bool TimedOut { get; init; }
    public int MaxCapturedOutputBytes { get; init; }
    public int StdoutCapturedBytes { get; init; }
    public int StderrCapturedBytes { get; init; }
    public bool StdoutTruncated { get; init; }
    public bool StderrTruncated { get; init; }
}

public sealed record AppleVirtualizationEngineProvisioningRequest
{
    public required string HostId { get; init; }
    public string? EngineId { get; init; }
    public EngineControlPlaneKind Kind { get; init; } = EngineControlPlaneKind.DockerCompatible;
    public EngineApiKind Api { get; init; } = EngineApiKind.DockerCompatible;
    public EngineAuthorityMode AuthorityMode { get; init; } = EngineAuthorityMode.Rootless;
    public EngineImageStoreMode ImageStore { get; init; } = EngineImageStoreMode.ProviderManaged;
    public EngineWorkloadAdoptionMode WorkloadAdoption { get; init; } = EngineWorkloadAdoptionMode.None;
    public bool ExplicitRealMode { get; init; }
    public bool AllowPackageInstall { get; init; }
    public bool AllowServiceEnablement { get; init; }
    public int ProvisioningTimeoutMilliseconds { get; init; } = 120_000;
    public int MaxCapturedOutputBytes { get; init; } = AppleVirtualizationEngineProvisioningOptions.DefaultMaxCapturedOutputBytes;
    public AppleVirtualizationEngineProvisioningPackageManager PackageManager { get; init; } =
        AppleVirtualizationEngineProvisioningPackageManager.Auto;
    public AppleVirtualizationEngineProvisioningExecutionState ScriptedExecutionState { get; init; } =
        AppleVirtualizationEngineProvisioningExecutionState.NotRequested;
    public AppleVirtualizationEngineProvisioningPrerequisiteStatus ScriptedPrerequisites { get; init; } =
        AppleVirtualizationEngineProvisioningPrerequisiteStatus.Supported;
    public string? ScriptedOutput { get; init; }
    public string? ScriptedStdout { get; init; }
    public string? ScriptedStderr { get; init; }
}

public sealed record AppleVirtualizationEngineProvisioningResponse
{
    public required string HostId { get; init; }
    public string? EngineId { get; init; }
    public AppleVirtualizationEngineProvisioningPhase Phase { get; init; } = AppleVirtualizationEngineProvisioningPhase.NotRequested;
    public EngineControlPlaneKind Kind { get; init; } = EngineControlPlaneKind.DockerCompatible;
    public EngineApiKind Api { get; init; } = EngineApiKind.DockerCompatible;
    public EngineAuthorityMode AuthorityMode { get; init; } = EngineAuthorityMode.Rootless;
    public EngineImageStoreMode ImageStore { get; init; } = EngineImageStoreMode.ProviderManaged;
    public bool ExistingEngineObserved { get; init; }
    public bool InstallAttempted { get; init; }
    public string? GuestSocketPath { get; init; }
    public AppleVirtualizationEngineProvisioningPrerequisiteStatus Prerequisites { get; init; } =
        AppleVirtualizationEngineProvisioningPrerequisiteStatus.Supported;
    public IReadOnlyList<AppleVirtualizationEngineProvisioningPlanStep> Plan { get; init; } =
        Array.Empty<AppleVirtualizationEngineProvisioningPlanStep>();
    public AppleVirtualizationEngineProvisioningOutputCapture Output { get; init; } = new();
    public AppleVirtualizationEngineProvisioningEvidence Evidence { get; init; } = new();
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
}

public sealed record AppleVirtualizationEngineStatusRequest
{
    public required string HostId { get; init; }
    public string? EngineId { get; init; }
    public EngineControlPlaneKind Kind { get; init; } = EngineControlPlaneKind.DockerCompatible;
    public EngineApiKind Api { get; init; } = EngineApiKind.DockerCompatible;
    public EngineAuthorityMode AuthorityMode { get; init; } = EngineAuthorityMode.Rootless;
    public EngineImageStoreMode ImageStore { get; init; } = EngineImageStoreMode.ProviderManaged;
    public EngineWorkloadAdoptionMode WorkloadAdoption { get; init; } = EngineWorkloadAdoptionMode.None;
    public bool IncludeGuestObservation { get; init; } = true;
    public bool ExplicitRealMode { get; init; }
    public BoundaryLocus ObservationLocus { get; init; } = BoundaryLocus.RuntimeHost;
    public string? ObservedSocketPath { get; init; }
    public string? ObservedVersion { get; init; }
    public string? ObservedStatus { get; init; }
    public IReadOnlyList<AppleVirtualizationGuestAgentContainerObservation> ObservedContainers { get; init; } =
        Array.Empty<AppleVirtualizationGuestAgentContainerObservation>();
    public IReadOnlyList<Diagnostic> ObservedDiagnostics { get; init; } = Array.Empty<Diagnostic>();
    public int MaxEndpoints { get; init; } = AppleVirtualizationGuestAgentEngineResultBounds.DefaultMaxEndpoints;
    public int MaxContainers { get; init; } = AppleVirtualizationGuestAgentEngineResultBounds.DefaultMaxContainers;
    public int MaxDiagnostics { get; init; } = AppleVirtualizationGuestAgentEngineResultBounds.DefaultMaxDiagnostics;
    public int MaxVersionLength { get; init; } = AppleVirtualizationGuestAgentEngineResultBounds.DefaultMaxVersionLength;
    public int MaxStatusLength { get; init; } = AppleVirtualizationGuestAgentEngineResultBounds.DefaultMaxStatusLength;
    public AppleVirtualizationEngineObservationState? ScriptedObservationState { get; init; }
}

public sealed record AppleVirtualizationEngineStatusResponse
{
    public required string HostId { get; init; }
    public string? EngineId { get; init; }
    public AppleVirtualizationEngineObservationState ObservationState { get; init; } = AppleVirtualizationEngineObservationState.Unknown;
    public EngineControlPlaneKind Kind { get; init; } = EngineControlPlaneKind.DockerCompatible;
    public EngineApiKind Api { get; init; } = EngineApiKind.DockerCompatible;
    public EngineAuthorityMode AuthorityMode { get; init; } = EngineAuthorityMode.Rootless;
    public EngineImageStoreMode ImageStore { get; init; } = EngineImageStoreMode.ProviderManaged;
    public EngineWorkloadAdoptionMode WorkloadAdoption { get; init; } = EngineWorkloadAdoptionMode.None;
    public EngineControlPlanePhase EnginePhase { get; init; } = EngineControlPlanePhase.Pending;
    public ResourcePhase Phase { get; init; } = ResourcePhase.Pending;
    public bool Installed { get; init; }
    public bool Running { get; init; }
    public bool Ready { get; init; }
    public bool GuestAgentReady { get; init; }
    public string? Version { get; init; }
    public string? Status { get; init; }
    public AppleVirtualizationGuestAgentEngineStatus? GuestEngineStatus { get; init; }
    public IReadOnlyList<AppleVirtualizationGuestAgentEngineApiEndpoint> Endpoints { get; init; } = Array.Empty<AppleVirtualizationGuestAgentEngineApiEndpoint>();
    public IReadOnlyList<AppleVirtualizationGuestAgentContainerObservation> Containers { get; init; } =
        Array.Empty<AppleVirtualizationGuestAgentContainerObservation>();
    public bool EndpointsTruncated { get; init; }
    public bool ContainersTruncated { get; init; }
    public bool DiagnosticsTruncated { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();

    public static AppleVirtualizationEngineStatusResponse FromGuestStatus(
        AppleVirtualizationEngineStatusRequest request,
        AppleVirtualizationGuestAgentEngineStatus guestStatus) =>
        new()
        {
            HostId = request.HostId,
            EngineId = request.EngineId ?? guestStatus.EngineId,
            ObservationState = guestStatus.ObservationState,
            Kind = guestStatus.Kind,
            Api = guestStatus.Api,
            AuthorityMode = guestStatus.AuthorityMode,
            ImageStore = guestStatus.ImageStore,
            WorkloadAdoption = guestStatus.WorkloadAdoption,
            EnginePhase = guestStatus.EnginePhase,
            Phase = guestStatus.Phase,
            Installed = guestStatus.Installed,
            Running = guestStatus.Running,
            Ready = guestStatus.Ready,
            GuestAgentReady = request.IncludeGuestObservation,
            Version = guestStatus.Version,
            Status = guestStatus.Status,
            GuestEngineStatus = guestStatus,
            Endpoints = guestStatus.Endpoints,
            Containers = guestStatus.Containers,
            EndpointsTruncated = guestStatus.EndpointsTruncated,
            ContainersTruncated = guestStatus.ContainersTruncated,
            DiagnosticsTruncated = guestStatus.DiagnosticsTruncated,
            Conditions = guestStatus.Conditions,
            Diagnostics = guestStatus.Diagnostics,
        };
}

public sealed record AppleVirtualizationProjectionConfigureRequest
{
    public required string ProjectionId { get; init; }
    public required string HostPath { get; init; }
    public required string Tag { get; init; }
    public AccessMode AccessMode { get; init; } = AccessMode.ReadOnly;
    public ProjectionRealizationKind Realization { get; init; } = ProjectionRealizationKind.LiveProjection;
}

public sealed record AppleVirtualizationProjectionMountRequest
{
    public required string ProjectionId { get; init; }
    public required string HostId { get; init; }
    public required string HostPath { get; init; }
    public required string Tag { get; init; }
    public required string GuestPath { get; init; }
    public AccessMode AccessMode { get; init; } = AccessMode.ReadOnly;
    public ProjectionRealizationKind Realization { get; init; } = ProjectionRealizationKind.ProviderDefault;
    public ProjectionWriteEffect RequestedWriteEffect { get; init; } = ProjectionWriteEffect.Unknown;
    public CoherenceClass RequestedCoherence { get; init; } = CoherenceClass.Unknown;
    public AppleVirtualizationGuestAgentReadinessState? ScriptedReadinessState { get; init; }
    public AppleVirtualizationHelperProjectionScriptedGuestState? ScriptedGuestProjectionState { get; init; }
    public AppleVirtualizationGuestAgentProjectionGenerationStamp Generation { get; init; } = new();
}

public sealed record AppleVirtualizationProjectionLifecycleRequest
{
    public required string ProjectionId { get; init; }
    public bool FinalizeBeforeRelease { get; init; }
    public string? Reason { get; init; }
}

public enum AppleVirtualizationHelperProjectionScriptedGuestState
{
    NotReady,
    HostConfiguredOnly,
    FrameworkAcceptedOnly,
    Verified,
    AccessMismatch,
    CoherenceUnknown,
    CoherenceDegraded,
    MalformedResponse,
    NotFound,
}

public sealed record AppleVirtualizationProjectionStatusRequest
{
    public required string ProjectionId { get; init; }
    public required string HostId { get; init; }
    public required string ExpectedGuestPath { get; init; }
    public bool VerifyAccess { get; init; } = true;
    public AppleVirtualizationGuestAgentReadinessState? ScriptedReadinessState { get; init; }
    public AppleVirtualizationHelperProjectionScriptedGuestState? ScriptedGuestProjectionState { get; init; }
}

public sealed record AppleVirtualizationProjectionUnmountRequest
{
    public required string ProjectionId { get; init; }
    public required string HostId { get; init; }
    public required string GuestPath { get; init; }
    public bool Force { get; init; }
    public AppleVirtualizationGuestAgentProjectionGenerationStamp? Generation { get; init; }
    public AppleVirtualizationGuestAgentReadinessState? ScriptedReadinessState { get; init; }
    public AppleVirtualizationHelperProjectionScriptedGuestState? ScriptedGuestProjectionState { get; init; }
}

public sealed record AppleVirtualizationProjectionObserveRequest
{
    public required string ProjectionId { get; init; }
    public required string HostId { get; init; }
    public required string GuestPath { get; init; }
    public bool Recursive { get; init; } = true;
    public long? AfterSequence { get; init; }
    public int? Limit { get; init; }
    public AppleVirtualizationGuestAgentReadinessState? ScriptedReadinessState { get; init; }
    public AppleVirtualizationHelperProjectionScriptedGuestState? ScriptedGuestProjectionState { get; init; }
}

public sealed record AppleVirtualizationProjectionSyncRequest
{
    public required string ProjectionId { get; init; }
    public required string HostId { get; init; }
    public required string GuestPath { get; init; }
    public SyncMode Mode { get; init; } = SyncMode.Manual;
    public SyncDirection Direction { get; init; } = SyncDirection.TargetToSource;
    public ConflictPolicy ConflictPolicy { get; init; } = ConflictPolicy.RecordConflict;
    public bool DryRun { get; init; }
    public int MaxChanges { get; init; } = AppleVirtualizationGuestAgentProjectionResultBounds.DefaultMaxChanges;
    public int MaxConflicts { get; init; } = AppleVirtualizationGuestAgentProjectionResultBounds.DefaultMaxConflicts;
    public AppleVirtualizationGuestAgentProjectionGenerationStamp Generation { get; init; } = new();
    public AppleVirtualizationGuestAgentReadinessState? ScriptedReadinessState { get; init; }
    public AppleVirtualizationHelperProjectionScriptedGuestState? ScriptedGuestProjectionState { get; init; }
}

public sealed record AppleVirtualizationProjectionFinalizationRequest
{
    public required string ProjectionId { get; init; }
    public required string HostId { get; init; }
    public required string GuestPath { get; init; }
    public FinalizationKind Kind { get; init; } = FinalizationKind.ManifestAndChangedContent;
    public bool IncludeProvenance { get; init; } = true;
    public bool IncludeDeletedEntries { get; init; } = true;
    public string? ProducerId { get; init; }
    public int MaxContentRefs { get; init; } = AppleVirtualizationGuestAgentProjectionResultBounds.DefaultMaxFinalizedContentRefs;
    public int MaxConflicts { get; init; } = AppleVirtualizationGuestAgentProjectionResultBounds.DefaultMaxConflicts;
    public AppleVirtualizationGuestAgentProjectionGenerationStamp Generation { get; init; } = new();
    public AppleVirtualizationGuestAgentReadinessState? ScriptedReadinessState { get; init; }
    public AppleVirtualizationHelperProjectionScriptedGuestState? ScriptedGuestProjectionState { get; init; }
}

public sealed record AppleVirtualizationProjectionChangeEnumerationRequest
{
    public required string ProjectionId { get; init; }
    public required string HostId { get; init; }
    public required string GuestPath { get; init; }
    public long? AfterSequence { get; init; }
    public int Limit { get; init; } = AppleVirtualizationGuestAgentProjectionResultBounds.DefaultMaxChanges;
    public bool IncludeDeletedEntries { get; init; } = true;
    public AppleVirtualizationGuestAgentProjectionGenerationStamp Generation { get; init; } = new();
    public AppleVirtualizationGuestAgentReadinessState? ScriptedReadinessState { get; init; }
    public AppleVirtualizationHelperProjectionScriptedGuestState? ScriptedGuestProjectionState { get; init; }
}

public sealed record AppleVirtualizationProjectionPromotionRequest
{
    public required string ProjectionId { get; init; }
    public required string HostId { get; init; }
    public required string GuestPath { get; init; }
    public SyncDirection Direction { get; init; } = SyncDirection.TargetToSource;
    public ConflictPolicy ConflictPolicy { get; init; } = ConflictPolicy.RequireExplicitPromotion;
    public bool DryRun { get; init; }
    public int MaxChanges { get; init; } = AppleVirtualizationGuestAgentProjectionResultBounds.DefaultMaxChanges;
    public int MaxConflicts { get; init; } = AppleVirtualizationGuestAgentProjectionResultBounds.DefaultMaxConflicts;
    public AppleVirtualizationGuestAgentProjectionGenerationStamp Generation { get; init; } = new();
    public AppleVirtualizationGuestAgentReadinessState? ScriptedReadinessState { get; init; }
    public AppleVirtualizationHelperProjectionScriptedGuestState? ScriptedGuestProjectionState { get; init; }
}

public sealed record AppleVirtualizationProjectionStatusResponse
{
    public required string ProjectionId { get; init; }
    public ContentProjectionPhase ProjectionPhase { get; init; }
    public ProjectionRealizationKind EffectiveRealization { get; init; }
    public ProjectionWriteEffect EffectiveWriteEffect { get; init; }
    public CoherenceClass EffectiveCoherence { get; init; }
    public bool GuestAgentReady { get; init; }
    public bool HostShareConfigured { get; init; }
    public bool FrameworkShareAccepted { get; init; }
    public bool VerifiedByGuestAgent { get; init; }
    public bool ReadyForHpdUse => VerifiedByGuestAgent &&
        GuestProjectionStatus?.ReadyForHpdUse == true &&
        ProjectionPhase == ContentProjectionPhase.Projected;
    public AppleVirtualizationGuestAgentProjectionStatus? GuestProjectionStatus { get; init; }
    public AppleVirtualizationGuestAgentProjectionUnmountResult? GuestProjectionUnmountResult { get; init; }
    public AppleVirtualizationGuestAgentProjectionObserveResult? GuestProjectionObserveResult { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
}

public sealed record AppleVirtualizationUnitEnsureRequest
{
    public required string UnitId { get; init; }
    public required string HostId { get; init; }
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string> Environment { get; init; } = EmptyStringDictionary.Value;
}

public sealed record AppleVirtualizationUnitLifecycleRequest
{
    public required string UnitId { get; init; }
    public StopKind? StopKind { get; init; }
    public string? Reason { get; init; }
}

public sealed record AppleVirtualizationUnitStatusResponse
{
    public required string UnitId { get; init; }
    public ExecutionUnitPhase UnitPhase { get; init; }
    public ProviderOpaqueHandle? ProviderHandle { get; init; }
    public string? WorkingDirectory { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
}

public sealed record AppleVirtualizationProcessStartRequest
{
    public required string ProcessId { get; init; }
    public required string UnitId { get; init; }
    public required ProcessCommandSpec Command { get; init; }
    public ProcessIdentitySpec? Identity { get; init; }
    public ProcessLimitSpec? Limits { get; init; }
    public ProcessIoSpec Io { get; init; } = ProcessIoSpec.Default;
    public ProcessInvocationPolicy Policy { get; init; } = ProcessInvocationPolicy.Default;
    public string? RequiredProjectionId { get; init; }
    public string? RequiredProjectionGuestPath { get; init; }
    public bool RequireVerifiedProjection { get; init; }
    public AppleVirtualizationGuestAgentReadinessState? ScriptedReadinessState { get; init; }
    public AppleVirtualizationHelperProjectionScriptedGuestState? ScriptedGuestProjectionState { get; init; }
}

public sealed record AppleVirtualizationProcessStdinRequest
{
    public required string ProcessId { get; init; }
    public ReadOnlyMemory<byte> Bytes { get; init; }
    public bool CloseAfterWrite { get; init; }
    public long Sequence { get; init; }
    public AppleVirtualizationGuestAgentReadinessState? ScriptedReadinessState { get; init; }
}

public sealed record AppleVirtualizationProcessSignalRequest(string ProcessId, ProcessSignal Signal, AppleVirtualizationGuestAgentReadinessState? ScriptedReadinessState = null);
public sealed record AppleVirtualizationProcessStopRequest(string ProcessId, StopKind Kind, TimeSpan? GracePeriod = null, string? Reason = null, AppleVirtualizationGuestAgentReadinessState? ScriptedReadinessState = null);
public sealed record AppleVirtualizationProcessResizeRequest
{
    public required string ProcessId { get; init; }
    public ProviderOpaqueHandle? ProcessHandle { get; init; }
    public required TerminalSpec Size { get; init; }
}

public sealed record AppleVirtualizationProcessLifecycleRequest
{
    public required string ProcessId { get; init; }
    public TimeSpan? Timeout { get; init; }
    public long? AfterOutputSequence { get; init; }
    public int? OutputLimit { get; init; }
    public AppleVirtualizationGuestAgentReadinessState? ScriptedReadinessState { get; init; }
    public AppleVirtualizationHelperProjectionScriptedGuestState? ScriptedGuestProjectionState { get; init; }
}

public sealed record AppleVirtualizationProcessStatusResponse
{
    public required string ProcessId { get; init; }
    public ProcessInvocationPhase ProcessPhase { get; init; }
    public ProcessIoState IoState { get; init; } = ProcessIoState.Unknown;
    public int? SystemProcessId { get; init; }
    public string? ProviderProcessId { get; init; }
    public ProcessInvocationResult? Result { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
}

public sealed record AppleVirtualizationProcessOutputEvent
{
    public required string ProcessId { get; init; }
    public required ProcessOutputStream Stream { get; init; }
    public required long Sequence { get; init; }
    public required DateTimeOffset ObservedAt { get; init; }
    public ReadOnlyMemory<byte> Bytes { get; init; }
    public ProcessOutputChunkFlags Flags { get; init; }
}

public enum AppleVirtualizationVmConfigurationValidationPhase
{
    Requested,
    StructuralInputValidation,
    SdkConfigurationBuild,
    SdkConfigurationValidate,
    SdkUnavailable,
    Completed,
}

public enum AppleVirtualizationVmConfigurationValidationState
{
    Unknown,
    Passed,
    Failed,
    Unsupported,
}

public sealed record AppleVirtualizationVmConfigurationValidationRequest
{
    public required string HostId { get; init; }
    public int CpuCount { get; init; }
    public long MemorySizeBytes { get; init; }
    public AppleVirtualizationGuestImageOptions GuestImage { get; init; } = new();
    public IReadOnlyList<AppleVirtualizationVmConfigurationSharedDirectory> SharedDirectories { get; init; } = Array.Empty<AppleVirtualizationVmConfigurationSharedDirectory>();
    public bool IncludeSerialConsole { get; init; } = true;
    public bool IncludeVirtioSocketPlaceholder { get; init; }
}

public sealed record AppleVirtualizationVmConfigurationSharedDirectory
{
    public required string Tag { get; init; }
    public required string HostPath { get; init; }
    public bool ReadOnly { get; init; } = true;
}

public sealed record AppleVirtualizationVmConfigurationValidationResponse
{
    public AppleVirtualizationVmConfigurationValidationPhase Phase { get; init; } = AppleVirtualizationVmConfigurationValidationPhase.Requested;
    public AppleVirtualizationVmConfigurationValidationState State { get; init; } = AppleVirtualizationVmConfigurationValidationState.Unknown;
    public bool Passed { get; init; }
    public bool HostRunning { get; init; }
    public bool HpdReady { get; init; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
    public IReadOnlyList<AppleVirtualizationPreflightFact> PreflightFacts { get; init; } = Array.Empty<AppleVirtualizationPreflightFact>();
}

public interface IAppleVirtualizationHelperClient
{
    ValueTask<AppleVirtualizationHelperEnvelope> SendAsync(AppleVirtualizationHelperEnvelope request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<AppleVirtualizationHelperEnvelope> ReadEventsAsync(CancellationToken cancellationToken = default);
}

public interface IAppleVirtualizationHelperTransport : IAsyncDisposable
{
    ValueTask SendAsync(AppleVirtualizationHelperEnvelope envelope, CancellationToken cancellationToken = default);
    IAsyncEnumerable<AppleVirtualizationHelperEnvelope> ReadAsync(CancellationToken cancellationToken = default);
}

public sealed class InMemoryAppleVirtualizationHelperTransport : IAppleVirtualizationHelperTransport
{
    private readonly ConcurrentQueue<AppleVirtualizationHelperEnvelope> _incoming = new();
    private readonly ConcurrentQueue<AppleVirtualizationHelperEnvelope> _sent = new();

    public IReadOnlyList<AppleVirtualizationHelperEnvelope> Sent => _sent.ToArray();

    public void EnqueueIncoming(AppleVirtualizationHelperEnvelope envelope) => _incoming.Enqueue(envelope);

    public ValueTask SendAsync(AppleVirtualizationHelperEnvelope envelope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _sent.Enqueue(envelope);
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<AppleVirtualizationHelperEnvelope> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        while (_incoming.TryDequeue(out AppleVirtualizationHelperEnvelope? envelope))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return envelope;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public static class AppleVirtualizationHelperOperationNames
{
    private static readonly IReadOnlyDictionary<AppleVirtualizationHelperOperation, string> Names =
        new Dictionary<AppleVirtualizationHelperOperation, string>
        {
            [AppleVirtualizationHelperOperation.Hello] = "hello",
            [AppleVirtualizationHelperOperation.CapabilitiesGet] = "capabilities.get",
            [AppleVirtualizationHelperOperation.PreflightRun] = "preflight.run",
            [AppleVirtualizationHelperOperation.ActivationStatus] = "activation.status",
            [AppleVirtualizationHelperOperation.HealthProbe] = "health.probe",
            [AppleVirtualizationHelperOperation.Shutdown] = "shutdown",
            [AppleVirtualizationHelperOperation.HostEnsure] = "host.ensure",
            [AppleVirtualizationHelperOperation.HostStart] = "host.start",
            [AppleVirtualizationHelperOperation.HostStatus] = "host.status",
            [AppleVirtualizationHelperOperation.HostRequestStop] = "host.requestStop",
            [AppleVirtualizationHelperOperation.HostStop] = "host.stop",
            [AppleVirtualizationHelperOperation.HostDelete] = "host.delete",
            [AppleVirtualizationHelperOperation.GuestControlWaitReady] = "guestControl.waitReady",
            [AppleVirtualizationHelperOperation.GuestControlStatus] = "guestControl.status",
            [AppleVirtualizationHelperOperation.ProjectionConfigure] = "projection.configure",
            [AppleVirtualizationHelperOperation.ProjectionMount] = "projection.mount",
            [AppleVirtualizationHelperOperation.ProjectionStatus] = "projection.status",
            [AppleVirtualizationHelperOperation.ProjectionRelease] = "projection.release",
            [AppleVirtualizationHelperOperation.UnitEnsure] = "unit.ensure",
            [AppleVirtualizationHelperOperation.UnitStatus] = "unit.status",
            [AppleVirtualizationHelperOperation.UnitStop] = "unit.stop",
            [AppleVirtualizationHelperOperation.UnitDelete] = "unit.delete",
            [AppleVirtualizationHelperOperation.ProcessStart] = "process.start",
            [AppleVirtualizationHelperOperation.ProcessStdin] = "process.stdin",
            [AppleVirtualizationHelperOperation.ProcessCloseStdin] = "process.closeStdin",
            [AppleVirtualizationHelperOperation.ProcessSignal] = "process.signal",
            [AppleVirtualizationHelperOperation.ProcessStop] = "process.stop",
            [AppleVirtualizationHelperOperation.ProcessWait] = "process.wait",
            [AppleVirtualizationHelperOperation.ProcessReadOutput] = "process.readOutput",
            [AppleVirtualizationHelperOperation.EndpointStatus] = "endpoint.status",
            [AppleVirtualizationHelperOperation.EndpointUnsupported] = "endpoint.unsupported",
            [AppleVirtualizationHelperOperation.ProcessResize] = "process.resize",
            [AppleVirtualizationHelperOperation.VmConfigurationValidate] = "vmConfiguration.validate",
            [AppleVirtualizationHelperOperation.GuestAgentTransportProbe] = "guestAgent.transportProbe",
            [AppleVirtualizationHelperOperation.GuestAgentReadinessProbe] = "guestAgent.readinessProbe",
            [AppleVirtualizationHelperOperation.ProjectionUnmount] = "projection.unmount",
            [AppleVirtualizationHelperOperation.ProjectionObserve] = "projection.observe",
            [AppleVirtualizationHelperOperation.ProjectionSync] = "projection.sync",
            [AppleVirtualizationHelperOperation.ProjectionFinalize] = "projection.finalize",
            [AppleVirtualizationHelperOperation.ProjectionEnumerateChanges] = "projection.enumerateChanges",
            [AppleVirtualizationHelperOperation.ProjectionPromote] = "projection.promote",
            [AppleVirtualizationHelperOperation.NetworkStatus] = "network.status",
            [AppleVirtualizationHelperOperation.EndpointPublish] = "endpoint.publish",
            [AppleVirtualizationHelperOperation.EndpointRelease] = "endpoint.release",
            [AppleVirtualizationHelperOperation.AuthorityBind] = "authority.bind",
            [AppleVirtualizationHelperOperation.AuthorityStatus] = "authority.status",
            [AppleVirtualizationHelperOperation.AuthorityRevoke] = "authority.revoke",
            [AppleVirtualizationHelperOperation.EngineStatus] = "engine.status",
            [AppleVirtualizationHelperOperation.EngineProvision] = "engine.provision",
        };

    public static string ToWireName(AppleVirtualizationHelperOperation operation) => Names[operation];
}

public static class AppleVirtualizationHelperJsonCodec
{
    public static byte[] Encode(AppleVirtualizationHelperEnvelope envelope) =>
        JsonSerializer.SerializeToUtf8Bytes(envelope, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);

    public static AppleVirtualizationHelperEnvelope Decode(ReadOnlySpan<byte> utf8Json) =>
        JsonSerializer.Deserialize(utf8Json, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)
        ?? throw new JsonException("Decoded Apple Virtualization helper envelope was null.");

    public static AppleVirtualizationHelperError ProtocolMismatch(string operation, string requestedVersion, string helperVersion) =>
        new()
        {
            Code = "AppleVirtualization.HelperProtocolMismatch",
            Message = $"Helper protocol '{helperVersion}' is not compatible with requested protocol '{requestedVersion}'.",
            Operation = operation,
            Retryable = false,
            FailedPhase = "Activation",
            Severity = DiagnosticSeverity.Error,
        };
}

public sealed class FakeAppleVirtualizationHelperClient : IAppleVirtualizationHelperClient
{
    private readonly ConcurrentQueue<AppleVirtualizationHelperEnvelope> _responses = new();
    private readonly ConcurrentQueue<AppleVirtualizationHelperEnvelope> _events = new();
    private long _sequence;

    public IReadOnlyList<AppleVirtualizationHelperEnvelope> Requests => _requests;
    private readonly List<AppleVirtualizationHelperEnvelope> _requests = [];

    public void EnqueueResponse(AppleVirtualizationHelperEnvelope response) => _responses.Enqueue(response);
    public void EnqueueEvent(AppleVirtualizationHelperEnvelope helperEvent) => _events.Enqueue(helperEvent);

    public ValueTask<AppleVirtualizationHelperEnvelope> SendAsync(AppleVirtualizationHelperEnvelope request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _requests.Add(request);
        if (_responses.TryDequeue(out AppleVirtualizationHelperEnvelope? response))
        {
            return ValueTask.FromResult(response);
        }

        if (request.Operation is AppleVirtualizationHelperOperation.AuthorityBind or
            AppleVirtualizationHelperOperation.AuthorityStatus or
            AppleVirtualizationHelperOperation.AuthorityRevoke)
        {
            return ValueTask.FromResult(HandleAuthority(request));
        }

        if (request.Operation == AppleVirtualizationHelperOperation.EngineStatus)
        {
            return ValueTask.FromResult(HandleEngineStatus(request));
        }

        if (request.Operation == AppleVirtualizationHelperOperation.EngineProvision)
        {
            return ValueTask.FromResult(HandleEngineProvision(request));
        }

        return ValueTask.FromResult(request.ToResponse(Interlocked.Increment(ref _sequence)));
    }

    public async IAsyncEnumerable<AppleVirtualizationHelperEnvelope> ReadEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        while (_events.TryDequeue(out AppleVirtualizationHelperEnvelope? helperEvent))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return helperEvent;
        }
    }

    private AppleVirtualizationHelperEnvelope HandleAuthority(AppleVirtualizationHelperEnvelope request)
    {
        AppleVirtualizationAuthorityBindingRequest? authority = request.AuthorityBindingRequest;
        if (authority is null)
        {
            return request.ToErrorResponse(
                Interlocked.Increment(ref _sequence),
                new AppleVirtualizationHelperError
                {
                    Code = "AppleVirtualization.AuthorityMissingPayload",
                    Message = "Authority helper request did not include the required authority binding payload.",
                    Operation = AppleVirtualizationHelperOperationNames.ToWireName(request.Operation),
                    FailedPhase = "Decode",
                    Retryable = false,
                });
        }

        DateTimeOffset boundAt = authority.Lease.BoundAt == default ? DateTimeOffset.UtcNow : authority.Lease.BoundAt;
        RevocationVerificationStatus revocationStatus = request.Operation == AppleVirtualizationHelperOperation.AuthorityRevoke
            ? RevocationVerificationStatus.Verified
            : RevocationVerificationStatus.Pending;
        AuthorityBindingPhase phase = request.Operation == AppleVirtualizationHelperOperation.AuthorityRevoke
            ? AuthorityBindingPhase.Revoked
            : AuthorityBindingPhase.Projected;
        AuthorityAuditKind auditKind = request.Operation == AppleVirtualizationHelperOperation.AuthorityRevoke
            ? AuthorityAuditKind.Revoked
            : AuthorityAuditKind.Projected;

        var audit = new AuthorityAuditEvent
        {
            Kind = auditKind,
            SourceKind = authority.Source.Kind,
            TargetKind = authority.Target.Kind,
            Timestamp = boundAt,
            CorrelationId = authority.AuditCorrelationId ?? "authority-" + authority.BindingId,
        };

        var boundAuthority = new AppleVirtualizationGuestAgentBoundAuthority
        {
            BindingId = authority.BindingId,
            SourceKind = authority.Source.Kind,
            ProjectionKind = authority.Projection.Kind,
            Direction = authority.Direction,
            EffectiveAuthorityClass = authority.EffectiveAuthorityClass,
            Redaction = authority.Redaction,
            TargetSocketPath = authority.Projection.TargetSocketPath,
            EnvironmentVariableName = authority.Projection.EnvironmentVariableName,
            BoundAt = boundAt,
            ExpiresAt = authority.Lease.ExpiresAt,
            RotationGeneration = 0,
            RevocationStatus = revocationStatus,
            AuditCorrelationId = audit.CorrelationId,
            SensitiveEndpointKind = authority.Source.SensitiveEndpointKind,
            Generation = authority.Generation,
        };

        return request.ToResponse(Interlocked.Increment(ref _sequence)) with
        {
            PayloadSchema = AppleVirtualizationHelperProtocol.AuthorityBindingResponseSchema,
            EventKind = request.Operation == AppleVirtualizationHelperOperation.AuthorityRevoke
                ? AppleVirtualizationHelperEventKind.AuthorityRevoked
                : AppleVirtualizationHelperEventKind.AuthorityBound,
            AuthorityBindingResponse = new AppleVirtualizationAuthorityBindingResponse
            {
                BindingId = authority.BindingId,
                BindingPhase = phase,
                BoundAuthority = boundAuthority,
                RevocationStatus = revocationStatus,
                RevocationEvidence = request.Operation == AppleVirtualizationHelperOperation.AuthorityRevoke
                    ?
                    [
                        new AppleVirtualizationAuthorityRevocationEvidence
                        {
                            Kind = AppleVirtualizationAuthorityRevocationEvidenceKind.ListenerRemoved,
                            Observed = true,
                            GuestSocketPath = authority.Projection.TargetSocketPath,
                            Detail = "fake-helper observed listener removal for the projected authority socket.",
                            ObservedAt = boundAt,
                        },
                    ]
                    : Array.Empty<AppleVirtualizationAuthorityRevocationEvidence>(),
                AuditEvents = [audit],
                Conditions =
                [
                    new Condition(
                        "AppleVirtualization.AuthorityBindingHelperAccepted",
                        ConditionStatus.True,
                        phase.ToString(),
                        "Authority binding helper route accepted the model request in the fake helper.",
                        boundAt,
                        default),
                ],
            },
        };
    }

    private AppleVirtualizationHelperEnvelope HandleEngineStatus(AppleVirtualizationHelperEnvelope request)
    {
        AppleVirtualizationEngineStatusRequest? engine = request.EngineStatusRequest;
        if (engine is null)
        {
            return request.ToErrorResponse(
                Interlocked.Increment(ref _sequence),
                new AppleVirtualizationHelperError
                {
                    Code = "AppleVirtualization.EngineMissingPayload",
                    Message = "Engine helper request did not include the required engine status payload.",
                    Operation = AppleVirtualizationHelperOperationNames.ToWireName(request.Operation),
                    FailedPhase = "Decode",
                    Retryable = false,
                });
        }

        AppleVirtualizationGuestAgentEngineStatus guestStatus =
            AppleVirtualizationGuestAgentEngineStatus.FromRequest(new AppleVirtualizationGuestAgentEngineStatusRequest
            {
                HostId = engine.HostId,
                EngineId = engine.EngineId,
                Kind = engine.Kind,
                Api = engine.Api,
                AuthorityMode = engine.AuthorityMode,
                ImageStore = engine.ImageStore,
                WorkloadAdoption = engine.WorkloadAdoption,
                ObservationLocus = engine.ObservationLocus,
                ObservedSocketPath = engine.ObservedSocketPath,
                ObservedVersion = engine.ObservedVersion,
                ObservedStatus = engine.ObservedStatus,
                ObservedContainers = engine.ObservedContainers,
                ObservedDiagnostics = engine.ObservedDiagnostics,
                MaxEndpoints = engine.MaxEndpoints,
                MaxContainers = engine.MaxContainers,
                MaxDiagnostics = engine.MaxDiagnostics,
                MaxVersionLength = engine.MaxVersionLength,
                MaxStatusLength = engine.MaxStatusLength,
                IncludeContainers = true,
                ScriptedObservationState = engine.ScriptedObservationState,
            });

        return request.ToResponse(Interlocked.Increment(ref _sequence)) with
        {
            PayloadSchema = AppleVirtualizationHelperProtocol.EngineStatusResponseSchema,
            EventKind = guestStatus.ObservationState == AppleVirtualizationEngineObservationState.Degraded
                ? AppleVirtualizationHelperEventKind.EngineDegraded
                : AppleVirtualizationHelperEventKind.EngineObserved,
            EngineStatusResponse = AppleVirtualizationEngineStatusResponse.FromGuestStatus(engine, guestStatus),
        };
    }

    private AppleVirtualizationHelperEnvelope HandleEngineProvision(AppleVirtualizationHelperEnvelope request)
    {
        AppleVirtualizationEngineProvisioningRequest? engine = request.EngineProvisioningRequest;
        if (engine is null)
        {
            return request.ToErrorResponse(
                Interlocked.Increment(ref _sequence),
                new AppleVirtualizationHelperError
                {
                    Code = "AppleVirtualization.EngineProvisioningMissingPayload",
                    Message = "Engine helper request did not include the required engine provisioning payload.",
                    Operation = AppleVirtualizationHelperOperationNames.ToWireName(request.Operation),
                    FailedPhase = "Decode",
                    Retryable = false,
                });
        }

        AppleVirtualizationGuestAgentEngineProvisioningResult guestProvisioning =
            AppleVirtualizationEngineProvisioningPlanner.PlanGuest(AppleVirtualizationEngineProvisioningPlanner.ToGuestRequest(engine));
        AppleVirtualizationEngineProvisioningResponse provisioning =
            AppleVirtualizationEngineProvisioningPlanner.FromGuestResult(guestProvisioning);
        return request.ToResponse(Interlocked.Increment(ref _sequence)) with
        {
            PayloadSchema = AppleVirtualizationHelperProtocol.EngineProvisionResponseSchema,
            EventKind = provisioning.Phase == AppleVirtualizationEngineProvisioningPhase.Degraded
                ? AppleVirtualizationHelperEventKind.EngineDegraded
                : AppleVirtualizationHelperEventKind.EngineProvisioned,
            EngineProvisioningResponse = provisioning,
        };
    }
}

internal static class AppleVirtualizationEngineProvisioningPlanner
{
    public static AppleVirtualizationGuestAgentEngineProvisioningRequest ToGuestRequest(
        AppleVirtualizationEngineProvisioningRequest request) =>
        new()
        {
            HostId = request.HostId,
            EngineId = request.EngineId,
            Kind = request.Kind,
            Api = request.Api,
            AuthorityMode = request.AuthorityMode,
            ImageStore = request.ImageStore,
            WorkloadAdoption = request.WorkloadAdoption,
            AllowPackageInstall = request.AllowPackageInstall,
            AllowServiceEnablement = request.AllowServiceEnablement,
            ProvisioningTimeoutMilliseconds = request.ProvisioningTimeoutMilliseconds,
            MaxCapturedOutputBytes = request.MaxCapturedOutputBytes,
            PackageManager = request.PackageManager,
            ScriptedExecutionState = request.ScriptedExecutionState,
            ScriptedPrerequisites = request.ScriptedPrerequisites,
            ScriptedOutput = request.ScriptedOutput,
            ScriptedStdout = request.ScriptedStdout,
            ScriptedStderr = request.ScriptedStderr,
        };

    public static AppleVirtualizationEngineProvisioningResponse FromGuestResult(
        AppleVirtualizationGuestAgentEngineProvisioningResult result) =>
        new()
        {
            HostId = result.HostId,
            EngineId = result.EngineId,
            Phase = result.Phase,
            Kind = result.Kind,
            Api = result.Api,
            AuthorityMode = result.AuthorityMode,
            ImageStore = result.ImageStore,
            ExistingEngineObserved = result.ExistingEngineObserved,
            InstallAttempted = result.InstallAttempted,
            GuestSocketPath = result.GuestSocketPath,
            Prerequisites = result.Prerequisites,
            Plan = result.Plan,
            Output = result.Output,
            Evidence = result.Evidence,
            Conditions = result.Conditions,
            Diagnostics = result.Diagnostics,
        };

    public static AppleVirtualizationEngineProvisioningResponse Plan(
        AppleVirtualizationEngineProvisioningRequest request)
    {
        AppleVirtualizationEngineProvisioningPrerequisiteStatus prerequisites = request.ScriptedPrerequisites;
        Diagnostic[] diagnostics = DiagnosticsFor(request, prerequisites);
        bool existing = ContainsExistingEngineMarker(request.ScriptedOutput) ||
            ContainsExistingEngineMarker(request.ScriptedStdout) ||
            ContainsExistingEngineMarker(request.ScriptedStderr);
        if (!existing)
        {
            diagnostics = diagnostics.Concat(GateDiagnosticsFor(request)).ToArray();
        }

        bool degraded = diagnostics.Length > 0;
        string socketPath = SocketPathFor(request.Api, request.AuthorityMode);
        AppleVirtualizationEngineProvisioningOutputCapture output = Capture(
            request.ScriptedOutput ?? request.ScriptedStdout ?? request.ScriptedStderr,
            request.ScriptedStdout ?? request.ScriptedOutput,
            request.ScriptedStderr,
            request.MaxCapturedOutputBytes);
        bool executionFailed = !degraded && !existing &&
            request.ScriptedExecutionState is AppleVirtualizationEngineProvisioningExecutionState.TimedOut
                or AppleVirtualizationEngineProvisioningExecutionState.Failed;
        bool executionSucceeded = !degraded && !existing &&
            request.ScriptedExecutionState == AppleVirtualizationEngineProvisioningExecutionState.Succeeded;
        if (executionFailed)
        {
            diagnostics = diagnostics.Concat([ExecutionDiagnostic(request)]).ToArray();
        }

        return new AppleVirtualizationEngineProvisioningResponse
        {
            HostId = request.HostId,
            EngineId = request.EngineId,
            Phase = degraded
                ? AppleVirtualizationEngineProvisioningPhase.Degraded
                : executionFailed ? AppleVirtualizationEngineProvisioningPhase.Failed
                : existing || executionSucceeded ? AppleVirtualizationEngineProvisioningPhase.Ready
                : AppleVirtualizationEngineProvisioningPhase.Planned,
            Kind = request.Kind,
            Api = request.Api,
            AuthorityMode = request.AuthorityMode,
            ImageStore = request.ImageStore,
            ExistingEngineObserved = existing,
            InstallAttempted = executionSucceeded || executionFailed,
            GuestSocketPath = socketPath,
            Prerequisites = prerequisites,
            Plan = degraded ? [Step("validate-prerequisites", AppleVirtualizationEngineProvisioningAction.ValidatePrerequisites, "Provisioning is blocked until guest prerequisites are available.")]
                : existing ? [Step("observe-existing-engine", AppleVirtualizationEngineProvisioningAction.ObserveExisting, "Existing in-guest engine was observed; no package installation is planned.")]
                : executionSucceeded ? [Step("observe-provisioned-engine", AppleVirtualizationEngineProvisioningAction.StartService, "Guest-agent provisioning completed and readiness can be observed on the next status probe.")]
                : executionFailed ? [Step("execute-provisioning", AppleVirtualizationEngineProvisioningAction.InstallPackages, "Guest-agent provisioning execution did not complete successfully.")]
                :
                [
                    Step("validate-prerequisites", AppleVirtualizationEngineProvisioningAction.ValidatePrerequisites, "Verify package manager, init system, network, and writable guest storage."),
                    Step("install-engine-packages", AppleVirtualizationEngineProvisioningAction.InstallPackages, "Install Docker/containerd-compatible engine packages inside the guest when explicitly allowed."),
                    Step("configure-engine-service", AppleVirtualizationEngineProvisioningAction.ConfigureService, "Configure the guest-local engine service and socket path."),
                    Step("enable-engine-service", AppleVirtualizationEngineProvisioningAction.EnableService, "Enable the appropriate system or user service."),
                    Step("start-engine-service", AppleVirtualizationEngineProvisioningAction.StartService, "Start the engine service and observe readiness."),
                ],
            Output = output,
            Evidence = EvidenceFor(request, prerequisites, output, existing, executionSucceeded || executionFailed),
            Conditions =
            [
                new Condition(
                    "AppleVirtualization.EngineProvisioning",
                    degraded || executionFailed ? ConditionStatus.False : ConditionStatus.True,
                    degraded ? "PrerequisitesMissing" :
                    executionFailed ? request.ScriptedExecutionState.ToString() :
                    existing ? "ExistingEngineObserved" :
                    executionSucceeded ? "ProvisioningExecuted" : "ProvisioningPlanned",
                    degraded
                        ? "Engine provisioning is blocked by missing guest prerequisites."
                        : executionFailed
                            ? "Engine provisioning execution failed or timed out inside the guest-agent path."
                        : existing
                            ? "Existing in-guest engine was observed; no install attempt was made."
                            : executionSucceeded
                                ? "Engine provisioning was executed inside the guest-agent path."
                                : "Engine provisioning plan was accepted for explicit in-guest bootstrap.",
                    DateTimeOffset.UtcNow,
                    default,
                    degraded ? DiagnosticSeverity.Warning : executionFailed ? DiagnosticSeverity.Error : DiagnosticSeverity.Info),
            ],
            Diagnostics = diagnostics,
        };
    }

    public static AppleVirtualizationGuestAgentEngineProvisioningResult PlanGuest(
        AppleVirtualizationGuestAgentEngineProvisioningRequest request)
    {
        AppleVirtualizationEngineProvisioningRequest helperRequest = new()
        {
            HostId = request.HostId,
            EngineId = request.EngineId,
            Kind = request.Kind,
            Api = request.Api,
            AuthorityMode = request.AuthorityMode,
            ImageStore = request.ImageStore,
            WorkloadAdoption = request.WorkloadAdoption,
            ExplicitRealMode = true,
            AllowPackageInstall = request.AllowPackageInstall,
            AllowServiceEnablement = request.AllowServiceEnablement,
            ProvisioningTimeoutMilliseconds = request.ProvisioningTimeoutMilliseconds,
            MaxCapturedOutputBytes = request.MaxCapturedOutputBytes,
            PackageManager = request.PackageManager,
            ScriptedExecutionState = request.ScriptedExecutionState,
            ScriptedPrerequisites = request.ScriptedPrerequisites,
            ScriptedOutput = request.ScriptedOutput,
            ScriptedStdout = request.ScriptedStdout,
            ScriptedStderr = request.ScriptedStderr,
        };
        AppleVirtualizationEngineProvisioningResponse response = Plan(helperRequest);
        return new AppleVirtualizationGuestAgentEngineProvisioningResult
        {
            HostId = response.HostId,
            EngineId = response.EngineId,
            Phase = response.Phase,
            Kind = response.Kind,
            Api = response.Api,
            AuthorityMode = response.AuthorityMode,
            ImageStore = response.ImageStore,
            ExistingEngineObserved = response.ExistingEngineObserved,
            InstallAttempted = response.InstallAttempted,
            GuestSocketPath = response.GuestSocketPath,
            Prerequisites = response.Prerequisites,
            Plan = response.Plan,
            Output = response.Output,
            Evidence = response.Evidence,
            Conditions = response.Conditions,
            Diagnostics = response.Diagnostics,
        };
    }

    private static AppleVirtualizationEngineProvisioningPlanStep Step(
        string name,
        AppleVirtualizationEngineProvisioningAction action,
        string detail) =>
        new()
        {
            Name = name,
            Action = action,
            Detail = detail,
        };

    private static Diagnostic[] DiagnosticsFor(
        AppleVirtualizationEngineProvisioningRequest request,
        AppleVirtualizationEngineProvisioningPrerequisiteStatus prerequisites)
    {
        var diagnostics = new List<Diagnostic>();
        AddIfMissing(diagnostics, request.AuthorityMode is EngineAuthorityMode.Rootless or EngineAuthorityMode.Rootful, "AppleVirtualization.EngineProvisioning.AuthorityModeUnsupported", "Engine provisioning supports explicit rootless or rootful authority mode only.", "engine.provisioning.authorityMode");
        AddIfMissing(diagnostics, prerequisites.PackageManagerAvailable, "AppleVirtualization.EngineProvisioning.PackageManagerMissing", "Engine provisioning requires a supported in-guest package manager.", "engine.provisioning.packageManager");
        AddIfMissing(diagnostics, prerequisites.SystemdAvailable, "AppleVirtualization.EngineProvisioning.SystemdMissing", "Rootful engine provisioning requires systemd inside the guest.", "engine.provisioning.systemd");
        AddIfMissing(diagnostics, request.AuthorityMode != EngineAuthorityMode.Rootless || prerequisites.UserSystemdAvailable, "AppleVirtualization.EngineProvisioning.UserSystemdMissing", "Rootless engine provisioning requires user systemd inside the guest.", "engine.provisioning.userSystemd");
        AddIfMissing(diagnostics, prerequisites.GuestAgentAvailable, "AppleVirtualization.EngineProvisioning.GuestAgentMissing", "Engine provisioning requires the HPD guest agent.", "engine.provisioning.guestAgent");
        AddIfMissing(diagnostics, request.AuthorityMode != EngineAuthorityMode.Rootless || prerequisites.RootlessSupported, "AppleVirtualization.EngineProvisioning.RootlessUnsupported", "The guest does not support rootless engine provisioning.", "engine.provisioning.authorityMode");
        AddIfMissing(diagnostics, request.AuthorityMode != EngineAuthorityMode.Rootful || prerequisites.RootfulSupported, "AppleVirtualization.EngineProvisioning.RootfulUnsupported", "The guest does not support rootful engine provisioning.", "engine.provisioning.authorityMode");
        AddIfMissing(diagnostics, prerequisites.ImageStoreSupported, "AppleVirtualization.EngineProvisioning.ImageStoreUnsupported", "The requested engine image-store mode is not supported by provisioning.", "engine.provisioning.imageStore");
        AddIfMissing(diagnostics, prerequisites.NetworkAvailable, "AppleVirtualization.EngineProvisioning.NetworkMissing", "Engine provisioning requires guest network egress for package retrieval.", "engine.provisioning.network");
        AddIfMissing(diagnostics, prerequisites.WritableGuestStorageAvailable, "AppleVirtualization.EngineProvisioning.WritableStorageMissing", "Engine provisioning requires writable guest storage.", "engine.provisioning.storage");
        AddIfMissing(diagnostics, prerequisites.GuestOsSupported, "AppleVirtualization.EngineProvisioning.GuestOsUnsupported", "The guest OS is not a supported Linux target for engine provisioning.", "engine.provisioning.guestOs");
        return diagnostics.ToArray();
    }

    private static bool ContainsExistingEngineMarker(string? value) =>
        value?.Contains("existing-engine", StringComparison.OrdinalIgnoreCase) == true;

    private static Diagnostic[] GateDiagnosticsFor(AppleVirtualizationEngineProvisioningRequest request)
    {
        var diagnostics = new List<Diagnostic>(2);
        AddIfMissing(
            diagnostics,
            request.AllowPackageInstall,
            "AppleVirtualization.EngineProvisioning.PackageInstallDisabled",
            "Engine provisioning package installation is disabled; set the explicit package-install gate before the guest agent can install Docker or containerd packages.",
            "engine.provisioning.allowPackageInstall");
        AddIfMissing(
            diagnostics,
            request.AllowServiceEnablement,
            "AppleVirtualization.EngineProvisioning.ServiceEnablementDisabled",
            "Engine provisioning service enablement is disabled; set the explicit service gate before the guest agent can enable or start Docker or containerd services.",
            "engine.provisioning.allowServiceEnablement");
        return diagnostics.ToArray();
    }

    private static Diagnostic ExecutionDiagnostic(AppleVirtualizationEngineProvisioningRequest request) =>
        request.ScriptedExecutionState == AppleVirtualizationEngineProvisioningExecutionState.TimedOut
            ? new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = new DiagnosticCode("AppleVirtualization.EngineProvisioning.Timeout"),
                Message = $"Engine provisioning timed out after {Math.Max(0, request.ProvisioningTimeoutMilliseconds)}ms inside the guest-agent execution path.",
                ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                TargetPath = "engine.provisioning.timeout",
            }
            : new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = new DiagnosticCode("AppleVirtualization.EngineProvisioning.ExecutionFailed"),
                Message = "Engine provisioning failed inside the guest-agent execution path.",
                ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                TargetPath = "engine.provisioning.execution",
            };

    private static void AddIfMissing(
        List<Diagnostic> diagnostics,
        bool available,
        string code,
        string message,
        string targetPath)
    {
        if (available)
        {
            return;
        }

        diagnostics.Add(new Diagnostic
        {
            Severity = DiagnosticSeverity.Warning,
            Code = new DiagnosticCode(code),
            Message = message,
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        });
    }

    private static AppleVirtualizationEngineProvisioningOutputCapture Capture(
        string? output,
        int maxBytes)
    {
        return Capture(output, stdout: output, stderr: null, maxBytes);
    }

    private static AppleVirtualizationEngineProvisioningOutputCapture Capture(
        string? output,
        string? stdout,
        string? stderr,
        int maxBytes)
    {
        int boundedMaxBytes = Math.Clamp(maxBytes, 0, AppleVirtualizationEngineProvisioningOptions.DefaultMaxCapturedOutputBytes);
        CapturedText combined = CaptureText(output, boundedMaxBytes);
        CapturedText stdoutCapture = CaptureText(stdout, boundedMaxBytes);
        CapturedText stderrCapture = CaptureText(stderr, boundedMaxBytes);
        return new AppleVirtualizationEngineProvisioningOutputCapture
        {
            MaxCapturedBytes = boundedMaxBytes,
            CapturedBytes = combined.CapturedBytes,
            Truncated = combined.Truncated,
            Text = combined.Text,
            StdoutCapturedBytes = stdoutCapture.CapturedBytes,
            StderrCapturedBytes = stderrCapture.CapturedBytes,
            StdoutTruncated = stdoutCapture.Truncated,
            StderrTruncated = stderrCapture.Truncated,
            StdoutText = stdoutCapture.Text,
            StderrText = stderrCapture.Text,
        };
    }

    private static AppleVirtualizationEngineProvisioningEvidence EvidenceFor(
        AppleVirtualizationEngineProvisioningRequest request,
        AppleVirtualizationEngineProvisioningPrerequisiteStatus prerequisites,
        AppleVirtualizationEngineProvisioningOutputCapture output,
        bool existingEngineObserved,
        bool installAttempted) =>
        new()
        {
            HelperMediated = true,
            GuestAgentMediated = true,
            HostShellInvoked = false,
            HostDockerInvoked = false,
            PackageManager = request.PackageManager,
            PackageManagerAvailable = prerequisites.PackageManagerAvailable,
            NetworkAvailable = prerequisites.NetworkAvailable,
            WritableGuestStorageAvailable = prerequisites.WritableGuestStorageAvailable,
            SystemdAvailable = prerequisites.SystemdAvailable,
            UserSystemdAvailable = prerequisites.UserSystemdAvailable,
            ExistingEngineObserved = existingEngineObserved,
            PackageInstallAllowed = request.AllowPackageInstall,
            ServiceEnablementAllowed = request.AllowServiceEnablement,
            InstallAttempted = installAttempted,
            TimeoutMilliseconds = Math.Max(0, request.ProvisioningTimeoutMilliseconds),
            TimedOut = request.ScriptedExecutionState == AppleVirtualizationEngineProvisioningExecutionState.TimedOut,
            MaxCapturedOutputBytes = output.MaxCapturedBytes,
            StdoutCapturedBytes = output.StdoutCapturedBytes,
            StderrCapturedBytes = output.StderrCapturedBytes,
            StdoutTruncated = output.StdoutTruncated,
            StderrTruncated = output.StderrTruncated,
        };

    private readonly record struct CapturedText(string? Text, int CapturedBytes, bool Truncated);

    private static CapturedText CaptureText(string? text, int maxBytes)
    {
        if (string.IsNullOrEmpty(text) || maxBytes == 0)
        {
            return default;
        }

        string captured = text.Length <= maxBytes ? text : text[..maxBytes];
        return new CapturedText(captured, captured.Length, text.Length > maxBytes);
    }

    private static string SocketPathFor(EngineApiKind api, EngineAuthorityMode authorityMode) =>
        api switch
        {
            EngineApiKind.ContainerdApi => "/run/containerd/containerd.sock",
            EngineApiKind.PodmanApi when authorityMode == EngineAuthorityMode.Rootful => "/run/podman/podman.sock",
            EngineApiKind.PodmanApi => "/run/user/1000/podman/podman.sock",
            EngineApiKind.BuildKitApi when authorityMode == EngineAuthorityMode.Rootless => "/run/user/1000/buildkit-default/buildkitd.sock",
            EngineApiKind.BuildKitApi => "/run/buildkit/buildkitd.sock",
            _ when authorityMode == EngineAuthorityMode.Rootful => "/var/run/docker.sock",
            _ => "/run/user/1000/docker.sock",
        };
}

internal static class EmptyStringDictionary
{
    public static IReadOnlyDictionary<string, string> Value { get; } = new Dictionary<string, string>(0, StringComparer.Ordinal);
}
