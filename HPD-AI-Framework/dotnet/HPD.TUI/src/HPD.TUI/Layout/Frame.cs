using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Utilities;

namespace HPD.TUI.Layout;

public sealed class Frame : IComponent
{
    private readonly IComponent _child;

    public Frame(IComponent child)
    {
        _child = child ?? throw new ArgumentNullException(nameof(child));
    }

    public BorderSpec Border { get; init; } = BorderSpec.Square;

    public Thickness Padding { get; init; } = Thickness.None;

    public FrameHeader? Header { get; init; }

    public FrameFooter? Footer { get; init; }

    public OverflowPolicy Overflow { get; init; } = OverflowPolicy.Clip;

    public int? Width { get; init; }

    public int? Height { get; init; }

    public static Frame Create(IComponent child) => new(child);

    public Frame WithBorder(BorderSpec border) => new(_child)
    {
        Border = border,
        Padding = Padding,
        Header = Header,
        Footer = Footer,
        Overflow = Overflow,
        Width = Width,
        Height = Height
    };

    public Frame WithPadding(int all) => WithPadding(new Thickness(all));

    public Frame WithPadding(Thickness padding) => new(_child)
    {
        Border = Border,
        Padding = padding,
        Header = Header,
        Footer = Footer,
        Overflow = Overflow,
        Width = Width,
        Height = Height
    };

    public Frame WithHeader(string text, Alignment alignment = Alignment.Start) => new(_child)
    {
        Border = Border,
        Padding = Padding,
        Header = new FrameHeader(text, alignment),
        Footer = Footer,
        Overflow = Overflow,
        Width = Width,
        Height = Height
    };

    public Frame WithFooter(string text, Alignment alignment = Alignment.Start) => new(_child)
    {
        Border = Border,
        Padding = Padding,
        Header = Header,
        Footer = new FrameFooter(text, alignment),
        Overflow = Overflow,
        Width = Width,
        Height = Height
    };

    public Frame WithSize(int? width = null, int? height = null) => new(_child)
    {
        Border = Border,
        Padding = Padding,
        Header = Header,
        Footer = Footer,
        Overflow = Overflow,
        Width = width,
        Height = height
    };

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        if (maxWidth <= 0)
        {
            return new Measurement(0, 0);
        }

        var edgeWidth = Border.IsVisible ? 2 : 0;
        var innerMaxWidth = Math.Max(0, ResolveWidth(maxWidth) - edgeWidth - Padding.Horizontal);
        var child = innerMaxWidth > 0
            ? _child.Measure(in context, innerMaxWidth)
            : new Measurement(0, 0);
        var min = child.MinWidth + edgeWidth + Padding.Horizontal;
        var max = child.MaxWidth + edgeWidth + Padding.Horizontal;

        if (Header is { } header)
        {
            max = Math.Max(max, UnicodeWidth.GetWidth(header.Text) + edgeWidth + 2);
        }

        if (Footer is { } footer)
        {
            max = Math.Max(max, UnicodeWidth.GetWidth(footer.Text) + edgeWidth + 2);
        }

        if (Width is { } width)
        {
            min = Math.Min(width, min);
            max = width;
        }

        min = Math.Min(min, maxWidth);
        max = Math.Min(max, maxWidth);
        return new Measurement(Math.Min(min, max), max);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        var frameWidth = ResolveWidth(maxWidth);
        if (frameWidth <= 0)
        {
            return;
        }

        var style = Border.ResolveStyle(in context);
        var showBorder = Border.IsVisible;
        if (showBorder && frameWidth == 1)
        {
            WriteBorderLine(Border.Glyphs.TopLeft, Border.Glyphs.Top, Border.Glyphs.TopRight, Header, style, frameWidth, ref output);
            if (ResolveHeight(in context) > 1)
            {
                output.WriteLineBreak();
                WriteBorderLine(Border.Glyphs.BottomLeft, Border.Glyphs.Bottom, Border.Glyphs.BottomRight, Footer, style, frameWidth, ref output);
            }

            return;
        }

        if (showBorder)
        {
            WriteBorderLine(Border.Glyphs.TopLeft, Border.Glyphs.Top, Border.Glyphs.TopRight, Header, style, frameWidth, ref output);
            output.WriteLineBreak();
        }

        var innerWidth = Math.Max(0, frameWidth - (showBorder ? 2 : 0));
        WriteVerticalPadding(Padding.Top, innerWidth, showBorder, style, context.Theme.Text, ref output);
        WriteChildRows(in context, frameWidth, innerWidth, showBorder, style, ref output);
        WriteVerticalPadding(Padding.Bottom, innerWidth, showBorder, style, context.Theme.Text, ref output);

        if (showBorder)
        {
            WriteBorderLine(Border.Glyphs.BottomLeft, Border.Glyphs.Bottom, Border.Glyphs.BottomRight, Footer, style, frameWidth, ref output);
        }
    }

    public void HandleInput(in KeyEvent key)
    {
        _child.HandleInput(in key);
    }

    public void Invalidate()
    {
        _child.Invalidate();
    }

    private int ResolveWidth(int maxWidth)
    {
        var width = Width is { } fixedWidth ? Math.Min(fixedWidth, maxWidth) : maxWidth;
        return Math.Max(0, width);
    }

    private int ResolveHeight(in RenderContext context)
    {
        return Height is { } fixedHeight ? Math.Min(fixedHeight, context.Height) : context.Height;
    }

    private void WriteChildRows(in RenderContext context, int frameWidth, int innerWidth, bool showBorder, Style borderStyle, ref SegmentWriter output)
    {
        var childWidth = Math.Max(0, innerWidth - Padding.Horizontal);
        if (childWidth <= 0)
        {
            WriteEmptyInnerRow(innerWidth, showBorder, borderStyle, context.Theme.Text, ref output);
            return;
        }

        var reservedRows =
            (showBorder ? 2 : 0) +
            Padding.Vertical;
        var childHeight = Math.Max(1, ResolveHeight(in context) - reservedRows);
        using var childGrid = TuiCapture.RenderToGrid(_child, childWidth, childHeight, context.Theme, context.ColorSystem, context.Elapsed);
        var rows = TuiCapture.GetUsedLineCount(childGrid);

        for (var y = 0; y < rows; y++)
        {
            WriteSide(showBorder, Border.Glyphs.Left, borderStyle, ref output);
            WriteSpaces(Padding.Left, context.Theme.Text, ref output);
            TuiCapture.WriteLineTo(childGrid, y, ref output);
            WriteSpaces(Padding.Right, context.Theme.Text, ref output);
            WriteSide(showBorder, Border.Glyphs.Right, borderStyle, ref output);
            output.WriteLineBreak();
        }
    }

    private void WriteVerticalPadding(int count, int innerWidth, bool showBorder, Style borderStyle, Style contentStyle, ref SegmentWriter output)
    {
        for (var i = 0; i < count; i++)
        {
            WriteEmptyInnerRow(innerWidth, showBorder, borderStyle, contentStyle, ref output);
        }
    }

    private void WriteEmptyInnerRow(int innerWidth, bool showBorder, Style borderStyle, Style contentStyle, ref SegmentWriter output)
    {
        WriteSide(showBorder, Border.Glyphs.Left, borderStyle, ref output);
        WriteSpaces(innerWidth, contentStyle, ref output);
        WriteSide(showBorder, Border.Glyphs.Right, borderStyle, ref output);
        output.WriteLineBreak();
    }

    private static void WriteSide(bool showBorder, char glyph, Style style, ref SegmentWriter output)
    {
        if (!showBorder)
        {
            return;
        }

        Span<char> buffer = stackalloc char[1];
        buffer[0] = glyph;
        output.Write(buffer, style);
    }

    private static void WriteBorderLine(
        char left,
        char fill,
        char right,
        object? title,
        Style style,
        int width,
        ref SegmentWriter output)
    {
        Span<char> buffer = width <= 256 ? stackalloc char[width] : new char[width];
        if (width == 1)
        {
            buffer[0] = fill;
            output.Write(buffer, style);
            return;
        }

        buffer[0] = left;
        buffer[^1] = right;
        buffer[1..^1].Fill(fill);

        switch (title)
        {
            case FrameHeader header:
                WriteTitle(buffer, header.Text, header.Alignment);
                break;
            case FrameFooter footer:
                WriteTitle(buffer, footer.Text, footer.Alignment);
                break;
        }

        output.Write(buffer, style);
    }

    private static void WriteTitle(Span<char> buffer, string title, Alignment alignment)
    {
        var titleWidth = UnicodeWidth.GetWidth(title);
        var required = titleWidth + 2;
        if (required > buffer.Length - 2)
        {
            return;
        }

        var available = buffer.Length - 2;
        var start = alignment switch
        {
            Alignment.Center => 1 + Math.Max(0, (available - required) / 2),
            Alignment.End => buffer.Length - 1 - required,
            _ => 1
        };

        buffer[start] = ' ';
        title.AsSpan().CopyTo(buffer[(start + 1)..]);
        buffer[start + 1 + title.Length] = ' ';
    }

    private static void WriteSpaces(int count, Style style, ref SegmentWriter output)
    {
        if (count <= 0)
        {
            return;
        }

        Span<char> spaces = stackalloc char[Math.Min(count, 256)];
        spaces.Fill(' ');
        while (count > 0)
        {
            var current = Math.Min(count, spaces.Length);
            output.Write(spaces[..current], style);
            count -= current;
        }
    }
}
