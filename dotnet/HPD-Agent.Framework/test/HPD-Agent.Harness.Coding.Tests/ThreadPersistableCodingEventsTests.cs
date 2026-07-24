using HPD.Agent;
using HPD.Agent.Serialization;
using HPD.Agent.ToolHarness.Coding.Debugging;
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
            LanguageServerStatus(),
            new DebugChildSessionStartedEvent
            {
                DebugTreeId = "tree-1", DebugSessionId = "child-1", AdapterId = "fixture",
                ParentDebugSessionId = "root-1", AdapterStartMethod = DebugAdapterStartMethod.Launch, OutputPresentation = "separate"
            },
            new DebugRunInTerminalRequestEvent
            {
                DebugRequestId = "request-1", DebugTreeId = "tree-1", DebugSessionId = "child-1",
                WorkingDirectory = "/workspace", Arguments = ["tool", "a b"],
                EnvironmentDelta = new Dictionary<string, string?> { ["DELETE"] = null }
            },
            new DebugRunInTerminalResponseEvent
            {
                DebugRequestId = "request-1", ProcessId = 123, ShellProcessId = 124
            },
            new DebugBreakpointChangedEvent
            {
                DebugTreeId = "tree-1", DebugSessionId = "child-1", AdapterId = "fixture",
                Reason = "changed", BreakpointId = 7, Verified = true,
                DisplayPath = "a.cs", Line = 12
            }
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
    public void Phase5_debugger_events_roundtrip_through_canonical_codec()
    {
        DebugLifecycleEvent[] events =
        [
            new DebugSessionStoppedEvent { DebugTreeId = "t", DebugSessionId = "s", AdapterId = "a", Reason = "breakpoint" },
            new DebugSessionContinuedEvent { DebugTreeId = "t", DebugSessionId = "s", AdapterId = "a", AdapterThreadId = 1 },
            new DebugSessionFailedEvent { DebugTreeId = "t", DebugSessionId = "s", AdapterId = "a", SafeReasonCode = "PROTOCOL_FAULT" },
            new DebugProcessChangedEvent { DebugTreeId = "t", DebugSessionId = "s", AdapterId = "a", Name = "p" },
            new DebugThreadChangedEvent { DebugTreeId = "t", DebugSessionId = "s", AdapterId = "a", Reason = "started", AdapterThreadId = 1 },
            new DebugModuleChangedEvent { DebugTreeId = "t", DebugSessionId = "s", AdapterId = "a", Reason = "new", OpaqueModuleId = "m", Name = "module" },
            new DebugLoadedSourceChangedEvent { DebugTreeId = "t", DebugSessionId = "s", AdapterId = "a", Reason = "new", Name = "source" },
            new DebugCapabilitiesChangedEvent { DebugTreeId = "t", DebugSessionId = "s", AdapterId = "a", Enabled = ["readMemory"], Disabled = [] },
            new DebugStateInvalidatedEvent { DebugTreeId = "t", DebugSessionId = "s", AdapterId = "a", Areas = ["variables"] },
            new DebugMemoryChangedEvent { DebugTreeId = "t", DebugSessionId = "s", AdapterId = "a", MemoryReferenceToken = "token", Offset = 0, Count = 4 },
            new DebugOutputAvailableEvent { DebugTreeId = "t", DebugSessionId = "s", AdapterId = "a", FirstSequence = 1, LastSequence = 2, Category = "Console", InlineText = "text" },
            new DebugProgressStartedEvent { DebugTreeId = "t", DebugSessionId = "s", AdapterId = "a", ProgressId = "p", Title = "work" },
            new DebugProgressUpdatedEvent { DebugTreeId = "t", DebugSessionId = "s", AdapterId = "a", ProgressId = "p", Percentage = 50 },
            new DebugProgressCompletedEvent { DebugTreeId = "t", DebugSessionId = "s", AdapterId = "a", ProgressId = "p" },
            new DebugSessionSummaryEvent { DebugTreeId = "t", DebugSessionId = "s", AdapterId = "a", FinalStatus = "Terminated", DurationMilliseconds = 1, ChildSessionCount = 0, RetainedOutputBytes = 0, DroppedOutputRecords = 0, DroppedOutputBytes = 0, ProjectionFailures = 0 }
        ];

        foreach (var proposed in events)
        {
            var json = AgentEventSerializer.ToJson(ThreadEventValidation.PrepareForAppend("session", "thread", proposed));
            AgentEventSerializer.FromJson(json).Should().BeOfType(proposed.GetType());
        }
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
