using HPD.Agent;
using HPD.Agent.TUI;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.TUI.Views;
using HPD.Agent.ToolHarness.Coding.TUI;
using HPD.TUI.Rendering;
using HPDOS.ToolHarnesses.Middleware;
using MiddlewareFileMutationHunk = HPDOS.ToolHarnesses.Middleware.FileMutationHunk;

namespace HPD.Agent.ToolHarness.Coding.TUI.Benchmarks;

internal static class CodingBenchmarkScenarios
{
    public static HpdAgentTuiRegistry Registry { get; } = CreateRegistry();

    public static AgentTuiTranscriptRendererRegistry Renderers { get; } = Registry.TranscriptRenderers;

    public static AgentTuiSessionState CreateState()
        => new(new AgentTuiRuntimeScope("agent", "session", "main"));

    public static string RenderTranscript(AgentTuiSessionState state, int width = 100, int height = 24)
        => TuiCapture.RenderToString(
            new TranscriptView(state.Shell.Transcript, Renderers, height: Math.Max(1, height - 2)),
            width: width,
            height: height,
            trimTrailingBlankLines: false);

    public static async Task PopulateCommandAsync(
        AgentTuiSessionState state,
        int chunks,
        ExecuteCommandStreamKind stream = ExecuteCommandStreamKind.Stdout,
        string? line = null)
    {
        await state.ApplyEventAsync(Started("dotnet test"), Registry);
        var text = line ?? $"line {new string('x', 80)}\n";
        for (var i = 0; i < chunks; i++)
        {
            await state.ApplyEventAsync(Output($"{i:D4} {text}", stream), Registry);
        }
    }

    private static HpdAgentTuiRegistry CreateRegistry()
    {
        var store = new AgentTuiContributionStore();
        return new HpdAgentTuiBuilder(store, HpdContributionOwner.App)
            .AddDefaultTranscriptRenderers()
            .AddCodingHarnessTui()
            .Build();
    }

    public static ExecuteCommandProcessStartedEvent Started(string command)
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

    public static ExecuteCommandOutputChunkEvent Output(
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

    public static async Task PopulateExplorationAsync(AgentTuiSessionState state, int operations)
    {
        for (var i = 0; i < operations; i++)
        {
            var callId = $"call-read-{i:D3}";
            await state.ApplyEventAsync(new ToolCallStartEvent(callId, "ReadFile", "msg-1"), Registry);
            await state.ApplyEventAsync(new ToolCallArgsEvent(callId, $$"""{"path":"src/File{{i:D3}}.cs"}"""), Registry);
        }
    }

    public static FileEditAppliedEvent Mutation(int hunkCount, int linesPerHunk)
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
            HunksTruncated = false,
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

    public static LanguageServerDiagnosticsReceivedEvent Diagnostics(int count)
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
}
