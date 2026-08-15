using System.Text;

namespace HPD.Payments.Tools.Conformance;

internal static class ReleaseApprovalRepository
{
    internal static IReadOnlyList<ReleaseApproval> LoadAll(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        if (new DirectoryInfo(root).LinkTarget is not null)
            throw new IOException("Release approval root may not be linked.");
        var approvals = new List<ReleaseApproval>();
        var addresses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
                !path.EndsWith(".approval", StringComparison.Ordinal))
                throw new InvalidDataException("Release approval repository contains inadmissible residue.");
            var approval = ReleaseApproval.Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
            var address = approval.ContentAddress();
            var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            if (!StringComparer.Ordinal.Equals(relative, $"{address[..2]}/{address}.approval") || !addresses.Add(address))
                throw new InvalidDataException("Release approval filename, address, or uniqueness is invalid.");
            approvals.Add(approval);
            if (approvals.Count > 100_000) throw new InvalidDataException("Release approval repository is over-bound.");
        }
        return approvals.AsReadOnly();
    }

    internal static IReadOnlyList<string> ValidateLineage(IReadOnlyList<ReleaseManifest> manifests,
        IReadOnlyList<ReleaseApproval> approvals, IReadOnlyDictionary<string, ReleaseApprovalKey> keys,
        ReleaseAuthorizationPolicy policy, DateTimeOffset evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(manifests); ArgumentNullException.ThrowIfNull(approvals);
        var errors = new List<string>();
        var addresses = manifests.Select(static manifest => manifest.ContentAddress()).ToHashSet(StringComparer.Ordinal);
        foreach (var approval in approvals)
            if (!addresses.Contains(approval.ManifestAddress)) errors.Add("release-approval-orphan-manifest");
        foreach (var manifest in manifests)
        {
            var matching = approvals.Where(approval =>
                StringComparer.Ordinal.Equals(approval.ManifestAddress, manifest.ContentAddress())).ToArray();
            if (manifest.Lifecycle == ReleaseManifestLifecycle.Candidate)
            {
                if (matching.Length != 0) errors.Add("candidate-manifest-has-release-approval");
                continue;
            }
            var context = new ReleaseAuthorizationContext(matching, keys, policy, evaluatedAtUtc);
            errors.AddRange(ReleaseAuthorizationValidator.Validate(manifest, context));
        }
        return errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
}
