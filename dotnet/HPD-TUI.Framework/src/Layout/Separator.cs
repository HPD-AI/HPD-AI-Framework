using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.TUI.Layout;

public sealed class Separator : IComponent
{
    private readonly string? _title;

    public Separator(string? title = null)
    {
        _title = string.IsNullOrWhiteSpace(title) ? null : title;
    }

    public char Glyph { get; init; } = '─';

    public Alignment TitleAlignment { get; init; } = Alignment.Center;

    public Style? Style { get; init; }

    public int TitleSpacing { get; init; } = 1;

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxWidth);
        return new Measurement(Math.Min(maxWidth, 1), maxWidth);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        if (maxWidth <= 0)
        {
            return;
        }

        var style = Style ?? context.Theme.Border;
        Span<char> buffer = maxWidth <= 256 ? stackalloc char[maxWidth] : new char[maxWidth];
        buffer.Fill(Glyph);

        if (_title is not null)
        {
            WriteTitle(buffer, _title);
        }

        output.Write(buffer, style);
    }

    public void HandleInput(in KeyEvent key)
    {
    }

    public void Invalidate()
    {
    }

    public static Separator Create(string? title = null) => new(title);

    private void WriteTitle(Span<char> buffer, string title)
    {
        var titleWidth = UnicodeWidth.GetWidth(title);
        var required = titleWidth + (TitleSpacing * 2);
        if (required > buffer.Length)
        {
            return;
        }

        var start = TitleAlignment switch
        {
            Alignment.Start => 0,
            Alignment.End => buffer.Length - required,
            _ => (buffer.Length - required) / 2
        };

        var textStart = start + TitleSpacing;
        buffer.Slice(start, TitleSpacing).Fill(' ');
        title.AsSpan().CopyTo(buffer[textStart..]);
        buffer.Slice(textStart + title.Length, TitleSpacing).Fill(' ');
    }
}
