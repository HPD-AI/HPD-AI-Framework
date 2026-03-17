using HPD.Agent;
using HPD.Agent.ErrorHandling;
using Xunit;

namespace HPD.Agent.Tests.Middleware;

/// <summary>
/// Quick test to verify the new WithErrorHandling() convenience method works correctly.
/// </summary>
public class ErrorHandlingConvenienceMethodTest
{
    [Fact]
    public void WithErrorHandling_SimpleUsage_RegistersAllMiddleware()
    {
        // Arrange
        var config = new AgentConfig
        {
            Name = "Test Agent",
            Provider = new ProviderConfig
            {
                ProviderKey = "openai",
                ModelName = "gpt-4"
            }
        };

        // Act
        var builder = new AgentBuilder(config)
            .WithErrorHandling(); // One call, all middleware registered

        // Assert
        Assert.NotNull(builder);
        Assert.Equal(6, builder.Middlewares.Count); // CB + ErrorTracking + TotalThreshold + Retry + Timeout + ErrorFormatting
    }

    [Fact]
    public void WithErrorHandling_CustomThresholds_UsesProvidedValues()
    {
        // Arrange
        var config = new AgentConfig();

        // Act
        var builder = new AgentBuilder(config)
            .WithErrorHandling(
                maxConsecutiveCalls: 3,
                maxConsecutiveErrors: 5,
                maxTotalErrors: 15);

        // Assert
        Assert.Equal(6, builder.Middlewares.Count);
        // Middleware are registered in order: CB, ErrorTracking, TotalThreshold, Retry, Timeout, ErrorFormatting
    }

    [Fact]
    public void WithErrorHandling_AdvancedConfiguration_AllowsFineTuning()
    {
        // Arrange
        var config = new AgentConfig();

        // Act
        var builder = new AgentBuilder(config)
            .WithErrorHandling(
                configureCircuitBreaker: cb =>
                {
                    cb.MaxConsecutiveCalls = 2;
                    cb.TerminationMessageTemplate = "Custom loop message";
                },
                configureFunctionRetry: retry =>
                {
                    retry.MaxRetries = 10;
                    retry.RetryDelay = TimeSpan.FromSeconds(5);
                },
                configureFunctionTimeout: TimeSpan.FromMinutes(3));

        // Assert
        Assert.Equal(6, builder.Middlewares.Count);
    }

    [Fact]
    public void WithFunctionRetry_Standalone_RegistersRetryMiddleware()
    {
        // Arrange
        var config = new AgentConfig
        {
            ErrorHandling = new ErrorHandlingConfig
            {
                MaxRetries = 5,
                RetryDelay = TimeSpan.FromSeconds(2)
            }
        };

        // Act
        var builder = new AgentBuilder(config)
            .WithFunctionRetry();

        // Assert
        Assert.Single(builder.Middlewares);
        Assert.IsType<HPD.Agent.Middleware.Function.RetryMiddleware>(builder.Middlewares[0]);
    }

    [Fact]
    public void WithFunctionTimeout_Standalone_RegistersTimeoutMiddleware()
    {
        // Arrange
        var config = new AgentConfig
        {
            ErrorHandling = new ErrorHandlingConfig
            {
                SingleFunctionTimeout = TimeSpan.FromMinutes(2)
            }
        };

        // Act
        var builder = new AgentBuilder(config)
            .WithFunctionTimeout();

        // Assert
        Assert.Single(builder.Middlewares);
        Assert.IsType<HPD.Agent.Middleware.Function.FunctionTimeoutMiddleware>(builder.Middlewares[0]);
    }

    [Fact]
    public void WithFunctionTimeout_CustomTimeout_UsesProvidedValue()
    {
        // Arrange
        var config = new AgentConfig();

        // Act
        var builder = new AgentBuilder(config)
            .WithFunctionTimeout(TimeSpan.FromMinutes(5));

        // Assert
        Assert.Single(builder.Middlewares);
        Assert.IsType<HPD.Agent.Middleware.Function.FunctionTimeoutMiddleware>(builder.Middlewares[0]);
    }

    [Fact]
    public void MiddlewareOrder_IsCorrect()
    {
        // Arrange
        var config = new AgentConfig();

        // Act
        var builder = new AgentBuilder(config)
            .WithErrorHandling();

        // Assert
        Assert.Equal(6, builder.Middlewares.Count);

        // Iteration-level middleware (first 3)
        Assert.IsType<CircuitBreakerMiddleware>(builder.Middlewares[0]);
        Assert.IsType<ErrorTrackingMiddleware>(builder.Middlewares[1]);
        Assert.IsType<TotalErrorThresholdMiddleware>(builder.Middlewares[2]);

        // Function-level middleware (last 3 - onion pattern)
        Assert.IsType<HPD.Agent.Middleware.Function.RetryMiddleware>(builder.Middlewares[3]);
        Assert.IsType<HPD.Agent.Middleware.Function.FunctionTimeoutMiddleware>(builder.Middlewares[4]);
        Assert.IsType<HPD.Agent.Middleware.Function.ErrorFormattingMiddleware>(builder.Middlewares[5]);
    }
}
