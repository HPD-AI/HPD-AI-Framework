using FluentAssertions;
using HPDAgent.Graph.Core.State;
using Xunit;

namespace HPD.Graph.Tests.State;

/// <summary>
/// Tests for ManagedContext execution metadata tracking.
/// </summary>
public class ManagedContextTests
{
    #region Step Tracking

    [Fact]
    public void CurrentStep_StartsAtZero()
    {
        // Arrange & Act
        var context = new ManagedContext();

        // Assert
        context.CurrentStep.Should().Be(0);
    }

    [Fact]
    public void IncrementStep_IncrementsCounter()
    {
        // Arrange
        var context = new ManagedContext();

        // Act - Use reflection to call internal method
        var method = typeof(ManagedContext).GetMethod("IncrementStep",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        method!.Invoke(context, null);
        method.Invoke(context, null);
        method.Invoke(context, null);

        // Assert
        context.CurrentStep.Should().Be(3);
    }

    [Fact]
    public void IncrementStep_ConcurrentCalls_ThreadSafe()
    {
        // Arrange
        var context = new ManagedContext();
        var method = typeof(ManagedContext).GetMethod("IncrementStep",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var tasks = new List<Task>();

        // Act - 100 concurrent increments
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => method!.Invoke(context, null)));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        context.CurrentStep.Should().Be(100);
    }

    #endregion

    #region Time Tracking

    [Fact]
    public void ElapsedTime_IncreasesOverTime()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var context = new ManagedContext(startTime);

        // Act
        Thread.Sleep(50);
        var elapsed = context.ElapsedTime;

        // Assert
        elapsed.TotalMilliseconds.Should().BeGreaterThanOrEqualTo(40); // Allow some margin
    }

    [Fact]
    public void RemainingTime_WithoutEstimates_ReturnsNull()
    {
        // Arrange
        var context = new ManagedContext();

        // Act
        var remaining = context.RemainingTime;

        // Assert
        remaining.Should().BeNull();
    }

    [Fact]
    public void RemainingTime_WithEstimates_CalculatesCorrectly()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow.AddSeconds(-10); // Started 10 seconds ago
        var context = new ManagedContext(startTime);

        // Use reflection to set internal state
        var setEstimated = typeof(ManagedContext).GetMethod("SetEstimatedTotalSteps",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var incrementStep = typeof(ManagedContext).GetMethod("IncrementStep",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        // Act - Set total to 10, complete 5 steps
        setEstimated!.Invoke(context, new object[] { 10 });
        for (int i = 0; i < 5; i++)
        {
            incrementStep!.Invoke(context, null);
        }

        var remaining = context.RemainingTime;

        // Assert
        remaining.Should().NotBeNull();
        // 10 seconds elapsed for 5 steps = 2 sec/step
        // 5 steps remaining = ~10 seconds
        remaining!.Value.TotalSeconds.Should().BeGreaterThan(8).And.BeLessThan(12);
    }

    [Fact]
    public void RemainingTime_WhenComplete_ReturnsZero()
    {
        // Arrange
        var context = new ManagedContext();

        var setEstimated = typeof(ManagedContext).GetMethod("SetEstimatedTotalSteps",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var incrementStep = typeof(ManagedContext).GetMethod("IncrementStep",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        // Act - Complete all steps
        setEstimated!.Invoke(context, new object[] { 5 });
        for (int i = 0; i < 5; i++)
        {
            incrementStep!.Invoke(context, null);
        }

        var remaining = context.RemainingTime;

        // Assert
        remaining.Should().NotBeNull();
        remaining!.Value.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void EstimatedTotalSteps_SetCorrectly()
    {
        // Arrange
        var context = new ManagedContext();
        var setEstimated = typeof(ManagedContext).GetMethod("SetEstimatedTotalSteps",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        // Act
        setEstimated!.Invoke(context, new object[] { 42 });

        // Assert
        context.EstimatedTotalSteps.Should().Be(42);
    }

    #endregion

    #region Metrics

    [Fact]
    public void RecordMetric_StoresNumericValue()
    {
        // Arrange
        var context = new ManagedContext();

        // Act
        context.RecordMetric("score", 95.5);

        // Assert
        context.Metrics.Should().ContainKey("score");
        context.Metrics["score"].Should().Be(95.5);
    }

    [Fact]
    public void RecordMetric_OverwritesExistingMetric()
    {
        // Arrange
        var context = new ManagedContext();
        context.RecordMetric("counter", 10);

        // Act
        context.RecordMetric("counter", 20);

        // Assert
        context.Metrics["counter"].Should().Be(20);
    }

    [Fact]
    public void RecordMetric_MultipleMetrics_StoresAll()
    {
        // Arrange
        var context = new ManagedContext();

        // Act
        context.RecordMetric("metric1", 1.0);
        context.RecordMetric("metric2", 2.0);
        context.RecordMetric("metric3", 3.0);

        // Assert
        context.Metrics.Should().HaveCount(3);
        context.Metrics["metric1"].Should().Be(1.0);
        context.Metrics["metric2"].Should().Be(2.0);
        context.Metrics["metric3"].Should().Be(3.0);
    }

    [Fact]
    public void RecordMetric_WithNullName_ThrowsArgumentException()
    {
        // Arrange
        var context = new ManagedContext();

        // Act
        var act = () => context.RecordMetric(null!, 10);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Metric name cannot be null or whitespace*");
    }

    [Fact]
    public void IncrementMetric_AddsToExistingMetric()
    {
        // Arrange
        var context = new ManagedContext();
        context.RecordMetric("count", 10);

        // Act
        context.IncrementMetric("count", 5);

        // Assert
        context.Metrics["count"].Should().Be(15);
    }

    [Fact]
    public void IncrementMetric_CreatesMetricIfNotExists()
    {
        // Arrange
        var context = new ManagedContext();

        // Act
        context.IncrementMetric("new_metric", 42);

        // Assert
        context.Metrics.Should().ContainKey("new_metric");
        context.Metrics["new_metric"].Should().Be(42);
    }

    [Fact]
    public void IncrementMetric_DefaultDelta_IncrementsBy1()
    {
        // Arrange
        var context = new ManagedContext();

        // Act
        context.IncrementMetric("counter");
        context.IncrementMetric("counter");
        context.IncrementMetric("counter");

        // Assert
        context.Metrics["counter"].Should().Be(3);
    }

    [Fact]
    public void IncrementMetric_WithNullName_ThrowsArgumentException()
    {
        // Arrange
        var context = new ManagedContext();

        // Act
        var act = () => context.IncrementMetric(null!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Metric name cannot be null or whitespace*");
    }

    [Fact]
    public void Metrics_ReturnsReadOnlyDictionary()
    {
        // Arrange
        var context = new ManagedContext();
        context.RecordMetric("test", 100);

        // Act
        var metrics = context.Metrics;

        // Assert
        metrics.Should().BeAssignableTo<IReadOnlyDictionary<string, double>>();
    }

    #endregion

    #region Concurrent Metrics

    [Fact]
    public void ConcurrentRecordMetric_ThreadSafe()
    {
        // Arrange
        var context = new ManagedContext();
        var tasks = new List<Task>();

        // Act - 50 tasks recording different metrics
        for (int i = 0; i < 50; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() => context.RecordMetric($"metric_{index}", index)));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        context.Metrics.Should().HaveCount(50);
        for (int i = 0; i < 50; i++)
        {
            context.Metrics[$"metric_{i}"].Should().Be(i);
        }
    }

    [Fact]
    public void ConcurrentIncrementMetric_ThreadSafe()
    {
        // Arrange
        var context = new ManagedContext();
        var tasks = new List<Task>();

        // Act - 100 concurrent increments of same metric
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => context.IncrementMetric("shared_counter", 1)));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        context.Metrics["shared_counter"].Should().Be(100);
    }

    #endregion

    #region IsLastNode

    [Fact]
    public void IsLastNode_DefaultValue_IsFalse()
    {
        // Arrange & Act
        var context = new ManagedContext();

        // Assert
        context.IsLastNode.Should().BeFalse();
    }

    [Fact]
    public void SetIsLastNode_UpdatesValue()
    {
        // Arrange
        var context = new ManagedContext();
        var setIsLastNode = typeof(ManagedContext).GetMethod("SetIsLastNode",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        // Act
        setIsLastNode!.Invoke(context, new object[] { true });

        // Assert
        context.IsLastNode.Should().BeTrue();
    }

    #endregion
}
