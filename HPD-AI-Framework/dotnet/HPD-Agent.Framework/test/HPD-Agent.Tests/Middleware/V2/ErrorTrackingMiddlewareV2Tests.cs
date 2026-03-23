using HPD.Agent;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using Xunit;

/// <summary>
/// Tests for ErrorTrackingMiddleware - OnErrorAsync hook usage (V2 Middleware Architecture).
/// </summary>
public class ErrorTrackingMiddlewareTests
{
    /// <summary>
    /// Tests that OnErrorAsync increments consecutive failures count.
    /// </summary>
    [Fact]
    public async Task OnErrorAsync_IncrementsConsecutiveFailures()
    {
        // Arrange
        var middleware = new ErrorTrackingMiddleware { MaxConsecutiveErrors = 3 };
        var context = CreateErrorContext();

        // Act
        await middleware.OnErrorAsync(context, CancellationToken.None);

        // Assert - failure count incremented
        var errorState = context.Analyze(s => s.MiddlewareState.ErrorTracking());
        Assert.NotNull(errorState);
        Assert.Equal(1, errorState.ConsecutiveFailures);
    }

    /// <summary>
    /// Tests that OnErrorAsync triggers termination when error threshold is reached.
    /// </summary>
    [Fact]
    public async Task OnErrorAsync_TriggersTerminationAtThreshold()
    {
        // Arrange
        var middleware = new ErrorTrackingMiddleware { MaxConsecutiveErrors = 2 };
        var context = CreateErrorContext();

        // Simulate 1st error
        await middleware.OnErrorAsync(context, CancellationToken.None);

        // Act - 2nd error should trigger termination
        await middleware.OnErrorAsync(context, CancellationToken.None);

        // Assert
        Assert.True(context.Analyze(s => s.IsTerminated));
        Assert.Contains("Maximum consecutive errors", context.Analyze(s => s.TerminationReason) ?? "");
        Assert.Equal(2, context.Analyze(s => s.MiddlewareState.ErrorTracking())?.ConsecutiveFailures);
    }

    /// <summary>
    /// Tests that AfterIterationAsync resets error counter on successful iteration.
    /// </summary>
    [Fact]
    public async Task AfterIterationAsync_ResetsCounterOnSuccess()
    {
        // Arrange
        var middleware = new ErrorTrackingMiddleware { MaxConsecutiveErrors = 3 };
        var errorContext = CreateErrorContext();
        var afterContext = CreateAfterIterationContext(allSucceeded: true);

        // Simulate error first
        await middleware.OnErrorAsync(errorContext, CancellationToken.None);
        Assert.Equal(1, errorContext.State.MiddlewareState.ErrorTracking()?.ConsecutiveFailures);

        // Update afterContext to have same state as errorContext
        var agentContext = GetAgentContextFromErrorContext(errorContext);
        var newAfterContext = agentContext.AsAfterIteration(0, Array.Empty<FunctionResultContent>(), new AgentRunConfig());

        // Act - successful iteration should reset
        await middleware.AfterIterationAsync(newAfterContext, CancellationToken.None);

        // Assert
        Assert.Equal(0, newAfterContext.State.MiddlewareState.ErrorTracking()?.ConsecutiveFailures);
    }

    /// <summary>
    /// Tests that AfterIterationAsync does not reset error counter when iteration has failures.
    /// </summary>
    [Fact]
    public async Task AfterIterationAsync_DoesNotResetOnFailure()
    {
        // Arrange
        var middleware = new ErrorTrackingMiddleware { MaxConsecutiveErrors = 3 };
        var errorContext = CreateErrorContext();
        var afterContext = CreateAfterIterationContext(allSucceeded: false);

        // Simulate error first
        await middleware.OnErrorAsync(errorContext, CancellationToken.None);
        Assert.Equal(1, errorContext.State.MiddlewareState.ErrorTracking()?.ConsecutiveFailures);

        // Update afterContext to have same state as errorContext
        var agentContext = GetAgentContextFromErrorContext(errorContext);
        var failedResult = new FunctionResultContent("call1", "TestFunc")
        {
            Exception = new InvalidOperationException("Test error")
        };
        var newAfterContext = agentContext.AsAfterIteration(0, new[] { failedResult }, new AgentRunConfig());

        // Act - failed iteration should NOT reset
        await middleware.AfterIterationAsync(newAfterContext, CancellationToken.None);

        // Assert - counter unchanged
        Assert.Equal(1, newAfterContext.State.MiddlewareState.ErrorTracking()?.ConsecutiveFailures);
    }

    /// <summary>
    /// Tests that state updates are immediate and visible without GetPendingState.
    /// </summary>
    [Fact]
    public async Task OnErrorAsync_ImmediateStateUpdates_NoGetPendingStateNeeded()
    {
        // Arrange
        var middleware = new ErrorTrackingMiddleware { MaxConsecutiveErrors = 3 };
        var context = CreateErrorContext();

        // Act
        await middleware.OnErrorAsync(context, CancellationToken.None);

        // Assert - state updated immediately, visible in context.State (no GetPendingState!)
        Assert.Equal(1, context.Analyze(s => s.MiddlewareState.ErrorTracking())?.ConsecutiveFailures);

        // Second error
        await middleware.OnErrorAsync(context, CancellationToken.None);

        // Assert - immediately visible
        Assert.Equal(2, context.Analyze(s => s.MiddlewareState.ErrorTracking())?.ConsecutiveFailures);
    }

    /// <summary>
    /// Tests that errors from different sources are all tracked correctly.
    /// </summary>
    [Fact]
    public async Task MultipleErrorSources_AllTrackedCorrectly()
    {
        // Arrange
        var middleware = new ErrorTrackingMiddleware { MaxConsecutiveErrors = 3 };

        // Different error sources
        var modelError = CreateErrorContext(ErrorSource.ModelCall);
        var toolError = CreateErrorContext(ErrorSource.ToolCall);

        // Act
        await middleware.OnErrorAsync(modelError, CancellationToken.None);

        // Use same context instance for second error (simulating real middleware pipeline)
        var agentContext = GetAgentContextFromErrorContext(modelError);
        var secondError = agentContext.AsError(
            new InvalidOperationException("Tool error"),
            ErrorSource.ToolCall,
            0);

        await middleware.OnErrorAsync(secondError, CancellationToken.None);

        // Assert - both errors counted
        Assert.Equal(2, secondError.State.MiddlewareState.ErrorTracking()?.ConsecutiveFailures);
    }

    /// <summary>
    /// Tests that custom termination message formats correctly.
    /// </summary>
    [Fact]
    public async Task CustomTerminationMessage_FormatsCorrectly()
    {
        // Arrange
        var middleware = new ErrorTrackingMiddleware
        {
            MaxConsecutiveErrors = 5,
            TerminationMessageTemplate = "Custom: {count} out of {max} errors"
        };
        var context = CreateErrorContext();

        // Simulate errors up to threshold
        for (int i = 0; i < 5; i++)
        {
            await middleware.OnErrorAsync(context, CancellationToken.None);
        }

        // Assert
        Assert.Contains("Custom: 5 out of 5 errors", context.Analyze(s => s.TerminationReason) ?? "");
    }

    // Helper methods

    private static ErrorContext CreateErrorContext(ErrorSource source = ErrorSource.ToolCall)
    {
        var state = AgentLoopState.InitialSafe(
            new List<ChatMessage>(),
            "run123",
            "conv123",
            "TestAgent");

        var agentContext = new AgentContext(
            "TestAgent",
            "conv123",
            state,
            new HPD.Events.Core.EventCoordinator(),
            new HPD.Agent.Session("test-session"),
            new HPD.Agent.Session("test-session").CreateBranch(),
            CancellationToken.None);

        return agentContext.AsError(
            new InvalidOperationException("Test error"),
            source,
            0);
    }

    private static AfterIterationContext CreateAfterIterationContext(bool allSucceeded)
    {
        var state = AgentLoopState.InitialSafe(
            new List<ChatMessage>(),
            "run123",
            "conv123",
            "TestAgent");

        var agentContext = new AgentContext(
            "TestAgent",
            "conv123",
            state,
            new HPD.Events.Core.EventCoordinator(),
            new HPD.Agent.Session("test-session"),
            new HPD.Agent.Session("test-session").CreateBranch(),
            CancellationToken.None);

        if (allSucceeded)
        {
            return agentContext.AsAfterIteration(0, Array.Empty<FunctionResultContent>(), new AgentRunConfig());
        }
        else
        {
            var failedResult = new FunctionResultContent("call1", "TestFunc")
            {
                Exception = new InvalidOperationException("Test error")
            };
            return agentContext.AsAfterIteration(0, new[] { failedResult }, new AgentRunConfig());
        }
    }

    private static AgentContext GetAgentContextFromErrorContext(ErrorContext context)
    {
        // Access internal Base property to get the AgentContext
        var baseProperty = typeof(HookContext).GetProperty("Base",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (AgentContext)baseProperty!.GetValue(context)!;
    }
}
