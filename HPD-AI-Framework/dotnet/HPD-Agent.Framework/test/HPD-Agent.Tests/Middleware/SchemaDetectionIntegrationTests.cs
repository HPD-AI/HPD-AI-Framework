// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using Xunit;

namespace HPD.Agent.Tests.Middleware;

/// <summary>
/// Integration tests for middleware state schema detection during checkpoint resume.
/// Tests the runtime validation logic for checkpoint middleware schema matching.
/// </summary>
public class SchemaDetectionIntegrationTests : AgentTestBase
{
    private readonly TestLoggerProvider _loggerProvider = new();
    private readonly TestEventObserver _eventObserver = new();
    private InMemorySessionStore? _sessionStore;

    [Fact]
    public async Task Resume_WithPreVersioningCheckpoint_ThrowsInvalidOperationException()
    {
        var preVersioningState = CreatePreVersioningCheckpoint();
        var (session, thread) = await CreateSessionWithCheckpoint(preVersioningState);
        var agent = CreateTestAgentWithLogging(_sessionStore!);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.RunAsync(new UserMessagesInputEvent(Array.Empty<ChatMessage>())
            {
                Session = session,
                Thread = thread
            }, TestCancellationToken));

        Assert.Contains("without middleware schema metadata", exception.Message);
        Assert.Empty(_eventObserver.GetEvents<SchemaChangedEvent>());
    }

    [Fact]
    public async Task Resume_WithRemovedMiddleware_ThrowsInvalidOperationException()
    {
        var checkpointWithOldMiddleware = CreateCheckpointWithRemovedMiddleware();
        var (session, thread) = await CreateSessionWithCheckpoint(checkpointWithOldMiddleware);

        var agent = CreateTestAgentWithLogging(_sessionStore!);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.RunAsync(new UserMessagesInputEvent(Array.Empty<ChatMessage>())
            {
                Session = session,
                Thread = thread
            }, TestCancellationToken));

        Assert.Contains("different middleware schema", exception.Message);
        Assert.Contains("ObsoleteMiddlewareStateData", exception.Message);
        Assert.Empty(_eventObserver.GetEvents<SchemaChangedEvent>());
    }

    [Fact]
    public async Task Resume_WithAddedMiddleware_ThrowsInvalidOperationException()
    {
        var checkpointBeforeNewMiddleware = CreateCheckpointWithFewerMiddleware();
        var (session, thread) = await CreateSessionWithCheckpoint(checkpointBeforeNewMiddleware);

        var agent = CreateTestAgentWithLogging(_sessionStore!);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.RunAsync(new UserMessagesInputEvent(Array.Empty<ChatMessage>())
            {
                Session = session,
                Thread = thread
            }, TestCancellationToken));

        Assert.Contains("different middleware schema", exception.Message);
        Assert.Empty(_eventObserver.GetEvents<SchemaChangedEvent>());
    }

    [Fact]
    public async Task Resume_WithMissingStateVersions_ThrowsInvalidOperationException()
    {
        var currentCheckpoint = CreateCheckpointWithCurrentSchema();
        var checkpoint = new MiddlewareState
        {
            States = currentCheckpoint.States,
            SchemaSignature = currentCheckpoint.SchemaSignature,
            SchemaVersion = currentCheckpoint.SchemaVersion,
            StateVersions = null
        };
        var (session, thread) = await CreateSessionWithCheckpoint(checkpoint);

        var agent = CreateTestAgentWithLogging(_sessionStore!);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.RunAsync(new UserMessagesInputEvent(Array.Empty<ChatMessage>())
            {
                Session = session,
                Thread = thread
            }, TestCancellationToken));

        Assert.Contains("without middleware state version metadata", exception.Message);
    }

    [Fact]
    public async Task Resume_WithChangedStateVersion_ThrowsInvalidOperationException()
    {
        var currentVersions = GetExpectedSchemaVersions();
        var firstKey = currentVersions.Keys.OrderBy(key => key, StringComparer.Ordinal).First();
        var currentCheckpoint = CreateCheckpointWithCurrentSchema();
        var checkpoint = new MiddlewareState
        {
            States = currentCheckpoint.States,
            SchemaSignature = currentCheckpoint.SchemaSignature,
            SchemaVersion = currentCheckpoint.SchemaVersion,
            StateVersions = currentVersions.SetItem(firstKey, currentVersions[firstKey] + 1)
        };
        var (session, thread) = await CreateSessionWithCheckpoint(checkpoint);

        var agent = CreateTestAgentWithLogging(_sessionStore!);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.RunAsync(new UserMessagesInputEvent(Array.Empty<ChatMessage>())
            {
                Session = session,
                Thread = thread
            }, TestCancellationToken));

        Assert.Contains("version", exception.Message);
        Assert.Contains(firstKey, exception.Message);
    }

    [Fact]
    public async Task Resume_WithUnchangedSchema_NoLogging()
    {
        // Arrange: Checkpoint with current schema
        var currentCheckpoint = CreateCheckpointWithCurrentSchema();
        var (session, thread) = await CreateSessionWithCheckpoint(currentCheckpoint);

        var agent = CreateTestAgentWithLogging(_sessionStore!);

        // Act: Resume
        await agent.RunAsync(new UserMessagesInputEvent(Array.Empty<ChatMessage>())
        {
            Session = session,
            Thread = thread
        }, TestCancellationToken);

        // Assert: No schema-related logs
        var logs = _loggerProvider.GetLogs();
        Assert.DoesNotContain(logs, log =>
            log.Message.Contains("schema", StringComparison.OrdinalIgnoreCase) ||
            log.Message.Contains("middleware", StringComparison.OrdinalIgnoreCase) ||
            log.Message.Contains("versioning", StringComparison.OrdinalIgnoreCase));

        // Assert: No SchemaChangedEvent emitted
        var schemaEvents = _eventObserver.GetEvents<SchemaChangedEvent>();
        Assert.Empty(schemaEvents);
    }

    //      
    // HELPER METHODS
    //      

    private Agent CreateTestAgentWithLogging(ISessionStore store)
    {
        var client = new FakeChatClient();
        client.EnqueueTextResponse("Test response");

        var config = DefaultConfig();

        // Enable observability events so SchemaChangedEvent is emitted to observers
        config.Observability = new ObservabilityConfig
        {
            EmitObservabilityEvents = true
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(_loggerProvider));
        var providerRegistry = new TestProviderRegistry(client);

        // Create service provider with ILoggerFactory registered
        // This is required because Agent retrieves ILoggerFactory from the service provider
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        var serviceProvider = services.BuildServiceProvider();

        var agent = new AgentBuilder(config, providerRegistry)
            .WithEventSubscription(coordinator =>
                coordinator.Subscribe<AgentEvent>(_eventObserver.HandleAsync))
            .WithServiceProvider(serviceProvider)
            .WithSessionStore(store)
            .BuildAsync(CancellationToken.None).GetAwaiter().GetResult();

        return agent;
    }

    private async Task<(global::HPD.Agent.Session session, global::HPD.Agent.Thread thread)> CreateSessionWithCheckpoint(MiddlewareState middlewareState)
    {
        var sessionId = "test-session";
        var session = new global::HPD.Agent.Session(sessionId);
        var thread = new global::HPD.Agent.Thread(sessionId);

        // V3 resume path: save an UncommittedTurn to an InMemorySessionStore.
        // The agent loads this from the store during RunAsync when no new messages are provided.
        _sessionStore = new InMemorySessionStore();

        var uncommittedTurn = new UncommittedTurn
        {
            SessionId = sessionId,
            ThreadId = UncommittedTurn.DefaultThread,
            TurnId = "schema-checkpoint-turn",
            Iteration = 1,
            CompletedFunctions = ImmutableHashSet<string>.Empty,
            MiddlewareState = middlewareState,
            IsTerminated = false,
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow
        };

        await _sessionStore.SaveUncommittedTurnAsync(uncommittedTurn);

        return (session, thread);
    }

    private MiddlewareState CreatePreVersioningCheckpoint()
    {
        // Create middleware state without schema metadata (SchemaSignature = null)
        return new MiddlewareState
        {
            States = ImmutableDictionary<string, object?>.Empty,
            SchemaSignature = null,  // Pre-versioning
            SchemaVersion = 0,
            StateVersions = null
        };
    }

    private MiddlewareState CreateCheckpointWithRemovedMiddleware()
    {
        var currentSignature = "HPD.Agent.ErrorTrackingStateData";
        var fakeOldSignature = currentSignature + ",HPD.Agent.ObsoleteMiddlewareStateData";

        return new MiddlewareState
        {
            States = ImmutableDictionary<string, object?>.Empty
                .Add("HPD.Agent.ObsoleteMiddlewareStateData", new { }),
            SchemaSignature = fakeOldSignature,
            SchemaVersion = 1,
            StateVersions = ImmutableDictionary<string, int>.Empty
                .Add("HPD.Agent.ErrorTrackingStateData", 1)
                .Add("HPD.Agent.ObsoleteMiddlewareStateData", 1)
        };
    }

    private MiddlewareState CreateCheckpointWithFewerMiddleware()
    {
        var currentSignature = GetExpectedSchemaSignature();
        var currentTypes = currentSignature.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        var olderSignature = currentTypes.Count > 0 ? currentTypes[0] : "";

        return new MiddlewareState
        {
            States = ImmutableDictionary<string, object?>.Empty,
            SchemaSignature = olderSignature,
            SchemaVersion = 1,
            StateVersions = GetExpectedSchemaVersions()
                .Where(kvp => string.Equals(kvp.Key, olderSignature, StringComparison.Ordinal))
                .ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };
    }

    private MiddlewareState CreateCheckpointWithCurrentSchema()
    {
        // Create middleware state with a schema signature that matches what the agent will compute.
        // The agent computes schema from its registered _stateFactories at runtime.
        // For a default AgentBuilder, this includes all [MiddlewareState] types from HPD-Agent.
        // We need to provide a signature that matches to avoid triggering schema change detection.
        //
        // When schema signatures and state versions match exactly, resume proceeds unchanged.
        var currentSignature = GetExpectedSchemaSignature();

        return new MiddlewareState
        {
            States = ImmutableDictionary<string, object?>.Empty,
            SchemaSignature = currentSignature,
            SchemaVersion = 1,
            StateVersions = GetExpectedSchemaVersions()
        };
    }

    /// <summary>
    /// Gets the expected schema signature that matches what CreateTestAgentWithLogging() will compute.
    /// This is determined by what MiddlewareStateRegistry.All contains in the HPD-Agent assembly.
    /// </summary>
    private string GetExpectedSchemaSignature()
    {
        // The schema signature is computed from the agent's _stateFactories keys, sorted alphabetically.
        // For a default AgentBuilder, this includes all [MiddlewareState] types discovered by the generator.
        // We can get this by building a temporary agent and inspecting, or by knowing the generated types.
        //
        // For this test, we use reflection to get the actual registry from the generated code.
        var registryType = typeof(MiddlewareState).Assembly.GetType("HPD.Agent.Generated.MiddlewareStateRegistry");
        if (registryType == null)
        {
            // Fallback: return empty signature which will trigger schema change (test may need adjustment)
            return "";
        }

        var allField = registryType.GetField("All", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (allField?.GetValue(null) is MiddlewareStateFactory[] factories)
        {
            return string.Join(",", factories.Select(f => f.FullyQualifiedName).OrderBy(k => k, StringComparer.Ordinal));
        }

        return "";
    }

    private ImmutableDictionary<string, int> GetExpectedSchemaVersions()
    {
        var registryType = typeof(MiddlewareState).Assembly.GetType("HPD.Agent.Generated.MiddlewareStateRegistry");
        if (registryType == null)
            return ImmutableDictionary<string, int>.Empty;

        var allField = registryType.GetField("All", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (allField?.GetValue(null) is MiddlewareStateFactory[] factories)
        {
            return factories.ToImmutableDictionary(
                f => f.FullyQualifiedName,
                f => f.Version,
                StringComparer.Ordinal);
        }

        return ImmutableDictionary<string, int>.Empty;
    }
}

/// <summary>
/// Test event observer that captures events for assertions.
/// </summary>
internal class TestEventObserver
{
    private readonly List<AgentEvent> _events = new();

    public ValueTask HandleAsync(AgentEvent evt)
    {
        lock (_events)
        {
            _events.Add(evt);
        }

        return ValueTask.CompletedTask;
    }

    public List<T> GetEvents<T>() where T : AgentEvent
    {
        lock (_events)
        {
            return _events.OfType<T>().ToList();
        }
    }

    public void Clear()
    {
        lock (_events)
        {
            _events.Clear();
        }
    }
}

/// <summary>
/// Test logger provider that captures logs for assertions.
/// </summary>
internal class TestLoggerProvider : ILoggerProvider
{
    private readonly TestLogger _logger = new();

    public ILogger CreateLogger(string categoryName) => _logger;

    public void Dispose() { }

    public List<LogEntry> GetLogs() => _logger.GetLogs();
}

/// <summary>
/// Test logger that captures log entries.
/// </summary>
internal class TestLogger : ILogger
{
    private readonly List<LogEntry> _logs = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _logs.Add(new LogEntry
        {
            LogLevel = logLevel,
            Message = formatter(state, exception),
            Exception = exception
        });
    }

    public List<LogEntry> GetLogs() => _logs;
}

/// <summary>
/// Captured log entry for test assertions.
/// </summary>
internal class LogEntry
{
    public LogLevel LogLevel { get; set; }
    public string Message { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
}
