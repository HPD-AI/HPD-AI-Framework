using HPD.Agent;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HPD.Agent.Tests.Middleware;

/// <summary>
/// Tests for LoggingMiddleware - the unified logging middleware that replaces
/// LoggingAIFunctionMiddleware, FunctionLoggingMiddleware, and PromptLoggingAgentMiddleware.
/// </summary>
public class LoggingMiddlewareTests
{
    //     
    // CONFIGURATION TESTS
    //     

    [Fact]
    public void DefaultOptions_HasReasonableDefaults()
    {
        // Arrange & Act
        var options = LoggingMiddlewareOptions.Default;

        // Assert
        Assert.True(options.LogMessageTurn);
        Assert.True(options.LogIteration);  // Default is true
        Assert.True(options.LogFunction);
        Assert.True(options.IncludeTiming);
        Assert.True(options.IncludeArguments);
        Assert.True(options.IncludeResults);
        Assert.True(options.IncludeInstructions);
        Assert.Equal(1000, options.MaxStringLength);
        Assert.Equal("[HPD-Agent]", options.LogPrefix);
    }

    [Fact]
    public void MinimalOptions_LogsMinimalInfo()
    {
        // Arrange & Act
        var options = LoggingMiddlewareOptions.Minimal;

        // Assert
        Assert.False(options.LogMessageTurn);
        Assert.False(options.LogIteration);
        Assert.True(options.LogFunction);
        Assert.False(options.IncludeArguments);
        Assert.False(options.IncludeResults);
        Assert.True(options.IncludeTiming);
    }

    [Fact]
    public void VerboseOptions_LogsEverything()
    {
        // Arrange & Act
        var options = LoggingMiddlewareOptions.Verbose;

        // Assert
        Assert.True(options.LogMessageTurn);
        Assert.True(options.LogIteration);
        Assert.True(options.LogFunction);
        Assert.True(options.IncludeArguments);
        Assert.True(options.IncludeResults);
        Assert.True(options.IncludeTiming);
        Assert.True(options.IncludeInstructions);
        Assert.Equal(0, options.MaxStringLength); // Unlimited
    }

    //     
    // MESSAGE TURN TESTS
    //     

    [Fact]
    public async Task BeforeMessageTurn_WhenEnabled_LogsContext()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogMessageTurn = true,
            LogFunction = false
        });

        var context = CreateBeforeMessageTurnContext();

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        Assert.NotEmpty(logOutput);
        Assert.Contains(logOutput, s => s.Contains("MESSAGE TURN"));
        Assert.Contains(logOutput, s => s.Contains("TestAgent"));
    }

    [Fact]
    public async Task BeforeMessageTurn_WhenDisabled_DoesNotLog()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogMessageTurn = false
        });

        var context = CreateBeforeMessageTurnContext();

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        Assert.Empty(logOutput);
    }

    [Fact]
    public async Task AfterMessageTurn_WhenEnabled_LogsCompletion()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogMessageTurn = true
        });

        var context = CreateAfterMessageTurnContext();

        // Act
        await middleware.AfterMessageTurnAsync(context, CancellationToken.None);

        // Assert
        Assert.NotEmpty(logOutput);
        Assert.Contains(logOutput, s => s.Contains("END"));
    }

    //     
    // ITERATION TESTS
    //     

    [Fact]
    public async Task BeforeIteration_WhenEnabled_LogsIterationInfo()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogIteration = true,
            LogMessageTurn = false,
            LogFunction = false
        });

        var context = CreateBeforeIterationContext(iteration: 2);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert
        Assert.NotEmpty(logOutput);
        Assert.Contains(logOutput, s => s.Contains("ITERATION"));
    }

    [Fact]
    public async Task BeforeIteration_WhenDisabled_DoesNotLog()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogIteration = false
        });

        var context = CreateBeforeIterationContext();

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert
        Assert.Empty(logOutput);
    }

    [Fact]
    public async Task AfterIteration_WhenEnabled_LogsToolResults()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogIteration = true
        });

        var toolResults = new List<FunctionResultContent>
        {
            new FunctionResultContent("call1", "Success"),
            new FunctionResultContent("call2", "Also success")
        };
        var context = CreateAfterIterationContext(toolResults: toolResults);

        // Act
        await middleware.AfterIterationAsync(context, CancellationToken.None);

        // Assert
        Assert.NotEmpty(logOutput);
        Assert.Contains(logOutput, s => s.Contains("ITERATION END"));
    }

    //     
    // FUNCTION TESTS
    //     

    [Fact]
    public async Task BeforeFunction_WhenEnabled_LogsFunctionInfo()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogFunction = true,
            IncludeArguments = true
        });

        var func = AIFunctionFactory.Create(() => "test", "TestFunc", "Test function");
        var args = new Dictionary<string, object?> { ["param1"] = "value1" };
        var context = CreateBeforeFunctionContext(function: func, arguments: args);

        // Act
        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        // Assert
        Assert.NotEmpty(logOutput);
        Assert.Contains(logOutput, s => s.Contains("[PRE]"));
        Assert.Contains(logOutput, s => s.Contains("TestFunc"));
    }

    [Fact]
    public async Task BeforeFunction_WithoutArguments_OmitsArgs()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogFunction = true,
            IncludeArguments = false
        });

        var func = AIFunctionFactory.Create(() => "test", "TestFunc");
        var args = new Dictionary<string, object?> { ["secret"] = "password123" };
        var context = CreateBeforeFunctionContext(function: func, arguments: args);

        // Act
        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        // Assert
        Assert.NotEmpty(logOutput);
        Assert.DoesNotContain(logOutput, s => s.Contains("password123"));
    }

    [Fact]
    public async Task AfterFunction_Success_LogsResultAndTiming()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogFunction = true,
            IncludeTiming = true,
            IncludeResults = true
        });

        var func = AIFunctionFactory.Create(() => "test", "TestFunc");

        // Simulate BeforeFunction to start timer
        var beforeContext = CreateBeforeFunctionContext(function: func);
        await middleware.BeforeFunctionAsync(beforeContext, CancellationToken.None);
        logOutput.Clear();

        // Act
        var afterContext = CreateAfterFunctionContext(function: func, result: "Success result");
        await middleware.AfterFunctionAsync(afterContext, CancellationToken.None);

        // Assert
        Assert.NotEmpty(logOutput);
        Assert.Contains(logOutput, s => s.Contains("[POST]"));
        Assert.Contains(logOutput, s => s.Contains("OK"));
        Assert.Contains(logOutput, s => s.Contains("ms")); // Timing
    }

    [Fact]
    public async Task AfterFunction_Error_LogsError()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogFunction = true
        });

        var func = AIFunctionFactory.Create(() => "test", "TestFunc");
        var context = CreateAfterFunctionContext(
            function: func,
            exception: new InvalidOperationException("Something went wrong"));

        // Act
        await middleware.AfterFunctionAsync(context, CancellationToken.None);

        // Assert
        Assert.NotEmpty(logOutput);
        Assert.Contains(logOutput, s => s.Contains("FAILED"));
        Assert.Contains(logOutput, s => s.Contains("Something went wrong"));
    }

    [Fact]
    public async Task AfterFunction_WithoutResults_OmitsResult()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogFunction = true,
            IncludeResults = false
        });

        var func = AIFunctionFactory.Create(() => "test", "TestFunc");
        var context = CreateAfterFunctionContext(
            function: func,
            result: "Secret result that should not be logged");

        // Act
        await middleware.AfterFunctionAsync(context, CancellationToken.None);

        // Assert
        Assert.NotEmpty(logOutput);
        Assert.DoesNotContain(logOutput, s => s.Contains("Secret result"));
    }

    //     
    // STRING TRUNCATION TESTS
    //     

    [Fact]
    public async Task LogsWithMaxStringLength_TruncatesLongStrings()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogFunction = true,
            IncludeResults = true,
            MaxStringLength = 20
        });

        var func = AIFunctionFactory.Create(() => "test", "TestFunc");
        var context = CreateAfterFunctionContext(
            function: func,
            result: "This is a very long result that should be truncated");

        // Act
        await middleware.AfterFunctionAsync(context, CancellationToken.None);

        // Assert
        Assert.Contains(logOutput, s => s.Contains("..."));
        Assert.DoesNotContain(logOutput, s => s.Contains("truncated")); // End of string should be cut
    }

    [Fact]
    public async Task LogsWithUnlimitedLength_DoesNotTruncate()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, LoggingMiddlewareOptions.Verbose);

        var func = AIFunctionFactory.Create(() => "test", "TestFunc");
        var context = CreateAfterFunctionContext(
            function: func,
            result: "This is a very long result that should NOT be truncated because MaxStringLength is 0");

        // Act
        await middleware.AfterFunctionAsync(context, CancellationToken.None);

        // Assert
        Assert.Contains(logOutput, s => s.Contains("should NOT be truncated"));
    }

    //
    // CUSTOM PREFIX TESTS
    //

    [Fact]
    public async Task LogsWithCustomPrefix_UsesPrefix()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogFunction = true,
            LogPrefix = "[MyCustomApp]"
        });

        var func = AIFunctionFactory.Create(() => "test", "TestFunc");
        var context = CreateBeforeFunctionContext(function: func);

        // Act
        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        // Assert
        Assert.Contains(logOutput, s => s.Contains("[MyCustomApp]"));
    }

    //     
    // TIMING/STOPWATCH TESTS (ported from IterationLoggingFilterTests)
    //     

    [Fact]
    public async Task FunctionTiming_MeasuresExecutionTime()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogFunction = true,
            IncludeTiming = true
        });

        var func = AIFunctionFactory.Create(() => "test", "TestFunc");
        var delayMs = 50;

        // Act
        var startTime = DateTime.UtcNow;
        var beforeContext = CreateBeforeFunctionContext(function: func);
        await middleware.BeforeFunctionAsync(beforeContext, CancellationToken.None);
        await Task.Delay(delayMs); // Simulate work
        var afterContext = CreateAfterFunctionContext(function: func, result: "Done");
        await middleware.AfterFunctionAsync(afterContext, CancellationToken.None);
        var endTime = DateTime.UtcNow;

        // Assert
        var actualDuration = (endTime - startTime).TotalMilliseconds;
        // Allow 5ms tolerance for Task.Delay imprecision (can complete slightly early)
        Assert.True(actualDuration >= delayMs - 5, $"Expected at least {delayMs - 5}ms, got {actualDuration}ms");

        // Verify timing was logged
        Assert.Contains(logOutput, s => s.Contains("ms"));
        Assert.Contains(logOutput, s => s.Contains("[POST]"));
    }

    [Fact]
    public async Task FunctionTiming_StartsStopwatch_InBeforePhase()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogFunction = true,
            IncludeTiming = true
        });

        var func = AIFunctionFactory.Create(() => "test", "TestFunc");

        // Act - Before
        var beforeContext = CreateBeforeFunctionContext(function: func);
        await middleware.BeforeFunctionAsync(beforeContext, CancellationToken.None);

        // Simulate work
        await Task.Delay(20);

        // Act - After
        var afterContext = CreateAfterFunctionContext(function: func, result: "Done");
        await middleware.AfterFunctionAsync(afterContext, CancellationToken.None);

        // Assert - Stopwatch should have measured time (check for timing in log)
        Assert.Contains(logOutput, s => s.Contains("ms"));
    }

    [Fact]
    public async Task FunctionTiming_WithoutTiming_OmitsTiming()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogFunction = true,
            IncludeTiming = false  // Timing disabled
        });

        var func = AIFunctionFactory.Create(() => "test", "TestFunc");

        // Act
        var beforeContext = CreateBeforeFunctionContext(function: func);
        await middleware.BeforeFunctionAsync(beforeContext, CancellationToken.None);
        var afterContext = CreateAfterFunctionContext(function: func, result: "Done");
        await middleware.AfterFunctionAsync(afterContext, CancellationToken.None);

        // Assert - No timing should be logged
        Assert.DoesNotContain(logOutput, s => s.Contains("ms"));
    }

    //
    // TOOLKIT EXPANSION TESTS
    //

    [Fact]
    public async Task BeforeFunction_ContainerCollapse_LogsToolkitExpand()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogFunction = false,
            LogToolkitExpansion = true
        });

        var containerFunc = HPDAIFunctionFactory.Create(
            (AIFunctionArguments _, CancellationToken __) => Task.FromResult<object?>("expanded"),
            new HPDAIFunctionFactoryOptions
            {
                Name = "CodingToolkit",
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["IsCollapse"] = true,
                    ["ToolkitName"] = "CodingToolkit",
                    ["FunctionNames"] = new List<string> { "ReadFile", "WriteFile" }
                }
            });

        var context = CreateBeforeFunctionContext(function: containerFunc);

        // Act
        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        // Assert
        Assert.NotEmpty(logOutput);
        Assert.Contains(logOutput, s => s.Contains("[TOOLKIT EXPAND]"));
        Assert.Contains(logOutput, s => s.Contains("CodingToolkit"));
    }

    [Fact]
    public async Task BeforeFunction_ContainerCollapse_WhenExpansionDisabled_DoesNotLog()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogFunction = false,
            LogToolkitExpansion = false
        });

        var containerFunc = HPDAIFunctionFactory.Create(
            (AIFunctionArguments _, CancellationToken __) => Task.FromResult<object?>("expanded"),
            new HPDAIFunctionFactoryOptions
            {
                Name = "CodingToolkit",
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["IsCollapse"] = true,
                    ["ToolkitName"] = "CodingToolkit"
                }
            });

        var context = CreateBeforeFunctionContext(function: containerFunc);

        // Act
        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        // Assert
        Assert.Empty(logOutput);
    }

    [Fact]
    public async Task AfterFunction_ContainerCollapse_LogsToolkitCollapse()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogFunction = false,
            LogToolkitExpansion = true
        });

        var containerFunc = HPDAIFunctionFactory.Create(
            (AIFunctionArguments _, CancellationToken __) => Task.FromResult<object?>("expanded"),
            new HPDAIFunctionFactoryOptions
            {
                Name = "CodingToolkit",
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["IsCollapse"] = true,
                    ["ToolkitName"] = "CodingToolkit"
                }
            });

        var context = CreateAfterFunctionContext(function: containerFunc, result: "ok");

        // Act
        await middleware.AfterFunctionAsync(context, CancellationToken.None);

        // Assert
        Assert.NotEmpty(logOutput);
        Assert.Contains(logOutput, s => s.Contains("[TOOLKIT COLLAPSE]"));
        Assert.Contains(logOutput, s => s.Contains("CodingToolkit"));
    }

    [Fact]
    public async Task AfterFunction_ContainerCollapse_WithScopedMiddleware_ListsMiddlewareNames()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogFunction = false,
            LogToolkitExpansion = true
        });

        var containerFunc = HPDAIFunctionFactory.Create(
            (AIFunctionArguments _, CancellationToken __) => Task.FromResult<object?>("expanded"),
            new HPDAIFunctionFactoryOptions
            {
                Name = "CodingToolkit",
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["IsCollapse"] = true,
                    ["ToolkitName"] = "CodingToolkit"
                }
            });

        // Seed ContainerMiddlewareState with a toolkit pipeline containing a scoped middleware
        var scopedMiddleware = new TestScopedMiddleware();
        var pipeline = new AgentMiddlewarePipeline([scopedMiddleware]);
        var containerState = new ContainerMiddlewareState()
            .WithToolkitPipeline("CodingToolkit", pipeline);

        var agentContext = CreateAgentContextWithState(containerState);
        var context = agentContext.AsAfterFunction(
            function: containerFunc,
            callId: "call-123",
            result: "ok",
            exception: null,
            runConfig: new AgentRunConfig());

        // Act
        await middleware.AfterFunctionAsync(context, CancellationToken.None);

        // Assert
        Assert.NotEmpty(logOutput);
        Assert.Contains(logOutput, s => s.Contains("[TOOLKIT COLLAPSE]"));
        Assert.Contains(logOutput, s => s.Contains("TestScopedMiddleware"));
    }

    [Fact]
    public async Task AfterFunction_ContainerCollapse_Error_LogsExpandFailed()
    {
        // Arrange
        var logOutput = new List<string>();
        var loggerFactory = CreateTestLoggerFactory(logOutput);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogToolkitExpansion = true
        });

        var containerFunc = HPDAIFunctionFactory.Create(
            (AIFunctionArguments _, CancellationToken __) => Task.FromResult<object?>("expanded"),
            new HPDAIFunctionFactoryOptions
            {
                Name = "CodingToolkit",
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["IsCollapse"] = true,
                    ["ToolkitName"] = "CodingToolkit"
                }
            });

        var context = CreateAfterFunctionContext(
            function: containerFunc,
            exception: new InvalidOperationException("expansion blew up"));

        // Act
        await middleware.AfterFunctionAsync(context, CancellationToken.None);

        // Assert
        Assert.Contains(logOutput, s => s.Contains("[TOOLKIT EXPAND FAILED]"));
        Assert.Contains(logOutput, s => s.Contains("expansion blew up"));
    }

    [Fact]
    public async Task DefaultOptions_LogToolkitExpansion_IsTrue()
    {
        var options = LoggingMiddlewareOptions.Default;
        Assert.True(options.LogToolkitExpansion);
    }

    //
    // NO LOGGER TESTS
    //

    [Fact]
    public async Task WithoutLogger_DoesNotThrow()
    {
        // Arrange - no logger factory
        var middleware = new LoggingMiddleware(null);

        var func = AIFunctionFactory.Create(() => "test", "TestFunc");

        // Act & Assert - should not throw
        var beforeContext = CreateBeforeFunctionContext(function: func);
        await middleware.BeforeFunctionAsync(beforeContext, CancellationToken.None);
        var afterContext = CreateAfterFunctionContext(function: func);
        await middleware.AfterFunctionAsync(afterContext, CancellationToken.None);
    }

    //
    // HELPER METHODS
    //

    private static AgentContext CreateAgentContext()
    {
        var state = AgentLoopState.InitialSafe(
            messages: Array.Empty<ChatMessage>(),
            runId: "test-run",
            conversationId: "test-conversation",
            agentName: "TestAgent");

        return new AgentContext(
            "TestAgent",
            "test-conversation",
            state,
            new HPD.Events.Core.EventCoordinator(),
            new global::HPD.Agent.Session("test-session"),
            new global::HPD.Agent.Branch("test-session"),
            CancellationToken.None);
    }

    private static AgentContext CreateAgentContextWithState(ContainerMiddlewareState containerState)
    {
        var stateKey = typeof(ContainerMiddlewareState).FullName!;
        var baseState = AgentLoopState.InitialSafe(
            messages: Array.Empty<ChatMessage>(),
            runId: "test-run",
            conversationId: "test-conversation",
            agentName: "TestAgent");

        var seededState = baseState with
        {
            MiddlewareState = baseState.MiddlewareState.SetState(stateKey, containerState)
        };

        return new AgentContext(
            "TestAgent",
            "test-conversation",
            seededState,
            new HPD.Events.Core.EventCoordinator(),
            new global::HPD.Agent.Session("test-session"),
            new global::HPD.Agent.Branch("test-session"),
            CancellationToken.None);
    }

    private class TestScopedMiddleware : IAgentMiddleware { }

    private static BeforeMessageTurnContext CreateBeforeMessageTurnContext()
    {
        var agentContext = CreateAgentContext();
        return agentContext.AsBeforeMessageTurn(
            userMessage: new ChatMessage(ChatRole.User, "Test message"),
            conversationHistory: new List<ChatMessage>(),
            runConfig: new AgentRunConfig());
    }

    private static AfterMessageTurnContext CreateAfterMessageTurnContext()
    {
        var agentContext = CreateAgentContext();
        return agentContext.AsAfterMessageTurn(
            finalResponse: new ChatResponse(new ChatMessage(ChatRole.Assistant, "Test response")),
            turnHistory: new List<ChatMessage>(),
            runConfig: new AgentRunConfig());
    }

    private static BeforeIterationContext CreateBeforeIterationContext(int iteration = 0)
    {
        var agentContext = CreateAgentContext();
        return agentContext.AsBeforeIteration(
            iteration: iteration,
            messages: new List<ChatMessage>(),
            options: new ChatOptions(),
            runConfig: new AgentRunConfig());
    }

    private static AfterIterationContext CreateAfterIterationContext(int iteration = 0, List<FunctionResultContent>? toolResults = null)
    {
        var agentContext = CreateAgentContext();
        return agentContext.AsAfterIteration(
            iteration: iteration,
            toolResults: toolResults ?? new List<FunctionResultContent>(),
            runConfig: new AgentRunConfig());
    }

    private static BeforeFunctionContext CreateBeforeFunctionContext(
        AIFunction? function = null,
        string callId = "call-123",
        IReadOnlyDictionary<string, object?>? arguments = null)
    {
        var agentContext = CreateAgentContext();
        function ??= AIFunctionFactory.Create(() => "test", "TestFunc");
        arguments ??= new Dictionary<string, object?>();

        return agentContext.AsBeforeFunction(
            function: function,
            callId: callId,
            arguments: arguments,
            runConfig: new AgentRunConfig());
    }

    private static AfterFunctionContext CreateAfterFunctionContext(
        AIFunction? function = null,
        string callId = "call-123",
        object? result = null,
        Exception? exception = null)
    {
        var agentContext = CreateAgentContext();
        function ??= AIFunctionFactory.Create(() => "test", "TestFunc");

        return agentContext.AsAfterFunction(
            function: function,
            callId: callId,
            result: result,
            exception: exception,
            runConfig: new AgentRunConfig());
    }

    private static ILoggerFactory CreateTestLoggerFactory(List<string> output)
    {
        return new TestLoggerFactory(output);
    }

    /// <summary>
    /// Simple logger factory for capturing log output in tests.
    /// </summary>
    private class TestLoggerFactory : ILoggerFactory
    {
        private readonly List<string> _output;

        public TestLoggerFactory(List<string> output)
        {
            _output = output;
        }

        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }

        public ILogger CreateLogger(string categoryName)
        {
            return new TestLogger(_output);
        }
    }

    private class TestLogger : ILogger
    {
        private readonly List<string> _output;

        public TestLogger(List<string> output)
        {
            _output = output;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _output.Add(formatter(state, exception));
        }
    }
}
