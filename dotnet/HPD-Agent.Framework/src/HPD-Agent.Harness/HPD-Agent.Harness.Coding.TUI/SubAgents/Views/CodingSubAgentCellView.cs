using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.Agent.ToolHarness.Coding.TUI.SubAgents.Views;

internal sealed class CodingSubAgentCellView(CodingSubAgentCell cell, CodingHarnessTuiTheme theme) : HPD.TUI.Core.Component
{
    private readonly CodingSubAgentCell _cell = cell ?? throw new ArgumentNullException(nameof(cell));
    private readonly CodingHarnessTuiTheme _theme = theme ?? throw new ArgumentNullException(nameof(theme));

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
        => new(1, Math.Min(constraints.MaxWidth, 100), string.IsNullOrWhiteSpace(_cell.Detail)
            ? 1
            : 1 + Wrap(_cell.Detail, Math.Max(1, constraints.MaxWidth - 4), 3).Count);

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
    {
        var maxWidth = output.MaxWidth;
        if (maxWidth <= 0) return;

        var metadata = new List<string> { StateText(_cell.State) };
        if (_cell.ContextPolicy is { } policy) metadata.Add(policy.ToString().ToLowerInvariant());
        if (_cell.Mode is { } mode) metadata.Add(mode.ToString().ToLowerInvariant());

        output.Write("  ├ ".AsSpan(), _theme.ResolvePrefix(context.Theme));
        output.Write(string.Join(" · ", metadata).AsSpan(), _theme.ResolveMuted(context.Theme));
        if (!string.IsNullOrWhiteSpace(_cell.Detail))
        {
            var rows = Wrap(_cell.Detail, Math.Max(1, maxWidth - 4), 3);
            for (var index = 0; index < rows.Count; index++)
            {
                output.WriteLineBreak();
                output.Write((index == 0 ? "  └ " : "    ").AsSpan(), _theme.ResolvePrefix(context.Theme));
                output.Write(rows[index].AsSpan(), _theme.ResolveText(context.Theme));
            }
        }
    }

    public override bool HandleInput(in TuiInputEvent input) => false;

    private static string StateText(CodingSubAgentState state) => state.ToString().ToLowerInvariant();

    private static IReadOnlyList<string> Wrap(string text, int width, int maxRows)
    {
        var rows = new List<string>();
        var current = new System.Text.StringBuilder();
        var currentWidth = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var runeWidth = Math.Max(0, UnicodeWidth.GetWidth(rune));
            if (current.Length > 0 && currentWidth + runeWidth > width)
            {
                rows.Add(current.ToString());
                current.Clear();
                currentWidth = 0;
                if (rows.Count == maxRows) break;
            }

            current.Append(rune.ToString());
            currentWidth += runeWidth;
        }

        if (rows.Count < maxRows && current.Length > 0) rows.Add(current.ToString());
        if (rows.Count == maxRows && string.Concat(rows).Length < text.Length)
            rows[^1] = rows[^1].Length == 0 ? "…" : $"{rows[^1][..^1]}…";
        return rows;
    }
}
