using System.Runtime.CompilerServices;
using System.Text;

namespace HPD.Agent;

/// <summary>
/// Extension methods for IContentStore providing folder management and convenience upload helpers.
/// </summary>
/// <remarks>
/// <para><b>Folder Management:</b></para>
/// Folders are virtual — they're implemented as tags on stored content.
/// Folder metadata (description, permissions) is stored in a per-store registry
/// using ConditionalWeakTable so it doesn't modify IContentStore itself.
///
/// <para><b>Named Upsert Semantics:</b></para>
/// All upload helpers use named puts (stable caller-defined keys). Calling the same
/// upload at every startup is safe — same content = no-op, changed content = overwrite.
/// </remarks>
public static class ContentStoreExtensions
{
    // Per-store folder registry — no changes to IContentStore interface needed
    private static readonly ConditionalWeakTable<IContentStore, FolderRegistry> _registries = new();

    private static FolderRegistry GetRegistry(IContentStore store) =>
        _registries.GetOrCreateValue(store);

    // ═══════════════════════════════════════════════════════════════════
    // Folder Management
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Register a named folder in this content store.
    /// Folders are virtual — content is organized by a ["folder"] tag on each stored item.
    /// Registering a folder makes it visible via FolderDiscoveryMiddleware and
    /// enables permission enforcement in ContentStoreToolkit.
    /// </summary>
    /// <param name="store">The content store.</param>
    /// <param name="name">Folder name (e.g., "knowledge"). No leading slash.</param>
    /// <param name="options">Folder description, permissions, and tags.</param>
    /// <returns>The newly created IContentFolder handle.</returns>
    public static IContentFolder CreateFolder(this IContentStore store, string name, FolderOptions options)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Folder name cannot be empty.", nameof(name));
        if (options == null) throw new ArgumentNullException(nameof(options));

        name = name.TrimStart('/');
        var registry = GetRegistry(store);
        var folder = new ContentFolder(store, name, options);
        registry.Register(name, folder);
        return folder;
    }

    /// <summary>
    /// Get a registered folder by name.
    /// </summary>
    /// <exception cref="InvalidOperationException">If the folder has not been registered.</exception>
    public static IContentFolder GetFolder(this IContentStore store, string name)
    {
        name = name.TrimStart('/');
        var registry = GetRegistry(store);
        if (!registry.TryGet(name, out var folder))
            throw new InvalidOperationException(
                $"Folder '{name}' is not registered. Call CreateFolder('{name}', ...) first.");
        return folder!;
    }

    /// <summary>Check whether a folder has been registered.</summary>
    public static bool HasFolder(this IContentStore store, string name)
    {
        name = name.TrimStart('/');
        return GetRegistry(store).TryGet(name, out _);
    }

    /// <summary>
    /// List all registered folders (for FolderDiscoveryMiddleware context injection).
    /// </summary>
    public static Task<IReadOnlyList<FolderInfo>> ListFoldersAsync(
        this IContentStore store,
        CancellationToken cancellationToken = default)
    {
        var folders = GetRegistry(store).GetAll()
            .Select(f => new FolderInfo
            {
                Name = f.Name,
                Path = $"/{f.Name}",
                Description = f.Options.Description,
                Permissions = f.Options.Permissions,
                Scope = "agent"
            })
            .OrderBy(f => f.Path)
            .ToList();

        return Task.FromResult<IReadOnlyList<FolderInfo>>(folders);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Convenience Folder Shortcuts
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Get the /skills folder. Must be registered first via CreateFolder("skills", ...).</summary>
    public static IContentFolder Skills(this IContentStore store) => store.GetFolder("skills");

    /// <summary>Get the /knowledge folder. Must be registered first via CreateFolder("knowledge", ...).</summary>
    public static IContentFolder Knowledge(this IContentStore store) => store.GetFolder("knowledge");

    /// <summary>Get the /memory folder. Must be registered first via CreateFolder("memory", ...).</summary>
    public static IContentFolder Memory(this IContentStore store) => store.GetFolder("memory");

    // ═══════════════════════════════════════════════════════════════════
    // Skill Document Upload (Global or Agent-Scoped)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Upload a skill instruction document.
    /// Named upsert: same documentId + same content = no-op (startup-safe).
    /// Same documentId + changed content = overwrite.
    ///
    /// Pass scope=null for global skills visible to all agents.
    /// Pass scope=agentName for agent-specific skills.
    /// </summary>
    /// <param name="store">The content store.</param>
    /// <param name="documentId">Stable caller-defined key, e.g. "oauth-guide".</param>
    /// <param name="content">Document text content.</param>
    /// <param name="description">Global default description shown to agents.</param>
    /// <param name="scope">null = global (all agents), agentName = agent-specific.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The content ID (stable across no-op upserts).</returns>
    public static Task<string> UploadSkillDocumentAsync(
        this IContentStore store,
        string documentId,
        string content,
        string description,
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        var data = Encoding.UTF8.GetBytes(content);
        var metadata = new ContentMetadata
        {
            Name = documentId,
            Description = description,
            Origin = ContentSource.System,
            Tags = new Dictionary<string, string> { ["folder"] = "/skills" }
        };
        return store.PutAsync(scope, data, "text/plain", metadata, cancellationToken);
    }

    /// <summary>
    /// Link an existing skill document to a specific skill with a skill-specific description override.
    /// The document must already exist (uploaded via UploadSkillDocumentAsync).
    ///
    /// The description override is stored as a tag: ["description:{skillName}"] = "override text".
    /// When FolderDiscoveryMiddleware renders results for an active skill, it picks the
    /// skill-specific description tag if present, falls back to global description otherwise.
    /// </summary>
    public static async Task LinkSkillDocumentAsync(
        this IContentStore store,
        string documentId,
        string skillName,
        string descriptionOverride,
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        // Find the existing document
        var existing = await store.QueryAsync(
            scope,
            new ContentQuery
            {
                Name = documentId,
                Tags = new Dictionary<string, string> { ["folder"] = "/skills" }
            },
            cancellationToken);

        if (existing.Count == 0)
            throw new InvalidOperationException(
                $"Skill document '{documentId}' not found. Upload it first via UploadSkillDocumentAsync.");

        // Re-upload with the additional skill-specific description tag
        var doc = existing[0];
        var contentData = await store.GetAsync(scope, doc.Id, cancellationToken);
        if (contentData == null) return;

        // Merge skill-link tag into existing tags
        var newTags = new Dictionary<string, string>(doc.Tags ?? new Dictionary<string, string>())
        {
            [$"description:{skillName}"] = descriptionOverride
        };

        // Overwrite using same Name key (named upsert will update in-place)
        await store.PutAsync(scope, contentData.Data, contentData.ContentType,
            new ContentMetadata
            {
                Name = documentId,
                Description = doc.Description,
                Origin = doc.Origin,
                Tags = newTags,
                OriginalSource = doc.OriginalSource
            },
            cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Knowledge Document Upload (Agent-Scoped)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Upload a knowledge document for a specific agent.
    /// Named upsert: same agentName + documentName + same content = no-op.
    /// Knowledge is ALWAYS agent-scoped.
    /// </summary>
    /// <param name="store">The content store.</param>
    /// <param name="agentName">Agent that owns this knowledge.</param>
    /// <param name="documentName">Stable caller-defined key, e.g. "api-guide".</param>
    /// <param name="data">Raw document bytes.</param>
    /// <param name="contentType">MIME type (e.g., "text/markdown", "application/pdf").</param>
    /// <param name="description">Optional description shown to agent.</param>
    /// <param name="extraTags">Optional additional tags for categorization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The content ID.</returns>
    public static Task<string> UploadKnowledgeDocumentAsync(
        this IContentStore store,
        string agentName,
        string documentName,
        byte[] data,
        string contentType,
        string? description = null,
        IReadOnlyDictionary<string, string>? extraTags = null,
        CancellationToken cancellationToken = default)
    {
        var tags = new Dictionary<string, string> { ["folder"] = "/knowledge" };
        if (extraTags != null)
            foreach (var kv in extraTags) tags[kv.Key] = kv.Value;

        var metadata = new ContentMetadata
        {
            Name = documentName,
            Description = description,
            Origin = ContentSource.System,
            Tags = tags
        };
        return store.PutAsync(agentName, data, contentType, metadata, cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Memory Write (Agent-Scoped)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Write a memory entry for a specific agent.
    /// Named upsert: same agentName + title = overwrite (memories are mutable by design).
    /// Memories are ALWAYS agent-scoped.
    /// </summary>
    /// <param name="store">The content store.</param>
    /// <param name="agentName">Agent that owns this memory.</param>
    /// <param name="title">Stable key within agent scope (acts as the memory's filename).</param>
    /// <param name="content">Memory content text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The content ID.</returns>
    public static Task<string> WriteMemoryAsync(
        this IContentStore store,
        string agentName,
        string title,
        string content,
        CancellationToken cancellationToken = default)
    {
        var data = Encoding.UTF8.GetBytes(content);
        var metadata = new ContentMetadata
        {
            Name = title,
            Origin = ContentSource.Agent,
            Tags = new Dictionary<string, string> { ["folder"] = "/memory" }
        };
        return store.PutAsync(agentName, data, "text/plain", metadata, cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Internal Folder Registry
    // ═══════════════════════════════════════════════════════════════════

    internal static FolderRegistry GetFolderRegistry(IContentStore store) => GetRegistry(store);

    internal static FolderOptions? GetFolderOptions(IContentStore store, string folderName)
    {
        folderName = folderName.TrimStart('/');
        if (GetRegistry(store).TryGet(folderName, out var folder))
            return folder!.Options;
        return null;
    }
}

/// <summary>
/// Internal registry mapping folder names to IContentFolder handles.
/// </summary>
internal sealed class FolderRegistry
{
    private readonly Dictionary<string, IContentFolder> _folders =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(string name, IContentFolder folder) => _folders[name] = folder;

    public bool TryGet(string name, out IContentFolder? folder) =>
        _folders.TryGetValue(name, out folder);

    public IEnumerable<IContentFolder> GetAll() => _folders.Values;
}

/// <summary>
/// Concrete implementation of IContentFolder — a scoped view of IContentStore.
/// </summary>
internal sealed class ContentFolder : IContentFolder
{
    private readonly IContentStore _store;

    public string Name { get; }
    public string Path => $"/{Name}";
    public FolderOptions Options { get; }

    public ContentFolder(IContentStore store, string name, FolderOptions options)
    {
        _store = store;
        Name = name;
        Options = options;
    }

    public Task<string> PutAsync(string? scope, byte[] data, string contentType,
        ContentMetadata? metadata = null, CancellationToken cancellationToken = default)
    {
        // Inject folder tag
        var tags = new Dictionary<string, string>(metadata?.Tags ?? new Dictionary<string, string>())
        {
            ["folder"] = Path
        };
        if (Options.Tags != null)
            foreach (var kv in Options.Tags) tags.TryAdd(kv.Key, kv.Value);

        var merged = (metadata ?? new ContentMetadata()) with { Tags = tags };
        return _store.PutAsync(scope, data, contentType, merged, cancellationToken);
    }

    public async Task<ContentData?> GetAsync(string scope, string nameOrId, CancellationToken cancellationToken = default)
    {
        // First try direct ID lookup
        var byId = await _store.GetAsync(scope, nameOrId, cancellationToken);
        if (byId != null) return byId;

        // Fall back to name lookup within this folder
        var results = await _store.QueryAsync(scope, new ContentQuery
        {
            Name = nameOrId,
            Tags = new Dictionary<string, string> { ["folder"] = Path }
        }, cancellationToken);

        if (results.Count == 0) return null;
        return await _store.GetAsync(scope, results[0].Id, cancellationToken);
    }

    public async Task DeleteAsync(string scope, string nameOrId, CancellationToken cancellationToken = default)
    {
        // Try as direct ID first, else resolve via name
        var contentData = await GetAsync(scope, nameOrId, cancellationToken);
        if (contentData != null)
            await _store.DeleteAsync(scope, contentData.Id, cancellationToken);
    }

    public Task<IReadOnlyList<ContentInfo>> ListAsync(string scope, CancellationToken cancellationToken = default)
    {
        return _store.QueryAsync(scope, new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = Path }
        }, cancellationToken);
    }
}
