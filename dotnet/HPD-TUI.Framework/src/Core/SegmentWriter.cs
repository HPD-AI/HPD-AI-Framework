namespace HPD.TUI.Core;

/// <summary>Receives display-list commands for retained recording or immediate rasterization.</summary>
public interface ISegmentSink
{
    int CursorX { get; }

    int CursorY { get; }

    bool Write(scoped ReadOnlySpan<char> text, Style style, TerminalRunMetadata metadata = default);

    bool WriteLineBreak();

    void MoveTo(int x, int y);

    void SetTerminalCursor(int x, int y);
}
