using Microsoft.Extensions.Logging;

namespace HPD.ML.Abstractions;

/// <summary>
/// Logging, RNG, cancellation, scheduling, device preferences.
/// Immutable after construction. Optional dependency.
/// </summary>
public interface IExecutionEnvironment
{
    ILogger Logger { get; }
    int? Seed { get; }
    CancellationToken CancellationToken { get; }
    IProgress<T> CreateProgress<T>(string name);
    TaskScheduler? Scheduler { get; }
    DevicePreference DefaultDevicePreference { get; }
    BackendSpec Backend { get; }
}

public sealed record BackendSpec(
    string Kind,
    string? Device = null,
    IReadOnlyDictionary<string, string>? Options = null)
{
    public static BackendSpec Default() => new("default");
    public static BackendSpec Cpu() => new("cpu");
    public static BackendSpec Blas(string? provider = null) => new("blas", provider);
    public static BackendSpec Mlx(string device = "gpu") => new("mlx", device);
    public static BackendSpec Pjrt(string plugin = "cpu") => new("pjrt", plugin);
    public static BackendSpec LightGbm() => new("lightgbm");

    public override string ToString()
        => Device is null ? Kind : $"{Kind}:{Device}";
}
