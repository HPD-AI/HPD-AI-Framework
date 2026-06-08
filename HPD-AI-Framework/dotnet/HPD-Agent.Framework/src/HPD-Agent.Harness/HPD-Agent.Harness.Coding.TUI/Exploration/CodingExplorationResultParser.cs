using System.Globalization;
using System.Text.Json;
using System.Xml;
using HPD.Agent;

namespace HPD.Agent.ToolHarness.Coding.TUI.Exploration;

internal static class CodingExplorationResultParser
{
    public static CodingExplorationSummary Parse(string toolName, ToolResultPayload result)
    {
        var text = GetResultText(result);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new UnknownExplorationSummary();
        }

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true
            };
            using var reader = XmlReader.Create(new StringReader(text), settings);
            if (!MoveToFirstElement(reader))
            {
                return new UnknownExplorationSummary();
            }

            if (string.Equals(reader.Name, "error", StringComparison.Ordinal))
            {
                var path = reader.GetAttribute("path");
                var message = reader.IsEmptyElement ? null : reader.ReadElementContentAsString();
                return new UnknownExplorationSummary
                {
                    Path = path,
                    IsError = true,
                    ErrorMessage = string.IsNullOrWhiteSpace(message) ? "failed" : message.Trim()
                };
            }

            return toolName switch
            {
                CodingExplorationToolNames.ReadFile => ParseRead(reader),
                CodingExplorationToolNames.Grep => ParseGrep(reader),
                CodingExplorationToolNames.GlobSearch => ParseGlob(reader),
                CodingExplorationToolNames.ListDirectory => ParseList(reader),
                _ => new UnknownExplorationSummary()
            };
        }
        catch (XmlException)
        {
            return new UnknownExplorationSummary();
        }
        catch (InvalidOperationException)
        {
            return new UnknownExplorationSummary();
        }
    }

    private static CodingExplorationSummary ParseRead(XmlReader reader)
    {
        var unchanged = string.Equals(reader.Name, "file_unchanged", StringComparison.Ordinal);
        var path = reader.GetAttribute("path");
        return new ReadFileExplorationSummary
        {
            Path = path,
            StartLine = ReadInt(reader, "start_line"),
            LinesRead = ReadInt(reader, "lines_read"),
            TotalLines = ReadInt(reader, "total_lines"),
            Truncated = ReadBool(reader, "truncated"),
            Coverage = reader.GetAttribute("coverage"),
            HasMore = HasChild(reader, "next_read"),
            Unchanged = unchanged
        };
    }

    private static CodingExplorationSummary ParseGrep(XmlReader reader)
        => new GrepExplorationSummary
        {
            Path = reader.GetAttribute("path"),
            Pattern = reader.GetAttribute("pattern"),
            OutputMode = reader.GetAttribute("output_mode"),
            TotalResults = reader.GetAttribute("total_results"),
            TotalMatches = reader.GetAttribute("total_matches"),
            Truncated = ReadBool(reader, "truncated"),
            TruncationReason = reader.GetAttribute("truncation_reason"),
            Status = reader.GetAttribute("status"),
            HasMore = HasChild(reader, "next_grep")
        };

    private static CodingExplorationSummary ParseGlob(XmlReader reader)
        => new GlobExplorationSummary
        {
            Path = reader.GetAttribute("path") ?? reader.GetAttribute("effective_path"),
            Pattern = reader.GetAttribute("pattern"),
            OriginalPattern = reader.GetAttribute("original_pattern"),
            TotalMatches = reader.GetAttribute("total_matches"),
            MatchesRead = ReadInt(reader, "matches_read"),
            IgnoredCount = ReadInt(reader, "ignored_count"),
            Truncated = ReadBool(reader, "truncated"),
            TruncationReason = reader.GetAttribute("truncation_reason"),
            HasMore = HasChild(reader, "next_glob")
        };

    private static CodingExplorationSummary ParseList(XmlReader reader)
        => new ListDirectoryExplorationSummary
        {
            Path = reader.GetAttribute("path"),
            Recursive = ReadBool(reader, "recursive"),
            EntriesRead = ReadInt(reader, "entries_read"),
            TotalEntries = reader.GetAttribute("total_entries"),
            IgnoredCount = ReadInt(reader, "ignored_count"),
            Truncated = ReadBool(reader, "truncated"),
            TruncationReason = reader.GetAttribute("truncation_reason"),
            HasMore = HasChild(reader, "next_list")
        };

    private static string? GetResultText(ToolResultPayload result)
    {
        if (!string.IsNullOrWhiteSpace(result.Text))
        {
            return result.Text;
        }

        if (result.Json is { ValueKind: JsonValueKind.String } json)
        {
            return json.GetString();
        }

        return null;
    }

    private static bool MoveToFirstElement(XmlReader reader)
    {
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasChild(XmlReader reader, string childName)
    {
        if (reader.IsEmptyElement)
        {
            return false;
        }

        using var subtree = reader.ReadSubtree();
        while (subtree.Read())
        {
            if (subtree.NodeType == XmlNodeType.Element &&
                string.Equals(subtree.Name, childName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int ReadInt(XmlReader reader, string attributeName)
        => int.TryParse(reader.GetAttribute(attributeName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    private static bool ReadBool(XmlReader reader, string attributeName)
        => bool.TryParse(reader.GetAttribute(attributeName), out var value) && value;
}
