using System.Text;
using DiffPlex;
using DiffPlex.DiffBuilder;
using HPD.Agent;

/// <summary>
/// WriteFile implementation for CodingToolkit (partial class).
/// Writes content to files with diff preview for existing files.
/// </summary>
public partial class CodingToolkit
{
    [AIFunction]
    [AIDescription("Write content to a file. Shows diff if file exists. Creates directories if needed.")]
    public string WriteFile(
        [AIDescription("Absolute path to the file to write.")] string filePath,
        [AIDescription("Content to write to the file.")] string content)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var sb = new StringBuilder();
            var fileExists = File.Exists(filePath);

            if (fileExists)
            {
                var originalContent = File.ReadAllText(filePath);

                // Generate diff
                var diffBuilder = new InlineDiffBuilder(new Differ());
                var diff = diffBuilder.BuildDiffModel(originalContent, content);

                var additions = diff.Lines.Count(l => l.Type == DiffPlex.DiffBuilder.Model.ChangeType.Inserted);
                var deletions = diff.Lines.Count(l => l.Type == DiffPlex.DiffBuilder.Model.ChangeType.Deleted);

                sb.AppendLine($"Modifying: {filePath}");
                sb.AppendLine($"Changes: +{additions} -{deletions} lines");
                sb.AppendLine("---");

                // Show condensed diff
                sb.Append(GenerateDiffDisplay(diff, maxLines: 50));
            }
            else
            {
                sb.AppendLine($"Creating: {filePath}");
                sb.AppendLine($"Size: {content.Length} characters, {content.Split('\n').Length} lines");
            }

            // Write the file
            File.WriteAllText(filePath, content);

            sb.AppendLine("---");
            sb.AppendLine($"✓ Successfully wrote to {filePath}");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error writing file: {ex.Message}";
        }
    }
}
