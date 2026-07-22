using HPD.Agent.TUI.Models;
using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.Agent.TUI.Views;

internal sealed class PendingPromptPreview(PendingPromptQueue queue) : IComponent
{
    private const int MaxVisibleItems = 3;
    private readonly PendingPromptQueue _queue = queue ?? throw new ArgumentNullException(nameof(queue));

    public Measurement Measure(in RenderContext context, int maxWidth)
        => _queue.Count == 0 ? new Measurement(0, 0, 0) : new Measurement(1, Math.Min(maxWidth, 100), Height());

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        if (maxWidth <= 0 || _queue.Count == 0) return;
        var items = _queue.Snapshot();
        output.Write("• Queued follow-ups".AsSpan(), context.Theme.Accent);
        foreach (var item in items.Take(MaxVisibleItems))
        {
            output.WriteLineBreak();
            output.Write("  ↳ ".AsSpan(), context.Theme.Border);
            output.Write(Truncate(item.Text, Math.Max(1, maxWidth - 4)).AsSpan(), context.Theme.Text);
        }

        if (items.Count > MaxVisibleItems)
        {
            output.WriteLineBreak();
            output.Write($"    … {items.Count - MaxVisibleItems} more".AsSpan(), context.Theme.Border);
        }

        output.WriteLineBreak();
        output.Write("  Alt+↑ edit latest · Esc steer next".AsSpan(), context.Theme.Border);
    }

    public bool HandleInput(in TuiInputEvent input) => false;

    private int Height() => 2 + Math.Min(_queue.Count, MaxVisibleItems) + (_queue.Count > MaxVisibleItems ? 1 : 0);

    private static string Truncate(string value, int width)
    {
        var result = new System.Text.StringBuilder();
        var used = 0;
        foreach (var rune in string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).EnumerateRunes())
        {
            var runeWidth = Math.Max(0, UnicodeWidth.GetWidth(rune));
            if (used + runeWidth > width)
            {
                result.Append('…');
                break;
            }

            result.Append(rune.ToString());
            used += runeWidth;
        }

        return result.ToString();
    }
}
