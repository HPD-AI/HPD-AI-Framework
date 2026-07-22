using HPD.Agent;
using HPD.Agent.Serialization;
using HPDOS.ToolHarnesses.Middleware;
using System.Text.Json;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class CanonicalCodingEventCodecTests
{
    [Fact]
    public void EveryPublishedCodingEvent_RoundTripsThroughTheCanonicalCodec()
    {
        AgentEvent[] events =
        [
            Started(),
            OutputChunk(),
            Progress(),
            Diagnostics(),
            LanguageServerStatus()
        ];

        foreach (var proposed in events)
        {
            var scoped = ThreadEventValidation.PrepareForAppend(
                "session-1",
                "thread-1",
                proposed);
            var json = AgentEventSerializer.ToJson(scoped);
            var decoded = AgentEventSerializer.FromJson(json).Should().BeAssignableTo<AgentEvent>().Subject;

            decoded.Should().BeOfType(proposed.GetType());
            decoded.EventId.Should().Be(scoped.EventId);
            decoded.SessionId.Should().Be("session-1");
            decoded.ThreadId.Should().Be("thread-1");
        }
    }

    [Fact]
    public void ExecuteCommandExit_RoundTripsNullableOutputHandles()
    {
        var proposed = new ExecuteCommandProcessExitedEvent
        {
            ToolCallId = "call-1",
            FunctionName = "ExecuteCommand",
            CommandId = "cmd-1",
            Command = "dotnet test",
            BaseCommand = "dotnet",
            Category = ExecuteCommandCategory.Test,
            WorkingDirectory = "/repo",
            CompletionKind = ExecuteCommandCompletionKind.TimedOut,
            DurationMilliseconds = 120_000,
            StdoutBytes = 0,
            StderrBytes = 0,
            CombinedOutputBytes = 0,
            StdoutBytesDiscarded = 0,
            StderrBytesDiscarded = 0,
            CombinedBytesDiscarded = 0,
            OutputTruncated = false,
            OutputDrainTimedOut = false,
            OutputEventsSuppressed = false,
            StdoutContentId = "stdout-content",
            CombinedOutputLocalPath = "/tmp/combined.log"
        };

        var json = AgentEventSerializer.ToJson(proposed);
        var decoded = AgentEventSerializer.FromJson(json)
            .Should().BeOfType<ExecuteCommandProcessExitedEvent>().Subject;

        decoded.ExitCode.Should().BeNull();
        decoded.StdoutArtifactPath.Should().BeNull();
        decoded.StderrArtifactPath.Should().BeNull();
        decoded.CombinedOutputArtifactPath.Should().BeNull();
        decoded.StdoutContentId.Should().Be("stdout-content");
        decoded.CombinedOutputLocalPath.Should().Be("/tmp/combined.log");
    }

    [Fact]
    public void LanguageServerDocumentVersion_DoesNotCollideWithEnvelopeVersion()
    {
        CodingHarnessEventSerialization.RegisterEvents();
        var proposed = new LanguageServerDocumentOpenedEvent
        {
            Path = "/repo/A.cs",
            Uri = "file:///repo/A.cs",
            LanguageId = "csharp",
            DocumentVersion = 42
        };

        var json = AgentEventSerializer.ToJson(proposed);
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("version").GetString().Should().Be("1.0");
        document.RootElement.GetProperty("documentVersion").GetInt32().Should().Be(42);

        var decoded = AgentEventSerializer.FromJson(json)
            .Should().BeOfType<LanguageServerDocumentOpenedEvent>().Subject;
        decoded.DocumentVersion.Should().Be(42);
    }

    private static ExecuteCommandProcessStartedEvent Started() => new()
    {
        ToolCallId = "call-1",
        FunctionName = "ExecuteCommand",
        CommandId = "cmd-1",
        Command = "dotnet test",
        BaseCommand = "dotnet",
        Category = ExecuteCommandCategory.Test,
        WorkingDirectory = "/repo",
        Shell = "/bin/zsh",
        StartedAt = DateTimeOffset.UnixEpoch,
        Background = false,
        AutoBackgroundEligible = true,
        ProcessId = 123,
        TimeoutMilliseconds = 120_000,
        EventFlowId = "cmd-1"
    };

    private static ExecuteCommandOutputChunkEvent OutputChunk() => new()
    {
        ToolCallId = "call-1",
        FunctionName = "ExecuteCommand",
        CommandId = "cmd-1",
        Command = "dotnet test",
        BaseCommand = "dotnet",
        Category = ExecuteCommandCategory.Test,
        WorkingDirectory = "/repo",
        Stream = ExecuteCommandStreamKind.Stdout,
        Text = "running",
        ObservedAt = DateTimeOffset.UnixEpoch,
        StreamBytesObserved = 7,
        CombinedBytesObserved = 7
    };

    private static ExecuteCommandProgressEvent Progress() => new()
    {
        ToolCallId = "call-1",
        FunctionName = "ExecuteCommand",
        CommandId = "cmd-1",
        Command = "dotnet test",
        BaseCommand = "dotnet",
        Category = ExecuteCommandCategory.Test,
        WorkingDirectory = "/repo",
        ElapsedMilliseconds = 100,
        StdoutBytes = 7,
        StderrBytes = 0,
        CombinedOutputBytes = 7,
        CombinedBytesDiscarded = 0,
        OutputObserved = true,
        OutputEventsSuppressed = false
    };

    private static LanguageServerDiagnosticsReceivedEvent Diagnostics() => new()
    {
        Path = "/repo/A.cs",
        Uri = "file:///repo/A.cs",
        ErrorCount = 1,
        DiagnosticSetCount = 1,
        Diagnostics =
        [
            new LanguageServerDiagnosticSummary
            {
                Path = "/repo/A.cs",
                ServerId = "csharp",
                Source = LanguageServerDiagnosticSource.Publish,
                Severity = LanguageServerDiagnosticSeverity.Error,
                Line = 10,
                Character = 4,
                Code = "CS1002",
                Message = "Missing semicolon"
            }
        ]
    };

    private static LanguageServerStatusSnapshotEvent LanguageServerStatus() => new()
    {
        Servers =
        [
            new LanguageServerStatusSnapshot
            {
                ServerId = "csharp",
                Root = "/repo",
                Status = LanguageServerStatusKind.Running
            }
        ]
    };
}
