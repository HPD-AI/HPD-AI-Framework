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
/// <para><b>Write Semantics:</b></para>
/// Helpers use explicit write modes. Stable named documents are created first and then
/// replaced conditionally by ID/version when updated.
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
    /// enables permission enforcement in ContentStoreToolHarness.
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
    /// Pass scope=null for global skills visible to all agents.
    /// Pass scope=agentName for agent-specific skills.
    /// </summary>
    /// <param name="store">The content store.</param>
    /// <param name="documentId">Stable caller-defined key, e.g. "oauth-guide".</param>
    /// <param name="content">Document text content.</param>
    /// <param name="description">Global default description shown to agents.</param>
    /// <param name="scope">null = global (all agents), agentName = agent-specific.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Metadata for the uploaded content.</returns>
    public static Task<ContentInfo> UploadSkillDocumentAsync(
        this IContentStore store,
        string documentId,
        string content,
        string description,
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        var metadata = new ContentMetadata
        {
            ContentType = "text/plain",
            Name = documentId,
            Description = description,
            Origin = ContentSource.System,
            Tags = new Dictionary<string, string> { ["folder"] = "/skills" }
        };
        return store.WriteNamedTextAsync(scope, content, metadata, cancellationToken);
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
        var contentData = await store.ReadBytesAsync(scope, doc.Id, cancellationToken);
        if (contentData == null) return;

        // Merge skill-link tag into existing tags
        var newTags = new Dictionary<string, string>(doc.Tags ?? new Dictionary<string, string>())
        {
            [$"description:{skillName}"] = descriptionOverride
        };

        await store.WriteBytesAsync(scope, contentData,
            new ContentMetadata
            {
                ContentType = doc.ContentType,
                Name = documentId,
                Description = doc.Description,
                Origin = doc.Origin,
                Tags = newTags,
                OriginalSource = doc.OriginalSource
            },
            new ContentWriteOptions
            {
                Mode = ContentWriteMode.ReplaceById,
                ContentId = doc.Id,
                IfMatchVersion = doc.Version
            },
            cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Knowledge Document Upload (Agent-Scoped)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Upload a knowledge document for a specific agent.
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
    /// <returns>Metadata for the uploaded content.</returns>
    public static Task<ContentInfo> UploadKnowledgeDocumentAsync(
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
            ContentType = contentType,
            Name = documentName,
            Description = description,
            Origin = ContentSource.System,
            Tags = tags
        };
        return store.WriteNamedBytesAsync(agentName, data, metadata, cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Memory Write (Agent-Scoped)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Write a memory entry for a specific agent.
    /// Create an append-only memory event for a specific agent.
    /// Canonical /memory writes are reserved for the memory consolidator.
    /// </summary>
    /// <param name="store">The content store.</param>
    /// <param name="agentName">Agent that owns this memory.</param>
    /// <param name="title">Stable key within agent scope (acts as the memory's filename).</param>
    /// <param name="content">Memory content text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Metadata for the written content.</returns>
    public static Task<ContentInfo> WriteMemoryAsync(
        this IContentStore store,
        string agentName,
        string title,
        string content,
        CancellationToken cancellationToken = default)
    {
        var metadata = new ContentMetadata
        {
            ContentType = "text/plain",
            Name = title,
            Origin = ContentSource.Agent,
            Tags = new Dictionary<string, string>
            {
                ["folder"] = "/memory/events",
                ["memory.kind"] = "agent_note"
            }
        };
        return store.WriteTextAsync(
            agentName,
            content,
            metadata,
            new ContentWriteOptions
            {
                Mode = ContentWriteMode.Create,
                FailIfNameExists = true
            },
            cancellationToken);
    }

    private static async Task<ContentInfo> WriteNamedTextAsync(
        this IContentStore store,
        string? scope,
        string content,
        ContentMetadata metadata,
        CancellationToken cancellationToken)
    {
        var data = Encoding.UTF8.GetBytes(content);
        return await store.WriteNamedBytesAsync(scope, data, metadata, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ContentInfo> WriteNamedBytesAsync(
        this IContentStore store,
        string? scope,
        byte[] data,
        ContentMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (metadata.Name is null)
        {
            return await store.WriteBytesAsync(
                scope,
                data,
                metadata,
                new ContentWriteOptions { Mode = ContentWriteMode.Create },
                cancellationToken).ConfigureAwait(false);
        }

        var existing = await store.QueryAsync(
            scope,
            new ContentQuery
            {
                Name = metadata.Name,
                Tags = metadata.Tags
            },
            cancellationToken).ConfigureAwait(false);

        if (existing.Count == 0)
        {
            return await store.WriteBytesAsync(
                scope,
                data,
                metadata,
                new ContentWriteOptions
                {
                    Mode = ContentWriteMode.Create,
                    FailIfNameExists = true
                },
                cancellationToken).ConfigureAwait(false);
        }

        return await store.WriteBytesAsync(
            scope,
            data,
            metadata,
            new ContentWriteOptions
            {
                Mode = ContentWriteMode.ReplaceById,
                ContentId = existing[0].Id,
                IfMatchVersion = existing[0].Version
            },
            cancellationToken).ConfigureAwait(false);
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

    public Task<ContentInfo> WriteAsync(string? scope, Stream data,
        ContentMetadata metadata, ContentWriteOptions options, CancellationToken cancellationToken = default)
    {
        // Inject folder tag
        var tags = new Dictionary<string, string>(metadata.Tags ?? new Dictionary<string, string>())
        {
            ["folder"] = Path
        };
        if (Options.Tags != null)
            foreach (var kv in Options.Tags) tags.TryAdd(kv.Key, kv.Value);

        var merged = metadata with { Tags = tags };
        return _store.WriteAsync(scope, data, merged, options, cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string scope, string nameOrId, CancellationToken cancellationToken = default)
    {
        var info = await StatAsync(scope, nameOrId, cancellationToken);
        return info == null
            ? null
            : await _store.OpenReadAsync(scope, info.Id, cancellationToken);
    }

    public async Task<ContentInfo?> StatAsync(string scope, string nameOrId, CancellationToken cancellationToken = default)
    {
        // First try direct ID lookup
        var byId = await _store.StatAsync(scope, nameOrId, cancellationToken);
        if (byId != null && byId.Tags != null &&
            byId.Tags.TryGetValue("folder", out var folder) &&
            folder.Equals(Path, StringComparison.OrdinalIgnoreCase))
            return byId;

        // Fall back to name lookup within this folder
        var results = await _store.QueryAsync(scope, new ContentQuery
        {
            Name = nameOrId,
            Tags = new Dictionary<string, string> { ["folder"] = Path }
        }, cancellationToken);

        if (results.Count == 0) return null;
        return results[0];
    }

    public async Task DeleteAsync(
        string scope,
        string nameOrId,
        ContentDeleteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Try as direct ID first, else resolve via name
        var info = await StatAsync(scope, nameOrId, cancellationToken);
        if (info != null)
            await _store.DeleteAsync(scope, info.Id, options, cancellationToken);
    }

    public Task<IReadOnlyList<ContentInfo>> ListAsync(string scope, CancellationToken cancellationToken = default)
    {
        return _store.QueryAsync(scope, new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = Path }
        }, cancellationToken);
    }
}
