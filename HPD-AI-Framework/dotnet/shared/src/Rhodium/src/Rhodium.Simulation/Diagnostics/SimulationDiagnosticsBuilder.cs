namespace Rhodium.Simulation.Diagnostics;

/// <summary>Integer counter emitted by a simulation module.</summary>
/// <param name="ModuleName">Module type or logical module name.</param>
/// <param name="CounterName">Counter name.</param>
/// <param name="Value">Counter value.</param>
public sealed record SimulationModuleCounter(
    string ModuleName,
    string CounterName,
    long Value);

/// <summary>Floating-point metric emitted by a simulation module.</summary>
/// <param name="ModuleName">Module type or logical module name.</param>
/// <param name="MetricName">Metric name.</param>
/// <param name="Value">Metric value.</param>
public sealed record SimulationModuleMetric(
    string ModuleName,
    string MetricName,
    double Value);

/// <summary>Text diagnostic emitted by a simulation module.</summary>
/// <param name="ModuleName">Module type or logical module name.</param>
/// <param name="Code">Stable diagnostic code.</param>
/// <param name="Message">Human-readable diagnostic message.</param>
public sealed record SimulationModuleMessage(
    string ModuleName,
    string Code,
    string Message);

/// <summary>
/// Builder exposed to modules for structured run diagnostics.
/// </summary>
public ref struct SimulationDiagnosticsBuilder
{
    private readonly List<SimulationModuleCounter> _counters;
    private readonly List<SimulationModuleMetric> _metrics;
    private readonly List<SimulationModuleMessage> _messages;

    internal SimulationDiagnosticsBuilder(
        List<SimulationModuleCounter> counters,
        List<SimulationModuleMetric> metrics,
        List<SimulationModuleMessage> messages)
    {
        _counters = counters;
        _metrics = metrics;
        _messages = messages;
    }

    /// <summary>Add a module counter.</summary>
    public void AddModuleCounter(
        string moduleName,
        string counterName,
        long value)
        => _counters.Add(new SimulationModuleCounter(moduleName, counterName, value));

    /// <summary>Add a module metric.</summary>
    public void AddModuleMetric(
        string moduleName,
        string metricName,
        double value)
        => _metrics.Add(new SimulationModuleMetric(moduleName, metricName, value));

    /// <summary>Add a module message.</summary>
    public void AddModuleMessage(
        string moduleName,
        string code,
        string message)
        => _messages.Add(new SimulationModuleMessage(moduleName, code, message));
}
