using System.Text;
using Telegram.Bot.Types.Enums;

namespace HPD.Agent.Bots.Telegram;

public enum TelegramRenderMode
{
    Plain,
    MarkdownV2,
}

internal static class TelegramMarkdownV2
{
    public const int MessageLimit = 4096;
    public const int CaptionLimit = 1024;

    private const string MarkdownV2Ellipsis = "\\.\\.\\.";
    private const string PlainEllipsis = "...";
    private static readonly char[] EntityMarkers = ['*', '_', '~', '`'];

    public static ParseMode ToBotParseMode(TelegramRenderMode mode)
        => mode == TelegramRenderMode.MarkdownV2 ? ParseMode.MarkdownV2 : ParseMode.None;

    public static string EscapeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (IsMarkdownV2Special(ch))
                sb.Append('\\');
            sb.Append(ch);
        }

        return sb.ToString();
    }

    public static string EscapeCode(string text)
        => EscapeOnly(text, '`', '\\');

    public static string EscapeLinkUrl(string url)
        => EscapeOnly(url, ')', '\\');

    public static string Truncate(string text, int limit, TelegramRenderMode mode)
    {
        if (text.Length <= limit)
            return text;

        var ellipsis = mode == TelegramRenderMode.MarkdownV2 ? MarkdownV2Ellipsis : PlainEllipsis;
        var sliceLength = Math.Max(0, limit - ellipsis.Length);
        var slice = text[..sliceLength];

        if (mode == TelegramRenderMode.MarkdownV2)
            slice = TrimToMarkdownV2SafeBoundary(slice);

        return slice + ellipsis;
    }

    private static string EscapeOnly(string text, params char[] chars)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (chars.Contains(ch))
                sb.Append('\\');
            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static bool IsMarkdownV2Special(char ch)
        => ch is '_' or '*' or '[' or ']' or '(' or ')' or '~' or '`' or '>' or '#'
            or '+' or '-' or '=' or '|' or '{' or '}' or '.' or '!' or '\\';

    private static string TrimToMarkdownV2SafeBoundary(string text)
    {
        var current = text;
        for (var i = 0; i <= text.Length; i++)
        {
            if (EndsWithOrphanBackslash(current))
            {
                current = current[..^1];
                continue;
            }

            var minUnsafePosition = current.Length;
            foreach (var marker in EntityMarkers)
            {
                var positions = FindUnescapedPositions(current, marker);
                if (positions.Count % 2 == 1)
                    minUnsafePosition = Math.Min(minUnsafePosition, positions[^1]);
            }

            var openBrackets = FindUnescapedPositions(current, '[');
            var closeBrackets = FindUnescapedPositions(current, ']');
            if (openBrackets.Count > closeBrackets.Count)
                minUnsafePosition = Math.Min(minUnsafePosition, openBrackets[^1]);

            if (minUnsafePosition >= current.Length)
                return current;

            current = current[..minUnsafePosition];
        }

        return current;
    }

    private static bool EndsWithOrphanBackslash(string text)
    {
        var trailing = 0;
        for (var i = text.Length - 1; i >= 0 && text[i] == '\\'; i--)
            trailing++;
        return trailing % 2 == 1;
    }

    private static List<int> FindUnescapedPositions(string text, char marker)
    {
        var positions = new List<int>();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != marker)
                continue;

            var backslashes = 0;
            for (var j = i - 1; j >= 0 && text[j] == '\\'; j--)
                backslashes++;

            if (backslashes % 2 == 0)
                positions.Add(i);
        }

        return positions;
    }
}
