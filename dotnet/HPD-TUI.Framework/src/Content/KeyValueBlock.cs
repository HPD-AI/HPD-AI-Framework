using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.TUI.Content;

public sealed class KeyValueBlock : Component, IContentBlock
{
    private readonly List<KeyValueEntry> _entries = [];

    public ContentBlockKind Kind => ContentBlockKind.KeyValue;

    public IReadOnlyList<KeyValueEntry> Entries => _entries;

    public KeyValueBlock Add(string key, string value, Style? valueStyle = null)
    {
        _entries.Add(new KeyValueEntry(key, value, valueStyle));
        return this;
    }

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
    {
        var maxWidth = constraints.MaxWidth;
        var width = 0;
        foreach (var entry in _entries)
        {
            width = Math.Max(width, Math.Min(maxWidth, UnicodeWidth.GetWidth(entry.Key) + UnicodeWidth.GetWidth(entry.Value) + 2));
        }

        return new Measurement(Math.Min(width, maxWidth), Math.Min(width, maxWidth));
    }

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
    {
        var maxWidth = output.MaxWidth;
        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            output.Write(entry.Key.AsSpan(), context.Theme.Accent);
            output.Write(": ", context.Theme.Border);
            output.Write(entry.Value.AsSpan(), entry.ValueStyle ?? context.Theme.Text);

            if (i < _entries.Count - 1)
            {
                output.WriteLineBreak();
            }
        }
    }

    public override bool HandleInput(in TuiInputEvent key)
    {
        return false;
    }

    public static KeyValueBlock Create(params KeyValueEntry[] entries)
    {
        var block = new KeyValueBlock();
        foreach (var entry in entries)
        {
            block._entries.Add(entry);
        }

        return block;
    }
}

public readonly record struct KeyValueEntry(string Key, string Value, Style? ValueStyle = null);
