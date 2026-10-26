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
    private readonly TimeProvider _timeProvider;
    private readonly object _grantGate = new();
    private readonly Dictionary<CapabilityTurnKey, DateTimeOffset> _turnGrants = [];
    private static readonly TimeSpan TurnGrantLifetime = TimeSpan.FromHours(24);
    private const int MaximumTurnGrants = 64;

    public AgentCapabilityRequestTuiHandler(
        CodingHarnessTuiTheme theme,
        TimeProvider? timeProvider = null)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task<AgentTuiInteractionResult> HandleAsync(
        AgentTuiInteractionContext<AgentCapabilityRequestEvent> context,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        if (IsApprovedForTurn(context))
        {
            return AgentTuiInteractionResult.AnswerRequest(new AgentCapabilityResponseEvent(
                request.RequestId,
                request.SourceName,
                Approved: true));
        }

        var response = await context.Dialogs.ShowAsync<SandboxCapabilityChoice>(
            $"agent-capability:{request.RequestId}",
            dialog => new AgentCapabilityDialogComponent(request, dialog, _theme),
            cancellationToken).ConfigureAwait(false);

        if (response.IsSubmitted && response.Value?.Decision == SandboxCapabilityDecision.AllowForTurn)
            RememberTurnGrant(context);

        return AgentTuiInteractionResult.AnswerRequest(
            new AgentCapabilityResponseEvent(
                request.RequestId,
                request.SourceName,
                response.IsSubmitted && response.Value?.Approved == true));
    }

    private bool IsApprovedForTurn(
        AgentTuiInteractionContext<AgentCapabilityRequestEvent> context)
    {
        if (TryCreateTurnKey(context, out var key) is false)
            return false;

        lock (_grantGate)
        {
            PruneExpiredGrants();
            return _turnGrants.TryGetValue(key, out var expiresAt) &&
                expiresAt > _timeProvider.GetUtcNow();
        }
    }

    private void RememberTurnGrant(
        AgentTuiInteractionContext<AgentCapabilityRequestEvent> context)
    {
        if (TryCreateTurnKey(context, out var key) is false)
            return;

        lock (_grantGate)
        {
            PruneExpiredGrants();
            _turnGrants[key] = _timeProvider.GetUtcNow() + TurnGrantLifetime;
            if (_turnGrants.Count <= MaximumTurnGrants)
                return;

            var oldest = _turnGrants.MinBy(static grant => grant.Value).Key;
            _turnGrants.Remove(oldest);
        }
    }

    private void PruneExpiredGrants()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var key in _turnGrants
                     .Where(grant => grant.Value <= now)
                     .Select(static grant => grant.Key)
                     .ToArray())
        {
            _turnGrants.Remove(key);
        }
    }

    private static bool TryCreateTurnKey(
        AgentTuiInteractionContext<AgentCapabilityRequestEvent> context,
        out CapabilityTurnKey key)
    {
        var request = context.Request;
        if (string.IsNullOrWhiteSpace(request.ThreadExecutionId))
        {
            key = default;
            return false;
        }

        key = new CapabilityTurnKey(
            context.Scope.AgentId,
            context.Scope.SessionId,
            context.Scope.ThreadId,
            request.ThreadExecutionId,
            GetGrantFamily(request.Capability));
        return true;
    }

    private static CapabilityGrantFamily GetGrantFamily(AgentCapabilityKind capability)
        => capability is AgentCapabilityKind.FilesystemRead or AgentCapabilityKind.FilesystemWrite
            ? CapabilityGrantFamily.Filesystem
            : capability switch
            {
                AgentCapabilityKind.NetworkEgress => CapabilityGrantFamily.NetworkEgress,
                AgentCapabilityKind.LocalBinding => CapabilityGrantFamily.LocalBinding,
                AgentCapabilityKind.InteractiveTerminal => CapabilityGrantFamily.InteractiveTerminal,
                AgentCapabilityKind.UnsandboxedExecution => CapabilityGrantFamily.UnsandboxedExecution,
                _ => throw new ArgumentOutOfRangeException(nameof(capability))
            };
}

internal sealed class AgentCapabilityDialogComponent : HPD.TUI.Core.Component, IFocusable
{
    private readonly AgentCapabilityRequestEvent _request;
    private readonly AgentTuiDialogContext<SandboxCapabilityChoice> _dialog;
    private readonly SelectionModel<SandboxCapabilityChoice> _choices = new();
    private readonly SelectionController<SandboxCapabilityChoice> _controller;
    private readonly SelectionView<SandboxCapabilityChoice> _view;
    private readonly CodingHarnessTuiTheme _theme;

    public AgentCapabilityDialogComponent(
        AgentCapabilityRequestEvent request,
        AgentTuiDialogContext<SandboxCapabilityChoice> dialog,
        CodingHarnessTuiTheme theme)
    {
        _request = request;
        _dialog = dialog;
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _choices.Add(new CollectionItem<SandboxCapabilityChoice>(
            "allow_once",
            new SandboxCapabilityChoice(
                SandboxCapabilityDecision.AllowOnce,
                Approved: true),
            "Allow once",
            BuildAllowDescription(request)));
        if (!string.IsNullOrWhiteSpace(request.ThreadExecutionId))
        {
            _choices.Add(new CollectionItem<SandboxCapabilityChoice>(
                "allow_for_turn",
                new SandboxCapabilityChoice(
                    SandboxCapabilityDecision.AllowForTurn,
                    Approved: true),
                BuildAllowForTurnLabel(request),
                BuildAllowForTurnDescription(request)));
        }
        _choices.Add(new CollectionItem<SandboxCapabilityChoice>(
            "deny",
            new SandboxCapabilityChoice(
                SandboxCapabilityDecision.Deny,
                Approved: false),
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
        WriteLine(
            ref output,
            "Use arrows to choose. Enter confirms. Esc denies. Turn approval remains audited.",
            _theme.ResolvePermissionDetail(context.Theme),
            maxWidth);
    }

    public override bool HandleInput(in TuiInputEvent input)
    {
        if (input.KeyEvent.Key == KeyCode.Escape)
        {
            Submit(new SandboxCapabilityChoice(
                SandboxCapabilityDecision.Deny,
                Approved: false));
            return true;
        }

        return _view.HandleInput(in input);
    }

    private void Submit(SandboxCapabilityChoice choice)
        => _dialog.Submit(choice);

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
            AgentCapabilityKind.LocalBinding => "This local binding only.",
            AgentCapabilityKind.NetworkEgress => "This network request only.",
            AgentCapabilityKind.FilesystemRead => "This file read only.",
            AgentCapabilityKind.FilesystemWrite => "This file write only.",
            AgentCapabilityKind.InteractiveTerminal => "This terminal operation only.",
            AgentCapabilityKind.UnsandboxedExecution => "This unsandboxed run only.",
            _ => "This operation only."
        };

    private static string BuildAllowForTurnLabel(AgentCapabilityRequestEvent request)
        => request.Capability is AgentCapabilityKind.FilesystemRead or AgentCapabilityKind.FilesystemWrite
            ? "Allow all files this turn"
            : request.Capability switch
            {
                AgentCapabilityKind.LocalBinding => "Allow all local bindings this turn",
                AgentCapabilityKind.NetworkEgress => "Allow all network access this turn",
                AgentCapabilityKind.InteractiveTerminal => "Allow all terminal access this turn",
                AgentCapabilityKind.UnsandboxedExecution => "Allow all unsandboxed runs this turn",
                _ => "Allow all similar requests this turn"
            };

    private static string BuildAllowForTurnDescription(AgentCapabilityRequestEvent request)
        => request.Capability is AgentCapabilityKind.FilesystemRead or AgentCapabilityKind.FilesystemWrite
            ? "Later reads and writes this turn."
            : "Later requests this turn.";

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
}

internal sealed record SandboxCapabilityChoice(
    SandboxCapabilityDecision Decision,
    bool Approved);

internal enum SandboxCapabilityDecision
{
    AllowOnce,
    AllowForTurn,
    Deny
}

internal enum CapabilityGrantFamily
{
    Filesystem,
    NetworkEgress,
    LocalBinding,
    InteractiveTerminal,
    UnsandboxedExecution
}

internal readonly record struct CapabilityTurnKey(
    string AgentId,
    string SessionId,
    string ThreadId,
    string ThreadExecutionId,
    CapabilityGrantFamily Family);
