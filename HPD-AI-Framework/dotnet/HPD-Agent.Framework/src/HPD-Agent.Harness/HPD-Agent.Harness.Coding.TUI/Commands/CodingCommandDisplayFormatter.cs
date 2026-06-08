namespace HPD.Agent.ToolHarness.Coding.TUI.Commands;

internal static class CodingCommandDisplayFormatter
{
    private const int MaxDisplayCharacters = 84;

    public static string Format(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return command;
        }

        var trimmed = command.Trim();
        foreach (var prefix in new[] { "bash -lc ", "zsh -lc ", "sh -lc " })
        {
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var wrapped = trimmed[prefix.Length..].Trim();
            if (TryUnquote(wrapped, out var unquoted))
            {
                return Compact(unquoted);
            }
        }

        return Compact(trimmed);
    }

    private static string Compact(string command)
    {
        var trimmed = command.Trim();
        if (TryFormatBulkFileDump(trimmed, out var bulkFileDump))
        {
            return bulkFileDump;
        }

        if (trimmed.Length <= MaxDisplayCharacters)
        {
            return trimmed;
        }

        var pipeIndex = trimmed.IndexOf(" | ", StringComparison.Ordinal);
        if (pipeIndex > 0)
        {
            var firstStage = trimmed[..pipeIndex].Trim();
            if (firstStage.Length > MaxDisplayCharacters - 8)
            {
                firstStage = ClipEnd(firstStage, MaxDisplayCharacters - 8);
            }

            return $"{firstStage} | ...";
        }

        return ClipEnd(trimmed, MaxDisplayCharacters);
    }

    private static bool TryFormatBulkFileDump(string command, out string display)
    {
        display = "";
        if (!command.StartsWith("find ", StringComparison.Ordinal) ||
            !command.Contains("while read", StringComparison.Ordinal) ||
            !command.Contains("cat ", StringComparison.Ordinal))
        {
            return false;
        }

        var pipeIndex = command.IndexOf(" | ", StringComparison.Ordinal);
        var findStage = pipeIndex > 0 ? command[..pipeIndex].Trim() : command;
        display = $"{ClipEnd(findStage, 52)} | cat matching files";
        return true;
    }

    private static string ClipEnd(string value, int maxCharacters)
    {
        if (value.Length <= maxCharacters)
        {
            return value;
        }

        if (maxCharacters <= 3)
        {
            return new string('.', Math.Max(0, maxCharacters));
        }

        return string.Concat(value.AsSpan(0, maxCharacters - 3), "...");
    }

    private static bool TryUnquote(string value, out string unquoted)
    {
        unquoted = value;
        if (value.Length < 2)
        {
            return false;
        }

        var quote = value[0];
        if ((quote != '"' && quote != '\'') || value[^1] != quote)
        {
            return false;
        }

        var inner = value[1..^1];
        if (inner.Contains('\n') || inner.Contains('\r'))
        {
            return false;
        }

        unquoted = inner;
        return true;
    }
}
