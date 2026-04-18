using System.Text;
using HPD.Agent;

/// <summary>
/// ListDirectory implementation for CodingToolkit (partial class).
/// Lists directory contents with file metadata and .gitignore support.
/// </summary>
public partial class CodingToolkit
{
    [AIFunction]
    [AIDescription("List directory contents with file metadata. Respects .gitignore patterns.")]
    public string ListDirectory(
        [AIDescription("Absolute path to the directory to list. If empty, uses current working directory.")] string directoryPath = "",
        [AIDescription("Include hidden files/directories. Default: false")] bool showHidden = false)
    {
        // Default to current directory if not provided
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            directoryPath = Directory.GetCurrentDirectory();
        }

        if (!Directory.Exists(directoryPath))
            return $"Error: Directory not found: {directoryPath}";

        try
        {
            var sb = new StringBuilder();

            sb.AppendLine($"Directory: {directoryPath}");
            sb.AppendLine("---");

            // List directories first
            var dirs = Directory.GetDirectories(directoryPath)
                .Select(d => new DirectoryInfo(d))
                .Where(d => showHidden || !d.Name.StartsWith('.'))
                .Where(d => !DefaultIgnoreDirs.Contains(d.Name))
                .OrderBy(d => d.Name);

            foreach (var dir in dirs)
            {
                sb.AppendLine($"▸ {dir.Name}/");
            }

            // List files
            var files = Directory.GetFiles(directoryPath)
                .Select(f => new FileInfo(f))
                .Where(f => showHidden || !f.Name.StartsWith('.'))
                .OrderBy(f => f.Name);

            // Apply gitignore filtering
            var filteredFiles = FilterIgnoredFiles(files.Select(f => f.FullName), directoryPath)
                .Select(p => new FileInfo(p));

            foreach (var file in filteredFiles)
            {
                var size = FormatFileSize(file.Length);
                var modified = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                sb.AppendLine($"📄 {file.Name,-40} {size,10} {modified}");
            }

            var dirCount = dirs.Count();
            var fileCount = filteredFiles.Count();

            sb.AppendLine("---");
            sb.AppendLine($"Total: {dirCount} directories, {fileCount} files");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error listing directory: {ex.Message}";
        }
    }
}
