using System.Collections.Concurrent;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Abstractions.Graph;

namespace HPDAgent.Graph.Core.Artifacts;

/// <summary>
/// O(1) artifact lookup index built at graph initialization.
/// Prevents linear scans during namespace resolution and dependency tracking.
///
/// Built by scanning graph nodes and indexing ProducesArtifact declarations.
/// Used by orchestrator to resolve artifact dependencies and for demand-driven execution.
/// </summary>
public class ArtifactIndex
{
    // Index: ArtifactKey → List<NodeId>
    private readonly Dictionary<ArtifactKey, List<string>> _producerIndex = new();

    // Registered namespaces (for absolute key detection)
    private readonly HashSet<IReadOnlyList<string>> _registeredNamespaces = new(
        new ListEqualityComparer<string>());

    /// <summary>
    /// Build the artifact index by scanning a graph and all its subgraphs.
    /// This method is idempotent - safe to call multiple times.
    /// </summary>
    /// <param name="graph">Root graph to index.</param>
    /// <param name="parentNamespace">Parent namespace (for nested SubGraphs).</param>
    public void BuildIndex(Abstractions.Graph.Graph graph, IReadOnlyList<string>? parentNamespace = null)
    {
        if (graph == null)
            throw new ArgumentNullException(nameof(graph));

        foreach (var node in graph.Nodes)
        {
            // Determine current namespace (node's namespace OR parent's namespace)
            var currentNamespace = GetCurrentNamespace(node, parentNamespace);

            // Validate and register namespace if present
            if (currentNamespace != null && currentNamespace.Count > 0)
            {
                ValidateNamespace(currentNamespace);
                _registeredNamespaces.Add(currentNamespace);
            }

            // Index artifact production
            if (node.ProducesArtifact != null)
            {
                // Qualify artifact key with namespace
                var qualifiedKey = QualifyArtifactKey(node.ProducesArtifact, currentNamespace);

                if (!_producerIndex.ContainsKey(qualifiedKey))
                {
                    _producerIndex[qualifiedKey] = new List<string>();
                }

                // Add node as producer
                if (!_producerIndex[qualifiedKey].Contains(node.Id))
                {
                    _producerIndex[qualifiedKey].Add(node.Id);
                }
            }

            // Recurse into SubGraphs
            if (node.Type == NodeType.SubGraph && node.SubGraph != null)
            {
                BuildIndex(node.SubGraph, currentNamespace);
            }

            // Recurse into Map processor graphs
            if (node.Type == NodeType.Map)
            {
                if (node.MapProcessorGraph != null)
                {
                    BuildIndex(node.MapProcessorGraph, currentNamespace);
                }

                if (node.MapProcessorGraphs != null)
                {
                    foreach (var processorGraph in node.MapProcessorGraphs.Values)
                    {
                        BuildIndex(processorGraph, currentNamespace);
                    }
                }

                if (node.MapDefaultGraph != null)
                {
                    BuildIndex(node.MapDefaultGraph, currentNamespace);
                }
            }
        }
    }

    /// <summary>
    /// Get node IDs that produce a specific artifact.
    /// Returns empty list if no producers found.
    /// </summary>
    /// <param name="key">Artifact key to look up.</param>
    /// <returns>List of node IDs that produce this artifact.</returns>
    public IReadOnlyList<string> GetProducers(ArtifactKey key)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        return _producerIndex.TryGetValue(key, out var producers)
            ? producers.AsReadOnly()
            : Array.Empty<string>();
    }

    /// <summary>
    /// Check if an artifact is produced by any node in the indexed graphs.
    /// </summary>
    /// <param name="key">Artifact key to check.</param>
    /// <returns>True if at least one producer exists.</returns>
    public bool HasProducers(ArtifactKey key)
    {
        return _producerIndex.ContainsKey(key) && _producerIndex[key].Count > 0;
    }

    /// <summary>
    /// Get all artifact keys in the index.
    /// Useful for catalog/discovery.
    /// </summary>
    /// <returns>All artifact keys with at least one producer.</returns>
    public IEnumerable<ArtifactKey> GetAllArtifactKeys()
    {
        return _producerIndex.Keys;
    }

    /// <summary>
    /// Get total number of indexed artifacts.
    /// </summary>
    public int ArtifactCount => _producerIndex.Count;

    /// <summary>
    /// Clear the index (useful for rebuilding).
    /// </summary>
    public void Clear()
    {
        _producerIndex.Clear();
        _registeredNamespaces.Clear();
    }

    /// <summary>
    /// Determine the current namespace for a node.
    /// Node-specific namespace combines with parent namespace.
    /// Phase 5: Hierarchical artifact namespaces.
    /// </summary>
    private static IReadOnlyList<string>? GetCurrentNamespace(Node node, IReadOnlyList<string>? parentNamespace)
    {
        // Combine parent and node namespaces
        if (node.ArtifactNamespace != null && parentNamespace != null)
        {
            return parentNamespace.Concat(node.ArtifactNamespace).ToList();
        }

        return node.ArtifactNamespace ?? parentNamespace;
    }

    /// <summary>
    /// Qualify an artifact key with its namespace.
    /// Phase 5: Prefix path with namespace segments.
    /// </summary>
    private static ArtifactKey QualifyArtifactKey(ArtifactKey key, IReadOnlyList<string>? currentNamespace)
    {
        // No namespace = return key as-is
        if (currentNamespace == null || currentNamespace.Count == 0)
            return key;

        // Phase 5: Prefix path with namespace
        var qualifiedPath = currentNamespace.Concat(key.Path).ToList();
        return new ArtifactKey { Path = qualifiedPath, Partition = key.Partition };
    }

    /// <summary>
    /// Validate namespace according to Phase 5 rules.
    /// </summary>
    private static void ValidateNamespace(IReadOnlyList<string> namespacePath)
    {
        // Max depth validation
        if (namespacePath.Count > 10)
            throw new ArgumentException(
                $"Namespace depth exceeds maximum of 10 levels: {string.Join("/", namespacePath)}");

        // Regex: Must start/end with alphanumeric, can contain alphanumeric/hyphen/underscore in middle
        // Special case: single character can be just alphanumeric
        var validSegmentRegex = new System.Text.RegularExpressions.Regex(
            @"^[a-zA-Z0-9][a-zA-Z0-9_-]{0,48}[a-zA-Z0-9]$|^[a-zA-Z0-9]$");

        foreach (var segment in namespacePath)
        {
            // Empty/whitespace validation
            if (string.IsNullOrWhiteSpace(segment))
                throw new ArgumentException("Namespace segment cannot be empty or whitespace");

            // Length validation
            if (segment.Length > 50)
                throw new ArgumentException(
                    $"Namespace segment exceeds maximum length of 50: '{segment}'");

            // Character pattern validation
            if (!validSegmentRegex.IsMatch(segment))
                throw new ArgumentException(
                    $"Invalid namespace segment: '{segment}'. " +
                    "Must contain only alphanumeric, hyphen, underscore (a-zA-Z0-9_-), " +
                    "cannot start/end with hyphen or underscore.");

            // Consecutive special character validation
            if (segment.Contains("--") || segment.Contains("__") ||
                segment.Contains("_-") || segment.Contains("-_"))
                throw new ArgumentException(
                    $"Namespace segment cannot contain consecutive special characters: '{segment}'");
        }
    }

    /// <summary>
    /// Equality comparer for IReadOnlyList&lt;string&gt; (for HashSet).
    /// </summary>
    private class ListEqualityComparer<T> : IEqualityComparer<IReadOnlyList<T>>
        where T : IEquatable<T>
    {
        public bool Equals(IReadOnlyList<T>? x, IReadOnlyList<T>? y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            if (x.Count != y.Count) return false;

            for (int i = 0; i < x.Count; i++)
            {
                if (!x[i].Equals(y[i]))
                    return false;
            }

            return true;
        }

        public int GetHashCode(IReadOnlyList<T> obj)
        {
            if (obj == null) return 0;

            var hash = new HashCode();
            foreach (var item in obj)
            {
                hash.Add(item);
            }
            return hash.ToHashCode();
        }
    }
}
