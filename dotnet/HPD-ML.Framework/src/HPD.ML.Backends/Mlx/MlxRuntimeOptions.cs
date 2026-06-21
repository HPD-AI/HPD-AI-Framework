namespace HPD.ML.Backends.Mlx;

public sealed record MlxRuntimeOptions
{
    public string? NativeLibraryPath { get; init; }
    public string? SearchRoot { get; init; }
    public MlxDeviceKind Device { get; init; } = MlxDeviceKind.Gpu;
    public bool AllowCpuFallback { get; init; } = true;
}

public enum MlxDeviceKind
{
    Cpu,
    Gpu
}

