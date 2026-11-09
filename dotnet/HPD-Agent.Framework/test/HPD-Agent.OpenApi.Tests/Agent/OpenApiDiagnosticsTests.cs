using System.Net;
using HPD.Agent.OpenApi;
using HPD.OpenApi.Core;
using HPD.OpenApi.Core.Model;
using Microsoft.Extensions.Logging;

namespace HPD.Agent.OpenApi.Tests.Agent;

public sealed class OpenApiDiagnosticsTests
{
    [Fact]
    public async Task EmptyFilteredCatalog_FailsWithActionableDiagnosticAndLogsContext()
    {
        var config = new OpenApiConfig
        {
            SpecPath = Path.Combine(AppContext.BaseDirectory, "TestSpecs", "weather.json"),
            OperationsToExclude = ["getForecast", "getAlerts"]
        };
        var logger = new CaptureLogger();
        var loader = new OpenApiLoader();

        var exception = await Assert.ThrowsAsync<AgentRunConfigurationException>(() =>
            loader.LoadAllAsync(
                [new OpenApiSourceRegistration("weather", null, false, config)],
                logger,
                CancellationToken.None));

        Assert.Equal("OpenApiSourceProducedNoOperations", exception.Code);
        Assert.Contains("weather", exception.Message, StringComparison.Ordinal);
        Assert.Contains("getForecast", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                entry.Message.Contains("weather", StringComparison.Ordinal) &&
                entry.Message.Contains("getForecast", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmptyFilteredCatalog_CanBeAllowedExplicitly()
    {
        var config = new OpenApiConfig
        {
            SpecPath = Path.Combine(AppContext.BaseDirectory, "TestSpecs", "weather.json"),
            OperationsToExclude = ["getForecast", "getAlerts"],
            AllowEmptyCatalog = true
        };
        var logger = new CaptureLogger();

        var result = await new OpenApiLoader().LoadAllAsync(
            [new OpenApiSourceRegistration("weather", null, false, config)],
            logger,
            CancellationToken.None);

        Assert.Empty(result.Functions);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void MalformedOperation_LogsSourceOperationMethodAndPath()
    {
        var operation = new RestApiOperation
        {
            Id = "brokenOperation",
            Path = "/broken",
            Method = HttpMethod.Get,
            ServerUrl = "https://api.example.com",
            Parameters =
            [
                new RestApiParameter
                {
                    Name = "value",
                    Type = "string",
                    Location = RestApiParameterLocation.Query
                },
                new RestApiParameter
                {
                    Name = "value",
                    Type = "string",
                    Location = RestApiParameterLocation.Query
                }
            ]
        };
        var logger = new CaptureLogger();

        var functions = OpenApiFunctionFactory.CreateFunctions(
            new ParsedOpenApiSpec { Operations = [operation] },
            new OpenApiConfig(),
            new OpenApiOperationRunner(
                new HttpClient(new EmptyResponseHandler())),
            namePrefix: "example",
            logger: logger);

        Assert.Empty(functions);
        var warning = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("example", warning.Message, StringComparison.Ordinal);
        Assert.Contains("brokenOperation", warning.Message, StringComparison.Ordinal);
        Assert.Contains("GET", warning.Message, StringComparison.Ordinal);
        Assert.Contains("/broken", warning.Message, StringComparison.Ordinal);
    }

    private sealed class EmptyResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private sealed class CaptureLogger : ILogger
    {
        internal List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
