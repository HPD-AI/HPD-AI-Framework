using System.Security.Cryptography;
using System.Text;

namespace HPD.Payments.Tools.Conformance;

/// <summary>Produces a deterministic retained-artifact inventory and rejects inadmissible proof residue.</summary>
internal static class ProofArtifactInventory
{
    /// <summary>Captures sorted receipt paths/digests and reports every forbidden retained artifact.</summary>
    internal static ProofInventoryResult Capture(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var entries = new List<string>();
        var errors = new List<string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) errors.Add("retained-reparse-point:" + relative);
            if (Directory.Exists(path))
            {
                var name = Path.GetFileName(path);
                if (name is "bin" or "obj" or "publish") errors.Add("retained-build-directory:" + relative);
                continue;
            }
            if (!relative.EndsWith(".receipt", StringComparison.Ordinal)) errors.Add("retained-non-receipt:" + relative);
            var digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            entries.Add($"{relative}|{digest}");
        }
        var canonical = string.Join('\n', entries);
        var inventoryDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new(errors.Count == 0, entries.Count, inventoryDigest,
            errors.Order(StringComparer.Ordinal).ToArray(), entries.AsReadOnly());
    }
}

/// <summary>Reports deterministic retained files, inventory digest, and cleanup failures.</summary>
internal sealed record ProofInventoryResult(bool IsClean, int FileCount, string InventoryDigest,
    IReadOnlyList<string> Errors, IReadOnlyList<string> Entries);
