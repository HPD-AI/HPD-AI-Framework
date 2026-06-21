using HPD.Agent.TUI.Composition;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.FileMutations.Status;

internal sealed class FileMutationStatusItem : IAgentTuiStatusItem
{
    public IComponent Create(AgentTuiStatusContext context)
        => new FileMutationStatusComponent(context.State);
}

internal sealed class CodingDiagnosticsStatusItem : IAgentTuiStatusItem
{
    public IComponent Create(AgentTuiStatusContext context)
        => new CodingDiagnosticsStatusComponent(context.State);
}

internal abstract class CodingFileStatusComponentBase : IComponent
{
    protected CodingFileStatusComponentBase(AgentTuiStateBag state)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    protected AgentTuiStateBag State { get; }

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

        output.Write(Clip(text, maxWidth).AsSpan(), context.Theme.Border);
    }

    public void HandleInput(in KeyEvent key)
    {
    }

    public void Invalidate()
    {
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
    public FileMutationStatusComponent(AgentTuiStateBag state)
        : base(state)
    {
    }

    protected override string BuildText()
    {
        if (!State.TryGet<FileMutationTuiState>(FileMutationTuiState.StateKey, out var state) ||
            state.MutationCount == 0)
        {
            return "";
        }

        return state.MutationCount == 1
            ? $"files +{state.AddedLines} -{state.RemovedLines}"
            : $"changed {state.MutationCount} files +{state.AddedLines} -{state.RemovedLines}";
    }
}

internal sealed class CodingDiagnosticsStatusComponent : CodingFileStatusComponentBase
{
    public CodingDiagnosticsStatusComponent(AgentTuiStateBag state)
        : base(state)
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
