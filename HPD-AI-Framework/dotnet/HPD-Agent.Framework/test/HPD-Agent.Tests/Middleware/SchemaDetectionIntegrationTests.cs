// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent;
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
/// Tests the runtime validation logic in Agent.ValidateAndMigrateSchema().
/// </summary>
public class SchemaDetectionIntegrationTests : AgentTestBase
{
    private readonly TestLoggerProvider _loggerProvider = new();
    private readonly TestEventObserver _eventObserver = new();
    private InMemorySessionStore? _sessionStore;

    [Fact]
    public async Task Resume_WithPreVersioningCheckpoint_UpgradesSchema()
    {
        // Arrange: Create checkpoint without schema metadata (simulate old version)
        var preVersioningState = CreatePreVersioningCheckpoint();
        var (session, branch) = await CreateSessionWithCheckpoint(preVersioningState);
        var agent = CreateTestAgentWithLogging(_sessionStore!);

        // Act: Resume from pre-versioning checkpoint
        await foreach (var evt in agent.RunAsync(
            Array.Empty<ChatMessage>(),
            session: session,
            branch: branch,
            cancellationToken: TestCancellationToken))
        {
            // Consume events
        }

        // Wait a bit for async observer notifications to complete
        await Task.Delay(100);

        // Assert: Schema upgraded and logged
        var logs = _loggerProvider.GetLogs();
        Assert.Contains(logs, log =>
            log.LogLevel == LogLevel.Information &&
            log.Message.Contains("before schema versioning"));

        // Assert: SchemaChangedEvent emitted
        var schemaEvents = _eventObserver.GetEvents<SchemaChangedEvent>();
        Assert.Single(schemaEvents);
        var schemaEvent = schemaEvents[0];
        Assert.Null(schemaEvent.OldSignature);
        Assert.NotNull(schemaEvent.NewSignature);
        Assert.True(schemaEvent.IsUpgrade);
    }

    [Fact]
    public async Task Resume_WithRemovedMiddleware_LogsWarning()
    {
        // Arrange: Checkpoint with middleware that no longer exists
        var checkpointWithOldMiddleware = CreateCheckpointWithRemovedMiddleware();
        var (session, branch) = await CreateSessionWithCheckpoint(checkpointWithOldMiddleware);

        var agent = CreateTestAgentWithLogging(_sessionStore!);

        // Act: Resume
        await foreach (var evt in agent.RunAsync(
            Array.Empty<ChatMessage>(),
            session: session,
            branch: branch,
            cancellationToken: TestCancellationToken))
        {
            // Consume events
        }

        // Assert: Warning logged
        var logs = _loggerProvider.GetLogs();
        Assert.Contains(logs, log =>
            log.LogLevel == LogLevel.Warning &&
            log.Message.Contains("no longer exist") &&
            log.Message.Contains("discarded"));

        // Assert: SchemaChangedEvent emitted with removed types
        var schemaEvents = _eventObserver.GetEvents<SchemaChangedEvent>();
        Assert.Single(schemaEvents);
        var schemaEvent = schemaEvents[0];
        Assert.NotEmpty(schemaEvent.RemovedTypes);
        Assert.False(schemaEvent.IsUpgrade);
    }

    [Fact]
    public async Task Resume_WithAddedMiddleware_LogsInfo()
    {
        // Arrange: Checkpoint without new middleware
        var checkpointBeforeNewMiddleware = CreateCheckpointWithFewerMiddleware();
        var (session, branch) = await CreateSessionWithCheckpoint(checkpointBeforeNewMiddleware);

        var agent = CreateTestAgentWithLogging(_sessionStore!);

        // Act: Resume (agent now has more middleware)
        await foreach (var evt in agent.RunAsync(
            Array.Empty<ChatMessage>(),
            session: session,
            branch: branch,
            cancellationToken: TestCancellationToken))
        {
            // Consume events
        }

        // Assert: Info logged about new middleware being initialized to defaults
        var logs = _loggerProvider.GetLogs();
        Assert.Contains(logs, log =>
            log.LogLevel == LogLevel.Information &&
            log.Message.Contains("new middleware") &&
            log.Message.Contains("defaults"));

        // Assert: SchemaChangedEvent emitted with added types
        var schemaEvents = _eventObserver.GetEvents<SchemaChangedEvent>();
        Assert.Single(schemaEvents);
        var schemaEvent = schemaEvents[0];
        Assert.NotEmpty(schemaEvent.AddedTypes);
        Assert.False(schemaEvent.IsUpgrade);
    }

    [Fact]
    public async Task Resume_WithUnchangedSchema_NoLogging()
    {
        // Arrange: Checkpoint with current schema
        var currentCheckpoint = CreateCheckpointWithCurrentSchema();
        var (session, branch) = await CreateSessionWithCheckpoint(currentCheckpoint);

        var agent = CreateTestAgentWithLogging(_sessionStore!);

        // Act: Resume
        await foreach (var evt in agent.RunAsync(
            Array.Empty<ChatMessage>(),
            session: session,
            branch: branch,
            cancellationToken: TestCancellationToken))
        {
            // Consume events
        }

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
            .WithObserver(_eventObserver)
            .WithServiceProvider(serviceProvider)
            .WithSessionStore(store)
            .BuildAsync(CancellationToken.None).GetAwaiter().GetResult();

        return agent;
    }

    private async Task<(global::HPD.Agent.Session session, global::HPD.Agent.Branch branch)> CreateSessionWithCheckpoint(MiddlewareState middlewareState)
    {
        var sessionId = "test-session";
        var session = new global::HPD.Agent.Session(sessionId);
        var branch = new global::HPD.Agent.Branch(sessionId);

        // V3 resume path: save an UncommittedTurn to an InMemorySessionStore.
        // The agent loads this from the store during RunAsync when no new messages are provided.
        _sessionStore = new InMemorySessionStore();

        var uncommittedTurn = new UncommittedTurn
        {
            SessionId = sessionId,
            BranchId = UncommittedTurn.DefaultBranch,
            TurnMessages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.Assistant, "Resuming from checkpoint")
            },
            Iteration = 1,
            CompletedFunctions = ImmutableHashSet<string>.Empty,
            MiddlewareState = middlewareState,
            IsTerminated = false,
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow
        };

        await _sessionStore.SaveUncommittedTurnAsync(uncommittedTurn);

        return (session, branch);
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
        // Create middleware state with a fake middleware that doesn't exist in current schema
        // Use a known state type name + a fake obsolete one
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
        // Create middleware state with fewer middleware than would be registered at runtime.
        // This simulates a checkpoint from an older version with fewer state types.
        // We use a subset of the current schema to trigger the "new middleware added" case.
        var currentSignature = GetExpectedSchemaSignature();
        var currentTypes = currentSignature.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        // Take only the first type (or just one type) to create an "older" checkpoint
        var olderSignature = currentTypes.Count > 0 ? currentTypes[0] : "";

        return new MiddlewareState
        {
            States = ImmutableDictionary<string, object?>.Empty,
            SchemaSignature = olderSignature,
            SchemaVersion = 1,
            StateVersions = string.IsNullOrEmpty(olderSignature)
                ? ImmutableDictionary<string, int>.Empty
                : ImmutableDictionary<string, int>.Empty.Add(olderSignature, 1)
        };
    }

    private MiddlewareState CreateCheckpointWithCurrentSchema()
    {
        // Create middleware state with a schema signature that matches what the agent will compute.
        // The agent computes schema from its registered _stateFactories at runtime.
        // For a default AgentBuilder, this includes all [MiddlewareState] types from HPD-Agent.
        // We need to provide a signature that matches to avoid triggering schema change detection.
        //
        // The key insight: When schema signatures match exactly, ValidateAndMigrateSchema
        // returns the checkpoint state unchanged (no logging, no events).
        var currentSignature = GetExpectedSchemaSignature();

        return new MiddlewareState
        {
            States = ImmutableDictionary<string, object?>.Empty,
            SchemaSignature = currentSignature,
            SchemaVersion = 1,
            StateVersions = ImmutableDictionary<string, int>.Empty
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
}

/// <summary>
/// Test event observer that captures events for assertions.
/// </summary>
internal class TestEventObserver : IAgentEventObserver
{
    private readonly List<AgentEvent> _events = new();

    public Task OnEventAsync(AgentEvent evt, CancellationToken cancellationToken = default)
    {
        lock (_events)
        {
            _events.Add(evt);
        }
        return Task.CompletedTask;
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
