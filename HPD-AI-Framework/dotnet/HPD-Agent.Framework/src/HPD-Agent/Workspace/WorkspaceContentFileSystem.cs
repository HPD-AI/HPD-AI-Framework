using System.Text;

namespace HPD.Agent;

public sealed class WorkspaceContentFileSystem
{
    private static readonly IReadOnlyDictionary<string, string> RootKinds = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["agents"] = "agent",
        ["projects"] = "project",
        ["sessions"] = "session",
        ["workspaces"] = "workspace"
    };

    private static readonly IReadOnlyDictionary<string, ChildSpaceDirectory> ChildDirectories = new Dictionary<string, ChildSpaceDirectory>(StringComparer.Ordinal)
    {
        ["memory"] = new("memory", "memory"),
        ["skills"] = new("skill", "skill"),
        ["knowledge"] = new("knowledge", "knowledge"),
        ["branches"] = new("branch", "branch")
    };

    private static readonly IReadOnlyDictionary<string, string> RoleDirectories = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["uploads"] = WorkspaceContentRoles.Upload,
        ["artifacts"] = WorkspaceContentRoles.Artifact
    };

    private readonly IWorkspaceStore _workspace;

    public WorkspaceContentFileSystem(IWorkspaceStore workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    }

    public async Task<IReadOnlyList<WorkspaceContentPathEntry>> ListAsync(
        WorkspacePrincipalRef principal,
        string? path = null,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolvePathAsync(
            principal,
            path,
            allowMissingContent: false,
            cancellationToken).ConfigureAwait(false);

        return resolved.Kind switch
        {
            WorkspaceContentPathKind.Root => RootKinds.Keys
                .Select(segment => WorkspaceContentPathEntry.Directory("/" + segment, segment))
                .ToList(),
            WorkspaceContentPathKind.RootKind => await ListRootKindAsync(principal, resolved, cancellationToken).ConfigureAwait(false),
            WorkspaceContentPathKind.Space => await ListSpaceAsync(principal, resolved.Space!, resolved.Path, cancellationToken).ConfigureAwait(false),
            WorkspaceContentPathKind.RoleDirectory => await ListRoleDirectoryAsync(principal, resolved, cancellationToken).ConfigureAwait(false),
            WorkspaceContentPathKind.ChildDirectory => await ListChildDirectoryAsync(principal, resolved, cancellationToken).ConfigureAwait(false),
            WorkspaceContentPathKind.Content => [await CreateContentEntryAsync(principal, resolved.Space!, resolved.Attachment!, cancellationToken, ParentPath(resolved.Path)).ConfigureAwait(false)],
            _ => []
        };
    }

    public async Task<Stream?> OpenReadAsync(
        WorkspacePrincipalRef principal,
        string path,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolvePathAsync(
            principal,
            path,
            allowMissingContent: false,
            cancellationToken).ConfigureAwait(false);

        if (resolved.Attachment is null)
            return null;

        if (resolved.Attachment.Metadata is not null &&
            resolved.Attachment.Metadata.ContainsKey("stream_id"))
        {
            return await OpenEventStreamProjectionAsync(
                principal,
                resolved.Space!.Id,
                resolved.Attachment.Role,
                cancellationToken).ConfigureAwait(false);
        }

        return await _workspace.OpenContentAsync(
            principal,
            resolved.Attachment.ContentId,
            resolved.Attachment.ContentVersion,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> ReadTextAsync(
        WorkspacePrincipalRef principal,
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = await OpenReadAsync(principal, path, cancellationToken).ConfigureAwait(false);
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<WorkspaceContentPathStat?> StatAsync(
        WorkspacePrincipalRef principal,
        string path,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolvePathAsync(
            principal,
            path,
            allowMissingContent: false,
            cancellationToken).ConfigureAwait(false);

        if (resolved.Kind is WorkspaceContentPathKind.Root or WorkspaceContentPathKind.RootKind)
            return new WorkspaceContentPathStat(resolved.Path, resolved.Kind, null, null, null);

        if (resolved.Space is null)
            return null;

        WorkspaceContentInfo? content = null;
        if (resolved.Attachment is not null)
        {
            content = await _workspace.StatContentAsync(
                principal,
                resolved.Attachment.ContentId,
                resolved.Attachment.ContentVersion,
                cancellationToken).ConfigureAwait(false);
        }

        return new WorkspaceContentPathStat(
            resolved.Path,
            resolved.Kind,
            resolved.Space,
            resolved.Attachment,
            content);
    }

    public async Task<WorkspaceContentPathEntry> WriteTextAsync(
        WorkspacePrincipalRef principal,
        string path,
        string content,
        string contentType = "text/plain",
        CancellationToken cancellationToken = default)
    {
        var destination = await ResolveWriteDestinationAsync(principal, path, cancellationToken).ConfigureAwait(false);
        var bytes = Encoding.UTF8.GetBytes(content);

        var attachment = await _workspace.WriteContentAsync(
            principal,
            destination.Space.Id,
            destination.ExistingAttachment?.Id,
            new MemoryStream(bytes),
            new WriteWorkspaceSpaceContentRequest
            {
                IfMatchContentVersion = destination.ExistingAttachment?.ContentVersion,
                IfMatchAttachmentVersion = destination.ExistingAttachment?.Version,
                ContentType = contentType,
                Role = destination.Role,
                Name = destination.Name,
                Permission = WorkspacePermissions.ReadWrite
            },
            cancellationToken).ConfigureAwait(false);

        return await CreateContentEntryAsync(principal, destination.Space, attachment, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        WorkspacePrincipalRef principal,
        string path,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolvePathAsync(
            principal,
            path,
            allowMissingContent: false,
            cancellationToken).ConfigureAwait(false);

        if (resolved.Space is null || resolved.Attachment is null)
            return;

        await _workspace.DetachContentAsync(
            principal,
            resolved.Space.Id,
            resolved.Attachment.Id,
            resolved.Attachment.Version,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Stream> OpenEventStreamProjectionAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        string role,
        CancellationToken cancellationToken)
    {
        var stream = new MemoryStream();
        await foreach (var evt in _workspace.ReadEventsAsync(
            principal,
            spaceId,
            new WorkspaceEventStreamQuery { Role = role },
            cancellationToken).ConfigureAwait(false))
        {
            await stream.WriteAsync(evt.Payload, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(new byte[] { (byte)'\n' }, cancellationToken).ConfigureAwait(false);
        }

        stream.Position = 0;
        return stream;
    }

    public async Task<WorkspaceContentPathResolution> ResolveAsync(
        WorkspacePrincipalRef principal,
        string? path,
        CancellationToken cancellationToken = default) =>
        await ResolvePathAsync(principal, path, allowMissingContent: false, cancellationToken).ConfigureAwait(false);

    private async Task<IReadOnlyList<WorkspaceContentPathEntry>> ListRootKindAsync(
        WorkspacePrincipalRef principal,
        WorkspaceContentPathResolution resolved,
        CancellationToken cancellationToken)
    {
        var spaces = await _workspace.ListSpacesAsync(
            principal,
            new WorkspaceSpaceQuery { Kind = resolved.SpaceKind },
            cancellationToken).ConfigureAwait(false);

        return spaces
            .Where(space => space.ParentSpaceId is null)
            .OrderBy(space => DisplaySpaceSegment(space))
            .Select(space => WorkspaceContentPathEntry.SpaceDirectory(
                CombinePath(resolved.Path, DisplaySpaceSegment(space)),
                DisplaySpaceSegment(space),
                space))
            .ToList();
    }

    private async Task<IReadOnlyList<WorkspaceContentPathEntry>> ListSpaceAsync(
        WorkspacePrincipalRef principal,
        WorkspaceSpaceInfo space,
        string basePath,
        CancellationToken cancellationToken)
    {
        var entries = new List<WorkspaceContentPathEntry>();
        foreach (var directory in GetDirectoriesForSpace(space).OrderBy(directory => directory))
            entries.Add(WorkspaceContentPathEntry.Directory(CombinePath(basePath, directory), directory));

        var content = await _workspace.ListContentAsync(principal, space.Id, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        foreach (var attachment in content
            .Where(attachment => !IsRoleDirectoryRole(attachment.Role))
            .OrderBy(attachment => attachment.Name))
        {
            entries.Add(await CreateContentEntryAsync(principal, space, attachment, cancellationToken, basePath).ConfigureAwait(false));
        }

        return entries;
    }

    private async Task<IReadOnlyList<WorkspaceContentPathEntry>> ListRoleDirectoryAsync(
        WorkspacePrincipalRef principal,
        WorkspaceContentPathResolution resolved,
        CancellationToken cancellationToken)
    {
        var content = await _workspace.ListContentAsync(
            principal,
            resolved.Space!.Id,
            new WorkspaceContentAttachmentQuery { Role = resolved.Role },
            cancellationToken).ConfigureAwait(false);

        var basePath = CombinePath(BuildSpacePath(resolved.Space), resolved.DirectorySegment!);
        var entries = new List<WorkspaceContentPathEntry>();
        foreach (var attachment in content.OrderBy(attachment => attachment.Name))
            entries.Add(await CreateContentEntryAsync(principal, resolved.Space, attachment, cancellationToken, basePath).ConfigureAwait(false));

        return entries;
    }

    private async Task<IReadOnlyList<WorkspaceContentPathEntry>> ListChildDirectoryAsync(
        WorkspacePrincipalRef principal,
        WorkspaceContentPathResolution resolved,
        CancellationToken cancellationToken)
    {
        var spaces = await _workspace.ListChildSpacesAsync(
            principal,
            resolved.Space!.Id,
            new WorkspaceSpaceQuery { Kind = resolved.ChildSpaceKind },
            cancellationToken).ConfigureAwait(false);

        var basePath = CombinePath(BuildSpacePath(resolved.Space), resolved.DirectorySegment!);
        return spaces
            .OrderBy(DisplaySpaceSegment)
            .Select(space => WorkspaceContentPathEntry.SpaceDirectory(
                CombinePath(basePath, DisplaySpaceSegment(space)),
                DisplaySpaceSegment(space),
                space))
            .ToList();
    }

    private async Task<WorkspaceContentPathResolution> ResolvePathAsync(
        WorkspacePrincipalRef principal,
        string? path,
        bool allowMissingContent,
        CancellationToken cancellationToken)
    {
        var normalized = WorkspaceContentPaths.NormalizePath(path);
        var segments = WorkspaceContentPaths.Split(normalized);
        if (segments.Count == 0)
            return WorkspaceContentPathResolution.Root(normalized);

        if (!RootKinds.TryGetValue(segments[0], out var rootKind))
            throw new WorkspacePathNotFoundException($"Unknown workspace root '/{segments[0]}'.");

        if (segments.Count == 1)
            return WorkspaceContentPathResolution.RootKind(normalized, rootKind);

        var space = await ResolveTopLevelSpaceAsync(principal, rootKind, segments[1], cancellationToken)
            .ConfigureAwait(false);
        if (segments.Count == 2)
            return WorkspaceContentPathResolution.SpaceNode(normalized, space);

        return await ResolveInsideSpaceAsync(
            principal,
            normalized,
            space,
            segments.Skip(2).ToArray(),
            allowMissingContent,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<WorkspaceContentPathResolution> ResolveInsideSpaceAsync(
        WorkspacePrincipalRef principal,
        string normalizedPath,
        WorkspaceSpaceInfo space,
        IReadOnlyList<string> segments,
        bool allowMissingContent,
        CancellationToken cancellationToken)
    {
        if (segments.Count == 0)
            return WorkspaceContentPathResolution.SpaceNode(normalizedPath, space);

        if (RoleDirectories.TryGetValue(segments[0], out var role))
        {
            if (segments.Count == 1)
                return WorkspaceContentPathResolution.RoleDirectory(normalizedPath, space, segments[0], role);

            var name = string.Join('/', segments.Skip(1));
            var attachment = await ResolveAttachmentAsync(principal, space.Id, role, name, allowMissingContent, cancellationToken)
                .ConfigureAwait(false);
            return WorkspaceContentPathResolution.ContentNode(normalizedPath, space, attachment);
        }

        if (ChildDirectories.TryGetValue(segments[0], out var childDirectory))
        {
            if (segments.Count == 1)
            {
                return WorkspaceContentPathResolution.ChildDirectory(
                    normalizedPath,
                    space,
                    segments[0],
                    childDirectory.Kind);
            }

            var childSpace = await ResolveChildSpaceAsync(
                principal,
                space.Id,
                childDirectory.Kind,
                segments[1],
                cancellationToken).ConfigureAwait(false);

            if (segments.Count == 2)
                return WorkspaceContentPathResolution.SpaceNode(normalizedPath, childSpace);

            return await ResolveInsideSpaceAsync(
                principal,
                normalizedPath,
                childSpace,
                segments.Skip(2).ToArray(),
                allowMissingContent,
                cancellationToken).ConfigureAwait(false);
        }

        var contentName = string.Join('/', segments);
        var content = await ResolveAttachmentAsync(principal, space.Id, role: null, contentName, allowMissingContent, cancellationToken)
            .ConfigureAwait(false);
        return WorkspaceContentPathResolution.ContentNode(normalizedPath, space, content);
    }

    private async Task<WorkspaceContentWriteDestination> ResolveWriteDestinationAsync(
        WorkspacePrincipalRef principal,
        string path,
        CancellationToken cancellationToken)
    {
        var normalized = WorkspaceContentPaths.NormalizePath(path);
        var segments = WorkspaceContentPaths.Split(normalized);
        if (segments.Count < 3)
            throw new WorkspacePathNotFoundException("Write paths must include a workspace space and filename.");

        var name = segments[^1];
        var parentPath = "/" + string.Join('/', segments.Take(segments.Count - 1));
        var parent = await ResolvePathAsync(principal, parentPath, allowMissingContent: false, cancellationToken)
            .ConfigureAwait(false);

        var role = parent.Kind switch
        {
            WorkspaceContentPathKind.RoleDirectory => parent.Role!,
            WorkspaceContentPathKind.Space => WorkspaceContentRoles.Content,
            _ => throw new WorkspacePathNotFoundException($"Path '{parentPath}' is not a writable content directory.")
        };

        var existing = await ResolveAttachmentAsync(
            principal,
            parent.Space!.Id,
            parent.Kind == WorkspaceContentPathKind.RoleDirectory ? role : null,
            name,
            allowMissingContent: true,
            cancellationToken).ConfigureAwait(false);

        return new WorkspaceContentWriteDestination(parent.Space, role, name, existing);
    }

    private async Task<WorkspaceSpaceInfo> ResolveTopLevelSpaceAsync(
        WorkspacePrincipalRef principal,
        string kind,
        string selector,
        CancellationToken cancellationToken)
    {
        var spaces = await _workspace.ListSpacesAsync(
            principal,
            new WorkspaceSpaceQuery { Kind = kind },
            cancellationToken).ConfigureAwait(false);

        return ResolveSpace(spaces.Where(space => space.ParentSpaceId is null), selector, kind);
    }

    private async Task<WorkspaceSpaceInfo> ResolveChildSpaceAsync(
        WorkspacePrincipalRef principal,
        string parentSpaceId,
        string kind,
        string selector,
        CancellationToken cancellationToken)
    {
        var spaces = await _workspace.ListChildSpacesAsync(
            principal,
            parentSpaceId,
            new WorkspaceSpaceQuery { Kind = kind },
            cancellationToken).ConfigureAwait(false);

        return ResolveSpace(spaces, selector, kind);
    }

    private static WorkspaceSpaceInfo ResolveSpace(IEnumerable<WorkspaceSpaceInfo> spaces, string selector, string kind)
    {
        var matches = spaces
            .Where(space =>
                string.Equals(space.Id, selector, StringComparison.Ordinal) ||
                string.Equals(space.ExternalId, selector, StringComparison.Ordinal) ||
                string.Equals(space.Slug, selector, StringComparison.Ordinal) ||
                string.Equals(space.Name, selector, StringComparison.Ordinal) ||
                string.Equals(WorkspaceContentPaths.NormalizeSegment(space.Name), selector, StringComparison.Ordinal) ||
                string.Equals(DisplaySpaceSegment(space), selector, StringComparison.Ordinal))
            .ToList();

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new WorkspacePathNotFoundException($"No {kind} space matched '{selector}'."),
            _ => throw new WorkspacePathAmbiguousException($"More than one {kind} space matched '{selector}'. Use the space id.")
        };
    }

    private async Task<WorkspaceContentAttachmentInfo?> ResolveAttachmentAsync(
        WorkspacePrincipalRef principal,
        string spaceId,
        string? role,
        string name,
        bool allowMissingContent,
        CancellationToken cancellationToken)
    {
        var attachments = await _workspace.ListContentAsync(
            principal,
            spaceId,
            role is null
                ? new WorkspaceContentAttachmentQuery { Name = name }
                : new WorkspaceContentAttachmentQuery { Role = role, Name = name },
            cancellationToken).ConfigureAwait(false);

        if (role is null)
            attachments = attachments.Where(attachment => !IsRoleDirectoryRole(attachment.Role)).ToList();

        return attachments.Count switch
        {
            1 => attachments[0],
            0 when allowMissingContent => null,
            0 => throw new WorkspacePathNotFoundException($"No content matched '{name}'."),
            _ => throw new WorkspacePathAmbiguousException($"More than one content attachment matched '{name}'.")
        };
    }

    private async Task<WorkspaceContentPathEntry> CreateContentEntryAsync(
        WorkspacePrincipalRef principal,
        WorkspaceSpaceInfo space,
        WorkspaceContentAttachmentInfo attachment,
        CancellationToken cancellationToken,
        string? basePath = null)
    {
        var info = await _workspace.StatContentAsync(
            principal,
            attachment.ContentId,
            attachment.ContentVersion,
            cancellationToken).ConfigureAwait(false);

        var path = CombinePath(basePath ?? BuildSpacePath(space), attachment.Name);
        return WorkspaceContentPathEntry.ContentFile(path, attachment.Name, space, attachment, info);
    }

    private static IEnumerable<string> GetDirectoriesForSpace(WorkspaceSpaceInfo space)
    {
        if (space.Kind == "agent")
        {
            yield return "knowledge";
            yield return "memory";
            yield return "skills";
        }

        if (space.Kind == "session")
        {
            yield return "artifacts";
            yield return "branches";
            yield return "uploads";
        }

        if (space.Kind == "branch")
            yield return "artifacts";
    }

    private static string BuildSpacePath(WorkspaceSpaceInfo space)
    {
        var root = RootKinds.FirstOrDefault(pair => pair.Value == space.Kind).Key;
        return root is null
            ? "/" + DisplaySpaceSegment(space)
            : "/" + root + "/" + DisplaySpaceSegment(space);
    }

    private static string DisplaySpaceSegment(WorkspaceSpaceInfo space) =>
        WorkspaceContentPaths.NormalizeSegment(space.Slug ?? space.ExternalId ?? space.Id);

    private static string CombinePath(string left, string right) =>
        left.TrimEnd('/') + "/" + WorkspaceContentPaths.NormalizeRelativePath(right);

    private static bool IsRoleDirectoryRole(string role) =>
        RoleDirectories.Values.Any(value => string.Equals(value, role, StringComparison.Ordinal));

    private static string ParentPath(string path)
    {
        var normalized = WorkspaceContentPaths.NormalizePath(path);
        var index = normalized.LastIndexOf('/');
        return index <= 0 ? "/" : normalized[..index];
    }

    private sealed record ChildSpaceDirectory(string Kind, string Role);

    private sealed record WorkspaceContentWriteDestination(
        WorkspaceSpaceInfo Space,
        string Role,
        string Name,
        WorkspaceContentAttachmentInfo? ExistingAttachment);
}

public sealed record WorkspaceContentPathEntry
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required WorkspaceContentPathKind Kind { get; init; }
    public WorkspaceSpaceInfo? Space { get; init; }
    public WorkspaceContentAttachmentInfo? Attachment { get; init; }
    public WorkspaceContentInfo? Content { get; init; }

    public static WorkspaceContentPathEntry Directory(string path, string name) => new()
    {
        Path = path,
        Name = name,
        Kind = WorkspaceContentPathKind.Directory
    };

    public static WorkspaceContentPathEntry SpaceDirectory(string path, string name, WorkspaceSpaceInfo space) => new()
    {
        Path = path,
        Name = name,
        Kind = WorkspaceContentPathKind.Space,
        Space = space
    };

    public static WorkspaceContentPathEntry ContentFile(
        string path,
        string name,
        WorkspaceSpaceInfo space,
        WorkspaceContentAttachmentInfo attachment,
        WorkspaceContentInfo? content) => new()
    {
        Path = path,
        Name = name,
        Kind = WorkspaceContentPathKind.Content,
        Space = space,
        Attachment = attachment,
        Content = content
    };
}

public sealed record WorkspaceContentPathResolution
{
    public required string Path { get; init; }
    public required WorkspaceContentPathKind Kind { get; init; }
    public string? SpaceKind { get; init; }
    public WorkspaceSpaceInfo? Space { get; init; }
    public WorkspaceContentAttachmentInfo? Attachment { get; init; }
    public string? DirectorySegment { get; init; }
    public string? Role { get; init; }
    public string? ChildSpaceKind { get; init; }

    public static WorkspaceContentPathResolution Root(string path) => new()
    {
        Path = path,
        Kind = WorkspaceContentPathKind.Root
    };

    public static WorkspaceContentPathResolution RootKind(string path, string spaceKind) => new()
    {
        Path = path,
        Kind = WorkspaceContentPathKind.RootKind,
        SpaceKind = spaceKind
    };

    public static WorkspaceContentPathResolution SpaceNode(string path, WorkspaceSpaceInfo space) => new()
    {
        Path = path,
        Kind = WorkspaceContentPathKind.Space,
        Space = space
    };

    public static WorkspaceContentPathResolution RoleDirectory(
        string path,
        WorkspaceSpaceInfo space,
        string directorySegment,
        string role) => new()
    {
        Path = path,
        Kind = WorkspaceContentPathKind.RoleDirectory,
        Space = space,
        DirectorySegment = directorySegment,
        Role = role
    };

    public static WorkspaceContentPathResolution ChildDirectory(
        string path,
        WorkspaceSpaceInfo space,
        string directorySegment,
        string childSpaceKind) => new()
    {
        Path = path,
        Kind = WorkspaceContentPathKind.ChildDirectory,
        Space = space,
        DirectorySegment = directorySegment,
        ChildSpaceKind = childSpaceKind
    };

    public static WorkspaceContentPathResolution ContentNode(
        string path,
        WorkspaceSpaceInfo space,
        WorkspaceContentAttachmentInfo? attachment) => new()
    {
        Path = path,
        Kind = WorkspaceContentPathKind.Content,
        Space = space,
        Attachment = attachment
    };
}

public sealed record WorkspaceContentPathStat(
    string Path,
    WorkspaceContentPathKind Kind,
    WorkspaceSpaceInfo? Space,
    WorkspaceContentAttachmentInfo? Attachment,
    WorkspaceContentInfo? Content);

public enum WorkspaceContentPathKind
{
    Root,
    RootKind,
    Directory,
    Space,
    RoleDirectory,
    ChildDirectory,
    Content
}

public class WorkspacePathNotFoundException : Exception
{
    public WorkspacePathNotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class WorkspacePathAmbiguousException : WorkspacePathNotFoundException
{
    public WorkspacePathAmbiguousException(string message)
        : base(message)
    {
    }
}
