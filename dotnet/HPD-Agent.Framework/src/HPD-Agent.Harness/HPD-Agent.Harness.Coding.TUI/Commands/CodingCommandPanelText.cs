using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands;

internal static class CodingCommandPanelText
{
    public static string BuildMetadata(CodingCommandExecutionState command, bool includeWorkingDirectory)
    {
        var parts = new List<string> { command.DisplayCommand };
        if (includeWorkingDirectory && !string.IsNullOrWhiteSpace(command.WorkingDirectory))
        {
            parts.Add(command.WorkingDirectory);
        }

        if (command.DurationMilliseconds is { } duration)
        {
            parts.Add(CodingCommandRenderText.FormatDuration(duration));
        }

        if (command.StdoutBytes > 0)
        {
            parts.Add($"stdout {FormatBytes(command.StdoutBytes)}");
        }

        if (command.StderrBytes > 0)
        {
            parts.Add($"stderr {FormatBytes(command.StderrBytes)}");
        }

        if (command.OutputTruncated || command.CombinedBytesDiscarded > 0)
        {
            parts.Add("truncated");
        }

        if (command.OutputEventsSuppressed)
        {
            parts.Add("suppressed");
        }

        if (command.BinaryOutputObserved)
        {
            parts.Add("binary");
        }

        if (command.Artifacts.HasAny)
        {
            parts.Add("artifacts");
        }

        return string.Join("  ", parts);
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1_024)
        {
            return $"{bytes}B";
        }

        if (bytes < 1_024 * 1_024)
        {
            return $"{bytes / 1_024d:0.#}KB";
        }

        return $"{bytes / (1_024d * 1_024d):0.#}MB";
    }

    public static void WriteClipped(string text, int width, Style style, ref SegmentWriter output)
    {
        if (width <= 0)
        {
            return;
        }

        var normalized = text.Replace('\t', ' ');
        if (normalized.Length <= width)
        {
            output.Write(normalized.AsSpan(), style);
            return;
        }

        if (width <= 3)
        {
            output.Write(new string('.', width).AsSpan(), style);
            return;
        }

        output.Write(normalized.AsSpan(0, width - 3), style);
        output.Write("...".AsSpan(), style);
    }
}
