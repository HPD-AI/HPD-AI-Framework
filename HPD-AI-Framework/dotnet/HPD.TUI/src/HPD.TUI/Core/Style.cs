using System.Buffers.Text;

namespace HPD.TUI.Core;

public readonly record struct Style(Color Foreground, Color Background, TextAttributes Attributes = TextAttributes.None)
{
    public static Style Default { get; } = new(Color.White, Color.Default);

    public int WriteAnsiPrefix(Span<char> destination)
    {
        Span<byte> bytes = stackalloc byte[3];
        var pos = 0;

        if (!TryAppend(destination, ref pos, "\x1b[38;2;") ||
            !TryAppendByte(destination, ref pos, bytes, Foreground.R) ||
            !TryAppend(destination, ref pos, ";") ||
            !TryAppendByte(destination, ref pos, bytes, Foreground.G) ||
            !TryAppend(destination, ref pos, ";") ||
            !TryAppendByte(destination, ref pos, bytes, Foreground.B))
        {
            return 0;
        }

        if (!Background.IsDefault &&
            (!TryAppend(destination, ref pos, ";48;2;") ||
            !TryAppendByte(destination, ref pos, bytes, Background.R) ||
            !TryAppend(destination, ref pos, ";") ||
            !TryAppendByte(destination, ref pos, bytes, Background.G) ||
            !TryAppend(destination, ref pos, ";") ||
            !TryAppendByte(destination, ref pos, bytes, Background.B)))
        {
            return 0;
        }

        if ((Attributes & TextAttributes.Bold) != 0 && !TryAppend(destination, ref pos, ";1"))
        {
            return 0;
        }

        if ((Attributes & TextAttributes.Italic) != 0 && !TryAppend(destination, ref pos, ";3"))
        {
            return 0;
        }

        if ((Attributes & TextAttributes.Underline) != 0 && !TryAppend(destination, ref pos, ";4"))
        {
            return 0;
        }

        if ((Attributes & TextAttributes.Strikethrough) != 0 && !TryAppend(destination, ref pos, ";9"))
        {
            return 0;
        }

        if (!TryAppend(destination, ref pos, "m"))
        {
            return 0;
        }

        return pos;
    }

    private static bool TryAppend(Span<char> destination, ref int pos, ReadOnlySpan<char> value)
    {
        if (destination.Length - pos < value.Length)
        {
            return false;
        }

        value.CopyTo(destination[pos..]);
        pos += value.Length;
        return true;
    }

    private static bool TryAppendByte(Span<char> destination, ref int pos, Span<byte> scratch, byte value)
    {
        if (!Utf8Formatter.TryFormat(value, scratch, out var bytesWritten))
        {
            return false;
        }

        if (destination.Length - pos < bytesWritten)
        {
            return false;
        }

        for (var i = 0; i < bytesWritten; i++)
        {
            destination[pos++] = (char)scratch[i];
        }

        return true;
    }
}

[Flags]
public enum TextAttributes
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Strikethrough = 8
}
