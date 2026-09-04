using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.TUI.Content;

public sealed class ListBlock : Component, IContentBlock
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

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
    {
        var maxWidth = constraints.MaxWidth;
        var width = 0;
        for (var i = 0; i < _items.Count; i++)
        {
            var prefix = Ordered ? $"{i + 1}. " : "• ";
            width = Math.Max(width, Math.Min(maxWidth, UnicodeWidth.GetWidth(prefix) + UnicodeWidth.GetWidth(_items[i].Text)));
        }

        return new Measurement(Math.Min(width, maxWidth), Math.Min(width, maxWidth));
    }

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
    {
        var maxWidth = output.MaxWidth;
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

    public override bool HandleInput(in TuiInputEvent key)
    {
        return false;
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
