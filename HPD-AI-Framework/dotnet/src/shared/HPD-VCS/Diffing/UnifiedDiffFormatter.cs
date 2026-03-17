using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using System.Text;

namespace HPD.VCS.Diffing;

public static class UnifiedDiffFormatter
{
    private static readonly IDiffer Differ = new Differ();
    private static readonly InlineDiffBuilder DiffBuilder = new(Differ);

    /// <summary>
    /// Generates raw DiffLine sequence from old and new text for internal use
    /// </summary>
    public static IEnumerable<DiffLine> GenerateDiffLines(string? oldText, string? newText)
    {
        // Handle null cases
        oldText ??= string.Empty;
        newText ??= string.Empty;

        // Get the diff result
        var diffResult = DiffBuilder.BuildDiffModel(oldText, newText);

        // Convert to our DiffLine format
        foreach (var line in diffResult.Lines)
        {
            var diffLineType = line.Type switch
            {
                ChangeType.Unchanged => DiffLineType.Unchanged,
                ChangeType.Inserted => DiffLineType.Added,
                ChangeType.Deleted => DiffLineType.Removed,
                _ => DiffLineType.Unchanged
            };

            yield return new DiffLine(diffLineType, line.Text ?? string.Empty);
        }
    }

    /// <summary>
    /// Takes the raw line-by-line diff result and formats it into the standard unified diff format
    /// with ---, +++, and @@ -l,s +l,s @@ hunk headers.
    /// </summary>
    /// <param name="lines">The diff lines to format</param>
    /// <param name="contextLines">Number of context lines to include around changes</param>
    /// <returns>Formatted unified diff string</returns>
    public static string Format(IReadOnlyList<DiffLine> lines, int contextLines = 3)
    {
        if (lines.Count == 0)
            return string.Empty;

        var result = new StringBuilder();
        var hunks = GroupIntoHunks(lines, contextLines);

        foreach (var hunk in hunks)
        {
            // Generate hunk header: @@ -oldStart,oldCount +newStart,newCount @@
            var (oldStart, oldCount, newStart, newCount) = CalculateHunkRange(hunk);
            result.AppendLine($"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@");

            // Add hunk lines with prefixes
            foreach (var line in hunk)
            {
                var prefix = line.Type switch
                {
                    DiffLineType.Unchanged => " ",
                    DiffLineType.Removed => "-",
                    DiffLineType.Added => "+",
                    _ => " "
                };
                result.AppendLine($"{prefix}{line.Content}");
            }
        }

        return result.ToString();
    }

    private static List<List<DiffLine>> GroupIntoHunks(IReadOnlyList<DiffLine> lines, int contextLines)
    {
        var hunks = new List<List<DiffLine>>();
        var currentHunk = new List<DiffLine>();
        var unchangedBuffer = new List<DiffLine>();

        foreach (var line in lines)
        {
            if (line.Type == DiffLineType.Unchanged)
            {
                unchangedBuffer.Add(line);
                
                // If we have too many unchanged lines, start a new hunk
                if (unchangedBuffer.Count > contextLines * 2 && currentHunk.Count > 0)
                {
                    // Add trailing context to current hunk
                    var trailingContext = unchangedBuffer.Take(contextLines);
                    currentHunk.AddRange(trailingContext);
                    
                    // Finish current hunk
                    hunks.Add(currentHunk);
                    
                    // Start new hunk with leading context
                    currentHunk = new List<DiffLine>();
                    var leadingContext = unchangedBuffer.Skip(unchangedBuffer.Count - contextLines);
                    currentHunk.AddRange(leadingContext);
                    
                    unchangedBuffer.Clear();
                }
            }
            else
            {
                // Add all buffered unchanged lines as context
                if (currentHunk.Count == 0)
                {
                    // Leading context - take at most contextLines
                    var leadingContext = unchangedBuffer.Skip(Math.Max(0, unchangedBuffer.Count - contextLines));
                    currentHunk.AddRange(leadingContext);
                }
                else
                {
                    // Middle context - add all buffered lines
                    currentHunk.AddRange(unchangedBuffer);
                }
                
                unchangedBuffer.Clear();
                currentHunk.Add(line);
            }
        }

        // Add any remaining lines to current hunk
        if (currentHunk.Count > 0)
        {
            // Add trailing context
            var trailingContext = unchangedBuffer.Take(contextLines);
            currentHunk.AddRange(trailingContext);
            hunks.Add(currentHunk);
        }

        return hunks;
    }

    private static (int oldStart, int oldCount, int newStart, int newCount) CalculateHunkRange(List<DiffLine> hunk)
    {
        int oldLineNumber = 1, newLineNumber = 1;
        int oldCount = 0, newCount = 0;
        int oldStart = oldLineNumber, newStart = newLineNumber;
        bool foundFirstChange = false;

        foreach (var line in hunk)
        {
            if (!foundFirstChange && line.Type != DiffLineType.Unchanged)
            {
                oldStart = oldLineNumber;
                newStart = newLineNumber;
                foundFirstChange = true;
            }

            switch (line.Type)
            {
                case DiffLineType.Unchanged:
                    oldLineNumber++;
                    newLineNumber++;
                    oldCount++;
                    newCount++;
                    break;
                case DiffLineType.Removed:
                    oldLineNumber++;
                    oldCount++;
                    break;
                case DiffLineType.Added:
                    newLineNumber++;
                    newCount++;
                    break;
            }
        }

        return (oldStart, oldCount, newStart, newCount);
    }
}
