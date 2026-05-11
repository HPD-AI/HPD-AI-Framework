namespace HPD.TUI.Core;

public ref struct SegmentWriter
{
    private readonly ISegmentSink _sink;
    private int _count;

    public SegmentWriter(ISegmentSink sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _count = 0;
    }

    public readonly int Count => _count;

    public int CursorX => _sink.CursorX;

    public int CursorY => _sink.CursorY;

    public bool Write(scoped ReadOnlySpan<char> text, Style style)
    {
        _count++;
        return _sink.Write(text, style);
    }

    public bool WriteLineBreak()
    {
        _count++;
        return _sink.WriteLineBreak();
    }

    public void MoveTo(int x, int y)
    {
        _sink.MoveTo(x, y);
    }

    public void SetTerminalCursor(int x, int y)
    {
        _sink.SetTerminalCursor(x, y);
    }
}

public interface ISegmentSink
{
    int CursorX { get; }

    int CursorY { get; }

    bool Write(scoped ReadOnlySpan<char> text, Style style);

    bool WriteLineBreak();

    void MoveTo(int x, int y);

    void SetTerminalCursor(int x, int y);
}
