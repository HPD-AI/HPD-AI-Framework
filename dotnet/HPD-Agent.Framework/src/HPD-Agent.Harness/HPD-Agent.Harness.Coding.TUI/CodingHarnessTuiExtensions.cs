using HPD.Agent;
using HPD.Agent.TUI;
using HPD.Agent.ToolHarness.Coding.TUI.Harness;
using HPD.Agent.ToolHarness.Coding.TUI.Commands;
using HPD.Agent.ToolHarness.Coding.TUI.Commands.Handlers;
using HPD.Agent.ToolHarness.Coding.TUI.Commands.Pages;
using HPD.Agent.ToolHarness.Coding.TUI.Commands.Footer;
using HPD.Agent.ToolHarness.Coding.TUI.Exploration;
using HPD.Agent.ToolHarness.Coding.TUI.Exploration.Handlers;
using HPD.Agent.ToolHarness.Coding.TUI.Exploration.Footer;
using HPD.Agent.ToolHarness.Coding.TUI.FileMutations;
using HPD.Agent.ToolHarness.Coding.TUI.FileMutations.Handlers;
using HPD.Agent.ToolHarness.Coding.TUI.FileMutations.Footer;
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
            .AddCodingCommandTui(theme, permissionScope)
            .AddCodingFileMutationTui(theme);
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
                new CodingExplorationCellRenderer(theme))
            .TryAddFooterItem(
                "hpd.coding.exploration",
                new CodingExplorationFooterItem(theme));
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
            .TryAddInteractionHandler<ExecuteCommandSandboxCapabilityRequestEvent>(
                "hpd.coding.command.sandbox-capability",
                new ExecuteCommandSandboxCapabilityRequestTuiHandler(theme),
                permissionScope)
            .TryAddTranscriptRenderer<CodingCommandCell>(
                CodingHarnessTuiTranscriptRendererKeys.Command,
                new CodingCommandCellRenderer(theme))
            .TryAddPage(CodingCommandPages.CommandsPage(theme))
            .TryAddPage(CodingCommandPages.BackgroundPage(theme))
            .TryAddFooterItem(
                "hpd.coding.commands",
                new CodingCommandFooterItem(theme))
            .TryAddFooterItem(
                "hpd.coding.background",
                new CodingBackgroundTerminalFooterItem(theme))
            .TryAddFooterItem(
                "hpd.coding.output",
                new CodingCommandOutputFooterItem(theme));
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
                new CodingDiagnosticsCellRenderer(theme))
            .TryAddFooterItem(
                "hpd.coding.files",
                new FileMutationFooterItem(theme))
            .TryAddFooterItem(
                "hpd.coding.diagnostics",
                new CodingDiagnosticsFooterItem(theme));
    }
}
