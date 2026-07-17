using System.Text;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Interactions;
using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Utilities;
using HPD.TUI.Views;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands.Handlers;

public sealed class ExecuteCommandPermissionRequestTuiHandler :
    AgentTuiInteractionHandler<ExecuteCommandPermissionRequestEvent>
{
    private readonly CodingHarnessTuiTheme _theme;

    public ExecuteCommandPermissionRequestTuiHandler(CodingHarnessTuiTheme theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    protected override async Task<AgentTuiInteractionResult> HandleAsync(
        AgentTuiInteractionContext<ExecuteCommandPermissionRequestEvent> context,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        context.Shell.FooterText = $"state: awaiting permission | ExecuteCommand";
        var response = await context.Dialogs.ShowAsync<ExecuteCommandPermissionResponseEvent>(
            $"execute-command-permission:{request.PermissionId}",
            dialog => new ExecuteCommandPermissionDialogComponent(request, dialog, _theme),
            cancellationToken).ConfigureAwait(false);
        context.Shell.FooterText = "state: running | press Esc twice to cancel";

        return AgentTuiInteractionResult.AnswerRequest(
            response.IsSubmitted && response.Value is not null
                ? response.Value
                : CreateDeniedResponse(request));
    }

    private static ExecuteCommandPermissionResponseEvent CreateDeniedResponse(
        ExecuteCommandPermissionRequestEvent request)
        => new(
            request.PermissionId,
            request.SourceName,
            "deny");
}

internal sealed class ExecuteCommandPermissionDialogComponent : IFocusable
{
    private readonly ExecuteCommandPermissionRequestEvent _request;
    private readonly AgentTuiDialogContext<ExecuteCommandPermissionResponseEvent> _dialog;
    private readonly SelectionModel<ExecuteCommandPermissionChoice> _choices;
    private readonly SelectionController<ExecuteCommandPermissionChoice> _choiceController;
    private readonly SelectionView<ExecuteCommandPermissionChoice> _choiceView;
    private readonly StringBuilder _feedback = new();
    private readonly CodingHarnessTuiTheme _theme;
    private bool _feedbackMode;
    private string? _validationMessage;

    public ExecuteCommandPermissionDialogComponent(
        ExecuteCommandPermissionRequestEvent request,
        AgentTuiDialogContext<ExecuteCommandPermissionResponseEvent> dialog,
        CodingHarnessTuiTheme theme)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _choices = CreateChoiceModel(request.AvailableChoices);
        _choiceController = new SelectionController<ExecuteCommandPermissionChoice>(_choices)
        {
            Submitted = item => SubmitChoice(item.Value)
        };
        _choiceView = new SelectionView<ExecuteCommandPermissionChoice>(_choices, _choiceController);
    }

    public bool IsFocused
    {
        get => _choiceView.IsFocused;
        set => _choiceView.IsFocused = value;
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var detailRows = BuildCommandLines(_request).Count + BuildSecurityReviewLines(_request).Count;
        var choiceRows = Math.Max(1, _choices.VisibleCount);
        var feedbackRows = _feedbackMode ? 3 : 0;
        var validationRows = string.IsNullOrWhiteSpace(_validationMessage) ? 0 : 1;
        return new Measurement(
            Math.Min(maxWidth, 24),
            Math.Min(maxWidth, 96),
            detailRows + choiceRows + feedbackRows + validationRows + 9);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        WriteLine(ref output, "Approve command?", _theme.ResolvePermissionTitle(context.Theme), maxWidth);
        output.WriteLineBreak();

        WriteLine(ref output, $"Reason: {BuildReason(_request.Plan)}", _theme.ResolvePermissionDetail(context.Theme), maxWidth);
        output.WriteLineBreak();

        foreach (var line in BuildCommandLines(_request))
        {
            var style = line.StartsWith("$ ", StringComparison.Ordinal)
                ? _theme.ResolvePermissionCommand(context.Theme)
                : _theme.ResolvePermissionDetail(context.Theme);
            WriteLine(ref output, line, style, maxWidth);
        }

        output.WriteLineBreak();
        WriteLine(ref output, "Security review", _theme.ResolvePermissionTitle(context.Theme), maxWidth);
        foreach (var line in BuildSecurityReviewLines(_request))
        {
            WriteLine(ref output, line, _theme.ResolvePermissionDetail(context.Theme), maxWidth);
        }

        output.WriteLineBreak();
        _choiceView.Render(in context, maxWidth, ref output);
        output.WriteLineBreak();
        output.WriteLineBreak();

        if (_feedbackMode)
        {
            WriteLine(ref output, "Feedback for the agent:", _theme.ResolvePermissionTitle(context.Theme), maxWidth);
            WriteLine(ref output, _feedback.Length == 0 ? "_" : _feedback.ToString(), _theme.ResolvePermissionCommand(context.Theme), maxWidth);
            WriteLine(ref output, "Enter submits feedback. Backspace edits. Esc cancels.", _theme.ResolvePermissionDetail(context.Theme), maxWidth);
        }
        else
        {
            WriteLine(ref output, "Use arrows to choose. Enter confirms. Esc denies.", _theme.ResolvePermissionDetail(context.Theme), maxWidth);
        }

        if (!string.IsNullOrWhiteSpace(_validationMessage))
        {
            output.WriteLineBreak();
            WriteLine(ref output, _validationMessage, _theme.ResolveDiagnosticWarning(context.Theme), maxWidth);
        }
    }

    public bool HandleInput(in TuiInputEvent input)
    {
        var key = input.KeyEvent;
        if (_feedbackMode)
        {
            HandleFeedbackInput(in key);
            return true;
        }

        return _choiceView.HandleInput(in input);
    }

    private void SubmitChoice(ExecuteCommandPermissionChoice choice)
    {
        if (choice is FeedbackChoice)
        {
            _feedbackMode = true;
            _validationMessage = null;
            return;
        }

        _dialog.Submit(new ExecuteCommandPermissionResponseEvent(
            _request.PermissionId,
            _request.SourceName,
            choice.Id));
    }

    private void HandleFeedbackInput(in KeyEvent key)
    {
        switch (key.Key)
        {
            case KeyCode.Enter:
                SubmitFeedback();
                return;
            case KeyCode.Backspace:
                RemoveLastFeedbackRune();
                _validationMessage = null;
                return;
            case KeyCode.Character:
                AppendFeedbackRune(key.Character);
                _validationMessage = null;
                return;
            case KeyCode.Paste when !string.IsNullOrEmpty(key.Text):
                _feedback.Append(key.Text);
                _validationMessage = null;
                return;
        }
    }

    private void SubmitFeedback()
    {
        var feedback = _feedback.ToString().Trim();
        if (string.IsNullOrWhiteSpace(feedback))
        {
            _validationMessage = "Feedback is required.";
            return;
        }

        var choice = _request.AvailableChoices.OfType<FeedbackChoice>().First();
        _dialog.Submit(new ExecuteCommandPermissionResponseEvent(
            _request.PermissionId,
            _request.SourceName,
            choice.Id,
            feedback));
    }

    private void AppendFeedbackRune(Rune rune)
    {
        Span<char> buffer = stackalloc char[2];
        if (rune.TryEncodeToUtf16(buffer, out var written))
        {
            _feedback.Append(buffer[..written]);
        }
    }

    private void RemoveLastFeedbackRune()
    {
        if (_feedback.Length == 0)
        {
            return;
        }

        var removeAt = _feedback.Length - 1;
        if (removeAt > 0 && char.IsLowSurrogate(_feedback[removeAt]) && char.IsHighSurrogate(_feedback[removeAt - 1]))
        {
            removeAt--;
        }

        _feedback.Remove(removeAt, _feedback.Length - removeAt);
    }

    private static SelectionModel<ExecuteCommandPermissionChoice> CreateChoiceModel(
        IReadOnlyList<ExecuteCommandPermissionChoice> choices)
    {
        var model = new SelectionModel<ExecuteCommandPermissionChoice>();
        foreach (var choice in choices)
        {
            model.Add(
                new CollectionItem<ExecuteCommandPermissionChoice>(
                    choice.Id,
                    choice,
                    choice.Label,
                    choice.Description,
                    category: GetChoiceCategory(choice)));
        }

        return model;
    }

    private static string GetChoiceCategory(ExecuteCommandPermissionChoice choice)
        => choice switch
        {
            AllowOnceChoice => "Allow once",
            PersistRuleChoice => "Remember",
            DenyChoice => "Deny",
            FeedbackChoice => "Feedback",
            _ => "Other"
        };

    private static string BuildReason(ExecuteCommandPermissionPlan plan)
        => plan.StartsInBackground
            ? "start a background shell command"
            : plan.Action switch
            {
                ExecuteCommandAction.Run => "run a shell command",
                ExecuteCommandAction.ListBackground => "list background commands",
                ExecuteCommandAction.ReadOutput => "read background command output",
                ExecuteCommandAction.Stop => "stop a background command",
                _ => "use ExecuteCommand"
            };

    private static IReadOnlyList<string> BuildCommandLines(ExecuteCommandPermissionRequestEvent request)
    {
        var plan = request.Plan;
        return
        [
            $"$ {plan.NormalizedCommand.Value}",
            $"cwd: {plan.WorkingDirectory}"
        ];
    }

    private static IReadOnlyList<string> BuildSecurityReviewLines(ExecuteCommandPermissionRequestEvent request)
    {
        var plan = request.Plan;
        var details = new List<string>
        {
            $"sandbox: {FormatSandbox(plan)}",
            $"effects: {FormatEffects(plan)}",
            $"risk: {FormatRisk(plan.Risk)}",
            $"analysis: {FormatAnalysis(plan)}",
            $"rule: {FormatRuleStatus(request)}"
        };

        if (plan.StartsInBackground)
        {
            details.Add("mode: background command");
        }

        if (!string.IsNullOrWhiteSpace(plan.AnalysisWarning))
        {
            details.Add($"warning: {plan.AnalysisWarning}");
        }

        if (plan.FilesystemEffects.Count > 0)
        {
            details.Add($"filesystem detail: {FormatFilesystemEffects(plan.FilesystemEffects)}");
        }

        if (plan.NetworkEffects.Count > 0)
        {
            details.Add($"network detail: {FormatNetworkEffects(plan.NetworkEffects)}");
        }

        if (plan is SimpleCommandPermissionPlan simple)
        {
            details.Add($"command base: {simple.CommandPlan.BaseCommand}");
            if (!string.IsNullOrWhiteSpace(simple.CommandPlan.SafePrefix))
            {
                details.Add($"allow prefix: {simple.CommandPlan.SafePrefix}");
            }
        }
        else if (plan is SegmentedCommandPermissionPlan segmented)
        {
            details.Add($"segments: {segmented.Segments.Count}");
        }
        else if (plan is NonRunCommandPermissionPlan nonRun)
        {
            details.Add($"policy: {nonRun.PolicyReason}");
        }
        else if (plan is UntrustedCommandPermissionPlan untrusted)
        {
            details.Add($"reason: {untrusted.FailureReason}");
        }

        return details;
    }

    private static string FormatEffects(ExecuteCommandPermissionPlan plan)
    {
        if (plan.FilesystemEffects.Count == 0 && plan.NetworkEffects.Count == 0)
        {
            return "no filesystem or network effects detected";
        }

        var parts = new List<string>();
        if (plan.FilesystemEffects.Count > 0)
        {
            parts.Add(FormatFilesystemEffects(plan.FilesystemEffects));
        }

        if (plan.NetworkEffects.Count > 0)
        {
            parts.Add(FormatNetworkEffects(plan.NetworkEffects));
        }

        return string.Join("; ", parts);
    }

    private static string FormatAnalysis(ExecuteCommandPermissionPlan plan)
        => string.IsNullOrWhiteSpace(plan.AnalysisWarning)
            ? plan.TrustLevel.ToString()
            : $"{plan.TrustLevel}, warning";

    private static string FormatRuleStatus(ExecuteCommandPermissionRequestEvent request)
        => request.MatchingRules.Count == 0
            ? "no saved rule matched"
            : request.MatchingRules.Count == 1
                ? "1 saved rule matched"
                : $"{request.MatchingRules.Count} saved rules matched";

    private static string FormatSandbox(ExecuteCommandPermissionPlan plan)
        => plan.RequestedSandbox.Mode == ExecuteCommandIsolationMode.Disabled
            ? "disabled"
            : plan.Risk.HasFlag(ExecuteCommandPermissionRisk.AdditionalSandboxPermissions)
                ? $"{plan.RequestedSandbox.Mode} plus requested access"
                : plan.RequestedSandbox.Mode.ToString();

    private static string FormatRisk(ExecuteCommandPermissionRisk risk)
        => risk == ExecuteCommandPermissionRisk.None
            ? "none"
            : risk.ToString();

    private static string FormatFilesystemEffects(IReadOnlyList<ExecuteCommandFilesystemEffect> effects)
    {
        var outsideSandbox = effects.Count(static effect => !effect.CoveredBySandbox);
        var mutation = effects.Count(static effect => effect.Operation != ExecuteCommandFilesystemOperation.Read);
        return outsideSandbox == 0
            ? $"{effects.Count} effect(s), covered by sandbox"
            : $"{effects.Count} effect(s), {outsideSandbox} outside sandbox, {mutation} mutation(s)";
    }

    private static string FormatNetworkEffects(IReadOnlyList<ExecuteCommandNetworkEffect> effects)
    {
        var outsideSandbox = effects.Count(static effect => !effect.CoveredBySandbox);
        return outsideSandbox == 0
            ? $"{effects.Count} effect(s), covered by sandbox"
            : $"{effects.Count} effect(s), {outsideSandbox} outside sandbox";
    }

    private static void WriteLine(
        ref SegmentWriter output,
        string value,
        Style style,
        int maxWidth)
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
        {
            return;
        }

        var used = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var width = Math.Max(0, UnicodeWidth.GetWidth(rune));
            if (used > 0 && used + width > maxWidth)
            {
                return;
            }

            output.Write(rune.ToString().AsSpan(), style);
            used += width;
        }
    }
}
