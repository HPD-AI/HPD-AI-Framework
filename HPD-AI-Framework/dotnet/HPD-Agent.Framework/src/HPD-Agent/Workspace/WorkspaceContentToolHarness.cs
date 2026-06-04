using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileSystemGlobbing;

namespace HPD.Agent;

/// <summary>
/// Agent-facing filesystem tools over the workspace content graph.
/// </summary>
public class WorkspaceContentToolHarness
{
    private readonly IWorkspaceStore _workspace;
    private readonly WorkspaceContentFileSystem _fileSystem;
    private readonly WorkspacePrincipalRef _principal;

    public WorkspaceContentToolHarness(
        IWorkspaceStore workspace,
        WorkspacePrincipalRef? principal = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _fileSystem = new WorkspaceContentFileSystem(_workspace);
        _principal = principal ?? WorkspacePrincipalRef.System;
    }

    [AIFunction(Name = "content_read")]
    [Description("Read workspace content by path. Use content_ls() first to discover available paths.")]
    public async Task<string> ReadAsync(
        [Description("Content path, e.g. '/projects/contract-review/summary.md' or '/sessions/session-1/branches/main/uploads/file.txt'")] string path,
        [Description("Line offset to start reading from (0-based, optional)")] int? offset = null,
        [Description("Maximum number of lines to read (optional)")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        return await RunToolAsync(async () =>
        {
            var stat = await _fileSystem.StatAsync(_principal, path, cancellationToken).ConfigureAwait(false);
            if (stat?.Attachment is null || stat.Content is null)
                return $"Error: '{path}' is not a content file.";

            await using var content = await _fileSystem.OpenReadAsync(_principal, path, cancellationToken).ConfigureAwait(false);
            if (content is null)
                return $"Error: Failed to read '{path}'.";

            return await ExtractTextAsync(content, stat.Content, offset, limit, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    [AIFunction(Name = "content_write")]
    [Description("Write or update workspace content at a path. Write to writable spaces or role directories such as session artifacts.")]
    public async Task<string> WriteAsync(
        [Description("Destination path, e.g. '/projects/contract-review/report.md' or '/sessions/session-1/branches/main/artifacts/report.md'")] string path,
        [Description("Content to write")] string content,
        CancellationToken cancellationToken = default)
    {
        return await RunToolAsync(async () =>
        {
            var entry = await _fileSystem.WriteTextAsync(_principal, path, content, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var bytes = Encoding.UTF8.GetByteCount(content);
            return $"Written: {entry.Path} (content_id: {entry.Attachment!.ContentId}, version: {entry.Attachment.ContentVersion}, {bytes} bytes)";
        }).ConfigureAwait(false);
    }

    [AIFunction(Name = "content_find")]
    [Description("Find workspace content by filename pattern. Supports * (any chars), ? (single char), ** (recursive).")]
    public async Task<string> FindAsync(
        [Description("Glob pattern, e.g. '*.md', '*auth*', 'api-*'")] string pattern,
        [Description("Workspace path to search. Defaults to root '/'.")] string? path = null,
        CancellationToken cancellationToken = default)
    {
        return await RunToolAsync(async () =>
        {
            var matcher = new Matcher();
            matcher.AddInclude(pattern);

            var root = WorkspaceContentPaths.NormalizePath(path);
            var query = await BuildFindQueryAsync(root, cancellationToken).ConfigureAwait(false);
            var matches = await _workspace.SearchContentAsync(_principal, query, cancellationToken)
                .ConfigureAwait(false);

            var pathResults = new List<(WorkspaceVisibleContentResult Result, string Path)>();
            foreach (var result in matches)
            {
                pathResults.Add((
                    result,
                    await BuildContentPathAsync(result, cancellationToken).ConfigureAwait(false)));
            }

            var filtered = pathResults
                .Where(entry => matcher.Match(entry.Result.Name).HasMatches || matcher.Match(entry.Path.TrimStart('/')).HasMatches)
                .OrderBy(entry => entry.Path)
                .ToList();

            if (filtered.Count == 0)
                return $"No files matching '{pattern}' in '{root}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Found {filtered.Count} file(s) matching '{pattern}':");
            foreach (var entry in filtered)
                sb.AppendLine($"  {entry.Path}");
            return sb.ToString().TrimEnd();
        }).ConfigureAwait(false);
    }

    [AIFunction(Name = "content_ls")]
    [Description("List workspace spaces, directories, and files by path. content_ls('/') lists workspace roots.")]
    public async Task<string> ListAsync(
        [Description("Workspace path to list, e.g. '/', '/projects', '/projects/contract-review'. Defaults to '/'.")] string? path = null,
        CancellationToken cancellationToken = default)
    {
        return await RunToolAsync(async () =>
        {
            var normalized = WorkspaceContentPaths.NormalizePath(path);
            var entries = await _fileSystem.ListAsync(_principal, normalized, cancellationToken).ConfigureAwait(false);
            if (entries.Count == 0)
                return $"{normalized}/ is empty.".Replace("//", "/", StringComparison.Ordinal);

            var sb = new StringBuilder();
            sb.AppendLine($"{normalized}:");
            foreach (var entry in entries.OrderBy(entry => entry.Kind).ThenBy(entry => entry.Name))
                sb.AppendLine("  " + FormatEntry(entry));
            return sb.ToString().TrimEnd();
        }).ConfigureAwait(false);
    }

    [AIFunction(Name = "content_detach")]
    [Description("Detach workspace content from the space path where it is visible.")]
    public async Task<string> DetachAsync(
        [Description("Content path to detach, e.g. '/projects/contract-review/old-report.md'")] string path,
        CancellationToken cancellationToken = default)
    {
        return await RunToolAsync(async () =>
        {
            await _fileSystem.DeleteAsync(_principal, path, cancellationToken).ConfigureAwait(false);
            return $"Detached: {WorkspaceContentPaths.NormalizePath(path)}";
        }).ConfigureAwait(false);
    }

    [AIFunction(Name = "content_attach")]
    [Description("Attach an existing workspace content object to a space path.")]
    public async Task<string> AttachAsync(
        [Description("Destination space or role directory path, e.g. '/projects/contract-review' or '/sessions/session-1/branches/main/artifacts'")] string spacePath,
        [Description("Existing content object id to attach")] string contentRef,
        [Description("Attachment filename. Defaults to the content object's name or id.")] string? name = null,
        [Description("Attachment role. Defaults to the destination directory role or generic content.")] string? role = null,
        CancellationToken cancellationToken = default)
    {
        return await RunToolAsync(async () =>
        {
            var destination = await _fileSystem.ResolveAsync(_principal, spacePath, cancellationToken).ConfigureAwait(false);
            if (destination.Space is null ||
                destination.Kind is not (WorkspaceContentPathKind.Space or WorkspaceContentPathKind.RoleDirectory))
            {
                return $"Error: '{spacePath}' is not an attachable workspace space.";
            }

            var content = await _workspace.StatContentAsync(_principal, contentRef, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (content is null)
                return $"Error: Content object '{contentRef}' was not found or is not accessible.";

            var attachmentRole = role ?? destination.Role ?? WorkspaceContentRoles.Content;
            var attachmentName = name ?? content.Name ?? content.Id;
            var attachment = await _workspace.AttachContentAsync(
                _principal,
                destination.Space.Id,
                content.Id,
                new AttachWorkspaceContentRequest
                {
                    Role = attachmentRole,
                    Name = attachmentName,
                    ContentVersion = content.Version,
                    Permission = WorkspacePermissions.ReadWrite
                },
                cancellationToken).ConfigureAwait(false);

            var path = destination.Kind == WorkspaceContentPathKind.RoleDirectory
                ? WorkspaceContentPaths.NormalizePath(spacePath) + "/" + WorkspaceContentPaths.NormalizeRelativePath(attachment.Name)
                : WorkspaceContentPaths.NormalizePath(spacePath) + "/" + WorkspaceContentPaths.NormalizeRelativePath(attachment.Name);

            return $"Attached: {path} (content_id: {attachment.ContentId}, version: {attachment.ContentVersion}, role: {attachment.Role})";
        }).ConfigureAwait(false);
    }

    [AIFunction(Name = "content_tree")]
    [Description("Display a recursive workspace content tree.")]
    public async Task<string> TreeAsync(
        [Description("Starting workspace path. Defaults to root '/'.")] string? path = null,
        [Description("Maximum depth to display. Defaults to 4.")] int? depth = null,
        CancellationToken cancellationToken = default)
    {
        return await RunToolAsync(async () =>
        {
            var normalized = WorkspaceContentPaths.NormalizePath(path);
            var maxDepth = Math.Max(0, depth ?? 4);
            var sb = new StringBuilder();
            sb.AppendLine(normalized);
            await AppendTreeAsync(sb, normalized, currentDepth: 0, maxDepth, cancellationToken).ConfigureAwait(false);
            return sb.ToString().TrimEnd();
        }).ConfigureAwait(false);
    }

    [AIFunction(Name = "content_stat")]
    [Description("Show detailed metadata for a workspace path.")]
    public async Task<string> StatAsync(
        [Description("Workspace path, e.g. '/projects/contract-review/summary.md'")] string path,
        CancellationToken cancellationToken = default)
    {
        return await RunToolAsync(async () =>
        {
            var stat = await _fileSystem.StatAsync(_principal, path, cancellationToken).ConfigureAwait(false);
            if (stat is null)
                return $"Error: '{path}' not found.";

            var sb = new StringBuilder();
            sb.AppendLine($"Path:         {stat.Path}");
            sb.AppendLine($"Kind:         {stat.Kind}");
            if (stat.Space is not null)
            {
                sb.AppendLine($"Space ID:     {stat.Space.Id}");
                sb.AppendLine($"Space Kind:   {stat.Space.Kind}");
                sb.AppendLine($"Space Name:   {stat.Space.Name}");
                sb.AppendLine($"Space Ver:    {stat.Space.Version}");
            }

            if (stat.Attachment is not null)
            {
                sb.AppendLine($"Attach ID:    {stat.Attachment.Id}");
                sb.AppendLine($"Role:         {stat.Attachment.Role}");
                sb.AppendLine($"Permission:   {stat.Attachment.Permission}");
                sb.AppendLine($"Attach Ver:   {stat.Attachment.Version}");
            }

            if (stat.Content is not null)
            {
                sb.AppendLine($"Content ID:   {stat.Content.Id}");
                sb.AppendLine($"Content Ver:  {stat.Content.Version}");
                sb.AppendLine($"Size:         {FormatSize(stat.Content.SizeBytes)}");
                sb.AppendLine($"Content-Type: {stat.Content.ContentType}");
                sb.AppendLine($"Checksum:     {stat.Content.Checksum}");
                sb.AppendLine($"Created:      {stat.Content.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");
                sb.AppendLine($"Updated:      {stat.Content.UpdatedAt:yyyy-MM-dd HH:mm:ss} UTC");
            }

            return sb.ToString().TrimEnd();
        }).ConfigureAwait(false);
    }

    private async Task AppendTreeAsync(
        StringBuilder sb,
        string path,
        int currentDepth,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        if (currentDepth >= maxDepth)
            return;

        var entries = await _fileSystem.ListAsync(_principal, path, cancellationToken).ConfigureAwait(false);
        foreach (var entry in entries.OrderBy(entry => entry.Kind).ThenBy(entry => entry.Name))
        {
            sb.Append(new string(' ', (currentDepth + 1) * 2));
            sb.AppendLine(FormatEntry(entry));
            if (entry.Kind is WorkspaceContentPathKind.Directory or WorkspaceContentPathKind.Space)
                await AppendTreeAsync(sb, entry.Path, currentDepth + 1, maxDepth, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<WorkspaceVisibleContentQuery> BuildFindQueryAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var resolved = await _fileSystem.ResolveAsync(_principal, path, cancellationToken).ConfigureAwait(false);

        return resolved.Kind switch
        {
            WorkspaceContentPathKind.Root => new WorkspaceVisibleContentQuery(),
            WorkspaceContentPathKind.RootKind => new WorkspaceVisibleContentQuery
            {
                SpaceKind = resolved.SpaceKind
            },
            WorkspaceContentPathKind.Space => new WorkspaceVisibleContentQuery
            {
                SpaceId = resolved.Space!.Id,
                TraversalMode = WorkspaceContentTraversalMode.SpaceDescendants
            },
            WorkspaceContentPathKind.RoleDirectory => new WorkspaceVisibleContentQuery
            {
                SpaceId = resolved.Space!.Id,
                Role = resolved.Role,
                TraversalMode = WorkspaceContentTraversalMode.SpaceOnly
            },
            WorkspaceContentPathKind.ChildDirectory => new WorkspaceVisibleContentQuery
            {
                SpaceId = resolved.Space!.Id,
                SpaceKind = resolved.ChildSpaceKind,
                TraversalMode = WorkspaceContentTraversalMode.SpaceDescendants
            },
            WorkspaceContentPathKind.Content => new WorkspaceVisibleContentQuery
            {
                SpaceId = resolved.Space!.Id,
                Name = resolved.Attachment?.Name,
                Role = resolved.Attachment?.Role,
                TraversalMode = WorkspaceContentTraversalMode.SpaceOnly
            },
            _ => new WorkspaceVisibleContentQuery()
        };
    }

    private async Task<string> BuildContentPathAsync(
        WorkspaceVisibleContentResult result,
        CancellationToken cancellationToken)
    {
        var spacePath = await BuildSpacePathAsync(result.Space, cancellationToken).ConfigureAwait(false);
        var name = WorkspaceContentPaths.NormalizeRelativePath(result.Name);
        return result.Role switch
        {
            WorkspaceContentRoles.Upload => $"{spacePath}/uploads/{name}",
            WorkspaceContentRoles.Artifact => $"{spacePath}/artifacts/{name}",
            _ => $"{spacePath}/{name}"
        };
    }

    private async Task<string> BuildSpacePathAsync(
        WorkspaceSpaceInfo space,
        CancellationToken cancellationToken)
    {
        if (space.ParentSpaceId is null)
            return $"/{RootSegment(space.Kind)}/{DisplaySpaceSegment(space)}";

        var parent = await _workspace.GetSpaceAsync(_principal, space.ParentSpaceId, cancellationToken)
            .ConfigureAwait(false);
        if (parent is null)
            return $"/{RootSegment(space.Kind)}/{DisplaySpaceSegment(space)}";

        return $"{await BuildSpacePathAsync(parent, cancellationToken).ConfigureAwait(false)}/{ChildDirectorySegment(space.Kind)}/{DisplaySpaceSegment(space)}";
    }

    private static string RootSegment(string kind) => kind switch
    {
        "agent" => "agents",
        "project" => "projects",
        "session" => "sessions",
        "workspace" => "workspaces",
        _ => WorkspaceContentPaths.NormalizeSegment(kind)
    };

    private static string ChildDirectorySegment(string kind) => kind switch
    {
        "memory" => "memory",
        "skill" => "skills",
        "knowledge" => "knowledge",
        "branch" => "branches",
        _ => WorkspaceContentPaths.NormalizeSegment(kind)
    };

    private static string DisplaySpaceSegment(WorkspaceSpaceInfo space) =>
        WorkspaceContentPaths.NormalizeSegment(space.Slug ?? space.ExternalId ?? space.Id);

    private static async Task<string> RunToolAsync(Func<Task<string>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (WorkspacePathNotFoundException ex)
        {
            return $"Error: {ex.Message}";
        }
        catch (WorkspaceAccessDeniedException ex)
        {
            return $"Error: {ex.Message}";
        }
        catch (WorkspaceConflictException ex)
        {
            return $"Error: {ex.Message}";
        }
        catch (KeyNotFoundException ex)
        {
            return $"Error: {ex.Message}";
        }
        catch (FileNotFoundException ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static async Task<string> ExtractTextAsync(
        Stream data,
        WorkspaceContentInfo info,
        int? offset,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (IsTextContent(info.ContentType))
        {
            using var reader = new StreamReader(data, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            if (offset == null && limit == null)
                return text;

            var lines = text.Split('\n');
            var start = Math.Clamp(offset ?? 0, 0, lines.Length);
            var count = limit.HasValue ? Math.Max(0, limit.Value) : lines.Length - start;
            return string.Join('\n', lines.Skip(start).Take(count));
        }

        return $"[Binary content: {info.Name ?? info.Id}, {info.SizeBytes} bytes, type={info.ContentType}.]";
    }

    private static bool IsTextContent(string contentType) =>
        contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
        contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
        contentType.Equals("application/xml", StringComparison.OrdinalIgnoreCase) ||
        contentType.Equals("application/x-ndjson", StringComparison.OrdinalIgnoreCase);

    private static string FormatEntry(WorkspaceContentPathEntry entry)
    {
        var suffix = entry.Kind switch
        {
            WorkspaceContentPathKind.Content when entry.Content is not null =>
                $" ({FormatSize(entry.Content.SizeBytes)}, role={entry.Attachment?.Role}, content_id={entry.Attachment?.ContentId})",
            WorkspaceContentPathKind.Content => $" (role={entry.Attachment?.Role}, content_id={entry.Attachment?.ContentId})",
            WorkspaceContentPathKind.Space when entry.Space is not null => $" (space_id={entry.Space.Id}, kind={entry.Space.Kind})",
            _ => "/"
        };

        return $"{entry.Name}{suffix}";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }
}
