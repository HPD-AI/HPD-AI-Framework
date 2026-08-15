using System.Text;

namespace HPD.Payments.Tools.Conformance;

/// <summary>Reconstructs one exact append-only release-manifest lineage.</summary>
internal static class ReleaseManifestRepository
{
    internal static IReadOnlyList<ReleaseManifest> LoadChain(string rootDirectory, RegistrySnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(snapshot);
        var root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        if (new DirectoryInfo(root).LinkTarget is not null)
            throw new IOException("Release manifest root may not be a symbolic link.");

        var byAddress = new Dictionary<string, ReleaseManifest>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || !path.EndsWith(".manifest", StringComparison.Ordinal))
                throw new InvalidDataException("Release manifest repository contains inadmissible residue.");
            var canonical = File.ReadAllText(path, new UTF8Encoding(false, true));
            var manifest = ReleaseManifest.Parse(canonical);
            _ = manifest.ValidateAgainst(snapshot);
            var address = manifest.ContentAddress();
            var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            if (!StringComparer.Ordinal.Equals(relative, $"{address[..2]}/{address}.manifest") ||
                !byAddress.TryAdd(address, manifest))
                throw new InvalidDataException("Release manifest filename, address, or uniqueness is invalid.");
        }

        if (byAddress.Count == 0) return Array.Empty<ReleaseManifest>();
        var genesis = byAddress.Where(static x => x.Value.PredecessorManifestDigest == "GENESIS").ToArray();
        if (genesis.Length != 1) throw new InvalidDataException("Release manifest chain requires exactly one genesis.");
        var successor = new Dictionary<string, KeyValuePair<string, ReleaseManifest>>(StringComparer.Ordinal);
        foreach (var entry in byAddress.Where(static x => x.Value.PredecessorManifestDigest != "GENESIS"))
            if (!successor.TryAdd(entry.Value.PredecessorManifestDigest, entry))
                throw new InvalidDataException("Release manifest chain forks at one predecessor.");

        var ordered = new List<ReleaseManifest>(byAddress.Count) { genesis[0].Value };
        var orderedAddresses = new HashSet<string>(StringComparer.Ordinal) { genesis[0].Key };
        var cursor = genesis[0].Key;
        while (successor.Remove(cursor, out var next))
        {
            var target = next.Value.SupersedesManifestDigest;
            if (target is not null && !orderedAddresses.Contains(target))
                throw new InvalidDataException("Release manifest supersedes an absent or future target.");
            if (next.Value.Lifecycle == ReleaseManifestLifecycle.Withdrawal &&
                (target is null || byAddress[target].Lifecycle != ReleaseManifestLifecycle.Published))
                throw new InvalidDataException("Release withdrawal target is not a prior published manifest.");
            ordered.Add(next.Value);
            orderedAddresses.Add(next.Key);
            cursor = next.Key;
        }
        if (ordered.Count != byAddress.Count || successor.Count != 0)
            throw new InvalidDataException("Release manifest chain contains an orphan, cycle, or missing predecessor.");
        return ordered.AsReadOnly();
    }
}
