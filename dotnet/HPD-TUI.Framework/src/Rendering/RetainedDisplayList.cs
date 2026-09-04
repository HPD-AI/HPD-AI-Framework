using HPD.TUI.Core;

namespace HPD.TUI.Rendering;

internal sealed class RetainedDisplayList : ISegmentSink
{
    private readonly List<DisplayOperation> _operations = [];
    private DisplayListKey _key;
    private int _cursorX;
    private int _cursorY;

    public int CursorX => _cursorX;
    public int CursorY => _cursorY;
    public int Count => _operations.Count;

    public bool Prepare(IComponent root, in RenderContext context, int maxWidth)
    {
        var dependencies = RenderContextFields.None;
        var key = new DisplayListKey(
            ComputeTreeRevision(root, ref dependencies),
            context.Width,
            context.Height,
            context.Theme.Key,
            context.ColorSystem,
            (dependencies & RenderContextFields.Elapsed) != 0 ? context.Elapsed : default);
        if (_operations.Count > 0 && key == _key) return true;

        _operations.Clear();
        _cursorX = 0;
        _cursorY = 0;
        var writer = new DisplayListBuilder(this, maxWidth);
        root.Render(in context, ref writer);
        _key = key;
        return false;
    }

    public void Replay(ISegmentSink destination)
    {
        foreach (var operation in _operations)
        {
            switch (operation.Kind)
            {
                case DisplayOperationKind.Text:
                    destination.Write(operation.Text.AsSpan(), operation.Style, operation.Metadata);
                    break;
                case DisplayOperationKind.LineBreak:
                    destination.WriteLineBreak();
                    break;
                case DisplayOperationKind.Move:
                    destination.MoveTo(operation.X, operation.Y);
                    break;
                case DisplayOperationKind.Cursor:
                    destination.SetTerminalCursor(operation.X, operation.Y);
                    break;
            }
        }
    }

    public bool Write(scoped ReadOnlySpan<char> text, Style style, TerminalRunMetadata metadata = default)
    {
        _operations.Add(new DisplayOperation(DisplayOperationKind.Text, text.ToString(), style, metadata, 0, 0));
        _cursorX += Utilities.UnicodeWidth.GetWidth(text);
        return true;
    }

    public bool WriteLineBreak()
    {
        _operations.Add(new DisplayOperation(DisplayOperationKind.LineBreak, string.Empty, default, default, 0, 0));
        _cursorX = 0;
        _cursorY++;
        return true;
    }

    public void MoveTo(int x, int y)
    {
        _operations.Add(new DisplayOperation(DisplayOperationKind.Move, string.Empty, default, default, x, y));
        _cursorX = x;
        _cursorY = y;
    }

    public void SetTerminalCursor(int x, int y) =>
        _operations.Add(new DisplayOperation(DisplayOperationKind.Cursor, string.Empty, default, default, x, y));

    private static ulong ComputeTreeRevision(IComponent component, ref RenderContextFields dependencies)
    {
        var hash = new HashCode();
        Add(component, ref hash, ref dependencies);
        return unchecked((ulong)hash.ToHashCode());

        static void Add(IComponent current, ref HashCode hash, ref RenderContextFields dependencies)
        {
            hash.Add(current.Lifecycle.Id.Value);
            hash.Add(current.LayoutRevision.Value);
            hash.Add(current.PaintRevision.Value);
            dependencies |= current.Dependencies.Layout | current.Dependencies.Paint;
            if (current is not Component owner) return;
            foreach (var child in owner.OwnedChildren) Add(child, ref hash, ref dependencies);
        }
    }

    private readonly record struct DisplayListKey(
        ulong TreeRevision,
        int Width,
        int Height,
        ThemeKey Theme,
        ColorSystem ColorSystem,
        TimeSpan Elapsed);

    private readonly record struct DisplayOperation(
        DisplayOperationKind Kind,
        string Text,
        Style Style,
        TerminalRunMetadata Metadata,
        int X,
        int Y);

    private enum DisplayOperationKind { Text, LineBreak, Move, Cursor }
}
