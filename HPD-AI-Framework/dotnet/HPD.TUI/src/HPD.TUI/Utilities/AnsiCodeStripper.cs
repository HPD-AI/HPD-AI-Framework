namespace HPD.TUI.Utilities;

public static class AnsiCodeStripper
{
    public static int VisibleLength(ReadOnlySpan<char> text)
    {
        var visible = 0;
        var i = 0;

        while (i < text.Length)
        {
            if (text[i] == '\x1b')
            {
                i = SkipEscapeSequence(text, i);
                continue;
            }

            visible++;
            i++;
        }

        return visible;
    }

    public static int Strip(ReadOnlySpan<char> text, Span<char> destination)
    {
        var written = 0;
        var i = 0;

        while (i < text.Length && written < destination.Length)
        {
            if (text[i] == '\x1b')
            {
                i = SkipEscapeSequence(text, i);
                continue;
            }

            destination[written++] = text[i++];
        }

        return written;
    }

    public static int SkipEscapeSequence(ReadOnlySpan<char> text, int start)
    {
        if ((uint)start >= (uint)text.Length || text[start] != '\x1b')
        {
            return start;
        }

        var i = start + 1;
        if (i >= text.Length)
        {
            return i;
        }

        return text[i] switch
        {
            '[' => SkipCsi(text, i + 1),
            ']' => SkipOsc(text, i + 1),
            'P' or '^' or '_' => SkipStringControl(text, i + 1),
            _ => Math.Min(i + 1, text.Length)
        };
    }

    private static int SkipCsi(ReadOnlySpan<char> text, int i)
    {
        while (i < text.Length)
        {
            var ch = text[i++];
            if (ch >= 0x40 && ch <= 0x7E)
            {
                break;
            }
        }

        return i;
    }

    private static int SkipOsc(ReadOnlySpan<char> text, int i)
    {
        while (i < text.Length)
        {
            if (text[i] == '\x07')
            {
                return i + 1;
            }

            if (text[i] == '\x1b' && i + 1 < text.Length && text[i + 1] == '\\')
            {
                return i + 2;
            }

            i++;
        }

        return i;
    }

    private static int SkipStringControl(ReadOnlySpan<char> text, int i)
    {
        while (i < text.Length)
        {
            if (text[i] == '\x1b' && i + 1 < text.Length && text[i + 1] == '\\')
            {
                return i + 2;
            }

            i++;
        }

        return i;
    }
}
