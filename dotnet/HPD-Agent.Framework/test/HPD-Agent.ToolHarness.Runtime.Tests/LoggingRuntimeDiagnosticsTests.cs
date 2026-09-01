using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace HPD.Agent.Tests.Middleware;

public sealed class LoggingRuntimeDiagnosticsTests
{
    [Fact]
    public async Task HarnessLoggingOutsideExecution_ReportsNoScopedRuntime()
    {
        var messages = new List<string>();
        using var loggerFactory = new CaptureLoggerFactory(messages);
        var middleware = new LoggingMiddleware(loggerFactory, new LoggingMiddlewareOptions
        {
            LogFunction = false,
            LogToolHarnessExpansion = true,
            IncludeTiming = false
        });
        var function = HPDAIFunctionFactory.Create(
            (AIFunctionArguments _, FunctionExecutionContext __, CancellationToken ___) =>
                Task.FromResult<object?>("expanded"),
            new HPDAIFunctionFactoryOptions
            {
                Name = "OutsideExecutionHarness",
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["IsToolHarnessContainer"] = true,
                    ["ToolHarnessName"] = "OutsideExecutionHarness",
                    ["ToolHarnessIdentity"] = "tests:outside"
                }
            });
        var state = AgentLoopState.InitialSafe([], "run", "conversation", "agent");
        var context = new AgentContext(
            "agent",
            "conversation",
            state,
            new HPD.Events.Core.EventCoordinator(),
            new Session("session"),
            new Thread("session", "agent"),
            CancellationToken.None).AsAfterFunction(
                function, "call", "expanded", null, new AgentRunConfig());

        await middleware.AfterFunctionAsync(context, CancellationToken.None);

        var message = Assert.Single(messages);
        Assert.Contains("[HARNESS COLLAPSE] OutsideExecutionHarness", message);
        Assert.DoesNotContain("Scoped runtime:", message);
    }

    private sealed class CaptureLoggerFactory(List<string> messages) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider) { }
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(messages);
        public void Dispose() { }
    }

    private sealed class CaptureLogger(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => messages.Add(formatter(state, exception));
    }
}
