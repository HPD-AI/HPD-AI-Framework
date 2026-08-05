using HPD.Agent;
using HPD.Agent.TUI;
using HPD.Agent.ToolHarness.Coding.TUI.Harness;
using HPD.Agent.ToolHarness.Coding.TUI.Commands;
using HPD.Agent.ToolHarness.Coding.TUI.Commands.Handlers;
using HPD.Agent.ToolHarness.Coding.TUI.Commands.Pages;
using HPD.Agent.ToolHarness.Coding.TUI.Exploration;
using HPD.Agent.ToolHarness.Coding.TUI.Exploration.Handlers;
using HPD.Agent.ToolHarness.Coding.TUI.FileMutations;
using HPD.Agent.ToolHarness.Coding.TUI.FileMutations.Handlers;
using HPD.Agent.ToolHarness.Coding.TUI.SubAgents;
using HPD.Agent.ToolHarness.Coding.TUI.LanguageServers;
using HPD.Agent.ToolHarness.Coding.TUI.Debugging;
using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Composition;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI;

public static class CodingHarnessTuiExtensions
{
    /// <summary>
    /// Adds coding-harness presentation for the selected thread and configures how far
    /// permission interactions may route through its runtime tree.
    /// </summary>
    public static HpdAgentTuiBuilder AddCodingHarnessTui(
        this HpdAgentTuiBuilder tui,
        CodingHarnessTuiTheme? theme = null,
        AgentTuiEventScope permissionScope = AgentTuiEventScope.CurrentThread)
    {
        ArgumentNullException.ThrowIfNull(tui);
        theme ??= CodingHarnessTuiTheme.Default;
        return tui
            .AddCodingHarnessExpansionTui(theme)
            .AddCodingExplorationTui(theme)
            .AddCodingSubAgentTui(theme)
            .AddCodingCommandTui(theme, permissionScope)
            .AddCodingFileMutationTui(theme)
            .AddCodingDebuggerTui(theme)
            .AddCodingLanguageServerTui(theme);
    }

    /// <summary>Adds live language-server status and the inspect-only <c>/lsp</c> page.</summary>
    public static HpdAgentTuiBuilder AddCodingLanguageServerTui(
        this HpdAgentTuiBuilder tui,
        CodingHarnessTuiTheme? theme = null)
    {
        ArgumentNullException.ThrowIfNull(tui);
        theme ??= CodingHarnessTuiTheme.Default;

        return tui
            .TryAddEventHandler<LanguageServerStatusSnapshotEvent, LanguageServerStatusTuiHandler>(
                "hpd.coding.language-server.status")
            .TryAddPage(LanguageServerStatusPage.Create(theme))
            .TryAddSlashCommand(new HpdAgentTuiCommandDescriptor(
                "lsp",
                context => context.Navigation.GoToPage(LanguageServerStatusPage.PageId))
            {
                Title = "/lsp",
                Description = "Inspect activated language servers."
            });
    }

    /// <summary>
    /// Adds semantic, replayable transcript presentation for coding subagent invocations.
    /// </summary>
    public static HpdAgentTuiBuilder AddCodingSubAgentTui(
        this HpdAgentTuiBuilder tui,
        CodingHarnessTuiTheme? theme = null)
    {
        ArgumentNullException.ThrowIfNull(tui);
        theme ??= CodingHarnessTuiTheme.Default;

        return tui
            .TryAddEventHandler(
                "hpd.coding.subagent.lifecycle",
                new CodingSubAgentTuiHandler())
            .TryAddTranscriptRenderer<CodingSubAgentCell>(
                CodingHarnessTuiTranscriptRendererKeys.SubAgent,
                new CodingSubAgentCellRenderer(theme));
    }

    public static HpdAgentTuiBuilder AddCodingHarnessExpansionTui(
        this HpdAgentTuiBuilder tui,
        CodingHarnessTuiTheme? theme = null)
    {
        ArgumentNullException.ThrowIfNull(tui);
        theme ??= CodingHarnessTuiTheme.Default;

        return tui
            .TryAddEventHandler(
                "hpd.coding.harness.tool-call",
                new CodingHarnessToolCallHandler())
            .TryAddTranscriptRenderer<CodingHarnessToolCell>(
                CodingHarnessTuiTranscriptRendererKeys.Harness,
                new CodingHarnessToolCellRenderer(theme));
    }

    public static HpdAgentTuiBuilder AddCodingExplorationTui(
        this HpdAgentTuiBuilder tui,
        CodingHarnessTuiTheme? theme = null)
    {
        ArgumentNullException.ThrowIfNull(tui);
        theme ??= CodingHarnessTuiTheme.Default;

        return tui
            .TryAddEventHandler<ToolCallStartEvent, CodingExplorationToolCallStartHandler>(
                "hpd.coding.exploration.tool-start")
            .TryAddEventHandler<ToolCallArgsEvent, CodingExplorationToolCallArgsHandler>(
                "hpd.coding.exploration.tool-args")
            .TryAddEventHandler<ToolCallResultEvent, CodingExplorationToolCallResultHandler>(
                "hpd.coding.exploration.tool-result")
            .TryAddEventHandler<ToolCallEndEvent, CodingExplorationToolCallEndHandler>(
                "hpd.coding.exploration.tool-end")
            .TryAddTranscriptRenderer<CodingExplorationCell>(
                CodingHarnessTuiTranscriptRendererKeys.Exploration,
                new CodingExplorationCellRenderer(theme));
    }

    public static HpdAgentTuiBuilder AddCodingCommandTui(
        this HpdAgentTuiBuilder tui,
        CodingHarnessTuiTheme? theme = null,
        AgentTuiEventScope permissionScope = AgentTuiEventScope.CurrentThread)
    {
        ArgumentNullException.ThrowIfNull(tui);
        theme ??= CodingHarnessTuiTheme.Default;

        return tui
            .TryAddEventHandler<ExecuteCommandProcessStartedEvent, ExecuteCommandStartedTuiHandler>(
                "hpd.coding.command.started")
            .TryAddEventHandler<ExecuteCommandOutputChunkEvent, ExecuteCommandOutputChunkTuiHandler>(
                "hpd.coding.command.output")
            .TryAddEventHandler<ExecuteCommandProgressEvent, ExecuteCommandProgressTuiHandler>(
                "hpd.coding.command.progress")
            .TryAddEventHandler<ExecuteCommandAutoBackgroundedEvent, ExecuteCommandAutoBackgroundedTuiHandler>(
                "hpd.coding.command.backgrounded")
            .TryAddEventHandler<ExecuteCommandProcessExitedEvent, ExecuteCommandExitedTuiHandler>(
                "hpd.coding.command.exited")
            .TryAddEventHandler<ToolCallResultEvent, ExecuteCommandResultTuiHandler>(
                "hpd.coding.command.result")
            .TryAddInteractionHandler<ExecuteCommandPermissionRequestEvent>(
                "hpd.coding.command.permission",
                new ExecuteCommandPermissionRequestTuiHandler(theme),
                permissionScope)
            .TryAddInteractionHandler<HPD.Agent.Security.AgentCapabilityRequestEvent>(
                "hpd.coding.command.sandbox-capability",
                new AgentCapabilityRequestTuiHandler(theme),
                permissionScope)
            .TryAddTranscriptRenderer<CodingCommandCell>(
                CodingHarnessTuiTranscriptRendererKeys.Command,
                new CodingCommandCellRenderer(theme))
            .TryAddPage(CodingCommandPages.CommandsPage(theme))
            .TryAddPage(CodingCommandPages.BackgroundPage(theme));
    }

    public static HpdAgentTuiBuilder AddCodingFileMutationTui(
        this HpdAgentTuiBuilder tui,
        CodingHarnessTuiTheme? theme = null)
    {
        ArgumentNullException.ThrowIfNull(tui);
        theme ??= CodingHarnessTuiTheme.Default;

        return tui
            .TryAddEventHandler<FileMutationAppliedEvent, FileMutationTuiHandler>(
                "hpd.coding.file-mutation.applied")
            .TryAddEventHandler<LanguageServerDiagnosticsReceivedEvent, LanguageServerDiagnosticsTuiHandler>(
                "hpd.coding.diagnostics.received")
            .TryAddTranscriptRenderer<FileMutationCell>(
                CodingHarnessTuiTranscriptRendererKeys.FileMutation,
                new FileMutationCellRenderer(theme))
            .TryAddTranscriptRenderer<CodingDiagnosticsCell>(
                CodingHarnessTuiTranscriptRendererKeys.Diagnostics,
                new CodingDiagnosticsCellRenderer(theme));
    }

    /// <summary>
    /// Adds replayable debugger lifecycle presentation, transcript cells, status, and the
    /// inspect-only <c>/debug</c> page.
    /// </summary>
    public static HpdAgentTuiBuilder AddCodingDebuggerTui(
        this HpdAgentTuiBuilder tui,
        CodingHarnessTuiTheme? theme = null)
    {
        ArgumentNullException.ThrowIfNull(tui);
        theme ??= CodingHarnessTuiTheme.Default;
        return tui
            .TryAddEventHandler(
                "hpd.coding.debug.reducer",
                new DebugLifecycleTuiReducer())
            .TryAddEventHandler(
                "hpd.coding.debug.tool-calls",
                new DebugToolCallTuiCoordinator())
            .TryAddEventHandler<DebugLifecycleEvent, DebugLifecycleTuiHandler>(
                "hpd.coding.debug.lifecycle-presentation")
            .TryAddEventHandler<DebugExecutionCommandAppliedEvent, DebugExecutionCommandTuiHandler>(
                "hpd.coding.debug.execution")
            .TryAddEventHandler<DebugStateMutationAppliedEvent, DebugStateMutationTuiHandler>(
                "hpd.coding.debug.mutation")
            .TryAddEventHandler<DebugBreakpointSelectionAppliedEvent, DebugBreakpointSelectionTuiHandler>(
                "hpd.coding.debug.breakpoint-selection")
            .TryAddEventHandler<DebugBreakpointChangedEvent, DebugBreakpointChangedTuiHandler>(
                "hpd.coding.debug.breakpoint-changed")
            .TryAddEventHandler<DebugSessionStoppedEvent, DebugStoppedTuiHandler>(
                "hpd.coding.debug.stopped")
            .TryAddEventHandler<DebugPrimaryStopAvailableEvent, DebugPrimaryStopTuiHandler>(
                "hpd.coding.debug.primary-stop")
            .TryAddEventHandler<DebugSessionContinuedEvent, DebugContinuedTuiHandler>(
                "hpd.coding.debug.continued")
            .TryAddTranscriptRenderer<DebugBreakpointCell>(
                CodingHarnessTuiTranscriptRendererKeys.DebugBreakpoint,
                new DebugBreakpointCellRenderer(theme))
            .TryAddTranscriptRenderer<DebugStoppedCell>(
                CodingHarnessTuiTranscriptRendererKeys.DebugStopped,
                new DebugStoppedCellRenderer(theme))
            .TryAddTranscriptRenderer<DebugActivityCell>(
                CodingHarnessTuiTranscriptRendererKeys.DebugActivity,
                new DebugActivityCellRenderer(theme))
            .TryAddPage(DebugStatusPage.Create(theme))
            .TryAddSlashCommand(new HpdAgentTuiCommandDescriptor(
                "debug",
                context => context.Navigation.GoToPage(DebugStatusPage.PageId))
            {
                Title = "/debug",
                Description = "Inspect debugger state."
            });
    }
}
