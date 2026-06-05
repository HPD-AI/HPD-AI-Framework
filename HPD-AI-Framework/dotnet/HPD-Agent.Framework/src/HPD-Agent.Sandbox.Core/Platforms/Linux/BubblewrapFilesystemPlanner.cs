using HPD.Agent.Sandbox.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace HPD.Agent.Sandbox.Platforms.Linux;

internal static class BubblewrapFilesystemPlanner
{
    private static readonly HashSet<string> RootReadDenySkips = new(StringComparer.Ordinal)
    {
        "/dev",
        "/proc",
        "/sys",
    };

    public static BubblewrapMountPlan PlanReadDenyMounts(IEnumerable<string> denyReadPaths)
    {
        var mounts = new List<BubblewrapMount>();
        var warnings = new List<string>();
        var protectedDestinations = new HashSet<string>(StringComparer.Ordinal);

        foreach (var denyPath in denyReadPaths)
        {
            foreach (var expandedPath in ExpandGlobIfNeeded(denyPath, warnings))
            {
                var normalized = NormalizeForMountPlanning(expandedPath);
                foreach (var mount in PlanReadDenyMountsForPath(normalized))
                {
                    if (protectedDestinations.Add(mount.DestinationPath))
                        mounts.Add(mount);
                }
            }
        }

        return mounts.Count == 0 && warnings.Count == 0
            ? BubblewrapMountPlan.Empty
            : new BubblewrapMountPlan(mounts, []) { Warnings = warnings };
    }

    public static BubblewrapMountPlan PlanSandboxFilesystemMounts(
        IEnumerable<string> allowWritePaths,
        IEnumerable<string> denyReadPaths,
        IEnumerable<string> allowReadPaths,
        IEnumerable<string> denyWritePaths)
    {
        var mounts = new List<BubblewrapMount>();
        var cleanupPaths = new List<string>();
        var warnings = new List<string>();

        var writableMounts = PlanWritableMounts(allowWritePaths);
        mounts.AddRange(writableMounts);

        var readDenyPlan = PlanReadDenyMounts(denyReadPaths);
        mounts.AddRange(readDenyPlan.Mounts);
        warnings.AddRange(readDenyPlan.Warnings);

        // Read-deny tmpfs overlays can wipe out writable subtrees. Rebind the
        // write roots before allowRead/writeDeny so explicit write policy still
        // wins where it is meant to.
        mounts.AddRange(writableMounts);

        var readAllowPlan = PlanReadAllowMounts(allowReadPaths);
        mounts.AddRange(readAllowPlan.Mounts);
        warnings.AddRange(readAllowPlan.Warnings);

        var writeDenyPlan = PlanWriteDenyMounts(denyWritePaths, writableMounts.Select(mount => mount.DestinationPath));
        mounts.AddRange(writeDenyPlan.Mounts);
        cleanupPaths.AddRange(writeDenyPlan.CleanupPaths);
        warnings.AddRange(writeDenyPlan.Warnings);

        return mounts.Count == 0 && cleanupPaths.Count == 0 && warnings.Count == 0
            ? BubblewrapMountPlan.Empty
            : new BubblewrapMountPlan(mounts, cleanupPaths) { Warnings = warnings };
    }

    public static BubblewrapMountPlan PlanReadAllowMounts(IEnumerable<string> allowReadPaths)
    {
        var mounts = new List<BubblewrapMount>();
        var warnings = new List<string>();
        var protectedDestinations = new HashSet<string>(StringComparer.Ordinal);

        foreach (var allowPath in allowReadPaths)
        {
            foreach (var expandedPath in ExpandGlobIfNeeded(allowPath, warnings))
            {
                var normalized = NormalizeForMountPlanning(expandedPath);
                if (!Directory.Exists(normalized) && !File.Exists(normalized))
                    continue;

                if (protectedDestinations.Add(normalized))
                {
                    mounts.Add(new BubblewrapMount(
                        BubblewrapMountKind.ReadOnlyBind,
                        normalized,
                        normalized));
                }
            }
        }

        return mounts.Count == 0 && warnings.Count == 0
            ? BubblewrapMountPlan.Empty
            : new BubblewrapMountPlan(mounts, []) { Warnings = warnings };
    }

    public static BubblewrapMountPlan PlanWriteDenyMounts(
        IEnumerable<string> denyWritePaths,
        IEnumerable<string> writableRoots)
    {
        var normalizedWritableRoots = writableRoots
            .Select(NormalizeForMountPlanning)
            .Where(path => Directory.Exists(path) || File.Exists(path))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var originalWritableRoots = writableRoots
            .Select(path => PathNormalizer.Normalize(path, resolveSymlinks: false))
            .Where(path => Directory.Exists(path) || File.Exists(path))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var mounts = new List<BubblewrapMount>();
        var cleanupPaths = new List<string>();
        var protectedDestinations = new HashSet<string>(StringComparer.Ordinal);

        foreach (var denyPath in denyWritePaths)
        {
            var original = PathNormalizer.Normalize(denyPath, resolveSymlinks: false);
            if (originalWritableRoots.Any(root => IsWithinNormalizedPath(original, root)))
            {
                foreach (var symlinkComponent in FindSymlinkComponents(original))
                {
                    if (!originalWritableRoots.Any(root => IsWithinNormalizedPath(symlinkComponent, root)))
                        continue;

                    AddProtectionMount(
                        new PlannedMount(
                            new BubblewrapMount(
                                BubblewrapMountKind.ReadOnlyBind,
                                "/dev/null",
                                symlinkComponent),
                            CleanupPath: null),
                        mounts,
                        cleanupPaths,
                        protectedDestinations);
                }
            }

            var normalized = NormalizeForMountPlanning(denyPath);
            if (!normalizedWritableRoots.Any(root => PathNormalizer.IsWithinPath(normalized, root)))
                continue;

            var protection = PlanWriteDenyMount(normalized);
            if (protection is null)
                continue;

            AddProtectionMount(protection, mounts, cleanupPaths, protectedDestinations);
        }

        return mounts.Count == 0
            ? BubblewrapMountPlan.Empty
            : new BubblewrapMountPlan(mounts, cleanupPaths);
    }

    private static IReadOnlyList<BubblewrapMount> PlanWritableMounts(IEnumerable<string> allowWritePaths)
    {
        var mounts = new List<BubblewrapMount>();
        var protectedDestinations = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in allowWritePaths)
        {
            var normalized = NormalizeForMountPlanning(path);

            if (!Directory.Exists(normalized) && !File.Exists(normalized))
                continue;

            if (normalized.StartsWith("/dev/", StringComparison.Ordinal))
                continue;

            if (protectedDestinations.Add(normalized))
            {
                mounts.Add(new BubblewrapMount(
                    BubblewrapMountKind.Bind,
                    normalized,
                    normalized));
            }
        }

        return mounts;
    }

    private static IEnumerable<BubblewrapMount> PlanReadDenyMountsForPath(string normalizedPath)
    {
        if (IsRootPath(normalizedPath))
            return PlanRootReadDenyMounts();

        if (Directory.Exists(normalizedPath))
        {
            return
            [
                new BubblewrapMount(BubblewrapMountKind.Tmpfs, SourcePath: null, normalizedPath),
            ];
        }

        if (File.Exists(normalizedPath))
        {
            return
            [
                new BubblewrapMount(BubblewrapMountKind.ReadOnlyBind, "/dev/null", normalizedPath),
            ];
        }

        return [];
    }

    private static IEnumerable<BubblewrapMount> PlanRootReadDenyMounts()
    {
        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateFileSystemEntries("/").ToArray();
        }
        catch
        {
            return [];
        }

        return children
            .Select(path => PathNormalizer.Normalize(path, resolveSymlinks: false))
            .Where(path => !RootReadDenySkips.Contains(path))
            .Select(path => Directory.Exists(path)
                ? new BubblewrapMount(BubblewrapMountKind.Tmpfs, SourcePath: null, path)
                : new BubblewrapMount(BubblewrapMountKind.ReadOnlyBind, "/dev/null", path));
    }

    private static bool IsRootPath(string path)
    {
        var root = Path.GetPathRoot(path);
        return !string.IsNullOrEmpty(root) &&
               PathNormalizer.NormalizeForComparison(path.TrimEnd(Path.DirectorySeparatorChar)) ==
               PathNormalizer.NormalizeForComparison(root.TrimEnd(Path.DirectorySeparatorChar));
    }

    private static IEnumerable<string> ExpandGlobIfNeeded(string path, List<string> warnings)
    {
        if (!PathNormalizer.ContainsGlobChars(path))
            return [path];

        var pattern = NormalizeGlobPattern(path);
        var searchRoot = GetGlobSearchRoot(pattern);
        if (searchRoot is null || !Directory.Exists(searchRoot))
        {
            warnings.Add($"Glob pattern '{path}' was skipped because its static prefix does not exist.");
            return [];
        }

        var regex = GlobToRegex(pattern);
        var matches = new List<string>();

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(
                         searchRoot,
                         "*",
                         SearchOption.AllDirectories))
            {
                var normalized = PathNormalizer.Normalize(entry, resolveSymlinks: false);
                if (regex.IsMatch(normalized))
                    matches.Add(normalized);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            warnings.Add($"Glob pattern '{path}' could not be fully expanded: {ex.Message}");
        }
        catch (DirectoryNotFoundException ex)
        {
            warnings.Add($"Glob pattern '{path}' could not be fully expanded: {ex.Message}");
        }

        if (matches.Count == 0)
            warnings.Add($"Glob pattern '{path}' did not match any existing paths.");

        return matches;
    }

    private static string NormalizeGlobPattern(string path)
    {
        var normalized = path;

        if (normalized == "~")
        {
            normalized = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        else if (normalized.StartsWith("~/", StringComparison.Ordinal))
        {
            normalized = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                normalized[2..]);
        }

        normalized = Environment.ExpandEnvironmentVariables(normalized);
        return Path.IsPathRooted(normalized)
            ? Path.GetFullPath(normalized)
            : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, normalized));
    }

    private static string? GetGlobSearchRoot(string pattern)
    {
        var firstGlob = pattern.IndexOfAny(['*', '?', '[']);
        if (firstGlob < 0)
            return Directory.Exists(pattern) ? pattern : Path.GetDirectoryName(pattern);

        var slash = pattern.LastIndexOf(Path.DirectorySeparatorChar, firstGlob);
        if (slash < 0)
            return Environment.CurrentDirectory;

        if (slash == 0)
            return Path.DirectorySeparatorChar.ToString();

        return pattern[..slash];
    }

    private static Regex GlobToRegex(string pattern)
    {
        var sb = new StringBuilder();
        sb.Append('^');

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            switch (c)
            {
                case '*':
                    if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                    {
                        if (i + 2 < pattern.Length && pattern[i + 2] == Path.DirectorySeparatorChar)
                        {
                            sb.Append("(.*");
                            sb.Append(Regex.Escape(Path.DirectorySeparatorChar.ToString()));
                            sb.Append(")?");
                            i += 2;
                        }
                        else
                        {
                            sb.Append(".*");
                            i++;
                        }
                    }
                    else
                    {
                        sb.Append("[^");
                        sb.Append(Regex.Escape(Path.DirectorySeparatorChar.ToString()));
                        sb.Append("]*");
                    }
                    break;
                case '?':
                    sb.Append("[^");
                    sb.Append(Regex.Escape(Path.DirectorySeparatorChar.ToString()));
                    sb.Append(']');
                    break;
                case '[':
                    var end = pattern.IndexOf(']', i + 1);
                    if (end > i + 1)
                    {
                        var content = pattern.Substring(i + 1, end - i - 1);
                        sb.Append('[');
                        if (content.StartsWith('!'))
                        {
                            sb.Append('^');
                            sb.Append(Regex.Escape(content[1..]));
                        }
                        else
                        {
                            sb.Append(Regex.Escape(content).Replace("\\-", "-"));
                        }
                        sb.Append(']');
                        i = end;
                    }
                    else
                    {
                        sb.Append("\\[");
                    }
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }

        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.CultureInvariant);
    }

    private static void AddProtectionMount(
        PlannedMount protection,
        List<BubblewrapMount> mounts,
        List<string> cleanupPaths,
        HashSet<string> protectedDestinations)
    {
        if (!protectedDestinations.Add(protection.Mount.DestinationPath))
        {
            CleanupUnusedPath(protection.CleanupPath);
            return;
        }

        mounts.Add(protection.Mount);
        if (protection.CleanupPath is not null)
            cleanupPaths.Add(protection.CleanupPath);
    }

    private static PlannedMount? PlanWriteDenyMount(string normalizedPath)
    {
        if (Directory.Exists(normalizedPath) || File.Exists(normalizedPath))
        {
            return new PlannedMount(
                new BubblewrapMount(BubblewrapMountKind.ReadOnlyBind, normalizedPath, normalizedPath),
                CleanupPath: null);
        }

        var firstMissing = FindFirstMissingComponent(normalizedPath);
        if (firstMissing is null)
            return null;

        if (PathNormalizer.NormalizeForComparison(firstMissing) ==
            PathNormalizer.NormalizeForComparison(normalizedPath))
        {
            return new PlannedMount(
                new BubblewrapMount(BubblewrapMountKind.ReadOnlyBind, "/dev/null", normalizedPath),
                CleanupPath: null);
        }

        var emptySource = CreateEmptyDirectorySource();
        return new PlannedMount(
            new BubblewrapMount(BubblewrapMountKind.ReadOnlyBind, emptySource, firstMissing),
            emptySource);
    }

    private static string? FindFirstMissingComponent(string normalizedPath)
    {
        var root = Path.GetPathRoot(normalizedPath);
        if (string.IsNullOrEmpty(root))
            return null;

        var parts = normalizedPath[root.Length..]
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        var current = root.TrimEnd(Path.DirectorySeparatorChar);
        foreach (var part in parts)
        {
            current = string.IsNullOrEmpty(current)
                ? $"{Path.DirectorySeparatorChar}{part}"
                : Path.Combine(current, part);

            if (!Directory.Exists(current) && !File.Exists(current))
                return current;
        }

        return null;
    }

    private static IEnumerable<string> FindSymlinkComponents(string normalizedPath)
    {
        var root = Path.GetPathRoot(normalizedPath);
        if (string.IsNullOrEmpty(root))
            yield break;

        var parts = normalizedPath[root.Length..]
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        var current = root.TrimEnd(Path.DirectorySeparatorChar);
        foreach (var part in parts)
        {
            current = string.IsNullOrEmpty(current)
                ? $"{Path.DirectorySeparatorChar}{part}"
                : Path.Combine(current, part);

            if (!Directory.Exists(current) && !File.Exists(current))
                yield break;

            var info = File.GetAttributes(current).HasFlag(FileAttributes.Directory)
                ? new DirectoryInfo(current) as FileSystemInfo
                : new FileInfo(current);

            if (info.LinkTarget is not null)
                yield return current;
        }
    }

    private static bool IsWithinNormalizedPath(string path, string root)
    {
        var normalizedPath = PathNormalizer.NormalizeForComparison(path);
        var normalizedRoot = PathNormalizer.NormalizeForComparison(root);

        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar))
            normalizedRoot += Path.DirectorySeparatorChar;

        return normalizedPath.StartsWith(normalizedRoot, StringComparison.Ordinal) ||
               normalizedPath == normalizedRoot.TrimEnd(Path.DirectorySeparatorChar);
    }

    private static string NormalizeForMountPlanning(string path)
    {
        var normalized = PathNormalizer.Normalize(path, resolveSymlinks: false);

        if (Directory.Exists(normalized) || File.Exists(normalized))
            return PathNormalizer.Normalize(normalized, resolveSymlinks: true);

        var root = Path.GetPathRoot(normalized);
        if (string.IsNullOrEmpty(root))
            return normalized;

        var parts = normalized[root.Length..]
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var existing = root.TrimEnd(Path.DirectorySeparatorChar);
        var missingIndex = 0;

        for (; missingIndex < parts.Length; missingIndex++)
        {
            var candidate = string.IsNullOrEmpty(existing)
                ? $"{Path.DirectorySeparatorChar}{parts[missingIndex]}"
                : Path.Combine(existing, parts[missingIndex]);

            if (!Directory.Exists(candidate) && !File.Exists(candidate))
                break;

            existing = candidate;
        }

        if (missingIndex == 0)
            return normalized;

        var resolvedExisting = PathNormalizer.Normalize(existing, resolveSymlinks: true);
        var remaining = parts.Skip(missingIndex).ToArray();
        return remaining.Length == 0
            ? resolvedExisting
            : Path.Combine([resolvedExisting, .. remaining]);
    }

    private static string CreateEmptyDirectorySource()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"hpd-bwrap-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CleanupUnusedPath(string? path)
    {
        if (path is null)
            return;

        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: false);
        }
        catch
        {
            // Best-effort cleanup for duplicate planner outputs.
        }
    }

    private sealed record PlannedMount(BubblewrapMount Mount, string? CleanupPath);
}
