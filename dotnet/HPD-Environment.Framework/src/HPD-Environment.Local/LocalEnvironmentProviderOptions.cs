namespace HPD.Environment.Local;

using HPD.Environment.Contracts;

public sealed record LocalEnvironmentProviderOptions
{
    public string? EngineSocketPath { get; init; }
    public string? WorkloadStateRoot { get; init; }
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
