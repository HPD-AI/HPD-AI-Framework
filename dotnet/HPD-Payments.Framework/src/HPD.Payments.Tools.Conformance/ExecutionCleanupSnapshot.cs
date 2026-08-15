using System.Security.Cryptography;
using System.Text;

namespace HPD.Payments.Tools.Conformance;

/// <summary>Verifies that exact command-declared cleanup patterns retain no matching artifact.</summary>
internal static class ExecutionCleanupSnapshotter
{
    internal static ExecutionCleanupSnapshot Capture(string rootDirectory, IReadOnlyCollection<string> cleanupPatterns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(cleanupPatterns);
        if (cleanupPatterns.Count > 4_096) throw new ArgumentOutOfRangeException(nameof(cleanupPatterns));
        var root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Cleanup root is absent or linked.");
        var patterns = cleanupPatterns.Select(static x => x.Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
        if (patterns.Any(static x => string.IsNullOrWhiteSpace(x) || x.Length > 4_096 || x[0] == '/'))
            throw new InvalidDataException("Cleanup pattern is invalid.");
        var residue = new List<string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Cleanup inventory contains a linked artifact.");
            var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            if (patterns.Any(pattern => Matches(pattern.AsSpan(), relative.AsSpan()))) residue.Add(relative);
            if (residue.Count > 100_000) throw new InvalidDataException("Cleanup residue is over-bound.");
        }
        residue.Sort(StringComparer.Ordinal);
        var canonical = ProofCanonical.Join(ProofCanonical.Join(patterns), ProofCanonical.Join(residue.ToArray()));
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new(residue.Count == 0, digest, residue.AsReadOnly());
    }

    private static bool Matches(ReadOnlySpan<char> pattern, ReadOnlySpan<char> value)
    {
        var states = new HashSet<(int Pattern, int Value)> { (0, 0) };
        var pending = new Stack<(int Pattern, int Value)>();
        pending.Push((0, 0));
        while (pending.TryPop(out var state))
        {
            var (p, v) = state;
            if (p == pattern.Length && v == value.Length) return true;
            if (p == pattern.Length) continue;
            if (pattern[p] == '*')
            {
                var recursive = p + 1 < pattern.Length && pattern[p + 1] == '*';
                var next = p + (recursive ? 2 : 1);
                Push(next, v);
                if (v < value.Length && (recursive || value[v] != '/')) Push(p, v + 1);
            }
            else if (v < value.Length && (pattern[p] == '?' && value[v] != '/' || pattern[p] == value[v]))
                Push(p + 1, v + 1);
        }
        return false;

        void Push(int p, int v)
        {
            if (states.Add((p, v))) pending.Push((p, v));
        }
    }
}

internal sealed record ExecutionCleanupSnapshot(bool IsClean, string InventoryDigest, IReadOnlyList<string> Residue)
{
    internal string Attestation => "clean:" + InventoryDigest;
}
