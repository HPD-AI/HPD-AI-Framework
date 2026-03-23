// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent;
using Xunit;

namespace HPD.Agent.Tests.Middleware;

/// <summary>
/// Unit tests for MiddlewareState and source generator.
/// Uses real middleware state types (ErrorTrackingStateData, CircuitBreakerStateData) to test the generated code.
/// </summary>
public class MiddlewareStateTests
{
    [Fact]
    public void Constructor_CreatesEmptyContainer()
    {
        // Arrange & Act
        var container = new MiddlewareState();

        // Assert
        Assert.NotNull(container);
    }

    [Fact]
    public void Constructor_IsSuccessful()
    {
        // Arrange & Act
        var exception = Record.Exception(() => new MiddlewareState());

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void GeneratedProperty_ErrorTracking_Exists()
    {
        // Arrange
        var container = new MiddlewareState();

        // Act
        var state = container.ErrorTracking();

        // Assert
        Assert.Null(state);
    }

    [Fact]
    public void GeneratedMethod_WithErrorTracking_CreatesNewInstance()
    {
        // Arrange
        var container = new MiddlewareState();
        var testState = new ErrorTrackingStateData
        {
            ConsecutiveFailures = 3
        };

        // Act
        var updated = container.WithErrorTracking(testState);

        // Assert
        Assert.NotSame(container, updated);
        Assert.NotNull(updated.ErrorTracking());
        Assert.Equal(3, updated.ErrorTracking().ConsecutiveFailures);
    }

    [Fact]
    public void GeneratedProperty_AfterSet_ReturnsCorrectValue()
    {
        // Arrange
        var container = new MiddlewareState();
        var testState = new ErrorTrackingStateData { ConsecutiveFailures = 5 };

        // Act
        var updated = container.WithErrorTracking(testState);

        // Assert
        Assert.Equal(5, updated.ErrorTracking()!.ConsecutiveFailures);
    }

    [Fact]
    public void GeneratedMethod_WithNull_ReturnsOriginalContainer()
    {
        // Arrange
        var container = new MiddlewareState();

        // Act
        var updated = container.WithErrorTracking(null);

        // Assert
        Assert.Same(container, updated);
    }

    [Fact]
    public void ImmutableUpdate_DoesNotModifyOriginal()
    {
        // Arrange
        var original = new MiddlewareState();
        var state = new ErrorTrackingStateData { ConsecutiveFailures = 10 };

        // Act
        var updated = original.WithErrorTracking(state);

        // Assert
        Assert.Null(original.ErrorTracking());
        Assert.NotNull(updated.ErrorTracking());
        Assert.Equal(10, updated.ErrorTracking().ConsecutiveFailures);
    }

    [Fact]
    public void GeneratedMethod_CalledMultipleTimes_CreatesNewInstanceEachTime()
    {
        // Arrange
        var container = new MiddlewareState();
        var state1 = new ErrorTrackingStateData { ConsecutiveFailures = 1 };
        var state2 = new ErrorTrackingStateData { ConsecutiveFailures = 2 };

        // Act
        var updated1 = container.WithErrorTracking(state1);
        var updated2 = updated1.WithErrorTracking(state2);

        // Assert
        Assert.NotSame(container, updated1);
        Assert.NotSame(updated1, updated2);
        Assert.Equal(1, updated1.ErrorTracking()!.ConsecutiveFailures);
        Assert.Equal(2, updated2.ErrorTracking()!.ConsecutiveFailures);
    }

    [Fact]
    public void PropertyNameGeneration_StripsStateDataSuffix()
    {
        // ErrorTrackingStateData -> ErrorTracking
        // CircuitBreakerStateData -> CircuitBreaker

        // Arrange
        var container = new MiddlewareState();

        // Act & Assert - property names should have 'StateData' suffix stripped
        var errorTracking = container.ErrorTracking();
        var circuitBreaker = container.CircuitBreaker();

        Assert.Null(errorTracking);
        Assert.Null(circuitBreaker);
    }

    [Fact]
    public void MultipleMiddlewareStates_CanCoexist()
    {
        // Arrange
        var container = new MiddlewareState();
        var errorState = new ErrorTrackingStateData { ConsecutiveFailures = 3 };
        var circuitState = new CircuitBreakerStateData
        {
            ConsecutiveCountPerTool = new Dictionary<string, int> { ["testTool"] = 2 }
        };

        // Act
        var withError = container.WithErrorTracking(errorState);
        var withBoth = withError.WithCircuitBreaker(circuitState);

        // Assert
        Assert.NotNull(withBoth.ErrorTracking());
        Assert.NotNull(withBoth.CircuitBreaker());
        Assert.Equal(3, withBoth.ErrorTracking().ConsecutiveFailures);
        Assert.Equal(2, withBoth.CircuitBreaker().ConsecutiveCountPerTool["testTool"]);
    }
}
