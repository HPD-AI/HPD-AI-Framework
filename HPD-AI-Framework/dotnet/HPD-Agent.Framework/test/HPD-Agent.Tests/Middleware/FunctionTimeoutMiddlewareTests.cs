using HPD.Agent.Middleware;
using HPD.Agent.Middleware.Function;
using Microsoft.Extensions.AI;
using Xunit;
using static HPD.Agent.Tests.Middleware.V2.MiddlewareTestHelpers;

namespace HPD.Agent.Tests.Middleware;

/// <summary>
/// Comprehensive tests for FunctionTimeoutMiddleware.
/// Tests timeout enforcement and cancellation token handling.
/// </summary>
public class FunctionTimeoutMiddlewareTests
{
    #region Basic Timeout Behavior

    [Fact]
    public async Task WrapFunctionCallAsync_CompletesBeforeTimeout_Succeeds()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(2);
        var middleware = new FunctionTimeoutMiddleware(timeout);
        var request = CreateFunctionRequest();

        Func<FunctionRequest, Task<object?>> handler = async (req) =>
        {
            await Task.Delay(50); // Fast execution
            return "Success";
        };

        // Act
        var result = await middleware.WrapFunctionCallAsync(request, handler, CancellationToken.None);

        // Assert
        Assert.Equal("Success", result);
    }

    [Fact]
    public async Task WrapFunctionCallAsync_ExceedsTimeout_ThrowsTimeoutException()
    {
        // Arrange
        var timeout = TimeSpan.FromMilliseconds(100);
        var middleware = new FunctionTimeoutMiddleware(timeout);
        var request = CreateFunctionRequest(
            function: AIFunctionFactory.Create(() => "test", "TestFunction"));

        Func<FunctionRequest, Task<object?>> handler = async (req) =>
        {
            await Task.Delay(500); // Slow execution (will timeout)
            return "Success";
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await middleware.WrapFunctionCallAsync(request, handler, CancellationToken.None));

        Assert.Contains("timed out", exception.Message);
        Assert.Contains("TestFunction", exception.Message);
        Assert.Contains("0.1", exception.Message); // Timeout in seconds
    }

    [Fact]
    public async Task WrapFunctionCallAsync_ExactlyAtTimeout_MaySucceedOrTimeout()
    {
        // Arrange
        var timeout = TimeSpan.FromMilliseconds(100);
        var middleware = new FunctionTimeoutMiddleware(timeout);
        var request = CreateFunctionRequest();

        Func<FunctionRequest, Task<object?>> handler = async (req) =>
        {
            await Task.Delay(100); // Exactly at timeout
            return "Success";
        };

        // Act - This is a race condition, either outcome is acceptable
        try
        {
            var result = await middleware.WrapFunctionCallAsync(request, handler, CancellationToken.None);
            // If we get here, execution completed just in time
            Assert.Equal("Success", result);
        }
        catch (TimeoutException ex)
        {
            // If we get here, timeout triggered first
            Assert.Contains("timed out", ex.Message);
        }

        // Test passes either way - just documenting the race condition
        Assert.True(true);
    }

    #endregion

    #region Cancellation Token Handling

    [Fact]
    public async Task WrapFunctionCallAsync_ParentCancellationTriggered_ThrowsOperationCanceled()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(10); // Long timeout
        var middleware = new FunctionTimeoutMiddleware(timeout);
        var request = CreateFunctionRequest();

        var cts = new CancellationTokenSource();

        Func<FunctionRequest, Task<object?>> handler = async (req) =>
        {
            await Task.Delay(50);
            cts.Cancel(); // Parent cancellation (not timeout)
            await Task.Delay(100, cts.Token); // This will throw
            return "Success";
        };

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await middleware.WrapFunctionCallAsync(request, handler, cts.Token));
    }

    [Fact]
    public async Task WrapFunctionCallAsync_TimeoutCancellation_ThrowsTimeoutException()
    {
        // Arrange
        var timeout = TimeSpan.FromMilliseconds(100);
        var middleware = new FunctionTimeoutMiddleware(timeout);
        var request = CreateFunctionRequest();

        Func<FunctionRequest, Task<object?>> handler = async (req) =>
        {
            // Slow operation that will timeout
            await Task.Delay(500);
            return "Success";
        };

        // Act & Assert - Should throw TimeoutException, not OperationCanceledException
        var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await middleware.WrapFunctionCallAsync(request, handler, CancellationToken.None));

        Assert.Contains("timed out", exception.Message);
    }

    [Fact]
    public async Task WrapFunctionCallAsync_ParentAlreadyCanceled_ThrowsImmediately()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(10);
        var middleware = new FunctionTimeoutMiddleware(timeout);
        var request = CreateFunctionRequest();

        var cts = new CancellationTokenSource();
        cts.Cancel(); // Already canceled

        bool nextCalled = false;
        Func<FunctionRequest, Task<object?>> handler = async (req) =>
        {
            nextCalled = true;
            // Add a small delay to ensure we have an async operation to cancel
            await Task.Delay(100, cts.Token);
            return "Success";
        };

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await middleware.WrapFunctionCallAsync(request, handler, cts.Token));

        // Next is called but should throw immediately when it checks the cancellation token
    }

    #endregion

    #region Function Name in Exception

    [Fact]
    public async Task WrapFunctionCallAsync_Timeout_IncludesFunctionNameInException()
    {
        // Arrange
        var timeout = TimeSpan.FromMilliseconds(50);
        var middleware = new FunctionTimeoutMiddleware(timeout);
        var request = CreateFunctionRequest(
            function: AIFunctionFactory.Create(() => "test", "MyCustomFunction"));

        Func<FunctionRequest, Task<object?>> handler = async (req) =>
        {
            await Task.Delay(200);
            return "Success";
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await middleware.WrapFunctionCallAsync(request, handler, CancellationToken.None));

        Assert.Contains("MyCustomFunction", exception.Message);
    }

    [Fact]
    public async Task WrapFunctionCallAsync_NoFunctionName_UsesUnknown()
    {
        // Arrange
        var timeout = TimeSpan.FromMilliseconds(50);
        var middleware = new FunctionTimeoutMiddleware(timeout);

        // Create request with explicitly null function (not using helper default)
        var state = AgentLoopState.InitialSafe(
            new List<ChatMessage>(),
            "test-run",
            "test-conv",
            "TestAgent");
        var request = new FunctionRequest
        {
            Function = null, // Explicitly null to test "Unknown" case
            CallId = "test-call",
            Arguments = new Dictionary<string, object?>(),
            State = state
        };

        Func<FunctionRequest, Task<object?>> handler = async (req) =>
        {
            await Task.Delay(200);
            return "Success";
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await middleware.WrapFunctionCallAsync(request, handler, CancellationToken.None));

        Assert.Contains("Unknown", exception.Message);
    }

    #endregion

    #region Constructor Validation

    [Fact]
    public void Constructor_NegativeTimeout_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new FunctionTimeoutMiddleware(TimeSpan.FromSeconds(-1)));

        Assert.Contains("greater than zero", exception.Message);
    }

    [Fact]
    public void Constructor_ZeroTimeout_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new FunctionTimeoutMiddleware(TimeSpan.Zero));

        Assert.Contains("greater than zero", exception.Message);
    }

    [Fact]
    public void Constructor_ValidTimeout_Succeeds()
    {
        // Act
        var middleware = new FunctionTimeoutMiddleware(TimeSpan.FromSeconds(1));

        // Assert - no exception thrown
        Assert.NotNull(middleware);
    }

    #endregion

    #region Integration with Other Middleware Concerns

    [Fact]
    public async Task WrapFunctionCallAsync_ExceptionDuringExecution_PropagatesException()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(10); // Long timeout
        var middleware = new FunctionTimeoutMiddleware(timeout);
        var request = CreateFunctionRequest();

        Func<FunctionRequest, Task<object?>> handler = (req) =>
        {
            throw new InvalidOperationException("Business logic error");
        };

        // Act & Assert - Exception should propagate (not timeout)
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await middleware.WrapFunctionCallAsync(request, handler, CancellationToken.None));

        Assert.Equal("Business logic error", exception.Message);
    }

    [Fact]
    public async Task WrapFunctionCallAsync_FastFailure_DoesNotWaitForTimeout()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(10); // Long timeout
        var middleware = new FunctionTimeoutMiddleware(timeout);
        var request = CreateFunctionRequest();

        var startTime = DateTime.UtcNow;
        Func<FunctionRequest, Task<object?>> handler = (req) =>
        {
            throw new InvalidOperationException("Immediate failure");
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await middleware.WrapFunctionCallAsync(request, handler, CancellationToken.None));

        var elapsed = DateTime.UtcNow - startTime;

        // Should fail immediately, not wait for timeout
        Assert.True(elapsed < TimeSpan.FromSeconds(1));
    }

    #endregion

    #region Multiple Concurrent Calls

    [Fact]
    public async Task WrapFunctionCallAsync_MultipleConcurrentCalls_EachHasOwnTimeout()
    {
        // Arrange
        var timeout = TimeSpan.FromMilliseconds(500);
        var middleware = new FunctionTimeoutMiddleware(timeout);

        // Act - Start multiple concurrent calls
        var tasks = new List<Task>();

        // Fast call - should succeed
        var request1 = CreateFunctionRequest();
        tasks.Add(Task.Run(async () =>
        {
            var result = await middleware.WrapFunctionCallAsync(
                request1,
                async (req) =>
                {
                    await Task.Delay(100);
                    return "Fast";
                },
                CancellationToken.None);
            Assert.Equal("Fast", result);
        }));

        // Slow call - should timeout
        var request2 = CreateFunctionRequest();
        tasks.Add(Task.Run(async () =>
        {
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await middleware.WrapFunctionCallAsync(
                    request2,
                    async (req) =>
                    {
                        await Task.Delay(1000);
                        return "Slow";
                    },
                    CancellationToken.None));
        }));

        // Assert - All tasks complete successfully
        await Task.WhenAll(tasks);
    }

    #endregion

    private static AgentContext CreateAgentContext(AgentLoopState? state = null)
    {
        var agentState = state ?? AgentLoopState.InitialSafe(
            messages: Array.Empty<ChatMessage>(),
            runId: "test-run",
            conversationId: "test-conversation",
            agentName: "TestAgent");

        return new AgentContext(
            "TestAgent",
            "test-conversation",
            agentState,
            new HPD.Events.Core.EventCoordinator(),
            new global::HPD.Agent.Session("test-session"),
            new global::HPD.Agent.Branch("test-session"),
            CancellationToken.None);
    }

    private static BeforeToolExecutionContext CreateBeforeToolExecutionContext(
        ChatMessage? response = null,
        List<FunctionCallContent>? toolCalls = null,
        AgentLoopState? state = null)
    {
        var agentContext = CreateAgentContext(state);
        response ??= new ChatMessage(ChatRole.Assistant, []);
        toolCalls ??= new List<FunctionCallContent>();
        return agentContext.AsBeforeToolExecution(response, toolCalls, new AgentRunConfig());
    }

    private static AfterMessageTurnContext CreateAfterMessageTurnContext(
        AgentLoopState? state = null,
        List<ChatMessage>? turnHistory = null)
    {
        var agentContext = CreateAgentContext(state);
        var finalResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Test response"));
        turnHistory ??= new List<ChatMessage>();
        return agentContext.AsAfterMessageTurn(finalResponse, turnHistory, new AgentRunConfig());
    }

}
