using HPD.Agent.TUI;
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
            "hpd.coding.command.background-list"
        ]);
        registry.InteractionHandlers.Select(static handler => handler.Key)
            .Should()
            .Contain(["hpd.coding.command.permission", "hpd.coding.command.sandbox-capability"]);
        registry.Pages.Select(static page => page.Id).Should().Contain([
            "hpd.coding.commands",
            "hpd.coding.background"
        ]);
        registry.StatusItems.Select(static item => item.Key).Should().Contain([
            "hpd.coding.commands",
            "hpd.coding.background",
            "hpd.coding.output"
        ]);
        registry.BelowEditorWidgets.Should().BeEmpty();
        registry.TranscriptRenderers.TryFindRenderer<CodingCommandCell>(
            CodingHarnessTuiTranscriptRendererKeys.Command,
            out _).Should().BeTrue();
        registry.TryFindInteractionHandler(CreatePermissionRequest(), out var interaction)
            .Should().BeTrue();
        interaction.Key.Should().Be("hpd.coding.command.permission");
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
        var handler = new ExecuteCommandPermissionRequestTuiHandler();

        var response = await handler.HandleAsync(
            CreateInteractionContext(dialogs, request),
            CancellationToken.None);

        response.Should().BeOfType<ExecuteCommandPermissionResponseEvent>()
            .Which.Should().Match<ExecuteCommandPermissionResponseEvent>(evt =>
                evt.PermissionId == "permission-1" &&
                evt.SourceName == "ExecuteCommandPermissionMiddleware" &&
                evt.ChoiceId == "allow_exact" &&
                evt.FeedbackText == null);
        dialogs.LastShowKey.Should().Be("execute-command-permission:permission-1");
        dialogs.ShowPromptCount.Should().Be(1);
    }

    [Fact]
    public async Task PermissionRequestHandler_DeniesWhenDialogClosesWithoutResult()
    {
        var request = CreatePermissionRequest();
        var dialogs = new TestDialogService { RenderDialog = false };
        var handler = new ExecuteCommandPermissionRequestTuiHandler();

        var response = await handler.HandleAsync(
            CreateInteractionContext(dialogs, request),
            CancellationToken.None);

        response.Should().BeOfType<ExecuteCommandPermissionResponseEvent>()
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
        var handler = new ExecuteCommandPermissionRequestTuiHandler();

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
        var handler = new ExecuteCommandPermissionRequestTuiHandler();

        var response = await handler.HandleAsync(
            CreateInteractionContext(dialogs, request),
            CancellationToken.None);

        response.Should().BeOfType<ExecuteCommandPermissionResponseEvent>()
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
        var handler = new ExecuteCommandPermissionRequestTuiHandler();

        var response = await handler.HandleAsync(
            CreateInteractionContext(dialogs, request),
            CancellationToken.None);

        response.Should().BeOfType<ExecuteCommandPermissionResponseEvent>()
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
        rendered.Should().Contain("/background");
        rendered.Should().Contain("output truncated");
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
        rendered.Should().Contain("bg completed npm run dev");
        rendered.Should().NotContain("bg 1 npm run dev");
        rendered.Should().NotContain("/background");
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
        rendered.Should().Contain("task cmd-1");
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
        var sandbox = new ExecuteCommandSandboxPolicy();
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
            RunInBackground = false,
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
    {
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        var shell = new ChatShellModel(scope);
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
        var rows = model.Snapshot().Entries.ToList();
        return rows;
    }

    private static string RenderTranscript(
        AgentTuiSessionState state,
        AgentTuiTranscriptRendererRegistry? renderers = null,
        int width = 100,
        int height = 14)
        => TuiCapture.RenderToString(
            new TranscriptHistoryView(state.Shell.Transcript, renderers ?? DefaultTranscriptRenderers(), height: 12),
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

    private sealed class TestDialogService : IAgentTuiDialogService
    {
        public ExecuteCommandPermissionResponseEvent? ShowResult { get; init; }
        public IReadOnlyList<KeyEvent> DialogKeys { get; init; } = [];
        public bool RenderDialog { get; init; } = true;
        public string LastShowKey { get; private set; } = "";
        public string RenderedDialog { get; private set; } = "";
        public int ShowPromptCount { get; private set; }
        public bool HasOpenDialog => false;

        public Task<TResult?> ShowAsync<TResult>(
            string key,
            Func<AgentTuiDialogContext<TResult>, IComponent> componentFactory,
            CancellationToken cancellationToken = default)
        {
            LastShowKey = key;
            ShowPromptCount++;
            if (ShowResult is not null)
                return Task.FromResult((TResult?)(object?)ShowResult);

            if (!RenderDialog && DialogKeys.Count == 0)
                return Task.FromResult<TResult?>(default);

            TResult? result = default;
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
            }

            foreach (var keyEvent in DialogKeys)
            {
                component.HandleInput(keyEvent);
            }

            return Task.FromResult(result);
        }

        public bool Close(string key) => true;

        public bool CloseTop() => true;

        public Task<bool?> ConfirmAsync(
            string title,
            bool? defaultValue = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<bool?>(defaultValue ?? true);

        public Task<T?> SelectAsync<T>(
            string title,
            IReadOnlyList<T> options,
            Func<T, string> titleSelector,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("ExecuteCommand permission TUI must use ShowAsync.");

        public Task<string?> InputAsync(
            string title,
            string? defaultValue = null,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("ExecuteCommand permission TUI must use ShowAsync.");

        public Task<string?> SecretInputAsync(
            string title,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
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

        public async IAsyncEnumerable<AgentEvent> ObserveAsync(
            AgentTuiRuntimeScope scope,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task SubmitInputAsync(
            AgentTuiRuntimeScope scope,
            AgentInputEvent input,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task InterruptAsync(
            AgentTuiRuntimeScope scope,
            string reason,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RespondAsync(
            AgentTuiRuntimeScope scope,
            AgentEvent response,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<AgentEvent>> GetThreadEventsAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentEvent>>([]);

        public Task<AgentTuiThreadRun?> GetActiveRunAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult<AgentTuiThreadRun?>(null);
    }
}
