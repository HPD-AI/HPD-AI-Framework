using HPD.Agent;
using HPD.Agent.TUI;
using HPD.Agent.ToolHarness.Coding.TUI.Commands.Handlers;
using HPD.Agent.ToolHarness.Coding.TUI.Commands.Pages;
using HPD.Agent.ToolHarness.Coding.TUI.Commands.Status;
using HPD.Agent.ToolHarness.Coding.TUI.Commands.Widgets;
using HPD.Agent.ToolHarness.Coding.TUI.Exploration.Handlers;
using HPD.Agent.ToolHarness.Coding.TUI.Exploration.Status;
using HPD.Agent.ToolHarness.Coding.TUI.FileMutations.Handlers;
using HPD.Agent.ToolHarness.Coding.TUI.FileMutations.Status;
using HPD.Agent.TUI.Composition;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI;

public static class CodingHarnessTuiExtensions
{
    public static HpdAgentTuiBuilder AddCodingHarnessTui(this HpdAgentTuiBuilder tui)
    {
        ArgumentNullException.ThrowIfNull(tui);
        return tui
            .AddCodingExplorationTui()
            .AddCodingCommandTui()
            .AddCodingFileMutationTui();
    }

    public static HpdAgentTuiBuilder AddCodingExplorationTui(this HpdAgentTuiBuilder tui)
    {
        ArgumentNullException.ThrowIfNull(tui);

        return tui
            .TryAddEventHandler<ToolCallStartEvent, CodingExplorationToolCallStartHandler>(
                "hpd.coding.exploration.tool-start")
            .TryAddEventHandler<ToolCallArgsEvent, CodingExplorationToolCallArgsHandler>(
                "hpd.coding.exploration.tool-args")
            .TryAddEventHandler<ToolCallResultEvent, CodingExplorationToolCallResultHandler>(
                "hpd.coding.exploration.tool-result")
            .TryAddEventHandler<ToolCallEndEvent, CodingExplorationToolCallEndHandler>(
                "hpd.coding.exploration.tool-end")
            .TryAddStatusItem(
                "hpd.coding.exploration",
                new CodingExplorationStatusItem());
    }

    public static HpdAgentTuiBuilder AddCodingCommandTui(this HpdAgentTuiBuilder tui)
    {
        ArgumentNullException.ThrowIfNull(tui);

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
            .TryAddEventHandler<ExecuteCommandBackgroundListEvent, ExecuteCommandBackgroundListTuiHandler>(
                "hpd.coding.command.background-list")
            .TryAddPage(CodingCommandPages.CommandsPage())
            .TryAddPage(CodingCommandPages.BackgroundPage())
            .TryAddStatusItem(
                "hpd.coding.commands",
                new CodingCommandStatusItem())
            .TryAddStatusItem(
                "hpd.coding.background",
                new CodingBackgroundTerminalStatusItem())
            .TryAddStatusItem(
                "hpd.coding.output",
                new CodingCommandOutputStatusItem())
            .TryAddWidget(
                TuiSlot.BelowEditor,
                "hpd.coding.active-command",
                new CodingActiveCommandTailWidget())
            .TryAddWidget(
                TuiSlot.BelowEditor,
                "hpd.coding.background-commands",
                new CodingBackgroundCommandsWidget());
    }

    public static HpdAgentTuiBuilder AddCodingFileMutationTui(this HpdAgentTuiBuilder tui)
    {
        ArgumentNullException.ThrowIfNull(tui);

        return tui
            .TryAddEventHandler<FileMutationAppliedEvent, FileMutationTuiHandler>(
                "hpd.coding.file-mutation.applied")
            .TryAddEventHandler<LanguageServerDiagnosticsReceivedEvent, LanguageServerDiagnosticsTuiHandler>(
                "hpd.coding.diagnostics.received")
            .TryAddStatusItem(
                "hpd.coding.files",
                new FileMutationStatusItem())
            .TryAddStatusItem(
                "hpd.coding.diagnostics",
                new CodingDiagnosticsStatusItem());
    }
}
