using FluentAssertions;
using HPDAgent.Graph.Abstractions.Graph;
using Xunit;

namespace HPD.Graph.Tests.Policies;

/// <summary>
/// Tests for RetryPolicy backoff strategies and exception filtering.
/// </summary>
public class RetryPolicyTests
{
    #region Backoff Strategy Tests

    [Fact]
    public void GetDelay_ConstantBackoff_ReturnsSameDelayForAllAttempts()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxAttempts = 5,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            Strategy = BackoffStrategy.Constant
        };

        // Act & Assert
        policy.GetDelay(1).Should().Be(TimeSpan.FromMilliseconds(100));
        policy.GetDelay(2).Should().Be(TimeSpan.FromMilliseconds(100));
        policy.GetDelay(3).Should().Be(TimeSpan.FromMilliseconds(100));
        policy.GetDelay(10).Should().Be(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void GetDelay_ExponentialBackoff_DoublesEachTime()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxAttempts = 5,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            Strategy = BackoffStrategy.Exponential
        };

        // Act & Assert
        // Attempt 1: 100 * 2^0 = 100ms
        policy.GetDelay(1).Should().Be(TimeSpan.FromMilliseconds(100));
        // Attempt 2: 100 * 2^1 = 200ms
        policy.GetDelay(2).Should().Be(TimeSpan.FromMilliseconds(200));
        // Attempt 3: 100 * 2^2 = 400ms
        policy.GetDelay(3).Should().Be(TimeSpan.FromMilliseconds(400));
        // Attempt 4: 100 * 2^3 = 800ms
        policy.GetDelay(4).Should().Be(TimeSpan.FromMilliseconds(800));
    }

    [Fact]
    public void GetDelay_LinearBackoff_IncreasesLinearly()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxAttempts = 5,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            Strategy = BackoffStrategy.Linear
        };

        // Act & Assert
        // Attempt 1: 100 * 1 = 100ms
        policy.GetDelay(1).Should().Be(TimeSpan.FromMilliseconds(100));
        // Attempt 2: 100 * 2 = 200ms
        policy.GetDelay(2).Should().Be(TimeSpan.FromMilliseconds(200));
        // Attempt 3: 100 * 3 = 300ms
        policy.GetDelay(3).Should().Be(TimeSpan.FromMilliseconds(300));
        // Attempt 4: 100 * 4 = 400ms
        policy.GetDelay(4).Should().Be(TimeSpan.FromMilliseconds(400));
    }

    [Fact]
    public void GetDelay_MaxDelay_CapsExponentialGrowth()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxAttempts = 10,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            Strategy = BackoffStrategy.Exponential,
            MaxDelay = TimeSpan.FromMilliseconds(500)
        };

        // Act & Assert
        policy.GetDelay(1).Should().Be(TimeSpan.FromMilliseconds(100));  // 100ms
        policy.GetDelay(2).Should().Be(TimeSpan.FromMilliseconds(200));  // 200ms
        policy.GetDelay(3).Should().Be(TimeSpan.FromMilliseconds(400));  // 400ms
        policy.GetDelay(4).Should().Be(TimeSpan.FromMilliseconds(500));  // Capped at 500ms
        policy.GetDelay(5).Should().Be(TimeSpan.FromMilliseconds(500));  // Still 500ms
        policy.GetDelay(10).Should().Be(TimeSpan.FromMilliseconds(500)); // Still 500ms
    }

    [Fact]
    public void GetDelay_MaxDelay_CapsLinearGrowth()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxAttempts = 10,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            Strategy = BackoffStrategy.Linear,
            MaxDelay = TimeSpan.FromMilliseconds(350)
        };

        // Act & Assert
        policy.GetDelay(1).Should().Be(TimeSpan.FromMilliseconds(100));  // 100ms
        policy.GetDelay(2).Should().Be(TimeSpan.FromMilliseconds(200));  // 200ms
        policy.GetDelay(3).Should().Be(TimeSpan.FromMilliseconds(300));  // 300ms
        policy.GetDelay(4).Should().Be(TimeSpan.FromMilliseconds(350));  // Capped at 350ms
        policy.GetDelay(5).Should().Be(TimeSpan.FromMilliseconds(350));  // Still 350ms
    }

    [Fact]
    public void GetDelay_AttemptZero_ReturnsZero()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            Strategy = BackoffStrategy.Exponential
        };

        // Act
        var delay = policy.GetDelay(0);

        // Assert
        delay.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void GetDelay_NegativeAttempt_ReturnsZero()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            Strategy = BackoffStrategy.Exponential
        };

        // Act
        var delay = policy.GetDelay(-1);

        // Assert
        delay.Should().Be(TimeSpan.Zero);
    }

    #endregion

    #region Exception Filtering Tests

    [Fact]
    public void ShouldRetry_NoFilter_ReturnsTrueForAllExceptions()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            RetryableExceptions = null  // No filter
        };

        // Act & Assert
        policy.ShouldRetry(new InvalidOperationException()).Should().BeTrue();
        policy.ShouldRetry(new ArgumentException()).Should().BeTrue();
        policy.ShouldRetry(new TimeoutException()).Should().BeTrue();
        policy.ShouldRetry(new Exception()).Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_EmptyFilter_ReturnsTrueForAllExceptions()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            RetryableExceptions = new List<Type>()  // Empty list
        };

        // Act & Assert
        policy.ShouldRetry(new InvalidOperationException()).Should().BeTrue();
        policy.ShouldRetry(new ArgumentException()).Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_MatchingExceptionType_ReturnsTrue()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            RetryableExceptions = new List<Type> { typeof(TimeoutException) }
        };

        // Act & Assert
        policy.ShouldRetry(new TimeoutException()).Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_NonMatchingExceptionType_ReturnsFalse()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            RetryableExceptions = new List<Type> { typeof(TimeoutException) }
        };

        // Act & Assert
        policy.ShouldRetry(new InvalidOperationException()).Should().BeFalse();
        policy.ShouldRetry(new ArgumentException()).Should().BeFalse();
    }

    [Fact]
    public void ShouldRetry_DerivedExceptionType_ReturnsTrue()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            RetryableExceptions = new List<Type> { typeof(ArgumentException) }
        };

        // Act - ArgumentNullException derives from ArgumentException
        var shouldRetry = policy.ShouldRetry(new ArgumentNullException());

        // Assert
        shouldRetry.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_MultipleRetryableTypes_MatchesAny()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            RetryableExceptions = new List<Type>
            {
                typeof(TimeoutException),
                typeof(InvalidOperationException),
                typeof(HttpRequestException)
            }
        };

        // Act & Assert
        policy.ShouldRetry(new TimeoutException()).Should().BeTrue();
        policy.ShouldRetry(new InvalidOperationException()).Should().BeTrue();
        policy.ShouldRetry(new HttpRequestException()).Should().BeTrue();
        policy.ShouldRetry(new ArgumentException()).Should().BeFalse();
    }

    [Fact]
    public void ShouldRetry_BaseExceptionType_MatchesDerived()
    {
        // Arrange - Register base Exception type
        var policy = new RetryPolicy
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            RetryableExceptions = new List<Type> { typeof(Exception) }
        };

        // Act & Assert - All exceptions derive from Exception
        policy.ShouldRetry(new InvalidOperationException()).Should().BeTrue();
        policy.ShouldRetry(new ArgumentException()).Should().BeTrue();
        policy.ShouldRetry(new TimeoutException()).Should().BeTrue();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void RetryPolicy_MaxAttemptsOne_MeansSingleAttemptNoRetries()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxAttempts = 1,
            InitialDelay = TimeSpan.FromMilliseconds(100)
        };

        // Act & Assert - Attempt 1 (retry 1) should still return delay
        policy.GetDelay(1).Should().Be(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void RetryPolicy_VeryLargeExponentialBackoff_DoesNotOverflow()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxAttempts = 100,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            Strategy = BackoffStrategy.Exponential,
            MaxDelay = TimeSpan.FromHours(1)
        };

        // Act - Attempt 30: 100ms * 2^29 = 53,687,091,200ms (way above 1 hour)
        // This would overflow without MaxDelay capping
        var delay = policy.GetDelay(30);

        // Assert - Should be capped at MaxDelay (1 hour = 3,600,000ms)
        delay.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void RetryPolicy_ZeroInitialDelay_WorksCorrectly()
    {
        // Arrange
        var policy = new RetryPolicy
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.Zero,
            Strategy = BackoffStrategy.Exponential
        };

        // Act & Assert
        policy.GetDelay(1).Should().Be(TimeSpan.Zero);
        policy.GetDelay(2).Should().Be(TimeSpan.Zero);
        policy.GetDelay(3).Should().Be(TimeSpan.Zero);
    }

    #endregion
}
