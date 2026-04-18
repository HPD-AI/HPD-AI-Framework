using System.Text;
using DiffPlex;
using DiffPlex.DiffBuilder;
using HPD.Agent;

/// <summary>
/// DiffFiles implementation for CodingToolkit (partial class).
/// Compares two files and shows differences.
/// </summary>
public partial class CodingToolkit
{
    [AIFunction]
    [AIDescription("Compare two files and show differences.")]
    public string DiffFiles(
        [AIDescription("Path to the original file.")] string originalPath,
        [AIDescription("Path to the modified file.")] string modifiedPath)
    {
        if (!File.Exists(originalPath))
            return $"Error: Original file not found: {originalPath}";
        if (!File.Exists(modifiedPath))
            return $"Error: Modified file not found: {modifiedPath}";

        try
        {
            var original = File.ReadAllText(originalPath);
            var modified = File.ReadAllText(modifiedPath);

            var diffBuilder = new SideBySideDiffBuilder(new Differ());
            var diff = diffBuilder.BuildDiffModel(original, modified);

            var sb = new StringBuilder();

            sb.AppendLine($"Diff: {Path.GetFileName(originalPath)} ↔ {Path.GetFileName(modifiedPath)}");
            sb.AppendLine("---");

            var lineNum = 0;
            foreach (var (oldLine, newLine) in diff.OldText.Lines.Zip(diff.NewText.Lines))
            {
                lineNum++;
                if (oldLine.Type == DiffPlex.DiffBuilder.Model.ChangeType.Unchanged && newLine.Type == DiffPlex.DiffBuilder.Model.ChangeType.Unchanged)
                    continue;

                if (oldLine.Type == DiffPlex.DiffBuilder.Model.ChangeType.Deleted)
                    sb.AppendLine($"-{lineNum,4}│ {oldLine.Text}");
                if (newLine.Type == DiffPlex.DiffBuilder.Model.ChangeType.Inserted)
                    sb.AppendLine($"+{lineNum,4}│ {newLine.Text}");
                if (oldLine.Type == DiffPlex.DiffBuilder.Model.ChangeType.Modified)
                {
                    sb.AppendLine($"-{lineNum,4}│ {oldLine.Text}");
                    sb.AppendLine($"+{lineNum,4}│ {newLine.Text}");
                }
            }

            return sb.Length > 0 ? sb.ToString() : "Files are identical.";
        }
        catch (Exception ex)
        {
            return $"Error comparing files: {ex.Message}";
        }
    }
}
