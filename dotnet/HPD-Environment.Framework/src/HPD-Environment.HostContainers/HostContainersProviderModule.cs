namespace HPD.Environment.HostContainers;

using System.Runtime.InteropServices;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

public sealed class HostContainersProviderModule : IProviderModule
{
    private readonly HostContainersProviderOptions _options;
    private readonly IContainerEngineProbe _engineProbe;
    private readonly IContainerEngineNetworkClient _networkClient;
    private readonly HostContainersProviderState _state;

    public HostContainersProviderModule()
        : this(new HostContainersProviderOptions())
    {
    }

    public HostContainersProviderModule(
        HostContainersProviderOptions options)
        : this(options, engineProbe: null, networkClient: null)
    {
    }

    internal HostContainersProviderModule(
        HostContainersProviderOptions options,
        IContainerEngineProbe? engineProbe,
        IContainerEngineNetworkClient? networkClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _engineProbe = engineProbe ?? new DockerCompatibleEngineProbe(options);
        _networkClient =
            networkClient ?? new DockerCompatibleNetworkClient();
        _state = new HostContainersProviderState(_options);
    }

    public ProviderDescriptor Descriptor =>
        HostContainersProviderDescriptor.Create(_options);

    internal AuthorityAuditEvent[] GetAuthorityAuditEvents(
        string authorityId) =>
        _state.GetAuthorityAudit(authorityId);

    public void Register(IProviderRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddProviderCapabilityReporter(
            new HostContainersCapabilityReporter(_options));
        var hostProvider = new HostContainersRuntimeHostProvider(_state);
        builder.AddRuntimeHostProvider(hostProvider);
        builder.AddRuntimeHostResetProvider(hostProvider);
        builder.AddExecutionUnitProvider(
            new HostContainersExecutionUnitProvider(_state));
        var processProvider =
            new HostContainersProcessProvider(_state);
        builder.AddProcessProvider(processProvider);
        builder.AddContentProjectionProvider(
            new HostContainersContentProjectionProvider(
                _state,
                processProvider));
        builder.AddWorkspaceFileStoreProvider(
            new HostContainersWorkspaceFileStoreProvider(
                _state,
                processProvider));
        builder.AddEngineControlPlaneProvider(
            new HostContainersEngineControlPlaneProvider(
                _state,
                _engineProbe));
        builder.AddAuthorityBindingProvider(
            new HostContainersAuthorityBindingProvider(_state));
        builder.AddEndpointPublicationProvider(
            new HostContainersEndpointPublicationProvider(_state));
        var networkProvider =
            new HostContainersNetworkProvider(_state, _networkClient);
        builder.AddNetworkProvider(networkProvider);
        builder.AddNetworkMembershipProvider(networkProvider);
        builder.AddServiceDiscoveryProvider(
            new HostContainersServiceDiscoveryProvider(_state));
        var storageProvider = new HostContainersStorageProvider(_state);
        builder.AddStoragePoolProvider(storageProvider);
        builder.AddDurableVolumeProvider(storageProvider);
        builder.AddStorageReservationProvider(storageProvider);
        builder.AddVolumeBackupProvider(storageProvider);
        builder.AddVolumeRestoreProvider(storageProvider);
    }

    public void RegisterJsonTypes(IProviderJsonTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
    }
}

public static class HostContainersRegistrationExtensions
{
    public static EnvironmentProviderRegistry RegisterHostContainers(
        this EnvironmentProviderRegistry registry,
        HostContainersProviderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.RegisterModule(
            new HostContainersProviderModule(
                options ?? new HostContainersProviderOptions()));
        return registry;
    }
}

public static class HostContainersProviderDescriptor
{
    public static readonly ProviderId ProviderId =
        new("hpd.execution.host-containers");

    public const string CapabilityPrefix = "hpd.execution.host-containers";
    public static readonly CapabilityId NativeHostCapability =
        new($"{CapabilityPrefix}.runtime-host.native");
    public static readonly CapabilityId ContainerIsolationCapability =
        StandardEnvironmentCapabilities.ContainerIsolation;
    public static readonly CapabilityId ProcessIsolationCapability =
        StandardEnvironmentCapabilities.ProcessIsolation;
    public static readonly CapabilityId SharedHostKernelCapability =
        StandardEnvironmentCapabilities.SharedHostKernel;
    public static readonly CapabilityId HardwareVirtualizationCapability =
        StandardEnvironmentCapabilities.HardwareVirtualization;
    public static readonly CapabilityId GuestAgentBoundaryCapability =
        StandardEnvironmentCapabilities.GuestAgentBoundary;
    public static readonly CapabilityId MediatedEngineAuthorityCapability =
        StandardEnvironmentCapabilities.MediatedEngineAuthority;
    public static readonly CapabilityId HostLocalEndpointCapability =
        StandardEnvironmentCapabilities.HostLocalEndpointPublication;

    public static readonly ProviderContractKind FirstSliceContracts =
        ProviderContractKind.RuntimeHost |
        ProviderContractKind.ExecutionUnit |
        ProviderContractKind.ProcessInvocation |
        ProviderContractKind.ContentProjection |
        ProviderContractKind.WorkspaceFileStore |
        ProviderContractKind.Network |
        ProviderContractKind.NetworkMembership |
        ProviderContractKind.ServiceDiscovery |
        ProviderContractKind.EndpointPublication |
        ProviderContractKind.AuthorityBinding |
        ProviderContractKind.EngineControlPlane |
        ProviderContractKind.StoragePool |
        ProviderContractKind.DurableVolume |
        ProviderContractKind.StorageReservation |
        ProviderContractKind.VolumeBackup |
        ProviderContractKind.VolumeRestore;

    public static ProviderDescriptor Create(
        HostContainersProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ProviderDescriptor
        {
            Id = ProviderId,
            DisplayName = "HPD Local Environment Provider",
            ContractVersion = new SemanticVersion(1, 0, 0),
            ProviderVersion = new SemanticVersion(0, 1, 0, "preview"),
            ContractKinds = FirstSliceContracts,
            TrustLevel = ProviderTrustLevel.BuiltIn,
            DefaultActivationScope = ProviderActivationScope.Runtime,
            SupportedActivationScopes = [ProviderActivationScope.Runtime],
            ActivationModels =
            [
                new ProviderActivationModel(
                    ProviderActivationKind.InProcess,
                    ProviderActivationScope.Runtime,
                    ProviderTransportKind.None),
            ],
            HostPlatforms =
            [
                new PlatformSpec("macos", "arm64"),
                new PlatformSpec("macos", "x64"),
                new PlatformSpec("linux", "arm64"),
                new PlatformSpec("linux", "x64"),
            ],
            GuestPlatforms = [],
            Discovery =
            [
                new ProviderDiscoveryDescriptor(
                    ProviderDiscoveryKind.StaticModule),
            ],
            HostDependencies =
            [
                new HostDependencyRequirement(
                    new HostDependencyRef(
                        HostDependencyKind.ProviderDefined,
                        "local-container-engine"),
                    Required: true,
                    Detail:
                        "An explicitly configured or unambiguous well-known local container-engine socket."),
            ],
        };
    }

    public static PlatformSpec CurrentPlatform() =>
        new(
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "macos"
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    ? "linux"
                    : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? "windows"
                        : "unknown",
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
}

internal sealed class HostContainersCapabilityReporter(
    HostContainersProviderOptions options)
    : IProviderCapabilityReporter
{
    public ValueTask<ProviderCapabilityReport> GetCapabilitiesAsync(
        ProviderId providerId,
        CancellationToken cancellationToken = default) =>
        GetCapabilitiesAsync(
            providerId,
            new ProviderCapabilityQuery(CapabilityRequirementSet.Empty),
            cancellationToken);

    public ValueTask<ProviderCapabilityReport> GetCapabilitiesAsync(
        ProviderId providerId,
        ProviderCapabilityQuery query,
        CancellationToken cancellationToken = default)
    {
        PlatformSpec host =
            query.HostPlatform ??
            HostContainersProviderDescriptor.CurrentPlatform();
        bool supported =
            string.Equals(
                host.OperatingSystem,
                "macos",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                host.OperatingSystem,
                "linux",
                StringComparison.OrdinalIgnoreCase);
        CapabilityState available = supported
            ? CapabilityState.Supported
            : CapabilityState.Unsupported;
        bool engineConfigured =
            !string.IsNullOrWhiteSpace(options.EngineSocketPath) &&
            File.Exists(options.EngineSocketPath);
        bool dockerCliAvailable;
        try
        {
            _ = HostContainersDockerCli.ResolvePath(options);
            dockerCliAvailable = true;
        }
        catch
        {
            dockerCliAvailable = false;
        }
        CapabilityState sandboxAvailable =
            !supported || !dockerCliAvailable
                ? CapabilityState.Unsupported
                : engineConfigured
                    ? CapabilityState.Supported
                    : CapabilityState.RequiresConfiguration;
        return ValueTask.FromResult(new ProviderCapabilityReport
        {
            ProviderId = providerId,
            ObservedAt = DateTimeOffset.UtcNow,
            HostPlatform = host,
            Capabilities =
            [
                Fact(
                    HostContainersProviderDescriptor.NativeHostCapability,
                    ProviderContractKind.RuntimeHost,
                    available,
                    "The current operating-system host is the logical runtime host."),
                Fact(
                    HostContainersProviderDescriptor.ProcessIsolationCapability,
                    ProviderContractKind.ExecutionUnit,
                    available,
                    "Workloads use provider-owned execution-unit and process boundaries."),
                Fact(
                    HostContainersProviderDescriptor.ContainerIsolationCapability,
                    ProviderContractKind.ExecutionUnit,
                    available,
                    "Workloads use the selected host container engine."),
                Fact(
                    HostContainersProviderDescriptor.SharedHostKernelCapability,
                    ProviderContractKind.ExecutionUnit,
                    !supported
                        ? CapabilityState.Unsupported
                        : OperatingSystem.IsLinux()
                            ? CapabilityState.Supported
                            : CapabilityState.Degraded,
                    OperatingSystem.IsLinux()
                        ? "The selected local engine executes workloads in Linux containers sharing the Linux host kernel."
                        : "The container engine may execute Linux workloads in an engine-managed VM; direct macOS kernel sharing is not claimed."),
                Fact(
                    HostContainersProviderDescriptor.HardwareVirtualizationCapability,
                    ProviderContractKind.RuntimeHost,
                    OperatingSystem.IsLinux()
                        ? CapabilityState.Unsupported
                        : CapabilityState.Degraded,
                    OperatingSystem.IsLinux()
                        ? "The provider does not create a hardware-virtualized boundary on Linux."
                        : "A container engine may use a hardware-virtualized VM, but the provider does not own or prove that boundary."),
                Fact(
                    HostContainersProviderDescriptor.GuestAgentBoundaryCapability,
                    ProviderContractKind.RuntimeHost,
                    CapabilityState.Unsupported,
                    "The Local provider has no guest or guest-agent boundary."),
                Fact(
                    HostContainersProviderDescriptor.MediatedEngineAuthorityCapability,
                    ProviderContractKind.AuthorityBinding |
                    ProviderContractKind.EngineControlPlane,
                    available,
                    "Engine authority remains inside the provider and is leased to HPDOS operations."),
                Fact(
                    HostContainersProviderDescriptor.HostLocalEndpointCapability,
                    ProviderContractKind.EndpointPublication,
                    available,
                    "HPD-owned loopback TCP publications mediate private App service access."),
                SandboxFact(
                    StandardSandboxEnvironmentCapabilities.ProcessExecution,
                    sandboxAvailable,
                    "Commands execute through Docker exec inside the retained sandbox container."),
                SandboxFact(
                    StandardSandboxEnvironmentCapabilities.ProcessOutput,
                    sandboxAvailable,
                    "Docker exec standard output and standard error are captured and streamed."),
                SandboxFact(
                    StandardSandboxEnvironmentCapabilities.ProcessCancellation,
                    sandboxAvailable,
                    "Retained process handles support stop and process-tree cleanup."),
                SandboxFact(
                    StandardSandboxEnvironmentCapabilities.RetainedProcessObservation,
                    sandboxAvailable,
                    "The provider retains process status and output."),
                SandboxFact(
                    StandardSandboxEnvironmentCapabilities.BackgroundProcessOwnership,
                    CapabilityState.Unsupported,
                    "A detached Docker exec process cannot yet be adopted and owned through a scoped container-side process authority."),
                SandboxFact(
                    StandardSandboxEnvironmentCapabilities.ProcessOutputReplay,
                    sandboxAvailable,
                    "Completed output remains replayable by the retained process handle."),
                SandboxFact(
                    StandardSandboxEnvironmentCapabilities.FilesystemReadRestrictions,
                    sandboxAvailable,
                    "The container root filesystem and validated bind mounts define visible content."),
                SandboxFact(
                    StandardSandboxEnvironmentCapabilities.FilesystemWriteRestrictions,
                    sandboxAvailable,
                    "A read-only root filesystem and mount flags enforce writes."),
                SandboxFact(
                    StandardSandboxEnvironmentCapabilities.SymlinkHandling,
                    sandboxAvailable == CapabilityState.Supported
                        ? CapabilityState.Degraded
                        : sandboxAvailable,
                    "Workspace file access denies symlinks; the container process view preserves mount-provided symlinks."),
                SandboxFact(
                    StandardSandboxEnvironmentCapabilities.NetworkBlock,
                    sandboxAvailable,
                    "Network-none containers provide a blocked network namespace."),
                SandboxFact(
                    StandardSandboxEnvironmentCapabilities.UnixSocketRestriction,
                    sandboxAvailable,
                    "The host engine socket is never mounted into sandbox containers."),
                SandboxFact(
                    StandardSandboxEnvironmentCapabilities.EnvironmentFiltering,
                    sandboxAvailable,
                    "Only provider-validated environment entries are injected."),
                SandboxFact(
                    StandardSandboxEnvironmentCapabilities.ProcessIdentity,
                    sandboxAvailable,
                    "Docker exec applies the requested container user."),
                SandboxFact(
                    StandardSandboxEnvironmentCapabilities.StandardInput,
                    sandboxAvailable,
                    "Docker exec interactive stdin is connected to the process handle."),
                SandboxFact(
                    StandardSandboxEnvironmentCapabilities.ResourceLimits,
                    sandboxAvailable,
                    "Container CPU, memory, and PID limits are applied at creation."),
                SandboxFact(
                    StandardSandboxEnvironmentCapabilities.ContentProjection,
                    sandboxAvailable,
                    "Validated bind mounts realize directory content projections."),
                SandboxFact(
                    StandardSandboxEnvironmentCapabilities.SessionCleanup,
                    sandboxAvailable,
                    "Processes, projections, and the retained container are released in order."),
                .. UnsupportedSandboxFact(
                    StandardSandboxEnvironmentCapabilities.NetworkAllowlist,
                    "Container sandboxing currently supports blocked or engine-default networks only."),
                .. UnsupportedSandboxFact(
                    StandardSandboxEnvironmentCapabilities.NetworkFiltering,
                    "Container sandboxing does not mediate filtered egress."),
                .. UnsupportedSandboxFact(
                    StandardSandboxEnvironmentCapabilities.NetworkProxyMediation,
                    "Container sandboxing does not inject an HPD egress proxy."),
                .. UnsupportedSandboxFact(
                    StandardSandboxEnvironmentCapabilities.Pty,
                    "Retained-process Docker exec does not expose a PTY in this provider."),
                .. UnsupportedSandboxFact(
                    StandardSandboxEnvironmentCapabilities.LocalPortBinding,
                    "Sandbox containers do not publish local ports through an approved endpoint policy."),
                .. UnsupportedSandboxFact(
                    StandardSandboxEnvironmentCapabilities.ViolationObservation,
                    "Container policy violations are not exported as provider events."),
                .. WorkspaceFacts(sandboxAvailable),
            ],
            HostDependencies =
            [
                new HostDependencyFact(
                    new HostDependencyRef(
                        HostDependencyKind.ProviderDefined,
                        "local-container-engine"),
                    string.IsNullOrWhiteSpace(options.EngineSocketPath)
                        ? DependencyState.Missing
                        : DependencyState.Present,
                    Detail: string.IsNullOrWhiteSpace(options.EngineSocketPath)
                        ? "No explicit engine socket is configured; bounded well-known discovery may resolve one during activation."
                        : "An explicit local engine socket is configured."),
            ],
            PreflightChecks =
            [
                new ProviderPreflightCheck(
                    "host-platform",
                    supported
                        ? PreflightCheckState.Passed
                        : PreflightCheckState.Failed,
                    supported
                        ? DiagnosticSeverity.Info
                        : DiagnosticSeverity.Error),
            ],
        });
    }

    private static CapabilityFact Fact(
        CapabilityId id,
        ProviderContractKind appliesTo,
        CapabilityState state,
        string detail) =>
        new()
        {
            Id = id,
            Category = new CapabilityCategory("local-environment"),
            AppliesTo = appliesTo,
            State = state,
            Detail = detail,
        };

    private static CapabilityFact SandboxFact(
        CapabilityId id,
        CapabilityState state,
        string detail) =>
        new()
        {
            Id = id,
            Category = new CapabilityCategory("sandbox"),
            AppliesTo =
                ProviderContractKind.ExecutionUnit |
                ProviderContractKind.ProcessInvocation,
            State = state,
            Detail = detail,
        };

    private static CapabilityFact[] UnsupportedSandboxFact(
        CapabilityId id,
        string detail) =>
    [
        SandboxFact(id, CapabilityState.Unsupported, detail),
    ];

    private static CapabilityFact[] WorkspaceFacts(
        CapabilityState state) =>
        StandardWorkspaceEnvironmentCapabilities
            .RequiredFor(AccessMode.ReadWrite)
            .Select(id => new CapabilityFact
            {
                Id = id,
                Category =
                    new CapabilityCategory("workspace"),
                AppliesTo =
                    ProviderContractKind.WorkspaceFileStore,
                State = state,
                Detail =
                    "Workspace operations use a verified local bind source or provider-owned content through the retained container process lane.",
            })
            .ToArray();
}
