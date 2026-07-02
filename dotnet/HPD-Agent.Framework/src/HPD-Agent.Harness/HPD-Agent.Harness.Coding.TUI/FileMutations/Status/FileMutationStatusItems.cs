using HPD.Agent.TUI.Composition;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.FileMutations.Status;

internal sealed class FileMutationStatusItem : IAgentTuiStatusItem
{
    private readonly CodingHarnessTuiTheme _theme;

    public FileMutationStatusItem(CodingHarnessTuiTheme theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    public IComponent Create(AgentTuiStatusContext context)
        => new FileMutationStatusComponent(context.State, _theme);
}

internal sealed class CodingDiagnosticsStatusItem : IAgentTuiStatusItem
{
    private readonly CodingHarnessTuiTheme _theme;

    public CodingDiagnosticsStatusItem(CodingHarnessTuiTheme theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    public IComponent Create(AgentTuiStatusContext context)
        => new CodingDiagnosticsStatusComponent(context.State, _theme);
}

internal abstract class CodingFileStatusComponentBase : IComponent
{
    protected CodingFileStatusComponentBase(AgentTuiStateBag state, CodingHarnessTuiTheme theme)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        Theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    protected AgentTuiStateBag State { get; }
    protected CodingHarnessTuiTheme Theme { get; }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var text = BuildText();
        return string.IsNullOrEmpty(text)
            ? new Measurement(0, 0, 0)
            : new Measurement(Math.Min(text.Length, maxWidth), Math.Min(text.Length, maxWidth), 1);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        var text = BuildText();
        if (string.IsNullOrEmpty(text) || maxWidth <= 0)
        {
            return;
        }

        output.Write(Clip(text, maxWidth).AsSpan(), Theme.ResolveMuted(context.Theme));
    }

    public bool HandleInput(in TuiInputEvent input)
    {
        return false;
    }

    protected abstract string BuildText();

    private static string Clip(string text, int maxWidth)
    {
        if (text.Length <= maxWidth)
        {
            return text;
        }

        if (maxWidth <= 3)
        {
            return new string('.', maxWidth);
        }

        return string.Concat(text.AsSpan(0, maxWidth - 3), "...");
    }
}

internal sealed class FileMutationStatusComponent : CodingFileStatusComponentBase
{
    public FileMutationStatusComponent(AgentTuiStateBag state, CodingHarnessTuiTheme theme)
        : base(state, theme)
    {
    }

    protected override string BuildText()
    {
        if (!State.TryGet<FileMutationTuiState>(FileMutationTuiState.StateKey, out var state) ||
            state.MutationCount == 0)
        {
            return "";
        }

        return state.ChangedPathCount <= 1
            ? $"files +{state.AddedLines} -{state.RemovedLines}"
            : $"changed {state.ChangedPathCount} files +{state.AddedLines} -{state.RemovedLines}";
    }
}

internal sealed class CodingDiagnosticsStatusComponent : CodingFileStatusComponentBase
{
    public CodingDiagnosticsStatusComponent(AgentTuiStateBag state, CodingHarnessTuiTheme theme)
        : base(state, theme)
    {
    }

    protected override string BuildText()
    {
        if (!State.TryGet<CodingDiagnosticsTuiState>(CodingDiagnosticsTuiState.StateKey, out var state))
        {
            return "";
        }

        if (state.ErrorCount == 0 && state.WarningCount == 0)
        {
            return "";
        }

        var parts = new List<string>();
        if (state.ErrorCount > 0)
        {
            parts.Add($"{state.ErrorCount}E");
        }

        if (state.WarningCount > 0)
        {
            parts.Add($"{state.WarningCount}W");
        }

        return $"diag {string.Join(" ", parts)}";
    }
}
