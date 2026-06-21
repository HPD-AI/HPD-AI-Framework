using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.TUI.Content;

public sealed class ListBlock : IContentBlock
{
    private readonly List<ListBlockItem> _items = [];

    public ContentBlockKind Kind => ContentBlockKind.List;

    public IReadOnlyList<ListBlockItem> Items => _items;

    public bool Ordered { get; init; }

    public ListBlock Add(string text, Style? style = null)
    {
        _items.Add(new ListBlockItem(text, style));
        return this;
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var width = 0;
        for (var i = 0; i < _items.Count; i++)
        {
            var prefix = Ordered ? $"{i + 1}. " : "• ";
            width = Math.Max(width, Math.Min(maxWidth, UnicodeWidth.GetWidth(prefix) + UnicodeWidth.GetWidth(_items[i].Text)));
        }

        return new Measurement(Math.Min(width, maxWidth), Math.Min(width, maxWidth));
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (Ordered)
            {
                output.Write((i + 1).ToString().AsSpan(), context.Theme.Accent);
                output.Write(". ", context.Theme.Accent);
            }
            else
            {
                output.Write("• ", context.Theme.Border);
            }

            output.Write(item.Text.AsSpan(), item.Style ?? context.Theme.Text);

            if (i < _items.Count - 1)
            {
                output.WriteLineBreak();
            }
        }
    }

    public void HandleInput(in KeyEvent key)
    {
    }

    public void Invalidate()
    {
    }

    public static ListBlock Create(IEnumerable<string> items, bool ordered = false)
    {
        ArgumentNullException.ThrowIfNull(items);

        var block = new ListBlock { Ordered = ordered };
        foreach (var item in items)
        {
            block.Add(item);
        }

        return block;
    }
}

public readonly record struct ListBlockItem(string Text, Style? Style = null);
