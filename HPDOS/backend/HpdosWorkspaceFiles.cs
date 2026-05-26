internal sealed record HpdosWorkspaceFileNode(
    string Name,
    string Path,
    string Absolute,
    string Type,
    bool Ignored,
    long? Size,
    string? ModifiedAt);

internal sealed record HpdosWorkspaceFileContent(
    string Name,
    string Path,
    string Absolute,
    string Type,
    string Content,
    string? Encoding,
    string? MimeType,
    long Size,
    string ModifiedAt);

internal sealed class HpdosWorkspaceFileService
{
    private const int DefaultSearchLimit = 100;
    private const int MaxSearchLimit = 500;
    private const long MaxReadBytes = 2 * 1024 * 1024;

    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".hg",
        ".svn",
        ".DS_Store",
        "bin",
        "obj",
        "node_modules",
        "dist",
        "build",
        ".hpdos"
    };

    private readonly HpdosWorkspaceStoreService workspaces;

    public HpdosWorkspaceFileService(HpdosWorkspaceStoreService workspaces)
    {
        this.workspaces = workspaces;
    }

    public async Task<IReadOnlyList<HpdosWorkspaceFileNode>> ListAsync(string? rootId, string? path, CancellationToken ct)
    {
        var root = await ResolveRootAsync(rootId, ct);
        var directory = ResolveWorkspacePath(root, path);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Directory was not found: {DisplayPath(root, directory)}");

        return Directory.EnumerateFileSystemEntries(directory)
            .Select(entry => ToNode(root, entry))
            .OrderBy(node => node.Type == "directory" ? 0 : 1)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<HpdosWorkspaceFileNode>> SearchAsync(string? rootId, string? query, string? type, int? limit, CancellationToken ct)
    {
        var root = await ResolveRootAsync(rootId, ct);
        var normalizedQuery = (query ?? "").Trim();
        if (normalizedQuery.Length == 0)
            return [];

        var wantedType = NormalizeSearchType(type);
        var max = Math.Clamp(limit ?? DefaultSearchLimit, 1, MaxSearchLimit);
        var results = new List<HpdosWorkspaceFileNode>();
        var pending = new Stack<string>();
        pending.Push(root.Path);

        while (pending.Count > 0 && results.Count < max)
        {
            ct.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(directory);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                var node = ToNode(root, entry);
                if (node.Type == "directory" && !node.Ignored)
                    pending.Push(entry);

                if (node.Ignored) continue;
                if (wantedType != "all" && node.Type != wantedType) continue;
                if (!node.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                    && !node.Path.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)) continue;

                results.Add(node);
                if (results.Count >= max) break;
            }
        }

        return results
            .OrderBy(node => node.Type == "directory" ? 0 : 1)
            .ThenBy(node => node.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<HpdosWorkspaceFileContent> ReadAsync(string? rootId, string? path, CancellationToken ct)
    {
        var root = await ResolveRootAsync(rootId, ct);
        var file = ResolveWorkspacePath(root, path);
        if (!File.Exists(file))
            throw new FileNotFoundException($"File was not found: {DisplayPath(root, file)}");

        var info = new FileInfo(file);
        if (info.Length > MaxReadBytes)
            throw new IOException($"File is too large to preview: {DisplayPath(root, file)}");

        var bytes = await File.ReadAllBytesAsync(file, ct);
        var mime = ContentType(file);
        var fileType = IsImageMime(mime) ? "image" : IsBinary(bytes) ? "binary" : "text";
        var content = fileType == "text"
            ? System.Text.Encoding.UTF8.GetString(bytes)
            : Convert.ToBase64String(bytes);

        return new HpdosWorkspaceFileContent(
            Name: Path.GetFileName(file),
            Path: RelativePath(root, file),
            Absolute: file,
            Type: fileType,
            Content: content,
            Encoding: fileType == "text" ? null : "base64",
            MimeType: mime,
            Size: info.Length,
            ModifiedAt: info.LastWriteTimeUtc.ToString("O"));
    }

    private async Task<HpdosWorkspaceRoot> ResolveRootAsync(string? rootId, CancellationToken ct)
    {
        var workspace = await workspaces.GetActiveWorkspaceAsync(ct)
            ?? throw new ArgumentException("No active workspace is configured.");
        var root = string.IsNullOrWhiteSpace(rootId)
            ? workspace.Roots.FirstOrDefault(root => string.Equals(root.Id, workspace.DefaultRootId, StringComparison.OrdinalIgnoreCase))
                ?? workspace.Roots.FirstOrDefault()
            : workspace.Roots.FirstOrDefault(root => string.Equals(root.Id, rootId, StringComparison.OrdinalIgnoreCase));

        if (root is null)
            throw new ArgumentException("Workspace root was not found.");

        if (!Directory.Exists(root.Path))
            throw new DirectoryNotFoundException($"Workspace root was not found: {root.Path}");

        return root;
    }

    private static string ResolveWorkspacePath(HpdosWorkspaceRoot root, string? relativePath)
    {
        var cleanRelative = (relativePath ?? "")
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root.Path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, cleanRelative));

        if (!IsPathInside(fullRoot, fullPath))
            throw new UnauthorizedAccessException("Path is outside the selected workspace root.");

        return fullPath;
    }

    private static HpdosWorkspaceFileNode ToNode(HpdosWorkspaceRoot root, string path)
    {
        var directory = Directory.Exists(path);
        var info = directory ? null : new FileInfo(path);
        var modifiedAt = directory
            ? Directory.GetLastWriteTimeUtc(path).ToString("O")
            : info?.LastWriteTimeUtc.ToString("O");
        return new HpdosWorkspaceFileNode(
            Name: Path.GetFileName(path),
            Path: RelativePath(root, path),
            Absolute: path,
            Type: directory ? "directory" : "file",
            Ignored: IsIgnored(path, directory),
            Size: directory ? null : info?.Length,
            ModifiedAt: modifiedAt);
    }

    private static bool IsPathInside(string root, string path)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(root, path, comparison)
            || path.StartsWith(root + Path.DirectorySeparatorChar, comparison)
            || path.StartsWith(root + Path.AltDirectorySeparatorChar, comparison);
    }

    private static string RelativePath(HpdosWorkspaceRoot root, string path)
    {
        var relative = Path.GetRelativePath(root.Path, path);
        return relative == "." ? "" : relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string DisplayPath(HpdosWorkspaceRoot root, string path)
    {
        var relative = RelativePath(root, path);
        return string.IsNullOrWhiteSpace(relative) ? root.Label : relative;
    }

    private static bool IsIgnored(string path, bool directory)
    {
        var name = Path.GetFileName(path);
        if (directory && IgnoredDirectoryNames.Contains(name)) return true;
        return name.StartsWith(".", StringComparison.Ordinal) && name is not "." and not "..";
    }

    private static string NormalizeSearchType(string? type)
    {
        return type is "file" or "directory" ? type : "all";
    }

    private static bool IsBinary(byte[] bytes)
    {
        if (bytes.Length == 0) return false;
        var sample = bytes.AsSpan(0, Math.Min(bytes.Length, 4096));
        return sample.Contains((byte)0);
    }

    private static bool IsImageMime(string mime) => mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    private static string ContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".avif" => "image/avif",
            ".css" => "text/css",
            ".gif" => "image/gif",
            ".htm" or ".html" => "text/html",
            ".jpeg" or ".jpg" => "image/jpeg",
            ".js" or ".mjs" => "text/javascript",
            ".json" => "application/json",
            ".md" => "text/markdown",
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            ".txt" => "text/plain",
            ".wasm" => "application/wasm",
            ".webp" => "image/webp",
            ".xml" => "application/xml",
            _ => "application/octet-stream"
        };
    }
}
