using System.Globalization;
using System.Text;
using HPD.Environment.Contracts;

namespace HPD.Environment.Runtime.Workspaces;

internal readonly record struct Utf8WorkspaceTextReadResult(
    string Content,
    long StartLine,
    long EndLine,
    long TotalLines,
    bool Truncated);

internal static class Utf8WorkspaceTextLineReader
{
    internal const int MaxSourceBytes = 64 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static Utf8WorkspaceTextReadResult Read(
        ReadOnlySpan<byte> source,
        WorkspaceTextReadOptions options)
    {
        if (source.Length > MaxSourceBytes)
        {
            throw new IOException(
                $"Text reads are limited to {MaxSourceBytes.ToString(CultureInfo.InvariantCulture)} source bytes.");
        }

        if (source.Length >= 3 &&
            source[0] == 0xEF &&
            source[1] == 0xBB &&
            source[2] == 0xBF)
        {
            source = source[3..];
        }

        var totalLines = CountLines(source);
        if (totalLines == 0 || options.StartLine > totalLines)
        {
            return new Utf8WorkspaceTextReadResult(
                string.Empty,
                options.StartLine,
                options.StartLine - 1,
                totalLines,
                Truncated: false);
        }

        var startOffset = FindLineStart(source, options.StartLine);
        var endOffset = FindSelectionEnd(
            source,
            startOffset,
            options.MaxLines);
        var selected = source[startOffset..endOffset];
        var byteLimit = Math.Min(options.MaxBytes, selected.Length);
        byteLimit = ValidUtf8PrefixLength(selected[..byteLimit]);
        var byteTruncated = byteLimit < selected.Length;
        string content;
        try
        {
            content = StrictUtf8.GetString(selected[..byteLimit]);
        }
        catch (DecoderFallbackException exception)
        {
            throw new IOException(
                "Workspace text contains invalid UTF-8.",
                exception);
        }

        var linesRead = CountLines(content.AsSpan());
        var endLine = linesRead == 0
            ? options.StartLine - 1
            : Math.Min(
                totalLines,
                options.StartLine + linesRead - 1);
        return new Utf8WorkspaceTextReadResult(
            content,
            options.StartLine,
            endLine,
            totalLines,
            Truncated: byteTruncated || endOffset < source.Length);
    }

    private static long CountLines(ReadOnlySpan<byte> source)
    {
        if (source.Length == 0)
            return 0;

        long count = 0;
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '\r')
            {
                count++;
                if (i + 1 < source.Length && source[i + 1] == '\n')
                    i++;
                continue;
            }

            if (source[i] == '\n')
                count++;
        }

        return source[^1] is 0x0D or 0x0A
            ? count
            : count + 1;
    }

    private static long CountLines(ReadOnlySpan<char> source)
    {
        if (source.Length == 0)
            return 0;

        long count = 0;
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '\r')
            {
                count++;
                if (i + 1 < source.Length && source[i + 1] == '\n')
                    i++;
                continue;
            }

            if (source[i] == '\n')
                count++;
        }

        return source[^1] is '\r' or '\n'
            ? count
            : count + 1;
    }

    private static int FindLineStart(
        ReadOnlySpan<byte> source,
        long startLine)
    {
        if (startLine <= 1)
            return 0;

        long line = 1;
        for (var i = 0; i < source.Length; i++)
        {
            int lineEndingLength;
            if (source[i] == '\r')
            {
                lineEndingLength =
                    i + 1 < source.Length && source[i + 1] == '\n'
                        ? 2
                        : 1;
            }
            else if (source[i] == '\n')
            {
                lineEndingLength = 1;
            }
            else
            {
                continue;
            }

            i += lineEndingLength - 1;
            line++;
            if (line == startLine)
                return i + 1;
        }

        return source.Length;
    }

    private static int FindSelectionEnd(
        ReadOnlySpan<byte> source,
        int startOffset,
        int? maxLines)
    {
        if (maxLines == null)
            return source.Length;

        var lines = 0;
        for (var i = startOffset; i < source.Length; i++)
        {
            int lineEndingLength;
            if (source[i] == '\r')
            {
                lineEndingLength =
                    i + 1 < source.Length && source[i + 1] == '\n'
                        ? 2
                        : 1;
            }
            else if (source[i] == '\n')
            {
                lineEndingLength = 1;
            }
            else
            {
                continue;
            }

            i += lineEndingLength - 1;
            lines++;
            if (lines >= maxLines.Value)
                return i + 1;
        }

        return source.Length;
    }

    private static int ValidUtf8PrefixLength(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.Length;
        for (var back = 0; back < Math.Min(4, end); back++)
        {
            var value = bytes[end - 1 - back];
            if ((value & 0xC0) == 0x80)
                continue;

            var expectedLength = value switch
            {
                < 0x80 => 1,
                < 0xE0 => 2,
                < 0xF0 => 3,
                _ => 4
            };
            return expectedLength <= back + 1
                ? end
                : end - back - 1;
        }

        return end;
    }
}
