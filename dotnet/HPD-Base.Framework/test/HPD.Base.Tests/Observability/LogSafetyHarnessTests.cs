using HPD.Base.Tests.Observability;
using Microsoft.Extensions.Logging;

namespace HPD.Base.Tests.Abstractions.Observability;

public sealed class LogSafetyHarnessTests
{
    [Fact]
    public void CollectorCapturesEveryLoggingSurfaceSeparately()
    {
        using var collector = new LogCollector();
        var logger = collector.CreateLogger<LogSafetyHarnessTests>();
        var exception = new InvalidOperationException("exception-marker");

        using (logger.BeginScope("scope-marker"))
        {
            logger.Log(
                LogLevel.Warning,
                new EventId(42, "UnsafeSample"),
                new[]
                {
                    new KeyValuePair<string, object?>("UnsafeValue", "state-marker"),
                    new KeyValuePair<string, object?>("{OriginalFormat}", "Template {UnsafeValue}")
                },
                exception,
                static (state, _) => $"rendered-marker:{state[0].Value}");
        }

        var record = Assert.Single(collector.Records);
        Assert.Equal(typeof(LogSafetyHarnessTests).FullName, record.Category);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(42, record.EventId.Id);
        Assert.Equal("UnsafeSample", record.EventId.Name);
        Assert.Equal("Template {UnsafeValue}", record.OriginalFormat);
        Assert.Equal("rendered-marker:state-marker", record.RenderedMessage);
        Assert.Equal("scope-marker", Assert.Single(record.Scopes));
        Assert.Same(exception, record.Exception);
    }

    [Theory]
    [InlineData("category")]
    [InlineData("event")]
    [InlineData("template")]
    [InlineData("rendered")]
    [InlineData("state-key")]
    [InlineData("state-value")]
    [InlineData("scope")]
    [InlineData("exception")]
    public void InspectorFailsClosedForEveryCapturedChannel(string channel)
    {
        const string marker = "forbidden-marker";
        var record = UnsafeRecord(channel, marker);

        var thrown = Assert.Throws<InvalidOperationException>(
            () => LogSafetyInspector.AssertSafe([record], marker));

        Assert.Contains("Forbidden marker", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectorRejectsComplexStateEvenWithoutKnownMarker()
    {
        var record = SafeRecord(
            [new KeyValuePair<string, object?>("UnsafeObject", new object())]);

        var thrown = Assert.Throws<InvalidOperationException>(
            () => LogSafetyInspector.AssertSafe([record], "unrelated-marker"));

        Assert.Contains("disallowed complex type", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DisabledLevelProducesNoCapturedRecord()
    {
        using var collector = new LogCollector(LogLevel.Warning);
        var logger = collector.CreateLogger<LogSafetyHarnessTests>();

        logger.Log(
            LogLevel.Debug,
            new EventId(43, "DisabledSample"),
            "state",
            null,
            static (state, _) => state);

        Assert.Empty(collector.Records);
    }

    private static CapturedLogRecord UnsafeRecord(string channel, string marker)
    {
        var category = channel == "category" ? marker : "Safe.Category";
        var eventName = channel == "event" ? marker : "SafeEvent";
        var template = channel == "template" ? marker : "Safe template.";
        var rendered = channel == "rendered" ? marker : "Safe rendered.";
        var stateKey = channel == "state-key" ? marker : "SafeKey";
        var stateValue = channel == "state-value" ? marker : "safe";
        var scopes = channel == "scope" ? new object?[] { marker } : [];
        var exception = channel == "exception" ? new InvalidOperationException(marker) : null;

        return new CapturedLogRecord(
            1,
            DateTimeOffset.UnixEpoch,
            category,
            LogLevel.Warning,
            new EventId(1, eventName),
            template,
            rendered,
            [
                new KeyValuePair<string, object?>(stateKey, stateValue),
                new KeyValuePair<string, object?>("{OriginalFormat}", template)
            ],
            scopes,
            exception);
    }

    private static CapturedLogRecord SafeRecord(
        IReadOnlyList<KeyValuePair<string, object?>> state) =>
        new(
            1,
            DateTimeOffset.UnixEpoch,
            "Safe.Category",
            LogLevel.Warning,
            new EventId(1, "SafeEvent"),
            "Safe template.",
            "Safe rendered.",
            state,
            [],
            null);
}
