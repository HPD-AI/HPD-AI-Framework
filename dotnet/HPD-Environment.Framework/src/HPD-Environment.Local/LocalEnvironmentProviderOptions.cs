namespace HPD.Environment.Local;

using HPD.Environment.Contracts;
using System.Text.Json.Serialization;

public enum LocalDurableVolumeBackendKind
{
    ObservedDirectory,
    PlatformHardQuota,
}

public sealed record LocalEnvironmentProviderOptions
{
    public string? EngineSocketPath { get; init; }
    public string? WorkloadStateRoot { get; init; }
    public string? StorageRoot { get; init; }
    public string? EngineDataRootPath { get; init; }
    public LocalDurableVolumeBackendKind DurableVolumeBackend { get; init; } =
        LocalDurableVolumeBackendKind.ObservedDirectory;
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
}
