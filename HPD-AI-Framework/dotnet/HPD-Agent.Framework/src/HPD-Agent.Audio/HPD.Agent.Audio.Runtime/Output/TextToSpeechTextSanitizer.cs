using System.Text;
using HPD.Agent.Audio.Output;

namespace HPD.Agent.Audio.Runtime.Output;

public sealed class TextToSpeechTextSanitizer
{
    public TextToSpeechSegment Sanitize(
        TextToSpeechSegment segment,
        TextToSpeechFilteringOptions options)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return segment;
        }

        var original = segment.Text;
        var text = original;
        if (options.RemoveCodeBlocks)
        {
            text = RemoveCodeBlocks(text);
        }

        if (options.RemoveMarkdownTables)
        {
            text = RemoveMarkdownTables(text);
        }

        if (options.SimplifyLinks)
        {
            text = SimplifyLinks(text);
        }

        if (options.StripMarkdownFormatting)
        {
            text = StripMarkdownFormatting(text);
        }

        if (options.EmojiPolicy == TextToSpeechEmojiPolicy.Remove)
        {
            text = RemoveNonSpeechSymbols(text);
        }

        if (options.CollapseRepeatedPunctuation)
        {
            text = CollapseRepeatedPunctuation(text);
        }

        if (options.NormalizeWhitespace)
        {
            text = NormalizeWhitespace(text);
        }

        return string.Equals(original, text, StringComparison.Ordinal)
            ? segment
            : segment with
            {
                Text = text,
                SourceText = segment.SourceText ?? original,
                SanitizerPolicyId = "default"
            };
    }

    private static string RemoveCodeBlocks(string value)
    {
        var builder = new StringBuilder(value.Length);
        var inFence = false;
        for (var index = 0; index < value.Length; index++)
        {
            if (index + 2 < value.Length &&
                value[index] == '`' &&
                value[index + 1] == '`' &&
                value[index + 2] == '`')
            {
                inFence = !inFence;
                index += 2;
                continue;
            }

            if (!inFence)
            {
                builder.Append(value[index]);
            }
        }

        return builder.ToString();
    }

    private static string RemoveMarkdownTables(string value)
    {
        var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Contains('|', StringComparison.Ordinal) &&
                (trimmed.Contains("---", StringComparison.Ordinal) ||
                 trimmed.Count(ch => ch == '|') >= 2))
            {
                continue;
            }

            builder.AppendLine(line);
        }

        return builder.ToString();
    }

    private static string SimplifyLinks(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '[')
            {
                var closeText = value.IndexOf(']', index + 1);
                if (closeText > index &&
                    closeText + 1 < value.Length &&
                    value[closeText + 1] == '(')
                {
                    var closeUrl = value.IndexOf(')', closeText + 2);
                    if (closeUrl > closeText)
                    {
                        builder.Append(value.AsSpan(index + 1, closeText - index - 1));
                        index = closeUrl;
                        continue;
                    }
                }
            }

            builder.Append(value[index]);
        }

        return builder.ToString();
    }

    private static string StripMarkdownFormatting(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is '*' or '_' or '`' or '#' or '>' or '~')
            {
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string RemoveNonSpeechSymbols(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Value > 0xFFFF)
            {
                continue;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }

    private static string CollapseRepeatedPunctuation(string value)
    {
        var builder = new StringBuilder(value.Length);
        char? previous = null;
        var repeatCount = 0;
        foreach (var ch in value)
        {
            if (ch == previous && ch is '!' or '?' or '.' or ',')
            {
                repeatCount++;
                if (repeatCount > 1)
                {
                    continue;
                }
            }
            else
            {
                repeatCount = 0;
            }

            previous = ch;
            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string NormalizeWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasWhitespace = false;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            builder.Append(ch);
            previousWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }
}
