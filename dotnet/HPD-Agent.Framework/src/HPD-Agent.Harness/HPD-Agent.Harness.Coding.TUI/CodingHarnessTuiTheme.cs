using HPD.TUI.Core;
using HPD.Agent.ToolHarness.Coding.TUI.Commands;

namespace HPD.Agent.ToolHarness.Coding.TUI;

public sealed class CodingHarnessTuiTheme
{
    public static CodingHarnessTuiTheme Default { get; } = CreateDefault();

    public Style? Label { get; init; }
    public Style? Prefix { get; init; }
    public Style? Muted { get; init; }
    public Style? Text { get; init; }

    public Style? CommandRunning { get; init; }
    public Style? CommandBackgrounded { get; init; }
    public Style? CommandCompleted { get; init; }
    public Style? CommandFailed { get; init; }
    public Style? CommandCancelled { get; init; }
    public Style? CommandOutput { get; init; }
    public Style? CommandErrorOutput { get; init; }

    public Style? DiffAdded { get; init; }
    public Style? DiffRemoved { get; init; }
    public Style? DiffContext { get; init; }
    public Style? DiffGutter { get; init; }

    public Style? DiagnosticError { get; init; }
    public Style? DiagnosticWarning { get; init; }

    public Style? PermissionTitle { get; init; }
    public Style? PermissionDetail { get; init; }
    public Style? PermissionCommand { get; init; }
    public Style? PermissionSelected { get; init; }

    public Style ResolveLabel(Theme theme)
        => Label ?? new Style(Color.Default, Color.Default, TextAttributes.Bold);

    public Style ResolvePrefix(Theme theme)
        => Prefix ?? theme.Border;

    public Style ResolveMuted(Theme theme)
        => Muted ?? theme.Border;

    public Style ResolveText(Theme theme)
        => Text ?? theme.Text;

    public Style ResolveCommandState(CodingCommandTranscriptState state, Theme theme)
        => state switch
        {
            CodingCommandTranscriptState.Completed => CommandCompleted ?? theme.Success,
            CodingCommandTranscriptState.Failed or CodingCommandTranscriptState.TimedOut => CommandFailed ?? theme.Error,
            CodingCommandTranscriptState.Cancelled => CommandCancelled ?? theme.Warning,
            CodingCommandTranscriptState.Backgrounded => CommandBackgrounded ?? theme.Accent,
            CodingCommandTranscriptState.Running => CommandRunning ?? theme.Border,
            _ => CommandOutput ?? theme.Border
        };

    public Style ResolveCommandOutput(Theme theme)
        => CommandOutput ?? theme.Border;

    public Style ResolveCommandErrorOutput(Theme theme)
        => CommandErrorOutput ?? theme.Warning;

    public Style ResolveDiffAdded(Theme theme, Color background)
        => DiffAdded ?? new Style(theme.Success.Foreground, background);

    public Style ResolveDiffRemoved(Theme theme, Color background)
        => DiffRemoved ?? new Style(theme.Error.Foreground, background);

    public Style ResolveDiffContext(Theme theme)
        => DiffContext ?? theme.Text;

    public Style ResolveDiffGutter(Theme theme, Color background)
        => DiffGutter ?? (background.IsDefault ? theme.Border : new Style(theme.Border.Foreground, background));

    public Style ResolveDiagnosticError(Theme theme)
        => DiagnosticError ?? theme.Error;

    public Style ResolveDiagnosticWarning(Theme theme)
        => DiagnosticWarning ?? theme.Warning;

    public Style ResolvePermissionTitle(Theme theme)
        => PermissionTitle ?? theme.Accent;

    public Style ResolvePermissionDetail(Theme theme)
        => PermissionDetail ?? theme.Border;

    public Style ResolvePermissionCommand(Theme theme)
        => PermissionCommand ?? theme.Text;

    public Style ResolvePermissionSelected(Theme theme)
        => PermissionSelected ?? theme.Text;

    private static CodingHarnessTuiTheme CreateDefault()
        => new()
        {
            Label = new Style(new Color(255, 205, 105), Color.Default, TextAttributes.Bold),
            Prefix = new Style(new Color(120, 190, 170), Color.Default),
            Muted = new Style(new Color(120, 128, 145), Color.Default),
            Text = new Style(new Color(218, 222, 232), Color.Default),

            CommandRunning = new Style(new Color(255, 198, 109), Color.Default),
            CommandBackgrounded = new Style(new Color(170, 145, 255), Color.Default),
            CommandCompleted = new Style(new Color(108, 210, 140), Color.Default),
            CommandFailed = new Style(new Color(255, 105, 120), Color.Default),
            CommandCancelled = new Style(new Color(155, 160, 172), Color.Default),
            CommandOutput = new Style(new Color(206, 211, 222), Color.Default),
            CommandErrorOutput = new Style(new Color(255, 135, 125), Color.Default),

            DiffAdded = new Style(new Color(92, 200, 125), new Color(24, 62, 38)),
            DiffRemoved = new Style(new Color(245, 105, 115), new Color(82, 34, 30)),
            DiffContext = new Style(new Color(170, 177, 190), Color.Default),

            DiagnosticError = new Style(new Color(255, 95, 110), Color.Default),
            DiagnosticWarning = new Style(new Color(255, 205, 105), Color.Default),

            PermissionTitle = new Style(new Color(255, 198, 109), Color.Default),
            PermissionDetail = new Style(new Color(185, 192, 205), Color.Default),
            PermissionCommand = new Style(new Color(140, 205, 255), Color.Default),
            PermissionSelected = new Style(new Color(0, 0, 0), new Color(255, 198, 109))
        };
}
