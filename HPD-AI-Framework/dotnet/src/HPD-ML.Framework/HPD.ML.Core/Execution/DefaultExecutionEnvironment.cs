namespace HPD.ML.Core;

using HPD.Events;
using HPD.Events.Core;
using HPD.ML.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Default IExecutionEnvironment backed by HPD-Events.
/// Provides logging, deterministic RNG, cancellation, and progress routing.
/// </summary>
public sealed class DefaultExecutionEnvironment : IExecutionEnvironment
{
    private readonly IEventCoordinator? _coordinator;

    public DefaultExecutionEnvironment(
        ILogger? logger = null,
        int? seed = null,
        CancellationToken cancellationToken = default,
        TaskScheduler? scheduler = null,
        DevicePreference? defaultDevicePreference = null,
        ComputeBackend computeBackend = ComputeBackend.Default,
        IEventCoordinator? coordinator = null)
    {
        Logger = logger ?? NullLogger.Instance;
        Seed = seed;
        CancellationToken = cancellationToken;
        Scheduler = scheduler;
        DefaultDevicePreference = defaultDevicePreference ?? new DevicePreference(null);
        ComputeBackend = computeBackend;
        _coordinator = coordinator;
    }

    public ILogger Logger { get; }
    public int? Seed { get; }
    public CancellationToken CancellationToken { get; }
    public TaskScheduler? Scheduler { get; }
    public DevicePreference DefaultDevicePreference { get; }
    public ComputeBackend ComputeBackend { get; }

    public IProgress<T> CreateProgress<T>(string name)
        => _coordinator is not null
            ? new EventProgress<T>(name, _coordinator)
            : new Progress<T>();

    /// <summary>Create a ProgressSubject wired to this environment's event coordinator.</summary>
    public ProgressSubject CreateProgressSubject() => new(_coordinator);

    /// <summary>Create a child environment with a child event coordinator for hierarchical bubbling.</summary>
    public DefaultExecutionEnvironment CreateChild(
        ILogger? logger = null,
        int? seed = null)
    {
        IEventCoordinator? childCoordinator = null;
        if (_coordinator is not null)
        {
            childCoordinator = new EventCoordinator();
            childCoordinator.SetParent(_coordinator);
        }

        return new DefaultExecutionEnvironment(
            logger ?? Logger,
            seed ?? (Seed.HasValue ? Seed.Value + 1 : null),
            CancellationToken,
            Scheduler,
            DefaultDevicePreference,
            ComputeBackend,
            childCoordinator);
    }
}

internal sealed class EventProgress<T> : IProgress<T>
{
    private readonly string _name;
    private readonly IEventCoordinator _coordinator;

    public EventProgress(string name, IEventCoordinator coordinator)
    {
        _name = name;
        _coordinator = coordinator;
    }

    public void Report(T value)
    {
        _coordinator.Emit(new ProgressReportEvent(_name, value));
    }
}

internal sealed record ProgressReportEvent : Event
{
    public string Name { get; }
    public object? Value { get; }

    public ProgressReportEvent(string name, object? value)
    {
        Name = name;
        Value = value;
        Kind = EventKind.Diagnostic;
        Priority = EventPriority.Background;
    }
}
