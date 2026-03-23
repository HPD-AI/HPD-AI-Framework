using HPDAgent.Graph.Abstractions.State;
using System.Collections.Concurrent;

namespace HPDAgent.Graph.Core.State;

/// <summary>
/// Implementation of IManagedContext for tracking execution metadata.
/// Thread-safe using ConcurrentDictionary for metrics.
/// </summary>
public sealed class ManagedContext : IManagedContext
{
    private readonly DateTimeOffset _startTime;
    private readonly ConcurrentDictionary<string, double> _metrics = new();
    private int _currentStep;
    private int? _estimatedTotalSteps;
    private bool _isLastNode;

    public int CurrentStep => _currentStep;
    public int? EstimatedTotalSteps => _estimatedTotalSteps;
    public bool IsLastNode => _isLastNode;

    public TimeSpan ElapsedTime => DateTimeOffset.UtcNow - _startTime;

    public TimeSpan? RemainingTime
    {
        get
        {
            if (_estimatedTotalSteps == null || _currentStep == 0)
            {
                return null;
            }

            var avgTimePerStep = ElapsedTime.TotalMilliseconds / _currentStep;
            var stepsRemaining = _estimatedTotalSteps.Value - _currentStep;

            if (stepsRemaining <= 0)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromMilliseconds(avgTimePerStep * stepsRemaining);
        }
    }

    public IReadOnlyDictionary<string, double> Metrics => _metrics;

    public ManagedContext(DateTimeOffset? startTime = null)
    {
        _startTime = startTime ?? DateTimeOffset.UtcNow;
    }

    public void RecordMetric(string name, double value)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Metric name cannot be null or whitespace.", nameof(name));
        }

        _metrics[name] = value;
    }

    public void IncrementMetric(string name, double delta = 1.0)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Metric name cannot be null or whitespace.", nameof(name));
        }

        _metrics.AddOrUpdate(name, delta, (_, current) => current + delta);
    }

    /// <summary>
    /// Increment the current step counter.
    /// Called by orchestrator after each node execution.
    /// </summary>
    internal void IncrementStep()
    {
        Interlocked.Increment(ref _currentStep);
    }

    /// <summary>
    /// Set the estimated total steps.
    /// Called by orchestrator after computing execution layers.
    /// </summary>
    internal void SetEstimatedTotalSteps(int totalSteps)
    {
        _estimatedTotalSteps = totalSteps;
    }

    /// <summary>
    /// Set whether this is the last node.
    /// Called by orchestrator before executing each node.
    /// </summary>
    internal void SetIsLastNode(bool isLastNode)
    {
        _isLastNode = isLastNode;
    }
}
