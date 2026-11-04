namespace HPD.Environment.HostContainers;

using HPD.Environment.Contracts;
using System.Text.Json.Serialization;

public enum HostContainersDurableVolumeBackendKind
{
    ObservedDirectory,
    PlatformHardQuota,
}

public sealed record HostContainersProviderOptions
{
    public string? EngineSocketPath { get; init; }
    public string? WorkloadStateRoot { get; init; }
    public string? StorageRoot { get; init; }
    public string? EngineDataRootPath { get; init; }
    public HostContainersDurableVolumeBackendKind DurableVolumeBackend { get; init; } =
        HostContainersDurableVolumeBackendKind.ObservedDirectory;
    [JsonIgnore]
    public IStorageBackupKeyProvider? BackupKeyProvider { get; init; }
    public string? DockerCliPath { get; init; }
    public string? DockerComposeCliPath { get; init; }
    public EngineControlPlaneKind EngineKind { get; init; } =
        EngineControlPlaneKind.DockerCompatible;
    public EngineApiKind EngineApi { get; init; } =
        EngineApiKind.DockerCompatible;
    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public bool AllowRootfulEngine { get; init; } = true;
    public bool EnableWellKnownSocketDiscovery { get; init; } = true;
    public IReadOnlyList<string> AllowedHostExecutableRoots { get; init; } =
        Array.Empty<string>();
    public IReadOnlyList<string> AllowedHostEnvironmentVariables { get; init; } =
        Array.Empty<string>();
}
