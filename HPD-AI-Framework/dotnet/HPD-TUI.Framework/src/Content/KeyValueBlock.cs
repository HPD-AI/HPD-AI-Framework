using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.TUI.Content;

public sealed class KeyValueBlock : IContentBlock
{
    private readonly List<KeyValueEntry> _entries = [];

    public ContentBlockKind Kind => ContentBlockKind.KeyValue;

    public IReadOnlyList<KeyValueEntry> Entries => _entries;

    public KeyValueBlock Add(string key, string value, Style? valueStyle = null)
    {
        _entries.Add(new KeyValueEntry(key, value, valueStyle));
        return this;
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var width = 0;
        foreach (var entry in _entries)
        {
            width = Math.Max(width, Math.Min(maxWidth, UnicodeWidth.GetWidth(entry.Key) + UnicodeWidth.GetWidth(entry.Value) + 2));
        }

        return new Measurement(Math.Min(width, maxWidth), Math.Min(width, maxWidth));
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
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

    public void HandleInput(in KeyEvent key)
    {
    }

    public void Invalidate()
    {
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
