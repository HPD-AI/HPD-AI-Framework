using HPD.Agent.TUI;
using HPD.Agent.Security;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Interactions;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.TUI.Views;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.ToolHarness.Coding.TUI;
using HPD.Agent.ToolHarness.Coding.TUI.Commands;
using HPD.Agent.ToolHarness.Coding.TUI.Commands.Handlers;
using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Views;
using HPDOS.ToolHarnesses.Middleware;

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
            "hpd.coding.command.result"
        ]);
        registry.InteractionHandlers.Select(static handler => handler.Key)
            .Should()
            .Contain(["hpd.coding.command.permission", "hpd.coding.command.sandbox-capability"]);
        registry.Pages.Select(static page => page.Id).Should().Contain([
            "hpd.coding.commands",
            "hpd.coding.background"
        ]);
        registry.BelowEditorWidgets.Should().BeEmpty();
        registry.TranscriptRenderers.TryFindRenderer<CodingCommandCell>(
            CodingHarnessTuiTranscriptRendererKeys.Command,
            out _).Should().BeTrue();
        registry.TryFindInteractionHandler(
                CreatePermissionRequest(),
                new AgentTuiRuntimeScope("agent", "session", "main"),
                out var interaction)
            .Should().BeTrue();
        interaction.Key.Should().Be("hpd.coding.command.permission");
    }

    [Fact]
    public void AddCodingHarnessTui_AppliesConfiguredPermissionScopeOnlyToInteractions()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddCodingHarnessTui(
                permissionScope: AgentTuiEventScope.CurrentThreadAndDescendants)
            .Build();

        registry.InteractionHandlers.Should().OnlyContain(
            item => item.Scope == AgentTuiEventScope.CurrentThreadAndDescendants);
        registry.EventHandlers.Should().OnlyContain(
            item => item.Scope == AgentTuiEventScope.CurrentThread);
    }

    [Fact]
    public async Task PermissionRequestHandler_ReturnsDialogChoice()
    {
        var request = CreatePermissionRequest();
        var dialogs = new TestDialogService
        {
            ShowResult = new ExecuteCommandPermissionResponseEvent(
                "permission-1",
                "ExecuteCommandPermissionMiddleware",
                "allow_exact")
        };
        var handler = new ExecuteCommandPermissionRequestTuiHandler(CodingHarnessTuiTheme.Default);
        var context = CreateInteractionContext(dialogs, request, out var shell);

        var result = await handler.HandleAsync(context, CancellationToken.None);

        result.Response.Should().BeOfType<ExecuteCommandPermissionResponseEvent>()
            .Which.Should().Match<ExecuteCommandPermissionResponseEvent>(evt =>
                evt.PermissionId == "permission-1" &&
                evt.SourceName == "ExecuteCommandPermissionMiddleware" &&
                evt.ChoiceId == "allow_exact" &&
                evt.FeedbackText == null);
        dialogs.LastShowKey.Should().Be("execute-command-permission:permission-1");
        dialogs.ShowPromptCount.Should().Be(1);
        shell.PromptStatusText.Should().Be("state: running | press Esc twice to cancel");
    }

    [Fact]
    public async Task PermissionRequestHandler_DeniesWhenDialogClosesWithoutResult()
    {
        var request = CreatePermissionRequest();
        var dialogs = new TestDialogService { RenderDialog = false };
        var handler = new ExecuteCommandPermissionRequestTuiHandler(CodingHarnessTuiTheme.Default);

        var result = await handler.HandleAsync(
            CreateInteractionContext(dialogs, request),
            CancellationToken.None);

        result.Response.Should().BeOfType<ExecuteCommandPermissionResponseEvent>()
            .Which.Should().Match<ExecuteCommandPermissionResponseEvent>(evt =>
                evt.PermissionId == "permission-1" &&
                evt.ChoiceId == "deny" &&
                evt.FeedbackText == null);
        dialogs.ShowPromptCount.Should().Be(1);
    }

    [Fact]
    public async Task PermissionDialog_RendersCommandDetailsAndBackendChoices()
    {
        var request = CreatePermissionRequest();
        var dialogs = new TestDialogService();
        var handler = new ExecuteCommandPermissionRequestTuiHandler(CodingHarnessTuiTheme.Default);

        await handler.HandleAsync(
            CreateInteractionContext(dialogs, request),
            CancellationToken.None);

        dialogs.RenderedDialog.Should().Contain("Approve command?");
        dialogs.RenderedDialog.Should().Contain("Reason: run a shell command");
        dialogs.RenderedDialog.Should().Contain("$ git status -sb");
        dialogs.RenderedDialog.Should().Contain("cwd: /repo");
        dialogs.RenderedDialog.Should().Contain("Security review");
        dialogs.RenderedDialog.Should().Contain("sandbox:");
        dialogs.RenderedDialog.Should().Contain("effects: no filesystem or network effects detected");
        dialogs.RenderedDialog.Should().Contain("risk: none");
        dialogs.RenderedDialog.Should().Contain("rule: no saved rule matched");
        dialogs.RenderedDialog.Should().Contain("Allow once");
        dialogs.RenderedDialog.Should().Contain("Always allow this exact command");
        dialogs.RenderedDialog.Should().Contain("Tell agent what to do instead");
    }

    [Fact]
    public async Task PermissionDialog_SubmitsSelectedBackendChoice()
    {
        var request = CreatePermissionRequest();
        var dialogs = new TestDialogService
        {
            DialogKeys =
            [
                new KeyEvent(KeyCode.DownArrow),
                new KeyEvent(KeyCode.Enter)
            ]
        };
        var handler = new ExecuteCommandPermissionRequestTuiHandler(CodingHarnessTuiTheme.Default);

        var result = await handler.HandleAsync(
            CreateInteractionContext(dialogs, request),
            CancellationToken.None);

        result.Response.Should().BeOfType<ExecuteCommandPermissionResponseEvent>()
            .Which.Should().Match<ExecuteCommandPermissionResponseEvent>(evt =>
                evt.ChoiceId == "allow_exact" &&
                evt.FeedbackText == null);
    }

    [Fact]
    public async Task PermissionDialog_CapturesFeedbackInSameDialog()
    {
        var request = CreatePermissionRequest();
        var dialogs = new TestDialogService
        {
            DialogKeys =
            [
                new KeyEvent(KeyCode.DownArrow),
                new KeyEvent(KeyCode.DownArrow),
                new KeyEvent(KeyCode.Enter),
                new KeyEvent(KeyCode.Paste, Text: "Use git status without shell wrappers."),
                new KeyEvent(KeyCode.Enter)
            ]
        };
        var handler = new ExecuteCommandPermissionRequestTuiHandler(CodingHarnessTuiTheme.Default);

        var result = await handler.HandleAsync(
            CreateInteractionContext(dialogs, request),
            CancellationToken.None);

        result.Response.Should().BeOfType<ExecuteCommandPermissionResponseEvent>()
            .Which.Should().Match<ExecuteCommandPermissionResponseEvent>(evt =>
                evt.ChoiceId == "feedback" &&
                evt.FeedbackText == "Use git status without shell wrappers.");
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
    public async Task CustomCodingTheme_StylesCommandTranscript()
    {
        var codingTheme = new CodingHarnessTuiTheme
        {
            Label = new Style(new Color(201, 82, 221), Color.Default, TextAttributes.Bold),
            CommandOutput = new Style(new Color(70, 210, 150), Color.Default),
            CommandErrorOutput = new Style(new Color(245, 110, 95), Color.Default),
            CommandCompleted = new Style(new Color(90, 230, 130), Color.Default),
            CommandFailed = new Style(new Color(255, 80, 90), Color.Default)
        };
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui(codingTheme)
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);

        await state.ApplyEventAsync(Started(command: "dotnet test"));
        await state.ApplyEventAsync(Output("restore complete\n"));
        await state.ApplyEventAsync(Output("compiler warning\n", stream: ExecuteCommandStreamKind.Stderr));
        await state.ApplyEventAsync(Exited(exitCode: 0));

        var rendered = RenderTranscriptAnsi(state, registry.TranscriptRenderers);

        rendered.Should().Contain("38;2;201;82;221");
        rendered.Should().Contain("38;2;70;210;150");
        rendered.Should().Contain("38;2;245;110;95");
        rendered.Should().Contain("38;2;90;230;130");
    }

    [Fact]
    public async Task CustomCodingTheme_StylesFailedCommandState()
    {
        var codingTheme = new CodingHarnessTuiTheme
        {
            CommandFailed = new Style(new Color(255, 80, 90), Color.Default)
        };
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui(codingTheme)
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);

        await state.ApplyEventAsync(Started(command: "dotnet build"));
        await state.ApplyEventAsync(Exited(command: "dotnet build", exitCode: 1));

        RenderTranscriptAnsi(state, registry.TranscriptRenderers)
            .Should().Contain("38;2;255;80;90");
    }

    [Fact]
    public async Task CustomCodingTheme_StylesCommandStatusAndPages()
    {
        var codingTheme = new CodingHarnessTuiTheme
        {
            CommandRunning = new Style(new Color(10, 180, 250), Color.Default),
            CommandOutput = new Style(new Color(70, 210, 150), Color.Default),
            Prefix = new Style(new Color(90, 100, 120), Color.Default)
        };
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddCodingHarnessTui(codingTheme)
            .Build();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            registry);

        await state.ApplyEventAsync(Started(command: "dotnet test"));
        await state.ApplyEventAsync(Output("tests running\n"));
        state.Shell.Navigation.GoToPage("hpd.coding.commands");

        var rendered = RenderShellAnsi(registry, state);

        rendered.Should().Contain("38;2;10;180;250");
        rendered.Should().Contain("38;2;70;210;150");
        rendered.Should().Contain("38;2;90;100;120");
    }

    [Fact]
    public async Task CustomCodingTheme_StylesPermissionDialog()
    {
        var request = CreatePermissionRequest();
        var dialogs = new TestDialogService();
        var handler = new ExecuteCommandPermissionRequestTuiHandler(new CodingHarnessTuiTheme
        {
            PermissionTitle = new Style(new Color(100, 210, 255), Color.Default),
            PermissionDetail = new Style(new Color(110, 120, 135), Color.Default),
            PermissionCommand = new Style(new Color(240, 245, 250), Color.Default)
        });

        await handler.HandleAsync(
            CreateInteractionContext(dialogs, request),
            CancellationToken.None);

        dialogs.RenderedDialogAnsi.Should().Contain("38;2;100;210;255");
        dialogs.RenderedDialogAnsi.Should().Contain("38;2;110;120;135");
        dialogs.RenderedDialogAnsi.Should().Contain("38;2;240;245;250");
    }

    [Fact]
    public async Task CustomCodingTheme_StylesSandboxCapabilityDialog()
    {
        var request = new AgentCapabilityRequestEvent(
            "sandbox-1",
            "ExecuteCommand",
            "call-1",
            "cmd-1",
            AgentCapabilityKind.NetworkEgress,
            new AgentCapabilityResource { Value = "registry.npmjs.org", DisplayName = "registry.npmjs.org" },
            "network is blocked");
        var dialogs = new TestDialogService();
        var handler = new AgentCapabilityRequestTuiHandler(new CodingHarnessTuiTheme
        {
            PermissionTitle = new Style(new Color(100, 210, 255), Color.Default),
            PermissionDetail = new Style(new Color(110, 120, 135), Color.Default),
            PermissionCommand = new Style(new Color(240, 245, 250), Color.Default)
        });

        await handler.HandleAsync(
            CreateInteractionContext(dialogs, request),
            CancellationToken.None);

        dialogs.RenderedDialogAnsi.Should().Contain("38;2;100;210;255");
        dialogs.RenderedDialogAnsi.Should().Contain("38;2;110;120;135");
        dialogs.RenderedDialogAnsi.Should().Contain("38;2;240;245;250");
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
        rendered.Should().Contain("• Background command completed npm run dev");
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
    public async Task BackgroundListResult_RendersTranscriptEntry()
    {
        var state = CreateState();

        await state.ApplyEventAsync(ExecuteCommandResult(
            """
            <execute_command_background count="1">
              <command operation_id="bg-1" command="npm run dev" cwd="/repo" status="running" />
            </execute_command_background>
            """));

        var rendered = RenderTranscript(state);

        rendered.Should().Contain("• Ran List background commands");
        rendered.Should().Contain("running bg-1 npm run dev");
    }

    [Fact]
    public async Task StopBackgroundCommandResult_FinalizesExistingBackgroundRow()
    {
        var state = CreateState();

        await state.ApplyEventAsync(Started(command: "npm run dev", background: true));
        await state.ApplyEventAsync(Output("ready on 5173\n", command: "npm run dev"));
        await state.ApplyEventAsync(ExecuteCommandResult(
            """
            <execute_command_stop operation_id="cmd-1" command="npm run dev" cwd="/repo" status="stopped" exit_code="137" completion_kind="stopped" />
            """,
            callId: "call-stop"));

        var rows = ReadRows(state.Shell.Transcript);
        rows.Should().ContainSingle();

        var rendered = RenderTranscript(state);
        rendered.Should().Contain("• Background command cancelled npm run dev");
        rendered.Should().Contain("ready on 5173");
        rendered.Should().Contain("exit 137");
    }

    [Fact]
    public async Task StopBackgroundCommandResult_RendersWithoutPriorStartEvent()
    {
        var state = CreateState();

        await state.ApplyEventAsync(ExecuteCommandResult(
            """
            <execute_command_stop operation_id="bg-1" command="npm run dev" cwd="/repo" status="stopped" exit_code="137" completion_kind="stopped" />
            """));

        var rendered = RenderTranscript(state);

        rendered.Should().Contain("• Background command cancelled npm run dev");
        rendered.Should().Contain("exit 137");
    }

    [Fact]
    public async Task CommandOutput_RendersInTranscript()
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

        var rendered = RenderTranscript(state, registry.TranscriptRenderers);
        rendered.Should().Contain("dotnet test");
        rendered.Should().Contain("restore complete");
        rendered.Should().Contain("tests running");
    }

    [Fact]
    public async Task BackgroundCommandOutput_RendersInTranscript()
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

        var rendered = RenderTranscript(state, registry.TranscriptRenderers);
        rendered.Should().Contain("npm run dev");
        rendered.Should().Contain("listening on http://localhost:5173");
    }

    [Fact]
    public async Task AutoBackgroundedCommandOutput_RendersInTranscript()
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

        var rendered = RenderTranscript(state, registry.TranscriptRenderers);
        rendered.Should().Contain("npm run dev");
        rendered.Should().Contain("server starting");
        rendered.Should().Contain("ready on 5173");
    }

    [Fact]
    public async Task BackgroundStatus_ShowsCompletedBackgroundResultAfterExit()
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

        var rendered = RenderShell(registry, state);
        rendered.Should().Contain("Background command completed npm run dev");
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
        rendered.Should().Contain("• npm run dev");
        rendered.Should().Contain("handle cmd-1");
        rendered.Should().Contain("cwd /repo");
        rendered.Should().Contain("state running");
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

    private static ExecuteCommandPermissionRequestEvent CreatePermissionRequest()
    {
        var sandbox = AgentSandboxRuntime.Default;
        var workspace = new ExecuteCommandPermissionWorkspaceScope
        {
            RootId = "default",
            RootPath = "/repo",
            RelativeWorkingDirectory = "."
        };
        var shell = new ExecuteCommandShellScope
        {
            Executable = "/bin/zsh",
            Family = ExecuteCommandShellFamily.Zsh
        };
        var exactRule = new ExecuteCommandPermissionRule
        {
            Id = "rule-exact",
            RuleSchemaVersion = 1,
            AnalyzerVersion = 1,
            NormalizationVersion = 1,
            Behavior = ExecuteCommandPermissionBehavior.Allow,
            MatchKind = ExecuteCommandPermissionMatchKind.Exact,
            Pattern = "git status -sb",
            Shell = shell,
            RequestedSandboxFingerprint = sandbox.Canonicalize("/repo"),
            Workspace = workspace,
            Risk = ExecuteCommandPermissionRisk.None,
            MinimumTrustLevel = ExecuteCommandAnalysisTrustLevel.Simple
        };
        var exactProposal = new ExactAllowRuleProposal
        {
            Rule = exactRule,
            UserLabel = "Always allow this exact command"
        };
        var plan = new SimpleCommandPermissionPlan
        {
            AnalyzerVersion = 1,
            NormalizationVersion = 1,
            Fingerprint = new PermissionFingerprint("fingerprint-1"),
            Action = ExecuteCommandAction.Run,
            Command = new RawCommandText("git status -sb"),
            NormalizedCommand = new NormalizedCommandText("git status -sb"),
            Shell = shell,
            WorkingDirectory = "/repo",
            Workspace = workspace,
            RequestedSandbox = sandbox,
            FilesystemEffects = [],
            NetworkEffects = [],
            StartsInBackground = false,
            Risk = ExecuteCommandPermissionRisk.None,
            CommandPlan = new ExecuteCommandSubcommandPlan
            {
                Text = "git status -sb",
                Argv = ["git", "status", "-sb"],
                BaseCommand = "git",
                SafePrefix = "git status",
                Risk = ExecuteCommandPermissionRisk.None,
                TrustLevel = ExecuteCommandAnalysisTrustLevel.Simple,
                Readiness = ExecuteCommandPolicyReadiness.PrefixAllowAllowed
            },
            ExactAllowRule = exactProposal,
            PrefixAllowRule = null,
            SuggestedRules = [exactProposal]
        };
        var choices = new ExecuteCommandPermissionChoice[]
        {
            new AllowOnceChoice
            {
                Id = "allow_once",
                Label = "Allow once"
            },
            new PersistRuleChoice
            {
                Id = "allow_exact",
                Label = "Always allow this exact command",
                Proposal = exactProposal
            },
            new FeedbackChoice
            {
                Id = "feedback",
                Label = "Tell agent what to do instead"
            }
        };

        return new ExecuteCommandPermissionRequestEvent(
            "permission-1",
            "ExecuteCommandPermissionMiddleware",
            "call-1",
            plan,
            [],
            new ExecuteCommandPermissionRuleDiagnostics(null, null, [], [], []),
            choices);
    }

    private static AgentTuiInteractionContext CreateInteractionContext(
        IAgentTuiDialogService dialogs,
        AgentEvent request)
        => CreateInteractionContext(dialogs, request, out _);

    private static AgentTuiInteractionContext CreateInteractionContext(
        IAgentTuiDialogService dialogs,
        AgentEvent request,
        out ChatShellModel shell)
    {
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        shell = new ChatShellModel(scope);
        return new AgentTuiInteractionContext(
            scope,
            shell,
            shell.Navigation,
            new NoopRuntime(),
            dialogs,
            request);
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
            OperationId = "bg-1",
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

    private static ToolCallResultEvent ExecuteCommandResult(
        string text,
        string callId = "call-1")
        => new(
            callId,
            new ToolResultPayload(Text: text),
            ToolHarnessName: "CodingToolHarness",
            Name: "ExecuteCommand");

    private static List<TranscriptEntry> ReadRows(TranscriptModel model)
    {
        var rows = model.Snapshot().Entries.ToList();
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

    private static string RenderTranscriptAnsi(
        AgentTuiSessionState state,
        AgentTuiTranscriptRendererRegistry renderers,
        int width = 100,
        int height = 14)
        => TuiCapture.RenderToAnsi(
            new TranscriptView(state.Shell.Transcript, renderers, height: 12),
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

    private static string RenderShellAnsi(HpdAgentTuiRegistry registry, AgentTuiSessionState state)
        => TuiCapture.RenderToAnsi(
            registry.ShellLayout.Create(new AgentTuiShellLayoutContext(
                state.Shell,
                PromptView.Create("Ask HPD..."),
                registry,
                registry.ShellChrome,
                state.State)),
            width: 100,
            height: 24);

    private sealed class TestDialogService : IAgentTuiDialogService
    {
        public ExecuteCommandPermissionResponseEvent? ShowResult { get; init; }
        public IReadOnlyList<KeyEvent> DialogKeys { get; init; } = [];
        public bool RenderDialog { get; init; } = true;
        public string LastShowKey { get; private set; } = "";
        public string RenderedDialog { get; private set; } = "";
        public string RenderedDialogAnsi { get; private set; } = "";
        public int ShowPromptCount { get; private set; }
        public bool HasOpenDialog => false;

        public Task<AgentTuiDialogResult<TResult>> ShowAsync<TResult>(
            string key,
            Func<AgentTuiDialogContext<TResult>, IComponent> componentFactory,
            CancellationToken cancellationToken = default)
        {
            LastShowKey = key;
            ShowPromptCount++;
            if (ShowResult is not null)
                return Task.FromResult(AgentTuiDialogResult<TResult>.Submitted((TResult)(object)ShowResult));

            if (!RenderDialog && DialogKeys.Count == 0)
                return Task.FromResult(AgentTuiDialogResult<TResult>.Dismissed());

            var result = AgentTuiDialogResult<TResult>.Dismissed();
            var shell = new ChatShellModel(new AgentTuiRuntimeScope("agent", "session", "main"));
            var dialog = new AgentTuiDialogContext<TResult>(
                key,
                shell.Navigation,
                value => result = value);
            var component = componentFactory(dialog);
            if (RenderDialog)
            {
                RenderedDialog = TuiCapture.RenderToString(
                    component,
                    width: 100,
                    height: 24,
                    trimTrailingBlankLines: true);
                RenderedDialogAnsi = TuiCapture.RenderToAnsi(
                    component,
                    width: 100,
                    height: 24);
            }

            foreach (var keyEvent in DialogKeys)
            {
                component.HandleInput(keyEvent);
            }

            return Task.FromResult(result);
        }

        public bool Close(string key) => true;

        public bool CloseTop() => true;

        public Task<AgentTuiDialogResult<bool>> ConfirmAsync(
            string title,
            bool? defaultValue = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiDialogResult<bool>.Submitted(defaultValue ?? true));

        public Task<AgentTuiDialogResult<T>> SelectAsync<T>(
            string title,
            IReadOnlyList<T> options,
            Func<T, string> titleSelector,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("ExecuteCommand permission TUI must use ShowAsync.");

        public Task<AgentTuiDialogResult<string>> InputAsync(
            string title,
            string? defaultValue = null,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("ExecuteCommand permission TUI must use ShowAsync.");

        public Task<AgentTuiDialogResult<string>> SecretInputAsync(
            string title,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiDialogResult<string>.Dismissed());
    }

    private sealed class NoopRuntime : IHpdAgentTuiRuntime
    {
        public Task<AgentTuiScopeResolution> ResolveInitialScopeAsync(
            AgentTuiRuntimeScope? requested,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiScopeResolution(
                requested ?? new AgentTuiRuntimeScope("agent", "session", "main"),
                IsDurable: true));

        public Task<AgentTuiRuntimeScope> EnsureDurableScopeAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult(scope);

        public async IAsyncEnumerable<AgentTuiEventBatch> ObserveAsync(
            AgentTuiRuntimeScope scope,
            ThreadJournalCursor after,
            ThreadJournalCursor initialObservedCursor,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<AgentTuiSubmitResult> SubmitInputAsync(
            AgentTuiRuntimeScope scope,
            AgentInputEvent input,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiSubmitResult(
                AgentInputDisposition.Queued,
                "run",
                new AgentTuiThreadExecution("run", scope.AgentId, scope.SessionId, scope.ThreadId, "active", DateTimeOffset.UtcNow)));

        public Task<AgentRespondResult> AnswerRequestAsync(
            AgentTuiRuntimeScope scope,
            AgentEvent response,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentRespondResult(
                AgentRespondStatus.Accepted,
                response.EventId));

        public Task<AgentTuiSubmitResult> CancelExecutionAsync(
            AgentTuiRuntimeScope scope, string threadExecutionId, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiSubmitResult(AgentInputDisposition.Accepted, threadExecutionId, null));

        public Task<AgentTuiThreadState> GetThreadStateAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiThreadState(ThreadJournalCursor.Start(1), null, []));
    }
}
