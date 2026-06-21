namespace HPD.TUI.Core;

public readonly ref struct Segment
{
    public Segment(ReadOnlySpan<char> text, Style style, bool isLineBreak = false)
    {
        Text = text;
        Style = style;
        IsLineBreak = isLineBreak;
    }

    public ReadOnlySpan<char> Text { get; }

    public Style Style { get; }

    public bool IsLineBreak { get; }

    public static Segment LineBreak => new([], Style.Default, true);
}
