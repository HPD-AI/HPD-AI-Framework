#nullable enable

namespace HPD.Environment.Contracts;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization.Metadata;

// ---------------------------------------------------------------------------
// Markers and typed identity
// ---------------------------------------------------------------------------

public interface IExecutionResourceMarker { }
public interface IOperationTargetMarker { }

public sealed record RuntimeHost : IExecutionResourceMarker, IOperationTargetMarker { }
public sealed record ExecutionUnit : IExecutionResourceMarker, IOperationTargetMarker { }
public sealed record ProcessInvocation : IExecutionResourceMarker, IOperationTargetMarker { }
public sealed record FunctionSandbox : IExecutionResourceMarker, IOperationTargetMarker { }
public sealed record FunctionInvocation : IExecutionResourceMarker, IOperationTargetMarker { }
public sealed record FunctionSandboxSnapshot : IExecutionResourceMarker, IOperationTargetMarker { }

public sealed record ContentArtifact : IExecutionResourceMarker, IOperationTargetMarker { }
public sealed record RootFilesystemView : IExecutionResourceMarker, IOperationTargetMarker { }
public sealed record Workspace : IExecutionResourceMarker, IOperationTargetMarker { }
public sealed record ContentProjection : IExecutionResourceMarker, IOperationTargetMarker { }
public sealed record GuestMemoryMapping : IExecutionResourceMarker, IOperationTargetMarker { }
public sealed record BlockVolume : IExecutionResourceMarker, IOperationTargetMarker { }

public sealed record Network : IExecutionResourceMarker, IOperationTargetMarker { }
public sealed record NetworkMembership : IExecutionResourceMarker, IOperationTargetMarker { }
public sealed record ServiceDiscovery : IExecutionResourceMarker, IOperationTargetMarker { }
public sealed record PublishedEndpoint : IExecutionResourceMarker, IOperationTargetMarker { }

public sealed record AuthorityBinding : IExecutionResourceMarker, IOperationTargetMarker { }
public sealed record ProviderActivation : IExecutionResourceMarker, IOperationTargetMarker { }
public sealed record EngineControlPlane : IExecutionResourceMarker, IOperationTargetMarker { }

public sealed record NetworkEndpointTarget : IOperationTargetMarker { }
public sealed record ProcessOutputStreamTarget : IOperationTargetMarker { }
public sealed record FunctionTraceStreamTarget : IOperationTargetMarker { }

public readonly record struct ResourceId<TResource>(string Value)
    where TResource : IExecutionResourceMarker;

public readonly record struct ResourceKind(string Value);
public readonly record struct ResourceScope(string Value);
public readonly record struct ResourceGeneration(long Value);
public readonly record struct EngineIncarnationGeneration(long Value);
public readonly record struct SchemaVersion(string Value);
public readonly record struct ProviderId(string Value);
public readonly record struct SchemaId(string Value);
public readonly record struct ContentType(string Value);
public readonly record struct EventId(string Value);
public readonly record struct EventCursor(string Value);
public readonly record struct RuntimePlanId(string Value);
public readonly record struct RuntimePlanStepId(string Value);
public readonly record struct ProviderActivationId(string Value);
public readonly record struct CapabilityId(string Value);
public readonly record struct CapabilityCategory(string Value);
public readonly record struct PermissionId(string Value);
public readonly record struct DiagnosticCode(string Value);
public readonly record struct SemanticVersion(ushort Major, ushort Minor, ushort Patch, string? Label = null);
public readonly record struct RuntimeHostStartGeneration(long Value);
public readonly record struct GuestBootGeneration(string Value);
public readonly record struct FunctionSandboxGeneration(ulong Value);
public readonly record struct FunctionInvocationId(string Value);
public readonly record struct FunctionName(string Value);
public readonly record struct HostFunctionName(string Value);
public readonly record struct HostPath(string Value);
public readonly record struct Digest(string Algorithm, string Value);
public readonly record struct MediaType(string Value);
public readonly record struct GuestPath(string Value);
public readonly record struct ByteSize(long Value);
public readonly record struct ScopedContentName(string Value);
public readonly record struct ContentPageCursor(string? Value);
public readonly record struct CredentialRef(string Value);
public readonly record struct DnsName(string Value);
public readonly record struct ServiceName(string Value);
public readonly record struct ScopedName(string Value);
public readonly record struct UnixSocketPath(string Value);
public readonly record struct NetworkPort(ushort Value);
public readonly record struct PortRange(NetworkPort Start, ushort Count);
public readonly record struct MacAddressValue(ulong Value);
public readonly record struct NetworkEndpointHandle(string Value);
public readonly record struct UnixSocketPermissions(uint Mode);

public readonly record struct ResourceRef<TResource>(
    ResourceId<TResource> Id,
    ResourceScope Scope,
    ResourceGeneration? Generation = null)
    where TResource : IExecutionResourceMarker;

public readonly record struct UntypedResourceRef(
    ResourceKind Kind,
    string Id,
    ResourceScope Scope,
    ResourceGeneration? Generation = null);

// ---------------------------------------------------------------------------
// Resource foundation, diagnostics, observations
// ---------------------------------------------------------------------------

public interface IResource<TResource, TSpec, TStatus>
    where TResource : IExecutionResourceMarker
    where TSpec : notnull
    where TStatus : ResourceStatus
{
    ResourceMetadata<TResource> Metadata { get; }
    TSpec Spec { get; }
    TStatus Status { get; }
}

public sealed record ResourceSnapshot<TResource, TSpec, TStatus>(
    ResourceMetadata<TResource> Metadata,
    TSpec Spec,
    TStatus Status)
    : IResource<TResource, TSpec, TStatus>
    where TResource : IExecutionResourceMarker
    where TSpec : notnull
    where TStatus : ResourceStatus;

public readonly record struct ResourceSnapshotEnvelope(
    ResourceKind Kind,
    string Id,
    ResourceScope Scope,
    ResourceGeneration Generation,
    SchemaVersion SchemaVersion,
    ContentType ContentType,
    ReadOnlyMemory<byte> Payload);

public readonly record struct ExecutionResourceQuery(
    ResourceScope Scope,
    ResourceKind? Kind = null,
    string? Id = null,
    ResourceGeneration? AfterGeneration = null,
    EventCursor? After = null,
    int? Limit = null,
    bool Follow = false);

public sealed record ResourceMetadata<TResource>
    where TResource : IExecutionResourceMarker
{
    public required ResourceId<TResource> Id { get; init; }
    public required ResourceKind Kind { get; init; }
    public required ResourceScope Scope { get; init; }
    public string? Name { get; init; }
    public ResourceGeneration Generation { get; init; }
    public required SchemaVersion SchemaVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public IReadOnlyDictionary<string, string> Labels { get; init; } = Empty.StringDictionary;
    public IReadOnlyDictionary<string, string> Annotations { get; init; } = Empty.StringDictionary;
    public IReadOnlyList<UntypedResourceRef> OwnerRefs { get; init; } = Array.Empty<UntypedResourceRef>();
    public IReadOnlyList<string> Finalizers { get; init; } = Array.Empty<string>();
    public ResourceLifetime Lifetime { get; init; } = ResourceLifetime.Runtime;
}

public abstract record ResourceStatus
{
    public ResourcePhase Phase { get; init; } = ResourcePhase.Unknown;
    public ResourceReconciliationOutcome ReconciliationOutcome { get; init; } = ResourceReconciliationOutcome.Accepted;
    public ResourceGeneration ObservedGeneration { get; init; }
    public DateTimeOffset? LastTransitionAt { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
    public IReadOnlyList<ProviderExtensionData> Extensions { get; init; } = Array.Empty<ProviderExtensionData>();
}

public enum ResourcePhase { Unknown, Pending, Reconciling, Ready, Degraded, Failed, Deleting, Deleted }
public enum ResourceReconciliationOutcome { Accepted, ImmutableConflict, Rejected }
public enum ResourceLifetime { Operation, Invocation, Process, ExecutionUnit, Runtime, Project, ExplicitRetain, ProviderOwned, SharedRefCounted }
public enum ConditionStatus { Unknown, False, True }
public enum DiagnosticSeverity { Trace, Debug, Info, Warning, Error, Fatal }

public readonly record struct Condition(
    string Type,
    ConditionStatus Status,
    string Reason,
    string Message,
    DateTimeOffset LastTransitionAt,
    ResourceGeneration ObservedGeneration,
    DiagnosticSeverity Severity = DiagnosticSeverity.Info);

public sealed record Diagnostic
{
    public required DiagnosticSeverity Severity { get; init; }
    public required DiagnosticCode Code { get; init; }
    public required string Message { get; init; }
    public ProviderId? ProviderId { get; init; }
    public string? TargetPath { get; init; }
    public ProviderExtensionData? Detail { get; init; }
}

public readonly record struct ProviderExtensionData(
    ProviderId ProviderId,
    SchemaId SchemaId,
    ContentType ContentType,
    ReadOnlyMemory<byte> Payload);

public readonly record struct ProviderOpaqueHandle(
    ProviderId ProviderId,
    string Token,
    SchemaId? SchemaId = null,
    ulong Generation = 0);

public readonly record struct TargetHandle<TTarget>(
    TargetRoute Route,
    TargetHandleLifetime Lifetime,
    TargetHandleAuthority Authority,
    ulong ProviderGeneration = 0)
    where TTarget : IOperationTargetMarker;

public sealed record TargetRoute
{
    public required TargetKind Kind { get; init; }
    public required ResourceScope Scope { get; init; }
    public IReadOnlyList<TargetRouteSegment> Segments { get; init; } = Array.Empty<TargetRouteSegment>();
    public ResourceKind? BackingResourceKind { get; init; }
    public string? BackingResourceId { get; init; }
    public ProviderId? ProviderId { get; init; }
    public ProviderOpaqueHandle? ProviderHandle { get; init; }
}

public readonly record struct TargetKind(string Value);
public readonly record struct TargetRouteSegment(TargetRouteSegmentKind Kind, string Value);
public enum TargetRouteSegmentKind { RuntimeHost, ExecutionUnit, ProcessInvocation, FunctionSandbox, FunctionInvocation, Stream, Network, Endpoint, ContentProjection, ProviderActivation, ProviderOpaque }
public enum TargetHandleLifetime { DurableAddress, Lease, LiveCapability }

[Flags]
public enum TargetHandleAuthority { None = 0, Observe = 1, Control = 2, Read = 4, Write = 8, Invoke = 16, Admin = 32 }

public readonly record struct ObservationTarget(
    ObservationTargetKind TargetKind,
    ResourceKind? ResourceKind,
    string? ResourceId,
    ResourceScope Scope,
    TargetRoute? Route);

public enum ObservationTargetKind { Resource, TargetHandle }
public enum ObservationKind { Status, Snapshot, Event, Log, Metric, Diagnostic, Health, Usage, Output }

public sealed record EventEnvelope
{
    public required EventId Id { get; init; }
    public required EventCursor Cursor { get; init; }
    public required ObservationTarget Target { get; init; }
    public required ObservationKind Kind { get; init; }
    public required string Type { get; init; }
    public required SchemaVersion SchemaVersion { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public long SequenceNumber { get; init; }
    public ResourceGeneration? ResourceGeneration { get; init; }
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public ReadOnlyMemory<byte> Payload { get; init; }
    public ContentType ContentType { get; init; } = new("application/json");
}

public sealed record ObservationQuery
{
    public required ObservationTarget Target { get; init; }
    public ObservationKind? Kind { get; init; }
    public string? Type { get; init; }
    public EventCursor? After { get; init; }
    public int? Limit { get; init; }
    public bool Follow { get; init; }
}

public interface IObservationReader
{
    ValueTask<EventEnvelope?> ReadAsync(EventCursor cursor, CancellationToken cancellationToken = default);
    IAsyncEnumerable<EventEnvelope> WatchAsync(ObservationQuery query, CancellationToken cancellationToken = default);
}

public interface IExecutionResourceReader
{
    ValueTask<ResourceSnapshotEnvelope?> ReadAsync(ExecutionResourceQuery query, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ResourceSnapshotEnvelope> WatchAsync(ExecutionResourceQuery query, CancellationToken cancellationToken = default);
}

public readonly record struct ExecutionEventChunk(
    EventId StreamId,
    long Sequence,
    DateTimeOffset ObservedAt,
    ObservationKind Kind,
    ReadOnlyMemory<byte> Payload,
    SchemaId PayloadSchema,
    ContentType ContentType);

public interface IExecutionEventSink
{
    ValueTask OnEventAsync(ExecutionEventChunk chunk, CancellationToken cancellationToken = default);
}

// ---------------------------------------------------------------------------
// Policy model
// ---------------------------------------------------------------------------

public sealed record RuntimeTopologyPolicy
{
    public RuntimeTopologyMode Mode { get; init; } = RuntimeTopologyMode.OneHostPerRuntime;
    public bool AllowHostSharing { get; init; } = true;
    public bool RequireExecutionUnitIsolation { get; init; }
    public bool StopHostOnPrimaryExit { get; init; }
    public bool RetainEmptyHost { get; init; } = true;
    public FailureDomainPolicy FailureDomains { get; init; } = FailureDomainPolicy.RuntimeScoped;
}

public enum RuntimeTopologyMode { OneHostPerExecutionUnit, OneHostPerRuntime, PooledHosts, RemotePlacement, Hybrid }

public sealed record FailureDomainPolicy
{
    public static FailureDomainPolicy RuntimeScoped { get; } = new();
    public ProviderActivationScope HostProviderScope { get; init; } = ProviderActivationScope.Runtime;
    public ProviderActivationScope NetworkProviderScope { get; init; } = ProviderActivationScope.Runtime;
    public ProviderActivationScope StorageProviderScope { get; init; } = ProviderActivationScope.Runtime;
    public ProviderActivationScope CredentialProviderScope { get; init; } = ProviderActivationScope.Runtime;
    public ProviderActivationScope InvocationProviderScope { get; init; } = ProviderActivationScope.Runtime;
    public bool IsolateProviderFailuresPerExecutionUnit { get; init; }
}

public sealed record LifecyclePolicy
{
    public static LifecyclePolicy Default { get; } = new();
    public bool AutoStart { get; init; }
    public bool RestartPrimaryProcess { get; init; }
    public bool StopExecutionUnitOnPrimaryExit { get; init; } = true;
    public bool StopHostWhenEmpty { get; init; }
    public TimeSpan? IdleRetention { get; init; }
    public CleanupPolicy Cleanup { get; init; } = CleanupPolicy.Default;
}

public sealed record RuntimeHostLifecyclePolicy
{
    public static RuntimeHostLifecyclePolicy Default { get; } = new();
    public bool ProtectFromDelete { get; init; }
    public bool AllowReset { get; init; } = true;
    public RuntimeHostResetPolicy ResetPolicy { get; init; } = RuntimeHostResetPolicy.Default;
    public bool RequireReadinessBeforeExecutionUnits { get; init; } = true;
}

public sealed record RuntimeHostResetPolicy
{
    public static RuntimeHostResetPolicy Default { get; } = new();
    public RuntimeHostResetScope DefaultScope { get; init; } = RuntimeHostResetScope.ProviderState;
    public bool RetainResourceIdentity { get; init; } = true;
    public bool RetainUserDataByDefault { get; init; } = true;
}

public enum RuntimeHostResetScope { ProviderState, RuntimeState, BootstrapState, StorageState, FullReinitialize }

public sealed record CleanupPolicy
{
    public static CleanupPolicy Default { get; } = new();
    public CleanupFailureMode FailureMode { get; init; } = CleanupFailureMode.MarkDegradedAndRetain;
    public bool FinalizeBeforeRelease { get; init; } = true;
    public bool RevokeAuthorityBindingsFirst { get; init; } = true;
    public TimeSpan OverallTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(5);
}

public enum CleanupFailureMode { FailOperation, MarkDegradedAndRetain, BestEffortRelease }

public sealed record SecurityPolicy
{
    public static SecurityPolicy Default { get; } = new();
    public bool AllowHostSharing { get; init; } = true;
    public bool AllowHostNetwork { get; init; }
    public bool AllowAuthorityBindings { get; init; }
    public bool AllowHostPathProjection { get; init; }
    public bool AllowHostFunctionCallbacks { get; init; }
    public bool RequireAuthorityAudit { get; init; } = true;
    public IReadOnlyList<string> RequiredCapabilities { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ProviderExtensionData> Extensions { get; init; } = Array.Empty<ProviderExtensionData>();
}

public sealed record ResourceQuotaPolicy
{
    public static ResourceQuotaPolicy Default { get; } = new();
    public double? CpuCores { get; init; }
    public long? MemoryBytes { get; init; }
    public long? StorageBytes { get; init; }
    public int? ProcessCount { get; init; }
    public long? FunctionCallBytes { get; init; }
}

public enum GarbageCollectionClass { Retain, CacheWhileReferenced, Ephemeral }

// ---------------------------------------------------------------------------
// Provider planning, capabilities, activation, and preflight
// ---------------------------------------------------------------------------

[Flags]
public enum ProviderContractKind
{
    None = 0,
    RuntimeHost = 1 << 0,
    ExecutionUnit = 1 << 1,
    ProcessInvocation = 1 << 2,
    Artifact = 1 << 3,
    RootFilesystemView = 1 << 4,
    ContentProjection = 1 << 5,
    Network = 1 << 6,
    NetworkMembership = 1 << 7,
    ServiceDiscovery = 1 << 8,
    EndpointPublication = 1 << 9,
    Credential = 1 << 10,
    Permission = 1 << 11,
    Supervisor = 1 << 12,
    Transport = 1 << 13,
    Build = 1 << 14,
    BlockVolume = 1 << 15,
    EngineControlPlane = 1 << 16,
    FunctionSandbox = 1 << 17,
    FunctionInvocation = 1 << 18,
    HostFunctionBinding = 1 << 19,
    FunctionSnapshot = 1 << 20,
    GuestMemoryMapping = 1 << 21,
    ProcessIsolation = 1 << 22,
    AuthorityBinding = 1 << 23
}

public enum ProviderActivationScope { Singleton, HostUser, HostSystem, Project, Runtime, RuntimeHost, Network, ExecutionUnit, FunctionSandbox, Operation, Preflight }
public enum ProviderTrustLevel { BuiltIn, Signed, WorkspaceTrusted, UserInstalled, Remote, Untrusted }
public enum ProviderActivationKind { InProcess, BuiltIn, SupervisedExecutable, ExternalProcess, HostDaemon, RemoteEndpoint, ProviderDefined }
public enum ProviderDiscoveryKind { StaticModule, Manifest, ExecutablePath, WellKnownPath, Environment, RemoteRegistry, ProviderDefined }
public enum ProviderTransportKind { None, StdIo, UnixSocket, Tcp, NamedPipe, Ssh, Grpc, Vsock, HvSocket, SharedMemory, ProviderDefined }
public enum ProviderComponentKind { Supervisor, Driver, HostAgent, GuestAgent, NetworkDaemon, DnsListener, MountHelper, PermissionHelper, EndpointRouter, EngineDaemon, FunctionSandboxHelper, HelperPool, ProviderDefined }
public enum ProviderComponentPhase { Unknown, Starting, Ready, Degraded, Restarting, Stopping, Stopped, Failed }
public enum ProviderEndpointPurpose { Control, Data, Logs, Health, GuestControl, DriverControl, Dns, Network, Mount, EndpointRouter, EngineApi, FunctionDebug, ProviderDefined }
public enum EndpointSensitivity { Public, LocalOnly, Sensitive, SecretBearing, PrivilegedControl }
public enum CapabilityState { Supported, Unsupported, Degraded, RequiresPermission, RequiresConfiguration, DisabledByPolicy, TemporarilyUnavailable, Planned, Deferred }
public enum CapabilityRequirementStrength { Required, Preferred, Disabled, Forbidden }
public enum ActivationUnavailablePolicy { FailPlan, SkipFeature, AllowDegraded, RequireExplicitApproval }
public enum DependencyState { Unknown, Present, Missing, VersionMismatch, PermissionDenied, Misconfigured, Degraded }
public enum HostDependencyKind { Executable, File, Directory, Service, KernelFeature, HypervisorFeature, NetworkHelper, Credential, Permission, ProviderDefined }
public enum PermissionGrantState { Unknown, Granted, Denied, PromptRequired, RemediationRequired, VerificationFailed, Revoked }
public enum PermissionSeverity { Info, Warning, Error, Fatal }
public enum PreflightCheckState { Unknown, Passed, Failed, Skipped, Warning, RequiresRemediation }
public enum PermissionRemediationKind { ManualInstruction, Command, InstallPackage, InstallPolicyFile, ModifySettings, GrantEntitlement, StartService, ProviderDefined }
public enum PermissionVerificationState { Unknown, Pending, Passed, Failed, Expired }
public enum CapabilityConstraintKind { HostPlatform, HostVersion, GuestPlatform, GuestAbi, ExecutableVersion, DiskFormat, ArtifactFormat, Protocol, Transport, Permission, Configuration, Observability, ProviderDefined }
public enum ExecutionMode { Native, Translated, Emulated, Remote, NoOsGuest, Unsupported }
public enum UnsupportedSeverity { Info, Warning, Error, Fatal }

public readonly record struct PlatformSpec(string OperatingSystem, string Architecture, string? Variant = null, string? Version = null);
public readonly record struct GuestAbiSpec(string Family, string Architecture, SemanticVersion? Version = null, string? Variant = null);

public static class StandardEnvironmentCapabilities
{
    public static readonly CapabilityId ProcessIsolation =
        new("hpd.environment.isolation.process");
    public static readonly CapabilityId ContainerIsolation =
        new("hpd.environment.isolation.container");
    public static readonly CapabilityId SharedHostKernel =
        new("hpd.environment.isolation.host-kernel-shared");
    public static readonly CapabilityId HardwareVirtualization =
        new("hpd.environment.isolation.hardware-virtualized");
    public static readonly CapabilityId GuestAgentBoundary =
        new("hpd.environment.boundary.guest-agent");
    public static readonly CapabilityId MediatedEngineAuthority =
        new("hpd.environment.engine.authority-mediated");
    public static readonly CapabilityId HostLocalEndpointPublication =
        new("hpd.environment.endpoint.host-local");
}

public sealed record CapabilityRequirementSet
{
    public static CapabilityRequirementSet Empty { get; } = new();
    public IReadOnlyList<CapabilityRequirement> Items { get; init; } = Array.Empty<CapabilityRequirement>();
}

public sealed record CapabilityRequirement
{
    public required CapabilityId Id { get; init; }
    public CapabilityRequirementStrength Strength { get; init; } = CapabilityRequirementStrength.Required;
    public ProviderContractKind AppliesTo { get; init; }
    public ProviderId? PreferredProvider { get; init; }
    public string? Reason { get; init; }
}

public sealed record RuntimeCapabilityPolicy
{
    public static RuntimeCapabilityPolicy Default { get; } = new();
    public bool AllowPreferredDegradation { get; init; } = true;
    public bool FailOnMissingRequired { get; init; } = true;
    public bool IncludeDisabledCapabilitiesInPlan { get; init; } = true;
}

public sealed record ProviderPreflightPolicy
{
    public static ProviderPreflightPolicy Default { get; } = new();
    public bool RunPlanningPreflight { get; init; } = true;
    public bool AllowInteractiveRemediation { get; init; }
    public bool RequireVerificationAfterRemediation { get; init; } = true;
}

public sealed record RuntimeProfileInput
{
    public string? Name { get; init; }
    public IReadOnlyList<CapabilityRequirement> Capabilities { get; init; } = Array.Empty<CapabilityRequirement>();
    public IReadOnlyList<ProviderExtensionData> ProviderHints { get; init; } = Array.Empty<ProviderExtensionData>();
}

public sealed record ProviderDescriptor
{
    public required ProviderId Id { get; init; }
    public required string DisplayName { get; init; }
    public required SemanticVersion ContractVersion { get; init; }
    public required SemanticVersion ProviderVersion { get; init; }
    public required ProviderContractKind ContractKinds { get; init; }
    public required ProviderTrustLevel TrustLevel { get; init; }
    public ProviderActivationScope DefaultActivationScope { get; init; } = ProviderActivationScope.Runtime;
    public SemanticVersion? ProtocolVersion { get; init; }
    public ProviderPackageIdentity? PackageIdentity { get; init; }
    public IReadOnlyList<PlatformSpec> HostPlatforms { get; init; } = Array.Empty<PlatformSpec>();
    public IReadOnlyList<PlatformSpec> GuestPlatforms { get; init; } = Array.Empty<PlatformSpec>();
    public IReadOnlyList<GuestAbiSpec> GuestAbis { get; init; } = Array.Empty<GuestAbiSpec>();
    public IReadOnlyList<ProviderActivationScope> SupportedActivationScopes { get; init; } = Array.Empty<ProviderActivationScope>();
    public IReadOnlyList<ProviderActivationModel> ActivationModels { get; init; } = Array.Empty<ProviderActivationModel>();
    public IReadOnlyList<ProviderDiscoveryDescriptor> Discovery { get; init; } = Array.Empty<ProviderDiscoveryDescriptor>();
    public IReadOnlyList<HostDependencyRequirement> HostDependencies { get; init; } = Array.Empty<HostDependencyRequirement>();
    public IReadOnlyList<SchemaId> ConfigurationSchemas { get; init; } = Array.Empty<SchemaId>();
    public IReadOnlyList<SchemaId> ExtensionSchemas { get; init; } = Array.Empty<SchemaId>();
}

public sealed record ProviderActivationModel(ProviderActivationKind Kind, ProviderActivationScope Scope, ProviderTransportKind Transport, bool RequiresSupervision = false);
public sealed record ProviderDiscoveryDescriptor(ProviderDiscoveryKind Kind, string? Location = null, string? Detail = null);
public sealed record ProviderPackageIdentity(string? PackageId = null, string? Publisher = null, Digest? Digest = null);

public sealed record ProviderCapabilityReport
{
    public required ProviderId ProviderId { get; init; }
    public DateTimeOffset ObservedAt { get; init; }
    public PlatformSpec? HostPlatform { get; init; }
    public IReadOnlyList<CapabilityFact> Capabilities { get; init; } = Array.Empty<CapabilityFact>();
    public IReadOnlyList<HostDependencyFact> HostDependencies { get; init; } = Array.Empty<HostDependencyFact>();
    public IReadOnlyList<ProviderLimit> Limits { get; init; } = Array.Empty<ProviderLimit>();
    public IReadOnlyList<ProviderPreflightCheck> PreflightChecks { get; init; } = Array.Empty<ProviderPreflightCheck>();
    public IReadOnlyList<ProviderPermissionRequirement> RequiredPermissions { get; init; } = Array.Empty<ProviderPermissionRequirement>();
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
    public IReadOnlyList<ProviderExtensionData> Extensions { get; init; } = Array.Empty<ProviderExtensionData>();
}

public sealed record CapabilityFact
{
    public required CapabilityId Id { get; init; }
    public CapabilityCategory Category { get; init; }
    public ProviderContractKind AppliesTo { get; init; }
    public CapabilityState State { get; init; }
    public IReadOnlyList<CapabilityConstraint> Constraints { get; init; } = Array.Empty<CapabilityConstraint>();
    public CapabilityObservability Observability { get; init; } = CapabilityObservability.Normal;
    public string? Detail { get; init; }
}

public sealed record CapabilityConstraint(CapabilityConstraintKind Kind, string Name, string? Required = null, string? Observed = null, string? Detail = null);
public readonly record struct CapabilityObservability(bool StatusAvailable, bool EventsAvailable, bool MetricsAvailable, string? Detail = null)
{
    public static CapabilityObservability Normal { get; } = new(true, true, false);
}

public sealed record HostDependencyRequirement(HostDependencyRef Dependency, bool Required, string? MinimumVersion = null, string? Detail = null);
public sealed record HostDependencyFact(HostDependencyRef Dependency, DependencyState State, string? ObservedVersion = null, string? Detail = null);
public readonly record struct HostDependencyRef(HostDependencyKind Kind, string Name);
public readonly record struct ProviderLimit(string Name, long? HardLimit, long? SoftLimit, string Unit, string? Detail = null);

public sealed record ProviderPermissionRequirement
{
    public required PermissionId Id { get; init; }
    public required CapabilityId Capability { get; init; }
    public PermissionScope Scope { get; init; } = PermissionScope.Provider;
    public bool Required { get; init; }
    public bool CanPrompt { get; init; }
    public PermissionGrantState State { get; init; }
    public PermissionSeverity Severity { get; init; }
    public IReadOnlyList<ProviderPreflightCheck> PreflightChecks { get; init; } = Array.Empty<ProviderPreflightCheck>();
    public IReadOnlyList<PermissionCheck> Checks { get; init; } = Array.Empty<PermissionCheck>();
    public IReadOnlyList<PermissionRemediationOption> RemediationOptions { get; init; } = Array.Empty<PermissionRemediationOption>();
    public PermissionVerification? Verification { get; init; }
    public string? DisplayMessage { get; init; }
}

public enum PermissionScope { Provider, HostUser, HostSystem, Project, Runtime, RuntimeHost, Network, ExecutionUnit, FunctionSandbox, Operation }
public sealed record PermissionCheck(string Name, PreflightCheckState State, string? Detail = null);
public sealed record ProviderPreflightCheck(string Name, PreflightCheckState State, DiagnosticSeverity Severity = DiagnosticSeverity.Info, string? Detail = null);
public sealed record PermissionRemediationOption(PermissionRemediationKind Kind, string Description, bool RequiresAdmin = false, ProviderExtensionData? GeneratedArtifact = null, PermissionRemediationArtifact? Artifact = null);
public sealed record PermissionRemediationArtifact(string Name, ContentType ContentType, Digest? Digest = null, string? PathHint = null);
public sealed record PermissionVerification(PermissionVerificationState State, DateTimeOffset? VerifiedAt = null, string? Detail = null);

public sealed record RuntimePlan
{
    public required RuntimePlanId Id { get; init; }
    public required RuntimeTopologyPolicy TopologyPolicy { get; init; }
    public required PlatformCompatibilityPlan Compatibility { get; init; }
    public IReadOnlyList<SelectedProvider> Providers { get; init; } = Array.Empty<SelectedProvider>();
    public IReadOnlyList<RuntimePlanActivationStep> ActivationSteps { get; init; } = Array.Empty<RuntimePlanActivationStep>();
    public IReadOnlyList<ProviderActivationSpec> Activations { get; init; } = Array.Empty<ProviderActivationSpec>();
    public IReadOnlyList<CapabilityCoverage> CapabilityCoverage { get; init; } = Array.Empty<CapabilityCoverage>();
    public IReadOnlyList<ProviderPermissionRequirement> PermissionPlan { get; init; } = Array.Empty<ProviderPermissionRequirement>();
    public IReadOnlyList<UnsupportedReason> UnsupportedReasons { get; init; } = Array.Empty<UnsupportedReason>();
    public IReadOnlyList<ProviderExtensionData> Extensions { get; init; } = Array.Empty<ProviderExtensionData>();
}

public sealed record RuntimePlanRequest
{
    public required RuntimeTopologyPolicy TopologyPolicy { get; init; }
    public PlatformSpec? RequestedPlatform { get; init; }
    public GuestAbiSpec? RequestedGuestAbi { get; init; }
    public IReadOnlyList<ProviderId> PreferredProviders { get; init; } = Array.Empty<ProviderId>();
    public ProviderContractKind RequiredContracts { get; init; }
    public CapabilityRequirementSet Capabilities { get; init; } = CapabilityRequirementSet.Empty;
    public RuntimeCapabilityPolicy CapabilityPolicy { get; init; } = RuntimeCapabilityPolicy.Default;
    public ProviderPreflightPolicy PreflightPolicy { get; init; } = ProviderPreflightPolicy.Default;
    public RuntimeProfileInput? Profile { get; init; }
    public IReadOnlyList<ProviderExtensionData> ProviderHints { get; init; } = Array.Empty<ProviderExtensionData>();
}

public sealed record RuntimePlanActivationStep
{
    public required RuntimePlanStepId Id { get; init; }
    public required ProviderActivationSpec Activation { get; init; }
    public IReadOnlyList<RuntimePlanStepId> DependsOn { get; init; } = Array.Empty<RuntimePlanStepId>();
    public ActivationUnavailablePolicy UnavailablePolicy { get; init; } = ActivationUnavailablePolicy.FailPlan;
    public IReadOnlyList<ProviderComponentExpectation> ExpectedComponents { get; init; } = Array.Empty<ProviderComponentExpectation>();
}

public readonly record struct SelectedProvider(ProviderContractKind ContractKind, ProviderId ProviderId, ProviderActivationScope ActivationScope, bool Required, IReadOnlyList<CapabilityId>? CoveredCapabilities = null);
public sealed record CapabilityCoverage(CapabilityId Capability, CapabilityRequirementStrength Strength, CapabilityState State, ProviderId? ProviderId = null, string? Detail = null);
public sealed record ProviderComponentExpectation(ProviderComponentKind Kind, string Name, bool Required = true);
public readonly record struct UnsupportedReason(DiagnosticCode Code, UnsupportedSeverity Severity, ProviderId? ProviderId, ProviderContractKind? ContractKind, string Message);

public sealed record PlatformCompatibilityPlan
{
    public required PlatformSpec RequestedPlatform { get; init; }
    public PlatformSpec? SelectedArtifactPlatform { get; init; }
    public PlatformSpec? GuestPlatform { get; init; }
    public GuestAbiSpec? GuestAbi { get; init; }
    public PlatformSpec? HostPlatform { get; init; }
    public required ExecutionMode ExecutionMode { get; init; }
    public ProviderId? PlacementProviderId { get; init; }
    public ProviderId? TranslationProviderId { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
}

public sealed record RuntimePlanValidationResult
{
    public bool IsSupported { get; init; }
    public IReadOnlyList<UnsupportedReason> UnsupportedReasons { get; init; } = Array.Empty<UnsupportedReason>();
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
}

public sealed record ProviderActivationSpec
{
    public required ProviderId ProviderId { get; init; }
    public required ProviderActivationScope Scope { get; init; }
    public required string ScopeKey { get; init; }
    public required ProviderContractKind RequiredContracts { get; init; }
    public ProviderActivationKind ActivationKind { get; init; } = ProviderActivationKind.ProviderDefined;
    public IReadOnlyList<CapabilityId> RequiredCapabilities { get; init; } = Array.Empty<CapabilityId>();
    public IReadOnlyList<RuntimePlanStepId> DependsOnSteps { get; init; } = Array.Empty<RuntimePlanStepId>();
    public IReadOnlyList<PermissionId> RequiredPermissions { get; init; } = Array.Empty<PermissionId>();
    public IReadOnlyList<ProviderExtensionData> Configuration { get; init; } = Array.Empty<ProviderExtensionData>();
    public ProviderSupervisorRequirement Supervisor { get; init; }
    public ProviderTransportRequirement Transport { get; init; }
    public ProviderAuthPolicy AuthPolicy { get; init; }
    public ProviderHealthPolicy HealthPolicy { get; init; }
    public ProviderLogPolicy LogPolicy { get; init; }
}

public sealed record ProviderActivationStatus : ResourceStatus
{
    public required ProviderActivationPhase ActivationPhase { get; init; }
    public ProviderActivationId? ActivationId { get; init; }
    public ProviderId ProviderId { get; init; }
    public ProviderActivationKind ActivationKind { get; init; }
    public TargetHandle<ProviderActivation>? ActivationHandle { get; init; }
    public ProviderEndpoint? ControlEndpoint { get; init; }
    public IReadOnlyList<ProviderNamedEndpoint> Endpoints { get; init; } = Array.Empty<ProviderNamedEndpoint>();
    public IReadOnlyList<ProviderComponentStatus> Components { get; init; } = Array.Empty<ProviderComponentStatus>();
    public IReadOnlyList<ProviderPreflightCheck> PreflightChecks { get; init; } = Array.Empty<ProviderPreflightCheck>();
    public IReadOnlyList<ProviderPermissionRequirement> Permissions { get; init; } = Array.Empty<ProviderPermissionRequirement>();
    public string? AuthIdentity { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? StoppedAt { get; init; }
}

public enum ProviderActivationPhase { Unknown, Starting, Ready, Degraded, Reconnecting, Stopping, Stopped, Failed }
public readonly record struct ProviderSupervisorRequirement(bool RequiresSupervision, bool RestartOnFailure, TimeSpan? StartupTimeout);
public readonly record struct ProviderTransportRequirement(ProviderTransportKind TransportKind, bool RequiresStreaming, bool RequiresHandlePassing, bool RequiresPeerAuthentication);
public readonly record struct ProviderAuthPolicy(string IdentityScope, bool RequireSameUser, bool AllowRemoteIdentity);
public readonly record struct ProviderHealthPolicy(TimeSpan StartupTimeout, TimeSpan ProbeInterval, TimeSpan StopTimeout);
public readonly record struct ProviderLogPolicy(string RetentionHint, bool CaptureStartupLogs, bool CaptureDiagnosticLogs);
public readonly record struct ProviderEndpoint(string Scheme, string Address, ushort? Port = null, string? Path = null);
public sealed record ProviderNamedEndpoint(string Name, ProviderEndpointPurpose Purpose, ProviderEndpoint Endpoint, ProviderTransportKind Transport, EndpointSensitivity Sensitivity = EndpointSensitivity.LocalOnly);
public sealed record ProviderComponentStatus(ProviderComponentKind Kind, string Name, ProviderComponentPhase Phase, string? ProcessId = null, ProviderNamedEndpoint? Endpoint = null, IReadOnlyList<Condition>? Conditions = null);

// ---------------------------------------------------------------------------
// Runtime host, execution unit, and process lane
// ---------------------------------------------------------------------------

public sealed record RuntimeHostSpec
{
    public RuntimePlanId? RuntimePlan { get; init; }
    public ProviderId? PreferredProvider { get; init; }
    public required PlatformSpec Platform { get; init; }
    public ResourceQuotaPolicy Capacity { get; init; } = ResourceQuotaPolicy.Default;
    public RuntimeHostStorageSpec? Storage { get; init; }
    public RuntimeHostBootstrapSpec? Bootstrap { get; init; }
    public SecurityPolicy SecurityPolicy { get; init; } = SecurityPolicy.Default;
    public RuntimeTopologyPolicy TopologyPolicy { get; init; } = new();
    public LifecyclePolicy LifecyclePolicy { get; init; } = LifecyclePolicy.Default;
    public RuntimeHostLifecyclePolicy HostPolicy { get; init; } = RuntimeHostLifecyclePolicy.Default;
    public IReadOnlyList<ResourceRef<NetworkMembership>> NetworkMemberships { get; init; } = Array.Empty<ResourceRef<NetworkMembership>>();
    public IReadOnlyList<ResourceRef<ContentProjection>> SharedContentProjections { get; init; } = Array.Empty<ResourceRef<ContentProjection>>();
    public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = Array.Empty<ProviderExtensionData>();
}

public sealed record RuntimeHostStatus : ResourceStatus
{
    public required RuntimeHostPhase HostPhase { get; init; }
    public TargetHandle<RuntimeHost>? Handle { get; init; }
    public ProviderOpaqueHandle? ProviderHandle { get; init; }
    public ResourceRef<ProviderActivation>? ProviderActivation { get; init; }
    public ControlEndpoint? GuestControlEndpoint { get; init; }
    public CapacityObservation? ObservedCapacity { get; init; }
    public RuntimeHostGenerationStatus Generations { get; init; } = new();
    public RuntimeHostStorageStatus? Storage { get; init; }
    public RuntimeHostBootstrapStatus? Bootstrap { get; init; }
    public RuntimeHostProvisioningStatus? Provisioning { get; init; }
    public GuestControlStatus? GuestControl { get; init; }
    public RuntimeHostReadinessStatus? Readiness { get; init; }
    public RuntimeHostControlPlaneStatus? ControlPlane { get; init; }
    public RuntimeHostProtectionStatus? Protection { get; init; }
    public IReadOnlyList<ResourceRef<ExecutionUnit>> ExecutionUnits { get; init; } = Array.Empty<ResourceRef<ExecutionUnit>>();
}

public enum RuntimeHostPhase { Unknown, Declared, Preparing, Provisioning, Starting, Running, Ready, Degraded, Stopping, Stopped, Resetting, Deleting, Deleted, Failed }
public sealed record ControlEndpoint(string Scheme, string Address, int? Port = null, string? Path = null);
public readonly record struct CapacityObservation(double CpuCores, long MemoryBytes, long StorageBytes);

public sealed record RuntimeHostBootstrapSpec
{
    public ResourceRef<ContentArtifact>? GuestImage { get; init; }
    public IReadOnlyList<RuntimeHostBootArtifactSpec> BootArtifacts { get; init; } = Array.Empty<RuntimeHostBootArtifactSpec>();
    public IReadOnlyList<RuntimeHostInitDataSpec> InitData { get; init; } = Array.Empty<RuntimeHostInitDataSpec>();
    public IReadOnlyList<GuestComponentSpec> GuestComponents { get; init; } = Array.Empty<GuestComponentSpec>();
    public RuntimeHostProvisioningSpec? Provisioning { get; init; }
    public IReadOnlyList<ReadinessGateSpec> ReadinessGates { get; init; } = Array.Empty<ReadinessGateSpec>();
    public RuntimeHostBootstrapRegenerationPolicy RegenerationPolicy { get; init; } = RuntimeHostBootstrapRegenerationPolicy.OnSpecGenerationChange;
}

public enum RuntimeHostBootArtifactKind { GuestImage, InstallMedia, Kernel, Initrd, Firmware, BootstrapIso, ProviderDefined }
public enum RuntimeHostInitDataKind { NoCloud, CloudInit, Ignition, GuestAgentConfig, ProviderDefined }
public enum RuntimeHostBootstrapRegenerationPolicy { Never, OnSpecGenerationChange, OnEveryStart, ProviderDefault }
public enum GuestComponentKind { GuestAgent, SshServer, CloudInit, ContainerRuntime, NetworkAgent, ProviderDefined }
public enum RuntimeHostProvisioningStage { EarlyBoot, BeforeGuestControl, GuestControlInstall, System, User, AfterReadiness, ProviderDefault }
public enum RuntimeHostProvisionRunPolicy { FirstBoot, EveryBoot, OnSpecGenerationChange, OnDependencyChange, Manual, ProviderDefault }
public enum ProvisioningIdempotency { Unknown, Required, BestEffort, NotIdempotent }
public enum GuestExecutionIdentityKind { Root, DefaultUser, NamedUser, ProviderDefault }
public enum RuntimeHostProvisioningActionKind { RunScript, AssertFile, StructuredEdit, InstallGuestComponent, ProviderDefined }
public enum ScriptSourceKind { Inline, ContentProjection, ContentStore, ProviderSource }
public enum GuestFileContentKind { InlineBytes, InlineText, ContentProjection, ContentStore, ProviderSource }
public enum GuestFileOverwritePolicy { Never, IfMissing, IfDifferent, Always }
public enum StructuredEditFormat { Yaml, Json, Toml, Ini, ProviderDefined }
public enum StructuredEditOperation { Set, Merge, Append, Remove, ProviderDefined }
public enum ReadinessGateKind { GuestControlReachable, Command, FileExists, PortOpen, ProviderCheck, Condition, EngineReady }
public enum ReadinessGateScope { RuntimeHost, GuestControl, Network, Storage, Engine, Provider }

public sealed record RuntimeHostBootArtifactSpec(RuntimeHostBootArtifactKind Kind, ResourceRef<ContentArtifact>? Artifact = null, ResourceRef<ContentProjection>? Content = null, ProviderExtensionData? Data = null);
public sealed record RuntimeHostInitDataSpec(RuntimeHostInitDataKind Kind, ResourceRef<ContentProjection>? Content = null, ProviderExtensionData? Data = null);
public sealed record GuestComponentSpec(GuestComponentKind Kind, string? Name = null, ResourceRef<ContentProjection>? Payload = null, ProviderExtensionData? Data = null);
public sealed record RuntimeHostProvisioningSpec(IReadOnlyList<RuntimeHostProvisioningStepSpec>? Steps = null);
public sealed record RuntimeHostProvisioningStepSpec(string Name, RuntimeHostProvisioningStage Stage, RuntimeHostProvisioningAction Action, RuntimeHostProvisionRunPolicy RunPolicy = RuntimeHostProvisionRunPolicy.OnSpecGenerationChange, ProvisioningIdempotency Idempotency = ProvisioningIdempotency.Required, TimeSpan? Timeout = null);
public sealed record GuestExecutionIdentity(GuestExecutionIdentityKind Kind, string? User = null, string? Group = null);
public sealed record RuntimeHostProvisioningAction(RuntimeHostProvisioningActionKind Kind, ScriptSourceSpec? Script = null, GuestFileAssertionSpec? File = null, GuestStructuredEditSpec? Edit = null, GuestComponentSpec? Component = null, ProviderExtensionData? Data = null);
public sealed record ScriptSourceSpec(ScriptSourceKind Kind, string? Inline = null, ResourceRef<ContentProjection>? ContentProjection = null, ProviderSourceSelection? ProviderSource = null);
public sealed record GuestFileAssertionSpec(GuestPath Path, GuestFileContentSpec Content, GuestFileOverwritePolicy Overwrite = GuestFileOverwritePolicy.IfDifferent, UnixFileModeSpec? Mode = null, GuestExecutionIdentity? Owner = null);
public sealed record GuestFileContentSpec(GuestFileContentKind Kind, ReadOnlyMemory<byte> InlineBytes = default, string? InlineText = null, ResourceRef<ContentProjection>? ContentProjection = null, ProviderSourceSelection? ProviderSource = null);
public sealed record UnixFileModeSpec(uint Mode);
public sealed record GuestStructuredEditSpec(GuestPath Path, StructuredEditFormat Format, StructuredEditOperation Operation, string Selector, ProviderExtensionData Value);
public sealed record ReadinessGateSpec(string Name, ReadinessGateKind Kind, ReadinessGateScope Scope, RetryPolicy Retry, TimeSpan? Timeout = null, ProviderExtensionData? Data = null);
public sealed record RetryPolicy(int MaxAttempts = 1, TimeSpan? Delay = null, TimeSpan? Backoff = null);

public sealed record RuntimeHostGenerationStatus
{
    public RuntimeHostStartGeneration? HostStartGeneration { get; init; }
    public GuestBootGeneration? GuestBootGeneration { get; init; }
    public ResourceGeneration? GuestAgentGeneration { get; init; }
    public ResourceGeneration? BootstrapGeneration { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
}

public sealed record RuntimeHostBootstrapStatus
{
    public IReadOnlyList<RuntimeHostBootArtifactStatus> Artifacts { get; init; } = Array.Empty<RuntimeHostBootArtifactStatus>();
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
}

public sealed record RuntimeHostBootArtifactStatus(RuntimeHostBootArtifactKind Kind, bool Ready, Digest? Digest = null, string? Detail = null);
public sealed record RuntimeHostProvisioningStatus(IReadOnlyList<RuntimeHostProvisioningStepStatus>? Steps = null, bool Complete = false);
public sealed record RuntimeHostProvisioningStepStatus(string Name, RuntimeHostProvisioningStage Stage, ResourcePhase Phase, DateTimeOffset? LastRunAt = null, Diagnostic? Diagnostic = null);
public sealed record GuestControlStatus(bool Expected, bool Installed, bool Reachable, ProviderNamedEndpoint? Endpoint = null, ProviderTransportKind Transport = ProviderTransportKind.ProviderDefined, IReadOnlyList<Condition>? Conditions = null);
public sealed record RuntimeHostReadinessStatus(bool Ready, RuntimeHostStartGeneration? ObservedHostStartGeneration = null, IReadOnlyList<ReadinessGateStatus>? Gates = null);
public sealed record ReadinessGateStatus(string Name, ReadinessGateKind Kind, ConditionStatus Status, DateTimeOffset? LastCheckedAt = null, string? Message = null);
public sealed record RuntimeHostControlPlaneStatus(IReadOnlyList<ProviderComponentStatus>? Components = null, IReadOnlyList<ProviderNamedEndpoint>? Endpoints = null);
public sealed record RuntimeHostProtectionStatus(bool DeleteProtected, string? Reason = null);

public sealed record RuntimeHostStorageSpec
{
    public RuntimeHostPrimaryDiskSpec? PrimaryDisk { get; init; }
    public IReadOnlyList<RuntimeHostBlockVolumeAttachmentSpec> BlockVolumes { get; init; } = Array.Empty<RuntimeHostBlockVolumeAttachmentSpec>();
}

public sealed record RuntimeHostPrimaryDiskSpec(ByteSize Size, DiskImageFormat? Format = null, bool AllowResize = true);
public sealed record RuntimeHostBlockVolumeAttachmentSpec(ResourceRef<BlockVolume> Volume, GuestPath? GuestPath = null, VolumeAccessMode AccessMode = VolumeAccessMode.ReadWrite);
public sealed record RuntimeHostStorageStatus(RuntimeHostPrimaryDiskStatus? PrimaryDisk = null, IReadOnlyList<RuntimeHostBlockVolumeAttachmentStatus>? BlockVolumes = null);
public sealed record RuntimeHostPrimaryDiskStatus(bool Ready, ByteSize? Size = null, DiskImageFormat? Format = null, bool Converted = false, bool ResizePending = false);
public sealed record RuntimeHostBlockVolumeAttachmentStatus(ResourceRef<BlockVolume> Volume, VolumeAttachmentPhase Phase, GuestPath? GuestPath = null, VolumeLockStatus? Lock = null);
public sealed record RuntimeHostResetRequest(RuntimeHostResetScope Scope, bool RetainResourceIdentity = true, bool RetainUserData = true, string? Reason = null);
public sealed record RuntimeHostResetResult(RuntimeHostResetScope Scope, ResourceRef<RuntimeHost> Host, DateTimeOffset CompletedAt, IReadOnlyList<Condition>? Conditions = null);

public sealed record ExecutionUnitSpec
{
    public ExecutionUnitIdentityKey? ReconciliationKey { get; init; }
    public WorkloadStorageRequest? WorkloadStorage { get; init; }
    public ResourceRef<RuntimeHost>? PreferredHost { get; init; }
    public PlacementPolicy Placement { get; init; } = PlacementPolicy.Default;
    public ResourceRef<RootFilesystemView>? Rootfs { get; init; }
    public IReadOnlyList<ResourceRef<ContentProjection>> ContentProjections { get; init; } = Array.Empty<ResourceRef<ContentProjection>>();
    public ProcessInvocationSpec? DefaultProcess { get; init; }
    public ExecutionUnitNetworkSpec Network { get; init; } = ExecutionUnitNetworkSpec.Empty;
    public ExecutionUnitIdentitySpec Identity { get; init; } = ExecutionUnitIdentitySpec.Default;
    public SecurityPolicy SecurityPolicy { get; init; } = SecurityPolicy.Default;
    public ResourceQuotaPolicy QuotaPolicy { get; init; } = ResourceQuotaPolicy.Default;
    public LifecyclePolicy LifecyclePolicy { get; init; } = LifecyclePolicy.Default;
    public ProcessLogPolicy LogPolicy { get; init; } = ProcessLogPolicy.Default;
    public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = Array.Empty<ProviderExtensionData>();
}

public readonly record struct ExecutionUnitIdentityKey(string Value);

public enum WorkloadStoragePersistenceClass
{
    Runtime,
    Workload,
    Installation,
}

public sealed record WorkloadStorageRequest
{
    public required string LogicalId { get; init; }
    public WorkloadStoragePersistenceClass PersistenceClass { get; init; } =
        WorkloadStoragePersistenceClass.Workload;
}

public sealed record WorkloadStorageAllocation
{
    public required string LogicalId { get; init; }
    public required ProviderOpaqueHandle ProviderHandle { get; init; }
    public required string EffectiveRuntimePath { get; init; }
    public required WorkloadStoragePersistenceClass PersistenceClass { get; init; }
    public required ResourceGeneration Generation { get; init; }
}

public sealed record ExecutionUnitStatus : ResourceStatus
{
    public required ExecutionUnitPhase UnitPhase { get; init; }
    public ResourceRef<RuntimeHost>? AssignedHost { get; init; }
    public TargetHandle<ExecutionUnit>? Handle { get; init; }
    public ProviderOpaqueHandle? NamespaceHandle { get; init; }
    public WorkloadStorageAllocation? WorkloadStorage { get; init; }
    public ResourceRef<ProcessInvocation>? PrimaryProcess { get; init; }
    public ProcessInvocationResult? PrimaryProcessResult { get; init; }
    public ResourceRef<RootFilesystemView>? RealizedRootfs { get; init; }
    public IReadOnlyList<ResourceRef<ProcessInvocation>> ActiveProcesses { get; init; } = Array.Empty<ResourceRef<ProcessInvocation>>();
    public IReadOnlyList<ResourceRef<ContentProjection>> RealizedContentProjections { get; init; } = Array.Empty<ResourceRef<ContentProjection>>();
    public IReadOnlyList<ResourceRef<NetworkMembership>> NetworkMemberships { get; init; } = Array.Empty<ResourceRef<NetworkMembership>>();
    public IReadOnlyList<ResourceRef<PublishedEndpoint>> PublishedEndpoints { get; init; } = Array.Empty<ResourceRef<PublishedEndpoint>>();
    public IReadOnlyList<ResourceRef<AuthorityBinding>> AuthorityBindings { get; init; } = Array.Empty<ResourceRef<AuthorityBinding>>();
    public ResourceUsageObservation? Usage { get; init; }
}

public enum ExecutionUnitPhase { Unknown, Declared, ProjectingContent, Ready, Starting, Running, Stopping, Stopped, Deleting, Deleted, Failed }
public sealed record PlacementPolicy { public static PlacementPolicy Default { get; } = new(); public RuntimeTopologyMode? RequestedTopology { get; init; } public IReadOnlyList<string> AffinityLabels { get; init; } = Array.Empty<string>(); public IReadOnlyList<string> AntiAffinityLabels { get; init; } = Array.Empty<string>(); }
public sealed record ExecutionUnitNetworkSpec { public static ExecutionUnitNetworkSpec Empty { get; } = new(); public string? Hostname { get; init; } public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>(); public IReadOnlyList<ResourceRef<NetworkMembership>> Memberships { get; init; } = Array.Empty<ResourceRef<NetworkMembership>>(); }
public sealed record ExecutionUnitIdentitySpec { public static ExecutionUnitIdentitySpec Default { get; } = new(); public string? User { get; init; } public string? Group { get; init; } }
public readonly record struct ResourceUsageObservation(double? CpuPercent, long? MemoryBytes, long? StorageBytes);

public sealed record ProcessInvocationSpec
{
    public required TargetHandle<ExecutionUnit> Target { get; init; }
    public ProcessRole Role { get; init; } = ProcessRole.Exec;
    public required ProcessCommandSpec Command { get; init; }
    public ProcessIdentitySpec? Identity { get; init; }
    public ProcessLimitSpec? Limits { get; init; }
    public ProcessIoSpec Io { get; init; } = ProcessIoSpec.Default;
    public ProcessInvocationPolicy Policy { get; init; } = ProcessInvocationPolicy.Default;
    public ProcessIsolationPolicy Isolation { get; init; } = ProcessIsolationPolicy.Default;
    public bool PersistResource { get; init; }
    public ObservationRetentionPolicy ObservationRetention { get; init; } = ObservationRetentionPolicy.ResultAndDiagnostics;
    public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = Array.Empty<ProviderExtensionData>();
}

public sealed record ProcessInvocationStatus : ResourceStatus { public required ProcessInvocationPhase ProcessPhase { get; init; } public TargetHandle<ProcessInvocation>? Handle { get; init; } public string? ProviderProcessId { get; init; } public int? SystemProcessId { get; init; } public ProcessIoState IoState { get; init; } = ProcessIoState.Unknown; public DateTimeOffset? StartedAt { get; init; } public DateTimeOffset? ExitedAt { get; init; } public ProcessInvocationResult? Result { get; init; } }
public enum ProcessRole { Primary, Exec, Task, Sidecar }
public enum ProcessInvocationPhase { Unknown, Created, Prepared, Running, Stopping, Stopped, Exited, Failed }
public enum ObservationRetentionPolicy { None, ResultAndDiagnostics, EventsAndResult, DurableResource }
public sealed record ProcessCommandSpec { public required string FileName { get; init; } public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>(); public string? WorkingDirectory { get; init; } public IReadOnlyDictionary<string, string?> Environment { get; init; } = Empty.NullableStringDictionary; }
public sealed record ProcessIdentitySpec(string? User = null, string? Group = null, IReadOnlyList<string>? SupplementalGroups = null);
public sealed record ProcessLimitSpec(int? ProcessCount = null, long? MemoryBytes = null, TimeSpan? CpuTime = null);
public sealed record ProcessIoSpec { public static ProcessIoSpec Default { get; } = new(); public ProcessInputSpec StandardInput { get; init; } = ProcessInputSpec.None; public ProcessOutputSpec StandardOutput { get; init; } = ProcessOutputSpec.CaptureAndStream; public ProcessOutputSpec StandardError { get; init; } = ProcessOutputSpec.CaptureAndStream; public bool MergeStandardError { get; init; } public TerminalSpec? Terminal { get; init; } public ProcessLogPolicy LogPolicy { get; init; } = ProcessLogPolicy.Default; }
public sealed record ProcessInputSpec { public static ProcessInputSpec None { get; } = new() { Kind = ProcessInputKind.None }; public ProcessInputKind Kind { get; init; } public ReadOnlyMemory<byte> InlineBytes { get; init; } public ResourceRef<ContentProjection>? Source { get; init; } }
public enum ProcessInputKind { None, InlineBytes, Stream, ContentProjection }
public sealed record ProcessOutputSpec { public static ProcessOutputSpec CaptureAndStream { get; } = new() { Capture = true, Stream = true }; public bool Capture { get; init; } public bool Stream { get; init; } public int? MaxCapturedBytes { get; init; } public ResourceRef<ContentProjection>? Sink { get; init; } }
public sealed record ProcessLogPolicy { public static ProcessLogPolicy Default { get; } = new(); public bool RetainOutputEvents { get; init; } public int? MaxRetainedBytesPerStream { get; init; } }
public readonly record struct TerminalSpec(int Columns, int Rows);
public sealed record ProcessInvocationPolicy { public static ProcessInvocationPolicy Default { get; } = new(); public bool AllowBackground { get; init; } public bool StopProcessTree { get; init; } = true; public TimeSpan? Timeout { get; init; } public TimeSpan? InactivityTimeout { get; init; } public StopPolicy Stop { get; init; } = StopPolicy.Default; public TimeSpan OutputDrainTimeout { get; init; } = TimeSpan.FromSeconds(2); public bool StopOnRunCancellation { get; init; } = true; }
public sealed record ProcessIsolationPolicy { public static ProcessIsolationPolicy Default { get; } = new(); public ProcessIsolationMode Mode { get; init; } = ProcessIsolationMode.ProviderDefault; public FilesystemAccessPolicy Filesystem { get; init; } = FilesystemAccessPolicy.Default; public NetworkEgressPolicy Network { get; init; } = NetworkEgressPolicy.Blocked; public UnixSocketAccessPolicy UnixSockets { get; init; } = UnixSocketAccessPolicy.None; public EnvironmentAccessPolicy Environment { get; init; } = EnvironmentAccessPolicy.Default; public TlsTrustPolicy TlsTrust { get; init; } = TlsTrustPolicy.None; public ProcessInteractivePolicy Interactive { get; init; } = ProcessInteractivePolicy.Default; public ProcessViolationPolicy Violations { get; init; } = ProcessViolationPolicy.Default; public ProcessIsolationDegradationPolicy Degradation { get; init; } = ProcessIsolationDegradationPolicy.FailClosed; public IReadOnlyList<ResourceRef<AuthorityBinding>> AuthorityBindings { get; init; } = Array.Empty<ResourceRef<AuthorityBinding>>(); public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = Array.Empty<ProviderExtensionData>(); }
public enum ProcessIsolationMode { ProviderDefault, Disabled, Isolated }
public sealed record ProcessIsolationDegradationPolicy { public static ProcessIsolationDegradationPolicy FailClosed { get; } = new() { Mode = ProcessIsolationDegradationMode.FailClosed }; public ProcessIsolationDegradationMode Mode { get; init; } = ProcessIsolationDegradationMode.FailClosed; public IReadOnlyList<string> AllowDegradedFeatures { get; init; } = Array.Empty<string>(); }
public enum ProcessIsolationDegradationMode { FailClosed, AllowProviderDegraded, ObserveOnly }
public sealed record FilesystemAccessPolicy { public static FilesystemAccessPolicy Default { get; } = new(); public IReadOnlyList<PathAccessRule> Rules { get; init; } = Array.Empty<PathAccessRule>(); public DangerousPathPolicy DangerousPaths { get; init; } = DangerousPathPolicy.Default; public SymlinkEvaluationPolicy Symlinks { get; init; } = SymlinkEvaluationPolicy.ResolveExistingPaths; public MoveProtectionPolicy MoveProtection { get; init; } = MoveProtectionPolicy.ProtectDeniedPaths; }
public sealed record PathAccessRule { public required PathAccessRuleKind Kind { get; init; } public required HostPath Path { get; init; } public PathPatternKind PatternKind { get; init; } = PathPatternKind.LiteralOrSubpath; public string? Reason { get; init; } }
public enum PathAccessRuleKind { AllowRead, DenyRead, AllowWrite, DenyWrite }
public enum PathPatternKind { Literal, LiteralOrSubpath, Glob, ProviderValidate }
public sealed record DangerousPathPolicy { public static DangerousPathPolicy Default { get; } = new(); public bool ProtectSensitiveDefaults { get; init; } = true; public IReadOnlyList<HostPath> AdditionalDeniedReads { get; init; } = Array.Empty<HostPath>(); public IReadOnlyList<HostPath> AdditionalDeniedWrites { get; init; } = Array.Empty<HostPath>(); }
public enum SymlinkEvaluationPolicy { ProviderDefault, ResolveExistingPaths, PreserveLexicalPath, DenySymlinks }
public enum MoveProtectionPolicy { ProviderDefault, None, ProtectDeniedPaths }
public sealed record NetworkEgressPolicy { public static NetworkEgressPolicy Blocked { get; } = new() { Mode = NetworkEgressMode.Blocked }; public required NetworkEgressMode Mode { get; init; } public IReadOnlyList<DomainRule> AllowedDomains { get; init; } = Array.Empty<DomainRule>(); public IReadOnlyList<DomainRule> DeniedDomains { get; init; } = Array.Empty<DomainRule>(); public ParentProxyPolicy? ParentProxy { get; init; } public RequestFilterPolicy? RequestFilter { get; init; } public bool RequireProxyMediation { get; init; } = true; }
public enum NetworkEgressMode { Blocked, Filtered, Unrestricted }
public sealed record DomainRule { public required string Pattern { get; init; } public DomainRuleKind Kind { get; init; } = DomainRuleKind.ProviderValidate; public string? Reason { get; init; } }
public enum DomainRuleKind { ExactHost, WildcardSubdomain, IpLiteral, Localhost, ProviderValidate }
public sealed record ParentProxyPolicy { public Uri? ProxyUri { get; init; } public CredentialRef? Credential { get; init; } public bool AllowEnvironmentProxy { get; init; } }
public sealed record RequestFilterPolicy { public string? PolicyName { get; init; } public ProviderExtensionData? ProviderRuleSet { get; init; } }
public sealed record UnixSocketAccessPolicy { public static UnixSocketAccessPolicy None { get; } = new(); public bool AllowAll { get; init; } public IReadOnlyList<UnixSocketAccessRule> AllowedSockets { get; init; } = Array.Empty<UnixSocketAccessRule>(); }
public sealed record UnixSocketAccessRule { public required UnixSocketPath Path { get; init; } public SensitiveAuthorityClass AuthorityClass { get; init; } = SensitiveAuthorityClass.ProviderDefined; public string? Purpose { get; init; } }
public sealed record EnvironmentAccessPolicy { public static EnvironmentAccessPolicy Default { get; } = new(); public IReadOnlyList<string> AllowedVariables { get; init; } = Array.Empty<string>(); public IReadOnlyDictionary<string, string> InjectedVariables { get; init; } = Empty.StringDictionary; public bool StripUnlistedVariables { get; init; } = true; }
public sealed record TlsTrustPolicy { public static TlsTrustPolicy None { get; } = new(); public TlsTrustMode Mode { get; init; } = TlsTrustMode.None; public ResourceRef<AuthorityBinding>? TrustAuthority { get; init; } public bool InjectTrustEnvironmentVariables { get; init; } }
public enum TlsTrustMode { None, ExistingAuthority, EphemeralProviderAuthority, ExternalMitmProxy }
public sealed record ProcessInteractivePolicy { public static ProcessInteractivePolicy Default { get; } = new(); public bool AllowPty { get; init; } public bool AllowStdin { get; init; } = true; public bool AllowLocalBinding { get; init; } public IReadOnlyList<string> AllowedMachLookups { get; init; } = Array.Empty<string>(); }
public sealed record ProcessViolationPolicy { public static ProcessViolationPolicy Default { get; } = new(); public ProcessViolationAction Action { get; init; } = ProcessViolationAction.ObserveAndFailInvocation; public IReadOnlyList<string> IgnorePatterns { get; init; } = Array.Empty<string>(); public int ObservationTailLimit { get; init; } = 100; }
public enum ProcessViolationAction { ObserveOnly, ObserveAndFailInvocation, ObserveAndBlockFutureInvocations, ProviderDefault }
public sealed record StopPolicy { public static StopPolicy Default { get; } = new(); public StopKind Kind { get; init; } = StopKind.GracefulThenKill; public TimeSpan GracePeriod { get; init; } = TimeSpan.FromSeconds(10); public string? ProviderSignal { get; init; } }
public enum StopKind { Graceful, Kill, GracefulThenKill, ProviderSignal }
public enum ProcessIoState { Unknown, Open, InputClosed, OutputClosed, Closed }
public readonly record struct ProcessSignal(string Name);
public sealed record ProcessStopRequest(StopKind Kind, string? Reason = null, TimeSpan? GracePeriod = null);
public sealed record ProcessInvocationResult { public ResourceId<ProcessInvocation>? ProcessId { get; init; } public int? SystemProcessId { get; init; } public string? ProviderProcessId { get; init; } public int? ExitCode { get; init; } public required ProcessCompletionKind CompletionKind { get; init; } public DateTimeOffset? StartedAt { get; init; } public DateTimeOffset? ExitedAt { get; init; } public TimeSpan? Duration { get; init; } public required ProcessCapturedOutput Output { get; init; } public IReadOnlyList<ProcessViolation> Violations { get; init; } = Array.Empty<ProcessViolation>(); public IReadOnlyList<Condition> Diagnostics { get; init; } = Array.Empty<Condition>(); }
public enum ProcessCompletionKind { Completed, Exited, FailedToStart, TimedOut, Cancelled, Stopped, Killed, Faulted }
public sealed record ProcessCapturedOutput { public required ProcessStreamOutput Stdout { get; init; } public required ProcessStreamOutput Stderr { get; init; } public bool MergedStandardError { get; init; } public bool OutputDrainTimedOut { get; init; } public TimeSpan OutputDrainTimeout { get; init; } }
public sealed record ProcessStreamOutput { public ReadOnlyMemory<byte> CapturedBytes { get; init; } public long BytesObserved { get; init; } public long BytesCaptured { get; init; } public long BytesDiscarded { get; init; } public bool Truncated { get; init; } }
public sealed record ProcessViolation(string Type, string Message, string? Path = null);
public readonly record struct ProcessOutputQuery(TargetHandle<ProcessInvocation> Process, long? AfterSequence = null, int? Limit = null, bool Follow = false);
public readonly record struct ProcessOutputChunk(TargetHandle<ProcessInvocation> Process, ProcessOutputStream Stream, long Sequence, DateTimeOffset ObservedAt, ReadOnlyMemory<byte> Bytes, ProcessOutputChunkFlags Flags);
public enum ProcessOutputStream { Stdout, Stderr }
[Flags] public enum ProcessOutputChunkFlags { None = 0, Final = 1, Truncated = 2, BorrowedBuffer = 4 }
public interface IProcessOutputSink { ValueTask OnOutputAsync(ProcessOutputChunk chunk, CancellationToken cancellationToken = default); }
public interface IProcessOutputReader { IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(ProcessOutputQuery query, CancellationToken cancellationToken = default); }
public interface IProcessInvocationHandle : IAsyncDisposable { TargetHandle<ProcessInvocation> Handle { get; } ResourceRef<ProcessInvocation>? Resource { get; } ProcessInvocationSpec Spec { get; } ValueTask WriteStdinAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default); ValueTask CloseStdinAsync(CancellationToken cancellationToken = default); ValueTask SignalAsync(ProcessSignal signal, CancellationToken cancellationToken = default); ValueTask StopAsync(ProcessStopRequest request, CancellationToken cancellationToken = default); ValueTask ResizeTerminalAsync(TerminalSpec size, CancellationToken cancellationToken = default); ValueTask<ProcessInvocationResult> WaitAsync(CancellationToken cancellationToken = default); IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(CancellationToken cancellationToken = default); }

// ---------------------------------------------------------------------------
// Function sandbox lane
// ---------------------------------------------------------------------------

public sealed record FunctionSandboxSpec
{
    public required ResourceRef<ContentArtifact> GuestBinary { get; init; }
    public GuestAbiSpec? RequiredGuestAbi { get; init; }
    public FunctionSandboxConfiguration Configuration { get; init; } = FunctionSandboxConfiguration.Default;
    public IReadOnlyList<ResourceRef<AuthorityBinding>> HostFunctionBindings { get; init; } = Array.Empty<ResourceRef<AuthorityBinding>>();
    public IReadOnlyList<GuestMemoryMappingSpec> MemoryMappings { get; init; } = Array.Empty<GuestMemoryMappingSpec>();
    public FunctionSandboxSnapshotPolicy SnapshotPolicy { get; init; } = FunctionSandboxSnapshotPolicy.Default;
    public SecurityPolicy SecurityPolicy { get; init; } = SecurityPolicy.Default;
    public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = Array.Empty<ProviderExtensionData>();
}

public sealed record FunctionSandboxStatus : ResourceStatus
{
    public required FunctionSandboxPhase SandboxPhase { get; init; }
    public TargetHandle<FunctionSandbox>? Handle { get; init; }
    public ProviderOpaqueHandle? ProviderHandle { get; init; }
    public ResourceRef<ContentArtifact>? ResolvedGuestBinary { get; init; }
    public GuestAbiSpec? GuestAbi { get; init; }
    public FunctionSandboxGeneration Generation { get; init; }
    public FunctionSandboxPoisonStatus? Poison { get; init; }
    public FunctionSandboxSnapshotStatus? LastSnapshot { get; init; }
    public IReadOnlyList<FunctionBindingStatus> HostFunctions { get; init; } = Array.Empty<FunctionBindingStatus>();
    public IReadOnlyList<GuestMemoryMappingStatus> MemoryMappings { get; init; } = Array.Empty<GuestMemoryMappingStatus>();
    public IReadOnlyList<ProviderNamedEndpoint> DebugEndpoints { get; init; } = Array.Empty<ProviderNamedEndpoint>();
}

public enum FunctionSandboxPhase { Unknown, Declared, Preparing, Initializing, Ready, Invoking, Poisoned, RestoreRequired, Restoring, Degraded, Deleting, Deleted, Failed }

public sealed record FunctionSandboxConfiguration
{
    public static FunctionSandboxConfiguration Default { get; } = new();
    public ByteSize? InputBufferSize { get; init; }
    public ByteSize? OutputBufferSize { get; init; }
    public ByteSize? HeapSize { get; init; }
    public ByteSize? ScratchSize { get; init; }
    public bool EnableGuestTracing { get; init; }
    public bool EnableCrashdump { get; init; }
    public bool EnableDebugEndpoint { get; init; }
}

public sealed record FunctionSandboxSnapshotPolicy
{
    public static FunctionSandboxSnapshotPolicy Default { get; } = new();
    public bool AllowSnapshot { get; init; } = true;
    public bool SnapshotAfterInitialization { get; init; }
    public bool RestoreOnPoisonWhenAvailable { get; init; }
    public RetentionPolicy Retention { get; init; } = RetentionPolicy.Operation;
}

public sealed record FunctionSandboxPoisonStatus(bool IsPoisoned, FunctionPoisonReason Reason, bool Restorable, string? Message = null);
public enum FunctionPoisonReason { Unknown, GuestPanic, GuestAbort, InvalidMemoryAccess, StackOverflow, HeapExhaustion, CancelledByHost, HostFunctionFault, ProviderFault }
public sealed record FunctionBindingStatus(HostFunctionName Name, FunctionSignature Signature, bool Registered, bool DefaultExposed = false, RevocationVerificationStatus RevocationStatus = RevocationVerificationStatus.Unknown);

public sealed record FunctionInvocationSpec
{
    public required TargetHandle<FunctionSandbox> Sandbox { get; init; }
    public required FunctionName Function { get; init; }
    public IReadOnlyList<FunctionArgument> Arguments { get; init; } = Array.Empty<FunctionArgument>();
    public FunctionReturnType? ExpectedReturn { get; init; }
    public FunctionInvocationPolicy Policy { get; init; } = FunctionInvocationPolicy.Default;
    public bool PersistResource { get; init; }
    public ObservationRetentionPolicy ObservationRetention { get; init; } = ObservationRetentionPolicy.ResultAndDiagnostics;
    public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = Array.Empty<ProviderExtensionData>();
}

public sealed record FunctionInvocationStatus : ResourceStatus
{
    public required FunctionInvocationPhase InvocationPhase { get; init; }
    public TargetHandle<FunctionInvocation>? Handle { get; init; }
    public ResourceRef<FunctionSandbox>? Sandbox { get; init; }
    public FunctionInvocationResult? Result { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

public enum FunctionInvocationPhase { Unknown, Created, Dispatching, RunningGuest, RunningHostCallback, Completing, Completed, Cancelling, Cancelled, Failed }

public sealed record FunctionInvocationPolicy
{
    public static FunctionInvocationPolicy Default { get; } = new();
    public TimeSpan? Timeout { get; init; }
    public FunctionCancellationPolicy Cancellation { get; init; } = FunctionCancellationPolicy.Interrupt;
    public bool RestoreSandboxOnPoisonWhenPossible { get; init; }
    public bool RejectWhenSandboxPoisoned { get; init; } = true;
}

public enum FunctionCancellationPolicy { Interrupt, ProviderCancel, DiscardSandbox, ProviderDefined }

public sealed record FunctionInvocationResult
{
    public ResourceId<FunctionInvocation>? InvocationId { get; init; }
    public required FunctionInvocationCompletionKind CompletionKind { get; init; }
    public FunctionValue ReturnValue { get; init; } = FunctionValue.Void;
    public FunctionBoundaryError? BoundaryError { get; init; }
    public FunctionSandboxPoisonStatus? Poison { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public TimeSpan? Duration { get; init; }
    public IReadOnlyList<Condition> Diagnostics { get; init; } = Array.Empty<Condition>();
}

public enum FunctionInvocationCompletionKind { Returned, GuestError, HostFunctionError, HostFunctionHung, ValidationError, CancelledByHost, TimedOut, SandboxPoisoned, SandboxFault, ProviderFault }
public sealed record FunctionBoundaryError(FunctionBoundaryErrorKind Kind, string Message, HostFunctionName? HostFunction = null, ProviderExtensionData? Detail = null);
public enum FunctionBoundaryErrorKind { TypeMismatch, FunctionNotFound, HostFunctionNotRegistered, HostFunctionDenied, HostFunctionFailed, PayloadInvalid, ProviderDefined }

public sealed record FunctionSignature
{
    public required FunctionName Name { get; init; }
    public IReadOnlyList<FunctionParameter> Parameters { get; init; } = Array.Empty<FunctionParameter>();
    public FunctionReturnType ReturnType { get; init; } = FunctionReturnType.Void;
}

public sealed record FunctionParameter(string Name, FunctionValueKind Kind, bool Required = true);
public readonly record struct FunctionReturnType(FunctionValueKind Kind)
{
    public static FunctionReturnType Void { get; } = new(FunctionValueKind.Void);
}

public sealed record FunctionArgument(string Name, FunctionValue Value);

public readonly record struct FunctionValue(FunctionValueKind Kind, int Int32 = 0, uint UInt32 = 0, long Int64 = 0, ulong UInt64 = 0, float Float32 = 0, double Float64 = 0, string? String = null, bool Bool = false, ReadOnlyMemory<byte> Bytes = default)
{
    public static FunctionValue Void { get; } = new(FunctionValueKind.Void);
}

public enum FunctionValueKind { Void, Int32, UInt32, Int64, UInt64, Float32, Float64, String, Bool, Bytes }

public sealed record FunctionSandboxSnapshotSpec
{
    public required ResourceRef<FunctionSandbox> Sandbox { get; init; }
    public string? Label { get; init; }
    public RetentionPolicy Retention { get; init; } = RetentionPolicy.Operation;
    public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = Array.Empty<ProviderExtensionData>();
}

public sealed record FunctionSandboxSnapshotStatus : ResourceStatus
{
    public required FunctionSandboxSnapshotPhase SnapshotPhase { get; init; }
    public ResourceRef<FunctionSandbox>? Sandbox { get; init; }
    public FunctionSandboxGeneration SandboxGeneration { get; init; }
    public Digest? Digest { get; init; }
    public ByteSize? Size { get; init; }
    public ProviderOpaqueHandle? ProviderHandle { get; init; }
}

public enum FunctionSandboxSnapshotPhase { Pending, Capturing, Ready, Restoring, Incompatible, Released, Failed }
public sealed record FunctionSnapshotRequest(string? Label = null);
public sealed record FunctionRestoreRequest(ResourceRef<FunctionSandboxSnapshot> Snapshot, bool ClearPoison = true);

public sealed record GuestMemoryMappingSpec
{
    public required ResourceRef<ContentArtifact> Source { get; init; }
    public string? Label { get; init; }
    public ByteSize? Offset { get; init; }
    public ByteSize? Size { get; init; }
    public GuestMemoryAccess Access { get; init; } = GuestMemoryAccess.ReadOnly;
    public bool CopyOnWrite { get; init; } = true;
    public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = Array.Empty<ProviderExtensionData>();
}

public sealed record GuestMemoryMappingStatus
{
    public string? Label { get; init; }
    public GuestMemoryMappingPhase Phase { get; init; }
    public GuestMemoryAccess EffectiveAccess { get; init; }
    public ByteSize? MappedSize { get; init; }
    public ProviderOpaqueHandle? ProviderHandle { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
}

public enum GuestMemoryAccess { ReadOnly, ReadExecute, ReadWrite, ProviderDefined }
public enum GuestMemoryMappingPhase { Pending, Applied, Rejected, Released, Failed }

public readonly record struct FunctionTraceChunk(TargetHandle<FunctionSandbox> Sandbox, FunctionInvocationId? Invocation, long Sequence, DateTimeOffset ObservedAt, ReadOnlyMemory<byte> Payload, SchemaId Schema, ContentType ContentType);
public interface IFunctionObservationSink { ValueTask OnFunctionEventAsync(ExecutionEventChunk chunk, CancellationToken cancellationToken = default); }

// ---------------------------------------------------------------------------
// Artifact, root filesystem, workspace, and content projection
// ---------------------------------------------------------------------------

public sealed record PlatformSelector(string? OperatingSystem = null, string? Architecture = null, string? Variant = null);

public sealed record ArtifactReference
{
    public required string Original { get; init; }
    public string? Normalized { get; init; }
    public string? Registry { get; init; }
    public string? Repository { get; init; }
    public string? Tag { get; init; }
    public Digest? Digest { get; init; }
}

public sealed record RegistryEndpointRef(string Host, bool AllowInsecureTransport = false);
public enum ContentArtifactKind { ContainerRootfsImage, MachineBootImage, InstallMedia, FunctionGuestBinary, BootstrapArtifact, ProviderFileArtifact }
public enum ArtifactSourceKind { RegistryReference, Url, HostFile, ContentStore, ProviderArtifact }
public enum DiskImageFormat { Raw, Qcow2, Vhdx, Asif, Iso, ProviderDefined }
public enum ArtifactFormat { OciImage, DockerImage, Elf, Pe, RawDisk, Qcow2, Vhdx, Asif, Iso, Tar, Zip, ProviderDefined }

public sealed record ContentArtifactSpec
{
    public ContentArtifactKind Kind { get; init; } = ContentArtifactKind.ContainerRootfsImage;
    public required ArtifactReference Reference { get; init; }
    public ArtifactSourceKind SourceKind { get; init; } = ArtifactSourceKind.RegistryReference;
    public PlatformSelector? RequestedPlatform { get; init; }
    public GuestAbiSpec? RequestedGuestAbi { get; init; }
    public RegistryEndpointRef? RegistryEndpoint { get; init; }
    public Uri? SourceUri { get; init; }
    public string? HostFilePath { get; init; }
    public CredentialRef? CredentialRef { get; init; }
    public MachineBootArtifactOptions? MachineBoot { get; init; }
    public FunctionGuestBinaryOptions? FunctionGuest { get; init; }
    public ArtifactAvailabilityPolicy AvailabilityPolicy { get; init; } = ArtifactAvailabilityPolicy.EnsureLocal;
    public GarbageCollectionClass GarbageCollectionClass { get; init; } = GarbageCollectionClass.CacheWhileReferenced;
    public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = Array.Empty<ProviderExtensionData>();
}

public sealed record MachineBootArtifactOptions(DiskImageFormat? Format = null, ResourceRef<ContentArtifact>? Kernel = null, ResourceRef<ContentArtifact>? Initrd = null, bool RequiresConversion = false);
public sealed record FunctionGuestBinaryOptions(ArtifactFormat? Format = null, GuestAbiSpec? GuestAbi = null, SemanticVersion? MinimumProviderVersion = null, bool RequireCompatibilityNote = true);
public enum ArtifactAvailabilityPolicy { UseLocalOnly, PullIfMissing, EnsureLocal, ImportOnly }
public enum ContentArtifactPhase { Pending, Resolving, Validating, Available, PartiallyAvailable, Unavailable, Deleting, Failed }
public enum BlobAvailability { Unknown, RemoteOnly, Local, Missing, Degraded }
public sealed record ArtifactDescriptor(Digest Digest, MediaType MediaType, ByteSize Size, ArtifactFormat? Format = null);
public sealed record ArtifactVariant(PlatformSpec? Platform, GuestAbiSpec? GuestAbi, ArtifactDescriptor Descriptor);
public sealed record ContentBlobDescriptor(Digest Digest, MediaType MediaType, ByteSize Size, BlobAvailability Availability);
public sealed record ResourceUsageSummary { public ByteSize LogicalSize { get; init; } public ByteSize PhysicalSize { get; init; } public ByteSize ReclaimableSize { get; init; } public int RefCount { get; init; } }
public sealed record ContentArtifactStatus : ResourceStatus { public ContentArtifactPhase ArtifactPhase { get; init; } public ContentArtifactKind Kind { get; init; } public ArtifactDescriptor? ResolvedDescriptor { get; init; } public ArtifactVariant? SelectedVariant { get; init; } public IReadOnlyList<ArtifactVariant> Variants { get; init; } = Array.Empty<ArtifactVariant>(); public IReadOnlyList<ContentBlobDescriptor> Blobs { get; init; } = Array.Empty<ContentBlobDescriptor>(); public FunctionGuestBinaryStatus? FunctionGuest { get; init; } public ResourceUsageSummary Usage { get; init; } = new(); public IReadOnlyList<ResourceRef<RootFilesystemView>> ActiveRootfsRefs { get; init; } = Array.Empty<ResourceRef<RootFilesystemView>>(); public ProviderOpaqueHandle? ProviderHandle { get; init; } }
public sealed record FunctionGuestBinaryStatus(GuestAbiSpec? GuestAbi, bool Compatible, SemanticVersion? GuestRuntimeVersion = null, SemanticVersion? RequiredHostVersion = null, IReadOnlyList<FunctionSignature>? ExportedFunctions = null, string? IncompatibilityReason = null);

public sealed record RootFilesystemViewSpec { public required ResourceRef<ContentArtifact> Image { get; init; } public PlatformSelector? RequiredPlatform { get; init; } public ResourceRef<RuntimeHost>? Host { get; init; } public ResourceRef<ExecutionUnit>? Unit { get; init; } public RootfsAccessMode AccessMode { get; init; } = RootfsAccessMode.ReadOnlyBaseWithWritableOverlay; public RootfsReusePolicy ReusePolicy { get; init; } = RootfsReusePolicy.ShareBaseLayers; public RootfsExportPolicy ExportPolicy { get; init; } = RootfsExportPolicy.DoNotExport; public GarbageCollectionClass GarbageCollectionClass { get; init; } = GarbageCollectionClass.CacheWhileReferenced; public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = Array.Empty<ProviderExtensionData>(); }
public enum RootfsAccessMode { ReadOnly, ReadOnlyBaseWithWritableOverlay, WritablePrivateCopy }
public enum RootfsReusePolicy { ProviderDefault, ShareBaseLayers, PrivateCopy, NoReuse }
public enum RootfsExportPolicy { DoNotExport, ExportWritableLayerOnFinalize, ExportFullRootfsOnFinalize }
public enum RootFilesystemViewPhase { Pending, ResolvingArtifact, PreparingBaseLayers, CreatingWritableLayer, Materialized, Degraded, Finalizing, Released, Failed }
public enum RootfsViewKind { ProviderNamespaceHandle, GuestMount, BlockDevice, Snapshot, Overlay }
public sealed record RealizedRootfsView(RootfsViewKind Kind, ProviderOpaqueHandle? ProviderHandle = null, GuestPath? GuestMountPath = null, bool IsWritable = false);
public sealed record RootfsLayerRef(Digest Digest, ProviderOpaqueHandle? ProviderHandle = null, bool IsShared = true, ByteSize Size = default);
public sealed record RootFilesystemViewStatus : ResourceStatus { public RootFilesystemViewPhase RootfsPhase { get; init; } public ResourceRef<ContentArtifact>? ResolvedArtifact { get; init; } public ArtifactVariant? SelectedVariant { get; init; } public RealizedRootfsView? View { get; init; } public IReadOnlyList<RootfsLayerRef> BaseLayers { get; init; } = Array.Empty<RootfsLayerRef>(); public RootfsLayerRef? WritableLayer { get; init; } public ResourceUsageSummary Usage { get; init; } = new(); public ProviderOpaqueHandle? ProviderHandle { get; init; } }

public sealed record WorkspaceSpec { public required WorkspaceScope Scope { get; init; } public required string Owner { get; init; } public IReadOnlyList<WorkspacePartitionSpec> Partitions { get; init; } = Array.Empty<WorkspacePartitionSpec>(); public RetentionPolicy Retention { get; init; } = RetentionPolicy.Runtime; public ConflictPolicy ConflictPolicy { get; init; } = ConflictPolicy.RecordConflict; public FinalizationPolicy FinalizationPolicy { get; init; } = FinalizationPolicy.Explicit; public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = Array.Empty<ProviderExtensionData>(); }
public enum WorkspaceScope { Global, Project, Agent, Runtime }
public sealed record WorkspacePartitionSpec { public required ContentProjectionRole Role { get; init; } public required ScopedContentName Name { get; init; } public GuestPath DefaultGuestPath { get; init; } public AccessMode DefaultAccess { get; init; } public SyncPolicy DefaultSync { get; init; } = SyncPolicy.None; public RetentionPolicy Retention { get; init; } = RetentionPolicy.Runtime; }
public enum WorkspacePhase { Pending, Active, Degraded, Finalizing, Finalized, Failed }
public sealed record WorkspaceContentSummary(int EntryCount = 0, ByteSize LogicalSize = default, DateTimeOffset? LastChangedAt = null, Digest? ManifestDigest = null);
public sealed record WorkspaceConflict(string Path, ConflictKind Kind, string? Description = null);
public enum ConflictKind { ConcurrentWrite, DeleteModify, PolicyViolation, ProviderConflict }
public sealed record WorkspaceStatus : ResourceStatus { public WorkspacePhase WorkspacePhase { get; init; } public long Version { get; init; } public WorkspaceContentSummary Summary { get; init; } = new(); public IReadOnlyList<WorkspaceConflict> Conflicts { get; init; } = Array.Empty<WorkspaceConflict>(); public IReadOnlyList<ResourceRef<ContentProjection>> ActiveProjections { get; init; } = Array.Empty<ResourceRef<ContentProjection>>(); public ProviderOpaqueHandle? ProviderHandle { get; init; } }

public sealed record ProjectionView { public required ProjectionViewKind Kind { get; init; } public GuestPath? GuestPath { get; init; } public string? ApiHandleName { get; init; } public string? StreamName { get; init; } }
public enum ProjectionViewKind { FilesystemTree, SingleFile, Stream, ContentApi, ProviderTransferEndpoint }
public enum ProjectionRealizationKind { ProviderDefault, LiveProjection, CopyIn, CopyOut, SyncMirror, ProviderTransferEndpoint, ProviderDefined }
public enum ProjectionWriteEffect { Unknown, NoWrites, DirectSourceMutation, StagedTargetWrite, CopyOnWrite, AppendOnlyArtifact, FinalizePromote, ProviderDefined }
public enum CoherenceClass { Unknown, Strong, CloseToOpen, Eventual, ManualRefresh, ProviderDefined }
public enum CacheBehavior { Unknown, None, ReadCache, WriteBack, WriteThrough, ProviderDefined }
public enum SymlinkPolicy { Preserve, Follow, Deny, ProviderDefault }
public enum IdentityMappingPolicy { Preserve, CurrentUser, FixedUser, ProviderDefault }
public enum ReadOnlyEnforcementPolicy { ProviderDefault, HostEnforced, GuestEnforced, BestEffort, Required }
public enum ProjectionFallbackReason { Unsupported, PermissionDenied, HostPathUnavailable, ProviderDegraded, PolicyDenied, ProviderDefined }
public enum FileEventDirection { SourceToTarget, TargetToSource, Bidirectional }
[Flags] public enum FileEventMask { None = 0, Create = 1, Modify = 2, Delete = 4, Rename = 8, Attribute = 16, All = 31 }
public enum FileEventBridgePhase { Disabled, Pending, Ready, Degraded, Failed, Unsupported }
public enum ContentProjectionDegradedFeature { HostPath, LiveProjection, FileEvents, Coherence, Cache, SymlinkPolicy, IdentityMapping, ReadOnlyEnforcement, Finalization, SecurityPolicy, ProviderHealth }

public sealed record ProjectionRealizationSpec
{
    public static ProjectionRealizationSpec ProviderDefault { get; } = new();
    public ProjectionRealizationKind Kind { get; init; } = ProjectionRealizationKind.ProviderDefault;
    public ProjectionWriteEffect WriteEffect { get; init; } = ProjectionWriteEffect.Unknown;
    public CoherenceClass RequestedCoherence { get; init; } = CoherenceClass.Unknown;
    public CacheBehavior Cache { get; init; } = CacheBehavior.Unknown;
    public ProjectionFallbackPolicy Fallback { get; init; } = ProjectionFallbackPolicy.Default;
    public FileEventBridgeSpec? FileEvents { get; init; }
}

public sealed record ProjectionFallbackPolicy { public static ProjectionFallbackPolicy Default { get; } = new(); public bool AllowFallback { get; init; } = true; public ProjectionRealizationKind? PreferredFallback { get; init; } }
public sealed record FileEventBridgeSpec(FileEventDirection Direction, FileEventMask Mask, bool BestEffort = true);
public sealed record ContentProjectionSecurityPolicy { public static ContentProjectionSecurityPolicy Default { get; } = new(); public bool AllowHostPathSource { get; init; } public bool AllowDirectSourceMutation { get; init; } public bool RequireAuditForHostWrites { get; init; } = true; public ReadOnlyEnforcementPolicy ReadOnlyEnforcement { get; init; } = ReadOnlyEnforcementPolicy.Required; }
public sealed record EffectiveSymlinkPolicy(SymlinkPolicy Policy, string? Detail = null);
public sealed record EffectiveIdentityMapping(IdentityMappingPolicy Policy, string? User = null, string? Group = null);
public sealed record ReadOnlyEnforcementStatus(ReadOnlyEnforcementPolicy Policy, bool Enforced, string? Detail = null);
public sealed record FileEventBridgeStatus(FileEventBridgePhase Phase, FileEventDirection Direction, FileEventMask Mask, bool MayBePartial = false, string? Detail = null);
public sealed record ProjectionFallbackStatus(bool Used, ProjectionFallbackReason? Reason = null, ProjectionRealizationKind? Selected = null, string? Detail = null);
public sealed record ContentProjectionLimitation(ContentProjectionDegradedFeature Feature, CapabilityDegradationMode Mode, string ReasonCode, string? Message = null);

public sealed record RealizedProjectionView
{
    public required ProjectionViewKind Kind { get; init; }
    public GuestPath? GuestPath { get; init; }
    public AccessMode EffectiveAccess { get; init; }
    public ProjectionRealizationKind EffectiveRealization { get; init; } = ProjectionRealizationKind.ProviderDefault;
    public ProjectionWriteEffect EffectiveWriteEffect { get; init; } = ProjectionWriteEffect.Unknown;
    public CoherenceClass EffectiveCoherence { get; init; } = CoherenceClass.Unknown;
    public CacheBehavior EffectiveCache { get; init; } = CacheBehavior.Unknown;
    public EffectiveSymlinkPolicy? EffectiveSymlinkPolicy { get; init; }
    public EffectiveIdentityMapping? EffectiveIdentityMapping { get; init; }
    public ReadOnlyEnforcementStatus? ReadOnlyEnforcement { get; init; }
    public FileEventBridgeStatus? FileEvents { get; init; }
    public ProjectionFallbackStatus? Fallback { get; init; }
    public ProviderOpaqueHandle? ProviderHandle { get; init; }
    public IReadOnlyList<ContentProjectionLimitation> Limitations { get; init; } = Array.Empty<ContentProjectionLimitation>();
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
}

public sealed record ContentProjectionSpec
{
    public required ContentSelector Source { get; init; }
    public required ContentProjectionTarget Target { get; init; }
    public required ProjectionView View { get; init; }
    public required ContentProjectionRole Role { get; init; }
    public AccessMode AccessMode { get; init; } = AccessMode.ReadOnly;
    public SyncPolicy SyncPolicy { get; init; } = SyncPolicy.None;
    public ContentProjectionLifecycle Lifecycle { get; init; } = ContentProjectionLifecycle.ExecutionUnit;
    public FinalizationPolicy FinalizationPolicy { get; init; } = FinalizationPolicy.Explicit;
    public ProjectionRealizationSpec Realization { get; init; } = ProjectionRealizationSpec.ProviderDefault;
    public ContentProjectionSecurityPolicy SecurityPolicy { get; init; } = ContentProjectionSecurityPolicy.Default;
    public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = Array.Empty<ProviderExtensionData>();
}

public sealed record ContentProjectionTarget { public ResourceRef<RuntimeHost>? Host { get; init; } public ResourceRef<ExecutionUnit>? Unit { get; init; } public string? TargetName { get; init; } }
public enum ContentProjectionRole { Workspace, Uploads, Artifacts, Knowledge, Memory, Scratch, Cache, ToolInput, ToolOutput }
public enum AccessMode { ReadOnly, ReadWrite, CopyOnWrite, AppendOnly, WriteOnly }
public enum ContentProjectionLifecycle { Invocation, Process, ExecutionUnit, Runtime, ExplicitRetain, ExplicitDelete }
public enum ContentProjectionPhase { Pending, Projecting, Projected, Syncing, Degraded, Finalizing, Finalized, Releasing, Released, Failed }
public sealed record ContentProjectionChangeSummary(int Created = 0, int Modified = 0, int Deleted = 0, int Conflicted = 0, Digest? ManifestDigest = null);
public sealed record ContentProjectionStatus : ResourceStatus { public ContentProjectionPhase ProjectionPhase { get; init; } public IReadOnlyList<RealizedProjectionView> Views { get; init; } = Array.Empty<RealizedProjectionView>(); public ContentProjectionChangeSummary ChangeSummary { get; init; } = new(); public SyncCheckpoint? LastSync { get; init; } public FinalizationResult? LastFinalization { get; init; } public ResourceUsageSummary Usage { get; init; } = new(); public ProviderOpaqueHandle? ProviderHandle { get; init; } }

public sealed record ContentSelector { public required ContentSelectorKind Kind { get; init; } public ResourceRef<Workspace>? Workspace { get; init; } public ContentProjectionRole? WorkspaceRole { get; init; } public string? PathPrefix { get; init; } public ContentStoreSelection? ContentStore { get; init; } public ScratchSelection? Scratch { get; init; } public ProviderSourceSelection? ProviderSource { get; init; } public HostPathSelection? HostPath { get; init; } public IReadOnlyList<ContentFilter> Filters { get; init; } = Array.Empty<ContentFilter>(); }
public enum ContentSelectorKind { WorkspaceRole, WorkspacePath, ContentStoreQuery, Scratch, ProviderSource, HostPath }
public sealed record HostPathSelection(HostPath Path, HostPathKind Kind = HostPathKind.Directory, bool RequireExists = true);
public enum HostPathKind { File, Directory, Socket, Any }
public sealed record ContentStoreSelection(string? Scope = null, string? Kind = null, string? Name = null, string? ContentType = null, IReadOnlyDictionary<string, string>? Tags = null);
public sealed record ContentFilter(ContentFilterKind Kind, string? Value = null, DateTimeOffset? Since = null);
public enum ContentFilterKind { NameEquals, PathPrefix, ContentTypeEquals, TagEquals, CreatedAfter, ModifiedAfter }
public sealed record ScratchSelection(ByteSize SizeHint = default, bool MemoryBackedPreferred = false);
public sealed record ProviderSourceSelection(ProviderId Provider, string SourceId, ProviderExtensionData? Data = null);
public readonly record struct SyncPolicy(SyncMode Mode, SyncDirection Direction, ConflictPolicy ConflictPolicy, bool IncludeDeletes) { public static readonly SyncPolicy None = new(SyncMode.None, SyncDirection.None, ConflictPolicy.RecordConflict, false); public static readonly SyncPolicy InitialOnly = new(SyncMode.InitialOnly, SyncDirection.SourceToTarget, ConflictPolicy.RecordConflict, false); public static readonly SyncPolicy OnFinalize = new(SyncMode.OnFinalize, SyncDirection.TargetToSource, ConflictPolicy.RecordConflict, true); public static readonly SyncPolicy ManualPromote = new(SyncMode.Manual, SyncDirection.TargetToSource, ConflictPolicy.RequireExplicitPromotion, true); }
public enum SyncMode { None, InitialOnly, Manual, OnFinalize, Continuous }
public enum SyncDirection { None, SourceToTarget, TargetToSource, Bidirectional }
public enum ConflictPolicy { Fail, RecordConflict, PreferSource, PreferTarget, RequireExplicitPromotion }
public enum FinalizationPolicy { None, Explicit, Required, OnExecutionUnitStop, OnRuntimeEnd, PromoteExplicitly }
public enum RetentionPolicy { Operation, Invocation, Process, ExecutionUnit, Runtime, Project, ExplicitRetain }
public sealed record SyncRequest { public SyncMode? OverrideMode { get; init; } public SyncDirection? OverrideDirection { get; init; } public ConflictPolicy? OverrideConflictPolicy { get; init; } public bool DryRun { get; init; } }
public sealed record SyncCheckpoint(long Version, DateTimeOffset CompletedAt, Digest? SourceManifestDigest = null, Digest? TargetManifestDigest = null, ContentProjectionChangeSummary? Changes = null);
public sealed record SyncResult(SyncCheckpoint Checkpoint, IReadOnlyList<WorkspaceConflict>? Conflicts = null, IReadOnlyList<Condition>? Conditions = null);
public sealed record FinalizationRequest { public FinalizationKind Kind { get; init; } = FinalizationKind.ManifestAndChangedContent; public bool IncludeProvenance { get; init; } = true; public bool IncludeDeletedEntries { get; init; } = true; public string? ProducerId { get; init; } }
public enum FinalizationKind { ManifestOnly, ChangedContent, ManifestAndChangedContent, Snapshot, PublishArtifacts, CommitWorkspace, PromoteMemory }
public sealed record FinalizationResult { public required DateTimeOffset CompletedAt { get; init; } public Digest? ManifestDigest { get; init; } public ResourceRef<Workspace>? DestinationWorkspace { get; init; } public IReadOnlyList<FinalizedContentRef> Content { get; init; } = Array.Empty<FinalizedContentRef>(); public IReadOnlyList<WorkspaceConflict> Conflicts { get; init; } = Array.Empty<WorkspaceConflict>(); public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>(); }
public sealed record FinalizedContentRef(string Path, string ContentId, Digest? Digest, ByteSize Size, ContentProjectionRole Role);
public readonly record struct ContentProjectionEntry(ContentProjectionEntryKind Kind, GuestPath Path, ByteSize Size, Digest? Digest, DateTimeOffset? LastModifiedAt);
public enum ContentProjectionEntryKind { File, Directory, Symlink, Socket, ProviderDefined }
public interface IContentProjectionEntrySink { ValueTask OnEntryAsync(ContentProjectionEntry entry, CancellationToken cancellationToken = default); }
public sealed record ContentEnumerationPage { public IReadOnlyList<ContentEntry> Entries { get; init; } = Array.Empty<ContentEntry>(); public ContentPageCursor Next { get; init; } public bool HasMore { get; init; } }
public readonly record struct ContentEntry(string Path, string? ContentId, MediaType? MediaType, ByteSize Size, Digest? Digest, DateTimeOffset? LastModified);
public sealed record ContentProjectionFileEvent(ResourceRef<ContentProjection> Projection, FileEventKind Kind, GuestPath Path, DateTimeOffset ObservedAt, bool MayBePartial = false);
public enum FileEventKind { Created, Modified, Deleted, Renamed, AttributeChanged, ProviderDefined }

// ---------------------------------------------------------------------------
// Network, discovery, endpoint, and authority binding
// ---------------------------------------------------------------------------

public readonly record struct NetworkAddressAssignment(IpAddressValue Address, byte PrefixLength, AddressAssignmentKind Kind, bool IsPrimary);
public readonly record struct IpAddressValue(NetworkAddressFamily Family, ulong HighBits, ulong LowBits);
public readonly record struct IpCidr(IpAddressValue Address, byte PrefixLength);
public readonly record struct SocketEndpoint(UnixSocketPath Path, UnixSocketPermissions? Permissions);
public enum NetworkAddressFamily { IPv4, IPv6 }
public enum NetworkTransport { Tcp, Udp, UnixStream, UnixDatagram, NamedPipe, ProviderDefined }
public enum AddressAssignmentKind { Dynamic, StaticRequested, StaticReserved, ProviderAssigned }
public enum NetworkScope { Host, Runtime, ExecutionUnit, Project, Shared, ProviderDefined }
public enum NetworkConnectivityIntent { Isolated, NatEgress, PeerReachable, Routed, ProviderDefined }
[Flags] public enum AddressFamilyRequirement { None = 0, IPv4Optional = 1, IPv4Required = 2, IPv6Optional = 4, IPv6Required = 8 }
public enum NetworkPhase { Pending, Creating, Ready, Degraded, Failed, Deleting, Deleted }
public readonly record struct NetworkIdentityKey(string Value);

public sealed record NetworkSpec
{
    public NetworkIdentityKey? ReconciliationKey { get; init; }
    public required NetworkScope Scope { get; init; }
    public required NetworkConnectivityIntent ConnectivityIntent { get; init; }
    public required AddressFamilyRequirement AddressFamilies { get; init; }
    public IReadOnlyList<IpCidr> CidrHints { get; init; } = Array.Empty<IpCidr>();
    public NetworkDiscoveryPolicy DiscoveryPolicy { get; init; } = new();
    public NetworkExposurePolicy ExposurePolicy { get; init; } = new();
    public ProviderId? PreferredProvider { get; init; }
    public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = Array.Empty<ProviderExtensionData>();
}
public sealed record NetworkRealizationIdentity(
    ScopedName Name,
    string OpaqueId);
public sealed record NetworkRealizationContext(
    ResourceRef<ExecutionUnit> OwnerExecutionUnit,
    ResourceRef<AuthorityBinding> EngineAuthority);
public sealed record NetworkStatus : ResourceStatus
{
    public required NetworkPhase NetworkPhase { get; init; }
    public NetworkCapabilitySet RealizedCapabilities { get; init; }
    public ResourceRef<ProviderActivation>? ProviderActivation { get; init; }
    public NetworkRealizationIdentity? Realization { get; init; }
    public IReadOnlyList<IpCidr> Subnets { get; init; } = Array.Empty<IpCidr>();
    public IReadOnlyList<IpAddressValue> Gateways { get; init; } = Array.Empty<IpAddressValue>();
    public IReadOnlyList<NetworkLimitation> Limitations { get; init; } = Array.Empty<NetworkLimitation>();
    public TargetHandle<Network>? Handle { get; init; }
}
public sealed record NetworkMembershipSpec { public required ResourceRef<Network> Network { get; init; } public required NetworkMembershipTarget Target { get; init; } public ScopedName? Hostname { get; init; } public IReadOnlyList<ScopedName> Aliases { get; init; } = Array.Empty<ScopedName>(); public IReadOnlyList<ServiceName> ServiceNames { get; init; } = Array.Empty<ServiceName>(); public IpAddressValue? RequestedAddress { get; init; } public MacAddressValue? RequestedMacAddress { get; init; } public ushort? RequestedMtu { get; init; } public MembershipConnectivityPolicy ConnectivityPolicy { get; init; } = new(); public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = Array.Empty<ProviderExtensionData>(); }
public sealed record NetworkMembershipStatus : ResourceStatus { public required NetworkMembershipPhase MembershipPhase { get; init; } public NetworkEndpointHandle? EndpointHandle { get; init; } public IReadOnlyList<NetworkAddressAssignment> Addresses { get; init; } = Array.Empty<NetworkAddressAssignment>(); public IReadOnlyList<IpAddressValue> Gateways { get; init; } = Array.Empty<IpAddressValue>(); public string? InterfaceName { get; init; } public MacAddressValue? MacAddress { get; init; } public ushort? Mtu { get; init; } public IReadOnlyList<DiscoveryRecord> RegisteredRecords { get; init; } = Array.Empty<DiscoveryRecord>(); public IReadOnlyList<NetworkLimitation> Limitations { get; init; } = Array.Empty<NetworkLimitation>(); public TargetHandle<NetworkMembership>? Handle { get; init; } }
public enum NetworkMembershipPhase { Pending, Allocating, Realizing, Ready, Degraded, Failed, Releasing, Released }
public readonly record struct NetworkMembershipTarget(NetworkMembershipTargetKind Kind, TargetHandle<RuntimeHost>? Host, TargetHandle<ExecutionUnit>? Unit, TargetHandle<ProcessInvocation>? Process);
public enum NetworkMembershipTargetKind { RuntimeHost, ExecutionUnit, ProcessInvocation, ProviderDefined }
public sealed record NetworkDiscoveryPolicy { public bool EnableInternalDns { get; init; } = true; public bool RequestHostDnsExport { get; init; } public bool RequestHostResolverImport { get; init; } public IReadOnlyList<DnsName> SearchDomains { get; init; } = Array.Empty<DnsName>(); public TimeSpan? DefaultTtl { get; init; } }
public sealed record NetworkExposurePolicy { public bool AllowPublishedEndpoints { get; init; } public bool AllowHostVisibleAddresses { get; init; } public bool RequireExplicitPublication { get; init; } = true; }
public sealed record MembershipConnectivityPolicy { public bool RequirePeerConnectivity { get; init; } public bool RequireEgress { get; init; } public bool RequireStaticAddress { get; init; } public bool AllowDegradedAddressFamilies { get; init; } = true; }

public sealed record ServiceDiscoverySpec { public required DiscoveryScope Scope { get; init; } public ResourceRef<Network>? Network { get; init; } public ResourceRef<RuntimeHost>? Host { get; init; } public DefaultDiscoveryRecordPolicy DefaultRecordPolicy { get; init; } = DefaultDiscoveryRecordPolicy.MembershipHostnames; public IReadOnlyList<DiscoveryRecordSpec> Records { get; init; } = Array.Empty<DiscoveryRecordSpec>(); public IReadOnlyList<DnsName> SearchDomains { get; init; } = Array.Empty<DnsName>(); public TimeSpan? DefaultTtl { get; init; } public bool RequestHostExport { get; init; } public bool RequestHostResolverImport { get; init; } }
public sealed record ServiceDiscoveryStatus : ResourceStatus { public required ServiceDiscoveryPhase DiscoveryPhase { get; init; } public DiscoveryCapabilitySet RealizedCapabilities { get; init; } public IReadOnlyList<DiscoveryRecord> Records { get; init; } = Array.Empty<DiscoveryRecord>(); public IReadOnlyList<DnsName> HostExportedDomains { get; init; } = Array.Empty<DnsName>(); public IReadOnlyList<ProviderNamedEndpoint> EffectiveResolvers { get; init; } = Array.Empty<ProviderNamedEndpoint>(); public IReadOnlyList<NetworkLimitation> Limitations { get; init; } = Array.Empty<NetworkLimitation>(); public TargetHandle<ServiceDiscovery>? Handle { get; init; } }
public enum DiscoveryScope { Network, Runtime, HostExported, HostResolverImported, ProviderDefined }
public enum DefaultDiscoveryRecordPolicy { None, MembershipHostnames, MembershipHostnamesAndAliases, ExplicitOnly }
public enum ServiceDiscoveryPhase { Pending, Configuring, Ready, Degraded, Failed, Disabled }
public sealed record DiscoveryRecordSpec(DnsName Name, DiscoveryRecordKind Kind, DiscoveryRecordTarget Target, TimeSpan? Ttl = null);
public sealed record DiscoveryRecord(DnsName Name, DiscoveryRecordKind Kind, DiscoveryRecordTarget Target, TimeSpan Ttl, bool IsDerivedFromMembership = false);
public readonly record struct DiscoveryRecordTarget(IpAddressValue? Address, ServiceName? ServiceName, ResourceRef<NetworkMembership>? Membership, NetworkPort? Port, NetworkTransport? Transport, DnsName? CanonicalName);
public enum DiscoveryRecordKind { A, AAAA, CName, Service, Text, ProviderDefined }

public sealed record PublishedEndpointSpec { public required EndpointListenerSpec Listener { get; init; } public required EndpointRouteTarget Target { get; init; } public EndpointExposurePolicy ExposurePolicy { get; init; } = new(); public EndpointAuthorizationPolicy AuthorizationPolicy { get; init; } = EndpointAuthorizationPolicy.None; public SensitiveEndpointPolicy? SensitivePolicy { get; init; } public ResourceRef<Network>? RoutingNetwork { get; init; } public ResourceRef<RuntimeHost>? RoutingHost { get; init; } public bool ReconcileRouteOnTargetRestart { get; init; } = true; public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = Array.Empty<ProviderExtensionData>(); }
public sealed record PublishedEndpointStatus : ResourceStatus { public required PublishedEndpointPhase EndpointPhase { get; init; } public BoundEndpoint? BoundListener { get; init; } public EndpointRouteStatus? Route { get; init; } public EndpointPublicationOrigin PublicationOrigin { get; init; } = EndpointPublicationOrigin.Explicit; public IReadOnlyList<NetworkLimitation> Limitations { get; init; } = Array.Empty<NetworkLimitation>(); public TargetHandle<PublishedEndpoint>? RouterHandle { get; init; } }
public enum PublishedEndpointPhase { Pending, Binding, Bound, Degraded, Failed, Releasing, Released, Suppressed }
public enum EndpointPublicationOrigin { Explicit, StaticConfigured, AutomaticObserved, ProviderDefault, SuppressedByPolicy }
public enum EndpointListenerKind { HostAddress, RuntimeGateway, NetworkGateway, UnixSocket, NamedPipe, ProviderDefined }
public enum EndpointTargetKind { NetworkMembership, UnitPort, ProcessPort, ServiceName, UnixSocket, NetworkAddress, ProviderDefined }
public enum EndpointExposureScope { HostLocal, HostLan, RuntimeOnly, NetworkOnly, External }
public readonly record struct EndpointListenerSpec(EndpointListenerKind Kind, NetworkTransport Transport, IpAddressValue? Address, PortRange? Ports, SocketEndpoint? Socket);
public readonly record struct BoundEndpoint(EndpointListenerKind Kind, NetworkTransport Transport, IpAddressValue? Address, PortRange? Ports, SocketEndpoint? Socket);
public readonly record struct EndpointRouteTarget(EndpointTargetKind Kind, ResourceRef<NetworkMembership>? Membership, ResourceRef<ExecutionUnit>? Unit, ResourceRef<ProcessInvocation>? Process, ServiceName? ServiceName, NetworkTransport Transport, NetworkPort? Port, UnixSocketPath? SocketPath, IpAddressValue? Address = null);
public readonly record struct EndpointRouteStatus(EndpointRouteTarget Target, NetworkEndpointHandle? ResolvedEndpoint, IpAddressValue? ResolvedAddress, NetworkPort? ResolvedPort, UnixSocketPath? ResolvedSocketPath);
public sealed record EndpointExposurePolicy { public EndpointExposureScope Scope { get; init; } = EndpointExposureScope.HostLocal; public bool RequireStableListener { get; init; } public bool AllowEphemeralPort { get; init; } }
public sealed record EndpointAuthorizationPolicy { public static EndpointAuthorizationPolicy None { get; } = new(); public bool RequireLoopbackClient { get; init; } public string? TokenAudience { get; init; } public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = Array.Empty<ProviderExtensionData>(); }

public enum BoundaryLocus { Host, Provider, RuntimeHost, ExecutionUnit, ProcessInvocation, FunctionSandbox, Network, External }
public enum AuthorityBindingDirection { HostToGuest, GuestToHost, ProviderToHost, ProviderToGuest, HostToFunctionGuest, FunctionGuestToHost, Bidirectional }
public enum SensitiveAuthorityClass { None, CredentialDelegation, TrustMutation, PrivilegedDaemonControl, RootfulEngineControl, RootlessEngineControl, NetworkPrivilegedHelper, HostFilesystemWrite, HostFunctionCallback, HostStateRead, HostStateWrite, NetworkViaHostFunction, CredentialUseViaHostFunction, HpdResourceMutation, DebugControl, ProviderDefined }
public enum SensitiveRedactionLevel { None, RedactSecretValues, RedactIdentifiers, RedactAll }
public enum SensitiveEndpointKind { CredentialProxy, EngineSocket, TrustService, SshAgent, HostDaemonControl, FunctionDebug, ProviderDefined }
public enum RevocationVerificationStatus { Unknown, Pending, Verified, Failed, NotSupported }
public enum AuthorityBindingKind { HostService, HostFunction, GuestCapability, Credential, TrustAnchor, PublishedEndpoint, ProviderDefined }

public sealed record SensitiveLeasePolicy { public BindingLifetime Lifetime { get; init; } = BindingLifetime.ExecutionUnit; public TimeSpan? ExpiresAfter { get; init; } public bool RevokeOnTargetStop { get; init; } = true; public bool SurviveTargetRestart { get; init; } public TimeSpan? RevocationGracePeriod { get; init; } }
public sealed record SensitiveProvenance(string? Source = null, string? Actor = null, string? Reason = null);
public sealed record SensitiveEndpointPolicy { public SensitiveEndpointKind Kind { get; init; } = SensitiveEndpointKind.ProviderDefined; public SensitiveAuthorityClass AuthorityClass { get; init; } = SensitiveAuthorityClass.ProviderDefined; public SensitiveLeasePolicy Lease { get; init; } = new(); public SensitiveRedactionLevel Redaction { get; init; } = SensitiveRedactionLevel.RedactSecretValues; public bool RequireAudit { get; init; } = true; public bool RequireExplicitUserApproval { get; init; } }

public sealed record AuthorityBindingSpec { public required AuthorityBindingKind Kind { get; init; } public required AuthorityBindingSource Source { get; init; } public required AuthorityBindingTarget Target { get; init; } public required AuthorityBindingProjection Projection { get; init; } public AuthorityBindingPolicy Policy { get; init; } = new(); public string? AuditLabel { get; init; } public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = Array.Empty<ProviderExtensionData>(); }
public sealed record AuthorityBindingStatus : ResourceStatus { public required AuthorityBindingPhase BindingPhase { get; init; } public BoundAuthority? BoundAuthority { get; init; } public IReadOnlyList<NetworkLimitation> Limitations { get; init; } = Array.Empty<NetworkLimitation>(); public TargetHandle<AuthorityBinding>? ProviderHandle { get; init; } }
public enum AuthorityBindingPhase { Pending, Projecting, Projected, Degraded, Revoking, Revoked, Failed }
public enum AuthoritySourceKind { HostService, UnixSocket, Credential, Secret, ProviderCapability, TrustAnchor, PublishedEndpoint, HostFunction, ProviderDefined }
public enum HostServiceKind { SshAgent, DockerDaemon, PodmanDaemon, ContainerdDaemon, BuildKitDaemon, KubernetesApi, GitCredentialHelper, TlsTrustService, TrustAnchorStore, HttpProxy, SocksProxy, HostResolver, DisplayServer, ProviderDefined }
public enum AuthorityProjectionKind { SocketPath, EnvironmentReference, FileDescriptor, ProxyEndpoint, AgentProtocol, TrustStore, TypedCallback, ProviderDefined }
public enum AuthorityTargetKind { ExecutionUnit, ProcessInvocation, Service, FunctionSandbox, ProviderDefined }
public enum BindingLifetime { Operation, Invocation, Process, ExecutionUnit, Runtime, FunctionSandbox }
public sealed record TrustAnchorBindingProfile(string Name, bool MutatesGuestTrustStore = true, bool RequireRollback = true);
public sealed record HostFunctionBindingProfile(HostFunctionName Name, FunctionSignature Signature, bool DefaultExposed = false, HostFunctionRevocationMode RevocationMode = HostFunctionRevocationMode.ProviderDefault);
public enum HostFunctionRevocationMode { RemoveRegistration, DenyWrapper, RotateGeneration, RecreateSandbox, ProviderDefault }
public sealed record AuthorityBindingSource { public required AuthoritySourceKind Kind { get; init; } public BoundaryLocus Locus { get; init; } = BoundaryLocus.Host; public HostServiceKind? HostService { get; init; } public UnixSocketPath? SocketPath { get; init; } public CredentialRef? Credential { get; init; } public string? SecretRef { get; init; } public string? ProviderCapabilityName { get; init; } public ResourceRef<PublishedEndpoint>? PublishedEndpoint { get; init; } public TrustAnchorBindingProfile? TrustAnchor { get; init; } public HostFunctionBindingProfile? HostFunction { get; init; } }
public readonly record struct AuthorityBindingTarget(AuthorityTargetKind Kind, TargetHandle<ExecutionUnit>? Unit = null, TargetHandle<ProcessInvocation>? Process = null, TargetHandle<FunctionSandbox>? FunctionSandbox = null, ServiceName? ServiceName = null, BoundaryLocus Locus = BoundaryLocus.ExecutionUnit, ProviderEndpoint? HostEndpoint = null);
public sealed record AuthorityBindingProjection { public required AuthorityProjectionKind Kind { get; init; } public UnixSocketPath? TargetSocketPath { get; init; } public string? EnvironmentVariableName { get; init; } public UnixSocketPermissions? SocketPermissions { get; init; } public bool ReadOnly { get; init; } = true; public FunctionSignature? CallbackSignature { get; init; } }
public sealed record AuthorityBindingPolicy { public AuthorityBindingDirection Direction { get; init; } = AuthorityBindingDirection.HostToGuest; public SensitiveAuthorityClass AuthorityClass { get; init; } = SensitiveAuthorityClass.CredentialDelegation; public SensitiveAuthorityClass EffectiveAuthorityClass { get; init; } = SensitiveAuthorityClass.CredentialDelegation; public SensitiveLeasePolicy Lease { get; init; } = new(); public SensitiveRedactionLevel Redaction { get; init; } = SensitiveRedactionLevel.RedactSecretValues; public bool RequireAudit { get; init; } = true; public bool AllowProviderSideProxy { get; init; } = true; public bool RequireExplicitUserApproval { get; init; } public SensitiveProvenance? Provenance { get; init; } }
public sealed record BoundAuthority { public required AuthoritySourceKind SourceKind { get; init; } public required AuthorityProjectionKind ProjectionKind { get; init; } public AuthorityBindingDirection Direction { get; init; } public SensitiveAuthorityClass EffectiveAuthorityClass { get; init; } public UnixSocketPath? TargetSocketPath { get; init; } public string? EnvironmentVariableName { get; init; } public HostFunctionName? HostFunctionName { get; init; } public DateTimeOffset BoundAt { get; init; } public DateTimeOffset? ExpiresAt { get; init; } public ulong RotationGeneration { get; init; } public RevocationVerificationStatus RevocationStatus { get; init; } public string? AuditCorrelationId { get; init; } }
public sealed record AuthorityAuditEvent { public ResourceRef<AuthorityBinding>? Binding { get; init; } public ResourceRef<PublishedEndpoint>? Endpoint { get; init; } public required AuthorityAuditKind Kind { get; init; } public required AuthoritySourceKind SourceKind { get; init; } public required AuthorityTargetKind TargetKind { get; init; } public required DateTimeOffset Timestamp { get; init; } public string? Actor { get; init; } public string? CorrelationId { get; init; } public DiagnosticCode? ReasonCode { get; init; } }
public enum AuthorityAuditKind { Projected, EndpointExposed, HostFunctionCalled, Used, Rotated, Revoked, RevocationVerified, Degraded, Failed }

[Flags] public enum NetworkCapabilitySet { None = 0, IPv4 = 1, IPv6 = 2, NatEgress = 4, PeerConnectivity = 8, RoutedIngress = 16, InternalDns = 32, ServiceRecords = 64, HostDnsExport = 128, HostResolverImport = 256, TcpPublish = 512, UdpPublish = 1024, UnixSocketPublish = 2048, StaticAddress = 4096, StaticMacAddress = 8192, CustomMtu = 16384, AuthorityProjection = 32768, BindingAudit = 65536 }
[Flags] public enum DiscoveryCapabilitySet { None = 0, ARecords = 1, AaaaRecords = 2, CNameRecords = 4, ServiceRecords = 8, SearchDomains = 16, HostExport = 32, HostResolverImport = 64 }
public sealed record NetworkLimitation(NetworkDegradedFeature Feature, CapabilityDegradationMode Mode, string ReasonCode, string? Message = null);
public enum NetworkDegradedFeature { IPv6, PeerConnectivity, HostDnsExport, HostResolverImport, TcpPublish, UdpPublish, UnixSocketPublish, StaticAddress, StaticMacAddress, CustomMtu, InternalDns, ServiceRecords, SocketProjection, CredentialProjection, BindingAudit, ProviderHealth }
public enum CapabilityDegradationMode { Unsupported, DisabledByPolicy, TemporarilyUnavailable, PartiallyAvailable, RequiresPermission, ProviderError }

// ---------------------------------------------------------------------------
// Optional block volume and engine control-plane families
// ---------------------------------------------------------------------------

public sealed record BlockVolumeSpec { public required ByteSize Size { get; init; } public DiskImageFormat? Format { get; init; } public VolumeAccessMode AccessMode { get; init; } = VolumeAccessMode.ReadWrite; public GuestFilesystemProvisioning Filesystem { get; init; } = GuestFilesystemProvisioning.ProviderDefault; public RetentionPolicy Retention { get; init; } = RetentionPolicy.Project; public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = Array.Empty<ProviderExtensionData>(); }
public sealed record BlockVolumeStatus : ResourceStatus { public BlockVolumePhase VolumePhase { get; init; } public ByteSize? Size { get; init; } public DiskImageFormat? Format { get; init; } public VolumeLockStatus? Lock { get; init; } public ResourceUsageSummary Usage { get; init; } = new(); public ProviderOpaqueHandle? ProviderHandle { get; init; } }
public enum BlockVolumePhase { Pending, Creating, Ready, Attaching, Attached, Degraded, Detaching, Deleting, Deleted, Failed }
public enum VolumeAccessMode { ReadOnly, ReadWrite, ExclusiveReadWrite }
public enum VolumeLockState { Unknown, Unlocked, Locked, Stale, Broken }
public enum GuestFilesystemProvisioning { ProviderDefault, None, Ext4, Xfs, Ntfs, Fat32, ProviderDefined }
public enum VolumeAttachmentPhase { Pending, Attached, Degraded, Detached, Failed }
public sealed record VolumeLockStatus(VolumeLockState State, string? Holder = null, DateTimeOffset? Since = null);

public sealed record EngineControlPlaneSpec { public required EngineControlPlaneKind Kind { get; init; } public EngineAuthorityMode AuthorityMode { get; init; } = EngineAuthorityMode.Rootless; public EngineApiKind Api { get; init; } = EngineApiKind.ProviderDefined; public EngineWorkloadAdoptionMode WorkloadAdoption { get; init; } = EngineWorkloadAdoptionMode.None; public EngineImageStoreMode ImageStore { get; init; } = EngineImageStoreMode.ProviderManaged; public ResourceRef<RuntimeHost>? Host { get; init; } public SensitiveEndpointPolicy? EndpointPolicy { get; init; } public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = Array.Empty<ProviderExtensionData>(); }
public sealed record EngineControlPlaneStatus : ResourceStatus { public EngineControlPlanePhase EnginePhase { get; init; } public EngineIncarnationGeneration? EngineGeneration { get; init; } public IReadOnlyList<EngineApiEndpointStatus> Endpoints { get; init; } = Array.Empty<EngineApiEndpointStatus>(); public bool ExternalMutationPossible { get; init; } public ProviderOpaqueHandle? ProviderHandle { get; init; } }
public sealed record EngineAuthorityBindingRequest
{
    public required ResourceRef<EngineControlPlane> Engine { get; init; }
    public required EngineApiKind Api { get; init; }
    public required TargetHandle<ExecutionUnit> TargetUnit { get; init; }
    public required UnixSocketPath TargetSocketPath { get; init; }
    public SensitiveProvenance? Provenance { get; init; }
}
public sealed record EngineAuthorityBindingPlan
{
    public required bool Accepted { get; init; }
    public EngineAuthorityBindingPlanId PlanId { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public ResourceRef<EngineControlPlane>? SourceEngine { get; init; }
    public AuthorityBindingSpec? Spec { get; init; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
}
public readonly record struct EngineAuthorityBindingPlanId(string Value);
public enum EngineControlPlaneKind { DockerCompatible, Podman, Containerd, Kubernetes, BuildKit, ProviderDefined }
public enum EngineAuthorityMode { Rootless, Rootful, Mixed, ProviderDefined }
public enum EngineApiKind { DockerCompatible, PodmanApi, ContainerdApi, KubernetesApi, BuildKitApi, ProviderDefined }
public enum EngineWorkloadAdoptionMode { None, ObserveOnly, ExplicitAdoption, AutomaticAdoption }
public enum EngineImageStoreMode { ProviderManaged, SharedWithRootfsProvider, EngineLocal, Remote, ProviderDefined }
public enum EngineControlPlanePhase { Pending, Installing, Starting, Ready, Degraded, Stopping, Stopped, Failed }
public sealed record EngineApiEndpointStatus(EngineApiKind Api, ProviderNamedEndpoint Endpoint, SensitiveEndpointPolicy? SensitivePolicy = null);

// ---------------------------------------------------------------------------
// Runtime facade and provider interfaces
// ---------------------------------------------------------------------------

public interface IEnvironmentRuntime
{
    ValueTask<RuntimePlan> PlanAsync(RuntimePlanRequest request, CancellationToken cancellationToken = default);
    ValueTask<RuntimePlanValidationResult> ValidateAsync(RuntimePlan plan, CancellationToken cancellationToken = default);
    ValueTask<ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus>> EnsureHostAsync(RuntimeHostSpec spec, CancellationToken cancellationToken = default);
    ValueTask<ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus>> StopHostAsync(StopPolicy policy, CancellationToken cancellationToken = default);
    ValueTask<RuntimeHostDeletionResult> DeleteHostAsync(CancellationToken cancellationToken = default);
    ValueTask<ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus>> EnsureEngineControlPlaneAsync(EngineControlPlaneSpec spec, CancellationToken cancellationToken = default);
    ValueTask DeleteEngineControlPlaneAsync(ResourceRef<EngineControlPlane> engine, CancellationToken cancellationToken = default);
    ValueTask<EngineAuthorityBindingPlan> PlanEngineAuthorityBindingAsync(EngineAuthorityBindingRequest request, CancellationToken cancellationToken = default);
    ValueTask<ResourceSnapshot<AuthorityBinding, AuthorityBindingSpec, AuthorityBindingStatus>> EnsureEngineAuthorityBindingAsync(EngineAuthorityBindingPlan plan, CancellationToken cancellationToken = default);
    ValueTask<ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus>> EnsureExecutionUnitAsync(ExecutionUnitSpec spec, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus>>> ListExecutionUnitsAsync(CancellationToken cancellationToken = default);
    ValueTask<ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus>> GetExecutionUnitAsync(ResourceRef<ExecutionUnit> unit, CancellationToken cancellationToken = default);
    ValueTask DeleteExecutionUnitAsync(ResourceRef<ExecutionUnit> unit, CancellationToken cancellationToken = default);
    ValueTask<ResourceSnapshot<Network, NetworkSpec, NetworkStatus>> EnsureNetworkAsync(NetworkSpec spec, NetworkRealizationContext? realizationContext = null, CancellationToken cancellationToken = default);
    ValueTask<ResourceSnapshot<Network, NetworkSpec, NetworkStatus>> GetNetworkAsync(ResourceRef<Network> network, CancellationToken cancellationToken = default);
    ValueTask DeleteNetworkAsync(ResourceRef<Network> network, CancellationToken cancellationToken = default);
    ValueTask<ResourceSnapshot<NetworkMembership, NetworkMembershipSpec, NetworkMembershipStatus>> EnsureNetworkMembershipAsync(NetworkMembershipSpec spec, CancellationToken cancellationToken = default);
    ValueTask<ResourceSnapshot<NetworkMembership, NetworkMembershipSpec, NetworkMembershipStatus>> GetNetworkMembershipAsync(ResourceRef<NetworkMembership> membership, CancellationToken cancellationToken = default);
    ValueTask ReleaseNetworkMembershipAsync(ResourceRef<NetworkMembership> membership, CancellationToken cancellationToken = default);
    ValueTask<ResourceSnapshot<ServiceDiscovery, ServiceDiscoverySpec, ServiceDiscoveryStatus>> EnsureServiceDiscoveryAsync(ServiceDiscoverySpec spec, CancellationToken cancellationToken = default);
    ValueTask<ResourceSnapshot<ServiceDiscovery, ServiceDiscoverySpec, ServiceDiscoveryStatus>> GetServiceDiscoveryAsync(ResourceRef<ServiceDiscovery> discovery, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<DiscoveryRecord>> ResolveServiceDiscoveryAsync(ServiceDiscoveryQuery query, CancellationToken cancellationToken = default);
    ValueTask ReleaseServiceDiscoveryAsync(ResourceRef<ServiceDiscovery> discovery, CancellationToken cancellationToken = default);
    ValueTask<ResourceSnapshot<PublishedEndpoint, PublishedEndpointSpec, PublishedEndpointStatus>> EnsurePublishedEndpointAsync(PublishedEndpointSpec spec, CancellationToken cancellationToken = default);
    ValueTask ReleasePublishedEndpointAsync(ResourceRef<PublishedEndpoint> endpoint, CancellationToken cancellationToken = default);
    ValueTask<ResourceSnapshot<AuthorityBinding, AuthorityBindingSpec, AuthorityBindingStatus>> EnsureAuthorityBindingAsync(AuthorityBindingSpec spec, CancellationToken cancellationToken = default);
    ValueTask RevokeAuthorityBindingAsync(ResourceRef<AuthorityBinding> binding, CancellationToken cancellationToken = default);
    ValueTask<ResourceSnapshot<ProcessInvocation, ProcessInvocationSpec, ProcessInvocationStatus>> StartProcessAsync(ProcessInvocationSpec spec, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<ResourceSnapshot<ProcessInvocation, ProcessInvocationSpec, ProcessInvocationStatus>>> ListProcessesAsync(ResourceRef<ExecutionUnit>? unit = null, CancellationToken cancellationToken = default);
    ValueTask<ResourceSnapshot<ProcessInvocation, ProcessInvocationSpec, ProcessInvocationStatus>> GetProcessAsync(ResourceRef<ProcessInvocation> process, CancellationToken cancellationToken = default);
    ValueTask<ResourceSnapshot<ProcessInvocation, ProcessInvocationSpec, ProcessInvocationStatus>> StopProcessAsync(ResourceRef<ProcessInvocation> process, ProcessStopRequest request, CancellationToken cancellationToken = default);
    ValueTask<ResourceSnapshot<ProcessInvocation, ProcessInvocationSpec, ProcessInvocationStatus>> WaitProcessAsync(ResourceRef<ProcessInvocation> process, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ProcessOutputChunk> ReadProcessOutputAsync(ResourceRef<ProcessInvocation> process, long? afterSequence = null, int? limit = null, bool follow = false, CancellationToken cancellationToken = default);
    ValueTask DeleteProcessAsync(ResourceRef<ProcessInvocation> process, CancellationToken cancellationToken = default);
    ValueTask<ProcessInvocationResult> RunProcessAsync(ProcessInvocationSpec spec, IProcessOutputSink? output = null, CancellationToken cancellationToken = default);
    ValueTask<ResourceSnapshot<FunctionSandbox, FunctionSandboxSpec, FunctionSandboxStatus>> EnsureFunctionSandboxAsync(FunctionSandboxSpec spec, CancellationToken cancellationToken = default);
    ValueTask<FunctionInvocationResult> InvokeFunctionAsync(FunctionInvocationSpec spec, IFunctionObservationSink? observations = null, CancellationToken cancellationToken = default);
    ValueTask<RuntimeFinalizationResult> FinalizeRuntimeAsync(RuntimeFinalizationRequest request, CancellationToken cancellationToken = default);
}

public sealed record RuntimeHostDeletionResult
{
    public required bool Deleted { get; init; }
    public RuntimeHostStatus? RetainedHostStatus { get; init; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
}

public sealed record RuntimeFinalizationRequest(ResourceScope RuntimeScope, bool PromoteMemory, CleanupPolicy CleanupPolicy);
public sealed record RuntimeFinalizationResult { public required ResourceScope RuntimeScope { get; init; } public IReadOnlyList<FinalizationResult> ContentProjections { get; init; } = Array.Empty<FinalizationResult>(); public IReadOnlyList<UntypedResourceRef> RetainedResources { get; init; } = Array.Empty<UntypedResourceRef>(); public IReadOnlyList<WorkspaceConflict> Conflicts { get; init; } = Array.Empty<WorkspaceConflict>(); public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>(); }

public interface IProviderModule { ProviderDescriptor Descriptor { get; } void Register(IProviderRegistrationBuilder builder); void RegisterJsonTypes(IProviderJsonTypeRegistry registry); }
public interface IProviderRegistrationBuilder { void AddProviderCapabilityReporter(IProviderCapabilityReporter reporter); void AddProviderActivator(IProviderActivator activator); void AddRuntimeHostProvider(IRuntimeHostProvider provider); void AddRuntimeHostResetProvider(IRuntimeHostResetProvider provider); void AddExecutionUnitProvider(IExecutionUnitProvider provider); void AddProcessProvider(IProcessProvider provider); void AddFunctionSandboxProvider(IFunctionSandboxProvider provider); void AddFunctionSnapshotProvider(IFunctionSnapshotProvider provider); void AddArtifactProvider(IArtifactProvider provider); void AddRootFilesystemProvider(IRootFilesystemProvider provider); void AddWorkspaceStore(IWorkspaceStore provider); void AddContentProjectionProvider(IContentProjectionProvider provider); void AddNetworkProvider(INetworkProvider provider); void AddNetworkMembershipProvider(INetworkMembershipProvider provider); void AddServiceDiscoveryProvider(IServiceDiscoveryProvider provider); void AddEndpointPublicationProvider(IEndpointPublicationProvider provider); void AddAuthorityBindingProvider(IAuthorityBindingProvider provider); void AddCredentialProvider(ICredentialProvider provider); void AddEngineControlPlaneProvider(IEngineControlPlaneProvider provider); }
public interface IProviderJsonTypeRegistry { void Add(JsonTypeInfo jsonTypeInfo, string typeDiscriminator); }
public interface IProviderCatalog { ValueTask<IReadOnlyList<ProviderDescriptor>> ListAsync(CancellationToken cancellationToken = default); ValueTask<ProviderDescriptor?> GetAsync(ProviderId providerId, CancellationToken cancellationToken = default); }
public interface IProviderCapabilityReporter { ValueTask<ProviderCapabilityReport> GetCapabilitiesAsync(ProviderId providerId, CancellationToken cancellationToken = default); ValueTask<ProviderCapabilityReport> GetCapabilitiesAsync(ProviderId providerId, ProviderCapabilityQuery query, CancellationToken cancellationToken = default); }
public sealed record ProviderCapabilityQuery(CapabilityRequirementSet? Requirements = null, PlatformSpec? HostPlatform = null, PlatformSpec? GuestPlatform = null, GuestAbiSpec? GuestAbi = null, ResourceScope? Scope = null);
public interface IProviderActivator { ValueTask<ResourceSnapshot<ProviderActivation, ProviderActivationSpec, ProviderActivationStatus>> ActivateAsync(ProviderActivationSpec spec, CancellationToken cancellationToken = default); ValueTask<ProviderActivationStatus> GetStatusAsync(ResourceRef<ProviderActivation> activation, CancellationToken cancellationToken = default); ValueTask StopAsync(TargetHandle<ProviderActivation> activation, ProviderStopOptions options, CancellationToken cancellationToken = default); }
public readonly record struct ProviderStopOptions(TimeSpan GracePeriod, bool Force, string Reason);
public interface IRuntimePlanner { ValueTask<RuntimePlan> PlanAsync(RuntimePlanRequest request, CancellationToken cancellationToken = default); ValueTask<RuntimePlanValidationResult> ValidateAsync(RuntimePlan plan, CancellationToken cancellationToken = default); }

public interface IRuntimeHostProvider { ProviderId ProviderId { get; } ValueTask<RuntimeHostStatus> EnsureAsync(ResourceMetadata<RuntimeHost> metadata, RuntimeHostSpec spec, RuntimeHostStatus? observed, CancellationToken cancellationToken = default); ValueTask<RuntimeHostStatus> StopAsync(TargetHandle<RuntimeHost> host, StopPolicy policy, CancellationToken cancellationToken = default); ValueTask DeleteAsync(ResourceRef<RuntimeHost> host, CancellationToken cancellationToken = default); ValueTask<RuntimeHostStatus> GetStatusAsync(TargetHandle<RuntimeHost> host, CancellationToken cancellationToken = default); }
public interface IRuntimeHostResetProvider { ProviderId ProviderId { get; } ValueTask<RuntimeHostResetResult> ResetAsync(TargetHandle<RuntimeHost> host, RuntimeHostResetRequest request, CancellationToken cancellationToken = default); }
public interface IExecutionUnitProvider { ProviderId ProviderId { get; } ValueTask<ExecutionUnitStatus> EnsureAsync(ResourceMetadata<ExecutionUnit> metadata, ExecutionUnitSpec spec, ExecutionUnitStatus? observed, CancellationToken cancellationToken = default); ValueTask<ExecutionUnitStatus> StopAsync(TargetHandle<ExecutionUnit> unit, StopPolicy policy, CancellationToken cancellationToken = default); ValueTask DeleteAsync(ResourceRef<ExecutionUnit> unit, CancellationToken cancellationToken = default); ValueTask<ExecutionUnitStatus> GetStatusAsync(TargetHandle<ExecutionUnit> unit, CancellationToken cancellationToken = default); }
public interface IProcessProvider { ProviderId ProviderId { get; } ValueTask<IProcessInvocationHandle> StartAsync(ProcessInvocationSpec spec, IProcessOutputSink? output = null, CancellationToken cancellationToken = default); ValueTask<ProcessInvocationResult> RunAsync(ProcessInvocationSpec spec, IProcessOutputSink? output = null, CancellationToken cancellationToken = default); ValueTask SignalAsync(TargetHandle<ProcessInvocation> process, ProcessSignal signal, CancellationToken cancellationToken = default); ValueTask ResizeTerminalAsync(TargetHandle<ProcessInvocation> process, TerminalSpec size, CancellationToken cancellationToken = default); ValueTask<ProcessInvocationResult> WaitAsync(TargetHandle<ProcessInvocation> process, CancellationToken cancellationToken = default); IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(TargetHandle<ProcessInvocation> process, CancellationToken cancellationToken = default); }
public interface IRetainedProcessProvider
{
    ProviderId ProviderId { get; }
    ValueTask<ProcessInvocationStatus> GetStatusAsync(TargetHandle<ProcessInvocation> process, CancellationToken cancellationToken = default);
    ValueTask StopAsync(TargetHandle<ProcessInvocation> process, ProcessStopRequest request, CancellationToken cancellationToken = default);
    ValueTask ReleaseAsync(ResourceRef<ProcessInvocation> process, CancellationToken cancellationToken = default);
}
public interface IFunctionSandboxProvider { ProviderId ProviderId { get; } ValueTask<FunctionSandboxStatus> EnsureAsync(ResourceMetadata<FunctionSandbox> metadata, FunctionSandboxSpec spec, FunctionSandboxStatus? observed, CancellationToken cancellationToken = default); ValueTask<FunctionInvocationResult> InvokeAsync(FunctionInvocationSpec spec, IFunctionObservationSink? observations = null, CancellationToken cancellationToken = default); ValueTask<FunctionSandboxStatus> GetStatusAsync(TargetHandle<FunctionSandbox> sandbox, CancellationToken cancellationToken = default); ValueTask ReleaseAsync(TargetHandle<FunctionSandbox> sandbox, CancellationToken cancellationToken = default); }
public interface IFunctionSnapshotProvider { ProviderId ProviderId { get; } ValueTask<FunctionSandboxSnapshotStatus> CaptureAsync(TargetHandle<FunctionSandbox> sandbox, FunctionSnapshotRequest request, CancellationToken cancellationToken = default); ValueTask<FunctionSandboxStatus> RestoreAsync(TargetHandle<FunctionSandbox> sandbox, FunctionRestoreRequest request, CancellationToken cancellationToken = default); ValueTask ReleaseSnapshotAsync(ResourceRef<FunctionSandboxSnapshot> snapshot, CancellationToken cancellationToken = default); }
public interface IArtifactProvider { ProviderId ProviderId { get; } ValueTask<ContentArtifactStatus> ResolveAsync(ResourceMetadata<ContentArtifact> metadata, ContentArtifactSpec spec, CancellationToken cancellationToken = default); ValueTask<ContentArtifactStatus> EnsureAvailableAsync(ResourceRef<ContentArtifact> artifact, CancellationToken cancellationToken = default); }
public interface IRootFilesystemProvider { ProviderId ProviderId { get; } ValueTask<RootFilesystemViewStatus> MaterializeAsync(ResourceMetadata<RootFilesystemView> metadata, RootFilesystemViewSpec spec, TargetHandle<RuntimeHost>? host, TargetHandle<ExecutionUnit>? unit, CancellationToken cancellationToken = default); ValueTask<FinalizationResult> FinalizeAsync(TargetHandle<RootFilesystemView> rootfs, FinalizationRequest request, CancellationToken cancellationToken = default); ValueTask ReleaseAsync(TargetHandle<RootFilesystemView> rootfs, CancellationToken cancellationToken = default); }
public interface IWorkspaceStore { ValueTask<WorkspaceStatus> GetStatusAsync(ResourceRef<Workspace> workspace, CancellationToken cancellationToken = default); ValueTask<ContentEnumerationPage> EnumerateAsync(ResourceRef<Workspace> workspace, ContentSelector selector, ContentPageCursor cursor, CancellationToken cancellationToken = default); ValueTask CopyContentAsync(ContentSelector selector, IBufferWriter<byte> destination, CancellationToken cancellationToken = default); }
public interface IContentProjectionProvider { ProviderId ProviderId { get; } ValueTask<ContentProjectionStatus> ProjectAsync(ResourceMetadata<ContentProjection> metadata, ContentProjectionSpec spec, TargetHandle<RuntimeHost>? host, TargetHandle<ExecutionUnit>? unit, CancellationToken cancellationToken = default); ValueTask EnumerateEntriesAsync(ResourceRef<ContentProjection> projection, IContentProjectionEntrySink sink, CancellationToken cancellationToken = default); ValueTask<SyncResult> SyncAsync(TargetHandle<ContentProjection> projection, SyncRequest request, CancellationToken cancellationToken = default); ValueTask<FinalizationResult> FinalizeAsync(TargetHandle<ContentProjection> projection, FinalizationRequest request, IExecutionEventSink? events = null, CancellationToken cancellationToken = default); ValueTask ReleaseAsync(TargetHandle<ContentProjection> projection, CancellationToken cancellationToken = default); }
public interface INetworkProvider { ProviderId ProviderId { get; } ValueTask<NetworkStatus> EnsureNetworkAsync(ResourceMetadata<Network> metadata, NetworkSpec spec, NetworkRealizationContext? realizationContext, NetworkStatus? observed, CancellationToken cancellationToken = default); ValueTask<NetworkStatus> GetStatusAsync(ResourceRef<Network> network, CancellationToken cancellationToken = default); ValueTask DeleteNetworkAsync(ResourceRef<Network> network, CancellationToken cancellationToken = default); }
public interface INetworkMembershipProvider { ProviderId ProviderId { get; } ValueTask<NetworkMembershipStatus> EnsureMembershipAsync(ResourceMetadata<NetworkMembership> metadata, NetworkMembershipSpec spec, NetworkMembershipStatus? observed, CancellationToken cancellationToken = default); ValueTask<NetworkMembershipStatus> GetMembershipStatusAsync(ResourceRef<NetworkMembership> membership, CancellationToken cancellationToken = default); ValueTask ReleaseMembershipAsync(ResourceRef<NetworkMembership> membership, CancellationToken cancellationToken = default); }
public interface IServiceDiscoveryProvider { ProviderId ProviderId { get; } ValueTask<ServiceDiscoveryStatus> EnsureServiceDiscoveryAsync(ResourceMetadata<ServiceDiscovery> metadata, ServiceDiscoverySpec spec, ServiceDiscoveryStatus? observed, CancellationToken cancellationToken = default); ValueTask<ServiceDiscoveryStatus> GetStatusAsync(ResourceRef<ServiceDiscovery> discovery, CancellationToken cancellationToken = default); ValueTask<IReadOnlyList<DiscoveryRecord>> ResolveAsync(ServiceDiscoveryQuery query, CancellationToken cancellationToken = default); ValueTask ReleaseAsync(ResourceRef<ServiceDiscovery> discovery, CancellationToken cancellationToken = default); }
public sealed record ServiceDiscoveryQuery(ResourceRef<ServiceDiscovery> Discovery, DnsName Name, DiscoveryRecordKind? Kind = null);
public interface IEndpointPublicationProvider { ProviderId ProviderId { get; } ValueTask<PublishedEndpointStatus> EnsurePublishedEndpointAsync(ResourceMetadata<PublishedEndpoint> metadata, PublishedEndpointSpec spec, PublishedEndpointStatus? observed, CancellationToken cancellationToken = default); ValueTask<PublishedEndpointStatus> GetStatusAsync(ResourceRef<PublishedEndpoint> endpoint, CancellationToken cancellationToken = default); ValueTask ReleasePublishedEndpointAsync(ResourceRef<PublishedEndpoint> endpoint, CancellationToken cancellationToken = default); }
public interface IAuthorityBindingProvider { ProviderId ProviderId { get; } ValueTask<AuthorityBindingStatus> EnsureAuthorityBindingAsync(ResourceMetadata<AuthorityBinding> metadata, AuthorityBindingSpec spec, AuthorityBindingStatus? observed, CancellationToken cancellationToken = default); ValueTask<AuthorityBindingStatus> GetStatusAsync(ResourceRef<AuthorityBinding> binding, CancellationToken cancellationToken = default); ValueTask RevokeAuthorityBindingAsync(ResourceRef<AuthorityBinding> binding, CancellationToken cancellationToken = default); }
public interface ICredentialProvider { ProviderId ProviderId { get; } ValueTask<CredentialResolution> ResolveAsync(CredentialRequest request, CancellationToken cancellationToken = default); }
public interface IEngineControlPlaneProvider
{
    ProviderId ProviderId { get; }
    ValueTask<EngineControlPlaneStatus> EnsureEngineControlPlaneAsync(ResourceMetadata<EngineControlPlane> metadata, EngineControlPlaneSpec spec, EngineControlPlaneStatus? observed, CancellationToken cancellationToken = default);
    ValueTask<EngineAuthorityBindingPlan> PlanAuthorityBindingAsync(EngineControlPlaneStatus engine, EngineAuthorityBindingRequest request, CancellationToken cancellationToken = default);
    ValueTask<EngineControlPlaneStatus> GetStatusAsync(ResourceRef<EngineControlPlane> engine, CancellationToken cancellationToken = default);
    ValueTask<EngineControlPlaneStatus> StopAsync(TargetHandle<EngineControlPlane> engine, StopPolicy policy, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(ResourceRef<EngineControlPlane> engine, CancellationToken cancellationToken = default);
}
public sealed record CredentialRequest(CredentialRef Credential, ResourceScope Scope, string? Purpose = null);
public sealed record CredentialResolution(CredentialRef Credential, ProviderOpaqueHandle Handle, DateTimeOffset? ExpiresAt = null);

internal static class Empty
{
    public static IReadOnlyDictionary<string, string> StringDictionary { get; } = new Dictionary<string, string>(0, StringComparer.Ordinal);
    public static IReadOnlyDictionary<string, string?> NullableStringDictionary { get; } = new Dictionary<string, string?>(0, StringComparer.Ordinal);
}
