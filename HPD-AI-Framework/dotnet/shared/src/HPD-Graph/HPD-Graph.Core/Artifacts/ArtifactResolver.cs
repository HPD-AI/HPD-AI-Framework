using HPDAgent.Graph.Abstractions.Artifacts;

namespace HPDAgent.Graph.Core.Artifacts;

/// <summary>
/// Resolves artifact keys with hierarchical namespace fallback (Phase 5: Hierarchical Namespaces).
/// Mirrors existing IGraphStateScope pattern for consistent scoping semantics.
/// </summary>
public class ArtifactResolver
{
    private readonly HashSet<string> _registeredNamespaces;
    private readonly IArtifactRegistry _registry;

    public ArtifactResolver(IArtifactRegistry registry, HashSet<string> registeredNamespaces)
    {
        _registry = registry;
        _registeredNamespaces = registeredNamespaces;
    }

    /// <summary>
    /// Resolve artifact key with namespace fallback.
    /// Search order:
    ///   1. Local scope (current subgraph namespace + key)
    ///   2. Parent scopes (walk up namespace hierarchy)
    ///   3. Global scope (key as-is)
    ///
    /// Example:
    ///   Current namespace: ["pipeline", "stage1"]
    ///   Requested key: ["users"]
    ///
    ///   Search:
    ///   1. ["pipeline", "stage1", "users"] (local)
    ///   2. ["pipeline", "users"] (parent)
    ///   3. ["users"] (global)
    /// </summary>
    public async Task<ArtifactKey> ResolveAsync(
        ArtifactKey requestedKey,
        IReadOnlyList<string>? currentNamespace,
        CancellationToken ct = default)
    {
        // If key is absolute (starts with any known namespace), use as-is
        if (IsAbsoluteKey(requestedKey))
            return requestedKey;

        // Try local scope first
        if (currentNamespace != null && currentNamespace.Count > 0)
        {
            var localKey = new ArtifactKey
            {
                Path = currentNamespace.Concat(requestedKey.Path).ToList(),
                Partition = requestedKey.Partition
            };

            if (await ArtifactExistsAsync(localKey, ct))
                return localKey;

            // Try parent scopes (walk up namespace)
            for (int i = currentNamespace.Count - 1; i > 0; i--)
            {
                var parentNamespace = currentNamespace.Take(i).ToList();
                var parentKey = new ArtifactKey
                {
                    Path = parentNamespace.Concat(requestedKey.Path).ToList(),
                    Partition = requestedKey.Partition
                };

                if (await ArtifactExistsAsync(parentKey, ct))
                    return parentKey;
            }
        }

        // Global scope (as-is)
        if (await ArtifactExistsAsync(requestedKey, ct))
            return requestedKey;

        // Not found - throw with helpful error message showing search path
        var searchedScopes = GetSearchedScopes(requestedKey, currentNamespace);
        throw new ArtifactNotFoundException(
            $"Artifact not found: {requestedKey} " +
            $"(searched scopes: {string.Join(", ", searchedScopes)})");
    }

    /// <summary>
    /// Check if artifact exists by checking if it has any producing nodes.
    /// </summary>
    private async Task<bool> ArtifactExistsAsync(ArtifactKey key, CancellationToken ct)
    {
        var producers = await _registry.GetProducingNodeIdsAsync(key, key.Partition, ct);
        return producers.Count > 0;
    }

    /// <summary>
    /// Check if key is absolute (starts with a known namespace prefix).
    /// </summary>
    private bool IsAbsoluteKey(ArtifactKey key)
    {
        // Key is absolute if it starts with a known namespace prefix
        // Serialize namespace path as "seg1/seg2" for comparison
        var keyPrefix = string.Join("/", key.Path.Take(1)); // Check first segment

        // Check if first segment matches any registered namespace first segment
        foreach (var ns in _registeredNamespaces)
        {
            var nsSegments = ns.Split('/');
            if (nsSegments.Length > 0 && key.Path.Count >= nsSegments.Length)
            {
                // Check if key starts with this namespace
                var matches = true;
                for (int i = 0; i < nsSegments.Length; i++)
                {
                    if (key.Path[i] != nsSegments[i])
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Get list of scopes that were searched for error reporting.
    /// </summary>
    private List<string> GetSearchedScopes(ArtifactKey requestedKey, IReadOnlyList<string>? currentNamespace)
    {
        var scopes = new List<string>();

        // Local scope
        if (currentNamespace != null && currentNamespace.Count > 0)
        {
            var localPath = currentNamespace.Concat(requestedKey.Path);
            scopes.Add(string.Join("/", localPath));

            // Parent scopes
            for (int i = currentNamespace.Count - 1; i > 0; i--)
            {
                var parentPath = currentNamespace.Take(i).Concat(requestedKey.Path);
                scopes.Add(string.Join("/", parentPath));
            }
        }

        // Global scope
        scopes.Add(string.Join("/", requestedKey.Path));

        return scopes;
    }
}

/// <summary>
/// Exception thrown when artifact cannot be found in any namespace scope.
/// </summary>
public class ArtifactNotFoundException : Exception
{
    public ArtifactNotFoundException(string message) : base(message) { }
}
