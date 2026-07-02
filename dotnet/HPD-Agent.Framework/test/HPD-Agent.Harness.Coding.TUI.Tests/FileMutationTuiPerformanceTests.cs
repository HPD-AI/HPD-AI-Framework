using HPD.Agent.TUI;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.TUI.Views;
using HPD.Agent.ToolHarness.Coding.TUI;
using HPD.Agent.ToolHarness.Coding.TUI.FileMutations;
using HPD.TUI.Rendering;
using HPDOS.ToolHarnesses.Middleware;
using MiddlewareFileMutationHunk = HPDOS.ToolHarnesses.Middleware.FileMutationHunk;

namespace HPD.Agent.ToolHarness.Coding.TUI.Tests;

public sealed class FileMutationTuiPerformanceTests
{
    private const int MaxCellDiagnostics = 64;

    [Fact]
    public async Task FileMutation_LargeDiff_RendersAllEventHunks()
    {
        var state = CreateState();

        await state.ApplyEventAsync(LargeMutation(hunkCount: 20, linesPerHunk: 20));

        var rendered = RenderTranscript(state, width: 180, height: 440);
        rendered.Should().Contain("added 0017");
        rendered.Should().Contain("added 0399");
        rendered.Should().NotContain("diff truncated");
    }

    [Fact]
    public async Task FileMutation_LargeDiff_CellSnapshotKeepsEventHunks()
    {
        var state = CreateState();

        await state.ApplyEventAsync(LargeMutation(hunkCount: 20, linesPerHunk: 20));

        var cell = ReadSingleCell<FileMutationCell>(state);
        cell.Hunks.Sum(static hunk => hunk.Lines.Count).Should().Be(400);
        cell.HunksTruncated.Should().BeFalse();
    }

    [Fact]
    public async Task FileMutation_EventTruncated_RendersTruncationMarker()
    {
        var state = CreateState();

        await state.ApplyEventAsync(LargeMutation(hunkCount: 2, linesPerHunk: 2, hunksTruncated: true));

        var cell = ReadSingleCell<FileMutationCell>(state);
        cell.Hunks.Sum(static hunk => hunk.Lines.Count).Should().Be(4);
        cell.HunksTruncated.Should().BeTrue();

        var rendered = RenderTranscript(state, height: 20);
        rendered.Should().Contain("added 0003");
        rendered.Should().Contain("diff truncated");
    }

    [Fact]
    public async Task Diagnostics_ManyDiagnostics_RenderedLinesAreCapped()
    {
        var state = CreateState();

        await state.ApplyEventAsync(Diagnostics(count: 100));

        var cell = ReadSingleCell<CodingDiagnosticsCell>(state);
        cell.Diagnostics.Count.Should().BeLessThanOrEqualTo(MaxCellDiagnostics);
        cell.Truncated.Should().BeTrue();

        var rendered = RenderTranscript(state, height: 80);
        var lines = rendered.Split('\n', StringSplitOptions.None);
        lines.Length.Should().BeLessThan(14);
        rendered.Should().Contain("diagnostics omitted");
        rendered.Should().Contain("CS0000");
        rendered.Should().NotContain("CS0099");
    }

    [Fact]
    public async Task Diagnostics_AttachedToMutation_UpdatesOneTranscriptRow()
    {
        var state = CreateState();

        await state.ApplyEventAsync(LargeMutation(hunkCount: 2, linesPerHunk: 4));
        var versionAfterMutation = state.Shell.Transcript.Version;
        await state.ApplyEventAsync(Diagnostics(count: 100));

        var rows = ReadRows(state.Shell.Transcript);
        rows.Should().ContainSingle();
        state.Shell.Transcript.Version.Should().Be(versionAfterMutation + 1);

        var cell = rows[0].Cell.Should().BeOfType<FileMutationCell>().Subject;
        cell.Diagnostics.Count.Should().BeLessThanOrEqualTo(MaxCellDiagnostics);
        cell.DiagnosticsTruncated.Should().BeTrue();
    }

    private static AgentTuiSessionState CreateState()
        => new(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            new HpdAgentTuiBuilder()
                .AddCodingHarnessTui()
                .Build());

    private static FileEditAppliedEvent LargeMutation(
        int hunkCount,
        int linesPerHunk,
        bool hunksTruncated = false)
        => new()
        {
            EventId = "evt-large-edit",
            ToolCallId = "call-large-edit",
            FunctionName = "EditFile",
            Path = "/repo/src/Foo.cs",
            DisplayPath = "src/Foo.cs",
            MutationKind = CodingFileMutationKind.Changed,
            Created = false,
            Changed = true,
            Before = Snapshot("before"),
            After = Snapshot("after"),
            TextEdits = [],
            Hunks = Enumerable.Range(0, hunkCount)
                .Select(i => new MiddlewareFileMutationHunk(
                    OldStart: (i * linesPerHunk) + 1,
                    OldLines: linesPerHunk,
                    NewStart: (i * linesPerHunk) + 1,
                    NewLines: linesPerHunk,
                    Lines: Enumerable.Range(0, linesPerHunk)
                        .Select(j => $"+added {((i * linesPerHunk) + j):D4} {new string('x', 120)}")
                        .ToArray()))
                .ToArray(),
            HunksTruncated = hunksTruncated,
            DiffStat = new HPDOS.ToolHarnesses.Middleware.FileMutationDiffStat(
                AddedLines: hunkCount * linesPerHunk,
                RemovedLines: 0,
                AddedChars: hunkCount * linesPerHunk * 120,
                RemovedChars: 0),
            Notes = [],
            EditCount = hunkCount,
            ReplacementCount = hunkCount,
            Replacements = [],
            Normalizations = []
        };

    private static LanguageServerDiagnosticsReceivedEvent Diagnostics(int count)
        => new()
        {
            EventId = "evt-many-diagnostics",
            Path = "/repo/src/Foo.cs",
            Uri = "file:///repo/src/Foo.cs",
            ErrorCount = count,
            WarningCount = 0,
            InformationCount = 0,
            HintCount = 0,
            DiagnosticSetCount = count,
            Diagnostics = Enumerable.Range(0, count)
                .Select(i => new LanguageServerDiagnosticSummary
                {
                    Path = "src/Foo.cs",
                    ServerId = "csharp",
                    Source = LanguageServerDiagnosticSource.Publish,
                    Severity = LanguageServerDiagnosticSeverity.Error,
                    Line = i + 1,
                    Character = 1,
                    Code = $"CS{i:D4}",
                    Message = $"diagnostic {i:D4} {new string('m', 120)}"
                })
                .ToArray(),
            DiagnosticsTruncated = false
        };

    private static FileMutationSnapshot Snapshot(string text)
        => new(
            Text: text,
            ContentHash: "hash",
            ByteLength: text.Length,
            LineCount: text.Split('\n').Length,
            EncodingName: "utf-8",
            HasBom: false,
            LineEnding: "\n",
            LastWriteTimeUtc: DateTimeOffset.Parse("2026-06-06T12:00:00Z"),
            TextOmitted: false,
            OmissionReason: null);

    private static TCell ReadSingleCell<TCell>(AgentTuiSessionState state)
        where TCell : TranscriptCell
    {
        var rows = ReadRows(state.Shell.Transcript);
        rows.Should().ContainSingle();
        return rows[0].Cell.Should().BeOfType<TCell>().Subject;
    }

    private static List<TranscriptEntry> ReadRows(TranscriptModel model)
    {
        var rows = model.Snapshot().Entries.ToList();
        return rows;
    }

    private static string RenderTranscript(AgentTuiSessionState state, int width = 100, int height = 24)
        => TuiCapture.RenderToString(
            new TranscriptView(state.Shell.Transcript, DefaultTranscriptRenderers(), height: height - 2),
            width: width,
            height: height,
            trimTrailingBlankLines: true);

    private static AgentTuiTranscriptRendererRegistry DefaultTranscriptRenderers()
        => new HpdAgentTuiBuilder()
            .AddDefaultTranscriptRenderers()
            .AddCodingHarnessTui()
            .Build()
            .TranscriptRenderers;
}
