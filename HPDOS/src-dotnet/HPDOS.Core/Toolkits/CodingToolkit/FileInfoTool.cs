using System.Text;
using HPD.Agent;

/// <summary>
/// GetFileInfo implementation for CodingToolkit (partial class).
/// Gets detailed metadata about a file.
/// </summary>
public partial class CodingToolkit
{
    [AIFunction]
    [AIDescription("Get detailed information about a file.")]
    public string GetFileInfo(
        [AIDescription("Absolute path to the file.")] string filePath)
    {
        if (!File.Exists(filePath))
            return $"Error: File not found: {filePath}";

        try
        {
            var info = new FileInfo(filePath);
            var ext = info.Extension;
            var mimeType = GetMimeType(ext);
            var isBinary = BinaryExtensions.Contains(ext);

            var sb = new StringBuilder();
            sb.AppendLine($"File: {info.Name}");
            sb.AppendLine($"Path: {info.FullName}");
            sb.AppendLine($"Size: {FormatFileSize(info.Length)} ({info.Length:N0} bytes)");
            sb.AppendLine($"Type: {mimeType}");
            sb.AppendLine($"Binary: {isBinary}");
            sb.AppendLine($"Created: {info.CreationTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Modified: {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Accessed: {info.LastAccessTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"ReadOnly: {info.IsReadOnly}");

            if (!isBinary)
            {
                var lines = File.ReadAllLines(filePath);
                sb.AppendLine($"Lines: {lines.Length}");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error getting file info: {ex.Message}";
        }
    }
}
