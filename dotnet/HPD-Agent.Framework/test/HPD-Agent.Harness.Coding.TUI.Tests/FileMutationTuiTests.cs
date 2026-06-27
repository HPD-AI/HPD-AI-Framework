using HPD.Agent.TUI;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.TUI.Views;
using HPD.Agent.ToolHarness.Coding.TUI;
using HPD.TUI.Components;
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
    public async Task FileWriteCreate_UsesCreatedAction()
    {
        var state = CreateState();

        await state.ApplyEventAsync(WriteMutation(FileWriteMode.Create));

        RenderTranscript(state).Should().Contain("• Created test/FooTests.cs (+2 -0)");
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

    private static FileWriteAppliedEvent WriteMutation(FileWriteMode mode)
        => new()
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
            After = Snapshot("using Xunit;\npublic sealed class FooTests\n"),
            TextEdits = [],
            Hunks =
            [
                new FileMutationHunk(
                    OldStart: 0,
                    OldLines: 0,
                    NewStart: 1,
                    NewLines: 2,
                    Lines:
                    [
                        "+using Xunit;",
                        "+public sealed class FooTests"
                    ])
            ],
            HunksTruncated = false,
            DiffStat = new FileMutationDiffStat(AddedLines: 2, RemovedLines: 0, AddedChars: 40, RemovedChars: 0),
            Notes = [],
            Mode = mode
        };

    private static LanguageServerDiagnosticsReceivedEvent Diagnostics(string path, string displayPath)
        => new()
        {
            EventId = "evt-diagnostics-1",
            Path = path,
            Uri = $"file://{path}",
            ErrorCount = 1,
            WarningCount = 0,
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
                    Severity = LanguageServerDiagnosticSeverity.Error,
                    Line = 2,
                    Character = 18,
                    Code = "CS1002",
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

    private static string RenderTranscript(
        AgentTuiSessionState state,
        AgentTuiTranscriptRendererRegistry? renderers = null,
        int width = 100,
        int height = 16)
        => TuiCapture.RenderToString(
            new TranscriptView(state.Shell.Transcript, renderers ?? DefaultTranscriptRenderers(), height: 14),
            width: width,
            height: height,
            trimTrailingBlankLines: true);

    private static string RenderTranscriptAnsi(AgentTuiSessionState state, int width = 100, int height = 16)
        => TuiCapture.RenderToAnsi(
            new TranscriptView(state.Shell.Transcript, DefaultTranscriptRenderers(), height: 14),
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
