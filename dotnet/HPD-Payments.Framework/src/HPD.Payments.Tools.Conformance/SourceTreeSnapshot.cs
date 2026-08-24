using System.Security.Cryptography;
using System.Text;

namespace HPD.Payments.Tools.Conformance;

/// <summary>Captures a bounded deterministic byte inventory for explicit source roots.</summary>
internal static class SourceTreeSnapshotter
{
    internal static SourceTreeSnapshot Capture(string rootDirectory, IReadOnlyCollection<string> includedRelativePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(includedRelativePaths);
        if (includedRelativePaths.Count is < 1 or > 4_096)
            throw new ArgumentOutOfRangeException(nameof(includedRelativePaths));
        var root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root) || IsLink(root)) throw new IOException("Source root is absent or linked.");
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var relativeInput in includedRelativePaths)
        {
            if (string.IsNullOrWhiteSpace(relativeInput) || Path.IsPathRooted(relativeInput))
                throw new InvalidDataException("Source inventory path is not relative.");
            var candidate = Path.GetFullPath(Path.Combine(root, relativeInput));
            if (!IsContained(root, candidate)) throw new InvalidDataException("Source inventory path escapes its root.");
            ValidateComponents(root, candidate);
            if (File.Exists(candidate)) paths.Add(candidate);
            else if (Directory.Exists(candidate))
            {
                foreach (var path in Directory.EnumerateFiles(candidate, "*", SearchOption.AllDirectories))
                {
                    if (IsLink(path)) throw new IOException("Source inventory contains a linked file.");
                    ValidateComponents(root, path);
                    var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                    if (IsGeneratedBuildOutput(relative)) continue;
                    paths.Add(path);
                    if (paths.Count > 100_000) throw new InvalidDataException("Source inventory contains too many files.");
                }
            }
            else throw new FileNotFoundException("Source inventory path is absent.", candidate);
        }

        var entries = new List<string>(paths.Count);
        foreach (var path in paths)
        {
            var info = new FileInfo(path);
            if (info.Length > 67_108_864) throw new InvalidDataException("Source inventory file is over-bound.");
            var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            var digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            entries.Add(ProofCanonical.Join(relative, info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), digest));
        }
        var canonical = ProofCanonical.Join(entries.ToArray());
        var inventoryDigest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new(inventoryDigest, entries.Count, entries.AsReadOnly());
    }

    internal static void RequireStable(SourceTreeSnapshot before, SourceTreeSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (!StringComparer.Ordinal.Equals(before.InventoryDigest, after.InventoryDigest) || before.FileCount != after.FileCount ||
            !before.Entries.SequenceEqual(after.Entries, StringComparer.Ordinal))
            throw new InvalidDataException("Source inventory changed during execution.");
    }

    private static bool IsContained(string root, string candidate) => candidate.StartsWith(
        root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool IsGeneratedBuildOutput(string relativePath) =>
        relativePath.Split('/').Any(static segment => segment is "bin" or "obj");

    private static void ValidateComponents(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        var cursor = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            cursor = Path.Combine(cursor, segment);
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && IsLink(cursor))
                throw new IOException("Source inventory traverses a symbolic link.");
        }
    }
}

internal sealed record SourceTreeSnapshot(string InventoryDigest, int FileCount, IReadOnlyList<string> Entries);
