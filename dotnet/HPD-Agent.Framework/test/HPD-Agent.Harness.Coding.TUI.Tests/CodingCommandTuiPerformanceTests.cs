using System.Diagnostics;
using HPD.Agent.TUI;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Observability;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.TUI.Views;
using HPD.Agent.ToolHarness.Coding.TUI;
using HPD.Agent.ToolHarness.Coding.TUI.Commands;
using HPD.Agent.ToolHarness.Coding.TUI.Observability;
using HPD.Events;
using HPD.TUI.Observability;
using HPD.TUI.Rendering;

namespace HPD.Agent.ToolHarness.Coding.TUI.Tests;

public sealed class CodingCommandTuiPerformanceTests
{
    private const int MaxCommandCellOutputLines = 32;

    [Fact]
    public async Task CommandOutput_ThousandChunks_StaysOneTranscriptRow()
    {
        var state = CreateState();

        await state.ApplyEventAsync(Started(command: "dotnet test"));
        for (var i = 0; i < 1_000; i++)
        {
            await state.ApplyEventAsync(Output($"line {i:D4}\n"));
        }

        var rows = ReadRows(state.Shell.Transcript);
        rows.Should().ContainSingle();
        rows[0].EntryKey.Should().Be("coding.command:cmd-1");
        rows[0].Cell.Should().BeOfType<CodingCommandCell>();
    }

    [Fact]
    public async Task CommandOutput_ThousandChunks_CellOutputIsCapped()
    {
        var state = CreateState();

        await state.ApplyEventAsync(Started(command: "dotnet test"));
        for (var i = 0; i < 1_000; i++)
        {
            await state.ApplyEventAsync(Output($"line {i:D4}\n"));
        }

        var cell = ReadCommandCell(state);
        cell.Output.Count.Should().BeLessThanOrEqualTo(MaxCommandCellOutputLines);
        cell.OutputWindow.OmittedLineCount.Should().BeGreaterThan(0);
        cell.Output.Should().Contain(static line => line.Text.Contains("line 0999", StringComparison.Ordinal));
        cell.Output.Should().NotContain(static line => line.Text.Contains("line 0500", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CommandOutput_ThousandChunks_UpdateTimeIsReasonable()
    {
        var state = CreateState();
        var stopwatch = Stopwatch.StartNew();

        await state.ApplyEventAsync(Started(command: "dotnet test"));
        for (var i = 0; i < 1_000; i++)
        {
            await state.ApplyEventAsync(Output($"line {i:D4} {new string('x', 64)}\n"));
        }

        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(5),
            "streaming one command should stay bounded enough for interactive TUI use");
        ReadCommandCell(state).Output.Count.Should().BeLessThanOrEqualTo(MaxCommandCellOutputLines);
    }

    [Fact]
    public async Task CommandProgress_InvisibleProgress_DoesNotUpdateTranscriptVersion()
    {
        var state = CreateState();

        await state.ApplyEventAsync(Started(command: "dotnet test"));
        var versionAfterStart = state.Shell.Transcript.Version;

        for (var i = 1; i <= 1_000; i++)
        {
            await state.ApplyEventAsync(Progress(
                elapsedMilliseconds: i * 10,
                stdoutBytes: i,
                stderrBytes: i % 3));
        }

        state.Shell.Transcript.Version.Should().Be(versionAfterStart);
    }

    [Fact]
    public async Task CommandOutput_NarrowWidth_RerenderIsBounded()
    {
        var state = CreateState();

        await state.ApplyEventAsync(Started(command: "dotnet test"));
        for (var i = 0; i < 200; i++)
        {
            await state.ApplyEventAsync(Output(
                $"alpha-beta-gamma-delta-epsilon-zeta-eta-theta-iota-kappa-lambda {i:D4}\n"));
        }

        var first = RenderTranscript(state, width: 24, height: 14);
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 25; i++)
        {
            RenderTranscript(state, width: 24, height: 14).Should().Be(first);
        }

        stopwatch.Stop();
        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(5),
            "re-rendering the same narrow command cell should reuse bounded render-source data");
        first.Should().Contain("... +");
    }

    [Fact]
    public async Task CommandOutput_LongLines_RemainClippedAndBounded()
    {
        var state = CreateState();
        var longLine = string.Concat(
            new string('a', 4_200),
            "middle-secret",
            new string('z', 4_200),
            "\n");

        await state.ApplyEventAsync(Started(command: "dotnet test"));
        for (var i = 0; i < 25; i++)
        {
            await state.ApplyEventAsync(Output(longLine));
        }

        var cell = ReadCommandCell(state);
        cell.Output.Count.Should().BeLessThanOrEqualTo(MaxCommandCellOutputLines);
        cell.Output.Should().Contain(static line => line.Text.Contains("[line clipped]", StringComparison.Ordinal));
        cell.Output.Should().NotContain(static line => line.Text.Contains("middle-secret", StringComparison.Ordinal));

        var rendered = RenderTranscript(state);
        rendered.Should().Contain("[line clipped]");
        rendered.Should().NotContain("middle-secret");
    }

    [Fact]
    public async Task CommandOutput_StderrAndStdout_MixedStreamsRemainBounded()
    {
        var state = CreateState();

        await state.ApplyEventAsync(Started(command: "dotnet test"));
        for (var i = 0; i < 500; i++)
        {
            await state.ApplyEventAsync(Output($"stdout {i:D4}\n"));
            await state.ApplyEventAsync(Output($"stderr {i:D4}\n", ExecuteCommandStreamKind.Stderr));
        }

        var rows = ReadRows(state.Shell.Transcript);
        rows.Should().ContainSingle();

        var cell = ReadCommandCell(state);
        cell.Output.Count.Should().BeLessThanOrEqualTo(MaxCommandCellOutputLines);
        cell.OutputWindow.OmittedLineCount.Should().BeGreaterThan(0);
        cell.Output.Should().Contain(static line => line.Stream == CodingCommandOutputStream.Stdout);
        cell.Output.Should().Contain(static line => line.Stream == CodingCommandOutputStream.Stderr);
        cell.Output.Should().Contain(static line => line.Text.Contains("stdout 0499", StringComparison.Ordinal));
        cell.Output.Should().Contain(static line => line.Text.Contains("stderr 0499", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CommandUpdates_PublishPerformanceDiagnosticsWhenSinkConfigured()
    {
        var state = CreateState();
        var sink = new RecordingSink();
        AgentTuiPerformanceDiagnostics.SetSink(state.State, sink);

        await state.ApplyEventAsync(Started(command: "dotnet test"));
        await state.ApplyEventAsync(Progress(
            elapsedMilliseconds: 10,
            stdoutBytes: 0,
            stderrBytes: 0));

        var events = sink.Events.OfType<CodingCommandTranscriptUpdated>().ToArray();
        events.Should().HaveCount(2);
        events[0].Applied.Should().BeTrue();
        events[0].OutputLinesInCell.Should().Be(0);
        events[0].ShouldPersistToThread().Should().BeFalse();
        events[0].Kind.Should().Be(EventKind.Diagnostic);
        events[0].Channel.Should().Be(EventChannel.Streaming);
        events[0].SessionId.Should().Be("session");
        events[1].Applied.Should().BeFalse();
    }

    private static AgentTuiSessionState CreateState()
        => new(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            new HpdAgentTuiBuilder()
                .AddCodingHarnessTui()
                .Build());

    private static CodingCommandCell ReadCommandCell(AgentTuiSessionState state)
    {
        var rows = ReadRows(state.Shell.Transcript);
        rows.Should().ContainSingle();
        return rows[0].Cell.Should().BeOfType<CodingCommandCell>().Subject;
    }

    private static ExecuteCommandProcessStartedEvent Started(string command)
        => new()
        {
            EventFlowId = "cmd-1",
            ToolCallId = "call-1",
            FunctionName = "ExecuteCommand",
            CommandId = "cmd-1",
            Command = command,
            BaseCommand = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? command,
            Category = ExecuteCommandCategory.Test,
            WorkingDirectory = "/repo",
            Shell = "zsh",
            StartedAt = DateTimeOffset.Parse("2026-06-06T12:00:00Z"),
            Background = false,
            AutoBackgroundEligible = true,
            ProcessId = 123,
            TimeoutMilliseconds = 120_000
        };

    private static ExecuteCommandOutputChunkEvent Output(
        string text,
        ExecuteCommandStreamKind stream = ExecuteCommandStreamKind.Stdout)
        => new()
        {
            EventFlowId = "cmd-1",
            ToolCallId = "call-1",
            FunctionName = "ExecuteCommand",
            CommandId = "cmd-1",
            Command = "dotnet test",
            BaseCommand = "dotnet",
            Category = ExecuteCommandCategory.Test,
            WorkingDirectory = "/repo",
            Stream = stream,
            Text = text,
            ObservedAt = DateTimeOffset.Parse("2026-06-06T12:00:01Z"),
            StreamBytesObserved = text.Length,
            CombinedBytesObserved = text.Length,
            Truncated = false,
            Suppressed = false,
            Binary = false
        };

    private static ExecuteCommandProgressEvent Progress(
        long elapsedMilliseconds,
        long stdoutBytes,
        long stderrBytes)
        => new()
        {
            EventFlowId = "cmd-1",
            ToolCallId = "call-1",
            FunctionName = "ExecuteCommand",
            CommandId = "cmd-1",
            Command = "dotnet test",
            BaseCommand = "dotnet",
            Category = ExecuteCommandCategory.Test,
            WorkingDirectory = "/repo",
            ElapsedMilliseconds = elapsedMilliseconds,
            StdoutBytes = stdoutBytes,
            StderrBytes = stderrBytes,
            CombinedOutputBytes = stdoutBytes + stderrBytes,
            CombinedBytesDiscarded = 0,
            OutputObserved = stdoutBytes + stderrBytes > 0,
            OutputEventsSuppressed = false
        };

    private static List<TranscriptEntry> ReadRows(TranscriptModel model)
    {
        var rows = model.Snapshot().Entries.ToList();
        return rows;
    }

    private static string RenderTranscript(AgentTuiSessionState state, int width = 100, int height = 14)
        => TuiCapture.RenderToString(
            new TranscriptHistoryView(state.Shell.Transcript, DefaultTranscriptRenderers(), height: 12),
            width: width,
            height: height,
            trimTrailingBlankLines: true);

    private static AgentTuiTranscriptRendererRegistry DefaultTranscriptRenderers()
        => new HpdAgentTuiBuilder()
            .AddDefaultTranscriptRenderers()
            .AddCodingHarnessTui()
            .Build()
            .TranscriptRenderers;

    private sealed class RecordingSink : IHpdTuiPerformanceEventSink
    {
        public List<Event> Events { get; } = [];

        public void Publish(Event evt)
        {
            Events.Add(evt);
        }
    }
}
