using System.Text.Json;
using HPD.Agent;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class BranchPersistableCodingEventsTests
{
    [Fact]
    public void CodingToolHarnessEvents_ProjectToBranchEvents()
    {
        var events = new AgentEvent[]
        {
            CreateExecuteCommandEvent(),
            CreateFileMutationEvent(),
            CreateLanguageServerEvent()
        };

        foreach (var evt in events)
        {
            evt.ShouldPersistToBranch().Should().BeTrue();

            var projected = BranchEventFactory.FromAgentEvent(
                "session-1",
                "branch-1",
                evt,
                messageTurnId: "turn-1",
                conversationId: "session-1",
                iteration: 1,
                inputMessageCount: 1,
                isResume: false,
                terminationReason: null,
                turnMessageCount: 1);

            projected.Should().NotBeNull();
            projected!.GetType().Should().Be(evt.GetType());
            projected.SessionId.Should().Be("session-1");
            projected.BranchId.Should().Be("branch-1");
            projected.EventId.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void BranchEventDocument_RoundTripsCodingToolHarnessEvents()
    {
        var document = new BranchEventDocument
        {
            SessionId = "session-1",
            BranchId = "branch-1",
            Events =
            [
                BranchEventFactory.FromAgentEvent("session-1", "branch-1", CreateExecuteCommandEvent(), "turn-1", "session-1", 1, 1, false, null, 1)!,
                BranchEventFactory.FromAgentEvent("session-1", "branch-1", CreateFileMutationEvent(), "turn-1", "session-1", 1, 1, false, null, 1)!,
                BranchEventFactory.FromAgentEvent("session-1", "branch-1", CreateLanguageServerEvent(), "turn-1", "session-1", 1, 1, false, null, 1)!
            ]
        };

        var json = JsonSerializer.Serialize(document, SessionJsonContext.Combined.Options);
        using var jsonDocument = JsonDocument.Parse(json);

        jsonDocument.RootElement
            .GetProperty("events")[0]
            .TryGetProperty("persistToBranch", out _)
            .Should().BeFalse();

        jsonDocument.RootElement
            .GetProperty("events")[0]
            .TryGetProperty("branchEventCategory", out _)
            .Should().BeFalse();

        var roundTrip = JsonSerializer.Deserialize<BranchEventDocument>(
            json,
            SessionJsonContext.Combined.Options);

        roundTrip.Should().NotBeNull();
        roundTrip!.Events.Should().HaveCount(3);
        roundTrip.Events[0].Should().BeOfType<ExecuteCommandProcessStartedEvent>();
        roundTrip.Events[1].Should().BeOfType<FileWriteAppliedEvent>();
        roundTrip.Events[2].Should().BeOfType<LanguageServerDiagnosticsReceivedEvent>();
    }

    [Fact]
    public void NoisyExecuteCommandEvents_DoNotPersistToBranch()
    {
        var events = new AgentEvent[]
        {
            CreateExecuteCommandOutputChunkEvent(),
            CreateExecuteCommandProgressEvent(),
            CreateExecuteCommandBackgroundListEvent()
        };

        foreach (var evt in events)
        {
            evt.ShouldPersistToBranch().Should().BeFalse();

            var projected = BranchEventFactory.FromAgentEvent(
                "session-1",
                "branch-1",
                evt,
                messageTurnId: "turn-1",
                conversationId: "session-1",
                iteration: 1,
                inputMessageCount: 1,
                isResume: false,
                terminationReason: null,
                turnMessageCount: 1);

            projected.Should().BeNull();
        }
    }

    private static ExecuteCommandProcessStartedEvent CreateExecuteCommandEvent() => new()
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

    private static ExecuteCommandOutputChunkEvent CreateExecuteCommandOutputChunkEvent() => new()
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

    private static ExecuteCommandProgressEvent CreateExecuteCommandProgressEvent() => new()
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

    private static ExecuteCommandBackgroundListEvent CreateExecuteCommandBackgroundListEvent() => new()
    {
        ToolCallId = "call-1",
        FunctionName = "ExecuteCommand",
        CommandId = "cmd-1",
        Command = "dotnet test",
        BaseCommand = "dotnet",
        Category = ExecuteCommandCategory.Test,
        WorkingDirectory = "/repo",
        Count = 1
    };

    private static FileWriteAppliedEvent CreateFileMutationEvent()
    {
        const string beforeText = "class A {}\n";
        const string afterText = "class A { void M() {} }\n";
        var beforeRange = new FileMutationRange(1, 1, 2, 1, 0, beforeText.Length);
        var afterRange = new FileMutationRange(1, 1, 2, 1, 0, afterText.Length);

        return new FileWriteAppliedEvent
        {
            ToolCallId = "call-2",
            FunctionName = "WriteFile",
            Path = "/repo/A.cs",
            DisplayPath = "A.cs",
            MutationKind = CodingFileMutationKind.Changed,
            Created = false,
            Changed = true,
            Before = new FileMutationSnapshot(
                beforeText,
                "sha256:before",
                beforeText.Length,
                1,
                "utf-8",
                HasBom: false,
                "lf",
                DateTimeOffset.UnixEpoch,
                TextOmitted: false,
                OmissionReason: null),
            After = new FileMutationSnapshot(
                afterText,
                "sha256:after",
                afterText.Length,
                1,
                "utf-8",
                HasBom: false,
                "lf",
                DateTimeOffset.UnixEpoch.AddSeconds(1),
                TextOmitted: false,
                OmissionReason: null),
            TextEdits =
            [
                new FileMutationTextEdit(
                    1,
                    beforeRange,
                    afterRange,
                    beforeText,
                    afterText,
                    TextOmitted: false,
                    OmissionReason: null)
            ],
            Hunks =
            [
                new FileMutationHunk(
                    1,
                    1,
                    1,
                    1,
                    ["-class A {}", "+class A { void M() {} }"])
            ],
            HunksTruncated = false,
            DiffStat = new FileMutationDiffStat(1, 1, afterText.Length - beforeText.Length, 0),
            Mode = FileWriteMode.Rewrite
        };
    }

    private static LanguageServerDiagnosticsReceivedEvent CreateLanguageServerEvent() => new()
    {
        Path = "/repo/A.cs",
        Uri = "file:///repo/A.cs",
        ErrorCount = 1,
        WarningCount = 2,
        DiagnosticSetCount = 3
    };
}
