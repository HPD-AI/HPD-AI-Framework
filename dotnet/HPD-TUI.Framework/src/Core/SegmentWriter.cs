using System.Text;

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

    public ISegmentSink Sink => _sink;

    public int CursorX => _sink.CursorX;

    public int CursorY => _sink.CursorY;

    public bool Write(scoped ReadOnlySpan<char> text, Style style)
    {
        _count++;
        return _sink.Write(text, style);
    }

    public bool Write(char value, Style style)
    {
        Span<char> buffer = stackalloc char[1];
        buffer[0] = value;
        return Write(buffer, style);
    }

    public bool WriteRepeated(char value, int count, Style style)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0)
        {
            return true;
        }

        Span<char> buffer = stackalloc char[64];
        buffer.Fill(value);
        while (count > 0)
        {
            var length = Math.Min(count, buffer.Length);
            if (!Write(buffer[..length], style))
            {
                return false;
            }

            count -= length;
        }

        return true;
    }

    public bool Write(StringBuilder text, int start, int length, Style style)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (start > text.Length || length > text.Length - start)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        Span<char> buffer = stackalloc char[128];
        var remaining = length;
        var offset = start;
        while (remaining > 0)
        {
            var chunkLength = Math.Min(remaining, buffer.Length);
            text.CopyTo(offset, buffer, chunkLength);
            if (!Write(buffer[..chunkLength], style))
            {
                return false;
            }

            offset += chunkLength;
            remaining -= chunkLength;
        }

        return true;
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
