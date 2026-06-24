using System.Text;
using HPD.Agent;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Interactions;
using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Utilities;
using HPD.TUI.Views;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands.Handlers;

public sealed class ExecuteCommandSandboxCapabilityRequestTuiHandler :
    AgentTuiInteractionHandler<ExecuteCommandSandboxCapabilityRequestEvent>
{
    protected override async Task<AgentEvent?> HandleAsync(
        AgentTuiInteractionContext<ExecuteCommandSandboxCapabilityRequestEvent> context,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = await context.Dialogs.ShowAsync<ExecuteCommandSandboxCapabilityResponseEvent>(
            $"execute-command-sandbox-capability:{request.RequestId}",
            dialog => new ExecuteCommandSandboxCapabilityDialogComponent(request, dialog),
            cancellationToken).ConfigureAwait(false);

        return response ?? new ExecuteCommandSandboxCapabilityResponseEvent(
            request.RequestId,
            request.SourceName,
            false);
    }
}

internal sealed class ExecuteCommandSandboxCapabilityDialogComponent : IFocusable
{
    private readonly ExecuteCommandSandboxCapabilityRequestEvent _request;
    private readonly AgentTuiDialogContext<ExecuteCommandSandboxCapabilityResponseEvent> _dialog;
    private readonly SelectionModel<SandboxCapabilityChoice> _choices = new();
    private readonly SelectionController<SandboxCapabilityChoice> _controller;
    private readonly SelectionView<SandboxCapabilityChoice> _view;

    public ExecuteCommandSandboxCapabilityDialogComponent(
        ExecuteCommandSandboxCapabilityRequestEvent request,
        AgentTuiDialogContext<ExecuteCommandSandboxCapabilityResponseEvent> dialog)
    {
        _request = request;
        _dialog = dialog;
        _choices.Add(new CollectionItem<SandboxCapabilityChoice>(
            "allow_once",
            new SandboxCapabilityChoice(true),
            "Allow once",
            BuildAllowDescription(request)));
        _choices.Add(new CollectionItem<SandboxCapabilityChoice>(
            "deny",
            new SandboxCapabilityChoice(false),
            "Deny",
            "Keep the current sandbox restrictions."));
        _controller = new SelectionController<SandboxCapabilityChoice>(_choices)
        {
            Submitted = item => Submit(item.Value)
        };
        _view = new SelectionView<SandboxCapabilityChoice>(_choices, _controller);
    }

    public bool IsFocused
    {
        get => _view.IsFocused;
        set => _view.IsFocused = value;
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
        => new(Math.Min(maxWidth, 28), Math.Min(maxWidth, 96), 11);

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        WriteLine(ref output, BuildTitle(_request.Capability), context.Theme.Accent, maxWidth);
        output.WriteLineBreak();
        WriteLine(ref output, $"$ {_request.Command}", context.Theme.Text, maxWidth);
        WriteLine(ref output, $"cwd: {_request.WorkingDirectory}", context.Theme.Border, maxWidth);
        WriteLine(ref output, $"reason: {_request.FailureSummary}", context.Theme.Border, maxWidth);
        output.WriteLineBreak();
        _view.Render(in context, maxWidth, ref output);
        output.WriteLineBreak();
        output.WriteLineBreak();
        WriteLine(ref output, "Use arrows to choose. Enter confirms. Esc denies.", context.Theme.Border, maxWidth);
    }

    public bool HandleInput(in TuiInputEvent input)
    {
        if (input.KeyEvent.Key == KeyCode.Escape)
        {
            Submit(new SandboxCapabilityChoice(false));
            return true;
        }

        return _view.HandleInput(in input);
    }

    private void Submit(SandboxCapabilityChoice choice)
        => _dialog.Submit(new ExecuteCommandSandboxCapabilityResponseEvent(
            _request.RequestId,
            _request.SourceName,
            choice.Approved));

    private static string BuildTitle(ExecuteCommandSandboxCapabilityKind capability)
        => capability switch
        {
            ExecuteCommandSandboxCapabilityKind.LocalBinding => "Sandbox blocked local server binding",
            ExecuteCommandSandboxCapabilityKind.NetworkEgress => "Sandbox blocked network access",
            ExecuteCommandSandboxCapabilityKind.FilesystemRead => "Sandbox blocked file read access",
            ExecuteCommandSandboxCapabilityKind.FilesystemWrite => "Sandbox blocked file write access",
            ExecuteCommandSandboxCapabilityKind.Unsandboxed => "Command needs sandbox bypass",
            _ => "Sandbox blocked command"
        };

    private static string BuildAllowDescription(ExecuteCommandSandboxCapabilityRequestEvent request)
        => request.Capability switch
        {
            ExecuteCommandSandboxCapabilityKind.LocalBinding => "Allow this command to bind localhost ports once.",
            ExecuteCommandSandboxCapabilityKind.NetworkEgress => "Allow this command network egress once.",
            ExecuteCommandSandboxCapabilityKind.FilesystemRead => "Allow this command the requested file read once.",
            ExecuteCommandSandboxCapabilityKind.FilesystemWrite => "Allow this command the requested file write once.",
            ExecuteCommandSandboxCapabilityKind.Unsandboxed => "Run this command without process isolation once.",
            _ => "Allow the requested sandbox capability once."
        };

    private static void WriteLine(ref SegmentWriter output, string value, Style style, int maxWidth)
    {
        WriteClipped(ref output, value, style, maxWidth);
        output.WriteLineBreak();
    }

    private static void WriteClipped(
        ref SegmentWriter output,
        string value,
        Style style,
        int maxWidth)
    {
        if (maxWidth <= 0 || value.Length == 0)
            return;

        var used = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var width = Math.Max(0, UnicodeWidth.GetWidth(rune));
            if (used > 0 && used + width > maxWidth)
                return;

            output.Write(rune.ToString().AsSpan(), style);
            used += width;
        }
    }

    private sealed record SandboxCapabilityChoice(bool Approved);
}
