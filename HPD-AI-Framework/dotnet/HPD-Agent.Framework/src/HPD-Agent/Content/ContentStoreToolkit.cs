using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileSystemGlobbing;

namespace HPD.Agent;

/// <summary>
/// Filesystem-like navigation tools for IContentStore.
/// Gives agents 7 idiomatic tools to browse, read, write, and manage content
/// using familiar filesystem metaphors (ls, cat, find, rm, stat).
/// </summary>
/// <remarks>
/// <para><b>Path Convention:</b></para>
/// <list type="bullet">
/// <item>/knowledge/api-docs.md — agent-scoped content</item>
/// <item>/memory/user-prefs.md — agent-scoped memory</item>
/// <item>/uploads/screenshot.png — session-scoped user uploads</item>
/// <item>/artifacts/report.md — session-scoped agent outputs</item>
/// </list>
/// <para><b>Scope Resolution:</b></para>
/// /uploads and /artifacts use scope=sessionId; all other folders use scope=agentName.
///
/// <para><b>Session ID Threading:</b></para>
/// FolderDiscoveryMiddleware calls SetSessionId() before each turn via the SetToolkit link
/// established by AgentBuilder — required for session-scoped folder access.
/// </remarks>
public class ContentStoreToolkit
{
    private readonly IContentStore _store;
    private readonly string _agentName;
    private string? _sessionId;

    // Session-scoped folder paths
    private static readonly HashSet<string> SessionScopedFolders =
        new(StringComparer.OrdinalIgnoreCase) { "uploads", "artifacts" };

    public ContentStoreToolkit(IContentStore store, string agentName)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _agentName = agentName ?? throw new ArgumentNullException(nameof(agentName));
    }

    /// <summary>
    /// Called by FolderDiscoveryMiddleware each turn to propagate the current session ID.
    /// Required for session-scoped folder access (/uploads, /artifacts).
    /// </summary>
    internal void SetSessionId(string? sessionId)
    {
        _sessionId = sessionId;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Core Navigation Tools
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Read content by path. Returns the content as text.</summary>
    [AIFunction(Name = "content_read")]
    [Description("Read content by path. Use content_list() first to discover what files are available. Example: content_read('/knowledge/api-docs.md')")]
    public async Task<string> ReadAsync(
        [Description("Content path, e.g. '/knowledge/api-docs.md' or '/memory/preferences.md'")] string path,
        [Description("Line offset to start reading from (0-based, optional)")] int? offset = null,
        [Description("Maximum number of lines to read (optional)")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var (scope, folderName, name) = ResolvePath(path);
        if (name == null)
            return $"Error: Path must include a filename. Example: '{path.TrimEnd('/')}/document.md'. Use content_list('{path}') to see available files.";

        // Look up by name in the folder
        var results = await _store.QueryAsync(scope, new ContentQuery
        {
            Name = name,
            Tags = new Dictionary<string, string> { ["folder"] = $"/{folderName}" }
        }, cancellationToken);

        if (results.Count == 0)
        {
            // Try direct ID lookup as fallback
            var byId = await _store.GetAsync(scope, name, cancellationToken);
            if (byId == null)
                return $"Error: '{path}' not found. Use content_list('/{folderName}') to see available files.";
            return ExtractText(byId.Data, byId.ContentType, byId.Info.Name, offset, limit);
        }

        var content = await _store.GetAsync(scope, results[0].Id, cancellationToken);
        if (content == null)
            return $"Error: Failed to read '{path}'.";

        return ExtractText(content.Data, content.ContentType, content.Info.Name, offset, limit);
    }

    /// <summary>Write content to a writable folder path.</summary>
    [AIFunction(Name = "content_write")]
    [Description("Write or update content at a path. Only works in writable folders (/memory, /artifacts). Example: content_write('/memory/preferences.md', 'User prefers email notifications')")]
    public async Task<string> WriteAsync(
        [Description("Destination path, e.g. '/memory/preferences.md'")] string path,
        [Description("Content to write")] string content,
        CancellationToken cancellationToken = default)
    {
        var (scope, folderName, name) = ResolvePath(path);
        if (name == null)
            return $"Error: Path must include a filename. Example: '{path.TrimEnd('/')}/document.md'";

        // Permission check
        var options = ContentStoreExtensions.GetFolderOptions(_store, folderName);
        if (options != null && !options.Permissions.HasFlag(ContentPermissions.Write))
            return $"Error: Folder '/{folderName}' is read-only. Cannot write to this location.";

        var data = Encoding.UTF8.GetBytes(content);
        var id = await _store.PutAsync(scope, data, "text/plain",
            new ContentMetadata
            {
                Name = name,
                Origin = ContentSource.Agent,
                Tags = new Dictionary<string, string> { ["folder"] = $"/{folderName}" }
            }, cancellationToken);

        return $"Written: /{folderName}/{name} (id: {id}, {data.Length} bytes)";
    }

    /// <summary>Find content by glob pattern.</summary>
    [AIFunction(Name = "content_glob")]
    [Description("Find content by filename pattern. Supports * (any chars), ? (single char), ** (recursive). Example: content_glob('*api*', '/knowledge') finds all files with 'api' in the name")]
    public async Task<string> GlobAsync(
        [Description("Glob pattern, e.g. '*.md', '*auth*', 'api-*'")] string pattern,
        [Description("Folder path to search (defaults to root '/')")] string? path = null,
        CancellationToken cancellationToken = default)
    {
        var folderFilter = path != null ? path.Trim('/') : null;

        // Build query
        ContentQuery? query = folderFilter != null
            ? new ContentQuery { Tags = new Dictionary<string, string> { ["folder"] = $"/{folderFilter}" } }
            : null;

        var scope = folderFilter != null && SessionScopedFolders.Contains(folderFilter)
            ? _sessionId
            : _agentName;

        var results = await _store.QueryAsync(scope, query, cancellationToken);

        // Apply glob pattern to names using Microsoft.Extensions.FileSystemGlobbing
        var matcher = new Matcher();
        matcher.AddInclude(pattern);

        var matched = results
            .Where(r => matcher.Match(r.Name).HasMatches)
            .ToList();

        if (matched.Count == 0)
            return $"No files matching '{pattern}'" + (path != null ? $" in '{path}'" : "") + ".";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {matched.Count} file(s) matching '{pattern}':");
        foreach (var m in matched)
            sb.AppendLine($"  /{(folderFilter ?? ExtractFolderFromTags(m))}/{m.Name}");

        return sb.ToString().TrimEnd();
    }

    /// <summary>List contents of a folder (like ls).</summary>
    [AIFunction(Name = "content_list")]
    [Description("List files in a folder. Example: content_list('/knowledge') shows all knowledge documents. content_list('/') lists all available folders.")]
    public async Task<string> ListAsync(
        [Description("Folder path to list (e.g. '/knowledge', '/memory'). Defaults to '/' to list all folders.")] string? path = null,
        CancellationToken cancellationToken = default)
    {
        // Root listing — show available folders
        if (path == null || path == "/" || path == "")
        {
            var folders = await _store.ListFoldersAsync(cancellationToken);

            // Add session folders if we have a session
            var allFolders = new List<FolderInfo>(folders);
            if (_sessionId != null)
            {
                allFolders.Add(new FolderInfo
                {
                    Name = "uploads",
                    Path = "/uploads",
                    Description = "User-uploaded files for this session",
                    Permissions = ContentPermissions.Read,
                    Scope = "session"
                });
                allFolders.Add(new FolderInfo
                {
                    Name = "artifacts",
                    Path = "/artifacts",
                    Description = "Agent-generated outputs for this session",
                    Permissions = ContentPermissions.ReadWrite,
                    Scope = "session"
                });
            }

            if (allFolders.Count == 0)
                return "No folders available. Use UseDefaultContentStore() to set up /skills, /knowledge, /memory.";

            var sb = new StringBuilder();
            sb.AppendLine("/");
            foreach (var f in allFolders.OrderBy(f => f.Path))
                sb.AppendLine($"  {f.Path}/ - {f.Description} ({f.Permissions.ToString().ToLower()}, {f.Scope}-scoped)");
            return sb.ToString().TrimEnd();
        }

        // Folder listing
        var (scope, folderName, _) = ResolvePath(path.TrimEnd('/') + "/dummy");
        var items = await _store.QueryAsync(scope, new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = $"/{folderName}" }
        }, cancellationToken);

        if (items.Count == 0)
            return $"/{folderName}/ is empty.";

        var result = new StringBuilder();
        result.AppendLine($"/{folderName}/ ({items.Count} file(s)):");
        foreach (var item in items.OrderBy(i => i.Name))
        {
            var size = FormatSize(item.SizeBytes);
            var desc = item.Description != null ? $" — {item.Description}" : "";
            result.AppendLine($"  {item.Name}  ({size}){desc}");
        }
        return result.ToString().TrimEnd();
    }

    /// <summary>Delete content by path.</summary>
    [AIFunction(Name = "content_delete")]
    [Description("Delete content by path. Only works in folders with delete permission. Example: content_delete('/memory/old-notes.md')")]
    public async Task<string> DeleteAsync(
        [Description("Content path to delete, e.g. '/memory/old-notes.md'")] string path,
        CancellationToken cancellationToken = default)
    {
        var (scope, folderName, name) = ResolvePath(path);
        if (name == null)
            return $"Error: Path must include a filename, e.g. '/{folderName}/document.md'";

        // Permission check
        var options = ContentStoreExtensions.GetFolderOptions(_store, folderName);
        if (options != null && !options.Permissions.HasFlag(ContentPermissions.Delete))
            return $"Error: Folder '/{folderName}' does not allow deletion.";

        // Find by name
        var results = await _store.QueryAsync(scope, new ContentQuery
        {
            Name = name,
            Tags = new Dictionary<string, string> { ["folder"] = $"/{folderName}" }
        }, cancellationToken);

        if (results.Count == 0)
            return $"Error: '{path}' not found.";

        await _store.DeleteAsync(scope, results[0].Id, cancellationToken);
        return $"Deleted: {path}";
    }

    /// <summary>Display folder structure tree (like tree command).</summary>
    [AIFunction(Name = "content_tree")]
    [Description("Display the folder structure and file count. Example: content_tree() shows all folders and their file counts.")]
    public async Task<string> TreeAsync(
        [Description("Starting path (defaults to root '/')")] string? path = null,
        [Description("Maximum depth to display")] int? depth = null,
        CancellationToken cancellationToken = default)
    {
        var folders = await _store.ListFoldersAsync(cancellationToken);
        var sb = new StringBuilder();
        sb.AppendLine(".");

        var allFolders = new List<FolderInfo>(folders);
        if (_sessionId != null)
        {
            allFolders.Add(new FolderInfo { Name = "uploads", Path = "/uploads", Description = "User uploads", Permissions = ContentPermissions.Read, Scope = "session" });
            allFolders.Add(new FolderInfo { Name = "artifacts", Path = "/artifacts", Description = "Agent artifacts", Permissions = ContentPermissions.ReadWrite, Scope = "session" });
        }

        foreach (var folder in allFolders.OrderBy(f => f.Path))
        {
            var scope = SessionScopedFolders.Contains(folder.Name) ? _sessionId : _agentName;
            var items = await _store.QueryAsync(scope, new ContentQuery
            {
                Tags = new Dictionary<string, string> { ["folder"] = folder.Path }
            }, cancellationToken);

            sb.AppendLine($"├── {folder.Path}/ ({items.Count} file(s))");

            if (depth != 1 && items.Count > 0)
            {
                var displayItems = items.OrderBy(i => i.Name).Take(10).ToList();
                foreach (var item in displayItems)
                    sb.AppendLine($"│   ├── {item.Name}  ({FormatSize(item.SizeBytes)})");
                if (items.Count > 10)
                    sb.AppendLine($"│   └── ... ({items.Count - 10} more)");
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Show content metadata (like stat command).</summary>
    [AIFunction(Name = "content_stat")]
    [Description("Show detailed metadata for a content item. Example: content_stat('/knowledge/api-docs.md') shows size, creation date, tags, etc.")]
    public async Task<string> StatAsync(
        [Description("Content path, e.g. '/knowledge/api-docs.md'")] string path,
        CancellationToken cancellationToken = default)
    {
        var (scope, folderName, name) = ResolvePath(path);
        if (name == null)
            return $"Error: Path must include a filename.";

        var results = await _store.QueryAsync(scope, new ContentQuery
        {
            Name = name,
            Tags = new Dictionary<string, string> { ["folder"] = $"/{folderName}" }
        }, cancellationToken);

        if (results.Count == 0)
            return $"Error: '{path}' not found.";

        var info = results[0];
        var sb = new StringBuilder();
        sb.AppendLine($"File: {path}");
        sb.AppendLine($"  ID:           {info.Id}");
        sb.AppendLine($"  Size:         {FormatSize(info.SizeBytes)}");
        sb.AppendLine($"  Content-Type: {info.ContentType}");
        sb.AppendLine($"  Created:      {info.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");
        if (info.LastModified.HasValue)
            sb.AppendLine($"  Modified:     {info.LastModified:yyyy-MM-dd HH:mm:ss} UTC");
        if (info.Description != null)
            sb.AppendLine($"  Description:  {info.Description}");
        sb.AppendLine($"  Origin:       {info.Origin}");
        if (info.Tags != null && info.Tags.Count > 0)
        {
            sb.AppendLine("  Tags:");
            foreach (var tag in info.Tags.Where(t => t.Key != "folder"))
                sb.AppendLine($"    {tag.Key}: {tag.Value}");
        }
        return sb.ToString().TrimEnd();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Path Resolution
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parse a virtual path into (scope, folderName, filename).
    /// /knowledge/api-docs.md → (agentName, "knowledge", "api-docs.md")
    /// /uploads/screenshot.png → (sessionId, "uploads", "screenshot.png")
    /// /memory/ → (agentName, "memory", null)
    /// </summary>
    private (string? scope, string folderName, string? fileName) ResolvePath(string path)
    {
        var parts = path.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
            return (_agentName, "", null);

        var folderName = parts[0];
        var fileName = parts.Length > 1 ? string.Join("/", parts.Skip(1)) : null;
        var scope = SessionScopedFolders.Contains(folderName) ? _sessionId : _agentName;

        return (scope, folderName, fileName);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static string ExtractText(byte[] data, string contentType, string name, int? offset, int? limit)
    {
        // For text-based content types, return as string
        if (contentType.StartsWith("text/") || contentType == "application/json" || contentType == "application/xml")
        {
            var text = Encoding.UTF8.GetString(data);
            if (offset == null && limit == null)
                return text;

            var lines = text.Split('\n');
            var start = offset ?? 0;
            var end = limit.HasValue ? Math.Min(start + limit.Value, lines.Length) : lines.Length;
            var slice = lines.Skip(start).Take(end - start);
            return string.Join('\n', slice);
        }

        // For binary content, return info
        return $"[Binary content: {name}, {data.Length} bytes, type={contentType}. " +
               $"Use the asset:// URI to access the raw data.]";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }

    private static string ExtractFolderFromTags(ContentInfo info)
    {
        if (info.Tags != null && info.Tags.TryGetValue("folder", out var folder))
            return folder.TrimStart('/');
        return "unknown";
    }
}
