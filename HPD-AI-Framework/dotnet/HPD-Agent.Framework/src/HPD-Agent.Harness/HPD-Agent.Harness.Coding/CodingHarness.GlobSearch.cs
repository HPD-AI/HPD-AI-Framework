using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml;
using HPD.Agent.Middleware;
using HPDOS.Harneses.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileSystemGlobbing;

public partial class CodingHarness
{
    private const int DefaultMatchLimit = 200;
    private const int MaxMatchLimit = 1000;
    private const int GlobTraversalTimeoutMilliseconds = 10_000;
    private const int MaxGlobTraversalEntries = 50_000;

    private static readonly TimeSpan RecentMatchWindow = TimeSpan.FromDays(1);

    /// <summary>
    /// Finds files or directories whose paths match a glob pattern as a bounded XML fragment.
    /// </summary>
    [AIFunction]
    [RequiresPermission]
    [Description("Finds files or directories whose paths match a glob pattern. Use this when you know a filename or path shape such as **/*.cs, src/**/*.json, or **/*Tests.cs. Use ListDirectory to inspect a known folder, Grep to search file contents, and ReadFile to read a specific file. When exploring and several filename shapes are plausible, run multiple useful GlobSearch calls in parallel rather than serially guessing one pattern at a time.")]
    public async Task<string> GlobSearch(
        [Description("The glob pattern to match. It may be relative to path or absolute. Bare filenames search recursively.")] string pattern,
        [Description("The search root. Relative paths are resolved from the current working directory.")] string path = ".",
        [Description("The 1-based match number to start returning after filtering and sorting.")] int offset = 1,
        [Description("The maximum number of matches to return. Maximum: 1000.")] int limit = DefaultMatchLimit,
        [Description("Whether glob matching should be case-sensitive.")] bool caseSensitive = false,
        [Description("Whether to include hidden files and directories.")] bool includeHidden = false,
        [Description("Whether to respect ignore files such as .gitignore.")] bool respectIgnoreFiles = true,
        [Description("Filters matches by kind.")] GlobEntryKindFilter kind = GlobEntryKindFilter.Files,
        [Description("Controls match ordering.")] GlobSortBy sortBy = GlobSortBy.Path,
        [Description("Controls ascending or descending sort order.")] SortDirection sortDirection = SortDirection.Ascending)
    {
        try
        {
            var argumentError = ValidateGlobSearchArguments(pattern, path, offset, limit, kind, sortBy, sortDirection);
            if (argumentError != null)
                return FormatGlobSearchError(path ?? string.Empty, argumentError);

            var resolved = await TryResolveGlobSearchWithHostAsync(path, pattern, CancellationToken.None).ConfigureAwait(false)
                ?? ResolveGlobSearch(path, pattern);

            if (IsBlockedSearchPath(resolved.InputPath) ||
                IsBlockedSearchPath(resolved.OriginalPattern) ||
                IsBlockedSearchPath(resolved.EffectiveFullPath))
            {
                return FormatGlobSearchError(resolved.EffectiveFullPath, "Cannot search blocked system path.");
            }

            if (IsBroadRootGlobSearch(resolved.EffectiveFullPath, resolved.EffectivePattern))
                return FormatGlobSearchError(resolved.EffectiveFullPath, "Pattern is too broad. Use a more specific path or pattern.");

            if (File.Exists(resolved.EffectiveFullPath))
                return FormatGlobSearchError(resolved.EffectiveFullPath, "Path is a file. Use ReadFile instead.");

            if (!Directory.Exists(resolved.EffectiveFullPath))
                return FormatGlobSearchError(resolved.EffectiveFullPath, BuildMissingDirectoryMessage(resolved.EffectiveFullPath));

            var request = new GlobSearchRequest
            {
                FullPath = resolved.FullPath,
                EffectiveFullPath = resolved.EffectiveFullPath,
                OriginalPattern = resolved.OriginalPattern,
                EffectivePattern = resolved.EffectivePattern,
                LiteralFullPath = resolved.LiteralFullPath,
                Offset = offset,
                Limit = limit,
                CaseSensitive = caseSensitive,
                IncludeHidden = includeHidden,
                RespectIgnoreFiles = respectIgnoreFiles,
                Kind = kind,
                SortBy = sortBy,
                SortDirection = sortDirection
            };

            var result = await ExecuteGlobSearchAsync(request, CancellationToken.None).ConfigureAwait(false);
            return FormatGlobSearchResult(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return FormatGlobSearchError(path ?? string.Empty, $"Unable to search files: {ex.Message}");
        }
        catch (IOException ex)
        {
            return FormatGlobSearchError(path ?? string.Empty, $"Unable to search files: {ex.Message}");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return FormatGlobSearchError(path ?? string.Empty, $"Unable to search files: {ex.Message}");
        }
    }

    private async Task<ResolvedGlobSearch?> TryResolveGlobSearchWithHostAsync(
        string path,
        string pattern,
        CancellationToken cancellationToken)
    {
        foreach (var resolver in _globSearchPathResolvers)
        {
            var result = await resolver.TryResolveAsync(path, pattern, cancellationToken).ConfigureAwait(false);
            if (result != null)
                return result;
        }

        return null;
    }

    private static string? ValidateGlobSearchArguments(
        string? pattern,
        string? path,
        int offset,
        int limit,
        GlobEntryKindFilter kind,
        GlobSortBy sortBy,
        SortDirection sortDirection)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return "Pattern is required.";
        if (string.IsNullOrWhiteSpace(path))
            return "Path is required.";
        if (offset < 1)
            return "Offset must be greater than or equal to 1.";
        if (limit < 1 || limit > MaxMatchLimit)
            return $"Limit must be between 1 and {MaxMatchLimit.ToString(CultureInfo.InvariantCulture)}.";
        if (!Enum.IsDefined(kind))
            return "Kind must be a valid GlobEntryKindFilter value.";
        if (!Enum.IsDefined(sortBy))
            return "SortBy must be a valid GlobSortBy value.";
        if (!Enum.IsDefined(sortDirection))
            return "SortDirection must be a valid SortDirection value.";

        return null;
    }

    private static ResolvedGlobSearch ResolveGlobSearch(string path, string pattern)
    {
        var trimmedPath = path.Trim();
        var trimmedPattern = NormalizePatternSeparators(pattern.Trim());
        var fullPath = Path.GetFullPath(trimmedPath, Directory.GetCurrentDirectory());
        var originalPattern = trimmedPattern;

        if (IsTrailingDirectoryPattern(trimmedPattern))
        {
            return new ResolvedGlobSearch(
                trimmedPath,
                originalPattern,
                fullPath,
                fullPath,
                NormalizeModelFriendlyPattern(trimmedPattern));
        }

        var normalizedPattern = NormalizeModelFriendlyPattern(trimmedPattern);
        var literalFullPath = TryGetLiteralFullPath(fullPath, normalizedPattern);

        if (Path.IsPathFullyQualified(normalizedPattern))
            return ExtractStaticBaseDirectory(trimmedPath, originalPattern, fullPath, normalizedPattern, literalFullPath);

        return ExtractStaticBaseDirectory(trimmedPath, originalPattern, fullPath, normalizedPattern, literalFullPath);
    }

    private static string NormalizeModelFriendlyPattern(string pattern)
    {
        var normalized = NormalizePatternSeparators(pattern.Trim());
        if (IsTrailingDirectoryPattern(normalized))
            return normalized.TrimEnd('/', '\\') + "/**";

        if (!Path.IsPathFullyQualified(normalized) &&
            !ContainsGlobSpecialCharacter(normalized) &&
            !normalized.Contains('/', StringComparison.Ordinal))
        {
            return "**/" + normalized;
        }

        return normalized;
    }

    private static ResolvedGlobSearch ExtractStaticBaseDirectory(
        string inputPath,
        string originalPattern,
        string fullPath,
        string pattern,
        string? literalFullPath)
    {
        if (literalFullPath != null)
        {
            var literalDirectory = Path.GetDirectoryName(literalFullPath) ?? fullPath;
            var literalFileName = Path.GetFileName(literalFullPath);
            return new ResolvedGlobSearch(inputPath, originalPattern, fullPath, literalDirectory, literalFileName, literalFullPath);
        }

        var firstGlobIndex = IndexOfFirstGlobSpecialCharacter(pattern);
        if (firstGlobIndex < 0)
        {
            var combined = Path.IsPathFullyQualified(pattern)
                ? pattern
                : Path.GetFullPath(pattern, fullPath);
            var literalEffectivePath = Path.GetDirectoryName(combined) ?? fullPath;
            var literalEffectivePattern = Path.GetFileName(combined);
            return new ResolvedGlobSearch(inputPath, originalPattern, fullPath, literalEffectivePath, literalEffectivePattern);
        }

        var separatorIndex = pattern.LastIndexOf('/', Math.Max(0, firstGlobIndex - 1));
        if (separatorIndex < 0)
            return new ResolvedGlobSearch(inputPath, originalPattern, fullPath, fullPath, pattern);

        var staticPrefix = pattern[..separatorIndex];
        var effectivePattern = pattern[(separatorIndex + 1)..];
        if (string.IsNullOrEmpty(effectivePattern))
            effectivePattern = "**";

        var effectivePath = Path.IsPathFullyQualified(staticPrefix)
            ? Path.GetFullPath(staticPrefix)
            : Path.GetFullPath(staticPrefix, fullPath);

        return new ResolvedGlobSearch(inputPath, originalPattern, fullPath, effectivePath, effectivePattern);
    }

    private static string? TryGetLiteralFullPath(string fullPath, string pattern)
    {
        if (!ContainsGlobSpecialCharacter(pattern))
            return null;

        var combined = Path.IsPathFullyQualified(pattern)
            ? pattern
            : Path.GetFullPath(pattern, fullPath);

        return File.Exists(combined) || Directory.Exists(combined)
            ? combined
            : null;
    }

    private async Task<GlobSearchResult> ExecuteGlobSearchAsync(
        GlobSearchRequest request,
        CancellationToken cancellationToken)
    {
        var ignoreRoot = IsUnderPath(request.EffectiveFullPath, request.FullPath)
            ? request.FullPath
            : request.EffectiveFullPath;
        var ignoreMatcher = request.RespectIgnoreFiles ? CreateIgnoreMatcher(ignoreRoot) : null;
        var state = new LocalGlobSearchState(ignoreMatcher);
        var stopwatch = Stopwatch.StartNew();

        var candidates = request.LiteralFullPath != null
            ? EnumerateLiteralGlobCandidate(request, state)
            : await EnumerateCandidatePathsAsync(request, state, stopwatch, cancellationToken).ConfigureAwait(false);

        var matches = request.LiteralFullPath != null
            ? candidates.Where(candidate => GlobMatchMatchesKind(candidate, request.Kind)).ToArray()
            : MatchCandidates(candidates, request);

        var sorted = SortGlobMatches(matches, request.SortBy, request.SortDirection);
        var totalMatches = state.TraversalTimedOut ? "unknown" : sorted.Count.ToString(CultureInfo.InvariantCulture);
        var page = sorted.Skip(request.Offset - 1).Take(request.Limit).ToArray();
        var hasMore = request.Offset - 1 + page.Length < sorted.Count;
        var truncated = state.TraversalStopped || hasMore;
        var truncationReason = state.TruncationReason ?? (hasMore ? "limit" : null);

        return new GlobSearchResult
        {
            Path = request.FullPath,
            EffectivePath = request.EffectiveFullPath,
            OriginalPattern = request.OriginalPattern,
            EffectivePattern = request.EffectivePattern,
            Matches = page,
            Offset = request.Offset,
            Limit = request.Limit,
            CaseSensitive = request.CaseSensitive,
            IncludeHidden = request.IncludeHidden,
            RespectIgnoreFiles = request.RespectIgnoreFiles,
            Kind = request.Kind,
            SortBy = request.SortBy,
            SortDirection = request.SortDirection,
            TotalMatches = totalMatches,
            IgnoredCount = state.IgnoredCount,
            Truncated = truncated,
            TruncationReason = truncationReason,
            NextOffset = hasMore ? request.Offset + page.Length : null
        };
    }

    private static IReadOnlyList<GlobMatchInfo> EnumerateLiteralGlobCandidate(
        GlobSearchRequest request,
        LocalGlobSearchState state)
    {
        if (request.LiteralFullPath == null ||
            !TryCreateGlobMatchInfo(request.LiteralFullPath, request.EffectiveFullPath, out var candidate))
        {
            return [];
        }

        return ShouldIncludeGlobCandidate(candidate, request, state) ? [candidate] : [];
    }

    private async Task<IReadOnlyList<GlobMatchInfo>> EnumerateCandidatePathsAsync(
        GlobSearchRequest request,
        LocalGlobSearchState state,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var matches = new List<GlobMatchInfo>();
        var queue = new Queue<string>();
        queue.Enqueue(request.EffectiveFullPath);

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (stopwatch.ElapsedMilliseconds > _globSearchOptions.TraversalTimeoutMilliseconds)
            {
                state.Stop("timeout");
                break;
            }

            var directory = queue.Dequeue();
            foreach (var fullPath in EnumerateFileSystemEntries(directory, throwOnFailure: directory == request.EffectiveFullPath))
            {
                if (matches.Count >= _globSearchOptions.MaxTraversalEntries)
                {
                    state.Stop("traversal_cap");
                    return matches;
                }

                if (!TryCreateGlobMatchInfo(fullPath, request.EffectiveFullPath, out var candidate))
                    continue;

                if (!ShouldIncludeGlobCandidate(candidate, request, state))
                    continue;

                matches.Add(candidate);

                if (candidate.Kind == GlobEntryKind.Directory && !candidate.IsSymlink)
                    queue.Enqueue(fullPath);
            }

            await Task.Yield();
        }

        return matches;
    }

    private static bool TryCreateGlobMatchInfo(string fullPath, string rootPath, out GlobMatchInfo entry)
    {
        entry = default!;

        try
        {
            var attributes = File.GetAttributes(fullPath);
            var isDirectory = attributes.HasFlag(FileAttributes.Directory);
            var isSymlink = attributes.HasFlag(FileAttributes.ReparsePoint);
            var kind = isDirectory ? GlobEntryKind.Directory : GlobEntryKind.File;
            var relativePath = Path.GetRelativePath(rootPath, fullPath).Replace(Path.DirectorySeparatorChar, '/');
            if (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar)
                relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, '/');

            long? size = null;
            if (!isDirectory)
                size = new FileInfo(fullPath).Length;

            entry = new GlobMatchInfo
            {
                RelativePath = EnsureGlobDirectorySuffix(relativePath, kind),
                Kind = kind,
                Size = size,
                LastWriteTimeUtc = File.GetLastWriteTimeUtc(fullPath),
                IsSymlink = isSymlink
            };

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ShouldIncludeGlobCandidate(
        GlobMatchInfo candidate,
        GlobSearchRequest request,
        LocalGlobSearchState state)
    {
        if (!request.IncludeHidden && IsHiddenPath(candidate.RelativePath))
        {
            state.IgnoredCount++;
            return false;
        }

        var normalizedName = candidate.RelativePath.TrimEnd('/');
        var leafName = Path.GetFileName(normalizedName);
        if (candidate.Kind == GlobEntryKind.Directory && BuiltInRecursiveSkips.Contains(leafName))
        {
            state.IgnoredCount++;
            return false;
        }

        if (request.RespectIgnoreFiles && state.IsIgnored(candidate))
        {
            state.IgnoredCount++;
            return false;
        }

        return true;
    }

    private static IReadOnlyList<GlobMatchInfo> MatchCandidates(
        IReadOnlyList<GlobMatchInfo> candidates,
        GlobSearchRequest request)
    {
        var matcher = new Microsoft.Extensions.FileSystemGlobbing.Matcher(
            request.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        foreach (var pattern in ExpandBracePatterns(NormalizeMatcherPattern(request.EffectivePattern)))
            matcher.AddInclude(pattern);

        return candidates
            .Where(candidate => GlobMatchMatchesKind(candidate, request.Kind))
            .Where(candidate => CandidateMatchesPattern(candidate, matcher, request.EffectivePattern))
            .ToArray();
    }

    private static bool CandidateMatchesPattern(
        GlobMatchInfo candidate,
        Microsoft.Extensions.FileSystemGlobbing.Matcher matcher,
        string pattern)
    {
        var relativePath = candidate.RelativePath.TrimEnd('/');
        if (matcher.Match([relativePath]).HasMatches ||
            matcher.Match([candidate.RelativePath]).HasMatches)
        {
            return true;
        }

        if (candidate.Kind != GlobEntryKind.Directory || !pattern.EndsWith("/**", StringComparison.Ordinal))
            return false;

        var treeRoot = pattern[..^3].TrimEnd('/');
        return GetPathComparer().Equals(relativePath, treeRoot);
    }

    private static bool GlobMatchMatchesKind(GlobMatchInfo candidate, GlobEntryKindFilter kind)
        => kind switch
        {
            GlobEntryKindFilter.Files => candidate.Kind == GlobEntryKind.File,
            GlobEntryKindFilter.Directories => candidate.Kind == GlobEntryKind.Directory,
            GlobEntryKindFilter.All => true,
            _ => false
        };

    private static IReadOnlyList<GlobMatchInfo> SortGlobMatches(
        IReadOnlyList<GlobMatchInfo> matches,
        GlobSortBy sortBy,
        SortDirection sortDirection)
    {
        var now = DateTimeOffset.UtcNow;

        IOrderedEnumerable<GlobMatchInfo> ordered = sortBy switch
        {
            GlobSortBy.ModifiedTime => sortDirection == SortDirection.Descending
                ? matches.OrderByDescending(match => match.LastWriteTimeUtc)
                : matches.OrderBy(match => match.LastWriteTimeUtc),
            GlobSortBy.Recency => SortByRecency(matches, now, sortDirection),
            GlobSortBy.Size => sortDirection == SortDirection.Descending
                ? matches.OrderByDescending(match => match.Size ?? 0)
                : matches.OrderBy(match => match.Size ?? 0),
            GlobSortBy.Kind => sortDirection == SortDirection.Descending
                ? matches.OrderByDescending(match => GlobKindSortRank(match.Kind))
                : matches.OrderBy(match => GlobKindSortRank(match.Kind)),
            _ => sortDirection == SortDirection.Descending
                ? matches.OrderByDescending(match => match.RelativePath, GetPathComparer())
                : matches.OrderBy(match => match.RelativePath, GetPathComparer())
        };

        if (sortBy != GlobSortBy.Path && sortBy != GlobSortBy.Recency)
            ordered = ordered.ThenBy(match => match.RelativePath, GetPathComparer());

        return ordered.ToArray();
    }

    private static IOrderedEnumerable<GlobMatchInfo> SortByRecency(
        IReadOnlyList<GlobMatchInfo> matches,
        DateTimeOffset now,
        SortDirection sortDirection)
    {
        var recentCutoff = now - RecentMatchWindow;
        return sortDirection == SortDirection.Descending
            ? matches
                .OrderByDescending(match => match.LastWriteTimeUtc >= recentCutoff ? 0 : 1)
                .ThenBy(match => match.LastWriteTimeUtc >= recentCutoff ? match.LastWriteTimeUtc : DateTimeOffset.MaxValue)
                .ThenBy(match => match.RelativePath, GetPathComparer())
            : matches
                .OrderBy(match => match.LastWriteTimeUtc >= recentCutoff ? 0 : 1)
                .ThenByDescending(match => match.LastWriteTimeUtc >= recentCutoff ? match.LastWriteTimeUtc : DateTimeOffset.MinValue)
                .ThenBy(match => match.RelativePath, GetPathComparer());
    }

    private static int GlobKindSortRank(GlobEntryKind kind)
        => kind switch
        {
            GlobEntryKind.Directory => 0,
            GlobEntryKind.File => 1,
            _ => 2
        };

    private static bool IsBlockedSearchPath(string fullPath)
    {
        var normalized = fullPath.Replace('\\', '/').TrimEnd('/');
        if (normalized.StartsWith("//", StringComparison.Ordinal))
            return true;

        if (normalized is "/dev" or "/proc")
            return true;

        return normalized.StartsWith("/dev/", StringComparison.Ordinal) ||
               normalized.StartsWith("/proc/", StringComparison.Ordinal);
    }

    private static bool IsBroadRootGlobSearch(string effectiveFullPath, string pattern)
    {
        var root = Path.GetPathRoot(effectiveFullPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedEffectivePath = effectiveFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var isRoot = string.IsNullOrEmpty(root)
            ? normalizedEffectivePath.Length == 0
            : GetPathComparer().Equals(normalizedEffectivePath, root);

        return isRoot && (pattern.Contains("**", StringComparison.Ordinal) || pattern.Contains('*', StringComparison.Ordinal));
    }

    private static bool IsUnderPath(string childPath, string parentPath)
    {
        var relative = Path.GetRelativePath(parentPath, childPath);
        return relative == "." ||
               (!relative.StartsWith("..", StringComparison.Ordinal) &&
                !Path.IsPathRooted(relative));
    }

    private static bool IsTrailingDirectoryPattern(string pattern)
        => pattern.EndsWith("/", StringComparison.Ordinal) ||
           pattern.EndsWith("\\", StringComparison.Ordinal);

    private static bool ContainsGlobSpecialCharacter(string value)
        => IndexOfFirstGlobSpecialCharacter(value) >= 0;

    private static int IndexOfFirstGlobSpecialCharacter(string value)
    {
        var index = -1;
        foreach (var ch in new[] { '*', '?', '[', '{' })
        {
            var candidate = value.IndexOf(ch, StringComparison.Ordinal);
            if (candidate >= 0 && (index < 0 || candidate < index))
                index = candidate;
        }

        return index;
    }

    private static string NormalizeMatcherPattern(string pattern)
    {
        var normalized = NormalizePatternSeparators(pattern);
        return normalized.StartsWith("./", StringComparison.Ordinal) ? normalized[2..] : normalized;
    }

    private static IReadOnlyList<string> ExpandBracePatterns(string pattern)
    {
        var open = pattern.IndexOf('{', StringComparison.Ordinal);
        if (open < 0)
            return [pattern];

        var close = pattern.IndexOf('}', open + 1);
        if (close < 0)
            return [pattern];

        var prefix = pattern[..open];
        var suffix = pattern[(close + 1)..];
        var alternatives = pattern[(open + 1)..close]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (alternatives.Length == 0)
            return [pattern];

        return alternatives
            .Select(alternative => prefix + alternative + suffix)
            .ToArray();
    }

    private static string EnsureGlobDirectorySuffix(string relativePath, GlobEntryKind kind)
        => kind == GlobEntryKind.Directory && !relativePath.EndsWith("/", StringComparison.Ordinal)
            ? relativePath + "/"
            : relativePath;

    private static string FormatGlobSearchResult(GlobSearchResult result)
    {
        var builder = new StringBuilder();
        using var writer = CreateCodingHarnessXmlWriter(builder);

        writer.WriteStartElement("glob");
        writer.WriteAttributeString("path", result.Path);
        writer.WriteAttributeString("effective_path", result.EffectivePath);
        writer.WriteAttributeString("pattern", result.EffectivePattern);
        writer.WriteAttributeString("original_pattern", result.OriginalPattern);
        writer.WriteAttributeString("offset", result.Offset.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("matches_read", result.Matches.Count.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("total_matches", result.TotalMatches);
        writer.WriteAttributeString("ignored_count", result.IgnoredCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("truncated", FormatBool(result.Truncated));
        if (!string.IsNullOrEmpty(result.TruncationReason))
            writer.WriteAttributeString("truncation_reason", result.TruncationReason);
        writer.WriteAttributeString("case_sensitive", FormatBool(result.CaseSensitive));
        writer.WriteAttributeString("include_hidden", FormatBool(result.IncludeHidden));
        writer.WriteAttributeString("respect_ignore_files", FormatBool(result.RespectIgnoreFiles));
        writer.WriteAttributeString("kind", FormatEnum(result.Kind));
        writer.WriteAttributeString("sort_by", FormatEnum(result.SortBy));
        writer.WriteAttributeString("sort_direction", FormatEnum(result.SortDirection));

        if (result.Matches.Count == 0)
        {
            writer.WriteStartElement("no_matches");
            writer.WriteEndElement();
        }
        else
        {
            foreach (var match in result.Matches)
                WriteGlobMatch(writer, match);
        }

        if (result.NextOffset.HasValue)
        {
            writer.WriteStartElement("next_glob");
            writer.WriteAttributeString("offset", result.NextOffset.Value.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("limit", result.Limit.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("reason", result.TruncationReason == "timeout" ? "partial_results_timeout" : "more_matches_available");
            writer.WriteEndElement();
        }

        if (result.Truncated)
        {
            writer.WriteStartElement("truncation_hint");
            writer.WriteString(result.TruncationReason == "timeout"
                ? "Traversal timed out. Use a more specific path or pattern."
                : "Use a more specific path or pattern.");
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.Flush();
        return builder.ToString();
    }

    private static void WriteGlobMatch(XmlWriter writer, GlobMatchInfo match)
    {
        writer.WriteStartElement("match");
        writer.WriteAttributeString("kind", FormatEnum(match.Kind));
        writer.WriteAttributeString("path", match.RelativePath);
        writer.WriteEndElement();
    }

    private static string FormatGlobSearchError(string path, string message)
    {
        var builder = new StringBuilder();
        using var writer = CreateCodingHarnessXmlWriter(builder);

        writer.WriteStartElement("error");
        writer.WriteAttributeString("tool", "GlobSearch");
        if (!string.IsNullOrEmpty(path))
            writer.WriteAttributeString("path", path);
        writer.WriteString(message);
        writer.WriteEndElement();
        writer.Flush();
        return builder.ToString();
    }

    private sealed class LocalGlobSearchState(HpdIgnoreMatcher? ignoreMatcher)
    {
        private readonly HpdIgnoreMatcher? _ignoreMatcher = ignoreMatcher;

        public int IgnoredCount { get; set; }

        public bool TraversalStopped { get; private set; }

        public bool TraversalTimedOut => TruncationReason == "timeout";

        public string? TruncationReason { get; private set; }

        public void Stop(string reason)
        {
            TraversalStopped = true;
            TruncationReason = reason;
        }

        public bool IsIgnored(GlobMatchInfo entry)
        {
            if (_ignoreMatcher == null)
                return false;

            var relativePath = entry.RelativePath.TrimEnd('/');
            return _ignoreMatcher.IsIgnored(
                relativePath,
                entry.Kind == GlobEntryKind.Directory);
        }
    }

}

namespace HPDOS.Harneses.Middleware
{
/// <summary>
/// Allows hosts to resolve workspace-facing glob paths before local filesystem fallback.
/// </summary>
public interface IGlobSearchPathResolver
{
    /// <summary>
    /// Returns a resolved glob search, or null when the resolver does not own the path/pattern.
    /// </summary>
    ValueTask<ResolvedGlobSearch?> TryResolveAsync(
        string path,
        string pattern,
        CancellationToken cancellationToken);
}

/// <summary>
/// Tunable filesystem traversal limits for GlobSearch.
/// </summary>
public sealed record GlobSearchOptions
{
    /// <summary>
    /// Default production traversal limits.
    /// </summary>
    public static GlobSearchOptions Default { get; } = new();

    /// <summary>
    /// Maximum number of filesystem entries to collect before returning partial output.
    /// </summary>
    public int MaxTraversalEntries { get; init; } = 50_000;

    /// <summary>
    /// Maximum traversal duration in milliseconds before returning partial output.
    /// </summary>
    public int TraversalTimeoutMilliseconds { get; init; } = 10_000;
}

public sealed record ResolvedGlobSearch(
    string InputPath,
    string OriginalPattern,
    string FullPath,
    string EffectiveFullPath,
    string EffectivePattern,
    string? LiteralFullPath = null);

internal sealed record GlobSearchRequest
{
    public required string FullPath { get; init; }

    public required string EffectiveFullPath { get; init; }

    public required string OriginalPattern { get; init; }

    public required string EffectivePattern { get; init; }

    public string? LiteralFullPath { get; init; }

    public required int Offset { get; init; }

    public required int Limit { get; init; }

    public required bool CaseSensitive { get; init; }

    public required bool IncludeHidden { get; init; }

    public required bool RespectIgnoreFiles { get; init; }

    public required GlobEntryKindFilter Kind { get; init; }

    public required GlobSortBy SortBy { get; init; }

    public required SortDirection SortDirection { get; init; }
}

internal sealed record GlobMatchInfo
{
    public required string RelativePath { get; init; }

    public required GlobEntryKind Kind { get; init; }

    public long? Size { get; init; }

    public DateTimeOffset? LastWriteTimeUtc { get; init; }

    public bool IsSymlink { get; init; }
}

internal enum GlobEntryKind
{
    Directory,
    File,
    Other
}

internal sealed record GlobSearchResult
{
    public required string Path { get; init; }

    public required string EffectivePath { get; init; }

    public required string OriginalPattern { get; init; }

    public required string EffectivePattern { get; init; }

    public required IReadOnlyList<GlobMatchInfo> Matches { get; init; }

    public required int Offset { get; init; }

    public required int Limit { get; init; }

    public required bool CaseSensitive { get; init; }

    public required bool IncludeHidden { get; init; }

    public required bool RespectIgnoreFiles { get; init; }

    public required GlobEntryKindFilter Kind { get; init; }

    public required GlobSortBy SortBy { get; init; }

    public required SortDirection SortDirection { get; init; }

    public required string TotalMatches { get; init; }

    public required int IgnoredCount { get; init; }

    public required bool Truncated { get; init; }

    public string? TruncationReason { get; init; }

    public int? NextOffset { get; init; }
}
}

public enum GlobEntryKindFilter
{
    Files,
    Directories,
    All
}

public enum GlobSortBy
{
    Path,
    ModifiedTime,
    Recency,
    Size,
    Kind
}
