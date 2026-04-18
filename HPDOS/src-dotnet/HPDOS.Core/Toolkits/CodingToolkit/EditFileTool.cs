using System.Text;
using System.Text.RegularExpressions;
using DiffPlex;
using DiffPlex.DiffBuilder;
using HPD.Agent;

/// <summary>
/// EditFile implementation for CodingToolkit (partial class).
/// Smart file editing with multiple match strategies: exact, flexible whitespace, regex fuzzy.
/// </summary>
public partial class CodingToolkit
{
    [AIFunction]
    [AIDescription("Edit a file by replacing exact string matches. Uses smart matching: tries exact match first, then flexible whitespace matching, then regex fuzzy matching. PREFERRED over WriteFile for targeted changes.")]
    public string EditFile(
        [AIDescription("Absolute path to the file to edit.")] string filePath,
        [AIDescription("Exact string to find and replace (will try smart matching if exact match fails).")] string oldString,
        [AIDescription("New string to replace with.")] string newString,
        [AIDescription("Replace all occurrences (true) or just first (false). Default: false")] bool replaceAll = false)
    {
        if (!File.Exists(filePath))
            return $"Error: File not found: {filePath}";

        if (string.IsNullOrEmpty(oldString))
            return "Error: oldString cannot be empty";

        if (oldString == newString)
            return "Error: oldString and newString are identical - no changes needed";

        try
        {
            var content = File.ReadAllText(filePath);

            // Use smart replacement
            var replacementResult = CalculateSmartReplacement(content, oldString, newString, replaceAll);

            if (replacementResult.Occurrences == 0)
            {
                return $"Error: Could not find the specified text in the file.\n" +
                       $"Looking for: {oldString[..Math.Min(100, oldString.Length)]}...\n" +
                       $"Tried: exact match, flexible whitespace matching, and regex fuzzy matching.";
            }

            if (!replaceAll && replacementResult.Occurrences > 1 && replacementResult.Strategy == "exact match")
            {
                return $"Error: Found {replacementResult.Occurrences} occurrences of the text. " +
                       $"Either set replaceAll=true or provide more context to make the match unique.";
            }

            var newContent = replacementResult.NewContent;

            // Generate diff for preview
            var diffBuilder = new InlineDiffBuilder(new Differ());
            var diff = diffBuilder.BuildDiffModel(content, newContent);

            var additions = diff.Lines.Count(l => l.Type == DiffPlex.DiffBuilder.Model.ChangeType.Inserted);
            var deletions = diff.Lines.Count(l => l.Type == DiffPlex.DiffBuilder.Model.ChangeType.Deleted);

            var sb = new StringBuilder();

            sb.AppendLine($"Editing: {filePath}");
            sb.AppendLine($"Strategy: {replacementResult.Strategy}");
            sb.AppendLine($"Replacements: {replacementResult.Occurrences} occurrence(s)");
            sb.AppendLine($"Changes: +{additions} -{deletions} lines");
            sb.AppendLine("---");

            // Show diff (condensed)
            sb.Append(GenerateDiffDisplay(diff));

            // Write the file
            File.WriteAllText(filePath, newContent);

            sb.AppendLine("---");
            sb.AppendLine($"✓ Successfully edited {filePath}");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error editing file: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // SMART EDIT HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private record SmartReplacementResult(string NewContent, int Occurrences, string Strategy);

    /// <summary>
    /// Tries multiple strategies to find and replace text: exact, flexible (whitespace-insensitive), and regex fuzzy
    /// </summary>
    private SmartReplacementResult CalculateSmartReplacement(string currentContent, string oldString, string newString, bool replaceAll)
    {
        // Normalize line endings to \n for consistent processing
        var normalizedContent = currentContent.Replace("\r\n", "\n");
        var normalizedOldString = oldString.Replace("\r\n", "\n");
        var normalizedNewString = newString.Replace("\r\n", "\n");

        // Strategy 1: Exact match
        var exactOccurrences = CountOccurrences(normalizedContent, normalizedOldString);
        if (exactOccurrences > 0)
        {
            string result;
            if (replaceAll)
            {
                result = normalizedContent.Replace(normalizedOldString, normalizedNewString);
            }
            else
            {
                result = ReplaceFirst(normalizedContent, normalizedOldString, normalizedNewString);
            }
            return new SmartReplacementResult(result, exactOccurrences, "exact match");
        }

        // Strategy 2: Flexible match (ignores whitespace differences and indentation)
        var flexibleResult = FlexibleReplace(normalizedContent, normalizedOldString, normalizedNewString, replaceAll);
        if (flexibleResult.Occurrences > 0)
        {
            return new SmartReplacementResult(flexibleResult.NewContent, flexibleResult.Occurrences, "flexible whitespace match");
        }

        // Strategy 3: Regex fuzzy match (tokenizes and allows flexible whitespace)
        var regexResult = RegexFuzzyReplace(normalizedContent, normalizedOldString, normalizedNewString);
        if (regexResult.Occurrences > 0)
        {
            return new SmartReplacementResult(regexResult.NewContent, regexResult.Occurrences, "regex fuzzy match");
        }

        // No matches found
        return new SmartReplacementResult(currentContent, 0, "no match");
    }

    /// <summary>
    /// Flexible replacement that ignores indentation differences
    /// </summary>
    private (string NewContent, int Occurrences) FlexibleReplace(string content, string search, string replace, bool replaceAll)
    {
        var sourceLines = content.Split('\n');
        var searchLines = search.Split('\n').Select(l => l.Trim()).ToArray();
        var replaceLines = replace.Split('\n');

        if (searchLines.Length == 0)
            return (content, 0);

        int occurrences = 0;
        int i = 0;

        while (i <= sourceLines.Length - searchLines.Length)
        {
            var window = sourceLines.Skip(i).Take(searchLines.Length).ToArray();
            var windowStripped = window.Select(l => l.Trim()).ToArray();

            if (windowStripped.SequenceEqual(searchLines))
            {
                occurrences++;

                // Preserve the indentation of the first line
                var firstLineIndentation = GetIndentation(window[0]);
                var indentedReplace = replaceLines.Select(line => firstLineIndentation + line.TrimStart());

                // Replace this section
                var before = sourceLines.Take(i);
                var after = sourceLines.Skip(i + searchLines.Length);
                sourceLines = before.Concat(indentedReplace).Concat(after).ToArray();

                i += replaceLines.Length;

                if (!replaceAll)
                    break;
            }
            else
            {
                i++;
            }
        }

        return (string.Join("\n", sourceLines), occurrences);
    }

    /// <summary>
    /// Regex-based fuzzy matching - tokenizes the search string and allows flexible whitespace
    /// </summary>
    private (string NewContent, int Occurrences) RegexFuzzyReplace(string content, string search, string replace)
    {
        var delimiters = new[] { '(', ')', ':', '[', ']', '{', '}', '>', '<', '=' };

        var processedSearch = search;
        foreach (var delim in delimiters)
        {
            processedSearch = processedSearch.Replace(delim.ToString(), $" {delim} ");
        }

        var tokens = processedSearch.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
            return (content, 0);

        var escapedTokens = tokens.Select(Regex.Escape);
        var pattern = string.Join(@"\s*", escapedTokens);
        var finalPattern = @"^(\s*)" + pattern;

        try
        {
            var regex = new Regex(finalPattern, RegexOptions.Multiline);
            var match = regex.Match(content);

            if (!match.Success)
                return (content, 0);

            var indentation = match.Groups[1].Value;
            var replaceLines = replace.Split('\n');
            var indentedReplace = string.Join("\n", replaceLines.Select(line => indentation + line.TrimStart()));

            var result = regex.Replace(content, indentedReplace, 1);

            return (result, 1);
        }
        catch (ArgumentException)
        {
            return (content, 0);
        }
    }
}
