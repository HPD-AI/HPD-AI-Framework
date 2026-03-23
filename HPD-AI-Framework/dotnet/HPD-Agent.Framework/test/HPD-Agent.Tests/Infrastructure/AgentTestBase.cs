using System.Diagnostics;
using Microsoft.Extensions.AI;
using Xunit;
using HPD.Agent;
using HPD.Agent.Providers;
namespace HPD.Agent.Tests.Infrastructure;

/// <summary>
/// Base class for all agent tests with AsyncLocal cleanup and background task tracking.
/// Provides helper methods for creating agents, messages, and test data.
/// Implements IAsyncDisposable for proper async cleanup (xUnit 2.5+ support).
/// </summary>
public abstract class AgentTestBase : IAsyncDisposable, IDisposable
{
    private readonly List<Task> _backgroundTasks = new();
    private readonly CancellationTokenSource _testCts = new();
    private bool _disposed = false;

    /// <summary>
    /// Creates an agent with default configuration.
    /// All operations use TestCancellationToken for clean shutdown.
    /// </summary>
    internal Agent CreateAgent(
        AgentConfig? config = null,
        IChatClient? client = null,
        int? circuitBreakerThreshold = 5,
        params AIFunction[] tools)
    {
        return TestAgentFactory.Create(config, client, circuitBreakerThreshold, tools);
    }

    /// <summary>
    /// Tracks a background task for cleanup.
    /// IMPORTANT: Call this for any fire-and-forget or long-running operations.
    /// </summary>
    protected void TrackBackgroundTask(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        _backgroundTasks.Add(task);
    }

    /// <summary>
    /// Gets a cancellation token that is cancelled when test completes.
    /// Use this for all agent operations to ensure clean shutdown.
    /// </summary>
    protected CancellationToken TestCancellationToken => _testCts.Token;

    /// <summary>
    /// Default agent configuration for tests.
    /// </summary>
    protected static AgentConfig DefaultConfig() => new()
    {
        Name = "TestAgent",
        MaxAgenticIterations = 50,
        Provider = new ProviderConfig
        {
            ProviderKey = "test",  // Required by validation
            ModelName = "test-model"
        },
        AgenticLoop = new AgenticLoopConfig
        {
            MaxTurnDuration = TimeSpan.FromMinutes(1)
        },
        ErrorHandling = new ErrorHandlingConfig
        {
            MaxRetries = 3,
            NormalizeErrors = true
        }
    };

    //      
    // SIMPLE HELPERS (for common, readable test cases)
    //      

    /// <summary>
    /// Creates a user message with the given text.
    /// Use for simple, readable test cases.
    /// </summary>
    protected static ChatMessage UserMessage(string text) =>
        new(ChatRole.User, text);

    /// <summary>
    /// Creates an assistant message with the given text.
    /// Use for simple, readable test cases.
    /// </summary>
    protected static ChatMessage AssistantMessage(string text) =>
        new(ChatRole.Assistant, text);

    /// <summary>
    /// Creates a tool result message.
    /// Use for simple, readable test cases.
    /// </summary>
    protected static ChatMessage ToolResultMessage(string callId, object result) =>
        new(ChatRole.Tool, [new FunctionResultContent(callId, result)]);

    /// <summary>
    /// Creates a list of messages for a simple conversation.
    /// </summary>
    protected static List<ChatMessage> CreateSimpleConversation(params string[] userMessages)
    {
        return userMessages.Select(msg => UserMessage(msg)).ToList();
    }

    //      
    // ASSERTION HELPERS
    //      

    /// <summary>
    /// Asserts that an event sequence matches expected types in order.
    /// </summary>
    protected static void AssertEventSequence(
        IEnumerable<AgentEvent> actualEvents,
        params Type[] expectedEventTypes)
    {
        var actual = actualEvents.Select(e => e.GetType()).ToList();
        var expected = expectedEventTypes.ToList();

        Assert.Equal(expected.Count, actual.Count);

        for (int i = 0; i < expected.Count; i++)
        {
            Assert.True(
                expected[i].IsAssignableFrom(actual[i]),
                $"Event at index {i}: expected {expected[i].Name}, got {actual[i].Name}");
        }
    }

    /// <summary>
    /// Asserts that the event sequence contains a specific event type.
    /// </summary>
    protected static void AssertContainsEvent<TEvent>(
        IEnumerable<AgentEvent> actualEvents) where TEvent : AgentEvent
    {
        Assert.Contains(actualEvents, e => e is TEvent);
    }

    //      
    // ASYNC DISPOSAL AND CLEANUP
    //      

    /// <summary>
    /// Async disposal: waits for background tasks, then clears AsyncLocal.
    /// Automatically called by xUnit 2.5+.
    /// </summary>
    public virtual async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        // Signal cancellation to all tracked tasks
        _testCts.Cancel();

        // Wait for all background tasks to complete (with 5-second timeout)
        if (_backgroundTasks.Count > 0)
        {
            using var gracefulShutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await Task.WhenAll(_backgroundTasks).WaitAsync(gracefulShutdownCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Tasks didn't complete within timeout
                Debug.WriteLine($"WARNING: {_backgroundTasks.Count} background tasks did not complete within 5 seconds");
            }
            catch (Exception ex)
            {
                // Tasks threw exceptions during cleanup (expected in some cases)
                Debug.WriteLine($"Background tasks threw during cleanup: {ex.Message}");
            }
        }

        // Now safe to clear AsyncLocal (no tasks running)
        ClearAsyncLocalState();

        // Dispose resources
        _testCts.Dispose();

        _disposed = true;

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Synchronous disposal: blocks on async cleanup.
    /// Used by test frameworks that don't support IAsyncDisposable.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Clears AsyncLocal state. Only called after all tasks complete.
    /// Override to add custom cleanup logic.
    /// </summary>
    protected virtual void ClearAsyncLocalState()
    {
        Agent.CurrentFunctionContext = null;
    }
}
