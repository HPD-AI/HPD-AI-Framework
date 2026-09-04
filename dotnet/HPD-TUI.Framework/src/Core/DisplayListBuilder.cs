namespace HPD.TUI.Core;

/// <summary>Records stable paint commands for deferred rasterization by a TUI compositor.</summary>
public ref struct DisplayListBuilder
{
    private readonly ISegmentSink _sink;
    private int _count;

    /// <summary>Creates a command builder over a display-list sink.</summary>
    /// <param name="sink">Destination that owns recorded command payloads.</param>
    /// <param name="maxWidth">Width available to the component.</param>
    public DisplayListBuilder(ISegmentSink sink, int maxWidth)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        ArgumentOutOfRangeException.ThrowIfNegative(maxWidth);
        MaxWidth = maxWidth;
    }

    /// <summary>Gets the width available to the current component.</summary>
    public int MaxWidth { get; }

    /// <summary>Gets the destination used by specialized command decorators.</summary>
    public ISegmentSink Sink => _sink;

    /// <summary>Gets the number of commands appended through this builder.</summary>
    public readonly int Count => _count;

    /// <summary>Gets the current raster cursor column.</summary>
    public int CursorX => _sink.CursorX;

    /// <summary>Gets the current raster cursor row.</summary>
    public int CursorY => _sink.CursorY;

    /// <summary>Appends an immutable text-run command.</summary>
    public bool Write(scoped ReadOnlySpan<char> text, Style style, TerminalRunMetadata metadata = default)
    { _count++; return _sink.Write(text, style, metadata); }

    /// <summary>Appends a one-character text-run command.</summary>
    public bool Write(char value, Style style)
    { Span<char> text = stackalloc char[1]; text[0] = value; return Write(text, style); }

    /// <summary>Appends repeated glyphs without allocating a temporary string.</summary>
    public bool WriteRepeated(char value, int count, Style style)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Span<char> buffer = stackalloc char[64];
        buffer.Fill(value);
        while (count > 0)
        {
            var length = Math.Min(count, buffer.Length);
            if (!Write(buffer[..length], style)) return false;
            count -= length;
        }
        return true;
    }

    /// <summary>Appends a line-break command.</summary>
    public bool WriteLineBreak() { _count++; return _sink.WriteLineBreak(); }

    /// <summary>Moves the raster cursor for subsequent commands.</summary>
    public void MoveTo(int x, int y) => _sink.MoveTo(x, y);

    /// <summary>Sets the requested terminal cursor position.</summary>
    public void SetTerminalCursor(int x, int y) => _sink.SetTerminalCursor(x, y);
}
