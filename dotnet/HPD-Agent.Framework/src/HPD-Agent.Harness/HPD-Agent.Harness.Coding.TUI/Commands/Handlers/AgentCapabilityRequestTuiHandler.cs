using System.Text;
using HPD.Agent;
using HPD.Agent.Security;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Interactions;
using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Utilities;
using HPD.TUI.Views;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands.Handlers;

public sealed class AgentCapabilityRequestTuiHandler :
    AgentTuiInteractionHandler<AgentCapabilityRequestEvent>
{
    private readonly CodingHarnessTuiTheme _theme;

    public AgentCapabilityRequestTuiHandler(CodingHarnessTuiTheme theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    protected override async Task<AgentTuiInteractionResult> HandleAsync(
        AgentTuiInteractionContext<AgentCapabilityRequestEvent> context,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = await context.Dialogs.ShowAsync<AgentCapabilityResponseEvent>(
            $"agent-capability:{request.RequestId}",
            dialog => new AgentCapabilityDialogComponent(request, dialog, _theme),
            cancellationToken).ConfigureAwait(false);

        return AgentTuiInteractionResult.AnswerRequest(
            response.IsSubmitted && response.Value is not null
                ? response.Value
                : new AgentCapabilityResponseEvent(
                    request.RequestId,
                    request.SourceName,
                    false));
    }
}

internal sealed class AgentCapabilityDialogComponent : HPD.TUI.Core.Component, IFocusable
{
    private readonly AgentCapabilityRequestEvent _request;
    private readonly AgentTuiDialogContext<AgentCapabilityResponseEvent> _dialog;
    private readonly SelectionModel<SandboxCapabilityChoice> _choices = new();
    private readonly SelectionController<SandboxCapabilityChoice> _controller;
    private readonly SelectionView<SandboxCapabilityChoice> _view;
    private readonly CodingHarnessTuiTheme _theme;

    public AgentCapabilityDialogComponent(
        AgentCapabilityRequestEvent request,
        AgentTuiDialogContext<AgentCapabilityResponseEvent> dialog,
        CodingHarnessTuiTheme theme)
    {
        _request = request;
        _dialog = dialog;
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
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

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
        => new(Math.Min(constraints.MaxWidth, 28), Math.Min(constraints.MaxWidth, 96), 11);

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
    {
        var maxWidth = output.MaxWidth;
        WriteLine(ref output, BuildTitle(_request.Capability), _theme.ResolvePermissionTitle(context.Theme), maxWidth);
        output.WriteLineBreak();
        WriteLine(ref output, $"operation: {_request.OperationId}", _theme.ResolvePermissionCommand(context.Theme), maxWidth);
        if (_request.Resource is not null)
            WriteLine(ref output, $"resource: {_request.Resource.Value}", _theme.ResolvePermissionDetail(context.Theme), maxWidth);
        WriteLine(ref output, $"reason: {_request.Reason}", _theme.ResolvePermissionDetail(context.Theme), maxWidth);
        output.WriteLineBreak();
        output.Render(_view, in context, maxWidth);
        output.WriteLineBreak();
        output.WriteLineBreak();
        WriteLine(ref output, "Use arrows to choose. Enter confirms. Esc denies.", _theme.ResolvePermissionDetail(context.Theme), maxWidth);
    }

    public override bool HandleInput(in TuiInputEvent input)
    {
        if (input.KeyEvent.Key == KeyCode.Escape)
        {
            Submit(new SandboxCapabilityChoice(false));
            return true;
        }

        return _view.HandleInput(in input);
    }

    private void Submit(SandboxCapabilityChoice choice)
        => _dialog.Submit(new AgentCapabilityResponseEvent(
            _request.RequestId,
            _request.SourceName,
            choice.Approved));

    private static string BuildTitle(AgentCapabilityKind capability)
        => capability switch
        {
            AgentCapabilityKind.LocalBinding => "Sandbox blocked local server binding",
            AgentCapabilityKind.NetworkEgress => "Sandbox blocked network access",
            AgentCapabilityKind.FilesystemRead => "Sandbox blocked file read access",
            AgentCapabilityKind.FilesystemWrite => "Sandbox blocked file write access",
            AgentCapabilityKind.InteractiveTerminal => "Sandbox blocked interactive terminal access",
            AgentCapabilityKind.UnsandboxedExecution => "Operation needs sandbox bypass",
            _ => "Sandbox blocked operation"
        };

    private static string BuildAllowDescription(AgentCapabilityRequestEvent request)
        => request.Capability switch
        {
            AgentCapabilityKind.LocalBinding => "Allow this operation to bind localhost ports once.",
            AgentCapabilityKind.NetworkEgress => "Allow this operation network egress once.",
            AgentCapabilityKind.FilesystemRead => "Allow this operation the requested file read once.",
            AgentCapabilityKind.FilesystemWrite => "Allow this operation the requested file write once.",
            AgentCapabilityKind.InteractiveTerminal => "Allow this operation to use an interactive terminal once.",
            AgentCapabilityKind.UnsandboxedExecution => "Run this operation without process isolation once.",
            _ => "Allow the requested sandbox capability once."
        };

    private static void WriteLine(ref DisplayListBuilder output, string value, Style style, int maxWidth)
    {
        WriteClipped(ref output, value, style, maxWidth);
        output.WriteLineBreak();
    }

    private static void WriteClipped(
        ref DisplayListBuilder output,
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
