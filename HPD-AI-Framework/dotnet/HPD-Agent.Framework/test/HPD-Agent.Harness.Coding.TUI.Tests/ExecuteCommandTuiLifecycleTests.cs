using HPD.Agent.TUI;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.TUI.Views;
using HPD.Agent.ToolHarness.Coding.TUI;
using HPD.Agent.ToolHarness.Coding.TUI.Commands;
using HPD.TUI.Components;
using HPD.TUI.Rendering;
using HPD.TUI.Views;

namespace HPD.Agent.ToolHarness.Coding.TUI.Tests;

public sealed class ExecuteCommandTuiLifecycleTests
{
    [Fact]
    public void AddCodingHarnessTui_RegistersExecuteCommandHandlers()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddCodingHarnessTui()
            .Build();

        registry.EventHandlers.Select(static handler => handler.Key).Should().Contain([
            "hpd.coding.command.started",
            "hpd.coding.command.output",
            "hpd.coding.command.progress",
            "hpd.coding.command.backgrounded",
            "hpd.coding.command.exited",
            "hpd.coding.command.background-list"
        ]);
        registry.Pages.Select(static page => page.Id).Should().Contain([
            "hpd.coding.commands",
            "hpd.coding.background"
        ]);
        registry.StatusItems.Select(static item => item.Key).Should().Contain([
            "hpd.coding.commands",
            "hpd.coding.background",
            "hpd.coding.output"
        ]);
        registry.BelowEditorWidgets.Select(static widget => widget.Key).Should().NotContain([
            "hpd.coding.active-command",
            "hpd.coding.background-commands"
        ]);
        registry.TranscriptRenderers.TryFindRenderer<CodingCommandCell>(
            CodingHarnessTuiTranscriptRendererKeys.Command,
            out _).Should().BeTrue();
    }

    [Fact]
    public async Task CommandLifecycle_UpdatesOneKeyedTranscriptEntry()
    {
        var state = CreateState();

        await state.ApplyEventAsync(Started(command: "bash -lc \"dotnet test\""));
        await state.ApplyEventAsync(Output("Determining projects to restore...\n"));
        await state.ApplyEventAsync(Progress(elapsedMilliseconds: 1_200, stdoutBytes: 34));
        await state.ApplyEventAsync(Exited(exitCode: 0, durationMilliseconds: 1_800));

        var rows = ReadRows(state.Shell.Transcript);
        rows.Should().ContainSingle();
        rows[0].EntryKey.Should().Be("coding.command:cmd-1");
        rows[0].Cell.Should().BeOfType<CodingCommandCell>()
            .Which.DisplayCommand.Should().Be("dotnet test");

        var rendered = RenderTranscript(state);
        rendered.Should().Contain("• Ran dotnet test");
        rendered.Should().Contain("Determining projects to restore...");
        rendered.Should().Contain("1.8s");
        rendered.Should().Contain("exit 0");
    }

    [Fact]
    public async Task ReplacedCommandRenderer_KeepsCommandHandlersAndState()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .ReplaceTranscriptRenderer<CodingCommandCell>(
                CodingHarnessTuiTranscriptRendererKeys.Command,
                _ => new Text("custom command row"))
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);

        await state.ApplyEventAsync(Started(command: "dotnet test"));
        await state.ApplyEventAsync(Output("restore complete\n"));

        ReadRows(state.Shell.Transcript).Should().ContainSingle()
            .Which.Cell.Should().BeOfType<CodingCommandCell>();
        RenderTranscript(state, registry.TranscriptRenderers).Should().Contain("custom command row");
    }

    [Fact]
    public async Task FailedExit_RendersFailureStateAndExitCode()
    {
        var state = CreateState();

        await state.ApplyEventAsync(Started(command: "dotnet build"));
        await state.ApplyEventAsync(Output(
            "error CS0246: missing type\n",
            command: "dotnet build",
            stream: ExecuteCommandStreamKind.Stderr));
        await state.ApplyEventAsync(Exited(command: "dotnet build", exitCode: 1, durationMilliseconds: 840));

        var rendered = RenderTranscript(state);
        rendered.Should().Contain("• Failed dotnet build");
        rendered.Should().Contain("error CS0246");
        rendered.Should().Contain("exit 1");
    }

    [Fact]
    public async Task BackgroundedCommand_StaysOneRowAndFinalizesByCommandId()
    {
        var state = CreateState();

        await state.ApplyEventAsync(Started(command: "npm run dev"));
        await state.ApplyEventAsync(Output("listening on http://localhost:5173\n", command: "npm run dev"));
        await state.ApplyEventAsync(Backgrounded());
        await state.ApplyEventAsync(Exited(command: "npm run dev", exitCode: 0, durationMilliseconds: 3_250));

        var rows = ReadRows(state.Shell.Transcript);
        rows.Should().ContainSingle();

        var rendered = RenderTranscript(state);
        rendered.Should().Contain("• Ran npm run dev");
        rendered.Should().Contain("listening on http://localhost:5173");
        rendered.Should().Contain("3.3s");
        rendered.Should().Contain("exit 0");
    }

    [Fact]
    public async Task OutputBuffer_RendersHeadTailTruncation()
    {
        var state = CreateState();

        await state.ApplyEventAsync(Started(command: "dotnet test"));
        for (var i = 1; i <= 8; i++)
        {
            await state.ApplyEventAsync(Output($"line {i}\n"));
        }

        var rendered = RenderTranscript(state);
        rendered.Should().Contain("line 1");
        rendered.Should().Contain("line 2");
        rendered.Should().Contain("... +4 lines");
        rendered.Should().Contain("line 7");
        rendered.Should().Contain("line 8");
        rendered.Should().NotContain("line 4");
    }

    [Fact]
    public async Task BulkFindFileDump_RendersAsCompactCommandCell()
    {
        var state = CreateState();
        const string command = "find . -not -path './bin/*' -not -path './obj/*' -type f | sort | while read f; do echo \"\"; echo \"═══════════════════════════════════════════\"; echo \"FILE: $f\"; cat \"$f\"; done";

        await state.ApplyEventAsync(Started(command: command));
        await state.ApplyEventAsync(Output("\n"));
        for (var i = 1; i <= 12; i++)
        {
            await state.ApplyEventAsync(Output($"file dump line {i}\n", command: command));
        }

        await state.ApplyEventAsync(Exited(command: command, exitCode: 0, durationMilliseconds: 147));

        var rendered = RenderTranscript(state);
        rendered.Should().Contain("• Ran find . -not -path './bin/*' -not -path './obj/*' ... | cat matching files");
        rendered.Should().NotContain("while read");
        rendered.Should().NotContain("echo \"\"");
        rendered.Split('\n').Should().NotContain(static line => line.TrimEnd().EndsWith("└", StringComparison.Ordinal));
        rendered.Should().Contain("file dump line 1");
        rendered.Should().Contain("... +");
        rendered.Should().Contain("file dump line 12");
    }

    [Fact]
    public async Task OutputBuffer_ClipsVeryLongLines()
    {
        var state = CreateState();
        var longLine = string.Concat(
            new string('a', 4_200),
            "middle-secret",
            new string('z', 4_200),
            "\n");

        await state.ApplyEventAsync(Started(command: "dotnet test"));
        await state.ApplyEventAsync(Output(longLine));

        var rendered = RenderTranscript(state);
        rendered.Should().Contain("[line clipped]");
        rendered.Should().NotContain("middle-secret");
    }

    [Fact]
    public async Task OutputBuffer_TruncatesAfterTerminalWidthWrapping()
    {
        var state = CreateState();

        await state.ApplyEventAsync(Started(command: "dotnet test"));
        await state.ApplyEventAsync(Output(
            "alpha-beta-gamma-delta-epsilon-zeta-eta-theta-iota-kappa-lambda-" +
            "mu-nu-xi-omicron-pi-rho-sigma-tau-upsilon-phi-chi-psi-omega\n"));

        var rendered = RenderTranscript(state, width: 24, height: 14);
        rendered.Should().Contain("alpha-beta");
        rendered.Should().Contain("... +");
        rendered.Should().Contain("ega");
    }

    [Fact]
    public async Task OutputBuffer_DropsOldLinesWhenCharacterBoundIsReached()
    {
        var state = CreateState();

        await state.ApplyEventAsync(Started(command: "dotnet test"));
        for (var i = 1; i <= 90; i++)
        {
            await state.ApplyEventAsync(Output($"line {i:D3} {new string('x', 900)}\n"));
        }

        var rendered = RenderTranscript(state, width: 1000);
        rendered.Should().Contain("... +");
        rendered.Should().Contain("line 090");
        rendered.Should().NotContain("line 001");
    }

    [Fact]
    public async Task BackgroundList_DoesNotCreateTranscriptEntryByItself()
    {
        var state = CreateState();

        await state.ApplyEventAsync(new ExecuteCommandBackgroundListEvent
        {
            EventFlowId = "cmd-1",
            ToolCallId = "call-1",
            FunctionName = "ExecuteCommand",
            CommandId = "cmd-1",
            Command = "jobs",
            BaseCommand = "jobs",
            Category = ExecuteCommandCategory.Server,
            WorkingDirectory = "/repo",
            Count = 2
        });

        state.Shell.Transcript.Count.Should().Be(0);
    }

    [Fact]
    public async Task StatusItems_ReadSharedCommandState()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);

        await state.ApplyEventAsync(Started(command: "npm run dev"));
        await state.ApplyEventAsync(Output(
            "listening on http://localhost:5173\n",
            command: "npm run dev",
            truncated: true));
        await state.ApplyEventAsync(Backgrounded());

        var rendered = RenderShell(registry, state);
        rendered.Should().Contain("bg 1 npm run dev");
        rendered.Should().Contain("output truncated");
    }

    [Fact]
    public async Task CommandOutput_RendersInTranscriptInsteadOfBelowEditorWidget()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);

        await state.ApplyEventAsync(Started(command: "dotnet test"));
        await state.ApplyEventAsync(Output("restore complete\n"));
        await state.ApplyEventAsync(Output("build complete\n"));
        await state.ApplyEventAsync(Output("tests running\n"));
        await state.ApplyEventAsync(Progress(elapsedMilliseconds: 1_250, stdoutBytes: 42));

        RenderBelowEditorWidgets(registry, state).Trim().Should().BeEmpty();

        var rendered = RenderTranscript(state, registry.TranscriptRenderers);
        rendered.Should().Contain("dotnet test");
        rendered.Should().Contain("restore complete");
        rendered.Should().Contain("tests running");
    }

    [Fact]
    public async Task BelowEditorWidget_ClearsAfterCommandExit()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);

        await state.ApplyEventAsync(Started(command: "dotnet test"));
        await state.ApplyEventAsync(Output("tests running\n"));
        await state.ApplyEventAsync(Exited(exitCode: 0, durationMilliseconds: 900));

        var rendered = RenderBelowEditorWidgets(registry, state);
        rendered.Should().NotContain("tests running");
        rendered.Trim().Should().BeEmpty();
    }

    [Fact]
    public async Task BackgroundCommandOutput_RendersInTranscriptInsteadOfBelowEditorWidget()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);

        await state.ApplyEventAsync(Started(command: "npm run dev", background: true));
        await state.ApplyEventAsync(Output("listening on http://localhost:5173\n", command: "npm run dev"));
        await state.ApplyEventAsync(Progress(
            elapsedMilliseconds: 1_500,
            command: "npm run dev",
            stdoutBytes: 37));

        RenderBelowEditorWidgets(registry, state).Trim().Should().BeEmpty();

        var rendered = RenderTranscript(state, registry.TranscriptRenderers);
        rendered.Should().Contain("npm run dev");
        rendered.Should().Contain("listening on http://localhost:5173");
    }

    [Fact]
    public async Task AutoBackgroundedCommandOutput_RendersInTranscriptInsteadOfBelowEditorWidget()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);

        await state.ApplyEventAsync(Started(command: "npm run dev"));
        await state.ApplyEventAsync(Output("server starting\n", command: "npm run dev"));
        await state.ApplyEventAsync(Backgrounded());
        await state.ApplyEventAsync(Output("ready on 5173\n", command: "npm run dev"));

        RenderBelowEditorWidgets(registry, state).Trim().Should().BeEmpty();

        var rendered = RenderTranscript(state, registry.TranscriptRenderers);
        rendered.Should().Contain("npm run dev");
        rendered.Should().Contain("server starting");
        rendered.Should().Contain("ready on 5173");
    }

    [Fact]
    public async Task BackgroundWidget_RemovesCommandAfterExit()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);

        await state.ApplyEventAsync(Started(command: "npm run dev", background: true));
        await state.ApplyEventAsync(Output("ready on 5173\n", command: "npm run dev"));
        await state.ApplyEventAsync(Exited(command: "npm run dev", exitCode: 0, durationMilliseconds: 2_000));

        var rendered = RenderBelowEditorWidgets(registry, state);
        rendered.Should().NotContain("ready on 5173");
        rendered.Trim().Should().BeEmpty();
    }

    [Fact]
    public async Task CommandsPage_RendersCommandStateFromSharedStore()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);

        await state.ApplyEventAsync(Started(command: "dotnet test"));
        await state.ApplyEventAsync(Output("tests passing\n"));
        await state.ApplyEventAsync(Exited(exitCode: 0, durationMilliseconds: 1_200));

        state.Shell.Navigation.GoToPage("hpd.coding.commands");
        var rendered = RenderShell(registry, state);

        rendered.Should().Contain("Coding commands");
        rendered.Should().Contain("Ran dotnet test");
        rendered.Should().Contain("tests passing");
        rendered.Should().Contain("exit 0");
    }

    [Fact]
    public async Task BackgroundPage_RendersActiveBackgroundCommands()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui()
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);

        await state.ApplyEventAsync(Started(command: "npm run dev", background: true));
        await state.ApplyEventAsync(Output("ready on 5173\n", command: "npm run dev"));

        state.Shell.Navigation.GoToPage("hpd.coding.background");
        var rendered = RenderShell(registry, state);

        rendered.Should().Contain("Background commands");
        rendered.Should().Contain("Backgrounded npm run dev");
        rendered.Should().Contain("ready on 5173");
    }

    [Fact]
    public async Task ProgressOnlyUpdate_DoesNotRepaintTranscriptWhenVisibleSummaryIsUnchanged()
    {
        var state = CreateState();

        await state.ApplyEventAsync(Started(command: "dotnet test"));
        var versionAfterStart = state.Shell.Transcript.Version;

        await state.ApplyEventAsync(Progress(elapsedMilliseconds: 1_000, stdoutBytes: 100));

        state.Shell.Transcript.Version.Should().Be(versionAfterStart);
    }

    [Fact]
    public async Task SuppressedAndBinaryOutput_RenderMarkersWithoutRawText()
    {
        var state = CreateState();

        await state.ApplyEventAsync(Started(command: "dotnet test"));
        await state.ApplyEventAsync(Output(
            "secret bytes",
            suppressed: true,
            binary: true));

        var rendered = RenderTranscript(state);
        rendered.Should().Contain("output suppressed");
        rendered.Should().Contain("binary output");
        rendered.Should().NotContain("secret bytes");
    }

    [Fact]
    public async Task UnsafeShellWrapper_IsPreservedForDisplay()
    {
        var state = CreateState();

        await state.ApplyEventAsync(Started(command: "bash -lc dotnet test"));

        RenderTranscript(state).Should().Contain("bash -lc dotnet test");
    }

    [Fact]
    public async Task OrphanStreamingEvents_DoNotCreateTranscriptEntries()
    {
        var state = CreateState();

        await state.ApplyEventAsync(Output("late output\n"));
        await state.ApplyEventAsync(Progress(elapsedMilliseconds: 500, stdoutBytes: 12));

        state.Shell.Transcript.Count.Should().Be(0);
    }

    [Fact]
    public async Task ExitWithoutInMemoryStart_CreatesFinalizedTranscriptEntry()
    {
        var state = CreateState();

        await state.ApplyEventAsync(Exited(command: "dotnet test", exitCode: 0, durationMilliseconds: 900));

        var rows = ReadRows(state.Shell.Transcript);
        rows.Should().ContainSingle();
        rows[0].EntryKey.Should().Be("coding.command:cmd-1");
        rows[0].Cell.Should().BeOfType<CodingCommandCell>()
            .Which.State.Should().Be(CodingCommandTranscriptState.Completed);

        RenderTranscript(state).Should().Contain("• Ran dotnet test");
    }

    [Fact]
    public async Task DuplicateExit_DoesNotRegressFinalState()
    {
        var state = CreateState();

        await state.ApplyEventAsync(Started(command: "dotnet test"));
        await state.ApplyEventAsync(Exited(command: "dotnet test", exitCode: 0, durationMilliseconds: 900));
        await state.ApplyEventAsync(Exited(command: "dotnet test", exitCode: 1, durationMilliseconds: 1_400));

        var rendered = RenderTranscript(state);
        rendered.Should().Contain("• Ran dotnet test");
        rendered.Should().Contain("exit 0");
        rendered.Should().NotContain("exit 1");
    }

    private static AgentTuiSessionState CreateState()
        => new(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            new HpdAgentTuiBuilder()
                .AddCodingHarnessTui()
                .Build());

    private static ExecuteCommandProcessStartedEvent Started(
        string command,
        string commandId = "cmd-1",
        string toolCallId = "call-1",
        bool background = false)
        => new()
        {
            EventFlowId = commandId,
            ToolCallId = toolCallId,
            FunctionName = "ExecuteCommand",
            CommandId = commandId,
            Command = command,
            BaseCommand = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? command,
            Category = ExecuteCommandCategory.Test,
            WorkingDirectory = "/repo",
            Shell = "zsh",
            StartedAt = DateTimeOffset.Parse("2026-06-06T12:00:00Z"),
            Background = background,
            AutoBackgroundEligible = true,
            ProcessId = 123,
            TimeoutMilliseconds = 120_000
        };

    private static ExecuteCommandOutputChunkEvent Output(
        string text,
        string command = "dotnet test",
        ExecuteCommandStreamKind stream = ExecuteCommandStreamKind.Stdout,
        bool truncated = false,
        bool suppressed = false,
        bool binary = false)
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
            Stream = stream,
            Text = text,
            ObservedAt = DateTimeOffset.Parse("2026-06-06T12:00:01Z"),
            StreamBytesObserved = text.Length,
            CombinedBytesObserved = text.Length,
            Truncated = truncated,
            Suppressed = suppressed,
            Binary = binary
        };

    private static ExecuteCommandProgressEvent Progress(long elapsedMilliseconds, long stdoutBytes = 0, long stderrBytes = 0)
        => Progress(
            elapsedMilliseconds,
            command: "dotnet test",
            stdoutBytes: stdoutBytes,
            stderrBytes: stderrBytes);

    private static ExecuteCommandProgressEvent Progress(
        long elapsedMilliseconds,
        string command,
        long stdoutBytes = 0,
        long stderrBytes = 0)
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
            ElapsedMilliseconds = elapsedMilliseconds,
            StdoutBytes = stdoutBytes,
            StderrBytes = stderrBytes,
            CombinedOutputBytes = stdoutBytes + stderrBytes,
            CombinedBytesDiscarded = 0,
            OutputObserved = stdoutBytes + stderrBytes > 0,
            OutputEventsSuppressed = false
        };

    private static ExecuteCommandAutoBackgroundedEvent Backgrounded()
        => new()
        {
            EventFlowId = "cmd-1",
            ToolCallId = "call-1",
            FunctionName = "ExecuteCommand",
            CommandId = "cmd-1",
            Command = "npm run dev",
            BaseCommand = "npm",
            Category = ExecuteCommandCategory.Server,
            WorkingDirectory = "/repo",
            BackgroundTaskId = "bg-1",
            BackgroundedAt = DateTimeOffset.Parse("2026-06-06T12:00:02Z"),
            ElapsedMilliseconds = 2_000
        };

    private static ExecuteCommandProcessExitedEvent Exited(
        string command = "dotnet test",
        int? exitCode = 0,
        long durationMilliseconds = 0)
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
            ExitCode = exitCode,
            CompletionKind = ExecuteCommandCompletionKind.Completed,
            DurationMilliseconds = durationMilliseconds,
            StdoutBytes = 0,
            StderrBytes = 0,
            CombinedOutputBytes = 0,
            StdoutBytesDiscarded = 0,
            StderrBytesDiscarded = 0,
            CombinedBytesDiscarded = 0,
            OutputTruncated = false,
            OutputDrainTimedOut = false,
            OutputEventsSuppressed = false,
            StdoutArtifactPath = null,
            StderrArtifactPath = null,
            CombinedOutputArtifactPath = null,
            StdoutContentId = null,
            StderrContentId = null,
            CombinedOutputContentId = null,
            StdoutLocalPath = null,
            StderrLocalPath = null,
            CombinedOutputLocalPath = null
        };

    private static List<TranscriptEntry> ReadRows(TranscriptModel model)
    {
        var rows = new List<TranscriptEntry>();
        model.CopyTo(rows);
        return rows;
    }

    private static string RenderTranscript(
        AgentTuiSessionState state,
        AgentTuiTranscriptRendererRegistry? renderers = null,
        int width = 100,
        int height = 14)
        => TuiCapture.RenderToString(
            new TranscriptView(state.Shell.Transcript, renderers ?? DefaultTranscriptRenderers(), height: 12),
            width: width,
            height: height,
            trimTrailingBlankLines: true);

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

    private static string RenderBelowEditorWidgets(HpdAgentTuiRegistry registry, AgentTuiSessionState state)
        => TuiCapture.RenderToString(
            new ContributionWidgetSlotView(
                TuiSlot.BelowEditor,
                state.Shell,
                state.State,
                registry.BelowEditorWidgets),
            width: 100,
            height: 8,
            trimTrailingBlankLines: true);
}
