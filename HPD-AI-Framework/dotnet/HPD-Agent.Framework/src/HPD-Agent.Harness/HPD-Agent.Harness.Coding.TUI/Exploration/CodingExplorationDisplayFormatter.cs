using System.Globalization;

namespace HPD.Agent.ToolHarness.Coding.TUI.Exploration;

internal static class CodingExplorationDisplayFormatter
{
    public static IReadOnlyList<string> BuildRows(CodingExplorationGroup group)
    {
        var rows = new List<string>();
        var pendingReads = new List<CodingExplorationOperation>();
        foreach (var operation in group.Operations)
        {
            if (string.Equals(operation.ToolName, CodingExplorationToolNames.ReadFile, StringComparison.Ordinal) &&
                !IsFailed(operation))
            {
                pendingReads.Add(operation);
                continue;
            }

            FlushReads(pendingReads, rows);
            rows.Add(FormatOperation(operation));
        }

        FlushReads(pendingReads, rows);
        return rows.Count == 0 ? ["Inspecting"] : rows;
    }

    public static string StatusText(CodingExplorationStore store)
    {
        var active = store.ActiveGroups;
        if (active.Count > 0)
        {
            var count = active.Sum(static group => group.Operations.Count(static operation => !operation.IsComplete));
            return count <= 1 ? "exploring" : $"exploring {count}";
        }

        var latest = store.RecentGroups.FirstOrDefault();
        if (latest is null || latest.Operations.Count == 0)
        {
            return "";
        }

        return latest.Operations.Count == 1
            ? "explored 1"
            : $"explored {latest.Operations.Count}";
    }

    private static void FlushReads(List<CodingExplorationOperation> reads, List<string> rows)
    {
        if (reads.Count == 0)
        {
            return;
        }

        rows.Add(FormatReadGroup(reads));
        reads.Clear();
    }

    private static string FormatReadGroup(IReadOnlyList<CodingExplorationOperation> reads)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var read in reads)
        {
            var label = ShortPath(read.Summary?.Path ?? CodingExplorationArgsParser.Parse(read.ArgsJson).Path);
            counts[label] = counts.TryGetValue(label, out var count) ? count + 1 : 1;
        }

        var parts = counts.Select(static pair => pair.Value == 1 ? pair.Key : $"{pair.Key} x{pair.Value}");
        var text = $"Read {string.Join(", ", parts)}";
        if (reads.Any(static read => read.Summary?.Truncated == true || read.Summary?.HasMore == true))
        {
            text += " truncated";
        }

        if (reads.Any(static read => read.Summary is ReadFileExplorationSummary { Unchanged: true }))
        {
            text += " unchanged";
        }

        return text;
    }

    private static string FormatOperation(CodingExplorationOperation operation)
    {
        if (IsFailed(operation))
        {
            return $"{VerbFor(operation.ToolName)} {BestSubject(operation)} failed";
        }

        return operation.Summary switch
        {
            GrepExplorationSummary grep => FormatGrep(operation, grep),
            GlobExplorationSummary glob => FormatGlob(operation, glob),
            ListDirectoryExplorationSummary list => FormatList(operation, list),
            ReadFileExplorationSummary => FormatReadGroup([operation]),
            _ => FormatPending(operation)
        };
    }

    private static string FormatGrep(CodingExplorationOperation operation, GrepExplorationSummary summary)
    {
        var args = CodingExplorationArgsParser.Parse(operation.ArgsJson);
        var pattern = Quote(summary.Pattern ?? args.Pattern);
        var path = ShortScope(summary.Path ?? args.Path);
        var text = string.IsNullOrEmpty(path)
            ? $"Search {pattern}"
            : $"Search {pattern} in {path}";
        text += FormatCount(summary.TotalMatches, "match", "matches");
        return AddMarkers(text, summary);
    }

    private static string FormatGlob(CodingExplorationOperation operation, GlobExplorationSummary summary)
    {
        var args = CodingExplorationArgsParser.Parse(operation.ArgsJson);
        var pattern = Quote(summary.OriginalPattern ?? summary.Pattern ?? args.Pattern);
        var path = ShortScope(summary.Path ?? args.Path);
        var text = string.IsNullOrEmpty(path) || path == "."
            ? $"Find {pattern}"
            : $"Find {pattern} in {path}";
        text += FormatCount(summary.TotalMatches, "match", "matches");
        return AddMarkers(text, summary);
    }

    private static string FormatList(CodingExplorationOperation operation, ListDirectoryExplorationSummary summary)
    {
        var args = CodingExplorationArgsParser.Parse(operation.ArgsJson);
        var path = ShortScope(summary.Path ?? args.Path);
        var text = $"List {path}";
        if (summary.Recursive || args.Recursive == true)
        {
            text += " recursively";
        }

        text += FormatCount(summary.TotalEntries, "entry", "entries");
        return AddMarkers(text, summary);
    }

    private static string FormatPending(CodingExplorationOperation operation)
    {
        var args = CodingExplorationArgsParser.Parse(operation.ArgsJson);
        return operation.ToolName switch
        {
            CodingExplorationToolNames.ReadFile => $"Read {ShortPath(args.Path)}",
            CodingExplorationToolNames.Grep => string.IsNullOrWhiteSpace(args.Pattern)
                ? "Search"
                : $"Search {Quote(args.Pattern)} in {ShortScope(args.Path)}",
            CodingExplorationToolNames.GlobSearch => string.IsNullOrWhiteSpace(args.Pattern)
                ? "Find"
                : $"Find {Quote(args.Pattern)} in {ShortScope(args.Path)}",
            CodingExplorationToolNames.ListDirectory => $"List {ShortScope(args.Path)}",
            _ => operation.ToolName
        };
    }

    private static string BestSubject(CodingExplorationOperation operation)
    {
        var args = CodingExplorationArgsParser.Parse(operation.ArgsJson);
        return operation.Summary?.Path ?? args.Path ?? args.Pattern ?? "";
    }

    private static string VerbFor(string toolName)
        => toolName switch
        {
            CodingExplorationToolNames.ReadFile => "Read",
            CodingExplorationToolNames.Grep => "Search",
            CodingExplorationToolNames.GlobSearch => "Find",
            CodingExplorationToolNames.ListDirectory => "List",
            _ => toolName
        };

    private static bool IsFailed(CodingExplorationOperation operation)
        => operation.Status == CodingExplorationOperationStatus.Failed || operation.Summary?.IsError == true;

    private static string AddMarkers(string text, CodingExplorationSummary summary)
    {
        if (summary.Truncated || summary.HasMore)
        {
            text += " truncated";
        }

        return text;
    }

    private static string FormatCount(string? value, string singular, string plural)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
        {
            return $" {value} {plural}";
        }

        return count == 1 ? $" 1 {singular}" : $" {count} {plural}";
    }

    private static string Quote(string? text)
        => string.IsNullOrWhiteSpace(text) ? "\"?\"" : $"\"{text}\"";

    private static string ShortScope(string? path)
        => string.IsNullOrWhiteSpace(path) ? "." : TrimTrailingSlash(NormalizePath(path));

    private static string ShortPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "?";
        }

        var normalized = NormalizePath(path);
        var fileName = Path.GetFileName(normalized);
        return string.IsNullOrWhiteSpace(fileName) ? normalized : fileName;
    }

    private static string NormalizePath(string path)
        => path.Replace('\\', '/');

    private static string TrimTrailingSlash(string path)
        => path.Length > 1 ? path.TrimEnd('/') : path;
}
