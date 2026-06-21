using System.Globalization;

namespace HPD.TUI.Utilities;

public static class UnicodeWidth
{
    public static int GetWidth(Rune rune)
    {
        var codePoint = rune.Value;
        var category = Rune.GetUnicodeCategory(rune);

        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format ||
            codePoint is 0x200B or 0x200C or 0x200D or 0xFE0E or 0xFE0F or 0xFEFF)
        {
            return 0;
        }

        if (IsWide(codePoint))
        {
            return 2;
        }

        return 1;
    }

    public static int GetWidth(ReadOnlySpan<char> text)
    {
        var width = 0;
        var enumerator = new RuneEnumerator(text);

        while (enumerator.MoveNext())
        {
            width += GetWidth(enumerator.Current);
        }

        return width;
    }

    private static bool IsWide(int codePoint)
    {
        return
            codePoint is >= 0x1100 and <= 0x115F ||
            codePoint is >= 0x2329 and <= 0x232A ||
            codePoint is >= 0x2E80 and <= 0xA4CF ||
            codePoint is >= 0xAC00 and <= 0xD7A3 ||
            codePoint is >= 0xF900 and <= 0xFAFF ||
            codePoint is >= 0xFE10 and <= 0xFE19 ||
            codePoint is >= 0xFE30 and <= 0xFE6F ||
            codePoint is >= 0xFF00 and <= 0xFF60 ||
            codePoint is >= 0xFFE0 and <= 0xFFE6 ||
            codePoint is >= 0x1F000 and <= 0x1FAFF ||
            codePoint is >= 0x20000 and <= 0x3FFFD;
    }
}
