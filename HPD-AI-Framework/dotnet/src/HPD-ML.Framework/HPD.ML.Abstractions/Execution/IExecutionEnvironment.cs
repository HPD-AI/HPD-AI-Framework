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
    ComputeBackend ComputeBackend { get; }
}

public enum ComputeBackend
{
    Default,
    MKL,
    OneDAL
}
