using System.Text.Json;
using HPD.Agent;
using HPD.Agent.TUI;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.TUI.Views;
using HPD.Agent.ToolHarness.Coding.TUI;
using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Views;
using HPDOS.ToolHarnesses.Middleware;
using CodingDiagnosticsTranscriptCell = HPD.Agent.ToolHarness.Coding.TUI.FileMutations.CodingDiagnosticsCell;
using CodingFileMutationTranscriptCell = HPD.Agent.ToolHarness.Coding.TUI.FileMutations.FileMutationCell;

namespace HPD.Agent.ToolHarness.Coding.TUI.Tests;

public sealed class FileMutationTuiTests
{
    [Fact]
    public void AddCodingHarnessTui_RegistersFileMutationHandlersAndStatus()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddCodingHarnessTui()
            .Build();

        registry.EventHandlers.Select(static handler => handler.Key).Should().Contain([
            "hpd.coding.file-mutation.applied",
            "hpd.coding.diagnostics.received"
        ]);
        registry.StatusItems.Select(static item => item.Key).Should().Contain([
            "hpd.coding.files",
            "hpd.coding.diagnostics"
        ]);
        registry.TranscriptRenderers.TryFindRenderer<CodingFileMutationTranscriptCell>(
            CodingHarnessTuiTranscriptRendererKeys.FileMutation,
            out _).Should().BeTrue();
        registry.TranscriptRenderers.TryFindRenderer<CodingDiagnosticsTranscriptCell>(
            CodingHarnessTuiTranscriptRendererKeys.Diagnostics,
            out _).Should().BeTrue();
    }

    [Fact]
    public async Task FileEditApplied_RendersCompactDiff()
    {
        var state = CreateState();

        await state.ApplyEventAsync(EditMutation());

        var rows = ReadRows(state.Shell.Transcript);
        rows.Should().ContainSingle();
        rows[0].Cell.Should().BeOfType<CodingFileMutationTranscriptCell>()
            .Which.DisplayPath.Should().Be("src/Foo.cs");

        var rendered = RenderTranscript(state);
        rendered.Should().Contain("• Edited src/Foo.cs (+1 -1)");
        rendered.Should().Contain("1  public sealed class Foo");
        rendered.Should().Contain("2 -    private string _oldName;");
        rendered.Should().Contain("2 +    private string _name;");
    }

    [Fact]
    public async Task ReplacedFileMutationRenderer_KeepsMutationHandlersAndState()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .ReplaceTranscriptRenderer<CodingFileMutationTranscriptCell>(
                CodingHarnessTuiTranscriptRendererKeys.FileMutation,
                _ => new Text("custom mutation row"))
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);

        await state.ApplyEventAsync(EditMutation());

        ReadRows(state.Shell.Transcript).Should().ContainSingle()
            .Which.Cell.Should().BeOfType<CodingFileMutationTranscriptCell>();
        RenderTranscript(state, registry.TranscriptRenderers).Should().Contain("custom mutation row");
        RenderShell(registry, state).Should().Contain("files +1 -1");
    }

    [Fact]
    public async Task FileEditApplied_RendersAddAndDeleteBackgroundBands()
    {
        var state = CreateState();

        await state.ApplyEventAsync(EditMutation());

        var rendered = RenderTranscriptAnsi(state);
        rendered.Should().Contain("48;2;82;34;30");
        rendered.Should().Contain("48;2;24;62;38");
    }

    [Fact]
    public async Task CustomCodingTheme_StylesFileMutationDiff()
    {
        var codingTheme = new CodingHarnessTuiTheme
        {
            Label = new Style(new Color(201, 82, 221), Color.Default, TextAttributes.Bold),
            DiffAdded = new Style(new Color(70, 220, 120), new Color(10, 40, 20)),
            DiffRemoved = new Style(new Color(245, 95, 95), new Color(45, 15, 15)),
            DiffContext = new Style(new Color(205, 210, 215), Color.Default),
            DiffGutter = new Style(new Color(110, 120, 130), Color.Default)
        };
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui(codingTheme)
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);

        await state.ApplyEventAsync(EditMutation());

        var rendered = RenderTranscriptAnsi(state, registry.TranscriptRenderers);

        rendered.Should().Contain("38;2;201;82;221");
        rendered.Should().Contain("38;2;70;220;120");
        rendered.Should().Contain("48;2;10;40;20");
        rendered.Should().Contain("38;2;245;95;95");
        rendered.Should().Contain("48;2;45;15;15");
        rendered.Should().Contain("38;2;205;210;215");
        rendered.Should().Contain("38;2;110;120;130");
    }

    [Fact]
    public async Task FileWriteCreate_UsesCreatedAction()
    {
        var state = CreateState();

        await state.ApplyEventAsync(WriteMutation(FileWriteMode.Create));

        RenderTranscript(state).Should().Contain("• Created test/FooTests.cs (+2 -0)");
    }

    [Fact]
    public async Task FileWriteCreate_RendersFullDiff()
    {
        var state = CreateState();

        await state.ApplyEventAsync(WriteMutation(FileWriteMode.Create, lineCount: 24));

        var cell = ReadRows(state.Shell.Transcript).Should().ContainSingle()
            .Which.Cell.Should().BeOfType<CodingFileMutationTranscriptCell>().Subject;
        cell.HunksTruncated.Should().BeFalse();
        cell.Hunks.Sum(static hunk => hunk.Lines.Count).Should().Be(24);

        var rendered = RenderTranscript(state, height: 36);
        rendered.Should().Contain("1 +line 001");
        rendered.Should().Contain("24 +line 024");
        rendered.Should().NotContain("diff truncated");
    }

    [Fact]
    public async Task FileMutationReplay_DoesNotDoubleCountStatus()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);
        var mutation = WriteMutation(FileWriteMode.Create);

        await state.ApplyEventAsync(mutation);
        await state.ApplyEventAsync(mutation);

        ReadRows(state.Shell.Transcript).Should().ContainSingle();
        var rendered = RenderShell(registry, state);
        rendered.Should().Contain("files +2 -0");
        rendered.Should().NotContain("changed 2 files");
        rendered.Should().NotContain("+4 -0");
    }

    [Fact]
    public async Task PersistedFileEditMutation_ReplaysDiffTranscript()
    {
        var state = CreateState();
        var persisted = RoundTripThreadEvent(MultiHunkEditMutation());

        await state.ApplyEventAsync(persisted);

        var rows = ReadRows(state.Shell.Transcript);
        rows.Should().ContainSingle();
        rows[0].Cell.Should().BeOfType<CodingFileMutationTranscriptCell>()
            .Which.Hunks.Should().HaveCount(2);

        var rendered = RenderTranscript(state, height: 24);
        rendered.Should().Contain("• Edited src/Foo.cs (+2 -2)");
        rendered.Should().Contain("10 -old value");
        rendered.Should().Contain("10 +new value");
        rendered.Should().Contain("80 -old tail");
        rendered.Should().Contain("80 +new tail");
        rendered.Should().NotContain("diff truncated");
    }

    [Fact]
    public async Task PersistedFileEditMutation_ReplaysTruncationMarker()
    {
        var state = CreateState();
        var persisted = RoundTripThreadEvent(MultiHunkEditMutation(hunksTruncated: true));

        await state.ApplyEventAsync(persisted);

        var rendered = RenderTranscript(state, height: 24);
        rendered.Should().Contain("10 +new value");
        rendered.Should().Contain("80 +new tail");
        rendered.Should().Contain("diff truncated");
    }

    [Fact]
    public async Task Diagnostics_UpdateLatestMutationRowForSamePath()
    {
        var state = CreateState();

        await state.ApplyEventAsync(EditMutation());
        await state.ApplyEventAsync(Diagnostics(path: "/repo/src/Foo.cs", displayPath: "src/Foo.cs"));

        var rows = ReadRows(state.Shell.Transcript);
        rows.Should().ContainSingle();
        rows[0].Cell.Should().BeOfType<CodingFileMutationTranscriptCell>()
            .Which.Diagnostics.Should().ContainSingle();

        var rendered = RenderTranscript(state);
        rendered.Should().Contain("Diagnostics");
        rendered.Should().Contain("CS1002");
        rendered.Should().Contain("Missing semicolon");
    }

    [Fact]
    public async Task Diagnostics_WithoutMutation_RendersStandaloneRow()
    {
        var state = CreateState();

        await state.ApplyEventAsync(Diagnostics(path: "/repo/src/Foo.cs", displayPath: "src/Foo.cs"));

        var rows = ReadRows(state.Shell.Transcript);
        rows.Should().ContainSingle();
        rows[0].Cell.Should().BeOfType<CodingDiagnosticsTranscriptCell>()
            .Which.Path.Should().Be("/repo/src/Foo.cs");

        var rendered = RenderTranscript(state);
        rendered.Should().Contain("• Diagnostics /repo/src/Foo.cs");
        rendered.Should().Contain("CS1002");
    }

    [Fact]
    public async Task CustomCodingTheme_StylesDiagnostics()
    {
        var codingTheme = new CodingHarnessTuiTheme
        {
            Label = new Style(new Color(201, 82, 221), Color.Default, TextAttributes.Bold),
            Muted = new Style(new Color(110, 120, 130), Color.Default),
            DiagnosticError = new Style(new Color(255, 70, 80), Color.Default),
            DiagnosticWarning = new Style(new Color(245, 190, 80), Color.Default)
        };
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui(codingTheme)
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);

        await state.ApplyEventAsync(Diagnostics(
            path: "/repo/src/Foo.cs",
            displayPath: "src/Foo.cs",
            severity: LanguageServerDiagnosticSeverity.Warning,
            code: "CS8618"));

        var rendered = RenderTranscriptAnsi(state, registry.TranscriptRenderers);

        rendered.Should().Contain("38;2;201;82;221");
        rendered.Should().Contain("38;2;110;120;130");
        rendered.Should().Contain("38;2;245;190;80");
    }

    [Fact]
    public async Task StatusItems_ReadFileAndDiagnosticsState()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);

        await state.ApplyEventAsync(EditMutation());
        await state.ApplyEventAsync(Diagnostics(path: "/repo/src/Foo.cs", displayPath: "src/Foo.cs"));

        var rendered = RenderShell(registry, state);
        rendered.Should().Contain("files +1 -1");
        rendered.Should().Contain("diag 1E");
    }

    private static AgentTuiSessionState CreateState()
        => new(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            new HpdAgentTuiBuilder()
                .AddCodingHarnessTui()
                .Build());

    private static FileEditAppliedEvent EditMutation()
        => new()
        {
            EventId = "evt-edit-1",
            ToolCallId = "call-edit-1",
            FunctionName = "EditFile",
            Path = "/repo/src/Foo.cs",
            DisplayPath = "src/Foo.cs",
            MutationKind = CodingFileMutationKind.Changed,
            Created = false,
            Changed = true,
            Before = Snapshot("public sealed class Foo\n    private string _oldName;\n"),
            After = Snapshot("public sealed class Foo\n    private string _name;\n"),
            TextEdits = [],
            Hunks =
            [
                new FileMutationHunk(
                    OldStart: 1,
                    OldLines: 2,
                    NewStart: 1,
                    NewLines: 2,
                    Lines:
                    [
                        " public sealed class Foo",
                        "-    private string _oldName;",
                        "+    private string _name;"
                    ])
            ],
            HunksTruncated = false,
            DiffStat = new FileMutationDiffStat(AddedLines: 1, RemovedLines: 1, AddedChars: 5, RemovedChars: 8),
            Notes = [],
            EditCount = 1,
            ReplacementCount = 1,
            Replacements = [],
            Normalizations = []
        };

    private static FileEditAppliedEvent MultiHunkEditMutation(bool hunksTruncated = false)
        => EditMutation() with
        {
            EventId = "evt-edit-multi-hunk",
            ToolCallId = "call-edit-multi-hunk",
            Before = Snapshot("old value\nold tail\n"),
            After = Snapshot("new value\nnew tail\n"),
            Hunks =
            [
                new FileMutationHunk(
                    OldStart: 10,
                    OldLines: 1,
                    NewStart: 10,
                    NewLines: 1,
                    Lines:
                    [
                        "-old value",
                        "+new value"
                    ]),
                new FileMutationHunk(
                    OldStart: 80,
                    OldLines: 1,
                    NewStart: 80,
                    NewLines: 1,
                    Lines:
                    [
                        "-old tail",
                        "+new tail"
                    ])
            ],
            HunksTruncated = hunksTruncated,
            DiffStat = new FileMutationDiffStat(AddedLines: 2, RemovedLines: 2, AddedChars: 18, RemovedChars: 18)
        };

    private static FileWriteAppliedEvent WriteMutation(FileWriteMode mode, int lineCount = 2)
    {
        var lines = lineCount == 2
            ? ["+using Xunit;", "+public sealed class FooTests"]
            : Enumerable.Range(1, lineCount).Select(static i => $"+line {i:D3}").ToArray();
        var after = string.Join('\n', lines.Select(static line => line[1..])) + "\n";

        return new FileWriteAppliedEvent
        {
            EventId = "evt-write-1",
            ToolCallId = "call-write-1",
            FunctionName = "WriteFile",
            Path = "/repo/test/FooTests.cs",
            DisplayPath = "test/FooTests.cs",
            MutationKind = CodingFileMutationKind.Created,
            Created = true,
            Changed = true,
            Before = Snapshot(""),
            After = Snapshot(after),
            TextEdits = [],
            Hunks =
            [
                new FileMutationHunk(
                    OldStart: 0,
                    OldLines: 0,
                    NewStart: 1,
                    NewLines: lineCount,
                    Lines: lines)
            ],
            HunksTruncated = false,
            DiffStat = new FileMutationDiffStat(
                AddedLines: lineCount,
                RemovedLines: 0,
                AddedChars: after.Length,
                RemovedChars: 0),
            Notes = [],
            Mode = mode
        };
    }

    private static LanguageServerDiagnosticsReceivedEvent Diagnostics(
        string path,
        string displayPath,
        LanguageServerDiagnosticSeverity severity = LanguageServerDiagnosticSeverity.Error,
        string code = "CS1002")
        => new()
        {
            EventId = "evt-diagnostics-1",
            Path = path,
            Uri = $"file://{path}",
            ErrorCount = severity == LanguageServerDiagnosticSeverity.Error ? 1 : 0,
            WarningCount = severity == LanguageServerDiagnosticSeverity.Warning ? 1 : 0,
            InformationCount = 0,
            HintCount = 0,
            DiagnosticSetCount = 1,
            Diagnostics =
            [
                new LanguageServerDiagnosticSummary
                {
                    Path = displayPath,
                    ServerId = "csharp",
                    Source = LanguageServerDiagnosticSource.Publish,
                    Severity = severity,
                    Line = 2,
                    Character = 18,
                    Code = code,
                    Message = "Missing semicolon"
                }
            ],
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

    private static List<TranscriptEntry> ReadRows(TranscriptModel model)
    {
        var rows = model.Snapshot().Entries.ToList();
        return rows;
    }

    private static AgentEvent RoundTripThreadEvent(AgentEvent evt)
    {
        var projected = ThreadEventFactory.FromAgentEvent(
            "session-1",
            "thread-1",
            evt,
            messageTurnId: "turn-1",
            conversationId: "session-1",
            iteration: 1,
            inputMessageCount: 1,
            isResume: false,
            terminationReason: null,
            turnMessageCount: 1);

        projected.Should().NotBeNull();

        var document = new ThreadEventDocument
        {
            SessionId = "session-1",
            ThreadId = "thread-1",
            Events = [projected!]
        };
        var json = JsonSerializer.Serialize(document, SessionJsonContext.Combined.ThreadEventDocument);
        var roundTrip = JsonSerializer.Deserialize<ThreadEventDocument>(
            json,
            SessionJsonContext.Combined.ThreadEventDocument);

        roundTrip.Should().NotBeNull();
        roundTrip!.Events.Should().ContainSingle();
        return roundTrip.Events[0];
    }

    private static string RenderTranscript(
        AgentTuiSessionState state,
        AgentTuiTranscriptRendererRegistry? renderers = null,
        int width = 100,
        int height = 16)
        => TuiCapture.RenderToString(
            new TranscriptView(state.Shell.Transcript, renderers ?? DefaultTranscriptRenderers(), height: height - 2),
            width: width,
            height: height,
            trimTrailingBlankLines: true);

    private static string RenderTranscriptAnsi(
        AgentTuiSessionState state,
        AgentTuiTranscriptRendererRegistry? renderers = null,
        int width = 100,
        int height = 16)
        => TuiCapture.RenderToAnsi(
            new TranscriptView(state.Shell.Transcript, renderers ?? DefaultTranscriptRenderers(), height: height - 2),
            width: width,
            height: height);

    private static AgentTuiTranscriptRendererRegistry DefaultTranscriptRenderers()
        => new HpdAgentTuiBuilder()
            .AddDefaultTranscriptRenderers()
            .AddCodingHarnessTui()
            .Build()
            .TranscriptRenderers;

    private static string RenderShell(HpdAgentTuiRegistry registry, AgentTuiSessionState state)
        => TuiCapture.RenderToString(
            registry.ShellLayout.Create(new AgentTuiShellLayoutContext(
                state.Shell,
                PromptView.Create("Ask HPD..."),
                registry,
                registry.ShellChrome,
                state.State)),
            width: 100,
            height: 24,
            trimTrailingBlankLines: true);
}
