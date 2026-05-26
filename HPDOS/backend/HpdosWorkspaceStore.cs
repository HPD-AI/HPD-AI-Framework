using System.Text.Json;

internal sealed record HpdosWorkspaceRoot(string Id, string Label, string Path);

internal sealed record HpdosWorkspace(
    string Id,
    string Name,
    string CreatedAt,
    string UpdatedAt,
    string DefaultRootId,
    IReadOnlyList<HpdosWorkspaceRoot> Roots);

internal sealed record HpdosWorkspaceStore(
    int Version,
    string ActiveWorkspaceId,
    IReadOnlyList<HpdosWorkspace> Workspaces);

internal sealed class HpdosWorkspaceStoreService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string storePath;
    private readonly HpdosProjectContext projectContext;
    private readonly SemaphoreSlim gate = new(1, 1);

    public HpdosWorkspaceStoreService(string dataRoot, HpdosProjectContext projectContext)
    {
        storePath = Path.Combine(dataRoot, "workspaces.json");
        this.projectContext = projectContext;
    }

    public async Task<HpdosWorkspaceStore> GetAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var store = await ReadUnsafeAsync(ct) ?? CreateDefaultStore();
            store = Normalize(store);
            await WriteUnsafeAsync(store, ct);
            return store;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<HpdosWorkspaceStore> SaveAsync(HpdosWorkspaceStore store, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var normalized = Normalize(store);
            await WriteUnsafeAsync(normalized, ct);
            return normalized;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<HpdosWorkspace?> GetActiveWorkspaceAsync(CancellationToken ct)
    {
        var store = await GetAsync(ct);
        return ActiveWorkspace(store);
    }

    private async Task<HpdosWorkspaceStore?> ReadUnsafeAsync(CancellationToken ct)
    {
        if (!File.Exists(storePath))
            return null;

        await using var stream = File.OpenRead(storePath);
        return await JsonSerializer.DeserializeAsync<HpdosWorkspaceStore>(stream, JsonOptions, ct);
    }

    private async Task WriteUnsafeAsync(HpdosWorkspaceStore store, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
        await using var stream = File.Create(storePath);
        await JsonSerializer.SerializeAsync(stream, store, JsonOptions, ct);
    }

    private HpdosWorkspaceStore CreateDefaultStore()
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        var root = new HpdosWorkspaceRoot("default", projectContext.Name, projectContext.Directory);
        var workspace = new HpdosWorkspace(
            Id: Slug(projectContext.Name),
            Name: projectContext.Name,
            CreatedAt: now,
            UpdatedAt: now,
            DefaultRootId: root.Id,
            Roots: [root]);

        return new HpdosWorkspaceStore(1, workspace.Id, [workspace]);
    }

    private HpdosWorkspaceStore Normalize(HpdosWorkspaceStore store)
    {
        var seenWorkspaceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var workspaces = store.Workspaces
            .Select(workspace => NormalizeWorkspace(workspace, seenWorkspaceIds))
            .Where(workspace => workspace.Roots.Count > 0)
            .ToList();

        if (workspaces.Count == 0)
            return CreateDefaultStore();

        var activeWorkspaceId = workspaces.Any(workspace => string.Equals(workspace.Id, store.ActiveWorkspaceId, StringComparison.OrdinalIgnoreCase))
            ? store.ActiveWorkspaceId
            : workspaces[0].Id;

        return new HpdosWorkspaceStore(1, activeWorkspaceId, workspaces);
    }

    private static HpdosWorkspace NormalizeWorkspace(HpdosWorkspace workspace, HashSet<string> seenWorkspaceIds)
    {
        var seenRootIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = workspace.Roots
            .Select((root, index) =>
            {
                var path = NormalizeDirectoryPath(root.Path);
                return new { Root = root, Index = index, Path = path };
            })
            .Where(item => item.Path.Length > 0 && seenPaths.Add(item.Path))
            .Select(item =>
            {
                var label = string.IsNullOrWhiteSpace(item.Root.Label)
                    ? LabelFromPath(item.Path)
                    : item.Root.Label.Trim();
                var baseId = item.Index == 0 ? "default" : Slug(string.IsNullOrWhiteSpace(item.Root.Id) ? label : item.Root.Id);
                return new HpdosWorkspaceRoot(UniqueId(baseId, seenRootIds), label, item.Path);
            })
            .ToList();

        var defaultRoot = roots.FirstOrDefault(root => string.Equals(root.Id, workspace.DefaultRootId, StringComparison.OrdinalIgnoreCase))
            ?? roots.FirstOrDefault();
        var workspaceId = UniqueId(Slug(string.IsNullOrWhiteSpace(workspace.Id) ? workspace.Name : workspace.Id), seenWorkspaceIds);
        var name = string.IsNullOrWhiteSpace(workspace.Name)
            ? defaultRoot?.Label ?? "Workspace"
            : workspace.Name.Trim();

        return workspace with
        {
            Id = workspaceId,
            Name = name,
            DefaultRootId = defaultRoot?.Id ?? "default",
            Roots = roots
        };
    }

    private static HpdosWorkspace? ActiveWorkspace(HpdosWorkspaceStore store) =>
        store.Workspaces.FirstOrDefault(workspace => string.Equals(workspace.Id, store.ActiveWorkspaceId, StringComparison.OrdinalIgnoreCase))
        ?? store.Workspaces.FirstOrDefault();

    private static string NormalizeDirectoryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        try
        {
            return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return "";
        }
    }

    private static string LabelFromPath(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(trimmed) is { Length: > 0 } name ? name : "Workspace";
    }

    private static string UniqueId(string baseId, HashSet<string> seen)
    {
        var candidate = string.IsNullOrWhiteSpace(baseId) ? "item" : baseId;
        if (seen.Add(candidate))
            return candidate;

        for (var index = 2; ; index++)
        {
            var next = $"{candidate}-{index}";
            if (seen.Add(next))
                return next;
        }
    }

    private static string Slug(string value)
    {
        var slug = new string(value.Trim().ToLowerInvariant()
            .Select(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' ? ch : '-')
            .ToArray())
            .Trim('-');

        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);

        return string.IsNullOrWhiteSpace(slug) ? "item" : slug;
    }
}
