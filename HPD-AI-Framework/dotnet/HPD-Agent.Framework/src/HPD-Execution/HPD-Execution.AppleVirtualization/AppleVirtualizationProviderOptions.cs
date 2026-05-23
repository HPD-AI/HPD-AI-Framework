using HPD.Execution.AppleVirtualization.Protocol;
using HPD.Execution.Contracts;

namespace HPD.Execution.AppleVirtualization;

public sealed record AppleVirtualizationProviderOptions
{
    public string HelperPath { get; init; } = AppleVirtualizationProviderDescriptor.HelperExecutableName;
    public IReadOnlyList<string> HelperArguments { get; init; } = Array.Empty<string>();
    public AppleVirtualizationHelperTransportMode HelperTransportMode { get; init; } = AppleVirtualizationHelperTransportMode.StdIo;
    public string? StateRoot { get; init; }
    public TimeSpan HelperStartupTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan HelperStopTimeout { get; init; } = TimeSpan.FromSeconds(2);
    public int StartupStderrCaptureBytes { get; init; } = 4096;
    public int DefaultCpuCores { get; init; } = 4;
    public long DefaultMemoryBytes { get; init; } = 4L * 1024 * 1024 * 1024;
    public long DefaultDiskBytes { get; init; } = 32L * 1024 * 1024 * 1024;
    public AppleVirtualizationGuestImageOptions GuestImage { get; init; } = new();
    public AppleVirtualizationEngineBootstrapOptions EngineBootstrap { get; init; } = new();
    public AppleVirtualizationProviderFeatureGates FeatureGates { get; init; } = new();
}

public enum AppleVirtualizationHelperTransportMode
{
    StdIo,
    UnixSocket,
    InMemoryFake,
}

public sealed record AppleVirtualizationProviderFeatureGates
{
    public bool EnableInMemoryFakeHelper { get; init; }
    public bool EnableRealHelperActivation { get; init; }
    public bool EnableRealVmBoot { get; init; }
    public bool EnableVmConfigurationValidation { get; init; }
    public bool EnableNetworkResources { get; init; }
    public bool EnableEndpointPublication { get; init; }
    public bool EnableAuthorityBinding { get; init; }
    public bool EnableEngineControlPlane { get; init; }
    public bool EnableArtifactAndRootfsProviders { get; init; }
    public bool EnableFunctionLanes { get; init; }
}

public enum AppleVirtualizationGuestBootLoaderKind
{
    LinuxBootLoader,
    Efi,
}

public enum AppleVirtualizationGuestArchitectureExpectation
{
    HostNative,
    Arm64,
    X64,
}

public enum AppleVirtualizationGuestImageConfigurationState
{
    Complete,
    MissingRequiredBootInputs,
}

public sealed record AppleVirtualizationGuestImageOptions
{
    public string? BundleRoot { get; init; }
    public AppleVirtualizationGuestBootLoaderKind BootLoader { get; init; } = AppleVirtualizationGuestBootLoaderKind.LinuxBootLoader;
    public string? KernelPath { get; init; }
    public string? InitrdPath { get; init; }
    public string? KernelCommandLine { get; init; }
    public string? DiskImagePath { get; init; }
    public string? EfiVariableStorePath { get; init; }
    public string? SerialLogPath { get; init; }
    public AppleVirtualizationGuestArchitectureExpectation Architecture { get; init; } = AppleVirtualizationGuestArchitectureExpectation.HostNative;
    public bool ExpectVirtiofsSupport { get; init; } = true;
    public string? ExpectedGuestAgentVersion { get; init; }
    public string? GuestAgentConfigPath { get; init; }
    public string? GuestAgentBootstrapPath { get; init; }
    public string? GuestAgentBootstrapInlinePayloadRef { get; init; }
    public IReadOnlyList<AppleVirtualizationGuestSharedDirectoryOptions> SharedDirectories { get; init; } =
        Array.Empty<AppleVirtualizationGuestSharedDirectoryOptions>();

    public AppleVirtualizationGuestImageConfigurationState GetConfigurationState()
    {
        if (string.IsNullOrWhiteSpace(DiskImagePath))
        {
            return AppleVirtualizationGuestImageConfigurationState.MissingRequiredBootInputs;
        }

        return BootLoader switch
        {
            AppleVirtualizationGuestBootLoaderKind.LinuxBootLoader
                when string.IsNullOrWhiteSpace(KernelPath) => AppleVirtualizationGuestImageConfigurationState.MissingRequiredBootInputs,
            AppleVirtualizationGuestBootLoaderKind.Efi
                when string.IsNullOrWhiteSpace(EfiVariableStorePath) => AppleVirtualizationGuestImageConfigurationState.MissingRequiredBootInputs,
            _ => AppleVirtualizationGuestImageConfigurationState.Complete,
        };
    }
}

public sealed record AppleVirtualizationGuestSharedDirectoryOptions
{
    public required string Tag { get; init; }
    public required string HostPath { get; init; }
    public bool ReadOnly { get; init; } = true;
}

public sealed record AppleVirtualizationEngineBootstrapOptions
{
    public bool Enabled { get; init; }
    public bool AuthorityModeConfigured { get; init; }
    public EngineControlPlaneKind Kind { get; init; } = EngineControlPlaneKind.DockerCompatible;
    public EngineApiKind Api { get; init; } = EngineApiKind.DockerCompatible;
    public EngineAuthorityMode AuthorityMode { get; init; } = EngineAuthorityMode.Rootless;
    public EngineImageStoreMode ImageStore { get; init; } = EngineImageStoreMode.ProviderManaged;
    public EngineWorkloadAdoptionMode WorkloadAdoption { get; init; } = EngineWorkloadAdoptionMode.None;
    public AppleVirtualizationEngineObservationState? ScriptedObservationState { get; init; }
    public AppleVirtualizationEngineProvisioningOptions Provisioning { get; init; } = new();
}

public sealed record AppleVirtualizationEngineProvisioningOptions
{
    public const int DefaultMaxCapturedOutputBytes = 4096;

    public bool Enabled { get; init; }
    public bool AllowPackageInstall { get; init; }
    public bool AllowServiceEnablement { get; init; }
    public TimeSpan ProvisioningTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public int MaxCapturedOutputBytes { get; init; } = DefaultMaxCapturedOutputBytes;
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
