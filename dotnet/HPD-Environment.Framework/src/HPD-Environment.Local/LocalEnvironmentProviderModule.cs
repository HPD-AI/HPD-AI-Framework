namespace HPD.Environment.Local;

using System.Runtime.InteropServices;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

public sealed class LocalEnvironmentProviderModule : IProviderModule
{
    private readonly LocalEnvironmentProviderOptions _options;
    private readonly ILocalEngineProbe _engineProbe;
    private readonly ILocalEngineNetworkClient _networkClient;
    private readonly LocalProviderState _state;

    public LocalEnvironmentProviderModule()
        : this(new LocalEnvironmentProviderOptions())
    {
    }

    public LocalEnvironmentProviderModule(
        LocalEnvironmentProviderOptions options)
        : this(options, engineProbe: null, networkClient: null)
    {
    }

    internal LocalEnvironmentProviderModule(
        LocalEnvironmentProviderOptions options,
        ILocalEngineProbe? engineProbe,
        ILocalEngineNetworkClient? networkClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _engineProbe = engineProbe ?? new LocalDockerEngineProbe(options);
        _networkClient =
            networkClient ?? new LocalDockerNetworkClient();
        _state = new LocalProviderState(_options);
    }

    public ProviderDescriptor Descriptor =>
        LocalEnvironmentProviderDescriptor.Create(_options);

    internal AuthorityAuditEvent[] GetAuthorityAuditEvents(
        string authorityId) =>
        _state.GetAuthorityAudit(authorityId);

    public void Register(IProviderRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddProviderCapabilityReporter(
            new LocalEnvironmentCapabilityReporter(_options));
        var hostProvider = new LocalRuntimeHostProvider(_state);
        builder.AddRuntimeHostProvider(hostProvider);
        builder.AddRuntimeHostResetProvider(hostProvider);
        builder.AddExecutionUnitProvider(
            new LocalExecutionUnitProvider(_state));
        builder.AddProcessProvider(
            new LocalProcessProvider(_state));
        builder.AddEngineControlPlaneProvider(
            new LocalEngineControlPlaneProvider(
                _state,
                _engineProbe));
        builder.AddAuthorityBindingProvider(
            new LocalAuthorityBindingProvider(_state));
        builder.AddEndpointPublicationProvider(
            new LocalEndpointPublicationProvider(_state));
        var networkProvider =
            new LocalNetworkProvider(_state, _networkClient);
        builder.AddNetworkProvider(networkProvider);
        builder.AddNetworkMembershipProvider(networkProvider);
        builder.AddServiceDiscoveryProvider(
            new LocalServiceDiscoveryProvider(_state));
        var storageProvider = new LocalStorageProvider(_state);
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

public static class LocalEnvironmentRegistrationExtensions
{
    public static EnvironmentProviderRegistry RegisterLocalEnvironmentProvider(
        this EnvironmentProviderRegistry registry,
        LocalEnvironmentProviderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.RegisterModule(
            new LocalEnvironmentProviderModule(
                options ?? new LocalEnvironmentProviderOptions()));
        return registry;
    }
}

public static class LocalEnvironmentProviderDescriptor
{
    public static readonly ProviderId ProviderId =
        new("hpd.execution.local");

    public const string CapabilityPrefix = "hpd.execution.local";
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
        LocalEnvironmentProviderOptions options)
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

internal sealed class LocalEnvironmentCapabilityReporter(
    LocalEnvironmentProviderOptions options)
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
            LocalEnvironmentProviderDescriptor.CurrentPlatform();
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
        return ValueTask.FromResult(new ProviderCapabilityReport
        {
            ProviderId = providerId,
            ObservedAt = DateTimeOffset.UtcNow,
            HostPlatform = host,
            Capabilities =
            [
                Fact(
                    LocalEnvironmentProviderDescriptor.NativeHostCapability,
                    ProviderContractKind.RuntimeHost,
                    available,
                    "The current operating-system host is the logical runtime host."),
                Fact(
                    LocalEnvironmentProviderDescriptor.ProcessIsolationCapability,
                    ProviderContractKind.ExecutionUnit,
                    available,
                    "Workloads use provider-owned execution-unit and process boundaries."),
                Fact(
                    LocalEnvironmentProviderDescriptor.ContainerIsolationCapability,
                    ProviderContractKind.ExecutionUnit,
                    available,
                    "Workloads use the selected host container engine."),
                Fact(
                    LocalEnvironmentProviderDescriptor.SharedHostKernelCapability,
                    ProviderContractKind.ExecutionUnit,
                    supported
                        ? CapabilityState.Supported
                        : CapabilityState.Unsupported,
                    "Local containers share the host kernel."),
                Fact(
                    LocalEnvironmentProviderDescriptor.HardwareVirtualizationCapability,
                    ProviderContractKind.RuntimeHost,
                    CapabilityState.Unsupported,
                    "The Local provider does not create a hardware-virtualized boundary."),
                Fact(
                    LocalEnvironmentProviderDescriptor.GuestAgentBoundaryCapability,
                    ProviderContractKind.RuntimeHost,
                    CapabilityState.Unsupported,
                    "The Local provider has no guest or guest-agent boundary."),
                Fact(
                    LocalEnvironmentProviderDescriptor.MediatedEngineAuthorityCapability,
                    ProviderContractKind.AuthorityBinding |
                    ProviderContractKind.EngineControlPlane,
                    available,
                    "Engine authority remains inside the provider and is leased to HPDOS operations."),
                Fact(
                    LocalEnvironmentProviderDescriptor.HostLocalEndpointCapability,
                    ProviderContractKind.EndpointPublication,
                    available,
                    "HPD-owned loopback TCP publications mediate private App service access."),
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
}
