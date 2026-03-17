using FluentAssertions;
using HPDAgent.Graph.Abstractions.Graph;
using Xunit;

namespace HPDAgent.Graph.Tests.Execution;

public class RetryJitterTests
{
    [Fact]
    public void JitteredExponential_ProducesRandomizedDelays()
    {
        var policy = new RetryPolicy
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromSeconds(1),
            Strategy = BackoffStrategy.JitteredExponential
        };

        var delays = Enumerable.Range(1, 100)
            .Select(i => policy.GetDelay(1).TotalMilliseconds)
            .ToList();

        // All delays should be between 0.5s and 1.5s (50-150% jitter)
        delays.Should().OnlyContain(d => d >= 500 && d <= 1500);

        // Delays should vary (not all identical) - at least 10 unique values
        delays.Distinct().Count().Should().BeGreaterThan(10);
    }

    [Fact]
    public void JitteredExponential_Attempt2_ProducesCorrectRange()
    {
        var policy = new RetryPolicy
        {
            MaxAttempts = 5,
            InitialDelay = TimeSpan.FromSeconds(1),
            Strategy = BackoffStrategy.JitteredExponential
        };

        var delays = Enumerable.Range(1, 100)
            .Select(i => policy.GetDelay(2).TotalMilliseconds)
            .ToList();

        // Attempt 2: base delay = 2s, jittered = 1.0s to 3.0s
        delays.Should().OnlyContain(d => d >= 1000 && d <= 3000);
        delays.Distinct().Count().Should().BeGreaterThan(10);
    }

    [Fact]
    public void JitteredExponential_Attempt3_ProducesCorrectRange()
    {
        var policy = new RetryPolicy
        {
            MaxAttempts = 5,
            InitialDelay = TimeSpan.FromSeconds(1),
            Strategy = BackoffStrategy.JitteredExponential
        };

        var delays = Enumerable.Range(1, 100)
            .Select(i => policy.GetDelay(3).TotalMilliseconds)
            .ToList();

        // Attempt 3: base delay = 4s, jittered = 2.0s to 6.0s
        delays.Should().OnlyContain(d => d >= 2000 && d <= 6000);
        delays.Distinct().Count().Should().BeGreaterThan(10);
    }

    [Fact]
    public void JitteredExponential_RespectsMaxDelay()
    {
        var policy = new RetryPolicy
        {
            MaxAttempts = 10,
            InitialDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(5),
            Strategy = BackoffStrategy.JitteredExponential
        };

        var delays = Enumerable.Range(1, 100)
            .Select(i => policy.GetDelay(10).TotalMilliseconds) // Attempt 10 would be huge without cap
            .ToList();

        // All delays should be capped at MaxDelay (5s)
        delays.Should().OnlyContain(d => d <= 5000);
    }

    [Fact]
    public void JitteredExponential_DifferentFromRegularExponential()
    {
        var jitteredPolicy = new RetryPolicy
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromSeconds(1),
            Strategy = BackoffStrategy.JitteredExponential
        };

        var regularPolicy = new RetryPolicy
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromSeconds(1),
            Strategy = BackoffStrategy.Exponential
        };

        var jitteredDelays = Enumerable.Range(1, 100)
            .Select(i => jitteredPolicy.GetDelay(1).TotalMilliseconds)
            .ToList();

        var regularDelay = regularPolicy.GetDelay(1).TotalMilliseconds;

        // Jittered delays should vary, regular should be constant
        jitteredDelays.Distinct().Count().Should().BeGreaterThan(10);
        jitteredDelays.Should().Contain(d => d != regularDelay); // At least some should differ
    }

    [Fact]
    public void JitteredExponential_AverageDelayApproximatesBase()
    {
        var policy = new RetryPolicy
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromSeconds(1),
            Strategy = BackoffStrategy.JitteredExponential
        };

        var delays = Enumerable.Range(1, 1000)
            .Select(i => policy.GetDelay(1).TotalMilliseconds)
            .ToList();

        var averageDelay = delays.Average();

        // Average should be close to 1000ms (base delay)
        // With 50-150% jitter, average should be around 100% (1000ms)
        averageDelay.Should().BeInRange(950, 1050); // Allow 5% variance
    }

    [Fact]
    public void ConstantStrategy_StillWorks()
    {
        var policy = new RetryPolicy
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromSeconds(2),
            Strategy = BackoffStrategy.Constant
        };

        policy.GetDelay(1).Should().Be(TimeSpan.FromSeconds(2));
        policy.GetDelay(2).Should().Be(TimeSpan.FromSeconds(2));
        policy.GetDelay(3).Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ExponentialStrategy_StillWorks()
    {
        var policy = new RetryPolicy
        {
            MaxAttempts = 5,
            InitialDelay = TimeSpan.FromSeconds(1),
            Strategy = BackoffStrategy.Exponential
        };

        policy.GetDelay(1).Should().Be(TimeSpan.FromSeconds(1));
        policy.GetDelay(2).Should().Be(TimeSpan.FromSeconds(2));
        policy.GetDelay(3).Should().Be(TimeSpan.FromSeconds(4));
        policy.GetDelay(4).Should().Be(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void LinearStrategy_StillWorks()
    {
        var policy = new RetryPolicy
        {
            MaxAttempts = 5,
            InitialDelay = TimeSpan.FromSeconds(1),
            Strategy = BackoffStrategy.Linear
        };

        policy.GetDelay(1).Should().Be(TimeSpan.FromSeconds(1));
        policy.GetDelay(2).Should().Be(TimeSpan.FromSeconds(2));
        policy.GetDelay(3).Should().Be(TimeSpan.FromSeconds(3));
        policy.GetDelay(4).Should().Be(TimeSpan.FromSeconds(4));
    }
}
