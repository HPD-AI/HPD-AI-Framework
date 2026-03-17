using HPD.Agent;
using HPD.Agent.Tests.Middleware.V2;
using static HPD.Agent.Tests.Middleware.V2.MiddlewareTestHelpers;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Middleware;
using HPD.Agent.Middleware.Function;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Middleware;

/// <summary>
/// Comprehensive tests for FunctionRetryMiddleware.
/// Tests provider-aware retry logic, exponential backoff, and error categorization.
/// </summary>
public class FunctionRetryMiddlewareTests
{
    #region Basic Retry Behavior

    [Fact]
    public async Task WrapFunctionCallAsync_SuccessOnFirstAttempt_NoRetry()
    {
        // Arrange
        var config = new ErrorHandlingConfig
        {
            MaxRetries = 3
        };
        var middleware = new RetryMiddleware(config);
        var request = CreateFunctionRequest();

        int attempts = 0;
        Func<FunctionRequest, Task<object?>> handler = async (req) =>
        {
            attempts++;
            return await Task.FromResult<object?>("Success");
        };

        // Act
        var result = await middleware.WrapFunctionCallAsync(request, handler, CancellationToken.None);

        // Assert
        Assert.Equal("Success", result);
        Assert.Equal(1, attempts); // Only 1 attempt (no retry)
    }

    [Fact]
    public async Task WrapFunctionCallAsync_FailsThreeTimes_RetriesThreeTimes()
    {
        // Arrange
        var config = new ErrorHandlingConfig
        {
            MaxRetries = 3,
            RetryDelay = TimeSpan.FromMilliseconds(10)
        };
        var middleware = new RetryMiddleware(config);
        var request = CreateFunctionRequest();

        int attempts = 0;
        Func<FunctionRequest, Task<object?>> handler = (req) =>
        {
            attempts++;
            throw new InvalidOperationException("Transient error");
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await middleware.WrapFunctionCallAsync(request, handler, CancellationToken.None));

        Assert.Equal(4, attempts); // Initial + 3 retries
    }

    [Fact]
    public async Task WrapFunctionCallAsync_SucceedsOnSecondAttempt_OnlyRetriesOnce()
    {
        // Arrange
        var config = new ErrorHandlingConfig
        {
            MaxRetries = 3,
            RetryDelay = TimeSpan.FromMilliseconds(10)
        };
        var middleware = new RetryMiddleware(config);
        var request = CreateFunctionRequest();

        int attempts = 0;
        Func<FunctionRequest, Task<object?>> handler = async (req) =>
        {
            attempts++;
            if (attempts == 1)
                throw new InvalidOperationException("Transient error");
            return await Task.FromResult<object?>("Success");
        };

        // Act
        var result = await middleware.WrapFunctionCallAsync(request, handler, CancellationToken.None);

        // Assert
        Assert.Equal("Success", result);
        Assert.Equal(2, attempts); // Initial + 1 retry
    }

    #endregion

    #region Custom Retry Strategy (Priority 1)

    [Fact]
    public async Task WrapFunctionCallAsync_CustomStrategyReturnsDelay_UsesCustomDelay()
    {
        // Arrange
        var customDelayCalled = false;
        var config = new ErrorHandlingConfig
        {
            MaxRetries = 3,
            CustomRetryStrategy = async (ex, attempt, ct) =>
            {
                customDelayCalled = true;
                return TimeSpan.FromMilliseconds(100); // Custom delay
            }
        };
        var middleware = new RetryMiddleware(config);
        var request = CreateFunctionRequest();

        int attempts = 0;
        var startTime = DateTime.UtcNow;
        Func<FunctionRequest, Task<object?>> handler = async (req) =>
        {
            attempts++;
            if (attempts == 1)
                throw new InvalidOperationException("Error");
            return await Task.FromResult<object?>("Success");
        };

        // Act
        var result = await middleware.WrapFunctionCallAsync(request, handler, CancellationToken.None);
        var elapsed = DateTime.UtcNow - startTime;

        // Assert
        Assert.Equal("Success", result);
        Assert.True(customDelayCalled);
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(90)); // Allow some variance
    }

    [Fact]
    public async Task WrapFunctionCallAsync_CustomStrategyReturnsNull_DoesNotRetry()
    {
        // Arrange
        var config = new ErrorHandlingConfig
        {
            MaxRetries = 3,
            CustomRetryStrategy = async (ex, attempt, ct) =>
            {
                return null; // Don't retry
            }
        };
        var middleware = new RetryMiddleware(config);
        var request = CreateFunctionRequest();

        int attempts = 0;
        Func<FunctionRequest, Task<object?>> handler = (req) =>
        {
            attempts++;
            throw new InvalidOperationException("Non-retryable error");
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await middleware.WrapFunctionCallAsync(request, handler, CancellationToken.None));

        Assert.Equal(1, attempts); // No retry
    }

    #endregion

    #region Provider-Aware Error Handling (Priority 2)

    [Fact]
    public async Task WrapFunctionCallAsync_RateLimitError_RespectsRetryAfter()
    {
        // Arrange
        var providerHandler = new TestProviderErrorHandler
        {
            ErrorDetails = new ProviderErrorDetails
            {
                Category = ErrorCategory.RateLimitRetryable,
                RetryAfter = TimeSpan.FromMilliseconds(200)
            }
        };

        var config = new ErrorHandlingConfig
        {
            MaxRetries = 3,
            UseProviderRetryDelays = true
        };
        var middleware = new RetryMiddleware(config, providerHandler);
        var request = CreateFunctionRequest();

        int attempts = 0;
        var startTime = DateTime.UtcNow;
        Func<FunctionRequest, Task<object?>> handler = async (req) =>
        {
            attempts++;
            if (attempts == 1)
                throw new InvalidOperationException("Rate limit");
            return await Task.FromResult<object?>("Success");
        };

        // Act
        var result = await middleware.WrapFunctionCallAsync(request, handler, CancellationToken.None);
        var elapsed = DateTime.UtcNow - startTime;

        // Assert
        Assert.Equal("Success", result);
        Assert.Equal(2, attempts);
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(180)); // Respects Retry-After
    }

    [Fact]
    public async Task WrapFunctionCallAsync_ClientError_DoesNotRetry()
    {
        // Arrange
        var providerHandler = new TestProviderErrorHandler
        {
            ErrorDetails = new ProviderErrorDetails
            {
                Category = ErrorCategory.ClientError // 400 - don't retry
            },
            ShouldRetry = false
        };

        var config = new ErrorHandlingConfig
        {
            MaxRetries = 3
        };
        var middleware = new RetryMiddleware(config, providerHandler);
        var request = CreateFunctionRequest();

        int attempts = 0;
        Func<FunctionRequest, Task<object?>> handler = (req) =>
        {
            attempts++;
            throw new InvalidOperationException("Client error");
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await middleware.WrapFunctionCallAsync(request, handler, CancellationToken.None));

        Assert.Equal(1, attempts); // No retry for client errors
    }

    [Fact]
    public async Task WrapFunctionCallAsync_PerCategoryRetryLimit_RespectsLimit()
    {
        // Arrange
        var providerHandler = new TestProviderErrorHandler
        {
            ErrorDetails = new ProviderErrorDetails
            {
                Category = ErrorCategory.RateLimitRetryable
            }
        };

        var config = new ErrorHandlingConfig
        {
            MaxRetries = 5, // Global max
            MaxRetriesByCategory = new Dictionary<ErrorCategory, int>
            {
                [ErrorCategory.RateLimitRetryable] = 2 // Category-specific limit
            },
            RetryDelay = TimeSpan.FromMilliseconds(10)
        };
        var middleware = new RetryMiddleware(config, providerHandler);
        var request = CreateFunctionRequest();

        int attempts = 0;
        Func<FunctionRequest, Task<object?>> handler = (req) =>
        {
            attempts++;
            throw new InvalidOperationException("Rate limit");
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await middleware.WrapFunctionCallAsync(request, handler, CancellationToken.None));

        Assert.Equal(3, attempts); // Initial + 2 retries (category limit, not global 5)
    }

    #endregion

    #region Exponential Backoff (Priority 3)

    [Fact]
    public async Task WrapFunctionCallAsync_NoProvider_UsesExponentialBackoff()
    {
        // Arrange
        var config = new ErrorHandlingConfig
        {
            MaxRetries = 2,
            RetryDelay = TimeSpan.FromMilliseconds(100),
            BackoffMultiplier = 2.0
        };
        var middleware = new RetryMiddleware(config);
        var request = CreateFunctionRequest();

        int attempts = 0;
        var delayTimes = new List<DateTime>();
        Func<FunctionRequest, Task<object?>> handler = async (req) =>
        {
            delayTimes.Add(DateTime.UtcNow);
            attempts++;
            if (attempts <= 2)
                throw new InvalidOperationException("Transient");
            return await Task.FromResult<object?>("Success");
        };

        // Act
        var result = await middleware.WrapFunctionCallAsync(request, handler, CancellationToken.None);

        // Assert
        Assert.Equal("Success", result);
        Assert.Equal(3, attempts);

        // Check delays meet minimum expectations with jitter tolerance
        // First retry: ~100ms * 2^0 = ~100ms (with 0.9x jitter = 90ms minimum)
        // Second retry: ~100ms * 2^1 = ~200ms (with 0.9x jitter = 180ms minimum)
        var delay1 = delayTimes[1] - delayTimes[0];
        var delay2 = delayTimes[2] - delayTimes[1];

        // Allow some tolerance for timing precision and system scheduling
        Assert.True(delay1.TotalMilliseconds >= 50, $"First delay ({delay1.TotalMilliseconds}ms) should be at least 50ms");
        Assert.True(delay2.TotalMilliseconds >= 100, $"Second delay ({delay2.TotalMilliseconds}ms) should be at least 100ms (exponential backoff)");
    }

    [Fact]
    public async Task WrapFunctionCallAsync_MaxRetryDelay_CapsDelay()
    {
        // Arrange
        var config = new ErrorHandlingConfig
        {
            MaxRetries = 3,
            RetryDelay = TimeSpan.FromMilliseconds(100),
            BackoffMultiplier = 10.0, // Huge multiplier
            MaxRetryDelay = TimeSpan.FromMilliseconds(200) // Cap at 200ms
        };
        var middleware = new RetryMiddleware(config);
        var request = CreateFunctionRequest();

        int attempts = 0;
        var delayTimes = new List<DateTime>();
        Func<FunctionRequest, Task<object?>> handler = async (req) =>
        {
            delayTimes.Add(DateTime.UtcNow);
            attempts++;
            if (attempts <= 2)
                throw new InvalidOperationException("Transient");
            return await Task.FromResult<object?>("Success");
        };

        // Act
        await middleware.WrapFunctionCallAsync(request, handler, CancellationToken.None);

        // Assert - delays should be capped at MaxRetryDelay
        for (int i = 1; i < delayTimes.Count; i++)
        {
            var delay = delayTimes[i] - delayTimes[i - 1];
            Assert.True(delay.TotalMilliseconds <= 400); // 200ms + generous tolerance for system delays and jitter
        }
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task WrapFunctionCallAsync_MaxRetriesZero_NoRetry()
    {
        // Arrange
        var config = new ErrorHandlingConfig
        {
            MaxRetries = 0 // No retries allowed
        };
        var middleware = new RetryMiddleware(config);
        var request = CreateFunctionRequest();

        int attempts = 0;
        Func<FunctionRequest, Task<object?>> handler = (req) =>
        {
            attempts++;
            throw new InvalidOperationException("Error");
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await middleware.WrapFunctionCallAsync(request, handler, CancellationToken.None));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task WrapFunctionCallAsync_CancellationToken_StopsRetry()
    {
        // Arrange
        var config = new ErrorHandlingConfig
        {
            MaxRetries = 10,
            RetryDelay = TimeSpan.FromMilliseconds(100)
        };
        var middleware = new RetryMiddleware(config);
        var request = CreateFunctionRequest();

        var cts = new CancellationTokenSource();
        int attempts = 0;
        Func<FunctionRequest, Task<object?>> handler = (req) =>
        {
            attempts++;
            if (attempts == 2)
                cts.Cancel(); // Cancel after first retry
            throw new InvalidOperationException("Error");
        };

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await middleware.WrapFunctionCallAsync(request, handler, cts.Token));

        Assert.True(attempts <= 3); // Should stop quickly after cancellation
    }

    #endregion

    #region Model Call Retry Tests

    [Fact]
    public async Task WrapModelCallStreamingAsync_SuccessOnFirstAttempt_NoRetry()
    {
        // Arrange
        var config = new ErrorHandlingConfig
        {
            MaxRetries = 3
        };
        var middleware = new RetryMiddleware(config);
        var mockClient = new FakeChatClient();
        var request = CreateModelRequest(mockClient);

        int attempts = 0;
        async IAsyncEnumerable<ChatResponseUpdate> Handler(ModelRequest req)
        {
            attempts++;
            yield return new ChatResponseUpdate
            {
                Contents = new List<AIContent> { new TextContent("Success") }
            };
        }

        // Act
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in middleware.WrapModelCallStreamingAsync(request, Handler, CancellationToken.None)!)
        {
            updates.Add(update);
        }

        // Assert
        Assert.Single(updates);
        Assert.Equal("Success", ((TextContent)updates[0].Contents[0]).Text);
        Assert.Equal(1, attempts); // No retry
    }

    [Fact]
    public async Task WrapModelCallStreamingAsync_FailsOnce_RetriesSuccessfully()
    {
        // Arrange
        var config = new ErrorHandlingConfig
        {
            MaxRetries = 3,
            RetryDelay = TimeSpan.FromMilliseconds(10)
        };
        var middleware = new RetryMiddleware(config);
        var mockClient = new FakeChatClient();
        var request = CreateModelRequest(mockClient);

        int attempts = 0;
        async IAsyncEnumerable<ChatResponseUpdate> Handler(ModelRequest req)
        {
            attempts++;
            if (attempts == 1)
                throw new InvalidOperationException("Transient error");

            yield return new ChatResponseUpdate
            {
                Contents = new List<AIContent> { new TextContent("Success") }
            };
        }

        // Act
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in middleware.WrapModelCallStreamingAsync(request, Handler, CancellationToken.None)!)
        {
            updates.Add(update);
        }

        // Assert
        Assert.Single(updates);
        Assert.Equal("Success", ((TextContent)updates[0].Contents[0]).Text);
        Assert.Equal(2, attempts); // Initial + 1 retry
    }

    [Fact]
    public async Task WrapModelCallStreamingAsync_ClientError_DoesNotRetry()
    {
        // Arrange
        var providerHandler = new TestProviderErrorHandler
        {
            ErrorDetails = new ProviderErrorDetails
            {
                Category = ErrorCategory.ClientError // Don't retry
            },
            ShouldRetry = false
        };

        var config = new ErrorHandlingConfig
        {
            MaxRetries = 3
        };
        var middleware = new RetryMiddleware(config, providerHandler);
        var mockClient = new FakeChatClient();
        var request = CreateModelRequest(mockClient);

        int attempts = 0;
        async IAsyncEnumerable<ChatResponseUpdate> Handler(ModelRequest req)
        {
            attempts++;
            throw new InvalidOperationException("Client error");
            yield break; // Never reached
        }

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var update in middleware.WrapModelCallStreamingAsync(request, Handler, CancellationToken.None)!)
            {
                // Should never reach here
            }
        });

        Assert.Equal(1, attempts); // No retry for client errors
    }

    [Fact]
    public async Task WrapModelCallStreamingAsync_RateLimitError_RespectsRetryAfter()
    {
        // Arrange
        var providerHandler = new TestProviderErrorHandler
        {
            ErrorDetails = new ProviderErrorDetails
            {
                Category = ErrorCategory.RateLimitRetryable,
                RetryAfter = TimeSpan.FromMilliseconds(200)
            }
        };

        var config = new ErrorHandlingConfig
        {
            MaxRetries = 3,
            UseProviderRetryDelays = true
        };
        var middleware = new RetryMiddleware(config, providerHandler);
        var mockClient = new FakeChatClient();
        var request = CreateModelRequest(mockClient);

        int attempts = 0;
        var startTime = DateTime.UtcNow;
        async IAsyncEnumerable<ChatResponseUpdate> Handler(ModelRequest req)
        {
            attempts++;
            if (attempts == 1)
                throw new InvalidOperationException("Rate limit");

            yield return new ChatResponseUpdate
            {
                Contents = new List<AIContent> { new TextContent("Success") }
            };
        }

        // Act
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in middleware.WrapModelCallStreamingAsync(request, Handler, CancellationToken.None)!)
        {
            updates.Add(update);
        }
        var elapsed = DateTime.UtcNow - startTime;

        // Assert
        Assert.Single(updates);
        Assert.Equal(2, attempts);
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(180)); // Respects Retry-After
    }

    [Fact]
    public async Task WrapModelCallStreamingAsync_FailsDuringStreaming_Retries()
    {
        // Arrange
        var config = new ErrorHandlingConfig
        {
            MaxRetries = 3,
            RetryDelay = TimeSpan.FromMilliseconds(10)
        };
        var middleware = new RetryMiddleware(config);
        var mockClient = new FakeChatClient();
        var request = CreateModelRequest(mockClient);

        int attempts = 0;
        async IAsyncEnumerable<ChatResponseUpdate> Handler(ModelRequest req)
        {
            attempts++;
            if (attempts == 1)
            {
                // Fail during streaming
                yield return new ChatResponseUpdate
                {
                    Contents = new List<AIContent> { new TextContent("Partial") }
                };
                throw new InvalidOperationException("Streaming error");
            }

            yield return new ChatResponseUpdate
            {
                Contents = new List<AIContent> { new TextContent("Success") }
            };
        }

        // Act
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in middleware.WrapModelCallStreamingAsync(request, Handler, CancellationToken.None)!)
        {
            updates.Add(update);
        }

        // Assert
        // Progressive streaming behavior: Both tokens are yielded immediately (no buffering).
        // The "Partial" token streams first, then error occurs, then retry streams "Success".
        // Consumers should listen for ModelCallRetryEvent to clear partial content from UI.
        // This follows the Gemini CLI pattern: show partial response, then clear on retry.
        Assert.Equal(2, updates.Count); // "Partial" from failed attempt + "Success" from retry
        Assert.Equal("Success", ((TextContent)updates[1].Contents[0]).Text);
        Assert.Equal(2, attempts); // Initial + 1 retry
    }

    [Fact]
    public async Task WrapModelCallStreamingAsync_ExhaustsRetries_ThrowsException()
    {
        // Arrange
        var config = new ErrorHandlingConfig
        {
            MaxRetries = 2,
            RetryDelay = TimeSpan.FromMilliseconds(10)
        };
        var middleware = new RetryMiddleware(config);
        var mockClient = new FakeChatClient();
        var request = CreateModelRequest(mockClient);

        int attempts = 0;
        async IAsyncEnumerable<ChatResponseUpdate> Handler(ModelRequest req)
        {
            attempts++;
            throw new InvalidOperationException($"Persistent error (attempt {attempts})");
            yield break; // Never reached
        }

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var update in middleware.WrapModelCallStreamingAsync(request, Handler, CancellationToken.None)!)
            {
                // Should never reach here
            }
        });

        Assert.Equal(3, attempts); // Initial + 2 retries
        Assert.Contains("attempt 3", exception.Message); // Last attempt's error
    }

    #endregion

    #region Test Doubles

    private class TestProviderErrorHandler : IProviderErrorHandler
    {
        public ProviderErrorDetails? ErrorDetails { get; set; }
        public bool ShouldRetry { get; set; } = true;

        public ProviderErrorDetails? ParseError(Exception exception)
        {
            return ErrorDetails;
        }

        public TimeSpan? GetRetryDelay(
            ProviderErrorDetails details,
            int attempt,
            TimeSpan initialDelay,
            double multiplier,
            TimeSpan maxDelay)
        {
            if (!ShouldRetry)
                return null;

            if (details.RetryAfter.HasValue)
                return details.RetryAfter;

            // Default exponential backoff
            var delay = TimeSpan.FromMilliseconds(
                initialDelay.TotalMilliseconds * Math.Pow(multiplier, attempt));

            return delay > maxDelay ? maxDelay : delay;
        }

        public bool RequiresSpecialHandling(ProviderErrorDetails details)
        {
            return false;
        }
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

    private static ModelRequest CreateModelRequest(IChatClient? client = null)
    {
        client ??= new FakeChatClient();
        var messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, "Test") };
        var options = new ChatOptions();
        var state = AgentLoopState.InitialSafe(
            messages: messages,
            runId: "test-run",
            conversationId: "test-conversation",
            agentName: "TestAgent");

        return new ModelRequest
        {
            Model = client,
            Messages = messages,
            Options = options,
            State = state,
            Iteration = 0
        };
    }

}
